using System;
using System.Numerics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// CPU-side reference helpers for depth-aware froxel composite/material fog.
/// </summary>
public static class VolumetricDepthMapping
{
    public static float DistanceToNormalizedSlice(float viewDistance, FroxelGridDesc grid)
    {
        if (!grid.IsValid)
        {
            return 0f;
        }
        var normalized = Math.Clamp((viewDistance - grid.NearPlane) /
                                    Math.Max(grid.FarPlane - grid.NearPlane, 1e-4f), 0f, 1f);
        return MathF.Sqrt(normalized);
    }

    public static float DistanceToTextureSlice(float viewDistance, FroxelGridDesc grid)
    {
        if (!grid.IsValid)
        {
            return 0f;
        }
        var normalized = DistanceToNormalizedSlice(viewDistance, grid);
        var depth = Math.Max(grid.Depth, 1);
        var centeredSlice = normalized * Math.Max(depth - 1, 0) + 0.5f;
        return Math.Clamp(centeredSlice / depth, 0f, 1f);
    }

    public static Vector4 DepthParams(FroxelGridDesc grid, bool enabled)
    {
        var range = Math.Max(grid.FarPlane - grid.NearPlane, 1e-4f);
        return new Vector4(enabled ? 1f : 0f, grid.NearPlane, 1f / range, grid.FarPlane);
    }

    public static Vector4 MaterialFogSettings(FroxelGridDesc grid, float meanExtinction) =>
        new(grid.NearPlane, grid.FarPlane, grid.Depth, Math.Max(meanExtinction, 0f));
}
