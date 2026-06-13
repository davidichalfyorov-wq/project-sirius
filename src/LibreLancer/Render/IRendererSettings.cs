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

    // Phase 5 feature gates. These default implementations keep existing
    // IRendererSettings implementers source-compatible while the game settings
    // partial class wires the real config keys in PR-5.0/5.1.
    bool SelectedVolumetricNebula => false;
    bool SelectedVolumetricNearCascade => false;
    bool SelectedVolumetricShipDisplacement => false;
    bool SelectedAtmosphereLuts => false;
    string SelectedDebugView => System.Environment.GetEnvironmentVariable("SIRIUS_DEBUG_VIEW") ?? "off";
    bool SelectedRenderDocCapture => System.Environment.GetEnvironmentVariable("SIRIUS_RENDERDOC_CAPTURE") != "0";
    int SelectedRenderDocCaptureFrame => int.TryParse(System.Environment.GetEnvironmentVariable("SIRIUS_CAPTURE_FRAME"), out var frame) ? frame : -1;
    string? SelectedRenderDocCapturePath => System.Environment.GetEnvironmentVariable("SIRIUS_CAPTURE_PATH");
    bool SelectedPassTimings => System.Environment.GetEnvironmentVariable("SIRIUS_PASS_TIMINGS") == "1";
    bool SelectedDevHud => System.Environment.GetEnvironmentVariable("SIRIUS_DEV_HUD") == "1";
}
