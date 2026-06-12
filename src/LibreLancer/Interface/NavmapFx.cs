using System;
using System.Numerics;
using LibreLancer.Graphics;

namespace LibreLancer.Interface;

/// <summary>
/// Procedural sprites for the AAA navmap layer: a radial glow and a soft
/// ring, generated once per render context. Drawn additively so halos
/// stack and bloom against the dark map background.
/// </summary>
internal static class NavmapFx
{
    private const int TexSize = 128;
    private static Texture2D? glow;
    private static Texture2D? ring;

    private static Texture2D Generate(RenderContext rc, Func<float, float> alphaForRadius)
    {
        var data = new byte[TexSize * TexSize * 4];
        var half = (TexSize - 1) / 2f;
        for (int y = 0; y < TexSize; y++)
        {
            for (int x = 0; x < TexSize; x++)
            {
                var dx = (x - half) / half;
                var dy = (y - half) / half;
                var d = MathF.Sqrt((dx * dx) + (dy * dy));
                var a = MathHelper.Clamp(alphaForRadius(d), 0, 1);
                var i = ((y * TexSize) + x) * 4;
                // BGRA, white with alpha falloff
                data[i] = 255;
                data[i + 1] = 255;
                data[i + 2] = 255;
                data[i + 3] = (byte)(a * 255);
            }
        }

        var tex = new Texture2D(rc, TexSize, TexSize);
        tex.SetData(data);
        return tex;
    }

    private static Texture2D GetGlow(RenderContext rc) =>
        glow ??= Generate(rc, d => MathF.Pow(MathF.Max(0, 1f - d), 2.6f));

    private static Texture2D GetRing(RenderContext rc) =>
        ring ??= Generate(rc, d =>
        {
            // Annulus centered at r=0.42 with soft 0.07 edges
            var band = 1f - (MathF.Abs(d - 0.42f) / 0.07f);
            return MathF.Pow(MathF.Max(0, band), 1.4f);
        });

    private static TexSource Whole => new() { Rectangle = new Rectangle(0, 0, TexSize, TexSize) };

    /// <summary>Additive radial glow centered on a point (points-space).</summary>
    public static void DrawGlow(UiContext ctx, DrawList2D dl, Vector2 center, float sizePts, Color4 color)
    {
        var rect = ctx.PointsToPixels(new RectangleF(
            center.X - (sizePts / 2f), center.Y - (sizePts / 2f), sizePts, sizePts));
        dl.Draw(GetGlow(ctx.RenderContext), Whole, rect, color, BlendMode.Additive);
    }

    /// <summary>Additive soft ring centered on a point (points-space).</summary>
    public static void DrawRing(UiContext ctx, DrawList2D dl, Vector2 center, float sizePts, Color4 color)
    {
        var rect = ctx.PointsToPixels(new RectangleF(
            center.X - (sizePts / 2f), center.Y - (sizePts / 2f), sizePts, sizePts));
        dl.Draw(GetRing(ctx.RenderContext), Whole, rect, color, BlendMode.Additive);
    }
}
