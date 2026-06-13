using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricDepthMappingTests
{
    [Fact]
    public void DistanceMapsNearToZeroAndFarToOne()
    {
        var grid = FroxelGridDesc.MainForViewport(1920, 1080, 2);

        Assert.Equal(0f, VolumetricDepthMapping.DistanceToNormalizedSlice(grid.NearPlane, grid));
        Assert.Equal(1f, VolumetricDepthMapping.DistanceToNormalizedSlice(grid.FarPlane, grid));
    }

    [Fact]
    public void DistanceMappingKeepsMorePrecisionNearCamera()
    {
        var grid = FroxelGridDesc.MainForViewport(1920, 1080, 2);
        var quarterDistance = grid.NearPlane + (grid.FarPlane - grid.NearPlane) * 0.25f;

        var slice = VolumetricDepthMapping.DistanceToNormalizedSlice(quarterDistance, grid);

        Assert.True(slice > 0.25f);
        Assert.True(slice < 0.6f);
    }

    [Fact]
    public void MaterialFogSettingsExposeNearFarDepthAndExtinction()
    {
        var grid = FroxelGridDesc.MainForViewport(1280, 720, 1);
        var settings = VolumetricDepthMapping.MaterialFogSettings(grid, 0.012f);

        Assert.Equal(grid.NearPlane, settings.X);
        Assert.Equal(grid.FarPlane, settings.Y);
        Assert.Equal(grid.Depth, settings.Z);
        Assert.True(settings.W > 0f);
    }
}
