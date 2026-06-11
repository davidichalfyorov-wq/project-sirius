# Linear/Gamma Audit — фазы 1–2 графического роадмапа

Дата: 2026-06-10. Инвентаризация перед Ф1.2 (тонемап) и Ф2.1 (linear lighting
cleanup). Роадмап: 4.4 (HDR pass) и 5.1 (linear cleanup).

## Текущее состояние пайплайна

- **Сцена** рендерится в RGBA16F (`HdrFramePipeline`, SystemRenderer.Draw:316)
  и попадает на экран одним проходом `Tonemap.frag.hlsl` — сейчас pass-through.
- **UI** (Renderer2D и интерфейс) рисуется ПОСЛЕ `hdrPipeline.End()` — уже
  соответствует чеклисту «UI composite after post-processing». Сохранять.
- **Свапчейн/таргеты**: B8G8R8A8Unorm (VK) и обычный RGBA8 (GL) — аппаратного
  sRGB-кодирования НЕТ ни на одном бэкенде; байты идут на монитор как есть.
- **Текстуры**: грузятся UNORM (sRGB-байты без аппаратного декода) — сэмплинг
  возвращает display-space значения. Аппаратные SRGB-форматы не используются.
- **Гамма-математика есть только в PBR.frag.hlsl**: `SRGBtoLinear()` на входе
  (строка 155) и `pow(color, 1/2.2)` на выходе (строка 264). Все остальные
  шейдеры считают и выводят в display-space — ровно как ванильный Freelancer.

## Решение: один явный путь вывода

**Ручной encode только в финальном проходе** (вариант роадмапа «output
pow(1.0/2.2) in final pass only»): GL_FRAMEBUFFER_SRGB/sRGB-свапчейн не
используем — единый код на обоих бэкендах, поведение явное. Ассерт пути — в
debug-overlay (Ф1.6): строка вида `output: manual-srgb`.

## Решение: двухэтапная линеаризация (почему НЕ всё в Ф1.2)

Альфа/аддитивный блендинг в линейном пространстве даёт другой результат, чем
в gamma: непрозрачная геометрия при decode→encode совпадает 1:1, а
полупрозрачные слои (небулы, партиклы, бимы — пол-Freelancer'а) меняются.
Полная линеаризация в Ф1.2 потребовала бы перетюнить прозрачность дважды
(до и после перевода света в Ф2.1). Поэтому:

- **Ф1.2 (тонемап)**: сцена остаётся display-referred в RGBA16F (>1.0 не
  клипуется — fp16). Тонемап-проход = `decode 2.2 → exposure → ACES →
  encode 1/2.2`; при `tonemapper=off, exposure=1` проход тождественен —
  вид FL сохраняется пиксель-в-пиксель, SSIM-гейт остаётся на старых
  базлайнах для off и получает новые для on.
- **Ф1.3 (bloom)**: extract работает по display-referred HDR до тонемапа.
  Порог тюнингуется под это пространство; при переходе на линейный свет
  (Ф2.1) пересмотреть threshold (запись в чеклист Ф2.1).
- **Ф2.1 (linear cleanup)**: одним шагом — все цветовые текстуры декодятся
  в линейное на сэмплинге, весь свет в линейном, materials выводят linear
  HDR, у тонемапа убирается входной decode. Один цикл тюнинга вместе с
  PBR/IBL. Пункт чеклиста Phase 1 «PBR material pass outputs linear HDR»
  формально закрывается здесь же (внутри scope goal).

## Карта шейдеров сцены (что и когда менять)

| Шейдер | Сейчас выводит | Ф1.2 | Ф2.1 (план) |
|---|---|---|---|
| `PBR.frag` | linear внутри, `pow(1/2.2)` на выходе (display) | не трогать | убрать выходной pow → linear HDR; вход уже SRGBtoLinear |
| `Basic.frag` (+`Basic_*` верш.) | display: Dt×свет, Et, Mod2x, ENVMAP×2 | не трогать | decode Dt/Et/Bt на сэмплинге; свет в линейном; Mod2x/×2 пересмотреть с A/B-скринами |
| `DetailMapMaterial` / `DetailMap2Dm1Msk2PassMaterial` / `Masked2DetailMapMaterial` / `IllumDetailMapMaterial` | display (базы/планеты) | не трогать | как Basic |
| `NebulaMaterial` / `NebulaExtPuff` / `NebulaInterior` | display, художественный цвет, блендинг | не трогать | выход linear; интенсивность Dc сохранить подбором (A/B со скринами) |
| `AsteroidBand`, `ZoneVolume`, `Atmosphere` | display, блендинг | не трогать | linear-выход; Atmosphere бленд проверить отдельно (halo планет) |
| `Sprite.frag` (партиклы/бильборды) | display: tex×color | не трогать | emissive-интенсивность сделать явной (×strength), линейный выход |
| `SunSpine` / `SunRadial` | display (солнце) | не трогать | физичная intensity >1, главный источник bloom |
| `Nomad.frag` | display | не трогать | linear-выход |
| `StarsphereCubemap` | display (фон) | не трогать | питает захват IBL (Ф2.4) — захватывать ДО decode не нужно: декод в билдере пробы |
| `Color.frag`, `Navmap`, `PhysicsDebug` | служебные | не трогать | не трогать (debug/линии) |
| `Tonemap.frag` | pass-through | **decode → exposure → ACES/off → encode** | убрать входной decode (сцена уже линейная) |
| 2D/UI (`FSTint`, Renderer2D) | display, после пайплайна | не трогать | не трогать (SDR/sRGB по чеклисту 5.1) |

## Инвентарь `pow`/sRGB в шейдерах (полный греп)

- `PBR.frag.hlsl:155-159` — SRGBtoLinear (вход, остаётся).
- `PBR.frag.hlsl:264` — выходной `pow(1/2.2)` (убрать в Ф2.1).
- Других вхождений `2.2|gamma|srgb` в `src/LibreLancer/Shaders/**` нет.

## Acceptance-инварианты (проверяются в Ф1.2/Ф1.6)

1. Белая текстура (1.0) после тонемапа остаётся белой (ACES(1.0)≈0.8 —
   поэтому White=1.0 калибруется exposure'ом; проверка: off-режим белый 1:1).
2. Emissive >1.0 не клипуется до тонемапа (fp16-буфер, ACES сводит плавно).
3. UI не тонемапится и не двоит гамму (рисуется после End()).
4. Один путь encode: только Tonemap-проход, ассерт в overlay.
