using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Metadata contract for future OpenVDB nebula density imports. The runtime
/// still uses procedural density until a decoder is added; this parser locks
/// the sidecar format and forbids authored volumes from moving canonical
/// Freelancer nebula zones.
/// </summary>
public static class VolumetricOpenVdbImport
{
    public const long MaxRuntimeVoxelCount = 256L * 256L * 256L;

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

        if (!ValidateTypedManifestValues(values, out var typedError))
        {
            return VolumetricOpenVdbImportResult.Invalid(typedError);
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
            SourceFile: Get(values, "source_file", Get(values, "dcc_file")),
            License: Get(values, "license"),
            ContentHash: GetContentHash(values),
            PreserveZoneTransform: ParseBool(values, "preserve_zone_transform", true));
        return Validate(metadata);
    }

    public static VolumetricOpenVdbHashVerification VerifyContentHash(
        ReadOnlySpan<byte> payload,
        VolumetricOpenVdbImportMetadata metadata) =>
        VerifyContentHash(payload, metadata.ContentHash);

    public static VolumetricOpenVdbHashVerification VerifyContentHash(
        ReadOnlySpan<byte> payload,
        string contentHash)
    {
        if (!TryParseContentHash(contentHash, out var algorithm, out var expected, out var error))
        {
            return VolumetricOpenVdbHashVerification.Invalid(error);
        }

        if (!algorithm.Equals("sha256", StringComparison.OrdinalIgnoreCase))
        {
            return VolumetricOpenVdbHashVerification.Invalid(
                "BLAKE3 content hash verification is reserved for the offline import tool");
        }

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(payload, hash);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        var normalizedExpected = expected.ToLowerInvariant();
        var matched = string.Equals(actual, normalizedExpected, StringComparison.OrdinalIgnoreCase);
        return new VolumetricOpenVdbHashVerification(
            Supported: true,
            Matched: matched,
            Algorithm: algorithm.ToLowerInvariant(),
            Expected: normalizedExpected,
            Actual: actual,
            Error: matched ? "" : "content hash mismatch");
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
        if (string.IsNullOrWhiteSpace(metadata.CanonicalNebula))
        {
            return VolumetricOpenVdbImportPlan.Invalid("OpenVDB manifest missing canonical nebula lock");
        }
        if (!string.IsNullOrWhiteSpace(canonicalSystem) &&
            string.IsNullOrWhiteSpace(metadata.CanonicalSystem))
        {
            return VolumetricOpenVdbImportPlan.Invalid("OpenVDB manifest missing canonical system lock");
        }
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
        if (!IsSafeRelativeAssetPath(metadata.DataPath))
        {
            return VolumetricOpenVdbImportResult.Invalid("OpenVDB data path must be a safe relative asset path");
        }
        if (string.IsNullOrWhiteSpace(metadata.GridName))
        {
            return VolumetricOpenVdbImportResult.Invalid("missing grid name");
        }
        if (string.IsNullOrWhiteSpace(metadata.Source))
        {
            return VolumetricOpenVdbImportResult.Invalid("missing source metadata");
        }
        if (string.IsNullOrWhiteSpace(metadata.SourceFile))
        {
            return VolumetricOpenVdbImportResult.Invalid("missing source file metadata");
        }
        if (!IsSafeRelativeAssetPath(metadata.SourceFile))
        {
            return VolumetricOpenVdbImportResult.Invalid("OpenVDB source file must be a safe relative asset path");
        }
        if (string.IsNullOrWhiteSpace(metadata.License))
        {
            return VolumetricOpenVdbImportResult.Invalid("missing license metadata");
        }
        if (string.IsNullOrWhiteSpace(metadata.ContentHash))
        {
            return VolumetricOpenVdbImportResult.Invalid("missing content hash metadata");
        }
        if (!IsSupportedContentHash(metadata.ContentHash))
        {
            return VolumetricOpenVdbImportResult.Invalid("content hash must be sha256:<64 hex> or blake3:<64 hex>");
        }
        if (metadata.Width <= 0 || metadata.Height <= 0 || metadata.Depth <= 0)
        {
            return VolumetricOpenVdbImportResult.Invalid("invalid dimensions");
        }
        if (metadata.Width > 512 || metadata.Height > 512 || metadata.Depth > 512)
        {
            return VolumetricOpenVdbImportResult.Invalid("dimensions exceed runtime import budget");
        }
        if (metadata.VoxelCount > MaxRuntimeVoxelCount)
        {
            return VolumetricOpenVdbImportResult.Invalid("voxel count exceeds runtime import budget");
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

    private static string GetContentHash(Dictionary<string, string> values)
    {
        if (values.TryGetValue("content_hash", out var contentHash))
        {
            return contentHash;
        }
        if (values.TryGetValue("sha256", out var sha256))
        {
            return PrefixHash("sha256", sha256);
        }
        if (values.TryGetValue("blake3", out var blake3))
        {
            return PrefixHash("blake3", blake3);
        }
        return Get(values, "hash");
    }

    private static string PrefixHash(string algorithm, string value)
    {
        var hash = value.Trim();
        return hash.Contains(':') ? hash : $"{algorithm}:{hash}";
    }

    private static bool ValidateTypedManifestValues(Dictionary<string, string> values, out string error)
    {
        foreach (var key in new[] { "width", "height", "depth" })
        {
            if (values.TryGetValue(key, out var value) &&
                !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                error = $"invalid {key}";
                return false;
            }
        }

        foreach (var key in new[]
                 {
                     "voxel_size_meters",
                     "density_min",
                     "density_max",
                     "density_multiplier"
                 })
        {
            if (values.TryGetValue(key, out var value) &&
                !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                error = $"invalid {key}";
                return false;
            }
        }

        foreach (var key in new[] { "origin_meters", "scale_meters" })
        {
            if (values.TryGetValue(key, out var value) &&
                !TryParseVector3(value, out _))
            {
                error = $"invalid {key}";
                return false;
            }
        }

        if (values.TryGetValue("preserve_zone_transform", out var preserve) &&
            !TryParseBool(preserve, out _))
        {
            error = "invalid preserve_zone_transform";
            return false;
        }

        error = "";
        return true;
    }

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
        values.TryGetValue(key, out var value) && TryParseBool(value, out var result)
            ? result
            : fallback;

    private static Vector3 ParseVector3(Dictionary<string, string> values, string key, Vector3 fallback)
    {
        return values.TryGetValue(key, out var value) && TryParseVector3(value, out var result)
            ? result
            : fallback;
    }

    private static bool TryParseVector3(string value, out Vector3 result)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            result = default;
            return false;
        }

        if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
            float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
        {
            result = new Vector3(x, y, z);
            return true;
        }

        result = default;
        return false;
    }

    private static bool TryParseBool(string value, out bool result)
    {
        var normalized = value.Trim();
        if (normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }
        if (normalized.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
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

    private static bool IsSupportedContentHash(string value) =>
        TryParseContentHash(value, out _, out _, out _);

    private static bool TryParseContentHash(
        string value,
        out string algorithm,
        out string hex,
        out string error)
    {
        algorithm = "";
        hex = "";
        error = "";
        var hash = value.Trim();
        var split = hash.IndexOf(':');
        if (split <= 0)
        {
            error = "content hash must include an algorithm prefix";
            return false;
        }

        algorithm = hash[..split].Trim().ToLowerInvariant();
        if (algorithm is not ("sha256" or "blake3"))
        {
            error = "unsupported content hash algorithm";
            return false;
        }

        hex = hash[(split + 1)..].Trim();
        if (hex.Length != 64)
        {
            error = "content hash must be 64 hex characters";
            return false;
        }
        foreach (var ch in hex)
        {
            if (!Uri.IsHexDigit(ch))
            {
                error = "content hash must be 64 hex characters";
                return false;
            }
        }
        return true;
    }

    private static bool IsSafeRelativeAssetPath(string value)
    {
        var path = value.Trim();
        if (path.Length == 0 ||
            path.StartsWith('/') ||
            path.StartsWith('\\') ||
            path.Contains('\\') ||
            path.Contains(':'))
        {
            return false;
        }

        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                return false;
            }
        }
        return true;
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
    string SourceFile,
    string License,
    string ContentHash,
    bool PreserveZoneTransform)
{
    public long VoxelCount => (long)Width * Height * Depth;

    public long EstimatedRawDensityBytes => VoxelCount * sizeof(float);

    public float EstimatedRawDensityMiB => EstimatedRawDensityBytes / (1024f * 1024f);

    public string DebugSummary =>
        FormattableString.Invariant(
            $"{GridName} {Width}x{Height}x{Depth} voxels={VoxelCount} raw={EstimatedRawDensityMiB:0.##}MiB voxel={VoxelSizeMeters:0.###}m density={DensityMin:0.###}-{DensityMax:0.###}x{DensityMultiplier:0.###} axis={AxisConvention} bounds={BoundsMode} placement={PlacementMode} lock={(PreserveZoneTransform ? "zone" : "off")} source={Source} file={SourceFile} hash={ShortHash(ContentHash)} license={License}");

    private static string ShortHash(string hash)
    {
        var normalized = hash.Trim();
        var split = normalized.IndexOf(':');
        var prefix = split > 0 ? normalized[..(split + 1)] : "";
        var body = split > 0 ? normalized[(split + 1)..] : normalized;
        return body.Length <= 12 ? normalized : prefix + body[..12];
    }
}

public readonly record struct VolumetricOpenVdbImportResult(
    bool Valid,
    VolumetricOpenVdbImportMetadata Metadata,
    string Error)
{
    public static VolumetricOpenVdbImportResult Invalid(string error) =>
        new(false, default, error);
}

public readonly record struct VolumetricOpenVdbHashVerification(
    bool Supported,
    bool Matched,
    string Algorithm,
    string Expected,
    string Actual,
    string Error)
{
    public static VolumetricOpenVdbHashVerification Invalid(string error) =>
        new(false, false, "", "", "", error);
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
