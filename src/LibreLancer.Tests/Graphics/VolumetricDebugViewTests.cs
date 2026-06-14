using System;
using LibreLancer;
using LibreLancer.Render;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricDebugViewTests
{
    [Fact]
    public void SelfShadowAliasSelectsLightingAlphaDebugView()
    {
        using var _ = new CleanDebugViewEnvironment();
        Environment.SetEnvironmentVariable("SIRIUS_DEBUG_VIEW", "vol_self_shadow");

        var features = RenderFeatureSet.FromSettings(new GameSettings
        {
            VolumetricNebula = true
        });

        Assert.Equal(RenderDebugView.VolumetricSelfShadow, features.DebugView);
        Assert.Equal("vol_self_shadow", GameSettings.NormalizePhase5DebugView("selfshadow"));
    }

    private sealed class CleanDebugViewEnvironment : IDisposable
    {
        private readonly string? debugView = Environment.GetEnvironmentVariable("SIRIUS_DEBUG_VIEW");
        private readonly string? volumetricNebula =
            Environment.GetEnvironmentVariable("SIRIUS_VOLUMETRIC_NEBULA");
        private readonly string? volfog = Environment.GetEnvironmentVariable("SIRIUS_VOLFOG");

        public CleanDebugViewEnvironment()
        {
            Environment.SetEnvironmentVariable("SIRIUS_DEBUG_VIEW", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLUMETRIC_NEBULA", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG", null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("SIRIUS_DEBUG_VIEW", debugView);
            Environment.SetEnvironmentVariable("SIRIUS_VOLUMETRIC_NEBULA", volumetricNebula);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG", volfog);
        }
    }
}
