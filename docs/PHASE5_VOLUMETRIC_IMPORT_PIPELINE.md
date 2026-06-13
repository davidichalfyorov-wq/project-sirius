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
- Every imported volume needs source metadata: source DCC, source file, scale,
  density range, axis convention, license/owner, and profile nickname.
- The fallback path must always be procedural `NebulaVolumeProfile` density, so
  missing imported assets cannot break a system load.
- RenderDoc/Vulkan debug names should stay identical between procedural and
  imported density sources: `vol_nebula_density`, `vol_nebula_light`,
  `vol_nebula_integrate`, `vol_nebula_composite`.

Initial OpenVDB PR scope should be importer-only:

```text
PR-OVDB-1:
  - CLI/import tool skeleton
  - manifest parser
  - bounds/scale validation
  - density min/max normalization
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
