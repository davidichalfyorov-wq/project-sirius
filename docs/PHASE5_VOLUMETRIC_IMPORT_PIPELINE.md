# Phase 5 Volumetric Import Pipeline

OpenVDB is part of the Phase 5 authoring pipeline, not an immediate runtime
dependency for the game renderer.

The runtime path remains:

```text
legacy Nebula/Zone data
  -> NebulaVolumeProfile
  -> froxel density/light/integrated volumes
  -> optional HDR composite
```

The offline art/import path should be added later as a separate, feature-gated
tooling layer:

```text
Blender/Houdini/OpenVDB volume
  -> validated import tool
  -> normalized density metadata
  -> compressed engine volume/noise asset
  -> NebulaVolumeProfile override
  -> same runtime froxel pipeline
```

Rules for the OpenVDB layer:

- Keep OpenVDB libraries out of the core game runtime until there is a measured
  need for direct runtime loading.
- Import tools may depend on OpenVDB; generated engine assets should not require
  OpenVDB to be present on player machines.
- Imported volumes must preserve existing Freelancer nebula zone positions and
  bounds. They may add internal density/detail, but must not move canonical
  nebula placement.
- Manifest files may describe density-space bounds, but must not contain world
  placement overrides such as `world_position_meters`, `offset_meters`,
  `rotation_degrees`, or custom transform matrices. The renderer gets placement
  exclusively from the original Freelancer zone.
- Every imported volume needs source metadata: source DCC, source file, scale,
  density range, axis convention, license/owner, and profile nickname.
- Import manifests are rejected if `source` or `license` is missing. This keeps
  generated nebula volumes reviewable before they enter the engine asset cache.
- Import manifests are also rejected if `source_file` or `content_hash` is
  missing. `content_hash` must be `sha256:<64 hex>` or `blake3:<64 hex>` so
  dense OpenVDB exports can be reproduced, reviewed, and invalidated without
  guessing which DCC cache produced them.
- Runtime import plans require explicit canonical identity locks. A sidecar can
  be parsed for diagnostics without them, but it cannot be bound to an active
  `NebulaVolumeProfile` unless `canonical_nebula` is present, and
  `canonical_system` is present whenever the runtime knows the active system.
  This prevents a valid density cache from being accidentally applied to the
  wrong Freelancer nebula zone.
- `data` and `source_file` must be portable project-relative paths. Absolute
  paths, Windows drive paths, backslashes, empty segments, `.` segments, and
  `..` traversal are rejected so Blender/Houdini exports remain reproducible and
  cannot point outside the reviewed asset tree.
- The initial runtime metadata bridge accepts at most 256^3 dense voxels before
  compression/cache conversion. Larger authored OpenVDB files should be reduced
  offline or split into reviewed tiles before the game can consume them.
- The fallback path must always be procedural `NebulaVolumeProfile` density, so
  missing imported assets cannot break a system load.
- RenderDoc/Vulkan debug names should stay identical between procedural and
  imported density sources: `vol_nebula_density`, `vol_nebula_light`,
  `vol_nebula_integrate`, `vol_nebula_composite`.

The same portability rule applies to optional blue-noise/STBN jitter sidecars:
the sidecar manifest itself may be selected by a developer path, but its
`data`/`path` payload entry must stay project-relative and must not use absolute
paths, Windows drive prefixes, backslashes, `.` segments, or `..` traversal.
This keeps temporal-noise assets reproducible in CI and on other developer
machines.

Initial OpenVDB PR scope should be importer-only:

```text
PR-OVDB-1:
  - CLI/import tool skeleton
  - manifest parser
  - bounds/scale validation
  - density min/max normalization
  - profile metadata bridge to the existing `NebulaVolumeProfile`
  - no runtime renderer changes

PR-OVDB-2:
  - engine volume asset/cache format
  - profile metadata bridge
  - debug preview/export reports
  - still no mandatory runtime dependency

PR-OVDB-3:
  - optional runtime sampling path for imported density assets
  - procedural fallback retained
  - golden Li01/Badlands comparison
```

The initial sidecar manifest is deliberately plain text so it can be emitted by
Blender, Houdini, or an OpenVDB conversion script without linking OpenVDB into
the game. A canonical Li01 export should look like:

```ini
data = li01_badlands_density.vdb
grid = density
profile = li01_badlands
width = 128
height = 96
depth = 64
voxel_size_meters = 250
origin_meters = 0, 0, 0
scale_meters = 1, 1, 1
density_min = 0.02
density_max = 0.85
density_multiplier = 0.75
axis = z_up
bounds = zone_local
placement = zone_locked
canonical_system = Li01
canonical_nebula = li01_badlands
source = blender_openvdb_export
source_file = art/li01/badlands_density.blend
license = project-owned
content_hash = sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
preserve_zone_transform = true
```

`canonical_system`, `canonical_nebula`, `placement = zone_locked`, and
`preserve_zone_transform = true` are safety fields. They let the importer reject
authored volumes that would move Freelancer's original nebula placement.
`bounds = zone_local` means the authored density is fitted inside the canonical
zone transform instead of carrying its own world transform. `density_min` and
`density_max` define the authored scalar range; the runtime bridge computes a
normalized density scale/bias from those values and keeps procedural density as
the fallback when the imported asset is unavailable.
