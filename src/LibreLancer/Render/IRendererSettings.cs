using LibreLancer.Graphics;

namespace LibreLancer.Render;

public enum TonemapMode
{
    Off,
    Aces,
    // Identity below the knee, smooth shoulder above: keeps the classic
    // Freelancer look in the dim/mid range and only rolls off highlights.
    Filmic
}

public enum PostAaMode
{
    Off,
    Fxaa,
    Smaa
}

public interface IRendererSettings
{
    int SelectedMSAA { get; }
    TextureFiltering SelectedFiltering { get; }
    int SelectedAnisotropy { get; }
    float LodMultiplier { get; }
    bool SelectedHdr { get; }
    TonemapMode SelectedTonemapper { get; }
    float SelectedExposure { get; }
    bool SelectedBloom { get; }
    float SelectedBloomThreshold { get; }
    float SelectedBloomIntensity { get; }
    float SelectedBloomRadius { get; }
    int SelectedBloomMips { get; }
    bool SelectedGodRays { get; }
    float SelectedGodRaysIntensity { get; }
    int SelectedGodRaysSamples { get; }
    PostAaMode SelectedPostAa { get; }
    bool SelectedIbl { get; }
    bool SelectedShadows { get; }
    bool SelectedRtShadows { get; }
    bool SelectedRtao { get; }
    bool SelectedRtReflections { get; }
    bool SelectedMeshAsteroids { get; }
    bool SelectedVrs { get; }
    bool SelectedVolumetricNebulae { get; }
    int SelectedVolumetricQuality { get; }
}
