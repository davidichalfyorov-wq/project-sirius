using System;
using LibreLancer.Graphics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// View-frustum froxel grid description used by Phase 5 volumetric nebula passes.
/// It is data-only so quality scaling can be unit-tested without a GPU.
/// </summary>
public readonly record struct FroxelGridDesc(
    string DebugName,
    FroxelGridKind Kind,
    int Width,
    int Height,
    int Depth,
    float NearPlane,
    float FarPlane,
    int Quality,
    float ResolutionScale,
    bool StorageCapable,
    bool TemporalHistory)
{
    public bool IsValid => Width > 0 && Height > 0 && Depth > 0 && FarPlane > NearPlane;

    public int VoxelCount => checked(Width * Height * Depth);

    public long BytesPerVolume(SurfaceFormat format = SurfaceFormat.HdrBlendable) =>
        checked((long)VoxelCount * BytesPerVoxel(format));

    public string Dimensions => $"{Width}x{Height}x{Depth}";

    public static FroxelGridDesc MainForViewport(int renderWidth, int renderHeight, int quality)
    {
        var preset = FroxelQualityPreset.FromQuality(quality);
        return new FroxelGridDesc(
            "vol_nebula.main",
            FroxelGridKind.Main,
            Align(Math.Max(16, (int)MathF.Ceiling(renderWidth * preset.MainScale)), 8),
            Align(Math.Max(9, (int)MathF.Ceiling(renderHeight * preset.MainScale)), 8),
            preset.MainDepth,
            1.0f,
            preset.MainFar,
            preset.Quality,
            preset.MainScale,
            StorageCapable: true,
            TemporalHistory: true);
    }

    public static FroxelGridDesc NearForViewport(int renderWidth, int renderHeight, int quality)
    {
        var preset = FroxelQualityPreset.FromQuality(quality);
        return new FroxelGridDesc(
            "vol_nebula.near",
            FroxelGridKind.Near,
            Align(Math.Max(32, (int)MathF.Ceiling(renderWidth * preset.NearScale)), 8),
            Align(Math.Max(18, (int)MathF.Ceiling(renderHeight * preset.NearScale)), 8),
            preset.NearDepth,
            0.25f,
            preset.NearFar,
            preset.Quality,
            preset.NearScale,
            StorageCapable: true,
            TemporalHistory: true);
    }

    private static int Align(int value, int alignment) => ((value + alignment - 1) / alignment) * alignment;

    private static int BytesPerVoxel(SurfaceFormat format) => format switch
    {
        SurfaceFormat.HdrBlendable or SurfaceFormat.HalfVector4 => 8,
        SurfaceFormat.Vector4 => 16,
        SurfaceFormat.Bgra8 => 4,
        SurfaceFormat.R8 => 1,
        SurfaceFormat.Single => 4,
        _ => 8
    };

    private readonly record struct FroxelQualityPreset(
        int Quality,
        float MainScale,
        int MainDepth,
        float MainFar,
        float NearScale,
        int NearDepth,
        float NearFar)
    {
        public static FroxelQualityPreset FromQuality(int quality)
        {
            var q = Math.Clamp(quality, 0, 3);
            return q switch
            {
                0 => new FroxelQualityPreset(0, 1f / 16f, 32, 100_000f, 1f / 12f, 32, 450f),
                1 => new FroxelQualityPreset(1, 1f / 12f, 48, 120_000f, 1f / 8f, 48, 520f),
                3 => new FroxelQualityPreset(3, 1f / 6f, 96, 180_000f, 1f / 4f, 96, 700f),
                _ => new FroxelQualityPreset(2, 1f / 8f, 64, 150_000f, 1f / 6f, 64, 620f)
            };
        }
    }
}

public enum FroxelGridKind
{
    Main,
    Near
}
