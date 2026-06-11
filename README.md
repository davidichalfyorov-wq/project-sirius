# Project Sirius

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![C#](https://img.shields.io/badge/C%23-13-239120?logo=csharp&logoColor=white)](#)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](#)
[![Vulkan](https://img.shields.io/badge/Vulkan-renderer-AC162C?logo=vulkan&logoColor=white)](#)
[![Platforms](https://img.shields.io/badge/Windows%20%7C%20Linux%20%7C%20macOS-cross--platform-blue)](#)

> *What Black Mesa is to Half-Life, Project Sirius is to Freelancer.*

**English version below — [русская версия ниже](#project-sirius-русская-версия).**

---

**Project Sirius** is a modern remake of the cult-classic space simulator **Freelancer** (2003, Digital Anvil / Microsoft). The engine is rewritten from scratch in C# with a **Vulkan** renderer, fully compatible with the original game formats.

It is built on the **[LibreLancer](https://github.com/Librelancer/Librelancer)** engine and the **Discovery Freelancer 4.86** mod — the largest community modification, with two decades of development history.

## Contents

- [What is Project Sirius?](#what-is-project-sirius)
- [What is Discovery?](#what-is-discovery)
- [Current state](#current-state)
- [Building and running](#building-and-running)
- [Project structure](#project-structure)
- [License](#license)
- [Русская версия](#project-sirius-русская-версия)

## What is Project Sirius?

Project Sirius is a modern engine for Freelancer with the following goals:

- **Native cross-platform support** — Windows, Linux, macOS
- **Modern graphics** — Vulkan renderer, PBR, HDR pipeline (filmic/ACES tonemapping, bloom, light rays, FXAA), cascaded shadow maps
- **Full compatibility** with original Freelancer formats (UTF models, Thorn scripts, INI data, ALE effects)
- **Multiplayer** — dedicated server with AI, NPCs and scripting
- **Modding tools** — built-in editors for models, particles, UI and scripts

### Technology stack

| Component | Technology |
|-----------|-----------|
| Language | C# 13 / .NET 10.0 |
| Graphics | **Vulkan** (default), PBR shaders (HLSL → SPIR-V) |
| Window/Input | SDL2 / SDL3 |
| Audio | OpenAL Soft |
| Physics | BepuPhysics2 |
| Networking | LiteNetLib (UDP) |
| UI | XML+Lua (in-game), Dear ImGui (editors) |
| Scripting | Thorn (cutscenes), WattleScript / Lua (UI) |

## What is Discovery?

**Discovery Freelancer 4.86** is the largest and longest-running Freelancer mod, developed by the **Discovery Gaming Community** since 2005.

Version 4.86 adds on top of the original game:

- **152 systems** (instead of 48) — including the new house of Gallia
- **139 factions**, **617 bases**, **290+ ships**
- **275+ commodities** with a faction-based economy
- Dozens of new equipment types: Cloaking Device, Jump Drive, Docking Module
- A large-scale storyline (818 A.S., the Sirius war)
- Thousands of new infocards, dialogues and missions

Project Sirius uses Discovery 4.86 as its base game data set — the engine parses the original `.ini` formats directly.

## Current state

Project Sirius is in active development. The engine already supports:

- **Vulkan rendering backend enabled by default** (validated against the OpenGL path with SSIM-gated screenshot comparison)
- Loading and rendering of game worlds (systems, planets, asteroid fields)
- PBR rendering of ships and environments with an HDR post-processing pipeline
- Cascaded shadow maps for the system sun and local spotlight shadows
- Basic physics and networking
- Content editors (models, particles, UI)

Being adapted for Discovery 4.86:

- Parsing of `commodities_per_faction.ini` (faction-based economy)
- Shield damage type modifiers from `weaponmoddb.ini`
- Support for Jump Drive, Docking Module, Survey Module
- Optimization for the increased data volume (5–10× vanilla)
- Server logic: player-owned bases, cloaking, cruise speeds

## Building and running

### Requirements (Linux)

- .NET 10.0 SDK (x86-64 or arm64)
- SDL2 (or SDL3)
- OpenAL Soft
- GCC/G++ and CMake 3.15+
- GTK3 (includes FreeType, Pango, Cairo)
- Vulkan-capable GPU and drivers

### Build

```bash
# Clone with submodules
git clone --recursive https://github.com/davidichalfyorov-wq/project-sirius.git
cd project-sirius

# Build the engine
./build.sh
```

### Run

```bash
dotnet run --project src/lancer
```

On first launch, point the game to a Freelancer installation (or to the `Discovery Freelancer 4.86.0` folder in the repository).

### Nix (alternative)

```bash
nix-shell --pure
./build.sh
```

## Project structure

```
Project Sirius/
├── src/                     # C# source code
│   ├── LibreLancer/         # Core game engine
│   ├── LibreLancer.Base/    # Base libraries (platform, graphics, input)
│   ├── LibreLancer.Data/    # Freelancer game data parsing
│   ├── LibreLancer.Media/   # Audio (OpenAL)
│   ├── LibreLancer.Physics/ # Physics (BepuPhysics2)
│   ├── LibreLancer.Thorn/   # Thorn scripting (cutscenes)
│   ├── lancer/              # Game client
│   ├── LLServer/            # Dedicated server
│   ├── Editor/              # Tools (LancerEdit, InterfaceEdit)
│   └── ...
├── extern/                  # Git submodules (dependencies)
├── deps/                    # Binary dependencies (OpenAL)
├── scripts/                 # Build scripts
├── uixml/                   # XML+Lua UI
├── docs/                    # Documentation
├── Discovery Freelancer 4.86.0/  # Discovery mod data
├── CMakeLists.txt           # Native C/C++ components build
├── LibreLancer.sln          # Visual Studio solution
├── build.sh / build.ps1     # Full build scripts
└── shell.nix                # Nix dev environment
```

## License

**Project Sirius** and **LibreLancer** are distributed under the MIT license.

```
Copyright (c) Callum McGing, Librelancer Contributors 2013-2026
```

**Discovery Freelancer 4.86** is a mod created by the Discovery Development Team and the Discovery Gaming Community. Discovery Freelancer is a free modification and does not belong to the authors of Project Sirius.

**Freelancer** is a registered trademark of Microsoft Corporation. Project Sirius is not affiliated with Microsoft or Digital Anvil.

## Acknowledgements

- **[LibreLancer](https://librelancer.net/)** — Callum McGing and all contributors to the open-source engine
- **[Discovery Gaming Community](https://discoverygc.com/)** — creators of the Discovery Freelancer mod
- **[Digital Anvil / Microsoft](https://en.wikipedia.org/wiki/Freelancer_(video_game))** — creators of the original game

---

# Project Sirius (русская версия)

> *Для Freelancer — как Black Mesa для Half-Life.*

**Project Sirius** — это современный ремейк культового космического симулятора **Freelancer** (2003, Digital Anvil / Microsoft). Полностью переписанный с нуля движок на C# с рендерером на **Vulkan**, совместимый с оригинальными форматами игры.

За основу взят движок **[LibreLancer](https://github.com/Librelancer/Librelancer)** и мод **Discovery Freelancer 4.86** — крупнейшая пользовательская модификация с многолетней историей разработки.

## Содержание

- [Что такое Project Sirius?](#что-такое-project-sirius)
- [Что такое Discovery?](#что-такое-discovery)
- [Текущее состояние](#текущее-состояние)
- [Сборка и запуск](#сборка-и-запуск)
- [Структура проекта](#структура-проекта)
- [Лицензия](#лицензия)

## Что такое Project Sirius?

Project Sirius — это современный движок для Freelancer, ставящий целью:

- **Нативную кроссплатформенность** — Windows, Linux, macOS
- **Современную графику** — рендерер на Vulkan, PBR, HDR-пайплайн (filmic/ACES-тонмаппинг, bloom, световые лучи, FXAA), каскадные карты теней
- **Полную совместимость** с оригинальными форматами Freelancer (UTF-модели, Thorn-скрипты, INI-данные, ALE-эффекты)
- **Сетевую игру** — выделенный сервер с AI, NPC и скриптами
- **Инструменты моддинга** — встроенные редакторы моделей, частиц, интерфейса, скриптов

### Технологический стек

| Компонент | Технология |
|-----------|-----------|
| Язык | C# 13 / .NET 10.0 |
| Графика | **Vulkan** (по умолчанию), PBR-шейдеры (HLSL → SPIR-V) |
| Окно/ввод | SDL2 / SDL3 |
| Аудио | OpenAL Soft |
| Физика | BepuPhysics2 |
| Сеть | LiteNetLib (UDP) |
| UI | XML+Lua (игровой), Dear ImGui (редакторы) |
| Скриптинг | Thorn (катсцены), WattleScript / Lua (интерфейс) |

## Что такое Discovery?

**Discovery Freelancer 4.86** — крупнейший и старейший мод для Freelancer, разрабатываемый сообществом **Discovery Gaming Community** с 2005 года.

Версия 4.86 добавляет к оригинальной игре:

- **152 системы** (вместо 48) — включая новый дом Gallia
- **139 фракций**, **617 баз**, **290+ кораблей**
- **275+ товаров** с faction-based экономикой
- Десятки новых типов оборудования: Cloaking Device, Jump Drive, Docking Module
- Масштабную сюжетную линию (818 A.S., война Сириуса)
- Тысячи новых инфокарт, диалогов, миссий

Project Sirius использует Discovery 4.86 как базовый набор игровых данных — движок парсит оригинальные форматы `.ini` напрямую.

## Текущее состояние

Project Sirius находится в активной разработке. Движок уже поддерживает:

- **Vulkan-бэкенд включён по умолчанию** (проверен против OpenGL-пути SSIM-сравнением скриншотов)
- Загрузку и отрисовку игровых миров (системы, планеты, астероиды)
- PBR-рендеринг кораблей и окружения с HDR-пайплайном постобработки
- Каскадные карты теней от солнца системы и локальные тени прожекторов
- Базовую физику и сетевое взаимодействие
- Редакторы контента (модели, частицы, UI)

В процессе адаптации под Discovery 4.86:

- Парсинг `commodities_per_faction.ini` (faction-based экономика)
- Shield damage type modifiers из `weaponmoddb.ini`
- Поддержка Jump Drive, Docking Module, Survey Module
- Оптимизация под возросший объём данных (x5-x10 от ваниллы)
- Серверная логика: player-owned bases, cloaking, cruise speeds

## Сборка и запуск

### Требования (Linux)

- .NET 10.0 SDK (x86-64 или arm64)
- SDL2 (или SDL3)
- OpenAL Soft
- GCC/G++ и CMake 3.15+
- GTK3 (включает FreeType, Pango, Cairo)
- GPU и драйверы с поддержкой Vulkan

### Сборка

```bash
# Клонирование с сабмодулями
git clone --recursive https://github.com/davidichalfyorov-wq/project-sirius.git
cd project-sirius

# Сборка движка
./build.sh
```

### Запуск

```bash
dotnet run --project src/lancer
```

При первом запуске укажите путь к установке Freelancer (или к папке `Discovery Freelancer 4.86.0` в репозитории).

### Nix (альтернативный способ)

```bash
nix-shell --pure
./build.sh
```

## Структура проекта

```
Project Sirius/
├── src/                     # Исходный код C#
│   ├── LibreLancer/         # Основной игровой движок
│   ├── LibreLancer.Base/    # Базовые библиотеки (платформа, графика, ввод)
│   ├── LibreLancer.Data/    # Парсинг игровых данных Freelancer
│   ├── LibreLancer.Media/   # Аудио (OpenAL)
│   ├── LibreLancer.Physics/ # Физика (BepuPhysics2)
│   ├── LibreLancer.Thorn/   # Thorn-скриптинг (катсцены)
│   ├── lancer/              # Клиент игры
│   ├── LLServer/            # Выделенный сервер
│   ├── Editor/              # Инструменты (LancerEdit, InterfaceEdit)
│   └── ...
├── extern/                  # Git-сабмодули зависимостей
├── deps/                    # Бинарные зависимости (OpenAL)
├── scripts/                 # Скрипты сборки
├── uixml/                   # UI на XML+Lua
├── docs/                    # Документация
├── Discovery Freelancer 4.86.0/  # Данные мода Discovery
├── CMakeLists.txt           # Сборка нативных C/C++ компонентов
├── LibreLancer.sln          # Решение Visual Studio
├── build.sh / build.ps1     # Скрипты полной сборки
└── shell.nix                # Nix dev environment
```

## Лицензия

**Project Sirius** и **LibreLancer** распространяются под лицензией MIT.

```
Copyright (c) Callum McGing, Librelancer Contributors 2013-2026
```

**Discovery Freelancer 4.86** — мод, созданный Discovery Development Team и Discovery Gaming Community. Discovery Freelancer является бесплатной модификацией и не принадлежит авторам Project Sirius.

**Freelancer** — зарегистрированная торговая марка Microsoft Corporation. Project Sirius не связан с Microsoft или Digital Anvil.

## Благодарности

- **[LibreLancer](https://librelancer.net/)** — Callum McGing и все контрибьюторы опенсорсного движка
- **[Discovery Gaming Community](https://discoverygc.com/)** — создатели мода Discovery Freelancer
- **[Digital Anvil / Microsoft](https://en.wikipedia.org/wiki/Freelancer_(video_game))** — создатели оригинальной игры
