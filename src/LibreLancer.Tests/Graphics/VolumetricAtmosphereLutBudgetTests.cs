using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricAtmosphereLutBudgetTests
{
    [Fact]
    public void DisabledWhenNotRequested()
    {
        var budget = VolumetricAtmosphereLutBudget.Create(
            requested: false,
            computeSupported: true,
            quality: 2,
            renderWidth: 1920,
            renderHeight: 1080,
            cloudShellRequested: true);

        Assert.False(budget.Enabled);
        Assert.Equal("off", budget.Reason);
        Assert.Equal(0, budget.EstimatedBytes);
    }

    [Fact]
    public void RequiresComputeSupport()
    {
        var budget = VolumetricAtmosphereLutBudget.Create(
            requested: true,
            computeSupported: false,
            quality: 2,
            renderWidth: 1920,
            renderHeight: 1080,
            cloudShellRequested: true);

        Assert.False(budget.Enabled);
        Assert.Contains("compute", budget.Reason);
    }

    [Fact]
    public void HighQualityCreatesAerialPerspectiveBudget()
    {
        var budget = VolumetricAtmosphereLutBudget.Create(
            requested: true,
            computeSupported: true,
            quality: 2,
            renderWidth: 1920,
            renderHeight: 1080,
            cloudShellRequested: true);

        Assert.True(budget.Enabled);
        Assert.Equal(256, budget.TransmittanceWidth);
        Assert.Equal(64, budget.TransmittanceHeight);
        Assert.Equal(240, budget.AerialWidth);
        Assert.Equal(135, budget.AerialHeight);
        Assert.Equal(32, budget.AerialDepth);
        Assert.True(budget.CloudShell);
        Assert.True(budget.EstimatedBytes > 0);
        Assert.Contains("aerial=240x135x32", budget.DebugSummary);
    }

    [Fact]
    public void EstimatedBytesMatchIndividualResources()
    {
        var budget = VolumetricAtmosphereLutBudget.Create(
            requested: true,
            computeSupported: true,
            quality: 2,
            renderWidth: 1920,
            renderHeight: 1080,
            cloudShellRequested: true);

        var expected = budget.TransmittanceBytes +
                       budget.MultiScatteringBytes +
                       budget.SkyViewBytes +
                       budget.AerialPerspectiveBytes +
                       budget.CloudShellBytes;

        Assert.True(budget.SkyViewBytes > 0);
        Assert.Equal(expected, budget.EstimatedBytes);
    }

    [Fact]
    public void DisabledBudgetHasNoResourceBytes()
    {
        var budget = VolumetricAtmosphereLutBudget.Create(
            requested: false,
            computeSupported: true,
            quality: 2,
            renderWidth: 1920,
            renderHeight: 1080,
            cloudShellRequested: true);

        Assert.Equal(0, budget.TransmittanceBytes);
        Assert.Equal(0, budget.MultiScatteringBytes);
        Assert.Equal(0, budget.SkyViewBytes);
        Assert.Equal(0, budget.AerialPerspectiveBytes);
        Assert.Equal(0, budget.CloudShellBytes);
    }

    [Fact]
    public void HighQualityDoesNotReserveCloudShellUnlessRequested()
    {
        var budget = VolumetricAtmosphereLutBudget.Create(
            requested: true,
            computeSupported: true,
            quality: 2,
            renderWidth: 1920,
            renderHeight: 1080,
            cloudShellRequested: false);

        Assert.True(budget.Enabled);
        Assert.False(budget.CloudShell);
    }

    [Fact]
    public void LowQualitySuppressesCloudShell()
    {
        var budget = VolumetricAtmosphereLutBudget.Create(
            requested: true,
            computeSupported: true,
            quality: 0,
            renderWidth: 1920,
            renderHeight: 1080,
            cloudShellRequested: true);

        Assert.True(budget.Enabled);
        Assert.False(budget.CloudShell);
        Assert.Equal(120, budget.AerialWidth);
        Assert.Equal(68, budget.AerialHeight);
        Assert.Equal(16, budget.AerialDepth);
    }
}
