using System;
using System.Numerics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Helper math for the high-resolution near cascade. Current PR-5.12 wiring
/// only enables shader/HDR contracts; separate near textures arrive in a later
/// pass, so callers must still pass an actual near resource before enabling.
/// </summary>
public static class VolumetricNearCascadeMath
{
    public static bool CanCompositeNear(RenderFeatureSet features, bool hasNearIntegrated) =>
        features.VolumetricNearCascade &&
        features.VolumetricNearComposite &&
        features.VolumetricComposite &&
        hasNearIntegrated;

    public static float FadeWeight(float viewDistance, FroxelGridDesc nearGrid)
    {
        if (!nearGrid.IsValid)
        {
            return 1f;
        }
        var fadeStart = FadeStart(nearGrid);
        var range = MathF.Max(nearGrid.FarPlane - fadeStart, 1e-3f);
        return Math.Clamp((viewDistance - fadeStart) / range, 0f, 1f);
    }

    public static float FadeStart(FroxelGridDesc nearGrid) =>
        MathF.Max(nearGrid.NearPlane, nearGrid.FarPlane * 0.72f);

    /// <summary>x enabled, y near far, z fade start, w inverse fade range.</summary>
    public static Vector4 ShaderParams(FroxelGridDesc nearGrid, bool enabled)
    {
        if (!enabled || !nearGrid.IsValid)
        {
            return Vector4.Zero;
        }
        var fadeStart = FadeStart(nearGrid);
        var invFade = 1f / MathF.Max(nearGrid.FarPlane - fadeStart, 1e-3f);
        return new Vector4(1f, nearGrid.FarPlane, fadeStart, invFade);
    }
}
