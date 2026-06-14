using System;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// CPU-side bridge between validated OpenVDB-derived engine volumes and the
/// froxel density injection path. GPU sampling is wired in a later pass.
/// </summary>
public readonly record struct VolumetricImportedDensityFrame(
    bool Valid,
    VolumetricEngineVolumeDescriptor Descriptor,
    int SampleCount,
    float MinDensity,
    float MaxDensity,
    float MeanDensity,
    float Coverage,
    float[] UnitDensitySamples,
    string DebugSummary,
    string Error)
{
    private const float CoverageThreshold = 0.02f;

    public static VolumetricImportedDensityFrame FromRuntimeLoadResult(
        VolumetricEngineVolumeRuntimeLoadResult load,
        NebulaVolumeProfile profile,
        string canonicalSystem = "")
    {
        if (!load.Valid)
        {
            return Invalid(load.Error);
        }
        var binding = VolumetricEngineVolumeRuntime.ValidateBinding(
            load.Descriptor,
            profile,
            canonicalSystem);
        if (!binding.Valid)
        {
            return Invalid(binding.Error);
        }
        if (load.UnitDensitySamples.Length != load.Descriptor.VoxelCount)
        {
            return Invalid("imported density sample count does not match descriptor voxel count");
        }

        return FromUnitDensitySamples(load.Descriptor, load.UnitDensitySamples);
    }

    public static VolumetricImportedDensityFrame FromCacheManifest(
        System.Collections.Generic.IEnumerable<string> cacheManifestLines,
        NebulaVolumeProfile profile,
        VolumetricEngineVolumeArtifactLoader loader,
        string canonicalSystem = "",
        string requiredGrid = "density")
    {
        var load = VolumetricEngineVolumeRuntime.DecodeDenseArtifactFromManifest(
            cacheManifestLines,
            profile,
            loader,
            canonicalSystem,
            requiredGrid);
        return FromRuntimeLoadResult(load, profile, canonicalSystem);
    }

    public static VolumetricImportedDensityFrame FromUnitDensitySamples(
        VolumetricEngineVolumeDescriptor descriptor,
        ReadOnlySpan<float> unitDensitySamples)
    {
        if (!descriptor.Valid)
        {
            return Invalid("cannot bind imported density for invalid descriptor");
        }
        if (unitDensitySamples.Length != descriptor.VoxelCount)
        {
            return Invalid("imported density sample count does not match descriptor voxel count");
        }
        if (unitDensitySamples.Length == 0)
        {
            return Invalid("imported density has no samples");
        }

        var min = 1f;
        var max = 0f;
        double sum = 0.0;
        var covered = 0;
        var samples = new float[unitDensitySamples.Length];
        for (var i = 0; i < unitDensitySamples.Length; i++)
        {
            var v = Math.Clamp(unitDensitySamples[i], 0f, 1f);
            samples[i] = v;
            min = MathF.Min(min, v);
            max = MathF.Max(max, v);
            sum += v;
            if (v > CoverageThreshold)
            {
                covered++;
            }
        }

        var mean = (float)(sum / unitDensitySamples.Length);
        var coverage = covered / (float)unitDensitySamples.Length;
        var summary = FormattableString.Invariant(
            $"openvdb {descriptor.Width}x{descriptor.Height}x{descriptor.Depth} system={descriptor.CanonicalSystem} nebula={descriptor.CanonicalNebula} grid={descriptor.GridName} mean={mean:0.000} cov={coverage:0.000} hash={VolumetricOpenVdbImport.ContentHashPrefix(descriptor.ContentHash, 12)}");
        return new VolumetricImportedDensityFrame(
            true,
            descriptor,
            unitDensitySamples.Length,
            min,
            max,
            mean,
            coverage,
            samples,
            summary,
            "");
    }

    public bool MatchesProfile(NebulaVolumeProfile profile, string canonicalSystem = "") =>
        Valid &&
        VolumetricEngineVolumeRuntime.ValidateBinding(
            Descriptor,
            profile,
            canonicalSystem).Valid;

    public static VolumetricImportedDensityFrame Invalid(string error) =>
        new(false, VolumetricEngineVolumeDescriptor.Invalid(error), 0, 0f, 0f, 0f, 0f, [], "", error);
}
