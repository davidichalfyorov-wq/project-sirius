using System;
using LibreLancer;
using LibreLancer.Render;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricAtmosphereAerialProfileTests
{
    [Fact]
    public void AerialProfileStaysNeutralAtCamera()
    {
        var sample = VolumetricAtmosphereAerialProfile.Evaluate(0.5f, 0.5f, 0f, 2);

        Assert.Equal(0f, sample.X, 4);
        Assert.Equal(0f, sample.Y, 4);
        Assert.Equal(0f, sample.Z, 4);
        Assert.Equal(1f, sample.W, 4);
    }

    [Fact]
    public void AerialProfileAccumulatesWithDistance()
    {
        var near = VolumetricAtmosphereAerialProfile.Evaluate(0.5f, 0.52f, 0.25f, 2);
        var far = VolumetricAtmosphereAerialProfile.Evaluate(0.5f, 0.52f, 1f, 2);

        Assert.True(far.Z > near.Z);
        Assert.True(far.W < near.W);
        Assert.InRange(far.W, 0.46f, 1f);
    }

    [Fact]
    public void AerialPerspectiveRequiresAtmosphereLuts()
    {
        using var _ = new CleanAerialEnvironment();
        var settings = new GameSettings
        {
            AtmosphereAerial = true,
            AtmosphereLuts = false
        };
        var features = RenderFeatureSet.FromSettings(settings);

        Assert.False(features.AtmosphereLuts);
        Assert.False(features.AtmosphereAerialPerspective);
    }

    [Fact]
    public void AerialPerspectiveCanBeEnabledWithLuts()
    {
        using var _ = new CleanAerialEnvironment();
        var settings = new GameSettings
        {
            AtmosphereAerial = true,
            AtmosphereLuts = true
        };
        var features = RenderFeatureSet.FromSettings(settings);

        Assert.True(features.AtmosphereLuts);
        Assert.True(features.AtmosphereAerialPerspective);
    }

    private sealed class CleanAerialEnvironment : IDisposable
    {
        private readonly string? atmosphereLuts = Environment.GetEnvironmentVariable("SIRIUS_ATMOSPHERE_LUTS");
        private readonly string? atmoLuts = Environment.GetEnvironmentVariable("SIRIUS_ATMO_LUTS");
        private readonly string? atmosphereAerial = Environment.GetEnvironmentVariable("SIRIUS_ATMOSPHERE_AERIAL");
        private readonly string? atmoAerial = Environment.GetEnvironmentVariable("SIRIUS_ATMO_AERIAL");
        private readonly string? aerialPerspective = Environment.GetEnvironmentVariable("SIRIUS_AERIAL_PERSPECTIVE");
        private readonly string? atmosphereCloudShell = Environment.GetEnvironmentVariable("SIRIUS_ATMOSPHERE_CLOUD_SHELL");
        private readonly string? atmoCloudShell = Environment.GetEnvironmentVariable("SIRIUS_ATMO_CLOUD_SHELL");
        private readonly string? cloudShell = Environment.GetEnvironmentVariable("SIRIUS_CLOUD_SHELL");

        public CleanAerialEnvironment()
        {
            Environment.SetEnvironmentVariable("SIRIUS_ATMOSPHERE_LUTS", null);
            Environment.SetEnvironmentVariable("SIRIUS_ATMO_LUTS", null);
            Environment.SetEnvironmentVariable("SIRIUS_ATMOSPHERE_AERIAL", null);
            Environment.SetEnvironmentVariable("SIRIUS_ATMO_AERIAL", null);
            Environment.SetEnvironmentVariable("SIRIUS_AERIAL_PERSPECTIVE", null);
            Environment.SetEnvironmentVariable("SIRIUS_ATMOSPHERE_CLOUD_SHELL", null);
            Environment.SetEnvironmentVariable("SIRIUS_ATMO_CLOUD_SHELL", null);
            Environment.SetEnvironmentVariable("SIRIUS_CLOUD_SHELL", null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("SIRIUS_ATMOSPHERE_LUTS", atmosphereLuts);
            Environment.SetEnvironmentVariable("SIRIUS_ATMO_LUTS", atmoLuts);
            Environment.SetEnvironmentVariable("SIRIUS_ATMOSPHERE_AERIAL", atmosphereAerial);
            Environment.SetEnvironmentVariable("SIRIUS_ATMO_AERIAL", atmoAerial);
            Environment.SetEnvironmentVariable("SIRIUS_AERIAL_PERSPECTIVE", aerialPerspective);
            Environment.SetEnvironmentVariable("SIRIUS_ATMOSPHERE_CLOUD_SHELL", atmosphereCloudShell);
            Environment.SetEnvironmentVariable("SIRIUS_ATMO_CLOUD_SHELL", atmoCloudShell);
            Environment.SetEnvironmentVariable("SIRIUS_CLOUD_SHELL", cloudShell);
        }
    }
}
