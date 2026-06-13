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

    // Core extinction scale (1/m, divided by the zone fog range). Tuned so a
    // dense nebula reads as a cloud you fly through rather than a flat tint.
    private const float CoreExtinction = 0.2f;
    private const float ExclusionInfluenceRange = 18000f;

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
            // banks readable across the froxel range instead. Keep core
            // extinction soft enough for noise-carved voids to survive
            // front-to-back integration as dense hearts and clear lanes.
            var fogFar = MathF.Max(nebula.FogRange.Y, 200f);
            var colour = ColorSpace.SrgbToLinear(nebula.FogColor);
            // The authored FogColor was a fullscreen TINT for the legacy
            // path - dark by design. A scattering medium wants a bright
            // albedo: keep the authored hue, lift it to cloud reflectivity.
            var profile = RuntimeProfileFor(zone);
            var maxComp = MathF.Max(colour.R, MathF.Max(colour.G, MathF.Max(colour.B, 1e-3f)));
            // Per feedback: nebulae read dark and moody, not milky -
            // lift to profile-specific mid albedo only.
            var albedoScale = profile.Albedo / maxComp;
            // Core extinction per metre. The previous 0.1x factor made the
            // medium so thin (optical depth ~0.9 across the whole 14km froxel
            // range) that the eye averaged through it into a flat veil -
            // density structure could not read. A scattering cloud you are
            // INSIDE wants the near body opaque within a couple of km so the
            // big soft forms and carved lanes show; this matches the proven
            // dark-mood baseline while keeping the ship and HUD readable.
            gpu.ColorDensity = new Vector4(
                colour.R * albedoScale, colour.G * albedoScale, colour.B * albedoScale,
                (CoreExtinction * profile.Extinction) / fogFar);
            gpu.Params.Z = 0;
            // Domain-warp amount (Н1): bends the round Perlin-Worley blobs
            // into flowing banks. Free Params.w slot, so the GPU stride is
            // untouched.
            gpu.Params.W = profile.Warp;
            gpu.NoiseProfile = profile.Noise;
            zones[count++] = gpu;

            if (nebula.ExclusionZones != null)
            {
                foreach (var exclusion in nebula.ExclusionZones)
                {
                    if (count >= MaxZones)
                    {
                        break;
                    }
                    if (exclusion.Zone == null ||
                        !ShouldIncludeExclusion(exclusion.Zone, cameraPos) ||
                        !TryBuildZone(exclusion.Zone, out var cut))
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

    private static bool ShouldIncludeExclusion(Zone zone, Vector3 cameraPos)
    {
        var reach = ZoneBoundsRadius(zone) + ExclusionInfluenceRange;
        return Vector3.DistanceSquared(cameraPos, zone.Position) <= reach * reach;
    }

    private static float ZoneBoundsRadius(Zone zone)
    {
        return zone.Shape switch
        {
            ShapeKind.Box => MathF.Max(zone.Size.X, MathF.Max(zone.Size.Y, zone.Size.Z)) * 0.5f,
            ShapeKind.Cylinder or ShapeKind.Ring => MathF.Max(zone.Size.X, zone.Size.Y * 0.5f),
            _ => MathF.Max(zone.Size.X, MathF.Max(zone.Size.Y, zone.Size.Z))
        };
    }

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
    /// Analytic Beer transmittance from the camera toward the sun, using
    /// the same cloud-scale mean extinction as ZonesAnalyticSunTau. This
    /// is intentionally a scalar for V7: it feeds the existing sun/god-ray
    /// sprites without adding another full-screen march.
    /// </summary>
    public static float SunTransmittance(List<NebulaRenderer> nebulae,
        Vector3 cameraPos, Vector3 toSun, GpuZone[] scratch)
    {
        if (toSun.LengthSquared() < 1e-6f)
        {
            return 1f;
        }
        var zoneCount = Collect(nebulae, cameraPos, scratch);
        if (zoneCount == 0)
        {
            return 1f;
        }
        return SunTransmittance(scratch.AsSpan(0, zoneCount), cameraPos,
            Vector3.Normalize(toSun));
    }

    public static float SunTransmittance(ReadOnlySpan<GpuZone> zones,
        Vector3 cameraPos, Vector3 toSun)
    {
        var tau = 0f;
        var p = new Vector4(cameraPos, 1f);
        foreach (var z in zones)
        {
            if (z.Params.Z > 0.5f)
            {
                continue;
            }
            var q = new Vector3(
                Vector4.Dot(z.InvCol0, p),
                Vector4.Dot(z.InvCol1, p),
                Vector4.Dot(z.InvCol2, p));
            var s = new Vector3(
                Vector3.Dot(new Vector3(z.InvCol0.X, z.InvCol0.Y, z.InvCol0.Z), toSun),
                Vector3.Dot(new Vector3(z.InvCol1.X, z.InvCol1.Y, z.InvCol1.Z), toSun),
                Vector3.Dot(new Vector3(z.InvCol2.X, z.InvCol2.Y, z.InvCol2.Z), toSun));
            var a = Vector3.Dot(s, s);
            if (a < 1e-12f)
            {
                continue;
            }
            var b = Vector3.Dot(q, s);
            var c = Vector3.Dot(q, q) - 1f;
            var h = b * b - a * c;
            if (h <= 0)
            {
                continue;
            }
            var root = MathF.Sqrt(h);
            var t0 = (-b - root) / a;
            var t1 = (-b + root) / a;
            var insideLength = MathF.Max(t1, 0f) - MathF.Max(t0, 0f);
            if (insideLength <= 0)
            {
                continue;
            }
            var meanExt = z.ColorDensity.W * (z.NoiseProfile.Y * 0.4f + 0.05f);
            tau += meanExt * insideLength;
        }
        return Math.Clamp(MathF.Exp(-MathF.Min(tau, 20f)), 0f, 1f);
    }

    /// <summary>
    /// Runtime noise-profile mapping (master-PDF rule: never extend the
    /// Discovery INI - profiles derive from existing zone PropertyFlags).
    /// x: base noise scale (1/m), y: coverage, z: detail erosion strength,
    /// w: drift speed (m/s).
    /// </summary>
    private readonly record struct RuntimeProfile(
        Vector4 Noise, float Albedo, float Extinction, float Warp);

    private static RuntimeProfile RuntimeProfileFor(Zone zone)
    {
        var flags = zone.PropertyFlags;
        var nickname = zone.Nickname ?? "";
        if ((flags & ZonePropFlags.Badlands) != 0 ||
            nickname.Contains("badlands", StringComparison.OrdinalIgnoreCase))
        {
            // Dirty rolling banks: broad bodies with carved lanes.
            return new RuntimeProfile(new Vector4(1f / 6500f, 0.56f, 0.60f, 13f), 0.42f, 0.65f, 0.42f);
        }
        if ((flags & (ZonePropFlags.Ice | ZonePropFlags.Crystal)) != 0 ||
            nickname.Contains("ice", StringComparison.OrdinalIgnoreCase) ||
            nickname.Contains("li05", StringComparison.OrdinalIgnoreCase))
        {
            // Crystalline haze: sharp wisps with readable cold voids.
            return new RuntimeProfile(new Vector4(1f / 7000f, 0.54f, 0.55f, 5f), 0.46f, 0.48f, 0.18f);
        }
        if ((flags & ZonePropFlags.Nomad) != 0 ||
            nickname.Contains("nomad", StringComparison.OrdinalIgnoreCase))
        {
            // Alien clouds: slow large sheets with brighter local scattering.
            return new RuntimeProfile(new Vector4(1f / 6200f, 0.52f, 0.62f, 14f), 0.48f, 0.55f, 0.40f);
        }
        if (nickname.Contains("crow", StringComparison.OrdinalIgnoreCase))
        {
            // Crow storms: dark blue-violet, high erosion, visible holes.
            return new RuntimeProfile(new Vector4(1f / 6500f, 0.50f, 0.62f, 12f), 0.34f, 0.48f, 0.46f);
        }
        if (nickname.Contains("okha", StringComparison.OrdinalIgnoreCase))
        {
            // Edge nebulae: compact broken shelves with strong silhouettes.
            return new RuntimeProfile(new Vector4(1f / 5600f, 0.54f, 0.62f, 9f), 0.40f, 0.58f, 0.30f);
        }
        if (nickname.Contains("walker", StringComparison.OrdinalIgnoreCase))
        {
            // Walker clouds: smoky orange cells, denser than generic clouds.
            return new RuntimeProfile(new Vector4(1f / 5200f, 0.58f, 0.62f, 7f), 0.44f, 0.65f, 0.34f);
        }
        if ((flags & ZonePropFlags.GasPockets) != 0)
        {
            // Pocketed gas: smaller cells, patchier coverage.
            return new RuntimeProfile(new Vector4(1f / 4800f, 0.45f, 0.64f, 10f), 0.42f, 0.55f, 0.28f);
        }
        if ((flags & ZonePropFlags.Cloud) != 0)
        {
            // Soft cloud banks: broad but no longer solid fullscreen fills.
            return new RuntimeProfile(new Vector4(1f / 5200f, 0.60f, 0.62f, 8f), 0.42f, 0.62f, 0.38f);
        }
        // Generic nebula interior.
        return new RuntimeProfile(new Vector4(1f / 4000f, 0.55f, 0.65f, 10f), 0.42f, 0.60f, 0.35f);
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
