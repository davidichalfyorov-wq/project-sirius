using System;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// CPU mirror of scalar history-rejection math used by the depth-aware temporal
/// shader. Keeping it here makes preset behaviour testable without a GPU.
/// </summary>
public static class VolumetricReprojectionMath
{
    public static float ViewDistanceToSlice(float distance, FroxelGridDesc grid)
    {
        var n = MathF.Max(grid.NearPlane, 0f);
        var f = MathF.Max(grid.FarPlane, n + 1f);
        var t = Math.Clamp((distance - n) / (f - n), 0f, 1f);
        return MathF.Sqrt(t);
    }

    public static float HistoryConfidence(float prevU, float prevV, float prevSlice,
        float currentSlice, float distanceMismatchMeters, float depthToleranceMeters,
        float rejectStrength)
    {
        var inside = prevU >= 0f && prevU <= 1f && prevV >= 0f && prevV <= 1f;
        if (!inside)
        {
            return 0f;
        }
        var sliceMismatch = MathF.Abs(prevSlice - currentSlice) * MathF.Max(rejectStrength, 0.01f);
        var depthMismatch = distanceMismatchMeters / MathF.Max(depthToleranceMeters, 1f);
        return Math.Clamp(1f - MathF.Max(sliceMismatch, depthMismatch), 0f, 1f);
    }

    public static float HistoryWeightWithConfidence(float baseWeight, float confidence) =>
        Math.Clamp(baseWeight, 0f, 1f) * Math.Clamp(confidence, 0f, 1f);
}
