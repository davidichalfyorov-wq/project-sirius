# TEST_REPORT — Project Sirius Discovery 4.86 compatibility pass

Date: 2026-06-09

## Environment

- Work directory: `/mnt/data/project_sirius_work/src_bundle`
- Discovery text data: `/mnt/data/project_sirius_work/discovery_text/Discovery Freelancer 4.86.0`
- Binary Discovery assets are not present in the provided archive.
- `.NET SDK / dotnet` is not installed in this execution environment.

## Required documentation/data review

- `PROJECT_SIRIUS_ARCHITECTURE.md`: read before code changes.
- Discovery `EXE/freelancer.ini`: audited.
- `discovery_data_text.tar.gz`: extracted and audited.

## Discovery data audit

### `EXE/freelancer.ini`

- `[Data]` entries found: 97.
- All valued `[Data]` paths exist in the provided text archive.
- Discovery additions confirmed:
  - `ships = ships\\rtc_shiparch.ini`
  - 16 fuse files under `fx\\fuse*.ini`
  - `loadouts_special.ini`
  - `loadouts_utility.ini`
  - `WeaponModDB = equipment\\WeaponModDB.ini`
- `commodities_per_faction.ini` exists under `DATA/EQUIPMENT/` but is not referenced by `[Data]`, so the loader now detects it as a Discovery fallback.
- `[Resources]` includes Windows DLL resources in the original `freelancer.ini`; a `librelancer.ini` with an empty `[Resources]` section was generated to avoid loading Windows DLLs on Linux.

### Discovery-specific files

- `commodities_per_faction.ini`:
  - `[FactionGood]` sections: 97
  - `MarketGood` lines: 1437
  - observed min/max tuple set: `(0, 0)`
- `weaponmoddb.ini`:
  - `[WeaponType]` sections: 21
  - `shield_mod` lines: 189

### Referenced/dependent graph audit

Command:

```bash
python3 audit_discovery_full.py
```

Result summary:

- Main referenced data groups:
  - unknown sections: none
  - unknown entries: none
- Dependent universe graph:
  - systems: 155
  - bases: 772
  - rooms: 1552
  - nebulas: 168
  - asteroid fields: 511
  - encounter files: 1781
  - missing dependent paths: 0
  - unknown dependent sections: none
  - unknown dependent entries: none

The full output is saved outside the source tree as `audit_discovery_full_output.txt`.

## Build/test attempts

### `dotnet build src/lancer/lancer.csproj`

Command attempted from `/mnt/data/project_sirius_work/src_bundle`:

```bash
dotnet build src/lancer/lancer.csproj
```

Result: not executed successfully in this container because `dotnet` is unavailable.

Captured output:

```text
bash: line 2: dotnet: command not found
```


### `./build.sh`

Command attempted from `/mnt/data/project_sirius_work/src_bundle`:

```bash
./build.sh
```

Result: failed during dependency check because `dotnet` is unavailable.

Captured output:

```text
Cannot find dotnet on PATH
ERROR: Dependency check failed.
```

## Functional runtime tests

Not verified in this environment:

- `./build.sh`
- launching `build/lancer`
- loading LI01/New York in the running client
- rendering ships/bases/asteroids
- flight, shooting, docking
- live trading UI/server transactions
- dedicated server accepting clients

Reasons:

- no `.NET SDK / dotnet` in the container;
- provided archive contains text data only, not the 4.4 GB binary asset set required for a real client run.

## Current status

Implemented and audited:

- Discovery parser compatibility for the audited data groups;
- `WeaponModDB` shield modifier loading and server-side damage integration;
- `commodities_per_faction.ini` parser and runtime profile wiring;
- Discovery fuse-section compatibility;
- Discovery shiparch/system/encounter/asteroid schema additions;
- Linux-safe `librelancer.ini` generation.

Remaining before declaring full Discovery gameplay support:

- run a real `dotnet build` on a machine with .NET 10 SDK;
- run the client/server with the complete Discovery binary assets;
- implement full C# replacements for the FLHook gameplay plugins listed in the technical assignment.
