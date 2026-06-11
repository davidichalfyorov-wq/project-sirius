# Промпт для ChatGPT 5.5 Pro — Project Sirius v2

**Кодовое имя: Project Sirius / LibreLancer + Discovery 4.86**

## Общее задание
Сделай полноценный, играбельный мир Freelancer Discovery 4.86 на движке LibreLancer для Linux (Ubuntu 26.04, NVIDIA RTX 5090, OpenGL 4.3, PipeWire + OpenAL). Никаких обрезанных версий — должны работать: полёт в космосе, бой, стыковка со станциями, торговля, диалоги с NPC, озвучка, квесты.

**Репозиторий уже запушен на GitHub:** `https://github.com/davidichalfyorov-wq/project-sirius.git`
**Архив с исходниками:** `project_sirius_for_chatgpt.tar.gz` (136 MB)
**Билд:** `build/v2/lancer.dll`
**Запуск:** `dotnet build/v2/lancer.dll`

## Текущее состояние (что уже работает)
- ✅ Движок компилируется без ошибок на .NET 10.0 / C# 13
- ✅ Загружаются 155 систем, 772 базы, 498 кораблей, 101 фракция, 2289 предметов снаряжения, 2173 товара, 97 faction commodity profiles
- ✅ Игрок спавнится в системе FP7_system
- ✅ Миссия Mission_13 загружается
- ✅ OpenGL 4.3, SDL3, PipeWire + OpenAL — всё инициализируется
- ✅ Иконка приложения, GPU-ускорение, анизотропная фильтрация, MSAA

## Критические проблемы (нужно исправить)

### P0 — Краши интерфейса при нажатии на кнопки (Инвентарь и др.)
```
Object reference not set to an instance of an object.
   at LibreLancer.Interface.UiContext.MouseOnDoubleClick(MouseEventArgs e) in .../UiContext.cs:line 93
   at LibreLancer.Mouse.OnMouseDoubleClick(MouseButtons b) in .../Mouse.cs:line 50
   at LibreLancer.Platforms.SDL3Game.Run(Game loop) in .../SDL3Game.cs:line 555
```
Причина: cimgui отключён → `Game.Debug == null`. Все обращения к `Game.Debug` в интерфейсном коде (UiContext.cs, SpaceGameplay.cs и др.) должны быть с null-проверками. Исправь **все** места, где `game.Debug` или `Game.Debug` вызывается без проверки на null.

### P0 — Отсутствуют текстовые строки (1271 ID не найдено)
```
Strings: Not Found: 1271
Strings: Not Found: 1268, 1269, 1270, 1272, 1273...
Strings: Not Found: 501028, 505217, 505219, 261039...
```
Причина: тексты хранятся в Windows DLL (Discovery.dll, DsyAddition.dll, resources.dll), которые недоступны на Linux. Нужно:
1. Извлечь строковые ресурсы из этих DLL (любым способом — wine, .NET reflection, или распарсить PE-формат)
2. Создать словарь `Dictionary<uint, string>` и подгрузить его в движок
3. Либо добавить новый ресурсный файл `DATA/strings.json` с маппингом ID → текст

### P0 — Озвучка не работает
```
Voices: Initing 107 voices
Warning: Pilot: MSN10_Bundschuh: Unable to find GunBlock...
```
1.8 GB аудио (WAV/UTF) лежит в `Discovery Freelancer 4.86.0/DATA/AUDIO/`. Нужно реализовать mapping voice ID → аудиофайл для озвучки NPC.

### P1 — NPC не спавнятся (19+ ошибок)
```
MRoom: Unable to create fixed npc 'ew0601_fix_ship' for Ew06_01_Base_ShipDealer
MRoom: Unable to create fixed npc 'Hi0102_fix_trader' for ST05_02_Base_Deck
... (всего 19 ошибок)
```
Нужны модели тел/костюмов NPC и их корректная привязка к локациям.

### P1 — ОБЯЗАТЕЛЬНО: все бары и базы должны содержать оригинальные НПС и слухи
**Это критическое требование:** каждая база и бар в мире Discovery 4.86 должны иметь тех же NPC и те же слухи/rumors, что и в оригинальной игре. Данные NPC и слухов берутся из INI-файлов:
- `DATA/MISSIONS/mbases.ini` — содержит секции `[GF_NPC]` с `knowdb`, `rumorknowdb`, `rumor`
- `DATA/MISSIONS/npcships.ini` — NPC ship definitions
- `DATA/MISSIONS/rumors.ini` и аналогичные файлы — тексты слухов

Текущие ошибки парсинга:
```
Warning: knowdb without know at section GF_NPC: DATA/MISSIONS\mbases.ini, line 47960
Warning: rumorknowdb without rumor at section GF_NPC: DATA/MISSIONS\mbases.ini, line 61446
... (десятки предупреждений)
```
Нужно:
1. Исправить парсер `GF_NPC`, чтобы он корректно обрабатывал `knowdb` с несколькими `know` и `rumorknowdb` с несколькими `rumor`
2. Реализовать спавн NPC на базах с их оригинальными именами, фракциями, репутацией и оборудованием
3. Реализовать систему слухов (rumors) — каждый бар должен предлагать слухи соответствующих NPC
4. Голосовые реплики NPC должны соответствовать их фракции и роли (bartender, trader, shipdealer, weaponsdealer и т.д.)
5. Сюжетные NPC (mission NPC) должны появляться только при активации соответствующих миссий

### P1 — Материал DcDtBt (уже исправлен в коде, но проверь)
```
Error loading material detailmap_ast_rock: System.Exception: Invalid material type: DcDtBt
```
Добавлен `case "DcDtBt":` в Material.cs:240 — проверь, что все астероиды и детальные карты текстур корректно отображаются.

### P2 — FLHook-логика отсутствует
```
Keymap: Unknown key command: USER_CLOAK
Keymap: Unknown key command: USER_ACTIVATE_JUMPDRIVE
Keymap: Unknown key command: USER_SHIELDS
Keymap: Unknown key command: USER_PLAYER_INFO
Keymap: Unknown key command: USER_SELF_DESTRUCT
```
Нужно реализовать на C#:
- Jump drive (прыжковые двигатели)
- Cloaking device (маскировка)
- Player-owned bases (базы игроков)
- Cruise speed modifier (крейсерские скорости)
- Shield management (управление щитами)
- Self-destruct (самоуничтожение)

### P2 — INI-несовместимости (Discovery-specific поля)
```
Error: Not enough components for lifetime at section explosion (18+ ошибок)
Error: Invalid value for enum WEAPONS_PLATFORM (7 ошибок)
Error: Invalid value for enum MISSION_SATELLITE (14 ошибок)
```
Расширь парсеры INI для Discovery-специфичных полей: `lifetime`, `radius`, `hull_damage`, `strength`, `impulse` в explosions; `WEAPONS_PLATFORM`, `MISSION_SATELLITE` в shiparch.

### P3 — Intro-сцена вулканической планеты (5 ошибок)
```
Thn: Entity Intro_volcanoplanet_gf_volcanicglow_1 null renderer
Thn: Entity Intro_volcanoplanet_sun_5 null renderer
```
Проверь рендеринг частиц и sun-объектов в Thn-сценах.

## Важные архитектурные решения (уже приняты)
1. **librelancer.ini вместо EXE/freelancer.ini** — движок ищет `librelancer.ini` первым; все пути используют `/`, секция Windows DLL исключена
2. **cimgui отключён** — редакторы не нужны; все обращения к `Game.Debug` должны быть с `?.` или `!= null`
3. **StbImageSharp для иконки** — использует уже имеющуюся зависимость для декодинга PNG → SDL2 `SDL_SetWindowIcon`
4. **ImGui v1.89.9** — единственная версия, совместимая с imgui-node-editor
5. **crunch LZMA-заглушки** — движок использует только DXT-декомпрессию

## Целевое состояние проекта
Игра должна запускаться одной командой:
```bash
dotnet build/v2/lancer.dll
```
И предоставлять ПОЛНОСТЬЮ играбельный мир Discovery 4.86:
- Космический полёт ✅ (уже)
- Бой с оружием ✅ (уже, частично)
- Стыковка со станциями и хождение по базам с оригинальными NPC ✅ (должно быть)
- Все бары содержат оригинальных NPC и слухи ✅ (должно быть)
- Торговля товарами ⚠️ (товары загружены, интерфейс крашится)
- Диалоги с NPC ❌ (нет текстов, нет NPC)
- Озвучка ❌ (нет маппинга голосов)
- Квесты ⚠️ (миссии загружаются, но без текстов и NPC неиграбельны)
- Jump drive, cloaking, player bases ❌ (FLHook логика отсутствует)

## Инструкция по работе
1. Клонируй репозиторий с GitHub
2. Проанализируй лог запуска (приложен ниже)
3. Исправь проблемы в порядке приоритета: P0 → P1 (NPC + слухи ОБЯЗАТЕЛЬНО) → P2 → P3
4. Проверь сборку: `dotnet build -c Release src/lancer/lancer.csproj`
5. Пулл-реквест с подробным описанием изменений

## Полный лог запуска
```log
[Info: 12:11:36] Platform: Ubuntu 26.04 LTS Ubuntu 26.04 LTS - X64
[Info: 12:11:36] Available Threads: 24
[Info: 12:11:36] SDL: Using SDL3
[Info: 12:11:36] Engine: Version: c6ac349-git (20260609)
[Info: 12:11:37] GL: Version String: 4.3.0 NVIDIA 610.43.02
[Info: 12:11:37] Audio: Initialising Audio
[Info: 12:11:37] Game: Loading game data
[Warning: 12:11:37] Game: DebugView unavailable: The type initializer for 'LibreLancer.ImUI.ImGuiHelper' threw an exception.
[Warning: 12:11:37] Ini: Duplicate of trail at section Debris: DATA/FX/explosions.ini, line 15
[Warning: 12:11:37] Ini: Too many components for at_t at section destroy_hp_attachment: DATA/FX/fuse.ini, line 13
[Warning: 12:11:37] Ini: Too many components for at_t at section destroy_group: DATA/FX/fuse.ini, line 17
[Error: 12:11:37] Ini: Not enough components for lifetime at section explosion: DATA/FX/explosions.ini, line 294
[Error: 12:11:37] Ini: Not enough components for lifetime at section explosion: DATA/FX/explosions.ini, line 299
[Error: 12:11:37] Ini: Not enough components for lifetime at section explosion: DATA/FX/explosions.ini, line 335
[Error: 12:11:37] Ini: Not enough components for lifetime at section explosion: DATA/FX/explosions.ini, line 1207
[Warning: 12:11:37] Ini: Unknown entry radius at section explosion: DATA/FX/explosions.ini, line 1210
[Warning: 12:11:37] Ini: Unknown entry hull_damage at section explosion: DATA/FX/explosions.ini, line 1211
[Warning: 12:11:37] Ini: Unknown entry strength at section explosion: DATA/FX/explosions.ini, line 1302
[Warning: 12:11:37] Ini: Unknown entry impulse at section explosion: DATA/FX/explosions.ini, line 1305
[Warning: 12:11:37] Ini: Unknown section make_invincible in DATA/FX/fuse_suprise_solar.ini
[Warning: 12:11:37] Ini: Unknown entry separable in section destroy_group: DATA/FX/fuse_suprise_solar.ini
[Warning: 12:11:37] Ini: Unknown entry LODranges in section fuse: DATA/FX/fuse_suprise_solar.ini
[Warning: 12:11:37] Ini: Unknown entry dmg_hp in section destroy_group: DATA/FX/fuse_suprise_solar.ini
[Warning: 12:11:37] Ini: Unknown entry dmg_obj in section destroy_group: DATA/FX/fuse_suprise_solar.ini
[Warning: 12:11:37] Ini: Too many components for color_curve at section LightSource: DATA/UNIVERSE/systems\St01\St01.ini, line 55
[Warning: 12:11:37] Ini: Too many components for bit_radius_random_variation at section Exterior: DATA/solar\nebula\BW13_nebula.ini, line 19
[Warning: 12:11:37] Ini: Too many components for move_bit_percent at section Exterior: DATA/solar\nebula\BW13_nebula.ini, line 22
[Warning: 12:11:37] Ini: Too many components for equator_bias at section Exterior: DATA/solar\nebula\BW13_nebula.ini, line 23
[Warning: 12:11:37] Ini: Too many components for sun_burnthrough_intensity at section NebulaLight: DATA/solar\nebula\BW13_nebula.ini, line 27
[Warning: 12:11:37] Ini: Too many components for sun_burnthrough_scaler at section NebulaLight: DATA/solar\nebula\BW13_nebula.ini, line 28
[Warning: 12:11:37] Ini: Too many components for puff_max_alpha at section Clouds: DATA/solar\nebula\BW13_nebula.ini, line 35
[Warning: 12:11:37] Ini: Too many components for puff_drift at section Clouds: DATA/solar\nebula\BW13_nebula.ini, line 41
[Warning: 12:11:37] Ini: Too many components for lightning_intensity at section Clouds: DATA/solar\nebula\BW13_nebula.ini, line 43
[Warning: 12:11:37] Ini: Too many components for lightning_gap at section Clouds: DATA/solar\nebula\BW13_nebula.ini, line 45
[Warning: 12:11:37] Ini: Too many components for lightning_duration at section Clouds: DATA/solar\nebula\BW13_nebula.ini, line 46
[Warning: 12:11:37] Ini: Too many components for duration at section BackgroundLightning: DATA/solar\nebula\BW13_nebula.ini, line 48
[Warning: 12:11:37] Ini: Too many components for gap at section BackgroundLightning: DATA/solar\nebula\BW13_nebula.ini, line 49
[Warning: 12:11:37] Ini: Too many components for texture_aspect at section Band: DATA/solar\asteroids\Hi20_Asteroid_ring.ini, line 49
[Warning: 12:11:37] Ini: Duplicate of material_library at section Good: DATA/EQUIPMENT/weapon_good.ini, line 5807
[Warning: 12:11:37] Ini: Too many components for fog_far at section Exclusion Zones: DATA/solar\nebula\LI13_nebula.ini, line 17
[Warning: 12:11:37] Ini: Too many components for color_curve at section LightSource: DATA/UNIVERSE/systems\LI15\LI15.ini, line 72
[Warning: 12:11:37] Ini: Too many components for shell_scalar at section Exclusion Zones: DATA/solar\nebula\EW09_siberianfield.ini, line 14
[Error: 12:11:37] Ini: Invalid value for enum WEAPONS_PLATFORM at section Ship: DATA/SHIPS/shiparch.ini, line 7884
[Error: 12:11:37] Ini: Invalid value for enum WEAPONS_PLATFORM at section Ship: DATA/SHIPS/shiparch.ini, line 7901
[Error: 12:11:37] Ini: Invalid value for enum MISSION_SATELLITE at section Ship: DATA/SHIPS/shiparch.ini, line 7918
[Error: 12:11:37] Ini: Invalid value for enum MISSION_SATELLITE at section Ship: DATA/SHIPS/shiparch.ini, line 7935
[Warning: 12:11:37] Ini: knowdb without know at section GF_NPC: DATA/MISSIONS\mbases.ini, line 47960
[Warning: 12:11:37] Ini: knowdb without know at section GF_NPC: DATA/MISSIONS\mbases.ini, line 48117
[Warning: 12:11:37] Ini: rumorknowdb without rumor at section GF_NPC: DATA/MISSIONS\mbases.ini, line 61446
[Warning: 12:11:37] Ini: rumorknowdb without rumor at section GF_NPC: DATA/MISSIONS\mbases.ini, line 61468
[Info: 12:11:37] Game: Initing Pilots
[Info: 12:11:37] Game: Initing 100asteroids
[Info: 12:11:37] Game: Initing Debris
[Info: 12:11:37] Game: Initing 73 stars
[Info: 12:11:37] Voices: Initing 107 voices
[Warning: 12:11:37] Pilot: MSN10_Bundschuh: Unable to find GunBlock 'story_gun_capship_msn10_bundschuh'
[Info: 12:11:37] Game: Initing Explosions
[Info: 12:11:37] Game: Initing 498 ships
[Info: 12:11:37] Game: Initing 2289 equipments
[Error: 12:11:37] Ship: Unrecognised hp_type hp_turret in ge_transport
[Error: 12:11:37] Ship: Unrecognised hp_type hp_cargo_pod in ge_transport
[Error: 12:11:37] Ship: Unrecognised hp_type hp_gun in msn_playership
[Error: 12:11:37] Ship: Unrecognised hp_type hp_gun in li_fighter_indestr
[Error: 12:11:37] Ship: Unrecognised hp_type hp_gun in li_fighter_King
[Error: 12:11:37] Ship: Unrecognised hp_type hp_turret in large_transport_m03
[Info: 12:11:37] Game: Initing 2173 goods
[Warning: 12:11:37] Keymap: Unknown key command: USER_HUD
[Warning: 12:11:37] Keymap: Unknown key command: USER_TOGGLE_HUD_COLOR
[Warning: 12:11:37] Keymap: Unknown key command: USER_CLOAK
[Warning: 12:11:37] Keymap: Unknown key command: USER_LIGHTS
[Warning: 12:11:37] Keymap: Unknown key command: USER_PLAYER_INFO
[Warning: 12:11:37] Keymap: Unknown key command: USER_SELF_DESTRUCT
[Warning: 12:11:37] Keymap: Unknown key command: USER_SHIELDS
[Warning: 12:11:37] Keymap: Unknown key command: USER_GROUP_MARK_TARGET
[Warning: 12:11:37] Keymap: Unknown key command: USER_GROUP_UNMARK_TARGET
[Warning: 12:11:37] Keymap: Unknown key command: USER_ACTIVATE_JUMPDRIVE
[Info: 12:11:37] Game: Initing 607 archetypes
[Info: 12:11:37] Factions: Initing 101 factions
[Info: 12:11:37] Game: Initing 772 bases
[Error: 12:11:37] MRoom: Unable to create fixed npc 'ew0601_fix_ship' for Ew06_01_Base_ShipDealer
[Error: 12:11:37] MRoom: Unable to create fixed npc 'ew0602_fix_weaponsdealer' for Ew06_02_Base_Equipment
[Error: 12:11:37] MRoom: Unable to create fixed npc 'ew0602_fix_ship' for Ew06_02_Base_ShipDealer
[Error: 12:11:38] MRoom: Unable to create fixed npc 'BW7103_fix_ship' for BW71_03_Base_Deck
[Error: 12:11:38] MRoom: Unable to create fixed npc 'hi1002_fix_ship' for HI10_02_Base_Deck
[Error: 12:11:38] MRoom: Unable to create fixed npc 'ST0501_fix_weaponsdealer' for ST05_01_Base_Equipment
[Error: 12:11:38] MRoom: Unable to create fixed npc 'ST0501_fix_ship' for ST05_01_Base_ShipDealer
[Error: 12:11:38] MRoom: Unable to create fixed npc 'Hi0102_fix_trader' for ST05_02_Base_Deck
[Error: 12:11:38] MRoom: Unable to create fixed npc 'Hi0102_fix_weaponsdealer' for ST05_02_Base_Deck
[Error: 12:11:38] MRoom: Unable to create fixed npc 'KU1701_fix_ship' for LI07_01_Base_ShipDealer
[Error: 12:11:38] MRoom: Unable to create fixed npc 'Hi0102_fix_trader' for BR14_01_Base_Deck
[Error: 12:11:38] MRoom: Unable to create fixed npc 'Hi0102_fix_weaponsdealer' for BR14_01_Base_Deck
[Error: 12:11:38] MRoom: Unable to create fixed npc 'li1006_fix_bartender' for Li10_05_Base_Bar
[Error: 12:11:38] MRoom: Unable to create fixed npc 'li1006_fix_trader' for Li10_05_Base_Deck
[Error: 12:11:38] MRoom: Unable to create fixed npc 'ST0803_fix_trader' for ST08_03_Base_Planetscape
[Error: 12:11:38] MRoom: Unable to create fixed npc 'EW1901_fix_bartender' for EW19_01_Base_Bar
[Error: 12:11:38] MRoom: Unable to create fixed npc 'EW1902_fix_bartender' for EW19_02_Base_Bar
[Error: 12:11:38] MRoom: Unable to create fixed npc 'EW1903_fix_bartender' for EW19_03_Base_Bar
[Error: 12:11:38] MRoom: Unable to create fixed npc 'EW1904_fix_bartender' for EW19_04_Base_Bar
[Info: 12:11:38] Game: Loading intro scenes
[Info: 12:11:38] Game: Initing 97 faction commodity profiles
[Info: 12:11:38] Game: Initing 1541 shops
[Info: 12:11:38] Game: Initing 155 systems
[Info: 12:11:38] Game: Calculating shortest paths
[Info: 12:11:38] Game: Shortest paths calculated
[Info: 12:11:38] UI: Interface loaded
[Info: 12:11:38] Game: Finished loading game data
[Warning: 12:11:38] Strings: Not Found: 1271
[Error: 12:11:38] Thn: Entity Intro_volcanoplanet_gf_volcanicglow_1 null renderer
[Error: 12:11:38] Thn: Entity Intro_volcanoplanet_gf_volcanicglow_2 null renderer
[Error: 12:11:38] Thn: Entity Intro_volcanoplanet_gf_volcanicglow_3 null renderer
[Error: 12:11:38] Thn: Entity Intro_volcanoplanet_planetstorm_4 null renderer
[Error: 12:11:38] Thn: Entity Intro_volcanoplanet_sun_5 null renderer
[Info: 12:11:38] Game: Initial load took 1,920947 seconds
[Warning: 12:11:38] Strings: Not Found: 1268
[Warning: 12:11:38] Strings: Not Found: 1270
[Warning: 12:11:38] Strings: Not Found: 1272
[Info: 12:11:41] Mission: Loading mission: Mission_13 with 0 saved triggers
[Info: 12:11:41] Missions: Activate 'bse_initialize_init_li01'
[Warning: 12:11:41] Server: Running slow: update took 16,78ms
[Warning: 12:11:41] Sph: Sph DATA/solar\suns\sun.sph does not contain all 6 sides and will not render
[Info: 12:11:41] Server: Spun up FP7_system ()
[Info: 12:11:41] Client: Spawning in FP7_system
[Info: 12:11:41] Game: Entering system FP7_system
[Info: 12:11:41] Player: Spawning at 6 + delay 2
[Warning: 12:11:41] Server: Running slow: update took 22,98ms
[Warning: 12:11:41] Sph: Sph DATA/solar\suns\sun.sph does not contain all 6 sides and will not render
[Error: 12:11:41] Mat: Error loading material detailmap_ast_rock: System.Exception: Invalid material type: DcDtBt
   at LibreLancer.Utf.Mat.Material.FromNode(IntermediateNode node) in .../Material.cs:line 251
[Error: 12:11:41] Mat: Error loading material detailmap_ast_rock02: System.Exception: Invalid material type: DcDtBt
   at LibreLancer.Utf.Mat.Material.FromNode(IntermediateNode node) in .../Material.cs:line 251
[Warning: 12:11:41] Strings: Not Found: 1254
[Warning: 12:11:41] Strings: Not Found: 3051
[Warning: 12:11:41] Strings: Not Found: 3101
[Warning: 12:11:41] Strings: Not Found: 993
[Warning: 12:11:41] Strings: Not Found: 3104
[Warning: 12:11:41] Strings: Not Found: 995
[Warning: 12:11:41] Strings: Not Found: 3117
[Warning: 12:11:41] Strings: Not Found: 1568
[Warning: 12:11:41] Strings: Not Found: 8512
[Warning: 12:11:41] Strings: Not Found: 1611
[Warning: 12:11:41] Strings: Not Found: 261039
[Warning: 12:11:41] Strings: Not Found: 501011
[Warning: 12:11:41] Strings: Not Found: 505217
[Warning: 12:11:41] Strings: Not Found: 263357
[Info: 12:11:41] Player: 11 ticks elapsed after load
[Error: 12:11:52] Engine: Librelancer has crashed.
Object reference not set to an instance of an object.
   at LibreLancer.Interface.UiContext.MouseOnDoubleClick(MouseEventArgs e) in .../UiContext.cs:line 93
   at LibreLancer.Mouse.OnMouseDoubleClick(MouseButtons b) in .../Mouse.cs:line 50
   at LibreLancer.Platforms.SDL3Game.Run(Game loop) in .../SDL3Game.cs:line 555
   at LibreLancer.Game.Run() in .../Game.cs:line 226
   at lancer.MainClass.<>c__DisplayClass0_0.<Main>b__0() in .../Program.cs:line 23
   at LibreLancer.AppHandler.Run(Action action, Action onCrash) in .../AppHandler.cs:line 152
Librelancer Version: c6ac349-git (20260609)
```
