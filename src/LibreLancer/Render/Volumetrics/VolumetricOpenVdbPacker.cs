using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Shared offline packing logic for reviewed OpenVDB density exports. This
/// stays independent of OpenVDB itself: external DCC/converter tools provide
/// raw float32 density samples after reading VDB grids.
/// </summary>
public static class VolumetricOpenVdbPacker
{
    public static VolumetricOpenVdbPackResult BuildDenseArtifact(
        IEnumerable<string> manifestLines,
        NebulaVolumeProfile profile,
        ReadOnlySpan<byte> sourcePayload,
        ReadOnlySpan<byte> rawFloat32DensitySamples,
        string canonicalSystem = "",
        VolumetricEngineVolumeFormat format = VolumetricEngineVolumeFormat.DensityR16UNorm)
    {
        var artifactPlan = VolumetricOpenVdbImport.CreateCacheArtifactPlan(
            manifestLines,
            profile,
            sourcePayload,
            canonicalSystem);
        if (!artifactPlan.Valid)
        {
            return VolumetricOpenVdbPackResult.Invalid(artifactPlan.Error);
        }

        var descriptor = VolumetricEngineVolumeDescriptor.FromOpenVdbArtifact(artifactPlan, format);
        if (!descriptor.Valid)
        {
            return VolumetricOpenVdbPackResult.Invalid(descriptor.Error);
        }
        if (descriptor.VoxelCount > int.MaxValue)
        {
            return VolumetricOpenVdbPackResult.Invalid(
                "OpenVDB dense sample count is too large for managed packing");
        }

        var densitySamples = DecodeAndNormalizeFloat32Samples(
            rawFloat32DensitySamples,
            checked((int)descriptor.VoxelCount),
            artifactPlan.ImportPlan);
        if (!densitySamples.Valid)
        {
            return VolumetricOpenVdbPackResult.Invalid(densitySamples.Error);
        }

        var artifact = VolumetricEngineVolumeDescriptor.BuildDenseArtifact(
            densitySamples.UnitDensitySamples,
            descriptor);
        if (!artifact.Valid)
        {
            return VolumetricOpenVdbPackResult.Invalid(artifact.Error);
        }

        return new VolumetricOpenVdbPackResult(
            true,
            artifactPlan,
            descriptor,
            artifact.Artifact,
            artifactPlan.CacheManifestLines,
            "");
    }

    public static VolumetricOpenVdbDensitySampleDecodeResult DecodeAndNormalizeFloat32Samples(
        ReadOnlySpan<byte> rawFloat32DensitySamples,
        int expectedSamples,
        VolumetricOpenVdbImportPlan importPlan)
    {
        if (!importPlan.Valid)
        {
            return VolumetricOpenVdbDensitySampleDecodeResult.Invalid(
                "cannot decode OpenVDB density samples for invalid import plan");
        }
        if (expectedSamples <= 0)
        {
            return VolumetricOpenVdbDensitySampleDecodeResult.Invalid(
                "OpenVDB expected sample count must be positive");
        }
        if (rawFloat32DensitySamples.Length % sizeof(float) != 0)
        {
            return VolumetricOpenVdbDensitySampleDecodeResult.Invalid(
                "OpenVDB density sample payload must be raw little-endian float32 values");
        }

        var sampleCount = rawFloat32DensitySamples.Length / sizeof(float);
        if (sampleCount != expectedSamples)
        {
            return VolumetricOpenVdbDensitySampleDecodeResult.Invalid(
                "OpenVDB density sample count mismatch");
        }

        var unitSamples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var bits = BinaryPrimitives.ReadInt32LittleEndian(
                rawFloat32DensitySamples[(i * sizeof(float))..((i + 1) * sizeof(float))]);
            unitSamples[i] = importPlan.NormalizeDensity(BitConverter.Int32BitsToSingle(bits));
        }

        return new VolumetricOpenVdbDensitySampleDecodeResult(true, unitSamples, "");
    }
}

public readonly record struct VolumetricOpenVdbPackResult(
    bool Valid,
    VolumetricOpenVdbCacheArtifactPlan ArtifactPlan,
    VolumetricEngineVolumeDescriptor Descriptor,
    byte[] Artifact,
    string[] CacheManifestLines,
    string Error)
{
    public static VolumetricOpenVdbPackResult Invalid(string error) =>
        new(
            false,
            VolumetricOpenVdbCacheArtifactPlan.Invalid(error),
            VolumetricEngineVolumeDescriptor.Invalid(error),
            [],
            [],
            error);
}

public readonly record struct VolumetricOpenVdbDensitySampleDecodeResult(
    bool Valid,
    float[] UnitDensitySamples,
    string Error)
{
    public static VolumetricOpenVdbDensitySampleDecodeResult Invalid(string error) =>
        new(false, [], error);
}
