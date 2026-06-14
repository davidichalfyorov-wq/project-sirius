using System.Numerics;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricWakeCurlTests
{
    [Fact]
    public void QualityControlsCurlStrength()
    {
        var low = VolumetricWakeCurlProfile.ForQuality(0, enabled: true, new Vector3(0f, 0f, -120f));
        var ultra = VolumetricWakeCurlProfile.ForQuality(3, enabled: true, new Vector3(0f, 0f, -120f));

        Assert.True(ultra.CurlStrength > low.CurlStrength);
        Assert.True(ultra.AxisWakeStrength > low.AxisWakeStrength);
        Assert.True(ultra.NoiseFrequency > low.NoiseFrequency);
    }

    [Fact]
    public void SlowVelocityFallsBackToForwardAxis()
    {
        var profile = VolumetricWakeCurlProfile.ForQuality(2, enabled: true, new Vector3(0.01f, 0f, 0f));

        Assert.Equal(Vector3.UnitZ, profile.Axis);
    }

    [Fact]
    public void BuildTangentUsesVelocityAxisCrossGradient()
    {
        var tangent = VolumetricWakeCurlProfile.BuildTangent(Vector3.UnitZ, Vector3.UnitX);

        Assert.Equal(Vector3.UnitY, tangent);
    }

    [Fact]
    public void DisabledProfileHasNoVectorStrength()
    {
        var profile = VolumetricWakeCurlProfile.ForQuality(2, enabled: false, new Vector3(0f, 0f, -120f));

        Assert.False(profile.Enabled);
        Assert.Equal(0f, profile.CurlStrength);
        Assert.Equal("off", profile.DebugSummary);
    }
}
