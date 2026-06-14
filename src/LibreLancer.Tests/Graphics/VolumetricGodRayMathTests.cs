using System.Numerics;
using LibreLancer.Data.GameData.World;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricGodRayMathTests
{
    [Fact]
    public void DisabledProfileLeavesSunUnattenuated()
    {
        var result = VolumetricGodRayMath.ForProfile(MakeProfile(), 50_000f, 2, 0.35f, enabled: false);

        Assert.False(result.Enabled);
        Assert.Equal(1f, result.SunTransmittance);
        Assert.Equal(1f, result.RayMaskTransmittance);
        Assert.Equal("off", result.DebugSummary);
    }

    [Fact]
    public void DenseMediumDimsButPreservesBurnthroughFloor()
    {
        var result = VolumetricGodRayMath.ForProfile(MakeProfile(coreExtinction: 0.00008f, godRays: 0.95f),
            180_000f, 3, 0.65f, enabled: true);

        Assert.True(result.Enabled);
        Assert.True(result.SunTransmittance < 0.55f);
        Assert.True(result.SunTransmittance >= result.BurnthroughFloor);
        Assert.True(result.RayMaskTransmittance >= result.SunTransmittance);
        Assert.True(result.RayMaskTransmittance <= 1f);
        Assert.InRange(result.RayDensity, 0.65f, 1.18f);
        Assert.Contains("sunT=", result.DebugSummary);
        Assert.Contains("rayT=", result.DebugSummary);
        Assert.Contains("q3", result.DebugSummary);
    }

    [Fact]
    public void LongerPathHasLowerTransmittance()
    {
        var profile = MakeProfile(coreExtinction: 0.00004f, godRays: 0.65f);

        var near = VolumetricGodRayMath.ForProfile(profile, 10_000f, 2, 0.35f, enabled: true);
        var far = VolumetricGodRayMath.ForProfile(profile, 180_000f, 2, 0.35f, enabled: true);

        Assert.True(far.OpticalDepth > near.OpticalDepth);
        Assert.True(far.SunTransmittance < near.SunTransmittance);
        Assert.True(far.RayMaskTransmittance < near.RayMaskTransmittance);
    }

    [Fact]
    public void EffectiveQualityControlsBurnthroughFloor()
    {
        var profile = MakeProfile(coreExtinction: 0.00008f, godRays: 0.95f);

        var low = VolumetricGodRayMath.ForProfile(profile, 180_000f, 0, 0.35f, enabled: true);
        var ultra = VolumetricGodRayMath.ForProfile(profile, 180_000f, 3, 0.35f, enabled: true);

        Assert.True(ultra.BurnthroughFloor > low.BurnthroughFloor);
        Assert.True(ultra.SunTransmittance > low.SunTransmittance);
    }

    [Fact]
    public void PostIntensityIsClampedForHdrComposite()
    {
        var profile = MakeProfile();

        var result = VolumetricGodRayMath.ForProfile(profile, 50_000f, 2, 12f, enabled: true);

        Assert.Equal(2f, result.PostIntensity);
        Assert.Contains("I=2.00", result.DebugSummary);
    }

    private static NebulaVolumeProfile MakeProfile(float coreExtinction = 0.000025f, float godRays = 0.55f) => new(
        "zone_godray_test",
        "test.ini",
        "badlands",
        ShapeKind.Sphere,
        Vector3.Zero,
        Matrix4x4.Identity,
        new Vector3(260_000f),
        0.2f,
        new Vector2(1_000f, 220_000f),
        new Color4(0.2f, 0.24f, 0.28f, 1f),
        new Color4(0.42f, 0.45f, 0.48f, 1f),
        new Color4(0.03f, 0.03f, 0.035f, 1f),
        coreExtinction,
        0.72f,
        1f / 6500f,
        0.6f,
        13f,
        0.42f,
        0.8f,
        -0.2f,
        0.82f,
        0.55f,
        godRays,
        0.24f,
        0.42f,
        true,
        true,
        0);
}
