using System;
using System.Numerics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Packs the AtmosphereMaterial aux-volume contract into shader parameters.
/// The shader has one aux 3D texture slot today: t13 is either sky-view LUT
/// or cloud-shell density, never both.
/// </summary>
public static class VolumetricAtmosphereAuxBinding
{
    public static Vector4 BuildLutParams(
        bool lutsActive,
        bool cloudShellAvailable,
        float cloudShellStrength,
        bool skyViewAvailable)
    {
        var cloudActive = cloudShellAvailable ? 1f : 0f;
        var skyViewActive = skyViewAvailable && !cloudShellAvailable ? 1f : 0f;
        return new Vector4(
            lutsActive ? 1f : 0f,
            cloudActive,
            cloudShellAvailable ? Math.Clamp(cloudShellStrength, 0f, 1f) : 0f,
            skyViewActive);
    }
}
