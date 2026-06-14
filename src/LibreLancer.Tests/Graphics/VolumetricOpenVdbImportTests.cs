using System;
using System.Numerics;
using System.Text;
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
    public void NormalizesSha256AliasToContentHash()
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            "sha256 = aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        ]);

        Assert.True(result.Valid);
        Assert.Equal("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            result.Metadata.ContentHash);
    }

    [Fact]
    public void VerifiesSha256PayloadHash()
    {
        var payload = Encoding.ASCII.GetBytes("abc");
        var verification = VolumetricOpenVdbImport.VerifyContentHash(payload,
            "sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");

        Assert.True(verification.Supported);
        Assert.True(verification.Matched);
        Assert.Equal("sha256", verification.Algorithm);
        Assert.Equal(verification.Expected, verification.Actual);
        Assert.Empty(verification.Error);
    }

    [Fact]
    public void ReportsSha256PayloadHashMismatch()
    {
        var payload = Encoding.ASCII.GetBytes("abc");
        var verification = VolumetricOpenVdbImport.VerifyContentHash(payload,
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.True(verification.Supported);
        Assert.False(verification.Matched);
        Assert.Equal("content hash mismatch", verification.Error);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            verification.Actual);
    }

    [Fact]
    public void LeavesBlake3PayloadVerificationToOfflineImporter()
    {
        var verification = VolumetricOpenVdbImport.VerifyContentHash([1, 2, 3, 4],
            "blake3:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.False(verification.Supported);
        Assert.False(verification.Matched);
        Assert.Contains("offline import tool", verification.Error);
    }

    [Fact]
    public void BuildsVerifiedImportPlanOnlyWhenPayloadHashMatches()
    {
        var plan = VolumetricOpenVdbImport.CreateVerifiedImportPlan([
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
            "content_hash = sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            "preserve_zone_transform = true"
        ], MakeProfile("li01_badlands"), Encoding.ASCII.GetBytes("abc"), "Li01");

        Assert.True(plan.Valid);
        Assert.Equal(VolumetricDensitySource.OpenVdbImported, plan.DensitySource);
        Assert.Equal(0.5f, plan.NormalizeDensity(1.2f), 3);
    }

    [Fact]
    public void BuildsDeterministicCacheIdentityFromCanonicalMetadata()
    {
        var plan = VolumetricOpenVdbImport.CreateVerifiedImportPlan([
            "data = artist_exports/tmp/li01_badlands_density.vdb",
            "grid = density.high",
            "width = 128",
            "height = 96",
            "depth = 64",
            "density_min = 0.2",
            "density_max = 1.2",
            "density_multiplier = 0.5",
            "canonical_system = Li 01",
            "canonical_nebula = Li01/Badlands",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            "content_hash = sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            "preserve_zone_transform = true"
        ], MakeProfile("Li01/Badlands"), Encoding.ASCII.GetBytes("abc"), "Li 01");

        Assert.True(plan.Valid);
        Assert.Equal("li_01_li01_badlands_density_high_128x96x64_ba7816bf8f01", plan.CacheKey);
        Assert.Equal("volumes/openvdb/li_01_li01_badlands_density_high_128x96x64_ba7816bf8f01.siriusvol",
            plan.CacheRelativePath);
        Assert.DoesNotContain("artist_exports", plan.CacheRelativePath);
        Assert.Contains("cache=volumes/openvdb/", plan.DebugSummary);
    }

    [Fact]
    public void BuildsDeterministicCacheManifestForReviewedEngineVolume()
    {
        var plan = VolumetricOpenVdbImport.CreateVerifiedImportPlan([
            "data = artist_exports/tmp/li01_badlands_density.vdb",
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
            "content_hash = sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            "preserve_zone_transform = true"
        ], MakeProfile("li01_badlands"), Encoding.ASCII.GetBytes("abc"), "Li01");

        Assert.True(plan.Valid);
        Assert.EndsWith(".siriusvol.manifest", plan.CacheManifestRelativePath);
        var lines = plan.BuildCacheManifestLines();
        Assert.Contains("cache_version = 1", lines);
        Assert.Contains($"cache = {plan.CacheRelativePath}", lines);
        Assert.Contains($"manifest = {plan.CacheManifestRelativePath}", lines);
        Assert.Contains("canonical_system = Li01", lines);
        Assert.Contains("canonical_nebula = li01_badlands", lines);
        Assert.Contains("density_normalize_scale = 0.5", lines);
        Assert.Contains("density_normalize_bias = -0.1", lines);
        Assert.Contains("source_file = art/li01/badlands_density.blend", lines);
        Assert.Contains("source_data = artist_exports/tmp/li01_badlands_density.vdb", lines);
        Assert.Contains("content_hash = sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", lines);
    }

    [Fact]
    public void ValidatesGeneratedCacheManifestAgainstImportPlan()
    {
        var plan = MakeVerifiedImportPlan();

        var validation = VolumetricOpenVdbImport.ValidateCacheManifest(
            plan.BuildCacheManifestLines(),
            plan);

        Assert.True(validation.Valid);
        Assert.Equal("", validation.Error);
    }

    [Fact]
    public void RejectsCacheManifestWithMismatchedCacheKey()
    {
        var plan = MakeVerifiedImportPlan();
        var lines = ReplaceLine(
            plan.BuildCacheManifestLines(),
            "cache_key = ",
            "cache_key = wrong");

        var validation = VolumetricOpenVdbImport.ValidateCacheManifest(lines, plan);

        Assert.False(validation.Valid);
        Assert.Contains("cache_key", validation.Error);
    }

    [Fact]
    public void RejectsCacheManifestWithMismatchedDensityNormalization()
    {
        var plan = MakeVerifiedImportPlan();
        var lines = ReplaceLine(
            plan.BuildCacheManifestLines(),
            "density_normalize_scale = ",
            "density_normalize_scale = 0.25");

        var validation = VolumetricOpenVdbImport.ValidateCacheManifest(lines, plan);

        Assert.False(validation.Valid);
        Assert.Contains("density_normalize_scale", validation.Error);
    }

    [Fact]
    public void RejectsCacheManifestForInvalidImportPlan()
    {
        var validation = VolumetricOpenVdbImport.ValidateCacheManifest(
            ["cache_version = 1"],
            VolumetricOpenVdbImportPlan.Invalid("bad manifest"));

        Assert.False(validation.Valid);
        Assert.Contains("invalid import plan", validation.Error);
    }

    [Fact]
    public void BuildsCacheArtifactPlanForOfflineOpenVdbTooling()
    {
        var artifactPlan = VolumetricOpenVdbImport.CreateCacheArtifactPlan(
            MakeVerifiedManifestLines(),
            MakeProfile("li01_badlands"),
            Encoding.ASCII.GetBytes("abc"),
            "Li01");

        Assert.True(artifactPlan.Valid);
        Assert.Equal("volumes/openvdb/li01_li01_badlands_density_128x96x64_ba7816bf8f01.siriusvol",
            artifactPlan.EngineVolumePath);
        Assert.Equal($"{artifactPlan.EngineVolumePath}.manifest", artifactPlan.CacheManifestPath);
        Assert.Equal(artifactPlan.ImportPlan.CacheKey, artifactPlan.CacheKey);
        Assert.DoesNotContain("artist_exports", artifactPlan.EngineVolumePath);

        var validation = VolumetricOpenVdbImport.ValidateCacheManifest(
            artifactPlan.CacheManifestLines,
            artifactPlan.ImportPlan);
        Assert.True(validation.Valid);

        var artifactValidation = VolumetricOpenVdbImport.ValidateCacheArtifactPlan(artifactPlan);
        Assert.True(artifactValidation.Valid);
    }

    [Fact]
    public void RejectsCacheArtifactPlanWhenPayloadHashMismatches()
    {
        var artifactPlan = VolumetricOpenVdbImport.CreateCacheArtifactPlan(
            MakeVerifiedManifestLines(),
            MakeProfile("li01_badlands"),
            Encoding.ASCII.GetBytes("not the reviewed vdb bytes"),
            "Li01");

        Assert.False(artifactPlan.Valid);
        Assert.Contains("hash mismatch", artifactPlan.Error);
        Assert.Equal("", artifactPlan.EngineVolumePath);
        Assert.Empty(artifactPlan.CacheManifestLines);
    }

    [Fact]
    public void RejectsCacheArtifactPlanWithMutatedEnginePath()
    {
        var artifactPlan = MakeCacheArtifactPlan();
        var mutated = artifactPlan with
        {
            EngineVolumePath = "artist_exports/tmp/li01_badlands_density.siriusvol"
        };

        var validation = VolumetricOpenVdbImport.ValidateCacheArtifactPlan(mutated);

        Assert.False(validation.Valid);
        Assert.Contains("engine path", validation.Error);
    }

    [Fact]
    public void RejectsCacheArtifactPlanWithMutatedManifestLines()
    {
        var artifactPlan = MakeCacheArtifactPlan();
        var mutated = artifactPlan with
        {
            CacheManifestLines = ReplaceLine(
                artifactPlan.CacheManifestLines,
                "content_hash = ",
                ContentHashLine)
        };

        var validation = VolumetricOpenVdbImport.ValidateCacheArtifactPlan(mutated);

        Assert.False(validation.Valid);
        Assert.Contains("content_hash", validation.Error);
    }

    [Fact]
    public void InvalidImportPlanDoesNotEmitCacheManifest()
    {
        var plan = VolumetricOpenVdbImportPlan.Invalid("bad manifest");

        Assert.Equal("", plan.CacheKey);
        Assert.Equal("", plan.CacheRelativePath);
        Assert.Equal("", plan.CacheManifestRelativePath);
        Assert.Empty(plan.BuildCacheManifestLines());
    }

    [Fact]
    public void RejectsVerifiedImportPlanWhenPayloadHashMismatches()
    {
        var plan = VolumetricOpenVdbImport.CreateVerifiedImportPlan([
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
        ], MakeProfile("li01_badlands"), Encoding.ASCII.GetBytes("abc"), "Li01");

        Assert.False(plan.Valid);
        Assert.Contains("hash mismatch", plan.Error);
    }

    [Fact]
    public void RejectsVerifiedImportPlanWhenRuntimeCannotVerifyHashAlgorithm()
    {
        var plan = VolumetricOpenVdbImport.CreateVerifiedImportPlan([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "canonical_system = Li01",
            "canonical_nebula = li01_badlands",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            "content_hash = blake3:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "preserve_zone_transform = true"
        ], MakeProfile("li01_badlands"), Encoding.ASCII.GetBytes("abc"), "Li01");

        Assert.False(plan.Valid);
        Assert.Contains("unsupported", plan.Error);
    }

    [Theory]
    [InlineData("width", "wide")]
    [InlineData("height", "tall")]
    [InlineData("depth", "deep")]
    [InlineData("voxel_size_meters", "large")]
    [InlineData("density_min", "low")]
    [InlineData("density_max", "high")]
    [InlineData("density_multiplier", "strong")]
    public void RejectsMalformedScalarManifestFields(string key, string value)
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            $"{key} = {value}",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            ContentHashLine
        ]);

        Assert.False(result.Valid);
        Assert.Contains(key, result.Error);
    }

    [Theory]
    [InlineData("origin_meters", "0, no, 0")]
    [InlineData("origin_meters", "0, 0")]
    [InlineData("scale_meters", "1, yes, 1")]
    [InlineData("scale_meters", "1, 1")]
    public void RejectsMalformedVectorManifestFields(string key, string value)
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            $"{key} = {value}",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            ContentHashLine
        ]);

        Assert.False(result.Valid);
        Assert.Contains(key, result.Error);
    }

    [Fact]
    public void RejectsMalformedPreserveZoneTransform()
    {
        var result = VolumetricOpenVdbImport.ParseManifest([
            "data = li01_badlands_density.vdb",
            "width = 128",
            "height = 96",
            "depth = 64",
            "preserve_zone_transform = maybe",
            "source = blender_openvdb_export",
            SourceFileLine,
            "license = project-owned",
            ContentHashLine
        ]);

        Assert.False(result.Valid);
        Assert.Contains("preserve_zone_transform", result.Error);
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

    private static VolumetricOpenVdbImportPlan MakeVerifiedImportPlan() =>
        VolumetricOpenVdbImport.CreateVerifiedImportPlan(
            MakeVerifiedManifestLines(),
            MakeProfile("li01_badlands"),
            Encoding.ASCII.GetBytes("abc"),
            "Li01");

    private static VolumetricOpenVdbCacheArtifactPlan MakeCacheArtifactPlan() =>
        VolumetricOpenVdbImport.CreateCacheArtifactPlan(
            MakeVerifiedManifestLines(),
            MakeProfile("li01_badlands"),
            Encoding.ASCII.GetBytes("abc"),
            "Li01");

    private static string[] MakeVerifiedManifestLines() =>
    [
            "data = artist_exports/tmp/li01_badlands_density.vdb",
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
            "content_hash = sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            "preserve_zone_transform = true"
    ];

    private static string[] ReplaceLine(string[] lines, string prefix, string replacement)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith(prefix, StringComparison.Ordinal))
            {
                lines[i] = replacement;
                return lines;
            }
        }

        throw new InvalidOperationException($"Missing line prefix {prefix}");
    }
}
