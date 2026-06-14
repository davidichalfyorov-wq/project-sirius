using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LibreLancer.Graphics;

namespace LibreLancer.Render.Volumetrics;

public enum VolumetricBlueNoiseAssetFormat
{
    Bgra8,
    Rgba8,
    R8
}

public readonly record struct VolumetricBlueNoiseAssetDesc(
    string DataPath,
    int Width,
    int Height,
    VolumetricBlueNoiseAssetFormat Format,
    int Frames,
    bool IsStbn,
    string SourceName);

/// <summary>
/// Optional imported blue-noise/STBN payload. The engine accepts a small
/// manifest + raw byte payload and falls back to generated deterministic noise
/// if the asset is missing or invalid.
/// </summary>
public sealed class VolumetricBlueNoiseAsset
{
    public const string EnvAssetPath = "SIRIUS_VOLFOG_BLUE_NOISE_ASSET";

    public VolumetricBlueNoiseAssetDesc Desc { get; }
    public byte[] Bytes { get; }
    public int Width => Desc.Width;
    public int Height => Desc.Height;
    public int Frames => Desc.Frames;
    public string SourceName => Desc.SourceName;
    public bool IsStbn => Desc.IsStbn;

    private VolumetricBlueNoiseAsset(VolumetricBlueNoiseAssetDesc desc, byte[] bytes)
    {
        Desc = desc;
        Bytes = bytes;
    }

    public static bool TryLoadFromEnvironment(out VolumetricBlueNoiseAsset? asset, out string? error)
    {
        var path = Environment.GetEnvironmentVariable(EnvAssetPath);
        if (string.IsNullOrWhiteSpace(path))
        {
            asset = null;
            error = null;
            return false;
        }
        return TryLoad(path, out asset, out error);
    }

    public static bool TryLoad(string manifestPath, out VolumetricBlueNoiseAsset? asset, out string? error)
    {
        asset = null;
        error = null;
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            error = "empty blue-noise manifest path";
            return false;
        }
        if (!File.Exists(manifestPath))
        {
            error = $"manifest not found: {manifestPath}";
            return false;
        }
        if (!TryParseManifest(File.ReadAllText(manifestPath),
                Path.GetDirectoryName(manifestPath) ?? ".", out var desc, out error))
        {
            return false;
        }
        if (!File.Exists(desc.DataPath))
        {
            error = $"blue-noise raw data not found: {desc.DataPath}";
            return false;
        }
        var bytes = File.ReadAllBytes(desc.DataPath);
        var expected = ExpectedByteCount(desc.Width, desc.Height, desc.Format);
        if (bytes.Length < expected)
        {
            error = $"blue-noise data is too small: {bytes.Length} bytes, expected {expected}";
            return false;
        }
        if (bytes.Length > expected)
        {
            Array.Resize(ref bytes, expected);
        }
        asset = new VolumetricBlueNoiseAsset(desc, bytes);
        return true;
    }

    public static bool TryParseManifest(string manifestText, string baseDirectory,
        out VolumetricBlueNoiseAssetDesc desc, out string? error)
    {
        desc = default;
        error = null;
        var values = ParseKeyValueLines(manifestText);
        if (!values.TryGetValue("data", out var data) && !values.TryGetValue("path", out data))
        {
            error = "manifest is missing data/path";
            return false;
        }
        if (!IsSafeRelativeAssetPath(data))
        {
            error = "blue-noise data path must be a safe relative asset path";
            return false;
        }
        if (!TryReadInt(values, "width", out var width) || width <= 0)
        {
            error = "manifest is missing a positive width";
            return false;
        }
        if (!TryReadInt(values, "height", out var height) || height <= 0)
        {
            error = "manifest is missing a positive height";
            return false;
        }

        var format = VolumetricBlueNoiseAssetFormat.Bgra8;
        if (values.TryGetValue("format", out var formatText) && !TryParseFormat(formatText, out format))
        {
            error = $"unsupported blue-noise format '{formatText}'";
            return false;
        }

        _ = TryReadInt(values, "frames", out var frames);
        frames = Math.Max(frames, 1);
        var isStbn = ReadBool(values, "stbn") || ReadBool(values, "spatiotemporal");
        var source = values.TryGetValue("source", out var sourceName) && !string.IsNullOrWhiteSpace(sourceName)
            ? sourceName
            : $"imported-{width}x{height}-{format.ToString().ToLowerInvariant()}";
        var resolvedData = Path.GetFullPath(Path.Combine(baseDirectory, data));
        desc = new VolumetricBlueNoiseAssetDesc(resolvedData, width, height, format, frames, isStbn, source);
        return true;
    }

    public uint[] ToBgraPacked()
    {
        var pixels = new uint[Width * Height];
        switch (Desc.Format)
        {
            case VolumetricBlueNoiseAssetFormat.R8:
                for (var i = 0; i < pixels.Length; i++)
                {
                    var v = Bytes[i];
                    pixels[i] = 0xFF000000u | ((uint)v << 16) | ((uint)v << 8) | v;
                }
                break;
            case VolumetricBlueNoiseAssetFormat.Rgba8:
                for (var i = 0; i < pixels.Length; i++)
                {
                    var o = i * 4;
                    var r = Bytes[o + 0];
                    var g = Bytes[o + 1];
                    var b = Bytes[o + 2];
                    var a = Bytes[o + 3];
                    pixels[i] = ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
                }
                break;
            default:
                for (var i = 0; i < pixels.Length; i++)
                {
                    var o = i * 4;
                    var b = Bytes[o + 0];
                    var g = Bytes[o + 1];
                    var r = Bytes[o + 2];
                    var a = Bytes[o + 3];
                    pixels[i] = ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
                }
                break;
        }
        return pixels;
    }

    private static int ExpectedByteCount(int width, int height, VolumetricBlueNoiseAssetFormat format) =>
        checked(width * height * (format == VolumetricBlueNoiseAssetFormat.R8 ? 1 : 4));

    private static Dictionary<string, string> ParseKeyValueLines(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim().Trim('"');
            values[key] = value;
        }
        return values;
    }

    private static bool TryReadInt(Dictionary<string, string> values, string key, out int value)
    {
        value = 0;
        return values.TryGetValue(key, out var text) &&
               int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool ReadBool(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var text) &&
        (text.Equals("1", StringComparison.OrdinalIgnoreCase) ||
         text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
         text.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
         text.Equals("on", StringComparison.OrdinalIgnoreCase));

    private static bool TryParseFormat(string text, out VolumetricBlueNoiseAssetFormat format)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "bgra8":
            case "bgra":
                format = VolumetricBlueNoiseAssetFormat.Bgra8;
                return true;
            case "rgba8":
            case "rgba":
                format = VolumetricBlueNoiseAssetFormat.Rgba8;
                return true;
            case "r8":
            case "r8_unorm":
                format = VolumetricBlueNoiseAssetFormat.R8;
                return true;
            default:
                format = default;
                return false;
        }
    }

    private static bool IsSafeRelativeAssetPath(string value)
    {
        var path = value.Trim();
        if (path.Length == 0 ||
            path.StartsWith('/') ||
            path.StartsWith('\\') ||
            path.Contains('\\') ||
            path.Contains(':'))
        {
            return false;
        }

        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// Deterministic blue-noise/STBN resource scaffold for volumetric jitter. The
/// runtime can use an optional imported tile, but always has a generated fallback.
/// </summary>
public sealed class VolumetricBlueNoiseResources : IDisposable
{
    public const int TextureSize = 64;

    public Texture2D Texture { get; }
    public string SourceName { get; }
    public bool Imported { get; }
    public bool IsStbn { get; }
    public int Frames { get; }

    public VolumetricBlueNoiseResources(RenderContext context)
    {
        if (VolumetricBlueNoiseAsset.TryLoadFromEnvironment(out var asset, out var error) && asset != null)
        {
            if (asset.Width == TextureSize && asset.Height == TextureSize)
            {
                Texture = new Texture2D(context, TextureSize, TextureSize, false, SurfaceFormat.Bgra8);
                Texture.SetData(asset.ToBgraPacked());
                SourceName = asset.SourceName;
                Imported = true;
                IsStbn = asset.IsStbn;
                Frames = asset.Frames;
                FLLog.Info("Volumetrics",
                    $"Imported blue-noise jitter asset ({SourceName}, frames={Frames}, stbn={IsStbn})");
                return;
            }
            error = $"asset must be {TextureSize}x{TextureSize} for current shader bindings, got {asset.Width}x{asset.Height}";
        }
        if (!string.IsNullOrWhiteSpace(error))
        {
            FLLog.Warning("Volumetrics", $"Blue-noise asset import failed: {error}; using deterministic fallback.");
        }

        Texture = new Texture2D(context, TextureSize, TextureSize, false, SurfaceFormat.Bgra8);
        Texture.SetData(CreateFallbackPixels(TextureSize));
        SourceName = $"generated-{TextureSize}x{TextureSize}";
        Imported = false;
        IsStbn = false;
        Frames = 1;
        FLLog.Info("Volumetrics", $"Blue-noise fallback jitter texture created ({SourceName})");
    }

    public void Dispose() => Texture.Dispose();

    public static uint[] CreateFallbackPixels(int size)
    {
        if (size <= 0 || (size & (size - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Size must be a positive power of two.");
        }
        var pixels = new uint[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var jx = QuantizeBlueLikeValue(x, y, 0);
                var jy = QuantizeBlueLikeValue(x, y, 1);
                var temporal = QuantizeBlueLikeValue(x, y, 2);
                var confidence = QuantizeBlueLikeValue(x, y, 3);
                pixels[y * size + x] =
                    (uint)(confidence << 24) |
                    (uint)(temporal << 16) |
                    (uint)(jy << 8) |
                    jx;
            }
        }
        return pixels;
    }

    public static byte QuantizeBlueLikeValue(int x, int y, int channel)
    {
        var value = BlueLikeValue01(x, y, channel);
        return (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
    }

    public static float BlueLikeValue01(int x, int y, int channel)
    {
        var rank = RadicalInverseBase2((uint)((x & 63) | ((y & 63) << 6) | ((channel & 3) << 12)));
        var hash = Hash01((uint)x, (uint)y, (uint)channel);
        var n1 = Hash01((uint)(x + 17), (uint)(y - 5), (uint)(channel + 11));
        var n2 = Hash01((uint)(x - 7), (uint)(y + 23), (uint)(channel + 29));
        var high = Math.Clamp(hash * 1.55f - (n1 + n2) * 0.275f, 0f, 1f);
        return Math.Clamp(rank * 0.35f + high * 0.65f, 0f, 1f);
    }

    private static float RadicalInverseBase2(uint bits)
    {
        bits = (bits << 16) | (bits >> 16);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
        return bits * 2.3283064365386963e-10f;
    }

    private static float Hash01(uint x, uint y, uint z)
    {
        var h = x * 0x8da6b343u ^ y * 0xd8163841u ^ z * 0xcb1ab31fu;
        h ^= h >> 16;
        h *= 0x7feb352du;
        h ^= h >> 15;
        h *= 0x846ca68bu;
        h ^= h >> 16;
        return (h & 0x00FFFFFFu) / 16777215f;
    }
}
