using System;
using System.Numerics;
using LibreLancer.Data.GameData.World;
using LibreLancer.Render;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricLightningChannelTests
{
    [Fact]
    public void PulseHasFlashAndAfterglow()
    {
        var flash = VolumetricLightningChannelState.EvaluatePulse(0.01f, 3f, 0.05f, 0.4f,
            out _, out var flashIndex);
        var after = VolumetricLightningChannelState.EvaluatePulse(0.22f, 3f, 0.05f, 0.4f,
            out _, out var afterIndex);
        var idle = VolumetricLightningChannelState.EvaluatePulse(1.2f, 3f, 0.05f, 0.4f,
            out _, out var idleIndex);

        Assert.True(flash > 0.5f);
        Assert.Equal(0, flashIndex);
        Assert.True(after > 0f);
        Assert.Equal(3, afterIndex);
        Assert.Equal(0f, idle);
        Assert.Equal(-1, idleIndex);
    }

    [Fact]
    public void ChannelBuildsEightPointsInNormalizedVolume()
    {
        Span<Vector4> points = stackalloc Vector4[VolumetricLightningChannelState.MaxPoints];
        VolumetricLightningChannelState.BuildChannel(points, 1234, 0.1f, "crow");
        for (var i = 0; i < points.Length; i++)
        {
            Assert.InRange(points[i].X, -1f, 1f);
            Assert.InRange(points[i].Y, -1f, 1f);
            Assert.InRange(points[i].Z, -1f, 1f);
            Assert.Equal(1f, points[i].W);
        }
        Assert.True(points[^1].Y > points[0].Y);
    }

    [Fact]
    public void NoLegacyLightningMeansInactiveFrame()
    {
        var state = new VolumetricLightningChannelState();
        var frame = state.BuildFrame(MakeProfile(hasLightning: false), MakeFeatures(), 0f);
        Assert.False(frame.Active);
    }

    [Fact]
    public void LightningProfileBuildsActiveChannelDuringFlash()
    {
        var state = new VolumetricLightningChannelState();
        var frame = state.BuildFrame(MakeProfile(hasLightning: true), MakeFeatures(), 0.01f);
        Assert.True(frame.Active);
        Assert.Equal(VolumetricLightningChannelState.MaxPoints, frame.PointCount);
        Assert.True(frame.Intensity > 0f);
        Assert.True(frame.Radius > 0f);
    }

    private static RenderFeatureSet MakeFeatures() => new(
        RenderFeatureBits.VolumetricNebula | RenderFeatureBits.VolumetricLightningChannels,
        2,
        RenderDebugView.VolumetricLightning,
        null);

    private static NebulaVolumeProfile MakeProfile(bool hasLightning) => new(
        "zone_crow_test",
        "crow.ini",
        "crow",
        ShapeKind.Sphere,
        Vector3.Zero,
        Matrix4x4.Identity,
        new Vector3(10000f),
        0.2f,
        new Vector2(1000f, 8000f),
        new Color4(0.2f, 0.3f, 0.6f, 1f),
        new Color4(0.4f, 0.5f, 0.8f, 1f),
        new Color4(0.02f, 0.02f, 0.03f, 1f),
        0.00008f,
        0.55f,
        1f / 6000f,
        0.62f,
        8f,
        0.42f,
        0.82f,
        -0.20f,
        0.80f,
        0.55f,
        0.90f,
        0.24f,
        0.42f,
        true,
        hasLightning,
        0);
}
