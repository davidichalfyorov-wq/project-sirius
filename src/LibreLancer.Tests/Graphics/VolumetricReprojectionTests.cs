using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricReprojectionTests
{
    [Fact]
    public void ViewDistanceToSliceMatchesFroxelCurve()
    {
        var grid = FroxelGridDesc.MainForViewport(1920, 1080, 2);
        Assert.Equal(0f, VolumetricReprojectionMath.ViewDistanceToSlice(grid.NearPlane, grid));
        Assert.Equal(1f, VolumetricReprojectionMath.ViewDistanceToSlice(grid.FarPlane, grid));
    }

    [Fact]
    public void ConfidenceRejectsOffscreenHistory()
    {
        var confidence = VolumetricReprojectionMath.HistoryConfidence(
            prevU: -0.1f,
            prevV: 0.5f,
            prevSlice: 0.5f,
            currentSlice: 0.5f,
            distanceMismatchMeters: 0f,
            depthToleranceMeters: 100f,
            rejectStrength: 1f);
        Assert.Equal(0f, confidence);
    }

    [Fact]
    public void ConfidenceFallsWithDepthMismatch()
    {
        var near = VolumetricReprojectionMath.HistoryConfidence(0.5f, 0.5f, 0.5f, 0.5f, 10f, 100f, 1f);
        var far = VolumetricReprojectionMath.HistoryConfidence(0.5f, 0.5f, 0.5f, 0.5f, 90f, 100f, 1f);
        Assert.True(near > far);
        Assert.InRange(far, 0f, 1f);
    }

    [Fact]
    public void HistoryWeightUsesConfidence()
    {
        Assert.Equal(0.4f, VolumetricReprojectionMath.HistoryWeightWithConfidence(0.8f, 0.5f));
    }
}
