using System;
using LibreLancer;
using LibreLancer.Render;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricOpenVdbSettingsTests
{
    [Fact]
    public void ConfiguredOpenVdbManifestRequiresVolumetricNebula()
    {
        using var _ = new CleanOpenVdbEnvironment();
        var features = RenderFeatureSet.FromSettings(new GameSettings
        {
            VolumetricNebula = false,
            VolumetricOpenVdbManifest = "volumes/openvdb/li01_badlands.siriusvol.manifest"
        });

        Assert.False(features.VolumetricNebula);
        Assert.Null(features.VolumetricOpenVdbManifest);
    }

    [Fact]
    public void ConfiguredOpenVdbManifestFlowsIntoRenderFeatures()
    {
        using var _ = new CleanOpenVdbEnvironment();
        var features = RenderFeatureSet.FromSettings(new GameSettings
        {
            VolumetricNebula = true,
            VolumetricOpenVdbManifest = "volumes/openvdb/li01_badlands.siriusvol.manifest"
        });

        Assert.True(features.VolumetricNebula);
        Assert.Equal("volumes/openvdb/li01_badlands.siriusvol.manifest",
            features.VolumetricOpenVdbManifest);
    }

    [Fact]
    public void EnvironmentOpenVdbManifestOverridesConfig()
    {
        using var _ = new CleanOpenVdbEnvironment();
        Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_OPENVDB_MANIFEST",
            @"volumes\openvdb\env_badlands.siriusvol.manifest");

        var features = RenderFeatureSet.FromSettings(new GameSettings
        {
            VolumetricNebula = true,
            VolumetricOpenVdbManifest = "volumes/openvdb/config_badlands.siriusvol.manifest"
        });

        Assert.Equal("volumes/openvdb/env_badlands.siriusvol.manifest",
            features.VolumetricOpenVdbManifest);
    }

    [Fact]
    public void EnvironmentOpenVdbManifestRejectsAbsolutePath()
    {
        using var _ = new CleanOpenVdbEnvironment();
        Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_OPENVDB_MANIFEST",
            "/tmp/li01_badlands.siriusvol.manifest");

        var features = RenderFeatureSet.FromSettings(new GameSettings
        {
            VolumetricNebula = true
        });

        Assert.Null(features.VolumetricOpenVdbManifest);
    }

    private sealed class CleanOpenVdbEnvironment : IDisposable
    {
        private readonly string? volfogManifest =
            Environment.GetEnvironmentVariable("SIRIUS_VOLFOG_OPENVDB_MANIFEST");
        private readonly string? openVdbManifest =
            Environment.GetEnvironmentVariable("SIRIUS_OPENVDB_MANIFEST");
        private readonly string? volumetricNebula =
            Environment.GetEnvironmentVariable("SIRIUS_VOLUMETRIC_NEBULA");
        private readonly string? volfog =
            Environment.GetEnvironmentVariable("SIRIUS_VOLFOG");

        public CleanOpenVdbEnvironment()
        {
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_OPENVDB_MANIFEST", null);
            Environment.SetEnvironmentVariable("SIRIUS_OPENVDB_MANIFEST", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLUMETRIC_NEBULA", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG", null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_OPENVDB_MANIFEST", volfogManifest);
            Environment.SetEnvironmentVariable("SIRIUS_OPENVDB_MANIFEST", openVdbManifest);
            Environment.SetEnvironmentVariable("SIRIUS_VOLUMETRIC_NEBULA", volumetricNebula);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG", volfog);
        }
    }
}
