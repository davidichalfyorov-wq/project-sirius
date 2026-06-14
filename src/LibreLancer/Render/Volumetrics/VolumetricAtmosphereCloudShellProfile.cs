using System;
using System.Numerics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Procedural fallback data for the future B3 cloud-shell bridge. This keeps
/// the resource path debuggable before authored cloud volumes or compute LUT
/// generation are available.
/// </summary>
public static class VolumetricAtmosphereCloudShellProfile
{
    public static Vector4 Evaluate(float x, float y, float altitude, int quality)
    {
        x = Math.Clamp(x, -1f, 1f);
        y = Math.Clamp(y, -1f, 1f);
        altitude = Math.Clamp(altitude, 0f, 1f);
        quality = Math.Clamp(quality, 0, 3);

        var radius = MathF.Sqrt(x * x + y * y);
        var limb = SmoothStep(0.82f, 0.18f, radius);
        var shell = MathF.Exp(-MathF.Pow((altitude - 0.42f) * 3.4f, 2f));
        var cell = MathF.Sin((x * 17.0f + y * 9.0f + altitude * 5.0f) * MathF.PI);
        var billow = MathF.Sin((x * 5.0f - y * 13.0f + altitude * 11.0f) * MathF.PI);
        var detail = 0.5f + 0.28f * cell + 0.22f * billow;
        var coverage = Math.Clamp((detail - 0.36f) * 1.65f, 0f, 1f);
        coverage *= limb * shell * (0.62f + quality * 0.08f);

        var silver = coverage * (0.18f + 0.10f * altitude);
        return new Vector4(
            silver * 0.86f,
            silver * 0.94f,
            silver * 1.05f,
            Math.Clamp(coverage, 0f, 1f));
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
