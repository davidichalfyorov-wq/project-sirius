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
    private static readonly string[] ForbiddenPlacementKeys =
    [
        "position",
        "position_meters",
        "world_position",
        "world_position_meters",
        "zone_position",
        "zone_position_meters",
        "translation",
        "translation_meters",
        "translate",
        "offset",
        "offset_meters",
        "rotation",
        "rotation_degrees",
        "orientation",
        "transform",
        "transform_matrix",
        "world_transform",
        "zone_transform"
    ];

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

        foreach (var key in ForbiddenPlacementKeys)
        {
            if (values.ContainsKey(key))
            {
                return VolumetricOpenVdbImportResult.Invalid(
                    $"OpenVDB manifest may not override canonical nebula placement with '{key}'");
            }
        }

        var metadata = new VolumetricOpenVdbImportMetadata(
            DataPath: Get(values, "data"),
            GridName: Get(values, "grid", "density"),
            ProfileNickname: Get(values, "profile", Get(values, "profile_nickname")),
            Width: ParseInt(values, "width"),
            Height: ParseInt(values, "height"),
            Depth: ParseInt(values, "depth"),
            VoxelSizeMeters: ParseFloat(values, "voxel_size_meters", 1f),
            OriginMeters: ParseVector3(values, "origin_meters", Vector3.Zero),
            ScaleMeters: ParseVector3(values, "scale_meters", Vector3.One),
            DensityMin: ParseFloat(values, "density_min", 0f),
            DensityMax: ParseFloat(values, "density_max", 1f),
            DensityMultiplier: ParseFloat(values, "density_multiplier", 1f),
            AxisConvention: Get(values, "axis", Get(values, "axis_convention", "freelancer_y_up")),
            BoundsMode: Get(values, "bounds", Get(values, "bounds_mode", "zone_local")),
            PlacementMode: Get(values, "placement", Get(values, "placement_mode", "zone_locked")),
            CanonicalSystem: Get(values, "canonical_system"),
            CanonicalNebula: Get(values, "canonical_nebula"),
            Source: Get(values, "source"),
            License: Get(values, "license"),
            PreserveZoneTransform: ParseBool(values, "preserve_zone_transform", true));
        return Validate(metadata);
    }

    public static VolumetricOpenVdbImportPlan CreateImportPlan(
        IEnumerable<string> lines,
        NebulaVolumeProfile profile,
        string canonicalSystem = "")
    {
        var parsed = ParseManifest(lines);
        if (!parsed.Valid)
        {
            return VolumetricOpenVdbImportPlan.Invalid(parsed.Error);
        }

        var metadata = parsed.Metadata;
        if (!string.IsNullOrWhiteSpace(metadata.CanonicalSystem) &&
            !string.IsNullOrWhiteSpace(canonicalSystem) &&
            !Matches(metadata.CanonicalSystem, canonicalSystem))
        {
            return VolumetricOpenVdbImportPlan.Invalid("OpenVDB manifest canonical system does not match active system");
        }

        if (!string.IsNullOrWhiteSpace(metadata.CanonicalNebula) &&
            !Matches(metadata.CanonicalNebula, profile.Nickname) &&
            !Matches(metadata.CanonicalNebula, profile.SourceFile))
        {
            return VolumetricOpenVdbImportPlan.Invalid("OpenVDB manifest canonical nebula does not match active profile");
        }

        var nickname = string.IsNullOrWhiteSpace(metadata.ProfileNickname)
            ? profile.Nickname
            : metadata.ProfileNickname;
        var range = MathF.Max(metadata.DensityMax - metadata.DensityMin, 1e-5f);
        var scale = metadata.DensityMultiplier / range;
        var bias = -metadata.DensityMin * scale;
        return new VolumetricOpenVdbImportPlan(
            true,
            metadata with { ProfileNickname = nickname },
            VolumetricDensitySource.OpenVdbImported,
            new Vector2(scale, bias),
            "");
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
        if (metadata.ScaleMeters.X <= 0f || metadata.ScaleMeters.Y <= 0f || metadata.ScaleMeters.Z <= 0f)
        {
            return VolumetricOpenVdbImportResult.Invalid("scale must be positive");
        }
        if (metadata.DensityMax <= metadata.DensityMin)
        {
            return VolumetricOpenVdbImportResult.Invalid("density range must be positive");
        }
        if (metadata.DensityMultiplier < 0f)
        {
            return VolumetricOpenVdbImportResult.Invalid("density multiplier must be non-negative");
        }
        if (!IsSupportedAxisConvention(metadata.AxisConvention))
        {
            return VolumetricOpenVdbImportResult.Invalid("unsupported axis convention");
        }
        if (!IsSupportedBoundsMode(metadata.BoundsMode))
        {
            return VolumetricOpenVdbImportResult.Invalid("unsupported bounds mode");
        }
        if (!IsSupportedPlacementMode(metadata.PlacementMode))
        {
            return VolumetricOpenVdbImportResult.Invalid("OpenVDB import placement must be zone_locked");
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

    private static bool Matches(string authored, string runtime) =>
        string.Equals(NormalizeName(authored), NormalizeName(runtime), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeName(string value) =>
        value.Trim().Replace('\\', '/');

    private static bool IsSupportedAxisConvention(string value)
    {
        var axis = value.Trim().Replace("-", "_").ToLowerInvariant();
        return axis is "freelancer_y_up" or "y_up" or "z_up";
    }

    private static bool IsSupportedBoundsMode(string value)
    {
        var bounds = value.Trim().Replace("-", "_").ToLowerInvariant();
        return bounds is "zone_local" or "zone_normalized" or "local_bounds" or "unit_cube";
    }

    private static bool IsSupportedPlacementMode(string value)
    {
        var placement = value.Trim().Replace("-", "_").ToLowerInvariant();
        return placement is "zone_locked" or "canonical_zone";
    }
}

public readonly record struct VolumetricOpenVdbImportMetadata(
    string DataPath,
    string GridName,
    string ProfileNickname,
    int Width,
    int Height,
    int Depth,
    float VoxelSizeMeters,
    Vector3 OriginMeters,
    Vector3 ScaleMeters,
    float DensityMin,
    float DensityMax,
    float DensityMultiplier,
    string AxisConvention,
    string BoundsMode,
    string PlacementMode,
    string CanonicalSystem,
    string CanonicalNebula,
    string Source,
    string License,
    bool PreserveZoneTransform)
{
    public string DebugSummary =>
        FormattableString.Invariant(
            $"{GridName} {Width}x{Height}x{Depth} voxel={VoxelSizeMeters:0.###}m density={DensityMin:0.###}-{DensityMax:0.###}x{DensityMultiplier:0.###} axis={AxisConvention} bounds={BoundsMode} placement={PlacementMode} lock={(PreserveZoneTransform ? "zone" : "off")}");
}

public readonly record struct VolumetricOpenVdbImportResult(
    bool Valid,
    VolumetricOpenVdbImportMetadata Metadata,
    string Error)
{
    public static VolumetricOpenVdbImportResult Invalid(string error) =>
        new(false, default, error);
}

public readonly record struct VolumetricOpenVdbImportPlan(
    bool Valid,
    VolumetricOpenVdbImportMetadata Metadata,
    VolumetricDensitySource DensitySource,
    Vector2 DensityNormalize,
    string Error)
{
    public float NormalizeDensity(float rawDensity) =>
        Math.Clamp(rawDensity * DensityNormalize.X + DensityNormalize.Y, 0f, Metadata.DensityMultiplier);

    public string DebugSummary => !Valid
        ? $"invalid: {Error}"
        : FormattableString.Invariant(
            $"{Metadata.ProfileNickname}: {Metadata.DebugSummary} normalize=({DensityNormalize.X:0.###},{DensityNormalize.Y:0.###})");

    public static VolumetricOpenVdbImportPlan Invalid(string error) =>
        new(false, default, VolumetricDensitySource.PerlinWorleyRuntime, Vector2.Zero, error);
}
