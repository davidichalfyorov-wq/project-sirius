using System;

namespace LibreLancer.Render.Volumetrics;

public static class VolumetricPhaseFunctions
{
    private const float FourPi = 12.566370614359172f;

    public static float BeerLambert(float sigmaT, float distanceMeters)
    {
        sigmaT = MathF.Max(0f, sigmaT);
        distanceMeters = MathF.Max(0f, distanceMeters);
        return MathF.Exp(-sigmaT * distanceMeters);
    }

    public static float HenyeyGreenstein(float cosTheta, float g)
    {
        cosTheta = Math.Clamp(cosTheta, -1f, 1f);
        g = Math.Clamp(g, -0.95f, 0.95f);
        var gg = g * g;
        var denom = MathF.Max(1e-4f, 1f + gg - 2f * g * cosTheta);
        return (1f - gg) / (FourPi * denom * MathF.Sqrt(denom));
    }

    public static float DualHenyeyGreenstein(float cosTheta, float forwardG, float backwardG, float forwardBlend)
    {
        forwardBlend = Math.Clamp(forwardBlend, 0f, 1f);
        var forward = HenyeyGreenstein(cosTheta, forwardG);
        var backward = HenyeyGreenstein(cosTheta, backwardG);
        return backward + (forward - backward) * forwardBlend;
    }

    public static float Powder(float density, float strength)
    {
        density = MathF.Max(0f, density);
        strength = MathF.Max(0f, strength);
        return 1f - MathF.Exp(-density * strength);
    }
}
