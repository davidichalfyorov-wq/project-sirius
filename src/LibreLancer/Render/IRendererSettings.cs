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

    /// <summary>
    /// Canonical PR-5.0 feature flag for the new froxel nebula path.
    /// The legacy plural setting remains as a compatibility alias.
    /// </summary>
    bool SelectedVolumetricNebula => SelectedVolumetricNebulae;

    /// <summary>
    /// Compatibility alias for configs written during earlier experiments.
    /// </summary>
    bool SelectedVolumetricNebulae { get; }

    int SelectedVolumetricQuality { get; }

    /// <summary>Near-camera high-frequency froxel cascade. Stubbed until PR-5.2+.</summary>
    bool SelectedVolumetricNearCascade => false;

    /// <summary>Composite the near froxel cascade together with the main volume. Opt-in PR-5.12 path.</summary>
    bool SelectedVolumetricNearComposite => false;

    /// <summary>High-frequency near-field density/detail tuning. Opt-in PR-5.13 path.</summary>
    bool SelectedVolumetricNearDetail => false;

    /// <summary>Interactive ship density displacement/wake. Stubbed until PR-5.2+.</summary>
    bool SelectedVolumetricShipDisplacement => false;

    /// <summary>Persistent near-field wake trail history for ship fog displacement.</summary>
    bool SelectedVolumetricWakeHistory => false;

    /// <summary>Velocity-aligned curl vector field for subtle wake turbulence.</summary>
    bool SelectedVolumetricWakeCurl => false;

    /// <summary>First visible opt-in froxel composite. Default false; legacy nebula remains fallback.</summary>
    bool SelectedVolumetricComposite => false;

    /// <summary>Bind integrated froxel fog to material shaders. Scaffold for transparent effects.</summary>
    bool SelectedVolumetricMaterialFog => false;

    /// <summary>Inject segmented lightning-channel light into the froxel lighting volume.</summary>
    bool SelectedVolumetricLightningChannels => false;

    /// <summary>Temporal history/reprojection scaffold for integrated froxel fog.</summary>
    bool SelectedVolumetricTemporal => false;

    /// <summary>Depth-aware history reprojection/rejection for temporal froxel fog.</summary>
    bool SelectedVolumetricReprojection => false;

    /// <summary>Blue-noise/STBN jitter source for density and temporal passes.</summary>
    bool SelectedVolumetricBlueNoise => false;

    /// <summary>Atmosphere LUT pipeline for planets with atmospheres. Stubbed until B1.</summary>
    bool SelectedAtmosphereLuts => false;

    /// <summary>Renderer debug view selector, e.g. off, voldensity, voltransmittance, froxels.</summary>
    string SelectedDebugView => "off";

    /// <summary>Enable backend debug labels/markers where supported.</summary>
    bool SelectedRenderDebugMarkers => true;

    /// <summary>Request a capture on the first rendered frame. No-op until a capture backend is linked.</summary>
    bool SelectedRenderCaptureStartup => false;

    /// <summary>Request a capture on the next rendered frame. No-op until a capture backend is linked.</summary>
    bool SelectedRenderCaptureNextFrame => false;

    /// <summary>Optional capture output path for RenderDoc/vulkan-tools integration.</summary>
    string? SelectedRenderCapturePath => null;
}
