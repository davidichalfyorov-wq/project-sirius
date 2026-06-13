using System;
using System.Numerics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// CPU reference for the PR-5.6 composite equation used by FroxelComposite.
/// </summary>
public static class VolumetricCompositeMath
{
    public static Vector3 Composite(Vector3 scene, Vector3 scattering, float transmittance, float intensity)
    {
        intensity = Math.Clamp(intensity, 0f, 1f);
        transmittance = Math.Clamp(transmittance, 0f, 1f);
        var effectiveTransmittance = 1f + (transmittance - 1f) * intensity;
        return scene * effectiveTransmittance + Vector3.Max(scattering, Vector3.Zero) * intensity;
    }
}
