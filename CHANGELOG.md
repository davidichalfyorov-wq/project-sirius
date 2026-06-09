# CHANGELOG — Project Sirius Discovery 4.86 compatibility pass

## 2026-06-09

### Discovery data loading
- Added parsing for Discovery `WeaponModDB = equipment\\WeaponModDB.ini` from `[Data]` in `EXE/freelancer.ini`.
- Added fallback loading for `DATA/EQUIPMENT/weaponmoddb.ini` when the key is absent.
- Added parser for `commodities_per_faction.ini`:
  - `[FactionGood]`
  - `faction = ...`
  - multiline `MarketGood = good, min, max`
- Added fallback loading for `DATA/EQUIPMENT/commodities_per_faction.ini`, because Discovery 4.86 ships the file but does not reference it from `[Data]`.
- Added runtime `FactionCommodityProfile` storage and linked profiles to `Faction` plus `BaseSoldGood` records.
- Added Discovery-safe `librelancer.ini` for the Discovery root. It has an empty `[Resources]` section and direct `[Data]` paths, so Linux runs do not attempt to load `Discovery.dll`, `DsyAddition.dll`, or other Windows DLL resources.

### Weapon/shield damage compatibility
- Added runtime shield modifier storage to `MunitionEquip` and `MissileEquip`.
- Applied `WeaponModDB` shield modifiers during equipment initialization.
- Integrated shield damage modifiers into server projectile damage.
- Integrated shield damage modifiers into missile explosion damage per affected target.

### Discovery-specific equipment recognition
- Added `DiscoverySpecialEquipmentKind` with `JumpDrive`, `DockingModule`, and `SurveyModule`.
- Classified Discovery jump drives, survey modules, and docking modules in `ShieldEquipment` based on their Discovery nicknames. Discovery encodes these items as `[ShieldGenerator]` entries in `st_equip.ini`.

### Fuse compatibility
- Added parser support for Discovery/large-ship fuse child sections:
  - `[dump_cargo]`
  - `[damage_root]`
  - `[damage_group]`
  - `[tumble]`
- Added `particles` alias support for `[start_effect]` and made runtime fuse effect spawning skip empty effect names safely.

### Additional schema compatibility
- Added Discovery/vanilla-extended fields found during the audit:
  - `CollisionGroup.explosion_resistance`
  - `Armor.category`
  - `Munition.owner_safe_time`
  - `NewCharPilot.thumb`
  - `LightSource.color_curve`
  - `SystemInfo.name`
  - `EncounterFormation.longevity`
  - `Asteroid.explosion_impulse`
  - `Asteroid.phantom_physics`
  - `DynamicAsteroid.particle_effect`
  - `Simple.MinSpecLOD`
- Added shiparch compatibility fields/handlers used by Discovery ship/solar-like ship entries:
  - `solar_radius`
  - `destructible`
  - `shape_name`
  - `loadout`
  - `distance_render`
  - `docking_camera`
  - multiline `docking_sphere`

### Audit result
- Audited Discovery `EXE/freelancer.ini`: 97 `[Data]` entries.
- Audited main referenced data groups and dependent universe graph files.
- After the changes, the audit reports no unknown sections or entries in the covered Discovery schema groups.

### Not completed in this pass
- Full FLHook replacement gameplay systems are not implemented here:
  - player-owned bases,
  - full jump-drive activation/gameplay,
  - full cloak detection/energy gameplay,
  - player slash commands,
  - mine control,
  - complete dynamic faction economy simulation.
- The changes add parser/runtime foundations and damage/economy data wiring needed before those gameplay systems can be implemented safely.
