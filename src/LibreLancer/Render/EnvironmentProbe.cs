using System;
using System.IO;
using System.Numerics;
using LibreLancer.Graphics;
using LibreLancer.Resources;

namespace LibreLancer.Render;

/// <summary>
/// Per-system image-based lighting (graphics roadmap 5.3), built on the
/// CPU at system load: the starsphere cubemap (or a flat ambient
/// fallback) box-filtered into a roughness mip chain plus a cosine
/// irradiance cube. Faces store sRGB bytes; shaders decode on sample
/// like every other colour texture.
/// </summary>
public sealed class EnvironmentProbe : IDisposable
{
    public const int SpecularSize = 64;
    public const int SpecularMips = 5; // 64..4
    public const int IrradianceSize = 16;

    public TextureCube Specular { get; }
    public TextureCube Irradiance { get; }

    private EnvironmentProbe(TextureCube specular, TextureCube irradiance)
    {
        Specular = specular;
        Irradiance = irradiance;
    }

    public void Dispose()
    {
        Specular.Dispose();
        Irradiance.Dispose();
    }

    private static readonly float[] srgbToLinear = BuildDecodeTable();

    private static float[] BuildDecodeTable()
    {
        var table = new float[256];
        for (var i = 0; i < 256; i++)
        {
            var c = i / 255f;
            table[i] = c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }
        return table;
    }

    private static byte EncodeSrgb(float linear)
    {
        var c = linear <= 0.0031308f
            ? linear * 12.92f
            : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
        return (byte)Math.Clamp((int)(c * 255f + 0.5f), 0, 255);
    }

    /// <summary>Builds the probe; null only on unexpected read errors.</summary>
    public static EnvironmentProbe Build(RenderContext rstate, ResourceManager resources,
        string? cubemapPath, Color3f ambientFallback)
    {
        Vector3[][] faces;
        if (!string.IsNullOrWhiteSpace(cubemapPath) && resources.ResourceExists(cubemapPath))
        {
            try
            {
                faces = LoadCubemapFaces(resources, cubemapPath!);
            }
            catch (Exception e)
            {
                FLLog.Warning("IBL", $"Cubemap '{cubemapPath}' unreadable ({e.Message}); ambient fallback");
                faces = AmbientFaces(ambientFallback);
            }
        }
        else
        {
            faces = AmbientFaces(ambientFallback);
        }

        var specular = new TextureCube(rstate, SpecularSize, true, SurfaceFormat.Bgra8);
        var level = faces;
        var size = SpecularSize;
        for (var mip = 0; mip < SpecularMips; mip++)
        {
            for (var face = 0; face < 6; face++)
            {
                specular.SetData((CubeMapFace)face, mip, null, EncodeFace(level[face], size), 0, size * size);
            }
            if (mip < SpecularMips - 1)
            {
                level = Downsample(level, size);
                size /= 2;
            }
        }

        var irradiance = new TextureCube(rstate, IrradianceSize, false, SurfaceFormat.Bgra8);
        var irradianceFaces = ConvolveIrradiance(faces);
        for (var face = 0; face < 6; face++)
        {
            irradiance.SetData((CubeMapFace)face, 0, null,
                EncodeFace(irradianceFaces[face], IrradianceSize), 0, IrradianceSize * IrradianceSize);
        }

        return new EnvironmentProbe(specular, irradiance);
    }

    private static Vector3[][] AmbientFaces(Color3f ambient)
    {
        var linear = new Vector3(
            srgbToLinear[(byte)Math.Clamp((int)(ambient.R * 255f), 0, 255)],
            srgbToLinear[(byte)Math.Clamp((int)(ambient.G * 255f), 0, 255)],
            srgbToLinear[(byte)Math.Clamp((int)(ambient.B * 255f), 0, 255)]);
        var faces = new Vector3[6][];
        for (var face = 0; face < 6; face++)
        {
            faces[face] = new Vector3[SpecularSize * SpecularSize];
            Array.Fill(faces[face], linear);
        }
        return faces;
    }

    /// <summary>DXT cubemap faces decoded and box-reduced to SpecularSize, linear floats.</summary>
    private static Vector3[][] LoadCubemapFaces(ResourceManager resources, string path)
    {
        using var stream = resources.OpenResource(path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x20534444) // 'DDS '
        {
            throw new InvalidDataException("not a DDS");
        }
        var header = reader.ReadBytes(124);
        var height = BitConverter.ToInt32(header, 8);
        var width = BitConverter.ToInt32(header, 12);
        var mipCount = Math.Max(BitConverter.ToInt32(header, 24), 1);
        var fourCC = BitConverter.ToUInt32(header, 80);
        var caps2 = BitConverter.ToUInt32(header, 108);
        if ((caps2 & 0x200) == 0)
        {
            throw new InvalidDataException("not a cubemap");
        }
        var format = fourCC switch
        {
            0x31545844 => SurfaceFormat.Dxt1,
            0x33545844 => SurfaceFormat.Dxt3,
            0x35545844 => SurfaceFormat.Dxt5,
            0 => SurfaceFormat.Bgra8,
            _ => throw new InvalidDataException($"fourCC {fourCC:X8}")
        };

        // Read all faces first, then decode in parallel: DXT decompression
        // of six 2048^2 faces dominates the build time single-threaded.
        var faceBytes = new byte[6][];
        for (var face = 0; face < 6; face++)
        {
            faceBytes[face] = reader.ReadBytes(format == SurfaceFormat.Bgra8
                ? width * height * 4
                : CompressedFaceSize(format, width, height));
            // Skip this face's mip tail (faces are stored with full chains).
            for (int mip = 1, w = width / 2, h = height / 2; mip < mipCount; mip++)
            {
                reader.BaseStream.Seek(format == SurfaceFormat.Bgra8
                    ? w * h * 4
                    : CompressedFaceSize(format, w, h), SeekOrigin.Current);
                w = Math.Max(w / 2, 1);
                h = Math.Max(h / 2, 1);
            }
        }
        var faces = new Vector3[6][];
        System.Threading.Tasks.Parallel.For(0, 6, face =>
        {
            var pixels = format == SurfaceFormat.Bgra8
                ? BgraFromBytes(faceBytes[face], width, height)
                : S3TC.Decompress(format, width, height, faceBytes[face]);
            faces[face] = ReduceToLinear(pixels, width, SpecularSize);
        });
        return faces;
    }

    private static int CompressedFaceSize(SurfaceFormat format, int width, int height)
    {
        var blockBytes = format == SurfaceFormat.Dxt1 ? 8 : 16;
        return Math.Max(width / 4, 1) * Math.Max(height / 4, 1) * blockBytes;
    }

    private static Bgra8[] BgraFromBytes(byte[] bytes, int width, int height)
    {
        var pixels = new Bgra8[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Bgra8(bytes[i * 4 + 2], bytes[i * 4 + 1], bytes[i * 4], bytes[i * 4 + 3]);
        }
        return pixels;
    }

    private static Vector3[] ReduceToLinear(Bgra8[] source, int sourceSize, int targetSize)
    {
        var ratio = sourceSize / targetSize;
        var result = new Vector3[targetSize * targetSize];
        var weight = 1f / (ratio * ratio);
        System.Threading.Tasks.Parallel.For(0, targetSize, y =>
        {
            for (var x = 0; x < targetSize; x++)
            {
                var sum = Vector3.Zero;
                for (var sy = 0; sy < ratio; sy++)
                {
                    var row = (y * ratio + sy) * sourceSize + x * ratio;
                    for (var sx = 0; sx < ratio; sx++)
                    {
                        var p = source[row + sx];
                        sum += new Vector3(srgbToLinear[p.R], srgbToLinear[p.G], srgbToLinear[p.B]);
                    }
                }
                result[y * targetSize + x] = sum * weight;
            }
        });
        return result;
    }

    private static Vector3[][] Downsample(Vector3[][] faces, int size)
    {
        var half = size / 2;
        var result = new Vector3[6][];
        for (var face = 0; face < 6; face++)
        {
            var src = faces[face];
            var dst = result[face] = new Vector3[half * half];
            for (var y = 0; y < half; y++)
            {
                for (var x = 0; x < half; x++)
                {
                    dst[y * half + x] = (src[(y * 2) * size + x * 2] +
                                         src[(y * 2) * size + x * 2 + 1] +
                                         src[(y * 2 + 1) * size + x * 2] +
                                         src[(y * 2 + 1) * size + x * 2 + 1]) * 0.25f;
                }
            }
        }
        return result;
    }

    private static Bgra8[] EncodeFace(Vector3[] linear, int size)
    {
        var pixels = new Bgra8[size * size];
        for (var i = 0; i < size * size; i++)
        {
            pixels[i] = new Bgra8(EncodeSrgb(linear[i].X), EncodeSrgb(linear[i].Y), EncodeSrgb(linear[i].Z), 255);
        }
        return pixels;
    }

    /// <summary>Direction through the centre of a cube face texel (GL face order).</summary>
    private static Vector3 TexelDirection(int face, int x, int y, int size)
    {
        var u = (x + 0.5f) / size * 2f - 1f;
        var v = (y + 0.5f) / size * 2f - 1f;
        var dir = face switch
        {
            0 => new Vector3(1, -v, -u),  // +X
            1 => new Vector3(-1, -v, u),  // -X
            2 => new Vector3(u, 1, v),    // +Y
            3 => new Vector3(u, -1, -v),  // -Y
            4 => new Vector3(u, -v, 1),   // +Z
            _ => new Vector3(-u, -v, -1)  // -Z
        };
        return Vector3.Normalize(dir);
    }

    private static Vector3[][] ConvolveIrradiance(Vector3[][] faces)
    {
        // Cosine-convolve against a reduced source (16^2 per face keeps the
        // double loop around 2.3M dot products - instant at load).
        const int sourceSize = 16;
        var source = faces;
        var size = SpecularSize;
        while (size > sourceSize)
        {
            source = Downsample(source, size);
            size /= 2;
        }
        var directions = new Vector3[6 * sourceSize * sourceSize];
        var colors = new Vector3[directions.Length];
        var index = 0;
        for (var face = 0; face < 6; face++)
        {
            for (var y = 0; y < sourceSize; y++)
            {
                for (var x = 0; x < sourceSize; x++)
                {
                    directions[index] = TexelDirection(face, x, y, sourceSize);
                    colors[index] = source[face][y * sourceSize + x];
                    index++;
                }
            }
        }

        var result = new Vector3[6][];
        System.Threading.Tasks.Parallel.For(0, 6, face =>
        {
            result[face] = new Vector3[IrradianceSize * IrradianceSize];
            for (var y = 0; y < IrradianceSize; y++)
            {
                for (var x = 0; x < IrradianceSize; x++)
                {
                    var normal = TexelDirection(face, x, y, IrradianceSize);
                    var sum = Vector3.Zero;
                    var weight = 0f;
                    for (var i = 0; i < directions.Length; i++)
                    {
                        var cosine = Vector3.Dot(normal, directions[i]);
                        if (cosine > 0)
                        {
                            sum += colors[i] * cosine;
                            weight += cosine;
                        }
                    }
                    result[face][y * IrradianceSize + x] = weight > 0 ? sum / weight : Vector3.Zero;
                }
            }
        });
        return result;
    }
}
