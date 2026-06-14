using System.Numerics;
using LibreLancer.Data.GameData.World;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricOpenVdbImportTests
{
    private const string SourceFileLine = "source_file = art/li01/badlands_density.blend";
    private const string ContentHashLine =
        "content_hash = sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

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
            "bounds = zone_local",
            "placement = zone_locked",
            "profile = li01_badlands_art",
            "canonical_system = Li01",
            "canonical_nebula = li01_badlands",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            ContentHashLine,
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
        Assert.Equal("zone_local", result.Metadata.BoundsMode);
        Assert.Equal("zone_locked", result.Metadata.PlacementMode);
        Assert.Equal(0.05f, result.Metadata.DensityMin);
        Assert.Equal(0.75f, result.Metadata.DensityMax);
        Assert.Equal(786432, result.Metadata.VoxelCount);
        Assert.Equal(3f, result.Metadata.EstimatedRawDensityMiB, 2);
        Assert.True(result.Metadata.PreserveZoneTransform);
        Assert.Contains("raw=3", result.Metadata.DebugSummary);
        Assert.Contains("source=blender_openvdb_export", result.Metadata.DebugSummary);
        Assert.Contains("file=art/li01/badlands_density.blend", result.Metadata.DebugSummary);
        Assert.Contains("hash=sha256:aaaaaaaaaaaa", result.Metadata.DebugSummary);
        Assert.Contains("license=project-owned", result.Metadata.DebugSummary);
        Assert.Contains("lock=zone", result.Metadata.DebugSummary);
        Assert.Contains("placement=zone_locked", result.Metadata.DebugSummary);
    }

    [Fact]
    public void RejectsManifestsThatMoveCanonicalNebula()
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            ContentHashLine,
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
            "depth = 128",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            ContentHashLine
        ]);

        Assert.False(result.Valid);
        Assert.Contains("budget", result.Error);
    }

    [Fact]
    public void RejectsVolumesAboveRuntimeVoxelBudget()
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = too_many_voxels.vdb",
            "width = 512",
            "height = 512",
            "depth = 128",
            "source = houdini_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            ContentHashLine
        ]);

        Assert.False(result.Valid);
        Assert.Contains("voxel count", result.Error);
    }

    [Fact]
    public void RejectsManifestsThatTryToOverrideWorldPlacement()
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            ContentHashLine,
            "world_position_meters = 12000, 0, -5000"
        ]);

        Assert.False(result.Valid);
        Assert.Contains("canonical nebula placement", result.Error);
    }

    [Fact]
    public void RejectsManifestsThatTryToRotateTheCanonicalZone()
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            ContentHashLine,
            "rotation_degrees = 0, 45, 0"
        ]);

        Assert.False(result.Valid);
        Assert.Contains("canonical nebula placement", result.Error);
    }

    [Fact]
    public void RejectsUnlockedPlacementModes()
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            ContentHashLine,
            "placement = world_authored"
        ]);

        Assert.False(result.Valid);
        Assert.Contains("zone_locked", result.Error);
    }

    [Fact]
    public void RejectsUnsupportedBoundsModes()
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            ContentHashLine,
            "bounds = world_bounds"
        ]);

        Assert.False(result.Valid);
        Assert.Contains("bounds mode", result.Error);
    }

    [Theory]
    [InlineData("/tmp/li01_badlands_density.vdb")]
    [InlineData("C:/temp/li01_badlands_density.vdb")]
    [InlineData("../li01_badlands_density.vdb")]
    [InlineData("art/../li01_badlands_density.vdb")]
    [InlineData("art\\li01_badlands_density.vdb")]
    public void RejectsUnsafeDataPaths(string dataPath)
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            $"data = {dataPath}",
            "width = 128",
            "height = 96",
            "depth = 64",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            ContentHashLine
        ]);

        Assert.False(result.Valid);
        Assert.Contains("data path", result.Error);
    }

    [Theory]
    [InlineData("/tmp/badlands_density.blend")]
    [InlineData("C:/art/badlands_density.blend")]
    [InlineData("../art/badlands_density.blend")]
    [InlineData("art/../badlands_density.blend")]
    [InlineData("art\\li01\\badlands_density.blend")]
    public void RejectsUnsafeSourceFilePaths(string sourceFile)
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "source = blender_openvdb_export",
            $"source_file = {sourceFile}",
            "license = project-owned",
            ContentHashLine
        ]);

        Assert.False(result.Valid);
        Assert.Contains("source file", result.Error);
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
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            ContentHashLine,
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
    public void RejectsImportPlanWithoutCanonicalNebulaLock()
    {
        var plan = VolumetricOpenVdbImport.CreateImportPlan([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "canonical_system = Li01",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            ContentHashLine,
            "preserve_zone_transform = true"
        ], MakeProfile("li01_badlands"), "Li01");

        Assert.False(plan.Valid);
        Assert.Contains("canonical nebula lock", plan.Error);
    }

    [Fact]
    public void RejectsImportPlanWithoutCanonicalSystemLockWhenRuntimeSystemIsKnown()
    {
        var plan = VolumetricOpenVdbImport.CreateImportPlan([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "canonical_nebula = li01_badlands",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            ContentHashLine,
            "preserve_zone_transform = true"
        ], MakeProfile("li01_badlands"), "Li01");

        Assert.False(plan.Valid);
        Assert.Contains("canonical system lock", plan.Error);
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
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            ContentHashLine,
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
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            ContentHashLine,
            "density_min = 1",
            "density_max = 1"
        ]);

        Assert.False(result.Valid);
        Assert.Contains("density range", result.Error);
    }

    [Fact]
    public void RejectsMissingProvenanceMetadata()
    {
        var missingSource = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            SourceFileLine,
            ContentHashLine,
            "license = project-owned"
        ]);
        var missingSourceFile = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "source = blender_openvdb_export",
            "license = project-owned",
            ContentHashLine
        ]);
        var missingLicense = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "source = blender_openvdb_export",
            SourceFileLine,
            ContentHashLine
        ]);
        var missingHash = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned"
        ]);
        var invalidHash = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            "content_hash = not-a-hash"
        ]);

        Assert.False(missingSource.Valid);
        Assert.Contains("source metadata", missingSource.Error);
        Assert.False(missingSourceFile.Valid);
        Assert.Contains("source file metadata", missingSourceFile.Error);
        Assert.False(missingLicense.Valid);
        Assert.Contains("license metadata", missingLicense.Error);
        Assert.False(missingHash.Valid);
        Assert.Contains("content hash metadata", missingHash.Error);
        Assert.False(invalidHash.Valid);
        Assert.Contains("content hash", invalidHash.Error);
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
