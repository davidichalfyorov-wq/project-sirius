using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricMaterialFogPolicyTests
{
    [Fact]
    public void DisabledRequestDoesNotBind()
    {
        var grid = FroxelGridDesc.MainForViewport(1280, 720, 2);

        var binding = VolumetricMaterialFogPolicy.Evaluate(
            requested: false,
            compositeApplied: true,
            integratedAvailable: true,
            temporalApplied: false,
            historyAvailable: false,
            grid,
            meanExtinction: 0.02f);

        Assert.False(binding.CanBind);
        Assert.Equal("off", binding.Status);
    }

    [Fact]
    public void CompositeReadyOwnsLegacyFogForWholeFrame()
    {
        Assert.True(VolumetricMaterialFogPolicy.OwnsLegacyFog(compositeReady: true));
        Assert.False(VolumetricMaterialFogPolicy.OwnsLegacyFog(compositeReady: false));
    }

    [Fact]
    public void LegacyFogCanBeSuppressedBeforeMaterialSamplingBinds()
    {
        var grid = FroxelGridDesc.MainForViewport(1280, 720, 2);

        var binding = VolumetricMaterialFogPolicy.Evaluate(
            requested: false,
            compositeApplied: true,
            integratedAvailable: true,
            temporalApplied: false,
            historyAvailable: false,
            grid,
            meanExtinction: 0.02f);

        Assert.True(VolumetricMaterialFogPolicy.OwnsLegacyFog(compositeReady: true));
        Assert.False(binding.CanBind);
        Assert.Equal("off", binding.Status);
    }

    [Fact]
    public void RequestedFogWaitsForComposite()
    {
        var grid = FroxelGridDesc.MainForViewport(1280, 720, 2);

        var binding = VolumetricMaterialFogPolicy.Evaluate(
            requested: true,
            compositeApplied: false,
            integratedAvailable: true,
            temporalApplied: false,
            historyAvailable: false,
            grid,
            meanExtinction: 0.02f);

        Assert.False(binding.CanBind);
        Assert.Equal("wait", binding.Status);
        Assert.False(binding.CompositeApplied);
        Assert.True(binding.IntegratedAvailable);
        Assert.Contains("composite", binding.DebugSummary);
    }

    [Fact]
    public void RequestedFogWaitsForIntegratedVolumeAfterComposite()
    {
        var grid = FroxelGridDesc.MainForViewport(1280, 720, 2);

        var binding = VolumetricMaterialFogPolicy.Evaluate(
            requested: true,
            compositeApplied: true,
            integratedAvailable: false,
            temporalApplied: false,
            historyAvailable: false,
            grid,
            meanExtinction: 0.02f);

        Assert.False(binding.CanBind);
        Assert.True(binding.CompositeApplied);
        Assert.False(binding.IntegratedAvailable);
        Assert.Contains("integrated", binding.DebugSummary);
    }

    [Fact]
    public void CompositeAndIntegratedVolumeAllowBinding()
    {
        var grid = FroxelGridDesc.MainForViewport(1600, 900, 3);

        var binding = VolumetricMaterialFogPolicy.Evaluate(
            requested: true,
            compositeApplied: true,
            integratedAvailable: true,
            temporalApplied: false,
            historyAvailable: false,
            grid,
            meanExtinction: 0.012f);

        Assert.True(binding.CanBind);
        Assert.False(binding.UsesHistory);
        Assert.Equal(grid.NearPlane, binding.Settings.X);
        Assert.Equal(grid.FarPlane, binding.Settings.Y);
        Assert.Equal(grid.Depth, binding.Settings.Z);
        Assert.Equal(0.012f, binding.Settings.W);
        Assert.Contains("transparent", binding.DebugSummary);
        Assert.Contains("current", binding.DebugSummary);
    }

    [Fact]
    public void TemporalHistoryIsPreferredWhenAvailable()
    {
        var grid = FroxelGridDesc.MainForViewport(1600, 900, 3);

        var binding = VolumetricMaterialFogPolicy.Evaluate(
            requested: true,
            compositeApplied: true,
            integratedAvailable: true,
            temporalApplied: true,
            historyAvailable: true,
            grid,
            meanExtinction: 0.012f);

        Assert.True(binding.CanBind);
        Assert.True(binding.UsesHistory);
        Assert.Contains("history", binding.DebugSummary);
    }
}
