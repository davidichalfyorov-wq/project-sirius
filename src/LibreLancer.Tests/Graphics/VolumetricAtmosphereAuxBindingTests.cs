using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricAtmosphereAuxBindingTests
{
    [Fact]
    public void SkyViewUsesAuxSlotWhenCloudShellIsMissing()
    {
        var parameters = VolumetricAtmosphereAuxBinding.BuildLutParams(
            lutsActive: true,
            cloudShellAvailable: false,
            cloudShellStrength: 0.8f,
            skyViewAvailable: true);

        Assert.Equal(1f, parameters.X);
        Assert.Equal(0f, parameters.Y);
        Assert.Equal(0f, parameters.Z);
        Assert.Equal(1f, parameters.W);
    }

    [Fact]
    public void CloudShellOwnsAuxSlotOverSkyView()
    {
        var parameters = VolumetricAtmosphereAuxBinding.BuildLutParams(
            lutsActive: true,
            cloudShellAvailable: true,
            cloudShellStrength: 0.24f,
            skyViewAvailable: true);

        Assert.Equal(1f, parameters.X);
        Assert.Equal(1f, parameters.Y);
        Assert.Equal(0.24f, parameters.Z, 4);
        Assert.Equal(0f, parameters.W);
    }

    [Fact]
    public void CloudShellStrengthIsClampedOnlyWhenCloudShellExists()
    {
        var high = VolumetricAtmosphereAuxBinding.BuildLutParams(
            lutsActive: true,
            cloudShellAvailable: true,
            cloudShellStrength: 3.0f,
            skyViewAvailable: false);
        var missing = VolumetricAtmosphereAuxBinding.BuildLutParams(
            lutsActive: true,
            cloudShellAvailable: false,
            cloudShellStrength: 3.0f,
            skyViewAvailable: false);

        Assert.Equal(1f, high.Z);
        Assert.Equal(0f, missing.Z);
    }
}
