using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricOpenVdbImportTests
{
    [Fact]
    public void ParsesLi01DensityManifestWithCanonicalLock()
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "grid = density",
            "width = 128",
            "height = 96",
            "depth = 64",
            "voxel_size_meters = 250",
            "origin_meters = 0, 0, 0",
            "scale_meters = 1, 1, 1",
            "density_multiplier = 0.85",
            "canonical_system = Li01",
            "canonical_nebula = li01_badlands",
            "source = blender_openvdb_export",
            "license = project-owned",
            "preserve_zone_transform = true"
        ]);

        Assert.True(result.Valid);
        Assert.Equal("li01_badlands_density.vdb", result.Metadata.DataPath);
        Assert.Equal("density", result.Metadata.GridName);
        Assert.Equal(128, result.Metadata.Width);
        Assert.Equal(96, result.Metadata.Height);
        Assert.Equal(64, result.Metadata.Depth);
        Assert.True(result.Metadata.PreserveZoneTransform);
        Assert.Contains("lock=zone", result.Metadata.DebugSummary);
    }

    [Fact]
    public void RejectsManifestsThatMoveCanonicalNebula()
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "preserve_zone_transform = false"
        ]);

        Assert.False(result.Valid);
        Assert.Contains("canonical zone transform", result.Error);
    }

    [Fact]
    public void RejectsVolumesAboveRuntimeBudget()
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = huge.vdb",
            "width = 1024",
            "height = 128",
            "depth = 128"
        ]);

        Assert.False(result.Valid);
        Assert.Contains("budget", result.Error);
    }
}
