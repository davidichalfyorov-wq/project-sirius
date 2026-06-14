using System;
using System.Numerics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// CPU fallback profile for the B2 atmosphere aerial-perspective bridge. It
/// produces a subtle, monotonic participating-media lookup until compute LUT
/// generation replaces it.
/// </summary>
public static class VolumetricAtmosphereAerialProfile
{
    public static Vector4 Evaluate(float screenX, float screenY, float distanceT, int quality)
    {
        screenX = Math.Clamp(screenX, 0f, 1f);
        screenY = Math.Clamp(screenY, 0f, 1f);
        distanceT = Math.Clamp(distanceT, 0f, 1f);
        quality = Math.Clamp(quality, 0, 3);

        var horizon = MathF.Exp(-MathF.Abs(screenY - 0.52f) * 4.8f);
        var zenith = MathF.Pow(1f - MathF.Abs(screenY - 0.5f) * 2f, 2f);
        var screenSoftness = 0.92f + 0.08f * MathF.Cos((screenX - 0.5f) * MathF.PI);
        var opticalDepth = MathF.Pow(distanceT, 1.35f) * (0.32f + horizon * 0.58f + zenith * 0.10f);
        opticalDepth *= screenSoftness * (0.72f + quality * 0.08f);
        opticalDepth = Math.Clamp(opticalDepth, 0f, 0.82f);

        var transmittance = MathF.Exp(-opticalDepth * 0.72f);
        var blueShift = 0.65f + horizon * 0.35f;
        var scattering = opticalDepth * 0.075f;
        return new Vector4(
            scattering * (0.58f + horizon * 0.15f),
            scattering * (0.78f + horizon * 0.10f),
            scattering * (1.00f + blueShift * 0.24f),
            Math.Clamp(transmittance, 0.46f, 1f));
    }
}
