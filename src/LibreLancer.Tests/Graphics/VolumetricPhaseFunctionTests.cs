using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricPhaseFunctionTests
{
    [Fact]
    public void BeerLambertAttenuatesWithDistance()
    {
        var near = VolumetricPhaseFunctions.BeerLambert(0.002f, 100f);
        var far = VolumetricPhaseFunctions.BeerLambert(0.002f, 1000f);

        Assert.InRange(near, 0f, 1f);
        Assert.InRange(far, 0f, near);
    }

    [Fact]
    public void DualHgIsFiniteAndPositive()
    {
        var phase = VolumetricPhaseFunctions.DualHenyeyGreenstein(0.35f, 0.80f, -0.20f, 0.82f);

        Assert.True(phase > 0f);
        Assert.False(float.IsNaN(phase));
        Assert.False(float.IsInfinity(phase));
    }

    [Fact]
    public void ForwardPhaseIsStrongerTowardTheLight()
    {
        var forward = VolumetricPhaseFunctions.DualHenyeyGreenstein(1f, 0.80f, -0.20f, 0.82f);
        var side = VolumetricPhaseFunctions.DualHenyeyGreenstein(0f, 0.80f, -0.20f, 0.82f);

        Assert.True(forward > side);
    }
}
