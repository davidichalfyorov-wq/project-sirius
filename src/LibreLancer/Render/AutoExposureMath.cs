using System;

namespace LibreLancer.Render;

/// <summary>
/// CPU reference math for the opt-in auto-exposure pass. Keeping this small
/// part outside the frame pipeline makes the exposure policy unit-testable
/// without requiring a GPU readback.
/// </summary>
public static class AutoExposureMath
{
    public static float AdaptLuminance(float previous, float current, float deltaSeconds,
        float speedUp, float speedDown)
    {
        if (!float.IsFinite(current) || current <= 0f)
        {
            current = 0.18f;
        }
        if (!float.IsFinite(previous) || previous <= 0f)
        {
            return current;
        }
        var speed = current > previous ? speedUp : speedDown;
        var t = 1f - MathF.Exp(-MathF.Max(deltaSeconds, 0f) * MathF.Max(speed, 0f));
        return previous + (current - previous) * Math.Clamp(t, 0f, 1f);
    }

    public static float ExposureForLuminance(float adaptedLuminance, float key,
        float compensationStops, float minExposure, float maxExposure)
    {
        if (!float.IsFinite(adaptedLuminance) || adaptedLuminance <= 0f)
        {
            adaptedLuminance = 0.18f;
        }
        key = Math.Clamp(key, 0.01f, 1f);
        minExposure = MathF.Max(0.001f, minExposure);
        maxExposure = MathF.Max(minExposure, maxExposure);
        var exp = key * MathF.Pow(2f, compensationStops) / MathF.Max(adaptedLuminance, 1e-4f);
        return Math.Clamp(exp, minExposure, maxExposure);
    }
}
