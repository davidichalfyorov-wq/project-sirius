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
        var timing = TimingFor(profile.Archetype, features.VolumetricQuality);
        var intensity = EvaluatePulse(totalTimeSeconds, timing.IntervalSeconds, timing.FlashSeconds,
            timing.AfterglowSeconds, out var phase, out var flashIndex);
        if (intensity <= 0.0001f)
        {
            return VolumetricLightningChannelFrame.Inactive(
                FormattableString.Invariant($"waiting interval={timing.IntervalSeconds:0.0}s"));
        }

        Span<Vector4> points = stackalloc Vector4[MaxPoints];
        BuildChannel(points, seed, phase, profile.Archetype);
        var color = ColorFor(profile);
        var radius = RadiusFor(profile.Archetype, features.VolumetricQuality);
        var gain = timing.Intensity * intensity;

        return new VolumetricLightningChannelFrame(
            true,
            points[0], points[1], points[2], points[3],
            points[4], points[5], points[6], points[7],
            MaxPoints,
            color,
            gain,
            radius,
            timing.AfterglowSeconds,
            FormattableString.Invariant($"pts={MaxPoints}, flash={flashIndex}, I={gain:0.00}, r={radius:0.00}"));
    }

    public static float EvaluatePulse(float time, float interval, float flashDuration, float afterglowDuration,
        out float phase, out int flashIndex)
    {
        interval = MathF.Max(interval, 0.25f);
        flashDuration = MathF.Max(flashDuration, 0.02f);
        afterglowDuration = MathF.Max(afterglowDuration, 0.01f);
        phase = time - MathF.Floor(time / interval) * interval;
        flashIndex = -1;

        Span<float> starts = stackalloc float[3] { 0f, flashDuration * 1.7f, flashDuration * 3.2f };
        Span<float> amps = stackalloc float[3] { 1.0f, 0.62f, 0.34f };
        for (var i = 0; i < starts.Length; i++)
        {
            var local = phase - starts[i];
            if (local >= 0f && local <= flashDuration)
            {
                flashIndex = i;
                var u = local / flashDuration;
                return amps[i] * MathF.Pow(1f - u, 1.7f);
            }
        }

        var afterStart = starts[^1] + flashDuration;
        var after = phase - afterStart;
        if (after >= 0f && after <= afterglowDuration)
        {
            flashIndex = 3;
            return 0.22f * MathF.Exp(-after * 4.2f / afterglowDuration);
        }
        return 0f;
    }

    public static void BuildChannel(Span<Vector4> points, int seed, float phase, string archetype)
    {
        if (points.Length < MaxPoints)
        {
            throw new ArgumentException("Lightning channel requires 8 points", nameof(points));
        }

        var vertical = archetype.Contains("crow", StringComparison.OrdinalIgnoreCase) ||
                       archetype.Contains("nomad", StringComparison.OrdinalIgnoreCase);
        for (var i = 0; i < MaxPoints; i++)
        {
            var t = i / (float)(MaxPoints - 1);
            var x = Lerp(-0.62f, 0.54f, Hash(seed, 11)) + (Hash(seed, i * 17 + 3) - 0.5f) * 0.34f;
            var y = vertical ? Lerp(-0.72f, 0.72f, t) : (Hash(seed, i * 19 + 5) - 0.5f) * 0.72f;
            var z = Lerp(-0.58f, 0.58f, Hash(seed, 23)) + (Hash(seed, i * 29 + 7) - 0.5f) * 0.42f;
            var bend = MathF.Sin((t + phase * 0.25f) * MathF.Tau) * 0.11f;
            points[i] = new Vector4(x + bend, y, z - bend * 0.5f, 1f);
        }
    }

    public static VolumetricLightningTiming TimingFor(string archetype, int quality)
    {
        quality = Math.Clamp(quality, 0, 3);
        var electric = archetype.Contains("crow", StringComparison.OrdinalIgnoreCase) ||
                       archetype.Contains("nomad", StringComparison.OrdinalIgnoreCase);
        return electric
            ? new VolumetricLightningTiming(2.8f - quality * 0.25f, 0.055f, 0.42f, 1.35f + quality * 0.18f)
            : new VolumetricLightningTiming(4.2f - quality * 0.20f, 0.045f, 0.32f, 0.82f + quality * 0.12f);
    }

    private static Vector4 ColorFor(NebulaVolumeProfile profile)
    {
        if (profile.Archetype.Contains("crow", StringComparison.OrdinalIgnoreCase))
        {
            return new Vector4(0.58f, 0.68f, 1.0f, 1f);
        }
        if (profile.Archetype.Contains("nomad", StringComparison.OrdinalIgnoreCase))
        {
            return new Vector4(0.75f, 0.45f, 1.0f, 1f);
        }
        return new Vector4(
            Math.Clamp(profile.FogColor.R * 1.35f + 0.15f, 0f, 1.6f),
            Math.Clamp(profile.FogColor.G * 1.35f + 0.15f, 0f, 1.6f),
            Math.Clamp(profile.FogColor.B * 1.35f + 0.15f, 0f, 1.6f),
            1f);
    }

    private static float RadiusFor(string archetype, int quality)
    {
        var electric = archetype.Contains("crow", StringComparison.OrdinalIgnoreCase) ||
                       archetype.Contains("nomad", StringComparison.OrdinalIgnoreCase);
        return (electric ? 0.13f : 0.10f) + Math.Clamp(quality, 0, 3) * 0.018f;
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
    string DebugSummary)
{
    public static VolumetricLightningChannelFrame Inactive(string reason) =>
        new(false, default, default, default, default, default, default, default, default,
            0, Vector4.Zero, 0f, 0f, 0f, reason);

    public Vector4 Params => new(Active ? 1f : 0f, PointCount, Intensity, Radius);
}
