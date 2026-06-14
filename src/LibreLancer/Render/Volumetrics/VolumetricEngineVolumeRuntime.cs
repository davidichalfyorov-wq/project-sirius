using System;
using System.Buffers.Binary;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Runtime-side reader for generated Phase 5 engine volume artifacts.
/// It validates canonical placement locks before exposing decoded density data.
/// </summary>
public static class VolumetricEngineVolumeRuntime
{
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
