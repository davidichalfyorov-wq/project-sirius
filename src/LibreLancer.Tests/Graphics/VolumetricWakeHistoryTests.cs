using System.Numerics;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricWakeHistoryTests
{
    [Fact]
    public void HalfLifeDecayReturnsHalfAfterOneHalfLife()
    {
        var decay = VolumetricWakeHistoryMath.DecayFactor(2.8f, 2.8f);

        Assert.InRange(decay, 0.49f, 0.51f);
    }

    [Fact]
    public void QualityControlsWakeHalfLife()
    {
        var low = VolumetricWakeHistoryProfile.ForQuality(0, 1f / 60f, enabled: true);
        var ultra = VolumetricWakeHistoryProfile.ForQuality(3, 1f / 60f, enabled: true);

        Assert.True(ultra.WakeHalfLifeSeconds > low.WakeHalfLifeSeconds);
        Assert.True(ultra.WakeStrength >= low.WakeStrength);
    }

    [Fact]
    public void BlendKeepsCurrentPushAndDecaysWake()
    {
        var profile = new VolumetricWakeHistoryProfile(true, 2.8f, 2.8f, 1.0f, 2.0f, 1.0f);
        var current = new Vector4(0.2f, 0.1f, 0.0f, 0.2f);
        var history = new Vector4(0.9f, 0.8f, 1.0f, 1.0f);

        var blended = VolumetricWakeHistoryMath.Blend(current, history, profile);

        Assert.True(blended.X >= current.X);
        Assert.InRange(blended.Z, 0.49f, 0.51f);
        Assert.True(blended.W >= blended.Z);
    }
}
