using System.Globalization;
using System.Numerics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Stable metadata contract for generated Phase 5 engine volume cache payloads.
/// The actual dense/compressed payload writer remains an offline tool concern.
/// </summary>
public readonly record struct VolumetricEngineVolumeDescriptor(
    bool Valid,
    int Version,
    VolumetricEngineVolumeFormat Format,
    int Width,
    int Height,
    int Depth,
    Vector2 DensityNormalize,
    string CacheKey,
    string EngineVolumePath,
    string CacheManifestPath,
    string CanonicalSystem,
    string CanonicalNebula,
    string GridName,
    string ContentHash,
    string Error)
{
    public const string Magic = "SIRIUSVOL";
    public const int CurrentVersion = 1;

    public long VoxelCount => Valid ? (long)Width * Height * Depth : 0;

    public int BytesPerVoxel => Format switch
    {
        VolumetricEngineVolumeFormat.DensityR8UNorm => 1,
        VolumetricEngineVolumeFormat.DensityR16UNorm => 2,
        VolumetricEngineVolumeFormat.DensityR16Float => 2,
        _ => 0
    };

    public long PayloadBytes => VoxelCount * BytesPerVoxel;

    public string[] BuildHeaderLines()
    {
        if (!Valid)
        {
            return [];
        }

        return
        [
            "# Sirius Phase 5 engine volume descriptor",
            $"magic = {Magic}",
            $"version = {Version}",
            $"format = {FormatToken(Format)}",
            $"cache_key = {CacheKey}",
            $"cache = {EngineVolumePath}",
            $"manifest = {CacheManifestPath}",
            $"canonical_system = {CanonicalSystem}",
            $"canonical_nebula = {CanonicalNebula}",
            $"grid = {GridName}",
            $"dimensions = {Width}, {Height}, {Depth}",
            $"voxel_count = {VoxelCount}",
            $"payload_bytes = {PayloadBytes}",
            $"density_normalize_scale = {Fmt(DensityNormalize.X)}",
            $"density_normalize_bias = {Fmt(DensityNormalize.Y)}",
            $"content_hash = {ContentHash}"
        ];
    }

    public static VolumetricEngineVolumeDescriptor FromOpenVdbArtifact(
        VolumetricOpenVdbCacheArtifactPlan artifactPlan,
        VolumetricEngineVolumeFormat format = VolumetricEngineVolumeFormat.DensityR16UNorm)
    {
        var validation = VolumetricOpenVdbImport.ValidateCacheArtifactPlan(artifactPlan);
        if (!validation.Valid)
        {
            return Invalid(validation.Error);
        }
        if (!IsSupportedFormat(format))
        {
            return Invalid("unsupported engine volume format");
        }

        var plan = artifactPlan.ImportPlan;
        return new VolumetricEngineVolumeDescriptor(
            true,
            CurrentVersion,
            format,
            plan.Metadata.Width,
            plan.Metadata.Height,
            plan.Metadata.Depth,
            plan.DensityNormalize,
            artifactPlan.CacheKey,
            artifactPlan.EngineVolumePath,
            artifactPlan.CacheManifestPath,
            plan.Metadata.CanonicalSystem,
            plan.Metadata.CanonicalNebula,
            plan.Metadata.GridName,
            plan.Metadata.ContentHash,
            "");
    }

    public static VolumetricEngineVolumeDescriptor Invalid(string error) =>
        new(false, 0, default, 0, 0, 0, Vector2.Zero, "", "", "", "", "", "", "", error);

    private static bool IsSupportedFormat(VolumetricEngineVolumeFormat format) =>
        format is VolumetricEngineVolumeFormat.DensityR8UNorm
            or VolumetricEngineVolumeFormat.DensityR16UNorm
            or VolumetricEngineVolumeFormat.DensityR16Float;

    private static string FormatToken(VolumetricEngineVolumeFormat format) =>
        format switch
        {
            VolumetricEngineVolumeFormat.DensityR8UNorm => "density_r8_unorm",
            VolumetricEngineVolumeFormat.DensityR16UNorm => "density_r16_unorm",
            VolumetricEngineVolumeFormat.DensityR16Float => "density_r16_float",
            _ => "unknown"
        };

    private static string Fmt(float value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);
}

public enum VolumetricEngineVolumeFormat
{
    DensityR8UNorm = 1,
    DensityR16UNorm = 2,
    DensityR16Float = 3
}
