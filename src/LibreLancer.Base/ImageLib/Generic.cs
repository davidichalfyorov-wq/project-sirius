// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System.IO;
using LibreLancer.Graphics;
using StbImageSharp;

namespace LibreLancer.ImageLib;

public static class Generic
{
    public static Image ImageFromStream(Stream stream, bool flip = false)
    {
        var len = (int)stream.Length;
        var b = new byte[len];
        var pos = 0;
        int r;

        while ((r = stream.Read(b, pos, len - pos)) > 0)
        {
            pos += r;
        }

        /* stb_image it */
        StbImage.stbi_set_flip_vertically_on_load(flip ? 1 : 0);
        ImageResult image = ImageResult.FromMemory(b, ColorComponents.RedGreenBlueAlpha);
        for (var i = 0; i < image.Data.Length; i += 4)
        {
            (image.Data[i + 2], image.Data[i]) = (image.Data[i], image.Data[i + 2]);
        }
        return new Image
        {
            Width = image.Width, Height = image.Height, Data = image.Data
        };
    }

    public static Texture TextureFromStream(RenderContext context, Stream stream, bool flip = true)
    {
        if (DDS.StreamIsDDS(stream))
        {
            return DDS.FromStream(context, stream);
        }

        /* Read full stream */
        var len = (int)stream.Length;
        var b = new byte[len];
        var pos = 0;
        int r;

        while ((r = stream.Read(b, pos, len - pos)) > 0)
        {
            pos += r;
        }

        /* stb_image it */
        StbImage.stbi_set_flip_vertically_on_load(flip ? 1 : 0);
        ImageResult image = ImageResult.FromMemory(b, ColorComponents.RedGreenBlueAlpha);
        for (var i = 0; i < image.Data.Length; i += 4)
        {
            (image.Data[i + 2], image.Data[i]) = (image.Data[i], image.Data[i + 2]);
        }

        var texture = new Texture2D(context, image.Width, image.Height, false, SurfaceFormat.Bgra8);
        texture.SetData(image.Data);
        return texture;
    }

    /// <summary>
    /// Loads an image and uploads a full CPU-generated mip chain. Used for hi-res UI
    /// sprites that are minified at lower window resolutions (plain bilinear sampling
    /// of a 3-4x density sprite aliases its hairlines).
    /// </summary>
    public static Texture TextureFromStreamMipmapped(RenderContext context, Stream stream, bool flip = true)
    {
        if (DDS.StreamIsDDS(stream))
        {
            return DDS.FromStream(context, stream);
        }

        var len = (int)stream.Length;
        var b = new byte[len];
        var pos = 0;
        int r;

        while ((r = stream.Read(b, pos, len - pos)) > 0)
        {
            pos += r;
        }

        StbImage.stbi_set_flip_vertically_on_load(flip ? 1 : 0);
        ImageResult image = ImageResult.FromMemory(b, ColorComponents.RedGreenBlueAlpha);
        for (var i = 0; i < image.Data.Length; i += 4)
        {
            (image.Data[i + 2], image.Data[i]) = (image.Data[i], image.Data[i + 2]);
        }

        var texture = new Texture2D(context, image.Width, image.Height, true, SurfaceFormat.Bgra8);
        texture.SetData(image.Data);
        var data = image.Data;
        int w = image.Width, h = image.Height, level = 1;
        while (w > 1 || h > 1)
        {
            int nw = System.Math.Max(1, w >> 1), nh = System.Math.Max(1, h >> 1);
            data = BoxHalveBgra(data, w, h, nw, nh);
            if (level >= texture.LevelCount)
                break;
            texture.SetData(level, null, data, 0, data.Length);
            w = nw; h = nh; level++;
        }
        return texture;
    }

    // Alpha-weighted 2x2 box filter: plain per-channel averaging bleeds the (black)
    // colour of fully transparent texels into glass-panel edges.
    static byte[] BoxHalveBgra(byte[] src, int w, int h, int nw, int nh)
    {
        var dst = new byte[nw * nh * 4];
        for (int y = 0; y < nh; y++)
        {
            int sy0 = System.Math.Min(h - 1, y * 2), sy1 = System.Math.Min(h - 1, y * 2 + 1);
            for (int x = 0; x < nw; x++)
            {
                int sx0 = System.Math.Min(w - 1, x * 2), sx1 = System.Math.Min(w - 1, x * 2 + 1);
                int i00 = (sy0 * w + sx0) * 4, i01 = (sy0 * w + sx1) * 4;
                int i10 = (sy1 * w + sx0) * 4, i11 = (sy1 * w + sx1) * 4;
                int a00 = src[i00 + 3], a01 = src[i01 + 3], a10 = src[i10 + 3], a11 = src[i11 + 3];
                int aSum = a00 + a01 + a10 + a11;
                int o = (y * nw + x) * 4;
                if (aSum == 0)
                {
                    dst[o + 3] = 0;
                }
                else
                {
                    dst[o] = (byte)((src[i00] * a00 + src[i01] * a01 + src[i10] * a10 + src[i11] * a11) / aSum);
                    dst[o + 1] = (byte)((src[i00 + 1] * a00 + src[i01 + 1] * a01 + src[i10 + 1] * a10 + src[i11 + 1] * a11) / aSum);
                    dst[o + 2] = (byte)((src[i00 + 2] * a00 + src[i01 + 2] * a01 + src[i10 + 2] * a10 + src[i11 + 2] * a11) / aSum);
                    dst[o + 3] = (byte)((aSum + 2) / 4);
                }
            }
        }
        return dst;
    }
}
