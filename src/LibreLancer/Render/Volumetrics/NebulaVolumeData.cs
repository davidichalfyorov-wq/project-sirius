using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using LibreLancer.Data.GameData.World;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Collects the system's nebula and exclusion zones into the GPU layout
/// the froxel passes consume (track V1). Density profiles are mapped at
/// runtime from existing zone/nebula data - the Discovery INI format is
/// never extended (master-PDF rule).
/// </summary>
public static class NebulaVolumeData
{
    public const int MaxZones = 16;

    // Mirrors VolumeZone in includes/VolumeCommon.hlsl (112 bytes).
    public const int GpuZoneSize = 112;

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = GpuZoneSize)]
    public struct GpuZone
    {
        public Vector4 InvCol0;
        public Vector4 InvCol1;
        public Vector4 InvCol2;
        public Vector4 ColorDensity;
        public Vector4 Params;
        public Vector4 NoiseProfile;  // x scale 1/m, y coverage, z detail strength, w drift m/s
        public Vector4 BoundsSphere;  // xyz world centre, w radius (distant-march intervals)
    }

    /// <summary>
    /// Fills <paramref name="zones"/> (camera-nearest first, capped at
    /// MaxZones) and returns the count. Exclusion zones of a written
    /// nebula are appended as density cuts.
    /// </summary>
    public static int Collect(List<NebulaRenderer> nebulae, Vector3 cameraPos, GpuZone[] zones)
    {
        var count = 0;
        // Nearest nebulae first: the cap should drop the far ones.
        sortScratch.Clear();
        foreach (var nr in nebulae)
        {
            var zone = nr.Nebula?.Zone;
            if (zone == null)
            {
                continue;
            }
            sortScratch.Add((Vector3.Distance(cameraPos, zone.Position), nr));
        }
        sortScratch.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        foreach (var (_, nr) in sortScratch)
        {
            var nebula = nr.Nebula;
            var zone = nebula.Zone!;
            if (count >= MaxZones)
            {
                break;
            }
            if (!TryBuildZone(zone, out var gpu))
            {
                continue;
            }

            // Runtime density profile. The legacy linear fog kills ALL
            // visibility at FogRange.Y; the volumetric look wants cloud
            // banks readable a few kilometres deep instead, so the core
            // extinction is softer (2.5/far) and the noise carves it into
            // dense hearts and clear lanes.
            var fogFar = MathF.Max(nebula.FogRange.Y, 200f);
            var colour = ColorSpace.SrgbToLinear(nebula.FogColor);
            // The authored FogColor was a fullscreen TINT for the legacy
            // path - dark by design. A scattering medium wants a bright
            // albedo: keep the authored hue, lift it to cloud reflectivity.
            var maxComp = MathF.Max(colour.R, MathF.Max(colour.G, MathF.Max(colour.B, 1e-3f)));
            // Per feedback: nebulae read dark and moody, not milky -
            // lift to mid albedo only.
            var albedoScale = 0.5f / maxComp;
            gpu.ColorDensity = new Vector4(
                colour.R * albedoScale, colour.G * albedoScale, colour.B * albedoScale,
                2.5f / fogFar);
            gpu.Params.Z = 0;
            gpu.NoiseProfile = NoiseProfileFor(zone);
            zones[count++] = gpu;

            if (nebula.ExclusionZones != null)
            {
                foreach (var exclusion in nebula.ExclusionZones)
                {
                    if (count >= MaxZones)
                    {
                        break;
                    }
                    if (exclusion.Zone == null || !TryBuildZone(exclusion.Zone, out var cut))
                    {
                        continue;
                    }
                    cut.ColorDensity = Vector4.Zero;
                    cut.Params.Z = 1;
                    // Authored exclusion edges are razor thin (vanilla used
                    // shell meshes to hide them) - hard analytic walls read
                    // as facets in the volume. Force a wide soft band.
                    cut.Params.Y = MathF.Max(cut.Params.Y, 0.45f);
                    zones[count++] = cut;
                }
            }
        }
        return count;
    }

    private static readonly List<(float Distance, NebulaRenderer Renderer)> sortScratch = new();

    /// <summary>True when the camera is inside (or near) any nebula zone -
    /// the froxel early-out. Bounds are inflated by the edge band.</summary>
    public static bool AnyVolumeNear(List<NebulaRenderer> nebulae, Vector3 cameraPos, float inflate)
    {
        foreach (var nr in nebulae)
        {
            var zone = nr.Nebula?.Zone;
            if (zone == null)
            {
                continue;
            }
            var radius = MathF.Max(zone.Size.X, MathF.Max(zone.Size.Y, zone.Size.Z)) + inflate;
            if (Vector3.DistanceSquared(cameraPos, zone.Position) <= radius * radius)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Runtime noise-profile mapping (master-PDF rule: never extend the
    /// Discovery INI - profiles derive from existing zone PropertyFlags).
    /// x: base noise scale (1/m), y: coverage, z: detail erosion strength,
    /// w: drift speed (m/s).
    /// </summary>
    private static Vector4 NoiseProfileFor(Zone zone)
    {
        var flags = zone.PropertyFlags;
        if ((flags & ZonePropFlags.Badlands) != 0)
        {
            // Dirty rolling banks: large features, moderate coverage.
            return new Vector4(1f / 5200f, 0.62f, 0.55f, 14f);
        }
        if ((flags & ZonePropFlags.GasPockets) != 0)
        {
            // Pocketed gas: smaller cells, patchier coverage.
            return new Vector4(1f / 2600f, 0.5f, 0.7f, 10f);
        }
        if ((flags & ZonePropFlags.Cloud) != 0)
        {
            // Soft cloud banks: big soft features, high coverage.
            return new Vector4(1f / 7000f, 0.72f, 0.4f, 8f);
        }
        if ((flags & (ZonePropFlags.Ice | ZonePropFlags.Crystal)) != 0)
        {
            // Crystalline haze: fine sharp wisps.
            return new Vector4(1f / 3200f, 0.55f, 0.8f, 6f);
        }
        // Generic nebula interior.
        return new Vector4(1f / 4500f, 0.6f, 0.5f, 10f);
    }

    private static bool TryBuildZone(Zone zone, out GpuZone gpu)
    {
        gpu = default;
        // Unit-local half extents per shape (Zone size semantics).
        Vector3 halfSize;
        float shape;
        switch (zone.Shape)
        {
            case ShapeKind.Sphere:
                halfSize = new Vector3(MathF.Max(zone.Size.X, 1));
                shape = 0;
                break;
            case ShapeKind.Ellipsoid:
                halfSize = Vector3.Max(zone.Size, new Vector3(1));
                shape = 0;
                break;
            case ShapeKind.Box:
                halfSize = Vector3.Max(zone.Size * 0.5f, new Vector3(1));
                shape = 1;
                break;
            case ShapeKind.Cylinder:
            case ShapeKind.Ring:
                // Bounding-ellipsoid approximation until V13 (no Discovery
                // nebula uses these as its primary volume).
                halfSize = new Vector3(
                    MathF.Max(zone.Size.X, 1),
                    MathF.Max(zone.Size.Y * 0.5f, 1),
                    MathF.Max(zone.Size.X, 1));
                shape = 0;
                break;
            default:
                return false;
        }

        var world = Matrix4x4.CreateScale(halfSize) * zone.RotationMatrix *
                    Matrix4x4.CreateTranslation(zone.Position);
        if (!Matrix4x4.Invert(world, out var inv))
        {
            return false;
        }
        // HLSL does local_i = dot(InvColI, (p,1)) - with System.Numerics'
        // row-vector convention (q = p * inv) that makes InvColI the I-th
        // COLUMN of the inverse.
        gpu.InvCol0 = new Vector4(inv.M11, inv.M21, inv.M31, inv.M41);
        gpu.InvCol1 = new Vector4(inv.M12, inv.M22, inv.M32, inv.M42);
        gpu.InvCol2 = new Vector4(inv.M13, inv.M23, inv.M33, inv.M43);
        gpu.Params = new Vector4(shape, MathF.Max(zone.EdgeFraction, 0.05f), 0, 0);
        var boundRadius = MathF.Max(halfSize.X, MathF.Max(halfSize.Y, halfSize.Z)) * 1.1f;
        gpu.BoundsSphere = new Vector4(zone.Position, boundRadius);
        return true;
    }
}
