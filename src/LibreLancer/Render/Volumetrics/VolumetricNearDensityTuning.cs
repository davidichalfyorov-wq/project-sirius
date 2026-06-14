using System;
using System.Numerics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Near-field density tuning keeps the main cascade broad while the optional
/// near cascade gets smaller erosion and dust features for cockpit-scale motion.
/// </summary>
public readonly record struct VolumetricNearDensityTuning(
    bool Enabled,
    float BaseFrequencyMultiplier,
    float DetailFrequencyMultiplier,
    float ErosionBoost,
    float WarpBoost,
    float DustDensity,
    float DustStrength,
    float ContrastBoost)
{
    public static readonly VolumetricNearDensityTuning Disabled = new(
        false, 1f, 1f, 1f, 1f, 0f, 0f, 1f);

    public Vector4 ShaderDetailParams => new(
        Enabled ? 1f : 0f,
        MathF.Max(BaseFrequencyMultiplier, 0.1f),
        MathF.Max(DetailFrequencyMultiplier, 0.1f),
        MathF.Max(ErosionBoost, 0.1f));

    public Vector4 ShaderDustParams => new(
        Math.Clamp(DustDensity, 0f, 2f),
        Math.Clamp(DustStrength, 0f, 2f),
        Math.Clamp(ContrastBoost, 0.1f, 4f),
        Math.Clamp(WarpBoost, 0.1f, 4f));

    public string DebugSummary => !Enabled
        ? "off"
        : FormattableString.Invariant(
            $"basex{BaseFrequencyMultiplier:0.0} detailx{DetailFrequencyMultiplier:0.0} dust={DustDensity:0.00}");

    public static VolumetricNearDensityTuning ForProfile(NebulaVolumeProfile profile, int quality, bool enabled)
    {
        if (!enabled)
        {
            return Disabled;
        }

        var q = Math.Clamp(quality, 0, 3);
        var qScale = q switch
        {
            0 => 0.65f,
            1 => 0.82f,
            2 => 1.0f,
            _ => 1.18f
        };

        var archetype = profile.Archetype ?? string.Empty;
        var badlands = archetype.Equals("badlands", StringComparison.OrdinalIgnoreCase);
        var ice = archetype.Equals("ice", StringComparison.OrdinalIgnoreCase);
        var plasma = archetype.Equals("nomad", StringComparison.OrdinalIgnoreCase) ||
                     archetype.Equals("crow", StringComparison.OrdinalIgnoreCase);

        var baseFrequency = (badlands ? 1.75f : ice ? 1.45f : plasma ? 1.55f : 1.60f) * qScale;
        var detailFrequency = (badlands ? 5.4f : ice ? 4.2f : plasma ? 5.0f : 4.7f) * qScale;
        var erosionBoost = badlands ? 1.22f : ice ? 1.12f : plasma ? 1.18f : 1.16f;
        var warpBoost = badlands ? 1.35f : ice ? 1.05f : plasma ? 1.42f : 1.22f;

        var dustDensity = Math.Clamp(profile.DustMoteDensity * (0.75f + q * 0.20f), 0.04f, 0.55f);
        var dustStrength = (badlands ? 0.18f : ice ? 0.11f : plasma ? 0.14f : 0.13f) * qScale;
        var contrast = badlands ? 1.38f : ice ? 1.20f : plasma ? 1.30f : 1.26f;

        return new VolumetricNearDensityTuning(
            true,
            baseFrequency,
            detailFrequency,
            erosionBoost,
            warpBoost,
            dustDensity,
            dustStrength,
            contrast);
    }
}
