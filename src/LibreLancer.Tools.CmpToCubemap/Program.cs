using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using LibreLancer;
using LibreLancer.Data.IO;
using LibreLancer.Graphics;
using LibreLancer.ImageLib;
using LibreLancer.Render;
using LibreLancer.Render.Materials;
using LibreLancer.Resources;
using LibreLancer.Shaders;

namespace LibreLancer.Tools.CmpToCubemap;

internal enum OutputFormat
{
    Bgra8,
    Dxt1,
    Dxt5
}

internal sealed class Options
{
    public string GameData = "";
    public string? Input;
    public string? Output;
    public string? Batch;
    public string? FromCross;
    public int Size = 1024;
    public OutputFormat Format = OutputFormat.Dxt5;
    public bool Force;
}

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var options = Parse(args);
            if (options.FromCross != null)
            {
                return RunFromCross(options);
            }

            if (options.Batch != null)
            {
                return RunBatch(options);
            }

            if (options.Input == null)
            {
                throw new ArgumentException("Specify --input, --batch or --from-cross.");
            }

            options.Output ??= Path.ChangeExtension(options.Input, ".cubemap.dds");
            BakeOne(options);
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

    // CPU-only path: slice a 4x3 horizontal-cross PNG (the layout exported
    // for the art pipeline) back into a cubemap DDS. Used to bring enhanced
    // or upscaled skies into the game.
    //
    //         [+Y]
    //   [-X]  [+Z]  [+X]  [-Z]
    //         [-Y]
    private static int RunFromCross(Options options)
    {
        if (options.Output == null)
        {
            throw new ArgumentException("--from-cross requires --output <file.dds>.");
        }

        using var input = File.OpenRead(options.FromCross!);
        var image = Generic.ImageFromStream(input); // BGRA
        if (image.Width % 4 != 0 || image.Height % 3 != 0 || image.Width / 4 != image.Height / 3)
        {
            throw new ArgumentException(
                $"'{options.FromCross}' is {image.Width}x{image.Height}; expected a 4x3 cross (width/4 == height/3).");
        }

        var face = image.Width / 4;
        // (column, row) in the cross for DDS face order +X -X +Y -Y +Z -Z
        (int Cx, int Cy)[] cells = [(2, 1), (0, 1), (1, 0), (1, 2), (1, 1), (3, 1)];
        var faces = new List<byte[]>(6);
        foreach (var (cx, cy) in cells)
        {
            var bgra = new byte[face * face * 4];
            for (var y = 0; y < face; y++)
            {
                Buffer.BlockCopy(image.Data,
                    ((cy * face + y) * image.Width + cx * face) * 4,
                    bgra, y * face * 4, face * 4);
            }
            faces.Add(options.Format switch
            {
                OutputFormat.Bgra8 => bgra,
                OutputFormat.Dxt1 => BcCompressor.CompressDxt1(bgra, face, face),
                OutputFormat.Dxt5 => BcCompressor.CompressDxt5(bgra, face, face),
                _ => throw new ArgumentOutOfRangeException()
            });
        }

        var surfaceFormat = options.Format switch
        {
            OutputFormat.Bgra8 => SurfaceFormat.Bgra8,
            OutputFormat.Dxt1 => SurfaceFormat.Dxt1,
            OutputFormat.Dxt5 => SurfaceFormat.Dxt5,
            _ => throw new ArgumentOutOfRangeException()
        };

        using var stream = File.Create(options.Output);
        DDS.WriteCubemap(stream, face, surfaceFormat, faces);
        Console.WriteLine($"Wrote {options.Output} ({face}x{face} per face, {options.Format})");
        return 0;
    }

    private static int RunBatch(Options options)
    {
        var vfs = FileSystem.FromPath(options.GameData, fastInit: true);
        var files = vfs.GetFiles(options.Batch!)
            .Where(x => x.EndsWith(".cmp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var jobs = new List<BakeJob>();
        foreach (var file in files)
        {
            var input = CombineVirtual(options.Batch!, file);
            QueueJob(jobs, new Options
            {
                GameData = options.GameData,
                Input = input,
                Output = Path.ChangeExtension(input, ".cubemap.dds"),
                Size = options.Size,
                Format = options.Format,
                Force = options.Force
            });
        }

        return RunJobs(options, jobs);
    }

    private static void BakeOne(Options options)
    {
        var jobs = new List<BakeJob>();
        QueueJob(jobs, options);
        RunJobs(options, jobs);
    }

    private static void QueueJob(List<BakeJob> jobs, Options options)
    {
        var outputPath = ToHostPath(options.GameData, options.Output!);
        if (File.Exists(outputPath) && !options.Force)
        {
            Console.WriteLine($"Skipping existing {options.Output}. Use --force to overwrite.");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        jobs.Add(new BakeJob(options, outputPath));
    }

    // All jobs share ONE Game/GL context. Spinning up a fresh context per
    // file leaks stale statically-cached GL handles (AllShaders et al.) into
    // the next context and every bake after the first dies with
    // "GL Error: Invalid Value".
    private static int RunJobs(Options options, List<BakeJob> jobs)
    {
        if (jobs.Count == 0)
        {
            return 0;
        }

        using var game = new BakeGame(options, jobs);
        game.Run();
        return game.Failures == 0 ? 0 : 2;
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
                case "--game-data":
                    options.GameData = Value();
                    break;
                case "--input":
                    options.Input = NormalizeVirtual(Value());
                    break;
                case "--output":
                    options.Output = NormalizeVirtual(Value());
                    break;
                case "--batch":
                    options.Batch = NormalizeVirtual(Value()).TrimEnd('/', '\\');
                    break;
                case "--from-cross":
                    options.FromCross = Value(); // host path to a 4x3 cross PNG
                    break;
                case "--size":
                    options.Size = int.Parse(Value(), CultureInfo.InvariantCulture);
                    break;
                case "--format":
                    options.Format = ParseFormat(Value());
                    break;
                case "--force":
                    options.Force = true;
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

        if (string.IsNullOrWhiteSpace(options.GameData) && options.FromCross == null)
        {
            throw new ArgumentException("--game-data is required.");
        }

        if (options.Size <= 0 || (options.Size & (options.Size - 1)) != 0)
        {
            throw new ArgumentException("--size must be a positive power of two.");
        }

        return options;
    }

    private static OutputFormat ParseFormat(string value) => value.ToLowerInvariant() switch
    {
        "bgra8" or "rgba8" => OutputFormat.Bgra8,
        "dxt1" or "bc1" => OutputFormat.Dxt1,
        "dxt5" or "bc3" => OutputFormat.Dxt5,
        _ => throw new ArgumentException($"Unsupported output format '{value}'. Use bgra8, dxt1/bc1 or dxt5/bc3.")
    };

    private static string NormalizeVirtual(string path) => path.Replace('\\', '/');

    private static string CombineVirtual(string directory, string file) =>
        NormalizeVirtual(directory).TrimEnd('/') + "/" + NormalizeVirtual(file).TrimStart('/');

    private static string ToHostPath(string gameData, string virtualPath)
    {
        if (Path.IsPathRooted(virtualPath))
        {
            return virtualPath;
        }
        return Path.Combine(gameData, virtualPath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  CmpToCubemap --game-data <FreelancerRoot> --input DATA/SOLAR/STARSPHERE/starsphere_li01.cmp [--output ...] [--size 2048] [--format dxt5] [--force]");
        Console.WriteLine("  CmpToCubemap --game-data <FreelancerRoot> --batch DATA/SOLAR/STARSPHERE [--size 2048] [--format dxt5] [--force]");
        Console.WriteLine("  CmpToCubemap --from-cross /path/to/cross.png --output /path/to/file.dds [--format dxt5]");
    }
}

internal sealed record BakeJob(Options Options, string OutputPath);

internal sealed class BakeGame : Game, IDisposable
{
    private static readonly (Vector3 Direction, Vector3 Up)[] FaceCameras =
    [
        (Vector3.UnitX, Vector3.UnitY),
        (-Vector3.UnitX, Vector3.UnitY),
        (Vector3.UnitY, -Vector3.UnitZ),
        (-Vector3.UnitY, Vector3.UnitZ),
        (Vector3.UnitZ, Vector3.UnitY),
        (-Vector3.UnitZ, Vector3.UnitY)
    ];

    private readonly Options options;
    private readonly List<BakeJob> jobs;
    private int jobIndex;
    private GameResourceManager? resources;

    public int Failures { get; private set; }

    public BakeGame(Options options, List<BakeJob> jobs)
        : base(options.Size, options.Size, false, GameConfiguration.SDL())
    {
        this.options = options;
        this.jobs = jobs;
        Title = "Project Sirius CmpToCubemap";
    }

    protected override void Load()
    {
        AllShaders.Compile(RenderContext);
        // Material.FromNode consults the global MaterialMap singleton, which
        // the full game creates while parsing freelancer.ini. Without one the
        // loader throws and every starsphere material renders black.
        _ = new LibreLancer.Data.Schema.MaterialMap();
        // Base viewport for the game loop: EndFrame() expects exactly one
        // viewport on the stack. Bake() pushes/pops its own on top of this.
        RenderContext.PushViewport(0, 0, options.Size, options.Size);
        var vfs = FileSystem.FromPath(options.GameData, fastInit: true);
        resources = new GameResourceManager(this, vfs);
    }

    protected override void Draw(double elapsed)
    {
        // One job per frame keeps every bake inside a clean, balanced frame
        // for the render context while reusing the single GL context.
        if (jobIndex >= jobs.Count)
        {
            Exit();
            return;
        }

        var job = jobs[jobIndex++];
        try
        {
            Console.WriteLine($"Baking {job.Options.Input} -> {job.Options.Output} ({job.Options.Size}x{job.Options.Size}, {job.Options.Format}) [{jobIndex}/{jobs.Count}]");
            Bake(job.Options, job.OutputPath);
        }
        catch (Exception ex)
        {
            Failures++;
            Console.Error.WriteLine($"Failed to bake {job.Options.Input}: {ex.Message}");
        }
    }

    private void Bake(Options options, string outputPath)
    {
        var drawable = resources!.GetDrawable(options.Input, MeshLoadMode.GPU)?.Drawable;
        if (drawable is not IRigidModelFile rigidModelFile)
        {
            throw new InvalidOperationException($"'{options.Input}' is not a CMP/rigid model starsphere.");
        }

        var model = rigidModelFile.CreateRigidModel(true, resources);
        model.Update(0);
        using var target = new RenderTarget2D(RenderContext, options.Size, options.Size);
        var restoreTarget = RenderContext.RenderTarget;
        var faces = new List<byte[]>(6);

        RenderContext.RenderTarget = target;
        RenderContext.PushViewport(new Rectangle(0, 0, options.Size, options.Size));
        RenderContext.PushScissor(new Rectangle(0, 0, options.Size, options.Size), false);
        try
        {
            for (var i = 0; i < 6; i++)
            {
                RenderFace(model, FaceCameras[i].Direction, FaceCameras[i].Up);
                var pixels = new byte[options.Size * options.Size * 4];
                target.Texture.GetData(pixels);
                FlipVerticalBgra(pixels, options.Size, options.Size);
                // The LookAt cameras put screen-left at -right while the GL
                // cube face parameterisation expects u to grow along +right.
                // Mirroring U on every face lines all six up with the spec —
                // without it the X<->Z face seams are visibly discontinuous.
                FlipHorizontalBgra(pixels, options.Size, options.Size);
                faces.Add(options.Format switch
                {
                    OutputFormat.Bgra8 => pixels,
                    OutputFormat.Dxt1 => BcCompressor.CompressDxt1(pixels, options.Size, options.Size),
                    OutputFormat.Dxt5 => BcCompressor.CompressDxt5(pixels, options.Size, options.Size),
                    _ => throw new ArgumentOutOfRangeException()
                });
            }
        }
        finally
        {
            RenderContext.PopScissor();
            RenderContext.PopViewport();
            RenderContext.RenderTarget = restoreTarget;
        }

        var surfaceFormat = options.Format switch
        {
            OutputFormat.Bgra8 => SurfaceFormat.Bgra8,
            OutputFormat.Dxt1 => SurfaceFormat.Dxt1,
            OutputFormat.Dxt5 => SurfaceFormat.Dxt5,
            _ => throw new ArgumentOutOfRangeException()
        };

        using var stream = File.Create(outputPath);
        DDS.WriteCubemap(stream, options.Size, surfaceFormat, faces);
        Console.WriteLine($"Wrote {outputPath}");
    }

    private void RenderFace(RigidModel model, Vector3 direction, Vector3 up)
    {
        var camera = new CubemapCamera(direction, up);
        RenderContext.SetCamera(camera);
        RenderContext.ClearColor = new Color4(0, 0, 0, 0);
        RenderContext.DepthEnabled = true;
        RenderContext.DepthWrite = true;
        RenderContext.ColorWrite = true;
        RenderContext.DepthFunction = DepthFunction.LessEqual;
        RenderContext.BlendMode = BlendMode.Normal;
        RenderContext.ClearAll();

        var lighting = Lighting.Empty;
        var world = Matrix4x4.Identity;
        foreach (var part in model.AllParts)
        {
            if (!part.Active || part.Mesh == null)
            {
                continue;
            }

            var partWorld = part.LocalTransform.Matrix() * world;
            part.Mesh.DrawImmediate(0, resources!, RenderContext, partWorld, ref lighting, model.MaterialAnims,
                BasicMaterial.ForceAlpha);
        }
    }

    private static void FlipHorizontalBgra(byte[] pixels, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            for (var x = 0; x < width / 2; x++)
            {
                var left = row + x * 4;
                var right = row + (width - 1 - x) * 4;
                for (var b = 0; b < 4; b++)
                {
                    (pixels[left + b], pixels[right + b]) = (pixels[right + b], pixels[left + b]);
                }
            }
        }
    }

    private static void FlipVerticalBgra(byte[] pixels, int width, int height)
    {
        var stride = width * 4;
        var scratch = new byte[stride];
        for (var y = 0; y < height / 2; y++)
        {
            var top = y * stride;
            var bottom = (height - 1 - y) * stride;
            Buffer.BlockCopy(pixels, top, scratch, 0, stride);
            Buffer.BlockCopy(pixels, bottom, pixels, top, stride);
            Buffer.BlockCopy(scratch, 0, pixels, bottom, stride);
        }
    }

    public void Dispose()
    {
        Cleanup();
    }

    protected override void Cleanup()
    {
        resources?.Dispose();
        resources = null;
    }
}

internal sealed class CubemapCamera : ICamera
{
    private readonly Matrix4x4 view;
    private readonly Matrix4x4 projection;
    private readonly Matrix4x4 viewProjection;

    public CubemapCamera(Vector3 direction, Vector3 up)
    {
        view = Matrix4x4.CreateLookAt(Vector3.Zero, direction, up);
        projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2.0f, 1.0f, 1.0f, 10000000.0f);
        viewProjection = view * projection;
    }

    public Matrix4x4 ViewProjection => viewProjection;
    public Matrix4x4 Projection => projection;
    public Matrix4x4 View => view;
    public Vector3 Position => Vector3.Zero;
    public bool FrustumCheck(BoundingSphere sphere) => true;
    public bool FrustumCheck(BoundingBox box) => true;
}

internal static class BcCompressor
{
    public static byte[] CompressDxt1(byte[] bgra, int width, int height)
    {
        var output = new byte[((width + 3) / 4) * ((height + 3) / 4) * 8];
        var offset = 0;
        Span<byte> block = stackalloc byte[64];
        for (var y = 0; y < height; y += 4)
        {
            for (var x = 0; x < width; x += 4)
            {
                ReadBlock(bgra, width, height, x, y, block);
                CompressColorBlock(block, output.AsSpan(offset, 8), false);
                offset += 8;
            }
        }
        return output;
    }

    public static byte[] CompressDxt5(byte[] bgra, int width, int height)
    {
        var output = new byte[((width + 3) / 4) * ((height + 3) / 4) * 16];
        var offset = 0;
        Span<byte> block = stackalloc byte[64];
        for (var y = 0; y < height; y += 4)
        {
            for (var x = 0; x < width; x += 4)
            {
                ReadBlock(bgra, width, height, x, y, block);
                CompressAlphaBlock(block, output.AsSpan(offset, 8));
                CompressColorBlock(block, output.AsSpan(offset + 8, 8), true);
                offset += 16;
            }
        }
        return output;
    }

    private static void ReadBlock(byte[] bgra, int width, int height, int blockX, int blockY, Span<byte> block)
    {
        for (var y = 0; y < 4; y++)
        {
            var srcY = Math.Min(blockY + y, height - 1);
            for (var x = 0; x < 4; x++)
            {
                var srcX = Math.Min(blockX + x, width - 1);
                var src = (srcY * width + srcX) * 4;
                var dst = (y * 4 + x) * 4;
                block[dst + 0] = bgra[src + 0];
                block[dst + 1] = bgra[src + 1];
                block[dst + 2] = bgra[src + 2];
                block[dst + 3] = bgra[src + 3];
            }
        }
    }

    private static void CompressAlphaBlock(ReadOnlySpan<byte> block, Span<byte> output)
    {
        byte min = 255;
        byte max = 0;
        for (var i = 0; i < 16; i++)
        {
            var a = block[i * 4 + 3];
            min = Math.Min(min, a);
            max = Math.Max(max, a);
        }

        output[0] = max;
        output[1] = min;
        Span<byte> palette = stackalloc byte[8];
        palette[0] = max;
        palette[1] = min;
        if (max > min)
        {
            palette[2] = (byte)((6 * max + 1 * min + 3) / 7);
            palette[3] = (byte)((5 * max + 2 * min + 3) / 7);
            palette[4] = (byte)((4 * max + 3 * min + 3) / 7);
            palette[5] = (byte)((3 * max + 4 * min + 3) / 7);
            palette[6] = (byte)((2 * max + 5 * min + 3) / 7);
            palette[7] = (byte)((1 * max + 6 * min + 3) / 7);
        }
        else
        {
            palette[2] = (byte)((4 * max + 1 * min + 2) / 5);
            palette[3] = (byte)((3 * max + 2 * min + 2) / 5);
            palette[4] = (byte)((2 * max + 3 * min + 2) / 5);
            palette[5] = (byte)((1 * max + 4 * min + 2) / 5);
            palette[6] = 0;
            palette[7] = 255;
        }

        ulong bits = 0;
        for (var i = 0; i < 16; i++)
        {
            var a = block[i * 4 + 3];
            var best = 0;
            var bestDistance = int.MaxValue;
            for (var p = 0; p < 8; p++)
            {
                var distance = Math.Abs(a - palette[p]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = p;
                }
            }
            bits |= (ulong)best << (i * 3);
        }

        for (var i = 0; i < 6; i++)
        {
            output[2 + i] = (byte)((bits >> (8 * i)) & 0xFF);
        }
    }

    private static void CompressColorBlock(ReadOnlySpan<byte> block, Span<byte> output, bool forceFourColor)
    {
        var minR = 255; var minG = 255; var minB = 255;
        var maxR = 0; var maxG = 0; var maxB = 0;
        var hasTransparent = false;
        for (var i = 0; i < 16; i++)
        {
            var b = block[i * 4 + 0];
            var g = block[i * 4 + 1];
            var r = block[i * 4 + 2];
            var a = block[i * 4 + 3];
            if (a < 128) hasTransparent = true;
            minR = Math.Min(minR, r); minG = Math.Min(minG, g); minB = Math.Min(minB, b);
            maxR = Math.Max(maxR, r); maxG = Math.Max(maxG, g); maxB = Math.Max(maxB, b);
        }

        var c0 = To565(maxR, maxG, maxB);
        var c1 = To565(minR, minG, minB);
        if (forceFourColor || !hasTransparent)
        {
            if (c0 < c1) (c0, c1) = (c1, c0);
        }
        else if (c0 > c1)
        {
            (c0, c1) = (c1, c0);
        }

        output[0] = (byte)(c0 & 0xFF);
        output[1] = (byte)(c0 >> 8);
        output[2] = (byte)(c1 & 0xFF);
        output[3] = (byte)(c1 >> 8);

        Span<int> palette = stackalloc int[16];
        Unpack565(c0, palette, 0);
        Unpack565(c1, palette, 4);
        if (c0 > c1 || forceFourColor)
        {
            for (var i = 0; i < 3; i++)
            {
                palette[8 + i] = (2 * palette[i] + palette[4 + i]) / 3;
                palette[12 + i] = (palette[i] + 2 * palette[4 + i]) / 3;
            }
            palette[11] = palette[15] = 255;
        }
        else
        {
            for (var i = 0; i < 3; i++) palette[8 + i] = (palette[i] + palette[4 + i]) / 2;
            palette[11] = 255;
            palette[12] = palette[13] = palette[14] = palette[15] = 0;
        }

        uint indices = 0;
        for (var i = 0; i < 16; i++)
        {
            var best = 0;
            var bestDistance = int.MaxValue;
            for (var p = 0; p < 4; p++)
            {
                var distance = ColorDistance(block, i * 4, palette, p * 4);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = p;
                }
            }
            indices |= (uint)best << (i * 2);
        }

        output[4] = (byte)(indices & 0xFF);
        output[5] = (byte)((indices >> 8) & 0xFF);
        output[6] = (byte)((indices >> 16) & 0xFF);
        output[7] = (byte)((indices >> 24) & 0xFF);
    }

    private static ushort To565(int r, int g, int b) =>
        (ushort)(((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | ((b * 31 + 127) / 255));

    private static void Unpack565(ushort c, Span<int> palette, int offset)
    {
        palette[offset + 0] = ((c >> 11) & 31) * 255 / 31;
        palette[offset + 1] = ((c >> 5) & 63) * 255 / 63;
        palette[offset + 2] = (c & 31) * 255 / 31;
        palette[offset + 3] = 255;
    }

    private static int ColorDistance(ReadOnlySpan<byte> block, int blockOffset, ReadOnlySpan<int> palette, int paletteOffset)
    {
        var db = block[blockOffset + 0] - palette[paletteOffset + 2];
        var dg = block[blockOffset + 1] - palette[paletteOffset + 1];
        var dr = block[blockOffset + 2] - palette[paletteOffset + 0];
        var da = block[blockOffset + 3] - palette[paletteOffset + 3];
        return dr * dr + dg * dg + db * db + da * da;
    }
}
