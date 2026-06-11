using System;

namespace LibreLancer.Render;

/// <summary>
/// Phase 2 linear workflow (docs/LINEAR_AUDIT.md): INI-authored colours
/// (lights, ambients, fog, material constants) are display-referred sRGB
/// values; decode them once on the CPU before they enter the linear
/// lighting math. The tonemap pass performs the single inverse encode.
/// </summary>
public static class ColorSpace
{
    public static float SrgbToLinear(float channel) =>
        channel <= 0.04045f
            ? channel / 12.92f
            : MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);

    public static Color3f SrgbToLinear(Color3f color) =>
        new(SrgbToLinear(color.R), SrgbToLinear(color.G), SrgbToLinear(color.B));

    /// <summary>Alpha stays linear; only the colour channels decode.</summary>
    public static Color4 SrgbToLinear(Color4 color) =>
        new(SrgbToLinear(color.R), SrgbToLinear(color.G), SrgbToLinear(color.B), color.A);
}
