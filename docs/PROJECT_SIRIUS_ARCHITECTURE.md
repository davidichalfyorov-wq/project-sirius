# Project Sirius — Архитектура движка (для LLM-ассистента)

## Смысл проекта
Project Sirius — ремейк Freelancer (2003) на C#+OpenGL, как Black Mesa для Half-Life. Движок: **LibreLancer** (.NET 10.0, MIT). Данные: **Discovery Freelancer 4.86** — крупнейший мод (152 системы, 139 фракций, 617 баз, 290+ кораблей). Цели: кроссплатформенность, PBR-рендеринг, мультиплеер, полная бинарная совместимость с оригинальными форматами.

## Технологический стек

| Слой | Технология |
|------|-----------|
| Язык | C# 13 / .NET 10.0 |
| Графика | OpenGL 3.1+, PBR, шейдеры HLSL→SPIR-V (DXC) |
| Окно/ввод | SDL2/SDL3 |
| Аудио | OpenAL Soft + lancerdecode (ADPCM) |
| Физика | BepuPhysics2 |
| Сеть | LiteNetLib (UDP) + свой битовый протокол |
| UI игры | XML + Lua (WattleScript, форк MoonSharp) |
| UI редакторов | Dear ImGui (C++ биндинги cimgui) |
| Скриптинг | Thorn (.thn катсцены, C++ компилятор) |
| Шрифты | blurgtext (C++/PInvoke) |

Нативные библиотеки в extern/: crunch (DXT-текстуры), imgui, lancerdecode, SPIRV-Cross, thorncompiler. Собираются CMake в папку binaries/, доступ через [DllImport].

## Где что находится

```
src/
├── LibreLancer/              # ЯДРО движка
│   ├── FreelancerGame.cs     # Точка входа (223c)
│   ├── Client/               # Сессия, торговля, компоненты клиента
│   ├── Render/               # SystemRenderer (514c), 18 материалов
│   ├── World/                # ECS: GameObject (977c) + 23 компонента
│   ├── Server/               # GameServer (314c), ServerWorld (1211c)
│   ├── Net/                  # GameNetClient (553c), Packets (1214c)
│   ├── Interface/            # XML+Lua UI: LuaContext, UiContext, UiWidget
│   ├── Utf/                  # Парсеры: Cmp, Dfm, Mat, Anm, Ale, Vms
│   ├── Thn/                  # Катсцены Thorn
│   ├── Missions/             # Миссии: условия, триггеры, директивы
│   ├── Fx/                   # Частицы: эмиттеры, поля
│   ├── Shaders/              # 61 HLSL-шейдер (PBR, nebula, atmosphere)
│   └── GameStates/           # LoadingData, SpaceGameplay, RoomGameplay
├── LibreLancer.Base/          # Game.cs (309c), OpenGL, SDL2, ввод, математика
├── LibreLancer.Data/          # Парсинг INI + GameItemDb (2666c!)
│   └── Generator/            # Roslyn source-gen парсеров
├── lancer/Program.cs          # main() клиента (26c)
├── LLServer/                  # Dedicated server
├── Editor/                    # LancerEdit, InterfaceEdit, ImUI
├── BindingsGen/               # Генераторы биндингов OpenGL/ImGui
└── cimgui_ext/                # C++ расширения ImGui
extern/                        # Git-сабмодули (12 библиотек)
uixml/                         # 36 пар XML+Lua UI сцен
Discovery Freelancer 4.86.0/   # Данные мода (Git LFS, 4.4G)
```

## Архитектура движка

### Жизненный цикл
`lancer/Program.cs` → `new FreelancerGame(config)` → `game.Run()`.
**Load()**: VFS из папки Freelancer → OpenGL → OpenAL → `GameDataManager.LoadData()` на фоновом потоке "GamedataLoader" → `ChangeState(LoadingDataState)`.
**Update/Draw**: делегируют текущему `GameState`. `Game.cs` (309c) — платформенная абстракция: `IGame` (SDL2), `List<object> Services` (Service Locator), управление окном/VSync.

### Конвейер данных (двухфазный)

**Фаза 1 — парсинг INI** (параллельный, все ядра CPU):
`FreelancerData.LoadData()` → `ParallelActionRunner` → 60+ типизированных контейнеров (EquipmentIni, ShiparchIni, UniverseIni, ...).
Roslyn Source Generator на атрибутах `[ParsedSection("name")]` и `[Entry("key")]` генерирует парсеры для каждого класса. `IniStringPool` — отложенная интернизация строк.

**Фаза 2 — превращение в runtime** (граф зависимостей):
`GameItemDb.LoadData()` → `LoadingTasks`: Pilots→Fuses→Ships→Equipment→Goods→Factions→Bases→Systems (зависит от всего). Результат: 15 репозиториев `GameItemCollection<T>`. Промежуточные объекты обнуляются, `GC.Collect()` с LOH-компактацией.

**Сервисный слой**: `GameDataManager` — `GetSolar()`, `GetEquipment()`, `GetInfocard()`, `PreloadObjects()`, `LoadSystemResources()`.

### ECS (Entity-Component-System)
Не Data-Oriented, а компромиссный Entity-Component. `GameObject` (977c) = иерархический scene graph (Parent/Children, Transform с ленивым пересчётом) + 3 жёстких слота (Render, Physics, Animation) + гибкая компонентная система:
- `AddComponent<T>(T)` → `List<GameComponent>` + `Dictionary<Type, GameComponent>` (O(1) lookup)
- `TryGetComponent<T>(out T)`, `GetComponents<T>()` (struct-итератор без аллокаций)
- 23+ компонента: Ship, Weapon, Gun, Shield, Cloak, Engine, Autopilot, Dock, CargoPod, AsteroidField, Costume, ...
- `GameWorld.LoadSystem(StarSystem)`: создаёт GameObject на каждый SystemObject + AsteroidFieldComponent на поле астероидов
- Update: физика (BepuPhysics2) → компоненты → SpatialLookup

### Рендер-пайплайн
`SystemRenderer.Draw()` — 16 этапов строго по порядку: MSAA → depth pre-pass (ColorWrite=false) → opaque pass → астероиды → туманности → спрайты → частицы (FxPool) → CommandBuffer.DrawOpaque() → звёздный фон → fog transition → transparent pass (DepthWrite=false) → MSAA blit.

18 материалов от `RenderMaterial` (316c). `BasicMaterial` (323c) — основной PBR-материал: динамически выбирает шейдер по флагам (VERTEX_LIGHTING, ALPHATEST, FADE, NORMALMAP, TEX2) и типу вершин (FVFVertex, DfmVertex для скининга). До 9 динамических источников света через UBO.

### Сетевая архитектура
Авторитарный сервер, клиентское предсказание. LiteNetLib UDP. `GameNetClient` (553c): выделенный поток networkThread, `ConcurrentQueue<IPacket>`, LAN discovery.
Протокол `Packets.cs` (1214c): битовое дельта-кодирование. `InputUpdatePacket` — 4 тика истории ввода. `PlayerAuthState` — позиция 24 бита в 32-юнитном окне, поворот 18 бит на кватернион. `ObjectSpawnInfo` — компактный 16-байтный формат. `NetHpidWriter/Reader` — дедупликация имён хардпоинтов.
Сервер: `GameServer` (314c) + `ServerWorld` (1211c). 60Hz FixedTimestepLoop. `SendWorldUpdates()` пакует ObjectUpdate через `UpdatePacker`. RPC — Roslyn source-gen (`[RPCMethod]`/`[RPCCallback]`).

### UI-система (XML + Lua)
```
uimain.lua → LuaContext (261c, C#↔Lua мост)
  → UiContext (546c, вирт. координаты 480ед высоты, стек модалок)
  → Scene → Container → UiWidget (378c, Anchor, CSS-cascade, fly-анимации)
```
36 пар XML+Lua в uixml/. WattleScript интерпретирует Lua, C# экспортирует виджеты через `[WattleScriptUserData]`. Stylesheet.xml — CSS-подобные стили с каскадом.

### Форматы Freelancer
UTF-файлы (Utf/): дерево Node (LeafNode/IntermediateNode). CmpFile — составные модели с хардпоинтами. DfmFile — скининг (кости, FaceGroup). MatFile — материалы (Dc/Dt/Et/Nt). AnmFile — анимации. AleFile — граф эффектов AlchemyNodeLibrary. VmsFile — меш-данные (D3DFVF).

## Ключевые паттерны
Service Locator (Game.Services), двухфазная загрузка, ленивые ресурсы, GC-оптимизация (обнуление промежуточных объектов), Roslyn Source Generators (INI + RPC), дельта-компрессия сети, State Machine (GameState), CSS-cascade UI, string interning (IniStringPool, CRC32).

## Быстрые ссылки
`src/lancer/Program.cs:1` — main; `src/LibreLancer/FreelancerGame.cs:75` — init; `src/LibreLancer.Data/Schema/FreelancerData.cs:119` — парсинг; `src/LibreLancer.Data/GameItemDb.cs:1` — runtime; `src/LibreLancer/Render/SystemRenderer.cs:1` — рендер; `src/LibreLancer/Render/Materials/BasicMaterial.cs:1` — PBR; `src/LibreLancer/World/GameObject.cs:1` — ECS; `src/LibreLancer/Net/GameNetClient.cs:1` — сеть; `src/LibreLancer/Net/Protocol/Packets.cs:1` — протокол; `src/LibreLancer/Server/GameServer.cs:1` — сервер; `src/LibreLancer/Interface/LuaContext.cs:1` — Lua; `src/LibreLancer/Interface/UiWidget.cs:1` — виджеты; `src/LibreLancer/Shaders/` — 61 шейдер; `src/LibreLancer.Data.Generator/ParserGenerator.cs:1` — source-gen.

## Что нужно доделать (Discovery-совместимость)
1. `commodities_per_faction.ini` — парсер `[FactionGood]`
2. `weaponmoddb.ini` — shield damage modifiers
3. Jump Drive, Docking Module, Survey Module
4. Оптимизация под ×5–×10 объёма данных (106K строк mbases.ini)
5. Серверная логика (player bases, cruise speeds — замена FLHook)
