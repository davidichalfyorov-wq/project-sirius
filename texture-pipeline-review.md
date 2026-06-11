# Project Sirius — пайплайн текстур и hi-res ревью

Дата: 2026-06-10. Просмотрено: данные Discovery FL 4.86.0 (DATA, EXE, JFP), исходники `src/` (форк LibreLancer, ветка `sirius-discovery-hotfix`), оба рендер-бэкенда.

## 1. Состав проекта

| Часть | Что это |
|---|---|
| `Discovery Freelancer 4.86.0/DATA` | Игровые данные: UTF-контейнеры (.txm/.mat/.cmp/.3db/.sph), текстовые и BINI ini |
| `Discovery Freelancer 4.86.0/EXE` | Легаси-клиент (Freelancer.exe, D3D8) + ENB (d3d8.dll→d3d9.dll), FLHook, dacom.ini |
| `src/` | Форк LibreLancer, net10.0. Клиент `lancer`, редакторы LancerEdit/InterfaceEdit, `lleditscript` (батч-скрипты), LLShaderCompiler (SPIR-V) |
| Бэкенды | `LibreLancer.Base/Graphics/Backends/{OpenGL,Vulkan}`, выбор через env `SIRIUS_RENDERER=gl|vulkan` (Phase 0 freeze, golden-тесты) |

## 2. Форматы хранения текстур

Всё лежит в UTF-контейнерах (magic `UTF `): `.txm` — библиотеки текстур, `.mat` — материалы, модели (`.cmp/.3db/.sph`) могут содержать встроенные `Texture library`/`Material library`.

Структура `.txm`: `Texture library → <имя текстуры> → ноды`:
- `MIPS` — целый DDS-файл как есть (с мип-цепочкой). Проверено: `icegrey.txm` = DDS DXT1.
- `MIP0..MIP9` — несжатый путь: отдельный TGA на каждый мип (поддержаны 24/32-бит и индексированные). Проверено: `animated.txm`.
- `MIPU`, `cube` — варианты (DDS, кубомапа).
- Анимация: `Texture count / Frame count / FPS / Frame rects` — атласы (sparks, lightning).

Структура `.mat`: `Material library → <имя материала> → Type` (`DcDt`, `DcDtOcOt`, `DetailMapMaterial`, `AtmosphereMaterial`, `NomadMaterialNoBendy`, `HighGlassMaterial`…) + `Dt_name/Dm_name/Et_name` — **ссылка на текстуру по имени**, плюс `Dc/Oc/Ac`, `flip u/v`, `TileRate`. Проверено: `planet_ice_grey.mat`.

Подключение библиотек — через ini: `material_library =` (shiparch/solararch/equipment), `textures = fx\*.txm` (ALE-эффекты), `[Texture] file =` (интерфейс; часть ini — компилированный BINI, напр. `hud_txm.ini`).

## 3. Загрузка в LibreLancer (runtime)

`GameResourceManager` (`src/LibreLancer/Resources/GameResourceManager.cs`):
- Словари текстур **case-insensitive**, имя текстуры **глобально на всю игру**; дубликат в библиотеке → ошибка в лог, нода отбрасывается.
- Ленивая загрузка: `FindTexture(name)` → если текстура выгружена, перезагружается весь файл-источник (`texturefiles` map). Материалы ищутся по CRC-id.
- `TextureData.Initialize` создаёт GPU-текстуру при первом использовании: `MIPS` → `ImageLib.DDS.FromStream`, `MIP0+` → TGA по уровням.

`ImageLib/DDS.cs`: принимает DXT1/3/5, ATI1N/ATI2N (=RGTC1/2 = BC4/5), 16-бит (565/5551/4444), 24/32-бит BGRA8. **DDS с DX10-заголовком отклоняется → BC7/BC6 недоступны.** Объёмные текстуры — нет. Грузятся ровно те мипы, что есть в файле.

## 4. Легаси-клиент (Freelancer.exe)

- `dacom.ini`: компоненты `TextureLibrary`, `DirectX8` (RP8.dll), `MaterialMap` (маппинг имён материалов на шейдерные классы — учитывай при неймингах: `alpha_mask*`, `detailmap_*`, `*_glass`, `nomad*`…), опция `TEXTURE_ALLOW_DXT`.
- `flconfigdatabase.txt`: пер-GPU флаги (`LimitTextureSize` 256², `ForceSquareTextures`, `FL_BAD_DXTN`, `Bad8888`). Современные/виртуальные адаптеры (DXVK) не матчатся → дефолты, флаги не действуют.
- Texture LOD режется через `perfoptions.ini` в профиле пользователя (перекрывает `TEXTURE_LOD_LOAD_MIN`) — для hi-res ставить максимальный texture detail.
- Графическая обвязка: `d3d8.dll` (ENB convertor) → `d3d9.dll` (ENBSeries: bloom, форс 16× aniso). Под Wine это **нативные** dll → нужны overrides `d3d8,d3d9=native,builtin`; альтернатива — убрать ENB и пустить через нативный d3d8 в DXVK.

## 5. Vulkan-миграция: текущее состояние

`VKRenderContext` — «сырой» Vulkan 1.3 + synchronization2, roadmap phase 3:
- Готово (3.1/3.2): instance/device/swapchain, текстуры 2D/cube, вершинные/индексные/storage буферы, пайплайн-кэш, сэмплер-кэш (aniso до 16×), RT2D/depth, uniform-ring 8 МБ.
- Не готово: MSAA-таргеты, texture readback/скриншоты («screenshot milestone»), часть phase-3 milestone'ов.
- Один кадр in flight (семантика GL, persistently-mapped писатели) — оптимизация после паритета.
- 16-бит форматы (565/5551/4444) **расширяются в BGRA8 на CPU** при аплоаде (краши драйвера NVIDIA) — старые 16-бит DDS станут x2 по VRAM.

## 6. Критично для hi-res текстур (GL vs VK расхождения)

1. **Полная мип-цепочка обязательна.** `VKTexture2D` всегда создаёт image с полной цепочкой (`LevelCount = log2(max(w,h))+1`) и переводит ВСЕ уровни в SHADER_READ_ONLY; незалитые уровни остаются undefined-мусором. GL-бэкенд клампит `GL_TEXTURE_MAX_LEVEL` по фактически загруженным. Итог: DDS с неполной цепочкой выглядит нормально на GL и артефачит на дистанции в Vulkan. Для 4096² нужно 13 уровней. (Фикс в движке: клампить LevelCount по числу залитых мипов или `maxLod` в сэмплере.)
2. **Бюджет текстурной памяти на VK врёт.** `VKTexture2D.EstimatedTextureMemory = W*H*4` — игнорирует сжатие и мипы; GL считает фактический размер. 4096² DXT1 реально ≈ 11 МБ, посчитается как 67 МБ → preview-рендер LancerEdit (`GameDataContext.MEMORY_BUDGET`) будет преждевременно сбрасывать весь ресурс-менеджер.
3. **TGA-путь ограничен MIP0..MIP9** (10 уровней, лимит и в импортере, и в формате нод) — для ≥2048 цепочка неполная → см. п.1. Hi-res — только через DDS/`MIPS`.
4. **Нет BC7** — лучшие доступные: DXT1 (diffuse), DXT5 (alpha), RGTC2 (нормали), RGTC1 (metallic/roughness). RGTC в легаси-клиенте на старом железе не гарантирован (через DXVK d3d8 — ок); если нужна совместимость с легаси — держись DXT1/3/5.
5. Размеры: power-of-two обязателен (импорт LancerEdit падает с ошибкой), не-квадрат — warning; кратность 4 для DXT-блоков.
6. Аплоад в VK синхронный: на каждый мип staging-буфер + one-shot submit + wait. Огромные либы грузить на загрузочных экранах; в кадре будет фриз (известная пост-паритетная оптимизация).
7. Имена не менять: материалы ссылаются на текстуры по имени, движок резолвит глобально и case-insensitive.

## 7. Рекомендуемый пайплайн авторинга

1. Мастер в PNG (PoT, кратно 4; квадрат предпочтительно).
2. Импорт: LancerEdit → UTF → Import texture. Автовыбор формата: opaque→DXT1, 1-бит альфа→DXT1a, альфа→DXT5; «Normal Map»→RGTC2, Metallic/Roughness→RGTC1. Мипы: Lanczos4, slow=true (crnlib). Батч — `lleditscript`.
3. Round-trip: импортер понимает PNG со встроенным `ddsz`-чанком (zstd-сжатый DDS + SHA256) — можно хранить мастера в PNG с готовым DDS внутри.
4. Результат — нода `MIPS` в `.txm`/`.mat` → в DATA Discovery (или отдельной библиотекой + `material_library=`/`textures=` в ini).
5. Прогон на обоих бэкендах: `SIRIUS_RENDERER=gl` и `=vulkan` (визуальное сравнение, пока readback/goldens для VK не готовы — глазами).
6. Для легаси-клиента: тот же DDS DXT1/5 совместим; выставить texture detail в максимум (perfoptions).

## 8. Чек-лист правок движка перед массовым hi-res

- [ ] `VKTexture2D`: клампить LevelCount/maxLod по фактическим мипам (п.6.1).
- [ ] `VKTexture2D.EstimatedTextureMemory`: учитывать сжатый размер и мипы (п.6.2).
- [ ] (после паритета) батчить staging-аплоады вместо submit-per-mip.
- [ ] Readback milestone — разблокирует golden-тесты VK для сверки текстурного рендера.

## 9. Мелочи, замеченные по ходу

- `DATA/SOLAR/PLANETS/PLANET_ICE_GREY/planet_ice_grey.mat~tmp1` — обломок незавершённого сохранения, можно удалить.
- `DATA/AUDIO/Новая папка/` — дубли ini.
- `JFP/` (JFLP-патч) лежит отдельной папкой, в EXE/DATA не вмержен — его `freelancer.ini`/`dacom.ini` не активны.
