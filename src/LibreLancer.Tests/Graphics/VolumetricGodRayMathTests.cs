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
        Assert.InRange(result.RayDensity, 0.65f, 1.18f);
        Assert.Contains("T=", result.DebugSummary);
    }

    [Fact]
    public void LongerPathHasLowerTransmittance()
    {
        var profile = MakeProfile(coreExtinction: 0.00004f, godRays: 0.65f);

        var near = VolumetricGodRayMath.ForProfile(profile, 10_000f, 2, 0.35f, enabled: true);
        var far = VolumetricGodRayMath.ForProfile(profile, 180_000f, 2, 0.35f, enabled: true);

        Assert.True(far.OpticalDepth > near.OpticalDepth);
        Assert.True(far.SunTransmittance < near.SunTransmittance);
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
