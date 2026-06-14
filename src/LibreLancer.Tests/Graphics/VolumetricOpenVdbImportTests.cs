using System.Numerics;
using LibreLancer.Data.GameData.World;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricOpenVdbImportTests
{
    [Fact]
    public void ParsesLi01DensityManifestWithCanonicalLock()
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "grid = density",
            "width = 128",
            "height = 96",
            "depth = 64",
            "voxel_size_meters = 250",
            "origin_meters = 0, 0, 0",
            "scale_meters = 1, 1, 1",
            "density_min = 0.05",
            "density_max = 0.75",
            "density_multiplier = 0.85",
            "axis = z_up",
            "profile = li01_badlands_art",
            "canonical_system = Li01",
            "canonical_nebula = li01_badlands",
            "source = blender_openvdb_export",
            "license = project-owned",
            "preserve_zone_transform = true"
        ]);

        Assert.True(result.Valid);
        Assert.Equal("li01_badlands_density.vdb", result.Metadata.DataPath);
        Assert.Equal("density", result.Metadata.GridName);
        Assert.Equal(128, result.Metadata.Width);
        Assert.Equal(96, result.Metadata.Height);
        Assert.Equal(64, result.Metadata.Depth);
        Assert.Equal("li01_badlands_art", result.Metadata.ProfileNickname);
        Assert.Equal("z_up", result.Metadata.AxisConvention);
        Assert.Equal(0.05f, result.Metadata.DensityMin);
        Assert.Equal(0.75f, result.Metadata.DensityMax);
        Assert.True(result.Metadata.PreserveZoneTransform);
        Assert.Contains("lock=zone", result.Metadata.DebugSummary);
    }

    [Fact]
    public void RejectsManifestsThatMoveCanonicalNebula()
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "preserve_zone_transform = false"
        ]);

        Assert.False(result.Valid);
        Assert.Contains("canonical zone transform", result.Error);
    }

    [Fact]
    public void RejectsVolumesAboveRuntimeBudget()
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = huge.vdb",
            "width = 1024",
            "height = 128",
            "depth = 128"
        ]);

        Assert.False(result.Valid);
        Assert.Contains("budget", result.Error);
    }

    [Fact]
    public void BuildsProfileLockedImportPlanWithDensityNormalization()
    {
        var plan = VolumetricOpenVdbImport.CreateImportPlan([
            "data = li01_badlands_density.vdb",
            "grid = density",
            "width = 128",
            "height = 96",
            "depth = 64",
            "density_min = 0.2",
            "density_max = 1.2",
            "density_multiplier = 0.5",
            "canonical_system = Li01",
            "canonical_nebula = li01_badlands",
            "preserve_zone_transform = true"
        ], MakeProfile("li01_badlands"), "Li01");

        Assert.True(plan.Valid);
        Assert.Equal(VolumetricDensitySource.OpenVdbImported, plan.DensitySource);
        Assert.Equal("li01_badlands", plan.Metadata.ProfileNickname);
        Assert.Equal(0f, plan.NormalizeDensity(0.2f), 3);
        Assert.Equal(0.5f, plan.NormalizeDensity(1.2f), 3);
        Assert.Contains("normalize=", plan.DebugSummary);
    }

    [Fact]
    public void RejectsManifestForDifferentCanonicalNebula()
    {
        var plan = VolumetricOpenVdbImport.CreateImportPlan([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "canonical_system = Li01",
            "canonical_nebula = li01_badlands",
            "preserve_zone_transform = true"
        ], MakeProfile("zone_not_badlands"), "Li01");

        Assert.False(plan.Valid);
        Assert.Contains("canonical nebula", plan.Error);
    }

    [Fact]
    public void RejectsInvalidDensityRange()
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "density_min = 1",
            "density_max = 1"
        ]);

        Assert.False(result.Valid);
        Assert.Contains("density range", result.Error);
    }

    private static NebulaVolumeProfile MakeProfile(string nickname) =>
        new(
            Nickname: nickname,
            SourceFile: $"{nickname}.ini",
            Archetype: "badlands",
            Shape: ShapeKind.Sphere,
            Position: Vector3.Zero,
            Rotation: Matrix4x4.Identity,
            Size: new Vector3(12000f),
            EdgeFraction: 0.2f,
            FogRange: new Vector2(1000f, 8000f),
            FogColor: Color4.White,
            Albedo: Color4.White,
            Ambient: Color4.Black,
            CoreExtinction: 0.001f,
            Coverage: 0.55f,
            BaseNoiseScale: 1f / 6500f,
            DetailErosion: 0.6f,
            DriftSpeed: 10f,
            DomainWarp: 0.4f,
            PhaseGForward: 0.8f,
            PhaseGBackward: -0.2f,
            PhaseBlend: 0.8f,
            PowderFactor: 0.55f,
            GodRayStrength: 0.75f,
            DustMoteDensity: 0.24f,
            DisplacementStrength: 0.42f,
            HasInteriorClouds: true,
            HasLightning: false,
            ExclusionCount: 0);
}
