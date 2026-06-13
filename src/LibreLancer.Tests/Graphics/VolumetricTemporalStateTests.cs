using System.Numerics;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricTemporalStateTests
{
    [Fact]
    public void JitterIsDeterministicAndBounded()
    {
        var a = VolumetricTemporalState.JitterForFrame(13, 2);
        var b = VolumetricTemporalState.JitterForFrame(13, 2);
        Assert.Equal(a, b);
        Assert.InRange(a.X, -0.5f, 0.5f);
        Assert.InRange(a.Y, -0.5f, 0.5f);
    }

    [Fact]
    public void FirstFrameAndProfileChangeResetHistory()
    {
        var state = new VolumetricTemporalState();
        var vp = Matrix4x4.Identity;
        var first = state.BeginFrame(Vector3.Zero, vp, enabled: true, quality: 2, profileNickname: "li01_badlands");
        var second = state.BeginFrame(Vector3.Zero, vp, enabled: true, quality: 2, profileNickname: "li01_badlands");
        var changed = state.BeginFrame(Vector3.Zero, vp, enabled: true, quality: 2, profileNickname: "crow");

        Assert.True(first.Reset);
        Assert.False(second.Reset);
        Assert.True(changed.Reset);
    }

    [Fact]
    public void LargeCameraMotionResetsHistory()
    {
        var state = new VolumetricTemporalState();
        var vp = Matrix4x4.Identity;
        _ = state.BeginFrame(Vector3.Zero, vp, enabled: true, quality: 2, profileNickname: "zone");
        var small = state.BeginFrame(new Vector3(10, 0, 0), vp, enabled: true, quality: 2, profileNickname: "zone");
        var large = state.BeginFrame(new Vector3(5000, 0, 0), vp, enabled: true, quality: 2, profileNickname: "zone");

        Assert.False(small.Reset);
        Assert.True(large.Reset);
    }

    [Fact]
    public void QualityControlsHistoryWeightAndClamp()
    {
        Assert.True(VolumetricTemporalState.HistoryWeightForQuality(3) >
                    VolumetricTemporalState.HistoryWeightForQuality(0));
        Assert.True(VolumetricTemporalState.ClampSigmaForQuality(3) >
                    VolumetricTemporalState.ClampSigmaForQuality(0));
        Assert.True(VolumetricTemporalState.DepthToleranceForQuality(3) >
                    VolumetricTemporalState.DepthToleranceForQuality(0));
    }
}
