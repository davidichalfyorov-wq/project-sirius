using LibreLancer.Graphics;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class FroxelGridDescTests
{
    [Theory]
    [InlineData(0, 1280, 720)]
    [InlineData(1, 1280, 720)]
    [InlineData(2, 1920, 1080)]
    [InlineData(3, 3840, 2160)]
    public void MainGridIsValidAndAligned(int quality, int width, int height)
    {
        var desc = FroxelGridDesc.MainForViewport(width, height, quality);

        Assert.True(desc.IsValid);
        Assert.Equal(FroxelGridKind.Main, desc.Kind);
        Assert.Equal(0, desc.Width % 8);
        Assert.Equal(0, desc.Height % 8);
        Assert.True(desc.Depth >= 32);
        Assert.True(desc.BytesPerVolume(SurfaceFormat.HdrBlendable) > 0);
    }

    [Fact]
    public void QualityPresetsScaleMonotonically()
    {
        var low = FroxelGridDesc.MainForViewport(1920, 1080, 0);
        var high = FroxelGridDesc.MainForViewport(1920, 1080, 2);
        var ultra = FroxelGridDesc.MainForViewport(1920, 1080, 3);

        Assert.True(high.VoxelCount > low.VoxelCount);
        Assert.True(ultra.VoxelCount > high.VoxelCount);
        Assert.True(ultra.FarPlane >= high.FarPlane);
    }

    [Fact]
    public void NearCascadeUsesShorterRangeAndSeparateDimensions()
    {
        var main = FroxelGridDesc.MainForViewport(1920, 1080, 2);
        var near = FroxelGridDesc.NearForViewport(1920, 1080, 2);

        Assert.Equal(FroxelGridKind.Near, near.Kind);
        Assert.True(near.FarPlane < main.FarPlane);
        Assert.True(near.Depth >= main.Depth);
        Assert.True(near.TemporalHistory);
        Assert.True(near.StorageCapable);
    }
}
