using System;
using System.Collections.Generic;
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

    public static VolumetricEngineVolumeHeaderValidation ValidateHeader(
        IEnumerable<string> lines,
        VolumetricEngineVolumeDescriptor descriptor)
    {
        if (!descriptor.Valid)
        {
            return VolumetricEngineVolumeHeaderValidation.Invalid(
                "cannot validate engine volume header for invalid descriptor");
        }
        if (!TryParseKeyValueLines(lines, out var values, out var parseError))
        {
            return VolumetricEngineVolumeHeaderValidation.Invalid(parseError);
        }

        var dimensions = FormattableString.Invariant(
            $"{descriptor.Width}, {descriptor.Height}, {descriptor.Depth}");
        var expected =
            new (string Key, string Value)[]
            {
                ("magic", Magic),
                ("version", descriptor.Version.ToString(CultureInfo.InvariantCulture)),
                ("format", FormatToken(descriptor.Format)),
                ("cache_key", descriptor.CacheKey),
                ("cache", descriptor.EngineVolumePath),
                ("manifest", descriptor.CacheManifestPath),
                ("canonical_system", descriptor.CanonicalSystem),
                ("canonical_nebula", descriptor.CanonicalNebula),
                ("grid", descriptor.GridName),
                ("dimensions", dimensions),
                ("content_hash", descriptor.ContentHash)
            };

        foreach (var (key, expectedValue) in expected)
        {
            if (!values.TryGetValue(key, out var actualValue))
            {
                return VolumetricEngineVolumeHeaderValidation.Invalid(
                    $"engine volume header missing {key}");
            }
            if (!string.Equals(actualValue, expectedValue, StringComparison.Ordinal))
            {
                return VolumetricEngineVolumeHeaderValidation.Invalid(
                    $"engine volume header {key} mismatch");
            }
        }

        var longChecks =
            new (string Key, long Value)[]
            {
                ("voxel_count", descriptor.VoxelCount),
                ("payload_bytes", descriptor.PayloadBytes)
            };

        foreach (var (key, expectedValue) in longChecks)
        {
            if (!values.TryGetValue(key, out var actualValue))
            {
                return VolumetricEngineVolumeHeaderValidation.Invalid(
                    $"engine volume header missing {key}");
            }
            if (!long.TryParse(actualValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return VolumetricEngineVolumeHeaderValidation.Invalid(
                    $"engine volume header invalid {key}");
            }
            if (parsed != expectedValue)
            {
                return VolumetricEngineVolumeHeaderValidation.Invalid(
                    $"engine volume header {key} mismatch");
            }
        }

        var floatChecks =
            new (string Key, float Value)[]
            {
                ("density_normalize_scale", descriptor.DensityNormalize.X),
                ("density_normalize_bias", descriptor.DensityNormalize.Y)
            };

        foreach (var (key, expectedValue) in floatChecks)
        {
            if (!values.TryGetValue(key, out var actualValue))
            {
                return VolumetricEngineVolumeHeaderValidation.Invalid(
                    $"engine volume header missing {key}");
            }
            if (!float.TryParse(actualValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return VolumetricEngineVolumeHeaderValidation.Invalid(
                    $"engine volume header invalid {key}");
            }
            if (MathF.Abs(parsed - expectedValue) > 1e-6f)
            {
                return VolumetricEngineVolumeHeaderValidation.Invalid(
                    $"engine volume header {key} mismatch");
            }
        }

        return VolumetricEngineVolumeHeaderValidation.Ok();
    }

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

    private static bool TryParseKeyValueLines(
        IEnumerable<string> lines,
        out Dictionary<string, string> values,
        out string error)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var line = raw.Split('#', 2)[0].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var split = line.IndexOf('=');
            if (split <= 0)
            {
                error = $"invalid engine volume header line '{raw}'";
                return false;
            }

            values[line[..split].Trim()] = line[(split + 1)..].Trim();
        }

        error = "";
        return true;
    }
}

public enum VolumetricEngineVolumeFormat
{
    DensityR8UNorm = 1,
    DensityR16UNorm = 2,
    DensityR16Float = 3
}

public readonly record struct VolumetricEngineVolumeHeaderValidation(
    bool Valid,
    string Error)
{
    public static VolumetricEngineVolumeHeaderValidation Ok() =>
        new(true, "");

    public static VolumetricEngineVolumeHeaderValidation Invalid(string error) =>
        new(false, error);
}
