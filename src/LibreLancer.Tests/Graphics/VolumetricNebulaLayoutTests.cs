using System.Linq;
using System.Numerics;
using LibreLancer.Data.GameData.World;
using LibreLancer.Render;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricNebulaLayoutTests
{
    [Fact]
    public void QualityProfileReservesProductionFeatures()
    {
        var features = new RenderFeatureSet(
            RenderFeatureBits.VolumetricNebula | RenderFeatureBits.VolumetricNearCascade,
            2,
            RenderDebugView.VolumetricFroxels,
            null);

        var profile = VolumetricNebulaQualityProfile.Create(1920, 1080, features, MakeProfile());

        Assert.Equal("high", profile.Name);
        Assert.Equal(2, profile.Performance.EffectiveQuality);
        Assert.True(profile.MainGrid.IsValid);
        Assert.True(profile.NearGrid.IsValid);
        Assert.True(profile.Temporal.Enabled);
        Assert.Equal(VolumetricJitterSource.SpatiotemporalBlueNoise, profile.Jitter.Source);
        Assert.Equal(VolumetricDensitySource.PerlinWorleyRuntime, profile.Density.Source);
        Assert.Equal(VolumetricPhaseFunction.DualHenyeyGreenstein, profile.Lighting.PhaseFunction);
        Assert.True(profile.Displacement.SdfCapsuleFields);
        Assert.True(profile.MaterialFog.Ships);
        Assert.True(profile.Atmosphere.AerialPerspectiveVolume);
        Assert.True(profile.Density.SupportsOpenVdbImport);
    }

    [Fact]
    public void AdaptiveHugeZoneDownshiftsExpensiveProfiles()
    {
        var features = new RenderFeatureSet(
            RenderFeatureBits.VolumetricNebula | RenderFeatureBits.VolumetricNearCascade |
            RenderFeatureBits.VolumetricAdaptiveQuality,
            3,
            RenderDebugView.VolumetricFroxels,
            null);

        var profile = VolumetricNebulaQualityProfile.Create(3840, 2160, features,
            MakeProfile(size: 520_000f, fogFar: 260_000f));

        Assert.True(profile.Performance.AdaptiveApplied);
        Assert.Equal(3, profile.Performance.RequestedQuality);
        Assert.True(profile.Performance.EffectiveQuality < profile.Performance.RequestedQuality);
        Assert.Equal(0, profile.Performance.EffectiveQuality);
        Assert.Equal(profile.Performance.EffectiveQuality, profile.MainGrid.Quality);
        Assert.Contains("adaptive", profile.Name);
        Assert.True(profile.Performance.EstimatedBytes > 0);
        Assert.Contains("q3->q", profile.Performance.DebugSummary);
        Assert.True(profile.Density.SupportsOpenVdbImport);
        Assert.Equal(3, profile.Density.BaseOctaves);
        Assert.Equal(1, profile.Density.DetailOctaves);
        Assert.Equal(8, profile.Jitter.FrameModulo);
        Assert.Equal(3, profile.Lighting.SelfShadowSteps);
        Assert.Equal(2, profile.Lighting.LocalLightBudget);
        Assert.Equal(1, profile.Lighting.LightningChannelBudget);
        Assert.Equal(6, profile.Displacement.MaxCapsules);
        Assert.Equal(1.75f, profile.Displacement.MaxWakeSeconds);
    }

    [Fact]
    public void AdaptiveQualityCanBePinnedOffForProfiling()
    {
        var features = new RenderFeatureSet(
            RenderFeatureBits.VolumetricNebula | RenderFeatureBits.VolumetricNearCascade,
            3,
            RenderDebugView.VolumetricFroxels,
            null);

        var profile = VolumetricNebulaQualityProfile.Create(3840, 2160, features,
            MakeProfile(size: 520_000f, fogFar: 260_000f));

        Assert.False(profile.Performance.AdaptiveEnabled);
        Assert.False(profile.Performance.AdaptiveApplied);
        Assert.Equal(3, profile.Performance.EffectiveQuality);
        Assert.Equal("ultra", profile.Name);
        Assert.Equal(3, profile.MainGrid.Quality);
    }

    [Fact]
    public void AdaptiveQualityDownshiftsNearCascadeWhenMemoryBudgetIsHigh()
    {
        var features = new RenderFeatureSet(
            RenderFeatureBits.VolumetricNebula | RenderFeatureBits.VolumetricNearCascade |
            RenderFeatureBits.VolumetricAdaptiveQuality,
            2,
            RenderDebugView.VolumetricFroxels,
            null);

        var profile = VolumetricNebulaQualityProfile.Create(1920, 1080, features, MakeProfile());

        Assert.True(profile.Performance.AdaptiveApplied);
        Assert.Equal(2, profile.Performance.RequestedQuality);
        Assert.True(profile.Performance.EffectiveQuality < profile.Performance.RequestedQuality);
        Assert.Contains("memory", profile.Performance.Reason);
        Assert.True(profile.Performance.NearVoxels > 0);
        Assert.True(profile.Performance.EstimatedBytes < 1280L * 1024L * 1024L);
    }

    [Fact]
    public void AdaptiveQualityDoesNotChargeNearCascadeWhenItIsDisabled()
    {
        var features = new RenderFeatureSet(
            RenderFeatureBits.VolumetricNebula | RenderFeatureBits.VolumetricAdaptiveQuality,
            2,
            RenderDebugView.VolumetricFroxels,
            null);

        var profile = VolumetricNebulaQualityProfile.Create(1920, 1080, features, MakeProfile());

        Assert.False(profile.Performance.AdaptiveApplied);
        Assert.Equal(2, profile.Performance.EffectiveQuality);
        Assert.Equal(0, profile.Performance.NearVoxels);
        Assert.DoesNotContain("memory", profile.Performance.Reason);
        Assert.True(profile.Performance.EstimatedBytes < 1280L * 1024L * 1024L);
    }

    [Fact]
    public void CanonicalPassOrderKeepsFutureSlotsNamedForRenderDoc()
    {
        var passes = VolumetricNebulaPassDeclaration.CanonicalOrder;

        Assert.Contains(passes, x => x.Pass == VolumetricNebulaPassSlot.Allocate && x.DebugName == "vol_nebula_allocate");
        Assert.Contains(passes, x => x.Pass == VolumetricNebulaPassSlot.ClearIdentity && x.DebugName == "vol_nebula_clear");
        Assert.Contains(passes, x => x.Pass == VolumetricNebulaPassSlot.DepthCopy && x.DebugName == "vol_nebula_depth_copy");
        Assert.Contains(passes, x => x.Pass == VolumetricNebulaPassSlot.InjectLightningChannels && x.DebugName == "vol_nebula_lightning_channels");
        Assert.Contains(passes, x => x.Pass == VolumetricNebulaPassSlot.WakeCurl && x.DebugName == "vol_nebula_wake_curl");
        Assert.Contains(passes, x => x.Pass == VolumetricNebulaPassSlot.DebugView && x.DebugName == "vol_nebula_debug_view");
        Assert.Contains(passes, x => x.Pass == VolumetricNebulaPassSlot.TemporalReproject);
        Assert.Contains(passes, x => x.Pass == VolumetricNebulaPassSlot.MaterialFogExport);
        Assert.Contains(passes, x => x.Pass == VolumetricNebulaPassSlot.AtmosphereBridge);
        Assert.True(IndexOf(VolumetricNebulaPassSlot.WakeCurl) < IndexOf(VolumetricNebulaPassSlot.InjectDensity));
        Assert.True(IndexOf(VolumetricNebulaPassSlot.Integrate) < IndexOf(VolumetricNebulaPassSlot.DepthCopy));
        Assert.True(IndexOf(VolumetricNebulaPassSlot.DepthCopy) < IndexOf(VolumetricNebulaPassSlot.TemporalReproject));
        Assert.True(IndexOf(VolumetricNebulaPassSlot.DepthCopy) < IndexOf(VolumetricNebulaPassSlot.Composite));
        Assert.True(passes.Count(x => !x.StubInPr52) >= 2);

        int IndexOf(VolumetricNebulaPassSlot slot) =>
            passes.Select((x, i) => (x.Pass, i)).First(x => x.Pass == slot).i;
    }

    private static NebulaVolumeProfile MakeProfile(float size = 12000f, float fogFar = 8000f) => new(
        "zone_test_badlands",
        "test.ini",
        "badlands",
        ShapeKind.Sphere,
        Vector3.Zero,
        Matrix4x4.Identity,
        new Vector3(size),
        0.2f,
        new Vector2(1000f, fogFar),
        new Color4(0.2f, 0.24f, 0.28f, 1f),
        new Color4(0.42f, 0.45f, 0.48f, 1f),
        new Color4(0.03f, 0.03f, 0.035f, 1f),
        0.00002f,
        0.56f,
        1f / 6500f,
        0.6f,
        13f,
        0.42f,
        0.8f,
        -0.2f,
        0.82f,
        0.55f,
        0.75f,
        0.24f,
        0.42f,
        true,
        true,
        0);
}
