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
        Assert.True(profile.MainGrid.IsValid);
        Assert.True(profile.NearGrid.IsValid);
        Assert.True(profile.Temporal.Enabled);
        Assert.Equal(VolumetricJitterSource.SpatiotemporalBlueNoise, profile.Jitter.Source);
        Assert.Equal(VolumetricDensitySource.PerlinWorleyRuntime, profile.Density.Source);
        Assert.Equal(VolumetricPhaseFunction.DualHenyeyGreenstein, profile.Lighting.PhaseFunction);
        Assert.True(profile.Displacement.SdfCapsuleFields);
        Assert.True(profile.MaterialFog.Ships);
        Assert.True(profile.Atmosphere.AerialPerspectiveVolume);
    }

    [Fact]
    public void CanonicalPassOrderKeepsFutureSlotsNamedForRenderDoc()
    {
        var passes = VolumetricNebulaPassDeclaration.CanonicalOrder;

        Assert.Contains(passes, x => x.Pass == VolumetricNebulaPassSlot.Allocate && x.DebugName == "vol_nebula_allocate");
        Assert.Contains(passes, x => x.Pass == VolumetricNebulaPassSlot.ClearIdentity && x.DebugName == "vol_nebula_clear");
        Assert.Contains(passes, x => x.Pass == VolumetricNebulaPassSlot.WakeCurl && x.DebugName == "vol_nebula_wake_curl");
        Assert.Contains(passes, x => x.Pass == VolumetricNebulaPassSlot.TemporalReproject);
        Assert.Contains(passes, x => x.Pass == VolumetricNebulaPassSlot.MaterialFogExport);
        Assert.Contains(passes, x => x.Pass == VolumetricNebulaPassSlot.AtmosphereBridge);
        Assert.True(IndexOf(VolumetricNebulaPassSlot.WakeCurl) < IndexOf(VolumetricNebulaPassSlot.InjectDensity));
        Assert.True(passes.Count(x => !x.StubInPr52) >= 2);

        int IndexOf(VolumetricNebulaPassSlot slot) =>
            passes.Select((x, i) => (x.Pass, i)).First(x => x.Pass == slot).i;
    }

    private static NebulaVolumeProfile MakeProfile() => new(
        "zone_test_badlands",
        "test.ini",
        "badlands",
        ShapeKind.Sphere,
        Vector3.Zero,
        Matrix4x4.Identity,
        new Vector3(12000f),
        0.2f,
        new Vector2(1000f, 8000f),
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
