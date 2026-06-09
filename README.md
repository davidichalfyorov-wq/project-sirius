# Project Sirius

> *Для Freelancer — как Black Mesa для Half-Life.*

**Project Sirius** — это современный ремейк культового космического симулятора **Freelancer** (2003, Digital Anvil / Microsoft). Полностью переписанный с нуля движок на C# и OpenGL, совместимый с оригинальными форматами игры.

За основу взят движок **[LibreLancer](https://github.com/Librelancer/Librelancer)** и мод **Discovery Freelancer 4.86** — крупнейшая пользовательская модификация с многолетней историей разработки.

---

## Содержание

- [Что такое Project Sirius?](#что-такое-project-sirius)
- [Что такое Discovery?](#что-такое-discovery)
- [Текущее состояние](#текущее-состояние)
- [Сборка и запуск](#сборка-и-запуск)
- [Структура проекта](#структура-проекта)
- [Лицензия](#лицензия)

---

## Что такое Project Sirius?

Project Sirius — это современный движок для Freelancer, ставящий целью:

- **Нативную кроссплатформенность** — Windows, Linux, macOS
- **Современную графику** — PBR-рендеринг, OpenGL 3.1+
- **Полную совместимость** с оригинальными форматами Freelancer (UTF-модели, Thorn-скрипты, INI-данные, ALE-эффекты)
- **Сетевую игру** — выделенный сервер с AI, NPC и скриптами
- **Инструменты моддинга** — встроенные редакторы моделей, частиц, интерфейса, скриптов

### Технологический стек

| Компонент | Технология |
|-----------|-----------|
| Язык | C# 13 / .NET 10.0 |
| Графика | OpenGL 3.1+, PBR-шейдеры (HLSL → SPIR-V) |
| Окно/ввод | SDL2 / SDL3 |
| Аудио | OpenAL Soft |
| Физика | BepuPhysics2 |
| Сеть | LiteNetLib (UDP) |
| UI | XML+Lua (игровой), Dear ImGui (редакторы) |
| Скриптинг | Thorn (катсцены), WattleScript / Lua (интерфейс) |

---

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

---

## Текущее состояние

Project Sirius находится в активной разработке. Движок LibreLancer уже поддерживает:

- Загрузку и отрисовку игровых миров (системы, планеты, астероиды)
- PBR-рендеринг космических кораблей и окружения
- Базовую физику и сетевое взаимодействие
- Редакторы контента (модели, частицы, UI)

В процессе адаптации под Discovery 4.86:

- Парсинг `commodities_per_faction.ini` (faction-based экономика)
- Shield damage type modifiers из `weaponmoddb.ini`
- Поддержка Jump Drive, Docking Module, Survey Module
- Оптимизация под возросший объём данных (x5-x10 от ваниллы)
- Серверная логика: player-owned bases, cloaking, cruise speeds

---

## Сборка и запуск

### Требования (Linux)

- .NET 10.0 SDK (x86-64 или arm64)
- SDL2 (или SDL3)
- OpenAL Soft
- GCC/G++ и CMake 3.15+
- GTK3 (включает FreeType, Pango, Cairo)

### Сборка

```bash
# Клонирование с сабмодулями
git clone --recursive https://github.com/yourname/project-sirius.git
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

---

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

---

## Лицензия

**Project Sirius** и **LibreLancer** распространяются под лицензией MIT.

```
Copyright (c) Callum McGing, Librelancer Contributors 2013-2026
```

**Discovery Freelancer 4.86** — мод, созданный Discovery Development Team и Discovery Gaming Community. Discovery Freelancer является бесплатной модификацией и не принадлежит авторам Project Sirius.

**Freelancer** — зарегистрированная торговая марка Microsoft Corporation. Project Sirius не связан с Microsoft или Digital Anvil.

---

## Благодарности

- **[LibreLancer](https://librelancer.net/)** — Callum McGing и все контрибьюторы опенсорсного движка
- **[Discovery Gaming Community](https://discoverygc.com/)** — создатели мода Discovery Freelancer
- **[Digital Anvil / Microsoft](https://en.wikipedia.org/wiki/Freelancer_(video_game))** — создатели оригинальной игры
