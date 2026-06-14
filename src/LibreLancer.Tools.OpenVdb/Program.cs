using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using LibreLancer;
using LibreLancer.Data.GameData.World;
using LibreLancer.Render.Volumetrics;

namespace LibreLancer.Tools.OpenVdb;

internal sealed class Options
{
    public string Manifest = "";
    public string OutputRoot = "";
    public string? SourcePayload;
    public string Samples = "";
    public string? RuntimeSystem;
    public VolumetricEngineVolumeFormat Format = VolumetricEngineVolumeFormat.DensityR16UNorm;
}

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var options = Parse(args);
            Pack(options);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            PrintUsage();
            return 1;
        }
    }

    private static void Pack(Options options)
    {
        var manifestLines = File.ReadAllLines(options.Manifest);
        var manifest = VolumetricOpenVdbImport.ParseManifest(manifestLines);
        if (!manifest.Valid)
        {
            throw new InvalidOperationException(manifest.Error);
        }

        var sourcePayloadPath = options.SourcePayload ??
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.Manifest)) ?? "", manifest.Metadata.DataPath);
        var sourcePayload = File.ReadAllBytes(sourcePayloadPath);
        var runtimeSystem = options.RuntimeSystem ?? manifest.Metadata.CanonicalSystem;
        var profile = MakeProfile(manifest.Metadata.CanonicalNebula);
        var artifactPlan = VolumetricOpenVdbImport.CreateCacheArtifactPlan(
            manifestLines,
            profile,
            sourcePayload,
            runtimeSystem);
        if (!artifactPlan.Valid)
        {
            throw new InvalidOperationException(artifactPlan.Error);
        }

        var descriptor = VolumetricEngineVolumeDescriptor.FromOpenVdbArtifact(artifactPlan, options.Format);
        if (!descriptor.Valid)
        {
            throw new InvalidOperationException(descriptor.Error);
        }

        var authoredSamples = ReadFloat32Samples(options.Samples, checked((int)descriptor.VoxelCount));
        var unitSamples = new float[authoredSamples.Length];
        for (var i = 0; i < authoredSamples.Length; i++)
        {
            unitSamples[i] = artifactPlan.ImportPlan.NormalizeDensity(authoredSamples[i]);
        }

        var artifact = VolumetricEngineVolumeDescriptor.BuildDenseArtifact(unitSamples, descriptor);
        if (!artifact.Valid)
        {
            throw new InvalidOperationException(artifact.Error);
        }

        var outputRoot = Path.GetFullPath(options.OutputRoot);
        var volumePath = ResolveOutput(outputRoot, artifactPlan.EngineVolumePath);
        var manifestPath = ResolveOutput(outputRoot, artifactPlan.CacheManifestPath);
        Directory.CreateDirectory(Path.GetDirectoryName(volumePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);

        File.WriteAllBytes(volumePath, artifact.Artifact);
        File.WriteAllLines(manifestPath, artifactPlan.CacheManifestLines);

        Console.WriteLine($"Wrote {artifactPlan.EngineVolumePath}");
        Console.WriteLine($"Wrote {artifactPlan.CacheManifestPath}");
        Console.WriteLine(
            FormattableString.Invariant(
                $"{descriptor.Width}x{descriptor.Height}x{descriptor.Depth}, {descriptor.Format}, {descriptor.PayloadBytes} payload bytes"));
    }

    private static Options Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Value()
            {
                if (++i >= args.Length)
                {
                    throw new ArgumentException($"Missing value for {arg}");
                }
                return args[i];
            }

            switch (arg)
            {
                case "--manifest":
                    options.Manifest = Value();
                    break;
                case "--source-payload":
                    options.SourcePayload = Value();
                    break;
                case "--samples":
                    options.Samples = Value();
                    break;
                case "--output-root":
                    options.OutputRoot = Value();
                    break;
                case "--system":
                    options.RuntimeSystem = Value();
                    break;
                case "--format":
                    options.Format = ParseFormat(Value());
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        if (string.IsNullOrWhiteSpace(options.Manifest))
        {
            throw new ArgumentException("--manifest is required.");
        }
        if (string.IsNullOrWhiteSpace(options.Samples))
        {
            throw new ArgumentException("--samples is required.");
        }
        if (string.IsNullOrWhiteSpace(options.OutputRoot))
        {
            throw new ArgumentException("--output-root is required.");
        }

        return options;
    }

    private static VolumetricEngineVolumeFormat ParseFormat(string value) =>
        value.ToLowerInvariant() switch
        {
            "r8" or "r8_unorm" or "density_r8_unorm" => VolumetricEngineVolumeFormat.DensityR8UNorm,
            "r16" or "r16_unorm" or "density_r16_unorm" => VolumetricEngineVolumeFormat.DensityR16UNorm,
            "r16f" or "r16_float" or "density_r16_float" => VolumetricEngineVolumeFormat.DensityR16Float,
            _ => throw new ArgumentException($"Unsupported density format '{value}'.")
        };

    private static float[] ReadFloat32Samples(string path, int expectedSamples)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length % sizeof(float) != 0)
        {
            throw new InvalidOperationException("sample payload must be raw little-endian float32 values");
        }

        var samples = new float[bytes.Length / sizeof(float)];
        if (samples.Length != expectedSamples)
        {
            throw new InvalidOperationException(
                $"sample count mismatch: expected {expectedSamples}, got {samples.Length}");
        }

        for (var i = 0; i < samples.Length; i++)
        {
            var bits = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * sizeof(float), sizeof(float)));
            samples[i] = BitConverter.Int32BitsToSingle(bits);
        }

        return samples;
    }

    private static string ResolveOutput(string outputRoot, string relativePath)
    {
        var root = Path.TrimEndingDirectorySeparator(outputRoot);
        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!string.Equals(full, root, StringComparison.Ordinal) &&
            !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("generated output path escaped output root");
        }
        return full;
    }

    private static NebulaVolumeProfile MakeProfile(string nickname) =>
        new(
            Nickname: nickname,
            SourceFile: $"{nickname}.openvdb",
            Archetype: "openvdb",
            Shape: ShapeKind.Sphere,
            Position: Vector3.Zero,
            Rotation: Matrix4x4.Identity,
            Size: Vector3.One,
            EdgeFraction: 0f,
            FogRange: new Vector2(0f, 1f),
            FogColor: Color4.White,
            Albedo: Color4.White,
            Ambient: Color4.Black,
            CoreExtinction: 0f,
            Coverage: 1f,
            BaseNoiseScale: 1f,
            DetailErosion: 0f,
            DriftSpeed: 0f,
            DomainWarp: 0f,
            PhaseGForward: 0f,
            PhaseGBackward: 0f,
            PhaseBlend: 0f,
            PowderFactor: 0f,
            GodRayStrength: 0f,
            DustMoteDensity: 0f,
            DisplacementStrength: 0f,
            HasInteriorClouds: false,
            HasLightning: false,
            ExclusionCount: 0);

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "Usage: dotnet run --project src/LibreLancer.Tools.OpenVdb -- --manifest <sidecar.manifest> --source-payload <source.vdb> --samples <density_f32.raw> --output-root <asset-root> [--format r16_unorm]");
        Console.Error.WriteLine(
            "The sample file is raw little-endian float32 authored density values in x-major order.");
    }
}
