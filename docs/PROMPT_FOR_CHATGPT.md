# Техническое задание: Project Sirius — полный запуск и тестирование

## 1. Контекст и смысл проекта

Project Sirius — ремейк игры Freelancer (2003, Microsoft / Digital Anvil) на современном движке LibreLancer. Аналог Black Mesa для Half-Life. Цель: полностью переписанный игровой движок на C# + OpenGL, совместимый с оригинальными форматами данных, но с современной графикой (PBR), кроссплатформенностью (Windows/Linux/macOS) и поддержкой мультиплеера.

За основу данных взят мод Discovery Freelancer 4.86 — крупнейшее расширение Freelancer: 152 системы, 139 фракций, 617 баз, 290+ кораблей, 275+ товаров.

## 2. Что в архиве

```
project_sirius_full.tar.gz
├── src/                              # Исходный код C# движка
│   ├── LibreLancer/                  # Ядро: рендер, ECS, сервер, сеть, UI, парсеры
│   ├── LibreLancer.Base/             # Платформа (OpenGL, SDL2), Game.cs
│   ├── LibreLancer.Data/             # Парсинг INI + GameItemDb.cs (2666 строк!)
│   ├── LibreLancer.Physics/          # BepuPhysics2 wrapper
│   ├── LibreLancer.Media/            # OpenAL аудио
│   ├── LibreLancer.Thorn/            # Thorn-скриптинг (катсцены)
│   ├── LibreLancer.Database/         # EF Core + SQLite
│   ├── lancer/Program.cs             # Точка входа клиента
│   ├── LLServer/                     # Сервер
│   ├── Editor/                       # LancerEdit, InterfaceEdit, ImUI
│   └── BindingsGen/                  # Генераторы биндингов OpenGL/ImGui
├── extern/                           # Git-сабмодули (BepuPhysics2, LiteNetLib, ImGui, и др.)
├── Discovery Freelancer 4.86.0/DATA/ # ВСЕ INI-файлы мода Discovery (3632 шт.)
│   ├── EQUIPMENT/                    # weapon_equip.ini (44K строк), goods.ini, и др.
│   ├── SHIPS/                        # shiparch.ini (36K строк), loadouts
│   ├── UNIVERSE/SYSTEMS/             # 152 системных каталога
│   ├── MISSIONS/                     # mbases.ini (106K строк!), npcships.ini
│   ├── SOLAR/                        # solararch.ini, stararch.ini, ASTEROIDS/
│   └── ...
├── uixml/                            # XML+Lua UI: 36 сцен (HUD, меню, базы, карта)
├── docs/
│   └── PROJECT_SIRIUS_ARCHITECTURE.md # ПОЛНАЯ АРХИТЕКТУРНАЯ ИНСТРУКЦИЯ (10K символов)
│   └── PROMPT_FOR_CHATGPT.md         # Этот документ
├── CMakeLists.txt                     # Сборка нативных C/C++ библиотек
├── LibreLancer.sln                    # Visual Studio Solution (21 проект)
├── build.sh                           # Основной скрипт сборки
└── README.md
```

**Главный документ — `docs/PROJECT_SIRIUS_ARCHITECTURE.md`** — прочитай его первым. Там вся архитектура: жизненный цикл игры, конвейер загрузки данных, ECS-система, рендер-пайплайн, сетевая архитектура, UI-система, форматы файлов.

## 3. Технологический стек

| Слой | Технология |
|------|-----------|
| Язык | C# 13 / .NET 10.0 |
| Графика | OpenGL 3.1+, PBR, шейдеры HLSL→SPIR-V (DXC) |
| Физика | BepuPhysics2 |
| Сеть | LiteNetLib (UDP) + битовый протокол |
| UI | XML+Lua (WattleScript) |
| Аудио | OpenAL Soft + lancerdecode |
| Нативные библы | blurgtext, crunch, lancerdecode, thorncompiler (C/C++, CMake) |

## 4. Текущее состояние (ЧТО УЖЕ СДЕЛАНО)

Движок собран. Находится в папке `build/`. Бинарник `lancer` (ELF x86-64) + все библиотеки + нативные .so. Конфиг `~/.config/librelancer.ini` указывает на папку Discovery.

**Что было исправлено в коде для сборки:**
- `CMakeLists.txt` — отключён cimgui (для редакторов нужен точный коммит ImGui)
- `ZoneLookup.cs:117` — `tree.GetOverlaps()` теперь принимает `BufferPool` параметр
- `GameListener.cs:266` — `Server.ConnectedPeerList` → `Server.GetConnectedPeers(List)`
- `PhysicsWorld.cs:254` — `Simulation.RayCast()` принимает 5-й аргумент
- `TextEditor.cpp` — убраны поля `WantTextInput`, `ViewportId` (нет в ImGui v1.89.9)
- `imgui_extra_math.inl` — убран дублирующийся `operator*(float, ImVec2)`
- `crnlib/*.cpp` — 16 заглушек для LZMA-файлов (есть только в старом форке crunch)

## 5. ЗАДАЧА: сделать ПОЛНЫЙ запуск и тестирование Project Sirius

Твоя задача — довести проект до состояния готового продукта: движок LibreLancer, полностью работающий с данными Discovery Freelancer 4.86.

### 5.1. Обязательный этап: аудит кодовой базы

Перед любыми изменениями проведи ПОЛНЫЙ аудит:

1. **Прочитай `docs/PROJECT_SIRIUS_ARCHITECTURE.md`** — это карта проекта
2. **Прочитай `src/lancer/Program.cs`** — точка входа
3. **Прочитай `src/LibreLancer/FreelancerGame.cs`** — инициализация движка
4. **Прочитай `src/LibreLancer.Data/Schema/FreelancerData.cs`** — загрузка INI-файлов
5. **Прочитай `src/LibreLancer.Data/Schema/FreelancerIni.cs`** — парсинг freelancer.ini, определение DataPath
6. **Прочитай `src/LibreLancer.Data/GameItemDb.cs`** — конвертация INI в runtime-объекты (крупнейший файл: 2666 строк)
7. **Прочитай `src/LibreLancer/GameConfig.cs`** — валидация пути к данным игры
8. **Проверь Discovery `EXE/freelancer.ini`** — `data path= ..\data`, `[Resources]`, `[Data]` секции
9. **Проверь ВСЕ Discovery INI-файлы** на предмет новых форматов, отсутствующих в ванильном Freelancer
10. **Составь полный список** того, что есть в Discovery, но отсутствует в коде парсеров

### 5.2. Data Pipeline (критический путь)

Discovery добавляет форматы, которых нет в ванильном Freelancer:

1. **`commodities_per_faction.ini`** — секции `[FactionGood]` с `faction = ...` и `MarketGood = name, min, max`. Нужен новый парсер в `FreelancerData.cs`, обработка в `GameItemDb.cs`, интеграция в систему экономики.

2. **`weaponmoddb.ini`** — секции `[WeaponType]` с `shield_mod = ...` для модификаторов урона по типам щитов. Новый парсер + интеграция в систему повреждений.

3. **CloakingDevice** — уже есть в `EquipmentIni.cs` (секция `[cloakingdevice]`), но проверить, полностью ли работает runtime-обработка в `GameItemDb.cs`.

4. **Новые типы оборудования**: Jump Drive, Docking Module, Survey Module — Discovery-специфичные. Добавить парсинг и runtime-обработку.

5. **Расширенный `freelancer.ini`** — Discovery добавляет свои DLL в `[Resources]` (`Discovery.dll`, `DsyAddition.dll`), дополнительные fuse-файлы, rtc_shiparch.ini. Проверить, что `FreelancerIni.cs` корректно всё резолвит.

6. **`DatastormWarning.ini`** — Discovery-специфичный файл. Разобраться, нужен ли для движка.

7. **Масштаб данных**: `mbases.ini` 106K строк, `weapon_equip.ini` 44K строк, `shiparch.ini` 36K строк. `ParallelActionRunner` должен справиться, но проверить на переполнение стека/памяти.

### 5.3. Runtime Gameplay (замена FLHook)

Discovery на Windows использует FLHook.dll + 13 плагинов для серверной логики. Всё это нужно переписать на C#:

1. **Player-owned bases** (FLHook: base.dll, mobiledock.dll) — создание/управление базами игроков
2. **Cloaking device logic** (FLHook: cloak.dll) — активация/деактивация, расход энергии
3. **Jump Drive** (FLHook: jump.dll) — прыжки между системами
4. **Cruise speeds** — разные корабли имеют разную крейсерскую скорость (MultiCruise.dll)
5. **Mine control** — управление минными полями (FLHook: minecontrol.dll)
6. **Player commands** — `/stuck`, `/charinfo` и т.д. (FLHook: playercntl.dll)
7. **Temp bans** (FLHook: tempban.dll) — перенести в `LLServer`
8. **Faction commodity demands** — на основе `commodities_per_faction.ini`

### 5.4. Тестирование (ОБЯЗАТЕЛЬНО)

1. **Сборка**: `./build.sh` должен проходить БЕЗ ОШИБОК (warnings допустимы)
2. **Запуск клиента**: `build/lancer` должен:
   - Загрузить все 3632 INI файла без краша
   - Распарсить `mbases.ini` (106K строк) без ошибок
   - Загрузить 152 системы, 617 баз, 290 кораблей
   - Показать главное меню
3. **Загрузка системы**: выбрать любую систему Discovery (например, `LI01` — New York) и загрузить её
4. **Рендеринг**: корабли, базы, астероиды должны отрисовываться
5. **Сервер**: `LLServer` должен стартовать и принимать подключения
6. **Аудио**: звуки, музыка, голосовые реплики
7. **UI**: все 36 сцен XML+Lua должны работать (HUD, меню, базы, карта, чат, торговля)
8. **Сетевая игра**: клиент-серверное соединение, спавн корабля

### 5.5. Критерии приёмки

- ✅ `./build.sh` проходит без ошибок
- ✅ `./build/lancer` запускается и показывает главное меню
- ✅ Все 3632 INI файла Discovery парсятся без крашей
- ✅ Можно загрузить и отрендерить минимум 1 игровую систему
- ✅ Сервер стартует и принимает клиентов
- ✅ Базовый геймплей: полёт, стрельба, стыковка
- ✅ Торговля работает с Discovery-экономикой
- ✅ Cloaking device, Jump Drive, Cruise speeds работают
- ✅ Все изменения задокументированы

## 6. Правила работы

1. **НЕ удаляй существующий код**, только дополняй
2. **Следуй стилю кода проекта**: C# 13, file-scoped namespaces, primary constructors
3. **Документируй ВСЕ изменения** — создай `CHANGELOG.md` с описанием что и зачем сделано
4. **Проверяй сборку после каждого изменения**: `dotnet build src/lancer/lancer.csproj`
5. **Discovery.dll и DsyAddition.dll — это Windows DLL**. Не пытайся их загрузить на Linux. Вместо этого:
   - Создай `Discovery Freelancer 4.86.0/librelancer.ini` с путями к данным напрямую
   - Или добавь fallback-логику в `FreelancerIni.cs`
6. **Используй `docs/PROJECT_SIRIUS_ARCHITECTURE.md` как карту проекта**. Там есть быстрые ссылки на все ключевые файлы.
7. **Веди дневник прогресса**: что проверил, что работает, что нет. Результат оформи в `TEST_REPORT.md`.
