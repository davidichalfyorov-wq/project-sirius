using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using LibreLancer.Data.GameData.World;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricImportedDensityFrameTests
{
    [Fact]
    public void ImportedDensityFrameSummarizesDecodedOpenVdbVolume()
    {
        var decoded = BuildDecodedVolume([0f, 0.01f, 0.5f, 1f]);

        var frame = VolumetricImportedDensityFrame.FromRuntimeLoadResult(
            decoded,
            MakeProfile("li01_badlands"),
            "Li01");

        Assert.True(decoded.Valid);
        Assert.True(frame.Valid);
        Assert.Equal(4, frame.SampleCount);
        Assert.Equal(0f, frame.MinDensity);
        Assert.Equal(1f, frame.MaxDensity);
        Assert.Equal((0f + 3f / 255f + 128f / 255f + 1f) / 4f, frame.MeanDensity, 6);
        Assert.Equal(0.5f, frame.Coverage);
        Assert.Contains("openvdb 4x1x1", frame.DebugSummary);
        Assert.Contains("hash=ba7816bf8f01", frame.DebugSummary);
    }

    [Fact]
    public void FrameResourcesAcceptMatchingImportedDensity()
    {
        var profile = MakeProfile("li01_badlands");
        var decoded = BuildDecodedVolume([0f, 0.25f, 0.5f, 1f]);
        var frame = VolumetricImportedDensityFrame.FromRuntimeLoadResult(decoded, profile, "Li01");
        using var resources = new VolumetricNebulaFrameResources();
        resources.ClearImportedDensity();

        var accepted = resources.SetImportedDensity(frame, profile);

        Assert.True(frame.Valid);
        Assert.True(accepted);
        Assert.True(resources.ImportedDensityReady);
        Assert.Equal(frame.DebugSummary, resources.ImportedDensitySummary);
        Assert.Equal(frame.DebugSummary, VolumetricNebulaFrameResources.LastImportedDensitySource);
    }

    [Fact]
    public void FrameResourcesRejectWrongProfileImportedDensity()
    {
        var decoded = BuildDecodedVolume([0f, 0.25f, 0.5f, 1f]);
        var frame = VolumetricImportedDensityFrame.FromRuntimeLoadResult(
            decoded,
            MakeProfile("li01_badlands"),
            "Li01");
        using var resources = new VolumetricNebulaFrameResources();
        resources.ClearImportedDensity();

        var accepted = resources.SetImportedDensity(frame, MakeProfile("li01_wrong_nebula"));

        Assert.True(frame.Valid);
        Assert.False(accepted);
        Assert.False(resources.ImportedDensityReady);
        Assert.Equal("off", resources.ImportedDensitySummary);
        Assert.Equal("off", VolumetricNebulaFrameResources.LastImportedDensitySource);
    }

    private static VolumetricEngineVolumeRuntimeLoadResult BuildDecodedVolume(float[] samples)
    {
        var packed = VolumetricOpenVdbPacker.BuildDenseArtifact(
            MakeSmallVerifiedManifestLines(samples.Length),
            MakeProfile("li01_badlands"),
            Encoding.ASCII.GetBytes("abc"),
            Float32Bytes(samples),
            "Li01",
            VolumetricEngineVolumeFormat.DensityR8UNorm);

        Assert.True(packed.Valid);
        return VolumetricEngineVolumeRuntime.DecodeDenseArtifact(
            packed.Artifact,
            MakeProfile("li01_badlands"),
            "Li01");
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

    private static string[] MakeSmallVerifiedManifestLines(int width) =>
    [
        "data = artist_exports/tmp/li01_badlands_density.vdb",
        "grid = density",
        $"width = {width}",
        "height = 1",
        "depth = 1",
        "density_min = 0",
        "density_max = 1",
        "density_multiplier = 1",
        "canonical_system = Li01",
        "canonical_nebula = li01_badlands",
        "source = blender_openvdb_export",
        "source_file = art/li01/badlands_density.blend",
        "license = project-owned",
        "content_hash = sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
        "preserve_zone_transform = true"
    ];

    private static byte[] Float32Bytes(params float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(i * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(values[i]));
        }

        return bytes;
    }
}
