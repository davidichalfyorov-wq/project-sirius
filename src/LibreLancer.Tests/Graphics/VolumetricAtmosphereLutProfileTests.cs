using System;
using LibreLancer;
using LibreLancer.Render;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricAtmosphereLutProfileTests
{
    [Fact]
    public void TransmittanceDarkensTowardGroundHorizon()
    {
        var groundHorizon = VolumetricAtmosphereLutProfile.EvaluateTransmittance(0f, 0f, 2);
        var highZenith = VolumetricAtmosphereLutProfile.EvaluateTransmittance(1f, 1f, 2);

        Assert.True(groundHorizon.X < highZenith.X);
        Assert.True(groundHorizon.Z < groundHorizon.X);
        Assert.True(highZenith.Z > 0.90f);
        Assert.Equal(1f, groundHorizon.W);
    }

    [Fact]
    public void MultiScatteringIsStrongerNearHorizon()
    {
        var horizon = VolumetricAtmosphereLutProfile.EvaluateMultiScattering(0f, 0.1f, 2);
        var zenith = VolumetricAtmosphereLutProfile.EvaluateMultiScattering(1f, 0.9f, 2);

        Assert.True(horizon.X > zenith.X);
        Assert.True(horizon.Y > zenith.Y);
        Assert.True(horizon.Z > zenith.Z);
        Assert.Equal(1f, horizon.W);
    }

    [Fact]
    public void SkyViewKeepsBlueDaylightAndWarmLowSun()
    {
        var daylight = VolumetricAtmosphereLutProfile.EvaluateSkyView(0.15f, 1f, 2);
        var lowSunHorizon = VolumetricAtmosphereLutProfile.EvaluateSkyView(0.48f, 0f, 2);

        Assert.True(daylight.Z > daylight.X);
        Assert.True(lowSunHorizon.X > daylight.X);
        Assert.True(lowSunHorizon.Y > 0f);
        Assert.Equal(1f, daylight.W);
    }

    [Fact]
    public void CloudShellIsSparseAndQualityScaled()
    {
        var low = VolumetricAtmosphereCloudShellProfile.Evaluate(0.25f, -0.15f, 0.42f, 0);
        var high = VolumetricAtmosphereCloudShellProfile.Evaluate(0.25f, -0.15f, 0.42f, 3);
        var outer = VolumetricAtmosphereCloudShellProfile.Evaluate(0.95f, 0.95f, 0.42f, 3);
        var highAltitude = VolumetricAtmosphereCloudShellProfile.Evaluate(0.25f, -0.15f, 1f, 3);

        Assert.True(high.W >= low.W);
        Assert.True(high.W <= 1f);
        Assert.True(outer.W < high.W);
        Assert.True(highAltitude.W < high.W);
        Assert.True(high.Z >= high.X);
    }

    [Fact]
    public void DebugViewAliasSelectsAtmosphereSkyView()
    {
        using var _ = new CleanDebugEnvironment();
        Environment.SetEnvironmentVariable("SIRIUS_DEBUG_VIEW", "atmo_sky_view");

        var features = RenderFeatureSet.FromSettings(new GameSettings
        {
            AtmosphereLuts = true
        });

        Assert.Equal(RenderDebugView.AtmosphereSkyView, features.DebugView);
    }

    private sealed class CleanDebugEnvironment : IDisposable
    {
        private readonly string? debugView = Environment.GetEnvironmentVariable("SIRIUS_DEBUG_VIEW");

        public CleanDebugEnvironment()
        {
            Environment.SetEnvironmentVariable("SIRIUS_DEBUG_VIEW", null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("SIRIUS_DEBUG_VIEW", debugView);
        }
    }
}
