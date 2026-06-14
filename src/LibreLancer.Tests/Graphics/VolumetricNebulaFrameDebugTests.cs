using LibreLancer.Render;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricNebulaFrameDebugTests
{
    [Fact]
    public void DisabledRequestReportsOff()
    {
        var debug = VolumetricNebulaFrameDebug.EvaluateSnapshot(
            requested: false,
            computeSupported: true,
            quality: 2,
            debugView: "off",
            nearCascade: true,
            nearDetail: true,
            shipDisplacement: true,
            atmosphereLuts: true,
            Allocated("GPU temporal history + HDR composite"));

        Assert.False(debug.Requested);
        Assert.False(debug.Active);
        Assert.False(debug.LegacyFallback);
        Assert.Equal("disabled", debug.Reason);
    }

    [Fact]
    public void MissingComputeReportsLegacyFallback()
    {
        var debug = VolumetricNebulaFrameDebug.EvaluateSnapshot(
            requested: true,
            computeSupported: false,
            quality: 3,
            debugView: "vol_density",
            nearCascade: true,
            nearDetail: true,
            shipDisplacement: true,
            atmosphereLuts: true,
            VolumetricNebulaResourceDebug.Disabled("not initialized"));

        Assert.True(debug.Requested);
        Assert.False(debug.Active);
        Assert.True(debug.LegacyFallback);
        Assert.Equal("backend has no compute feature", debug.Reason);
        Assert.False(debug.NearCascade);
        Assert.False(debug.ShipDisplacement);
    }

    [Fact]
    public void AllocatedCompositeReportsActiveRuntimeState()
    {
        var debug = VolumetricNebulaFrameDebug.EvaluateSnapshot(
            requested: true,
            computeSupported: true,
            quality: 1,
            debugView: "vol_transmittance",
            nearCascade: true,
            nearDetail: true,
            shipDisplacement: true,
            atmosphereLuts: true,
            Allocated("GPU temporal history + HDR composite"));

        Assert.True(debug.Requested);
        Assert.True(debug.Active);
        Assert.False(debug.LegacyFallback);
        Assert.Equal("GPU temporal history + HDR composite", debug.Reason);
        Assert.Equal("Zone_Li01_Badlands_Nebula", debug.ProfileNickname);
        Assert.Equal(2, debug.Quality);
        Assert.True(debug.NearCascade);
        Assert.True(debug.NearDetail);
        Assert.True(debug.ShipDisplacement);
        Assert.True(debug.AtmosphereLuts);
        Assert.Equal("vol_transmittance", debug.DebugView);
    }

    [Fact]
    public void AllocatedWithoutCompositeReportsLegacyRuntimeState()
    {
        var debug = VolumetricNebulaFrameDebug.EvaluateSnapshot(
            requested: true,
            computeSupported: true,
            quality: 2,
            debugView: "vol_density",
            nearCascade: true,
            nearDetail: false,
            shipDisplacement: false,
            atmosphereLuts: false,
            Allocated("GPU light integrate"));

        Assert.True(debug.Requested);
        Assert.False(debug.Active);
        Assert.True(debug.LegacyFallback);
        Assert.Equal("GPU light integrate", debug.Reason);
        Assert.Equal("Zone_Li01_Badlands_Nebula", debug.ProfileNickname);
        Assert.True(debug.NearCascade);
        Assert.False(debug.NearDetail);
        Assert.False(debug.ShipDisplacement);
    }

    [Fact]
    public void WaitingResourcesExposeCurrentReason()
    {
        var debug = VolumetricNebulaFrameDebug.EvaluateSnapshot(
            requested: true,
            computeSupported: true,
            quality: 2,
            debugView: "off",
            nearCascade: false,
            nearDetail: false,
            shipDisplacement: false,
            atmosphereLuts: false,
            VolumetricNebulaResourceDebug.Waiting("waiting for active nebula profile"));

        Assert.True(debug.Requested);
        Assert.False(debug.Active);
        Assert.True(debug.LegacyFallback);
        Assert.Equal("waiting for active nebula profile", debug.Reason);
        Assert.Equal(2, debug.Quality);
    }

    private static VolumetricNebulaResourceDebug Allocated(string operation) => new(
        true,
        "512x288x32",
        "256x144x32",
        "high",
        2,
        "q2 memory-budget",
        "Zone_Li01_Badlands_Nebula",
        768L * 1024L * 1024L,
        operation,
        "vol_nebula.main",
        16,
        true,
        "basex1.8 detailx5.4",
        true,
        1,
        "caps=1",
        true,
        "half=2.8s",
        true,
        "curl=0.15",
        true,
        "medium",
        true,
        "history",
        true,
        "golden disabled");
}
