using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;

namespace LibreLancer.Render.Volumetrics;

public delegate bool VolumetricEngineVolumeArtifactLoader(string relativePath, out byte[] artifact);

/// <summary>
/// Runtime-side reader for generated Phase 5 engine volume artifacts.
/// It validates canonical placement locks before exposing decoded density data.
/// </summary>
public static class VolumetricEngineVolumeRuntime
{
    public static VolumetricEngineVolumeRuntimeLoadResult DecodeDenseArtifactFromManifest(
        IEnumerable<string> cacheManifestLines,
        NebulaVolumeProfile profile,
        VolumetricEngineVolumeArtifactLoader loader,
        string canonicalSystem = "",
        string requiredGrid = "density")
    {
        var manifest = VolumetricEngineVolumeCacheManifest.Parse(cacheManifestLines);
        if (!manifest.Valid)
        {
            return VolumetricEngineVolumeRuntimeLoadResult.Invalid(manifest.Error);
        }

        var manifestBinding = manifest.ValidateBinding(profile, canonicalSystem, requiredGrid);
        if (!manifestBinding.Valid)
        {
            return VolumetricEngineVolumeRuntimeLoadResult.Invalid(
                manifestBinding.Error,
                VolumetricEngineVolumeDescriptor.Invalid(manifestBinding.Error),
                manifestBinding);
        }

        if (loader == null ||
            !loader(manifest.CachePath, out var artifact) ||
            artifact.Length == 0)
        {
            return VolumetricEngineVolumeRuntimeLoadResult.Invalid(
                "engine volume cache artifact not found");
        }

        var decoded = DecodeDenseArtifact(artifact, profile, canonicalSystem, requiredGrid);
        if (!decoded.Valid)
        {
            return decoded;
        }

        var consistency = manifest.ValidateDescriptor(decoded.Descriptor);
        if (!consistency.Valid)
        {
            return VolumetricEngineVolumeRuntimeLoadResult.Invalid(
                consistency.Error,
                decoded.Descriptor,
                decoded.Binding);
        }

        return decoded;
    }

    public static VolumetricEngineVolumeRuntimeLoadResult DecodeDenseArtifact(
        ReadOnlySpan<byte> artifact,
        NebulaVolumeProfile profile,
        string canonicalSystem = "",
        string requiredGrid = "density")
    {
        var read = VolumetricEngineVolumeDescriptor.ReadDenseArtifact(artifact);
        if (!read.Valid)
        {
            return VolumetricEngineVolumeRuntimeLoadResult.Invalid(read.Error);
        }

        var binding = ValidateBinding(read.Descriptor, profile, canonicalSystem, requiredGrid);
        if (!binding.Valid)
        {
            return VolumetricEngineVolumeRuntimeLoadResult.Invalid(
                binding.Error,
                read.Descriptor,
                binding);
        }

        var decoded = DecodeUnitDensityPayload(
            artifact.Slice(read.PayloadOffset, read.PayloadBytes),
            read.Descriptor);
        if (!decoded.Valid)
        {
            return VolumetricEngineVolumeRuntimeLoadResult.Invalid(
                decoded.Error,
                read.Descriptor,
                binding);
        }

        return new VolumetricEngineVolumeRuntimeLoadResult(
            true,
            read.Descriptor,
            VolumetricDensitySource.OpenVdbImported,
            decoded.UnitDensitySamples,
            binding,
            "");
    }

    public static VolumetricEngineVolumeRuntimeBinding ValidateBinding(
        VolumetricEngineVolumeDescriptor descriptor,
        NebulaVolumeProfile profile,
        string canonicalSystem = "",
        string requiredGrid = "density")
    {
        if (!descriptor.Valid)
        {
            return VolumetricEngineVolumeRuntimeBinding.Invalid(
                "cannot bind invalid engine volume descriptor");
        }
        if (!profile.IsValid)
        {
            return VolumetricEngineVolumeRuntimeBinding.Invalid(
                "cannot bind engine volume without an active nebula profile");
        }
        if (!string.IsNullOrWhiteSpace(canonicalSystem))
        {
            if (string.IsNullOrWhiteSpace(descriptor.CanonicalSystem))
            {
                return VolumetricEngineVolumeRuntimeBinding.Invalid(
                    "engine volume missing canonical system lock");
            }
            if (!MatchesCanonical(descriptor.CanonicalSystem, canonicalSystem))
            {
                return VolumetricEngineVolumeRuntimeBinding.Invalid(
                    "engine volume canonical system does not match active system");
            }
        }
        if (string.IsNullOrWhiteSpace(descriptor.CanonicalNebula))
        {
            return VolumetricEngineVolumeRuntimeBinding.Invalid(
                "engine volume missing canonical nebula lock");
        }
        if (!MatchesCanonical(descriptor.CanonicalNebula, profile.Nickname) &&
            !MatchesCanonical(descriptor.CanonicalNebula, profile.SourceFile))
        {
            return VolumetricEngineVolumeRuntimeBinding.Invalid(
                "engine volume canonical nebula does not match active profile");
        }
        if (!string.IsNullOrWhiteSpace(requiredGrid) &&
            !MatchesCanonical(descriptor.GridName, requiredGrid))
        {
            return VolumetricEngineVolumeRuntimeBinding.Invalid(
                "engine volume grid does not match required density grid");
        }

        return VolumetricEngineVolumeRuntimeBinding.Ok();
    }

    public static VolumetricEngineVolumePayloadDecodeResult DecodeUnitDensityPayload(
        ReadOnlySpan<byte> payload,
        VolumetricEngineVolumeDescriptor descriptor)
    {
        var validation = VolumetricEngineVolumeDescriptor.ValidatePayload(payload, descriptor);
        if (!validation.Valid)
        {
            return VolumetricEngineVolumePayloadDecodeResult.Invalid(validation.Error);
        }
        if (descriptor.VoxelCount > int.MaxValue)
        {
            return VolumetricEngineVolumePayloadDecodeResult.Invalid(
                "engine volume payload is too large for managed runtime decode");
        }

        var samples = new float[checked((int)descriptor.VoxelCount)];
        var offset = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = descriptor.Format switch
            {
                VolumetricEngineVolumeFormat.DensityR8UNorm =>
                    payload[offset++] / (float)byte.MaxValue,
                VolumetricEngineVolumeFormat.DensityR16UNorm =>
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        payload.Slice(Advance(ref offset, sizeof(ushort)), sizeof(ushort))) /
                    (float)ushort.MaxValue,
                VolumetricEngineVolumeFormat.DensityR16Float =>
                    Math.Clamp(
                        (float)BitConverter.UInt16BitsToHalf(
                            BinaryPrimitives.ReadUInt16LittleEndian(
                                payload.Slice(Advance(ref offset, sizeof(ushort)), sizeof(ushort)))),
                        0f,
                        1f),
                _ => 0f
            };
        }

        return new VolumetricEngineVolumePayloadDecodeResult(true, samples, "");
    }

    private static int Advance(ref int offset, int count)
    {
        var start = offset;
        offset += count;
        return start;
    }

    private static bool MatchesCanonical(string authored, string runtime) =>
        string.Equals(
            NormalizeCanonical(authored),
            NormalizeCanonical(runtime),
            StringComparison.Ordinal);

    private static string NormalizeCanonical(string value)
    {
        var normalized = value.Trim().Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        if (slash >= 0)
        {
            normalized = normalized[(slash + 1)..];
        }

        var dot = normalized.LastIndexOf('.');
        if (dot > 0)
        {
            normalized = normalized[..dot];
        }

        return VolumetricOpenVdbImport.CacheSlug(normalized);
    }

    internal static bool MatchesCanonicalName(string authored, string runtime) =>
        MatchesCanonical(authored, runtime);
}

public readonly record struct VolumetricEngineVolumeCacheManifest(
    bool Valid,
    string CachePath,
    string ManifestPath,
    string CanonicalSystem,
    string CanonicalNebula,
    string GridName,
    int Width,
    int Height,
    int Depth,
    string ContentHash,
    string Error)
{
    public static VolumetricEngineVolumeCacheManifest Parse(IEnumerable<string> lines)
    {
        if (!TryParseKeyValueLines(lines, out var values, out var parseError))
        {
            return Invalid(parseError);
        }
        if (!Required(values, "cache_version", out var version) ||
            !string.Equals(version, "1", StringComparison.Ordinal))
        {
            return Invalid("engine volume cache manifest version mismatch");
        }
        if (!Required(values, "cache", out var cachePath) ||
            !IsSafeRelativeCachePath(cachePath) ||
            !cachePath.EndsWith(".siriusvol", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("engine volume cache manifest has invalid cache path");
        }
        if (!Required(values, "manifest", out var manifestPath) ||
            !IsSafeRelativeCachePath(manifestPath) ||
            !manifestPath.EndsWith(".siriusvol.manifest", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("engine volume cache manifest has invalid manifest path");
        }
        if (!Required(values, "canonical_nebula", out var canonicalNebula))
        {
            return Invalid("engine volume cache manifest missing canonical nebula lock");
        }
        if (!Required(values, "grid", out var grid))
        {
            return Invalid("engine volume cache manifest missing density grid");
        }
        if (!RequiredDimensions(values, out var width, out var height, out var depth))
        {
            return Invalid("engine volume cache manifest invalid dimensions");
        }
        if (!Required(values, "content_hash", out var contentHash) ||
            VolumetricOpenVdbImport.ContentHashBody(contentHash).Length == 0)
        {
            return Invalid("engine volume cache manifest invalid content hash");
        }
        values.TryGetValue("canonical_system", out var canonicalSystem);

        return new VolumetricEngineVolumeCacheManifest(
            true,
            cachePath,
            manifestPath,
            canonicalSystem ?? "",
            canonicalNebula,
            grid,
            width,
            height,
            depth,
            contentHash,
            "");
    }

    public VolumetricEngineVolumeRuntimeBinding ValidateBinding(
        NebulaVolumeProfile profile,
        string canonicalSystem = "",
        string requiredGrid = "density")
    {
        if (!Valid)
        {
            return VolumetricEngineVolumeRuntimeBinding.Invalid(
                "cannot bind invalid engine volume cache manifest");
        }
        if (!profile.IsValid)
        {
            return VolumetricEngineVolumeRuntimeBinding.Invalid(
                "cannot bind engine volume cache without an active nebula profile");
        }
        if (!string.IsNullOrWhiteSpace(canonicalSystem))
        {
            if (string.IsNullOrWhiteSpace(CanonicalSystem))
            {
                return VolumetricEngineVolumeRuntimeBinding.Invalid(
                    "engine volume cache manifest missing canonical system lock");
            }
            if (!VolumetricEngineVolumeRuntime.MatchesCanonicalName(CanonicalSystem, canonicalSystem))
            {
                return VolumetricEngineVolumeRuntimeBinding.Invalid(
                    "engine volume cache manifest canonical system does not match active system");
            }
        }
        if (!VolumetricEngineVolumeRuntime.MatchesCanonicalName(CanonicalNebula, profile.Nickname) &&
            !VolumetricEngineVolumeRuntime.MatchesCanonicalName(CanonicalNebula, profile.SourceFile))
        {
            return VolumetricEngineVolumeRuntimeBinding.Invalid(
                "engine volume cache manifest canonical nebula does not match active profile");
        }
        if (!string.IsNullOrWhiteSpace(requiredGrid) &&
            !VolumetricEngineVolumeRuntime.MatchesCanonicalName(GridName, requiredGrid))
        {
            return VolumetricEngineVolumeRuntimeBinding.Invalid(
                "engine volume cache manifest grid does not match required density grid");
        }

        return VolumetricEngineVolumeRuntimeBinding.Ok();
    }

    public VolumetricEngineVolumeCacheManifestValidation ValidateDescriptor(
        VolumetricEngineVolumeDescriptor descriptor)
    {
        if (!Valid)
        {
            return VolumetricEngineVolumeCacheManifestValidation.Invalid(
                "cannot validate descriptor against invalid engine volume cache manifest");
        }
        if (!descriptor.Valid)
        {
            return VolumetricEngineVolumeCacheManifestValidation.Invalid(
                "engine volume cache artifact descriptor is invalid");
        }
        if (!string.Equals(CachePath, descriptor.EngineVolumePath, StringComparison.Ordinal))
        {
            return VolumetricEngineVolumeCacheManifestValidation.Invalid(
                "engine volume cache manifest/artifact cache path mismatch");
        }
        if (!string.Equals(ManifestPath, descriptor.CacheManifestPath, StringComparison.Ordinal))
        {
            return VolumetricEngineVolumeCacheManifestValidation.Invalid(
                "engine volume cache manifest/artifact manifest path mismatch");
        }
        if (!VolumetricEngineVolumeRuntime.MatchesCanonicalName(CanonicalSystem, descriptor.CanonicalSystem) ||
            !VolumetricEngineVolumeRuntime.MatchesCanonicalName(CanonicalNebula, descriptor.CanonicalNebula) ||
            !VolumetricEngineVolumeRuntime.MatchesCanonicalName(GridName, descriptor.GridName))
        {
            return VolumetricEngineVolumeCacheManifestValidation.Invalid(
                "engine volume cache manifest/artifact canonical binding mismatch");
        }
        if (Width != descriptor.Width ||
            Height != descriptor.Height ||
            Depth != descriptor.Depth)
        {
            return VolumetricEngineVolumeCacheManifestValidation.Invalid(
                "engine volume cache manifest/artifact dimensions mismatch");
        }
        if (!string.Equals(ContentHash, descriptor.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            return VolumetricEngineVolumeCacheManifestValidation.Invalid(
                "engine volume cache manifest/artifact content hash mismatch");
        }

        return VolumetricEngineVolumeCacheManifestValidation.Ok();
    }

    public static VolumetricEngineVolumeCacheManifest Invalid(string error) =>
        new(false, "", "", "", "", "", 0, 0, 0, "", error);

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
                error = $"invalid engine volume cache manifest line '{raw}'";
                return false;
            }

            values[line[..split].Trim()] = line[(split + 1)..].Trim();
        }

        error = "";
        return true;
    }

    private static bool Required(
        Dictionary<string, string> values,
        string key,
        out string value)
    {
        if (values.TryGetValue(key, out var found) && found.Trim().Length > 0)
        {
            value = found.Trim();
            return true;
        }

        value = "";
        return false;
    }

    private static bool RequiredDimensions(
        Dictionary<string, string> values,
        out int width,
        out int height,
        out int depth)
    {
        width = 0;
        height = 0;
        depth = 0;
        if (!Required(values, "dimensions", out var raw))
        {
            return false;
        }

        var parts = raw.Split(',');
        if (parts.Length != 3)
        {
            return false;
        }

        return int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out width) &&
            int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out height) &&
            int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out depth) &&
            width > 0 &&
            height > 0 &&
            depth > 0;
    }

    private static bool IsSafeRelativeCachePath(string value)
    {
        var path = value.Trim();
        if (path.Length == 0 ||
            path.StartsWith('/') ||
            path.StartsWith('\\') ||
            path.Contains('\\') ||
            path.Contains(':') ||
            path.Contains("artist_exports", StringComparison.OrdinalIgnoreCase))
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

public readonly record struct VolumetricEngineVolumeRuntimeLoadResult(
    bool Valid,
    VolumetricEngineVolumeDescriptor Descriptor,
    VolumetricDensitySource DensitySource,
    float[] UnitDensitySamples,
    VolumetricEngineVolumeRuntimeBinding Binding,
    string Error)
{
    public static VolumetricEngineVolumeRuntimeLoadResult Invalid(string error) =>
        Invalid(error, VolumetricEngineVolumeDescriptor.Invalid(error), VolumetricEngineVolumeRuntimeBinding.Invalid(error));

    public static VolumetricEngineVolumeRuntimeLoadResult Invalid(
        string error,
        VolumetricEngineVolumeDescriptor descriptor,
        VolumetricEngineVolumeRuntimeBinding binding) =>
        new(false, descriptor, VolumetricDensitySource.PerlinWorleyRuntime, [], binding, error);
}

public readonly record struct VolumetricEngineVolumeRuntimeBinding(
    bool Valid,
    string Error)
{
    public static VolumetricEngineVolumeRuntimeBinding Ok() =>
        new(true, "");

    public static VolumetricEngineVolumeRuntimeBinding Invalid(string error) =>
        new(false, error);
}

public readonly record struct VolumetricEngineVolumePayloadDecodeResult(
    bool Valid,
    float[] UnitDensitySamples,
    string Error)
{
    public static VolumetricEngineVolumePayloadDecodeResult Invalid(string error) =>
        new(false, [], error);
}

public readonly record struct VolumetricEngineVolumeCacheManifestValidation(
    bool Valid,
    string Error)
{
    public static VolumetricEngineVolumeCacheManifestValidation Ok() =>
        new(true, "");

    public static VolumetricEngineVolumeCacheManifestValidation Invalid(string error) =>
        new(false, error);
}
