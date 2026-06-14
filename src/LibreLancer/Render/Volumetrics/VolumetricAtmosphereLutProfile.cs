using System;
using System.Numerics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// CPU fallback for the Phase 5 atmosphere LUT resources. This is not the final
/// compute generator, but keeps the LUT stack physically plausible and
/// RenderDoc-visible while the pass contract is still being built.
/// </summary>
public static class VolumetricAtmosphereLutProfile
{
    public static Vector4 EvaluateTransmittance(float sunZenithT, float altitudeT, int quality)
    {
        sunZenithT = Math.Clamp(sunZenithT, 0f, 1f);
        altitudeT = Math.Clamp(altitudeT, 0f, 1f);
        quality = Math.Clamp(quality, 0, 3);

        var horizonPath = 1f + MathF.Pow(1f - sunZenithT, 2.25f) * 8.5f;
        var altitudeDensity = MathF.Exp(-altitudeT * 3.2f);
        var opticalDepth = horizonPath * altitudeDensity * (0.060f + quality * 0.0045f);

        return new Vector4(
            MathF.Exp(-opticalDepth * 0.62f),
            MathF.Exp(-opticalDepth * 1.00f),
            MathF.Exp(-opticalDepth * 1.72f),
            1f);
    }

    public static Vector4 EvaluateMultiScattering(float sunZenithT, float altitudeT, int quality)
    {
        sunZenithT = Math.Clamp(sunZenithT, 0f, 1f);
        altitudeT = Math.Clamp(altitudeT, 0f, 1f);
        quality = Math.Clamp(quality, 0, 3);

        var horizon = MathF.Pow(1f - sunZenithT, 1.6f);
        var lowAir = MathF.Exp(-altitudeT * 2.4f);
        var strength = (0.018f + quality * 0.004f) * (0.35f + horizon * 0.85f) * lowAir;
        return new Vector4(
            strength * (0.80f + horizon * 0.20f),
            strength * (0.95f + horizon * 0.10f),
            strength * (1.18f + sunZenithT * 0.14f),
            1f);
    }

    public static Vector4 EvaluateSkyView(float viewZenithT, float sunZenithT, int quality)
    {
        viewZenithT = Math.Clamp(viewZenithT, 0f, 1f);
        sunZenithT = Math.Clamp(sunZenithT, 0f, 1f);
        quality = Math.Clamp(quality, 0, 3);

        var horizon = MathF.Exp(-MathF.Abs(viewZenithT - 0.48f) * 5.5f);
        var sunGlow = MathF.Pow(sunZenithT, 2.0f);
        var dusk = MathF.Pow(1f - sunZenithT, 2.4f);
        var exposure = 0.12f + quality * 0.018f;

        var blue = exposure * (0.42f + sunGlow * 0.42f + horizon * 0.26f);
        var warm = exposure * dusk * horizon;
        return new Vector4(
            Math.Clamp(blue * 0.48f + warm * 1.05f, 0f, 1f),
            Math.Clamp(blue * 0.68f + warm * 0.58f, 0f, 1f),
            Math.Clamp(blue * 1.18f + warm * 0.18f, 0f, 1f),
            1f);
    }
}
