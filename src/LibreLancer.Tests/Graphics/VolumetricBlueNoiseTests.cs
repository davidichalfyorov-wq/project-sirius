using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricBlueNoiseTests
{
    [Fact]
    public void FallbackTextureIsDeterministic()
    {
        var a = VolumetricBlueNoiseResources.CreateFallbackPixels(16);
        var b = VolumetricBlueNoiseResources.CreateFallbackPixels(16);
        Assert.Equal(a, b);
    }

    [Fact]
    public void FallbackTextureHasUsableRange()
    {
        var pixels = VolumetricBlueNoiseResources.CreateFallbackPixels(16);
        var min = 255;
        var max = 0;
        foreach (var pixel in pixels)
        {
            var channel = (int)(pixel & 0xFF);
            min = System.Math.Min(min, channel);
            max = System.Math.Max(max, channel);
        }
        Assert.True(min < 32);
        Assert.True(max > 220);
    }

    [Fact]
    public void ChannelsAreDecorrelated()
    {
        var x = VolumetricBlueNoiseResources.QuantizeBlueLikeValue(7, 11, 0);
        var y = VolumetricBlueNoiseResources.QuantizeBlueLikeValue(7, 11, 1);
        var t = VolumetricBlueNoiseResources.QuantizeBlueLikeValue(7, 11, 2);
        Assert.NotEqual(x, y);
        Assert.NotEqual(y, t);
    }
}
