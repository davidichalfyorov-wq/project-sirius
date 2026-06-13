using System.Numerics;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricCompositeMathTests
{
    [Fact]
    public void ZeroIntensityLeavesSceneUnchanged()
    {
        var scene = new Vector3(1.0f, 0.8f, 0.6f);
        var result = VolumetricCompositeMath.Composite(scene, new Vector3(3f, 2f, 1f), 0.25f, 0f);
        Assert.Equal(scene, result);
    }

    [Fact]
    public void TransmittanceDarkensAndScatteringAddsLight()
    {
        var scene = new Vector3(1.0f, 1.0f, 1.0f);
        var result = VolumetricCompositeMath.Composite(scene, new Vector3(0.1f, 0.2f, 0.3f), 0.5f, 1f);
        Assert.Equal(new Vector3(0.6f, 0.7f, 0.8f), result);
    }

    [Fact]
    public void IntensityInterpolatesToIdentity()
    {
        var scene = new Vector3(1.0f, 1.0f, 1.0f);
        var half = VolumetricCompositeMath.Composite(scene, Vector3.Zero, 0.5f, 0.5f);
        var full = VolumetricCompositeMath.Composite(scene, Vector3.Zero, 0.5f, 1.0f);
        Assert.True(half.X > full.X);
        Assert.True(half.X < scene.X);
    }
}
