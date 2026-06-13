using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricNearCascadeMathTests
{
    [Fact]
    public void FadeWeightKeepsNearCascadeDominantCloseToCamera()
    {
        var near = FroxelGridDesc.NearForViewport(1920, 1080, 2);

        Assert.Equal(0f, VolumetricNearCascadeMath.FadeWeight(near.NearPlane, near));
        Assert.True(VolumetricNearCascadeMath.FadeWeight(near.FarPlane * 0.5f, near) < 0.05f);
    }

    [Fact]
    public void FadeWeightReachesMainAtNearFarPlane()
    {
        var near = FroxelGridDesc.NearForViewport(1920, 1080, 2);

        Assert.Equal(1f, VolumetricNearCascadeMath.FadeWeight(near.FarPlane, near));
    }

    [Fact]
    public void ShaderParamsDisableWithoutNearResource()
    {
        var near = FroxelGridDesc.NearForViewport(1920, 1080, 2);
        var disabled = VolumetricNearCascadeMath.ShaderParams(near, enabled: false);

        Assert.Equal(0f, disabled.X);
    }
}
