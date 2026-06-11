namespace LibreLancer.Graphics;

public record struct SamplerState(TextureFiltering Filtering, WrapMode WrapS, WrapMode WrapT)
{
    public static readonly SamplerState LinearRepeat = new(TextureFiltering.Linear, WrapMode.Repeat,
        WrapMode.Repeat);

    public static readonly SamplerState LinearClamp = new(TextureFiltering.Linear, WrapMode.ClampToEdge,
        WrapMode.ClampToEdge);

    /// <summary>Packed-data textures (shadow atlas): filtering would mix
    /// the RGB-encoded depth channels into garbage.</summary>
    public static readonly SamplerState PointClamp = new(TextureFiltering.Nearest, WrapMode.ClampToEdge,
        WrapMode.ClampToEdge);

    internal static readonly SamplerState Unset = new((TextureFiltering)(-1), (WrapMode) (-1), (WrapMode)(-1));
}
