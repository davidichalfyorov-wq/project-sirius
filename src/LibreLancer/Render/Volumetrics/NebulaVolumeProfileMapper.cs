using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LibreLancer.Data.GameData.World;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Builds PR-5.1 runtime nebula profiles from legacy Freelancer/Discovery data.
/// This is intentionally CPU-only metadata in PR-5.1; PR-5.2 will allocate and
/// upload froxel resources that consume these values.
/// </summary>
public static class NebulaVolumeProfileMapper
{
    public const int DefaultMaxProfiles = 32;

    public static NebulaVolumeProfile[] BuildProfiles(IEnumerable<Nebula>? nebulas, int maxProfiles = DefaultMaxProfiles)
    {
        if (nebulas == null || maxProfiles <= 0)
        {
            return [];
        }

        var profiles = new List<NebulaVolumeProfile>(Math.Min(maxProfiles, DefaultMaxProfiles));
        foreach (var nebula in nebulas)
        {
            if (profiles.Count >= maxProfiles)
            {
                break;
            }
            if (TryCreate(nebula, out var profile))
            {
                profiles.Add(profile);
            }
        }
        return profiles.ToArray();
    }

    public static bool TryCreate(Nebula? nebula, out NebulaVolumeProfile profile)
    {
        profile = default;
        if (nebula?.Zone == null)
        {
            return false;
        }

        var zone = nebula.Zone;
        var runtime = RuntimeProfileFor(zone);
        var fogFar = MathF.Max(nebula.FogRange.Y, 200f);
        var fogColor = nebula.FogColor;
        var albedo = LiftAlbedo(fogColor, runtime.Albedo);
        var ambient = nebula.AmbientColor ?? new Color4(0.015f, 0.015f, 0.015f, 1f);
        var lightning = nebula.DynamicLightning || nebula.BackgroundLightning || nebula.CloudLightning;
        var exclusionCount = nebula.ExclusionZones?.Count(x => x.Zone != null) ?? 0;

        profile = new NebulaVolumeProfile(
            zone.Nickname ?? nebula.SourceFile,
            nebula.SourceFile,
            runtime.Archetype,
            zone.Shape,
            zone.Position,
            zone.RotationMatrix,
            zone.Size,
            MathF.Max(zone.EdgeFraction, 0.05f),
            nebula.FogRange,
            fogColor,
            albedo,
            ambient,
            (0.2f * runtime.Extinction) / fogFar,
            runtime.Coverage,
            runtime.BaseNoiseScale,
            runtime.DetailErosion,
            runtime.DriftSpeed,
            runtime.DomainWarp,
            runtime.PhaseGForward,
            runtime.PhaseGBackward,
            runtime.PhaseBlend,
            runtime.PowderFactor,
            runtime.GodRayStrength,
            runtime.DustMoteDensity,
            runtime.DisplacementStrength,
            nebula.HasInteriorClouds,
            lightning,
            exclusionCount);
        return true;
    }

    private readonly record struct RuntimeProfile(
        string Archetype,
        float BaseNoiseScale,
        float Coverage,
        float DetailErosion,
        float DriftSpeed,
        float Albedo,
        float Extinction,
        float DomainWarp,
        float PhaseGForward,
        float PhaseGBackward,
        float PhaseBlend,
        float PowderFactor,
        float GodRayStrength,
        float DustMoteDensity,
        float DisplacementStrength);

    private static RuntimeProfile RuntimeProfileFor(Zone zone)
    {
        var flags = zone.PropertyFlags;
        var nickname = zone.Nickname ?? string.Empty;
        if ((flags & ZonePropFlags.Badlands) != 0 ||
            nickname.Contains("badlands", StringComparison.OrdinalIgnoreCase))
        {
            return new RuntimeProfile("badlands", 1f / 6500f, 0.56f, 0.60f, 13f,
                0.42f, 0.65f, 0.42f, 0.80f, -0.20f, 0.82f, 0.55f, 0.75f, 0.24f, 0.42f);
        }
        if ((flags & (ZonePropFlags.Ice | ZonePropFlags.Crystal)) != 0 ||
            nickname.Contains("ice", StringComparison.OrdinalIgnoreCase) ||
            nickname.Contains("li05", StringComparison.OrdinalIgnoreCase))
        {
            return new RuntimeProfile("ice", 1f / 7000f, 0.54f, 0.55f, 5f,
                0.46f, 0.48f, 0.18f, 0.72f, -0.18f, 0.78f, 0.45f, 0.55f, 0.18f, 0.30f);
        }
        if ((flags & ZonePropFlags.Nomad) != 0 ||
            nickname.Contains("nomad", StringComparison.OrdinalIgnoreCase))
        {
            return new RuntimeProfile("nomad", 1f / 6200f, 0.52f, 0.62f, 14f,
                0.48f, 0.55f, 0.40f, 0.86f, -0.22f, 0.84f, 0.62f, 0.80f, 0.28f, 0.38f);
        }
        if (nickname.Contains("crow", StringComparison.OrdinalIgnoreCase))
        {
            return new RuntimeProfile("crow", 1f / 6500f, 0.50f, 0.62f, 12f,
                0.34f, 0.48f, 0.46f, 0.82f, -0.22f, 0.80f, 0.58f, 0.95f, 0.32f, 0.45f);
        }
        if (nickname.Contains("okha", StringComparison.OrdinalIgnoreCase))
        {
            return new RuntimeProfile("okha", 1f / 5600f, 0.54f, 0.62f, 9f,
                0.40f, 0.58f, 0.30f, 0.78f, -0.20f, 0.78f, 0.50f, 0.65f, 0.24f, 0.35f);
        }
        if (nickname.Contains("walker", StringComparison.OrdinalIgnoreCase))
        {
            return new RuntimeProfile("walker", 1f / 5200f, 0.58f, 0.62f, 7f,
                0.44f, 0.65f, 0.34f, 0.80f, -0.18f, 0.80f, 0.54f, 0.65f, 0.22f, 0.35f);
        }
        if ((flags & ZonePropFlags.GasPockets) != 0)
        {
            return new RuntimeProfile("gas_pockets", 1f / 4800f, 0.45f, 0.64f, 10f,
                0.42f, 0.55f, 0.28f, 0.76f, -0.18f, 0.78f, 0.50f, 0.55f, 0.20f, 0.34f);
        }
        if ((flags & ZonePropFlags.Cloud) != 0)
        {
            return new RuntimeProfile("cloud", 1f / 5200f, 0.60f, 0.62f, 8f,
                0.42f, 0.62f, 0.38f, 0.80f, -0.20f, 0.80f, 0.56f, 0.70f, 0.24f, 0.40f);
        }
        return new RuntimeProfile("generic", 1f / 4000f, 0.55f, 0.65f, 10f,
            0.42f, 0.60f, 0.35f, 0.80f, -0.20f, 0.80f, 0.55f, 0.65f, 0.22f, 0.36f);
    }

    private static Color4 LiftAlbedo(Color4 authoredFogColor, float targetMax)
    {
        var max = MathF.Max(authoredFogColor.R, MathF.Max(authoredFogColor.G, MathF.Max(authoredFogColor.B, 1e-3f)));
        var scale = targetMax / max;
        return new Color4(
            Math.Clamp(authoredFogColor.R * scale, 0f, 1f),
            Math.Clamp(authoredFogColor.G * scale, 0f, 1f),
            Math.Clamp(authoredFogColor.B * scale, 0f, 1f),
            1f);
    }
}
