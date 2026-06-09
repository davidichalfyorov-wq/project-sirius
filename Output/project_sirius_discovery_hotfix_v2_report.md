# Project Sirius / Discovery 4.86 hotfix v2

Ветка исходников: `sirius-discovery-hotfix`.

Проверено в контейнере:

```bash
git diff --check
```

`dotnet build` в этой среде не выполнялся: в контейнере нет установленного `dotnet`/C# compiler. Все изменения сделаны по исходникам и по приложенному runtime-логу.

## Главное, что исправлено

### 1. Строки и infocards на Linux

`librelancer.ini` больше не оставляет движок без Windows resource DLL. Для Linux-сборки добавлена автоматическая загрузка ресурсных DLL:

- сначала из `EXE/freelancer.ini`, если он есть;
- затем через стабильный fallback-список vanilla + Discovery DLL;
- поддерживаются DLL как в `EXE/`, так и в корне игры.

Также `InfocardManager` теперь делает fallback-поиск по локальному IDS внутри всех DLL, если точный глобальный индекс DLL не совпал.

Ожидаемый эффект: главное меню, pause UI, map object names, goods/equipment names, infocards, news, rumors и `IDS??` должны начать подтягиваться из `resources.dll`, `infocard.dll`, `Discovery.dll`, `DsyAddition.dll` и связанных DLL.

### 2. PE resource parser стал устойчивее

`ResourceDll` теперь:

- выбирает доступную locale-ветку ресурса, предпочитая neutral/en-US;
- не падает на дубликатах string/infocard resources;
- безопаснее читает RT_STRING блоки;
- пропускает malformed resource entries вместо hard crash;
- безопасно читает RT_VERSION, RT_DIALOG, RT_MENU.

### 3. Discovery material types

Обобщён распознаватель basic material token-комбинаций. Теперь проходят типы вроде:

- `DcDtBt`
- `DcDtBtEc`
- `DcDtEcEt`
- `DcDtBtOcOt`
- другие комбинации из `Dc/Dt/Ec/Et/Oc/Ot/Bt/Nt/Mt/Rt/Nm/Two`

`BasicMaterial` больше не падает на сочетании `Bt + Et`; renderer выбирает Et во второй texture slot, иначе Bt. Unsupported material в `Initialize()` теперь уходит в fallback material вместо `NotImplementedException`.

### 4. NPC на базах и в комнатах

NPC теперь добавляются не только когда у комнаты нет THN script, но и после завершения room start/land script. Это должно исправить пустые бары, commodity dealer, equipment dealer и ship dealer после проигрывания сцен.

Также `Base` хранит полный список NPC базы, а synthetic fixed NPC добавляются туда, чтобы UI/interaction fallback не терял их полностью.

### 5. THN null renderer и engine/exhaust/fire PSys

Для THN PSys:

- engine/exhaust/fire PSys, привязанные к player ship, теперь распознаются не только по `PlayerShipEngines`, но и по Discovery-style именам вроде `Ship_l_fighter_1_engine`, `*_exhaust_*`, `*_fire_*`;
- optional PSys без renderer больше не логируются как fatal error;
- эффекты ищутся через общий resolver `Effects + VisEffects`.

### 6. Эффекты двигателей, оружия, снарядов, fuses

Добавлен resolver `ResolveFx`/`ResolveEffect`, который ищет эффект и в `Effects`, и в `VisEffects`. Переведены на него:

- engine trails/flames;
- munition const effects;
- projectile hit/travel effects;
- gun flash effects;
- thruster particles;
- cloak in/out effects;
- tradelane active effects;
- explosion effects;
- fuse effects;
- generic effect equipment.

### 7. Mission actions: Act_LockDock, Act_SetRep, Act_GcsClamp

Реализованы базовые server-side действия:

- `Act_LockDock`: добавляет/снимает locked dockable object в `MPlayer.LockedGates`, отправляет клиенту updated allowed docking;
- server-side `RequestDock` теперь запрещает docking к locked объектам и учитывает `CanDock`/`CanTl` exceptions;
- client-side docking UI также учитывает locked dockables;
- `Act_SetRep`: меняет репутацию игрока к фракции через character transaction и отправляет обновление клиенту;
- `Act_GcsClamp`: принят как безопасный no-op без warning spam.

Дополнительно исправлена ошибка сериализации `AllowedDocking`: для `TlExceptions` теперь пишется правильный count, а не `DockExceptions.Count`.

### 8. Discovery hp_type compatibility

Добавлены fallback hardpoint types:

- `hp_gun`
- `hp_turret`
- `hp_cargo_pod`

Это убирает ошибки `Ship: Unrecognised hp_type ...` для transport/train/mission ships.

### 9. INI compatibility по логу

Смягчены/расширены парсеры для Discovery INI:

- `fuse.ini`: `at_t` теперь принимает лишние компоненты и использует первое значение;
- `destroy_group`: приняты `separable`, `LODranges`, `dmg_hp`, `dmg_obj`;
- добавлена секция fuse `make_invincible`;
- nebula/light/cloud/band fields принимают extra/empty components: `color_curve`, `bit_radius_random_variation`, `move_bit_percent`, `equator_bias`, `sun_burnthrough_*`, `puff_*`, `lightning_*`, `duration`, `gap`, `fog_far`, `shell_scalar`, `texture_aspect`.

### 10. Убраны remaining NotImplementedException в runtime-пути

Заменены runtime `NotImplementedException` на безопасные no-op/fallback:

- `LooseConstruct.Update()`;
- `FixConstruct.Update()`;
- unsupported ALE nodes;
- DFM vertices with >4 bone weights now keep first four and normalize;
- unsupported material Initialize fallback.

### 11. Дополнительные устойчивости

- `CTradelaneComponent` больше не падает при activate/deactivate, если lane renderer не создался;
- `ModelFile` игнорирует Discovery/old-exporter `WMeshWire` node вместо model error;
- `Cnd_JumpgateAct` и `Cnd_CmpToPlane` больше не бросают `NotImplementedException`, а сохраняют аргументы для round-trip.

## Как применить

### Вариант A: поверх уже применённого первого hotfix

```bash
cd project-sirius
git checkout sirius-discovery-hotfix
git apply /path/to/project_sirius_discovery_hotfix_v2_incremental.patch
dotnet build -c Release src/lancer/lancer.csproj
```

### Вариант B: полный source archive

Распаковать `project_sirius_discovery_hotfix_v2_full_source.tar.gz`. Внутри архивная папка `sirius_repo/` уже содержит исходники с первым hotfix и hotfix v2.

### Вариант C: combined patch от исходного состояния архива

```bash
cd project-sirius
git apply /path/to/project_sirius_discovery_hotfix_v2_combined.patch
dotnet build -c Release src/lancer/lancer.csproj
```

## Runtime-примечание

Для полного исправления текстов в игре DLL-файлы ресурсов должны реально присутствовать в VFS, например:

```text
EXE/resources.dll
EXE/infocard.dll
EXE/nameresources.dll
EXE/equipresources.dll
EXE/goodsresources.dll
EXE/Discovery.dll
EXE/DsyAddition.dll
```

или их root-эквиваленты. Код теперь умеет читать эти PE DLL на Linux без Wine.
