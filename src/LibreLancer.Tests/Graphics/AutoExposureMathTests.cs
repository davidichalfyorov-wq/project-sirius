using LibreLancer.Render;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class AutoExposureMathTests
{
    [Fact]
    public void ExposureFallsAsSceneGetsBrighter()
    {
        var dark = AutoExposureMath.ExposureForLuminance(0.05f, 0.18f, 0f, 0.05f, 8f);
        var bright = AutoExposureMath.ExposureForLuminance(2.0f, 0.18f, 0f, 0.05f, 8f);

        Assert.True(dark > bright);
        Assert.InRange(bright, 0.05f, 8f);
    }

    [Fact]
    public void CompensationUsesStops()
    {
        var neutral = AutoExposureMath.ExposureForLuminance(0.18f, 0.18f, 0f, 0.05f, 8f);
        var plusOne = AutoExposureMath.ExposureForLuminance(0.18f, 0.18f, 1f, 0.05f, 8f);

        Assert.InRange(plusOne, neutral * 1.999f, neutral * 2.001f);
    }

    [Fact]
    public void AdaptationMovesTowardCurrentLuminance()
    {
        var adapted = AutoExposureMath.AdaptLuminance(0.18f, 1.0f, 1f / 60f, 3f, 1f);

        Assert.True(adapted > 0.18f);
        Assert.True(adapted < 1.0f);
    }

    [Fact]
    public void InvalidLuminanceFallsBackToMiddleGrey()
    {
        var exposure = AutoExposureMath.ExposureForLuminance(float.NaN, 0.18f, 0f, 0.05f, 8f);

        Assert.InRange(exposure, 0.999f, 1.001f);
    }
}
