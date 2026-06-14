using System;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// CPU reference for PR-5.21 sun burnthrough/god-ray attenuation. The actual
/// ray smear remains the existing post pass; this policy feeds it a physically
/// plausible Beer-Lambert transmittance from the active nebula profile.
/// </summary>
public static class VolumetricGodRayMath
{
    public static VolumetricGodRayProfile ForProfile(
        NebulaVolumeProfile profile,
        float sunDistanceMeters,
        int quality,
        float userGodRayIntensity,
        bool enabled)
    {
        if (!enabled || !profile.IsValid)
        {
            return VolumetricGodRayProfile.Disabled;
        }

        var q = Math.Clamp(quality, 0, 3);
        var maxMediumDistance = MathF.Max(profile.FogRange.Y, profile.BoundsRadius);
        var pathLength = Math.Clamp(
            sunDistanceMeters > 1f ? MathF.Min(sunDistanceMeters, maxMediumDistance) : maxMediumDistance,
            250f,
            300_000f);
        var coverage = Math.Clamp(profile.Coverage, 0.05f, 1f);
        var strength = Math.Clamp(profile.GodRayStrength, 0f, 1.5f);
        var sigmaT = MathF.Max(profile.CoreExtinction, profile.EdgeExtinction) *
                     (0.35f + coverage * 0.70f) *
                     (0.70f + strength * 0.35f);
        var opticalDepth = Math.Clamp(sigmaT * pathLength, 0f, 12f);
        var beer = MathF.Exp(-opticalDepth);

        // Keep a small star core visible in dense banks. Freelancer authors
        // sun burnthrough as an art cue, and fully blacking it out reads worse
        // than a dim, colourable glow through the medium.
        var floor = Math.Clamp(0.055f + strength * 0.16f + q * 0.012f, 0.055f, 0.32f);
        var sunTransmittance = Math.Clamp(beer + floor * (1f - beer), 0.015f, 1f);
        var rayMaskTransmittance = Math.Clamp(
            Lerp(sunTransmittance, MathF.Sqrt(sunTransmittance), 0.42f + strength * 0.10f),
            sunTransmittance,
            1f);
        var postIntensity = Math.Clamp(userGodRayIntensity, 0f, 2f);
        var density = Math.Clamp(0.72f + strength * 0.24f + opticalDepth * 0.012f, 0.65f, 1.18f);
        var decay = Math.Clamp(0.955f + strength * 0.012f - opticalDepth * 0.006f, 0.88f, 0.975f);

        return new VolumetricGodRayProfile(
            true,
            sunTransmittance,
            rayMaskTransmittance,
            density,
            decay,
            pathLength,
            opticalDepth,
            floor,
            postIntensity,
            FormattableString.Invariant(
                $"q{q} sunT={sunTransmittance:0.00} rayT={rayMaskTransmittance:0.00} od={opticalDepth:0.00} path={pathLength / 1000f:0}km dens={density:0.00} I={postIntensity:0.00}"));
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
}

public readonly record struct VolumetricGodRayProfile(
    bool Enabled,
    float SunTransmittance,
    float RayMaskTransmittance,
    float RayDensity,
    float RayDecay,
    float PathLengthMeters,
    float OpticalDepth,
    float BurnthroughFloor,
    float PostIntensity,
    string DebugSummary)
{
    public static VolumetricGodRayProfile Disabled { get; } = new(
        false, 1f, 1f, 0.9f, 0.95f, 0f, 0f, 1f, 0f, "off");
}
