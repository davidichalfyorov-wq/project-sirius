using System;
using System.Numerics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Persistent near-field wake profile. Push collapses quickly; wake and swirl
/// linger for a few seconds so fog closes softly behind moving ships.
/// </summary>
public readonly record struct VolumetricWakeHistoryProfile(
    bool Enabled,
    float DeltaSeconds,
    float WakeHalfLifeSeconds,
    float PushHalfLifeSeconds,
    float SwirlHalfLifeSeconds,
    float WakeStrength)
{
    public static VolumetricWakeHistoryProfile Disabled => new(false, 0f, 0f, 0f, 0f, 0f);

    public static VolumetricWakeHistoryProfile ForQuality(int quality, float deltaSeconds, bool enabled)
    {
        if (!enabled)
        {
            return Disabled;
        }
        var q = Math.Clamp(quality, 0, 3);
        var wakeHalfLife = q switch
        {
            0 => 1.7f,
            1 => 2.2f,
            3 => 3.2f,
            _ => 2.8f
        };
        return new VolumetricWakeHistoryProfile(
            true,
            Math.Clamp(deltaSeconds, 1f / 240f, 0.25f),
            wakeHalfLife,
            Math.Max(0.35f, wakeHalfLife * 0.36f),
            Math.Max(0.80f, wakeHalfLife * 0.72f),
            q >= 2 ? 1.0f : 0.82f);
    }

    public Vector4 ShaderParams => new(DeltaSeconds, WakeHalfLifeSeconds, PushHalfLifeSeconds, SwirlHalfLifeSeconds);

    public Vector4 ShaderParams2 => new(WakeStrength, Enabled ? 1f : 0f, 0f, 0f);

    public string DebugSummary => !Enabled
        ? "off"
        : FormattableString.Invariant(
            $"dt={DeltaSeconds:0.000}s half={WakeHalfLifeSeconds:0.0}s strength={WakeStrength:0.00}");
}

public static class VolumetricWakeHistoryMath
{
    public static float DecayFactor(float deltaSeconds, float halfLifeSeconds)
    {
        if (halfLifeSeconds <= 0f)
        {
            return 0f;
        }
        return MathF.Pow(0.5f, MathF.Max(deltaSeconds, 0f) / halfLifeSeconds);
    }

    public static Vector4 Blend(Vector4 current, Vector4 history, VolumetricWakeHistoryProfile profile)
    {
        if (!profile.Enabled)
        {
            return current;
        }
        var pushDecay = DecayFactor(profile.DeltaSeconds, profile.PushHalfLifeSeconds);
        var swirlDecay = DecayFactor(profile.DeltaSeconds, profile.SwirlHalfLifeSeconds);
        var wakeDecay = DecayFactor(profile.DeltaSeconds, profile.WakeHalfLifeSeconds);
        var push = MathF.Max(current.X, history.X * pushDecay);
        var swirl = MathF.Max(current.Y, history.Y * swirlDecay);
        var wake = MathF.Max(current.Z, history.Z * wakeDecay) * profile.WakeStrength;
        var occupancy = MathF.Max(push, MathF.Max(swirl, wake));
        return new Vector4(
            Math.Clamp(push, 0f, 1f),
            Math.Clamp(swirl, 0f, 1f),
            Math.Clamp(wake, 0f, 1f),
            Math.Clamp(occupancy, 0f, 1f));
    }
}
