using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Metadata contract for future OpenVDB nebula density imports. The runtime
/// still uses procedural density until a decoder is added; this parser locks
/// the sidecar format and forbids authored volumes from moving canonical
/// Freelancer nebula zones.
/// </summary>
public static class VolumetricOpenVdbImport
{
    public static VolumetricOpenVdbImportResult ParseManifest(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                return VolumetricOpenVdbImportResult.Invalid($"invalid manifest line '{raw}'");
            }

            values[line[..split].Trim()] = line[(split + 1)..].Trim();
        }

        var metadata = new VolumetricOpenVdbImportMetadata(
            DataPath: Get(values, "data"),
            GridName: Get(values, "grid", "density"),
            Width: ParseInt(values, "width"),
            Height: ParseInt(values, "height"),
            Depth: ParseInt(values, "depth"),
            VoxelSizeMeters: ParseFloat(values, "voxel_size_meters", 1f),
            OriginMeters: ParseVector3(values, "origin_meters", Vector3.Zero),
            ScaleMeters: ParseVector3(values, "scale_meters", Vector3.One),
            DensityMultiplier: ParseFloat(values, "density_multiplier", 1f),
            CanonicalSystem: Get(values, "canonical_system"),
            CanonicalNebula: Get(values, "canonical_nebula"),
            Source: Get(values, "source"),
            License: Get(values, "license"),
            PreserveZoneTransform: ParseBool(values, "preserve_zone_transform", true));
        return Validate(metadata);
    }

    private static VolumetricOpenVdbImportResult Validate(VolumetricOpenVdbImportMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.DataPath))
        {
            return VolumetricOpenVdbImportResult.Invalid("missing data path");
        }
        if (string.IsNullOrWhiteSpace(metadata.GridName))
        {
            return VolumetricOpenVdbImportResult.Invalid("missing grid name");
        }
        if (metadata.Width <= 0 || metadata.Height <= 0 || metadata.Depth <= 0)
        {
            return VolumetricOpenVdbImportResult.Invalid("invalid dimensions");
        }
        if (metadata.Width > 512 || metadata.Height > 512 || metadata.Depth > 512)
        {
            return VolumetricOpenVdbImportResult.Invalid("dimensions exceed runtime import budget");
        }
        if (metadata.VoxelSizeMeters <= 0f)
        {
            return VolumetricOpenVdbImportResult.Invalid("voxel size must be positive");
        }
        if (metadata.DensityMultiplier < 0f)
        {
            return VolumetricOpenVdbImportResult.Invalid("density multiplier must be non-negative");
        }
        if (!metadata.PreserveZoneTransform)
        {
            return VolumetricOpenVdbImportResult.Invalid("OpenVDB import must preserve canonical zone transform");
        }
        return new VolumetricOpenVdbImportResult(true, metadata, "");
    }

    private static string Get(Dictionary<string, string> values, string key, string fallback = "") =>
        values.TryGetValue(key, out var value) ? value : fallback;

    private static int ParseInt(Dictionary<string, string> values, string key, int fallback = 0) =>
        values.TryGetValue(key, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;

    private static float ParseFloat(Dictionary<string, string> values, string key, float fallback = 0f) =>
        values.TryGetValue(key, out var value) &&
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;

    private static bool ParseBool(Dictionary<string, string> values, string key, bool fallback) =>
        values.TryGetValue(key, out var value)
            ? value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
              value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
              value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            : fallback;

    private static Vector3 ParseVector3(Dictionary<string, string> values, string key, Vector3 fallback)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return fallback;
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            return fallback;
        }

        return float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
               float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
               float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
            ? new Vector3(x, y, z)
            : fallback;
    }
}

public readonly record struct VolumetricOpenVdbImportMetadata(
    string DataPath,
    string GridName,
    int Width,
    int Height,
    int Depth,
    float VoxelSizeMeters,
    Vector3 OriginMeters,
    Vector3 ScaleMeters,
    float DensityMultiplier,
    string CanonicalSystem,
    string CanonicalNebula,
    string Source,
    string License,
    bool PreserveZoneTransform)
{
    public string DebugSummary =>
        FormattableString.Invariant(
            $"{GridName} {Width}x{Height}x{Depth} voxel={VoxelSizeMeters:0.###}m density={DensityMultiplier:0.###} lock={(PreserveZoneTransform ? "zone" : "off")}");
}

public readonly record struct VolumetricOpenVdbImportResult(
    bool Valid,
    VolumetricOpenVdbImportMetadata Metadata,
    string Error)
{
    public static VolumetricOpenVdbImportResult Invalid(string error) =>
        new(false, default, error);
}
