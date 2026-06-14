using System.Numerics;
using LibreLancer.Data.GameData.World;
using LibreLancer.Render;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricNearDensityTuningTests
{
    [Fact]
    public void DisabledProfileExportsIdentityShaderParams()
    {
        var tuning = VolumetricNearDensityTuning.Disabled;

        Assert.False(tuning.Enabled);
        Assert.Equal(new Vector4(0f, 1f, 1f, 1f), tuning.ShaderDetailParams);
        Assert.Equal(0f, tuning.ShaderDustParams.X);
        Assert.Equal(0f, tuning.ShaderDustParams.Y);
        Assert.Equal(1f, tuning.ShaderDustParams.Z);
    }

    [Fact]
    public void BadlandsNearTuningAddsMoreDetailThanGeneric()
    {
        var generic = VolumetricNearDensityTuning.ForProfile(MakeProfile("generic"), 2, enabled: true);
        var badlands = VolumetricNearDensityTuning.ForProfile(MakeProfile("badlands"), 2, enabled: true);

        Assert.True(badlands.Enabled);
        Assert.True(badlands.DetailFrequencyMultiplier >= generic.DetailFrequencyMultiplier);
        Assert.True(badlands.WarpBoost >= generic.WarpBoost);
        Assert.True(badlands.ContrastBoost >= generic.ContrastBoost);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void QualityScalesNearFrequencyIntoStableRange(int quality)
    {
        var tuning = VolumetricNearDensityTuning.ForProfile(MakeProfile("crow"), quality, enabled: true);

        Assert.True(tuning.Enabled);
        Assert.InRange(tuning.BaseFrequencyMultiplier, 0.5f, 2.5f);
        Assert.InRange(tuning.DetailFrequencyMultiplier, 2.0f, 8.0f);
        Assert.InRange(tuning.ShaderDetailParams.X, 0.9f, 1.1f);
        Assert.InRange(tuning.ShaderDustParams.Y, 0.01f, 0.4f);
    }

    private static NebulaVolumeProfile MakeProfile(string archetype) => new(
        $"zone_{archetype}_test",
        $"{archetype}.ini",
        archetype,
        ShapeKind.Sphere,
        Vector3.Zero,
        Matrix4x4.Identity,
        new Vector3(10000f),
        0.2f,
        new Vector2(1000f, 8000f),
        new Color4(0.2f, 0.25f, 0.3f, 1f),
        new Color4(0.4f, 0.5f, 0.6f, 1f),
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
        archetype is "crow",
        0);
}
