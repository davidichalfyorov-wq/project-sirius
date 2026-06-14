using System;
using System.Numerics;
using LibreLancer.Render;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Deterministic lightning-channel scaffold. It converts legacy nebula lightning
/// flags into a short segmented channel in normalized froxel space, so the GPU
/// pass lights the medium along a bolt-shaped path instead of one point flash.
/// </summary>
public sealed class VolumetricLightningChannelState
{
    public const int MaxPoints = 8;

    public VolumetricLightningChannelFrame BuildFrame(NebulaVolumeProfile profile, RenderFeatureSet features,
        float totalTimeSeconds)
    {
        if (!features.VolumetricLightningChannels || !profile.HasLightning)
        {
            return VolumetricLightningChannelFrame.Inactive("off");
        }

        var seed = StableHash(profile.Nickname) ^ StableHash(profile.Archetype);
        var art = VolumetricLightningArtProfile.ForNebula(profile, features.VolumetricQuality);
        var intensity = EvaluatePulse(totalTimeSeconds, art.Timing.IntervalSeconds, art.Timing.FlashSeconds,
            art.Timing.AfterglowSeconds, art.FlashCount, out var phase, out var flashIndex);
        if (intensity <= 0.0001f)
        {
            return VolumetricLightningChannelFrame.Inactive(
                FormattableString.Invariant($"waiting {art.Name} interval={art.Timing.IntervalSeconds:0.0}s"));
        }

        Span<Vector4> points = stackalloc Vector4[MaxPoints];
        BuildChannel(points, seed, phase, art);
        var gain = art.Timing.Intensity * intensity;

        return new VolumetricLightningChannelFrame(
            true,
            points[0], points[1], points[2], points[3],
            points[4], points[5], points[6], points[7],
            MaxPoints,
            art.Color,
            gain,
            art.Radius,
            art.Timing.AfterglowSeconds,
            art.Name,
            art.FlashCount,
            art.DebugColorMode,
            FormattableString.Invariant(
                $"art={art.Name}, pts={MaxPoints}, flashes={art.FlashCount}, flash={flashIndex}, I={gain:0.00}, r={art.Radius:0.00}"));
    }

    public static float EvaluatePulse(float time, float interval, float flashDuration, float afterglowDuration,
        out float phase, out int flashIndex)
    {
        return EvaluatePulse(time, interval, flashDuration, afterglowDuration, flashCount: 3, out phase,
            out flashIndex);
    }

    public static float EvaluatePulse(float time, float interval, float flashDuration, float afterglowDuration,
        int flashCount, out float phase, out int flashIndex)
    {
        interval = MathF.Max(interval, 0.25f);
        flashDuration = MathF.Max(flashDuration, 0.02f);
        afterglowDuration = MathF.Max(afterglowDuration, 0.01f);
        flashCount = Math.Clamp(flashCount, 1, 4);
        phase = time - MathF.Floor(time / interval) * interval;
        flashIndex = -1;

        for (var i = 0; i < flashCount; i++)
        {
            var local = phase - FlashStart(flashDuration, i);
            if (local >= 0f && local <= flashDuration)
            {
                flashIndex = i;
                var u = local / flashDuration;
                return MathF.Pow(0.62f, i) * MathF.Pow(1f - u, 1.7f);
            }
        }

        var afterStart = FlashStart(flashDuration, flashCount - 1) + flashDuration;
        var after = phase - afterStart;
        if (after >= 0f && after <= afterglowDuration)
        {
            flashIndex = flashCount;
            return 0.22f * MathF.Exp(-after * 4.2f / afterglowDuration);
        }
        return 0f;
    }

    public static void BuildChannel(Span<Vector4> points, int seed, float phase, string archetype)
    {
        var art = VolumetricLightningArtProfile.ForArchetype(archetype, quality: 2, new Vector4(0.45f, 0.55f, 0.75f, 1f));
        BuildChannel(points, seed, phase, art);
    }

    public static void BuildChannel(Span<Vector4> points, int seed, float phase, VolumetricLightningArtProfile art)
    {
        if (points.Length < MaxPoints)
        {
            throw new ArgumentException("Lightning channel requires 8 points", nameof(points));
        }

        for (var i = 0; i < MaxPoints; i++)
        {
            var t = i / (float)(MaxPoints - 1);
            var jitter = art.ChannelJitter;
            var x = Lerp(-0.62f, 0.54f, Hash(seed, 11)) + (Hash(seed, i * 17 + 3) - 0.5f) * jitter;
            var randomY = (Hash(seed, i * 19 + 5) - 0.5f) * 0.72f;
            var verticalY = Lerp(-0.72f, 0.72f, t);
            var y = Lerp(randomY, verticalY, art.VerticalBlend);
            var z = Lerp(-0.58f, 0.58f, Hash(seed, 23)) + (Hash(seed, i * 29 + 7) - 0.5f) * jitter * 1.2f;
            var bend = MathF.Sin((t + phase * 0.25f) * MathF.Tau) * art.BendAmplitude;
            var branch = MathF.Sin((t * 3.0f + Hash(seed, i * 31 + 9)) * MathF.Tau) * art.BranchAmplitude;
            points[i] = new Vector4(x + bend + branch, y, z - bend * 0.5f + branch * 0.35f, 1f);
        }
    }

    public static VolumetricLightningTiming TimingFor(string archetype, int quality)
    {
        return VolumetricLightningArtProfile.ForArchetype(
            archetype, quality, new Vector4(0.45f, 0.55f, 0.75f, 1f)).Timing;
    }

    private static float FlashStart(float flashDuration, int flashIndex)
    {
        return flashIndex switch
        {
            0 => 0f,
            1 => flashDuration * 1.7f,
            2 => flashDuration * 3.2f,
            _ => flashDuration * 4.45f
        };
    }

    private static int StableHash(string? value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var ch in value ?? string.Empty)
            {
                hash ^= ch;
                hash *= 16777619u;
            }
            return (int)hash;
        }
    }

    private static float Hash(int seed, int salt)
    {
        unchecked
        {
            var x = (uint)seed + (uint)salt * 0x9E3779B9u;
            x ^= x >> 16;
            x *= 0x7feb352d;
            x ^= x >> 15;
            x *= 0x846ca68b;
            x ^= x >> 16;
            return (x & 0xFFFF) / 65535f;
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

public enum VolumetricLightningDebugColorMode
{
    ProfileColor,
    ElectricBlue,
    PlasmaViolet,
    FogTint
}

public readonly record struct VolumetricLightningArtProfile(
    string Name,
    int FlashCount,
    VolumetricLightningTiming Timing,
    Vector4 Color,
    float Radius,
    float ChannelJitter,
    float BendAmplitude,
    float BranchAmplitude,
    float VerticalBlend,
    VolumetricLightningDebugColorMode DebugColorMode)
{
    public static VolumetricLightningArtProfile ForNebula(NebulaVolumeProfile profile, int quality)
    {
        var fogColor = new Vector4(
            Math.Clamp(profile.FogColor.R * 1.35f + 0.15f, 0f, 1.6f),
            Math.Clamp(profile.FogColor.G * 1.35f + 0.15f, 0f, 1.6f),
            Math.Clamp(profile.FogColor.B * 1.35f + 0.15f, 0f, 1.6f),
            1f);
        return ForArchetype(profile.Archetype, quality, fogColor);
    }

    public static VolumetricLightningArtProfile ForArchetype(string archetype, int quality, Vector4 fogColor)
    {
        var q = Math.Clamp(quality, 0, 3);
        if (archetype.Contains("crow", StringComparison.OrdinalIgnoreCase) ||
            archetype.Contains("electric", StringComparison.OrdinalIgnoreCase))
        {
            return new VolumetricLightningArtProfile(
                "crow-electric",
                4,
                new VolumetricLightningTiming(2.8f - q * 0.25f, 0.055f, 0.50f + q * 0.035f, 1.35f + q * 0.18f),
                new Vector4(0.58f, 0.68f, 1.0f, 1f),
                0.13f + q * 0.018f,
                0.38f,
                0.16f,
                0.10f,
                1.0f,
                VolumetricLightningDebugColorMode.ElectricBlue);
        }
        if (archetype.Contains("nomad", StringComparison.OrdinalIgnoreCase) ||
            archetype.Contains("plasma", StringComparison.OrdinalIgnoreCase))
        {
            return new VolumetricLightningArtProfile(
                "nomad-plasma",
                3,
                new VolumetricLightningTiming(3.15f - q * 0.20f, 0.070f, 0.58f + q * 0.04f, 1.20f + q * 0.18f),
                new Vector4(0.75f, 0.45f, 1.0f, 1f),
                0.15f + q * 0.019f,
                0.44f,
                0.22f,
                0.17f,
                0.72f,
                VolumetricLightningDebugColorMode.PlasmaViolet);
        }
        if (archetype.Contains("ice", StringComparison.OrdinalIgnoreCase))
        {
            return new VolumetricLightningArtProfile(
                "ice-static",
                2,
                new VolumetricLightningTiming(4.6f - q * 0.18f, 0.050f, 0.36f + q * 0.025f, 0.78f + q * 0.10f),
                new Vector4(0.70f, 0.86f, 1.0f, 1f),
                0.10f + q * 0.012f,
                0.26f,
                0.09f,
                0.04f,
                0.42f,
                VolumetricLightningDebugColorMode.ElectricBlue);
        }
        return new VolumetricLightningArtProfile(
            "generic-nebula",
            2,
            new VolumetricLightningTiming(4.2f - q * 0.20f, 0.045f, 0.32f + q * 0.02f, 0.82f + q * 0.12f),
            fogColor,
            0.10f + q * 0.014f,
            0.32f,
            0.11f,
            0.05f,
            0.10f,
            VolumetricLightningDebugColorMode.FogTint);
    }
}

public readonly record struct VolumetricLightningTiming(
    float IntervalSeconds,
    float FlashSeconds,
    float AfterglowSeconds,
    float Intensity);

public readonly record struct VolumetricLightningChannelFrame(
    bool Active,
    Vector4 Point0,
    Vector4 Point1,
    Vector4 Point2,
    Vector4 Point3,
    Vector4 Point4,
    Vector4 Point5,
    Vector4 Point6,
    Vector4 Point7,
    int PointCount,
    Vector4 Color,
    float Intensity,
    float Radius,
    float AfterglowSeconds,
    string ArtProfileName,
    int FlashCount,
    VolumetricLightningDebugColorMode DebugColorMode,
    string DebugSummary)
{
    public static VolumetricLightningChannelFrame Inactive(string reason) =>
        new(false, default, default, default, default, default, default, default, default,
            0, Vector4.Zero, 0f, 0f, 0f, "", 0, VolumetricLightningDebugColorMode.ProfileColor, reason);

    public Vector4 Params => new(Active ? 1f : 0f, PointCount, Intensity, Radius);
}
