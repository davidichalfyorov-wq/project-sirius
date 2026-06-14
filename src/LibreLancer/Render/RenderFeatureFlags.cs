using System;

namespace LibreLancer.Render;

[Flags]
public enum RenderFeatureBits
{
    None = 0,
    VolumetricNebula = 1 << 0,
    VolumetricNearCascade = 1 << 1,
    VolumetricShipDisplacement = 1 << 2,
    AtmosphereLuts = 1 << 3,
    DebugMarkers = 1 << 4,
    CaptureTooling = 1 << 5,
    VolumetricComposite = 1 << 6,
    VolumetricMaterialFog = 1 << 7,
    VolumetricLightningChannels = 1 << 8,
    VolumetricTemporal = 1 << 9,
    VolumetricReprojection = 1 << 10,
    VolumetricBlueNoise = 1 << 11,
    VolumetricNearComposite = 1 << 12,
    VolumetricNearDetail = 1 << 13,
    VolumetricWakeHistory = 1 << 14
}

/// <summary>
/// Debug views reserved by PR-5.0 for current/future render HUDs.
/// Views that need froxel resources are intentionally stubs until PR-5.2.
/// </summary>
public enum RenderDebugView
{
    Off,
    Depth,
    Normals,
    Albedo,
    Roughness,
    Metallic,
    Shadow,
    Ibl,
    VolumetricZones,
    VolumetricDensity,
    VolumetricTransmittance,
    VolumetricFroxels,
    VolumetricDisplacement,
    VolumetricDisplacementHistory,
    VolumetricLightning,
    VolumetricHistory,
    VolumetricHistoryConfidence,
    VolumetricJitter,
    VolumetricNear,
    VolumetricNearDensity,
    AtmosphereLuts,
    AtmosphereAerial
}

/// <summary>
/// Immutable frame feature snapshot. It merges renderer settings with SIRIUS_*
/// environment overrides, so graphics PRs can be tested without changing INI files.
/// </summary>
public readonly record struct RenderFeatureSet(
    RenderFeatureBits Bits,
    int VolumetricQuality,
    RenderDebugView DebugView,
    string? CapturePath)
{
    public bool VolumetricNebula => Bits.HasFlag(RenderFeatureBits.VolumetricNebula);
    public bool VolumetricNearCascade => Bits.HasFlag(RenderFeatureBits.VolumetricNearCascade);
    public bool VolumetricShipDisplacement => Bits.HasFlag(RenderFeatureBits.VolumetricShipDisplacement);
    public bool VolumetricComposite => Bits.HasFlag(RenderFeatureBits.VolumetricComposite);
    public bool VolumetricMaterialFog => Bits.HasFlag(RenderFeatureBits.VolumetricMaterialFog);
    public bool VolumetricLightningChannels => Bits.HasFlag(RenderFeatureBits.VolumetricLightningChannels);
    public bool VolumetricTemporal => Bits.HasFlag(RenderFeatureBits.VolumetricTemporal);
    public bool VolumetricReprojection => Bits.HasFlag(RenderFeatureBits.VolumetricReprojection);
    public bool VolumetricBlueNoise => Bits.HasFlag(RenderFeatureBits.VolumetricBlueNoise);
    public bool VolumetricNearComposite => Bits.HasFlag(RenderFeatureBits.VolumetricNearComposite);
    public bool VolumetricNearDetail => Bits.HasFlag(RenderFeatureBits.VolumetricNearDetail);
    public bool VolumetricWakeHistory => Bits.HasFlag(RenderFeatureBits.VolumetricWakeHistory);
    public bool AtmosphereLuts => Bits.HasFlag(RenderFeatureBits.AtmosphereLuts);
    public bool DebugMarkers => Bits.HasFlag(RenderFeatureBits.DebugMarkers);
    public bool CaptureTooling => Bits.HasFlag(RenderFeatureBits.CaptureTooling);

    public static RenderFeatureSet FromSettings(IRendererSettings settings)
    {
        var bits = RenderFeatureBits.None;
        if (OverrideBool(settings.SelectedVolumetricNebula, "SIRIUS_VOLUMETRIC_NEBULA", "SIRIUS_VOLFOG"))
            bits |= RenderFeatureBits.VolumetricNebula;
        if (OverrideBool(settings.SelectedVolumetricNearCascade, "SIRIUS_VOLUMETRIC_NEAR_CASCADE", "SIRIUS_VOLNEAR"))
            bits |= RenderFeatureBits.VolumetricNearCascade;
        if (OverrideBool(settings.SelectedVolumetricShipDisplacement, "SIRIUS_VOLUMETRIC_SHIP_DISPLACEMENT", "SIRIUS_VOLDISP"))
            bits |= RenderFeatureBits.VolumetricShipDisplacement;
        if (OverrideBool(settings.SelectedVolumetricWakeHistory, "SIRIUS_VOLUMETRIC_WAKE_HISTORY",
                "SIRIUS_VOLFOG_WAKE_HISTORY", "SIRIUS_VOLWAKE_HISTORY"))
            bits |= RenderFeatureBits.VolumetricWakeHistory;
        if (OverrideBool(settings.SelectedVolumetricComposite, "SIRIUS_VOLFOG_COMPOSITE", "SIRIUS_VOLUMETRIC_COMPOSITE"))
            bits |= RenderFeatureBits.VolumetricComposite;
        if (OverrideBool(settings.SelectedVolumetricMaterialFog, "SIRIUS_VOLFOG_MATERIALS", "SIRIUS_VOLUMETRIC_MATERIAL_FOG"))
            bits |= RenderFeatureBits.VolumetricMaterialFog;
        if (OverrideBool(settings.SelectedVolumetricLightningChannels, "SIRIUS_VOLUMETRIC_LIGHTNING_CHANNELS",
                "SIRIUS_VOLFOG_LIGHTNING", "SIRIUS_VOLLIGHTNING"))
            bits |= RenderFeatureBits.VolumetricLightningChannels;
        if (OverrideBool(settings.SelectedVolumetricTemporal, "SIRIUS_VOLFOG_TEMPORAL", "SIRIUS_VOLUMETRIC_TEMPORAL"))
            bits |= RenderFeatureBits.VolumetricTemporal;
        if (OverrideBool(settings.SelectedVolumetricReprojection, "SIRIUS_VOLFOG_REPROJECT",
                "SIRIUS_VOLUMETRIC_REPROJECTION"))
            bits |= RenderFeatureBits.VolumetricReprojection;
        if (OverrideBool(settings.SelectedVolumetricBlueNoise, "SIRIUS_VOLFOG_BLUE_NOISE",
                "SIRIUS_VOLFOG_STBN", "SIRIUS_VOLUMETRIC_BLUE_NOISE"))
            bits |= RenderFeatureBits.VolumetricBlueNoise;
        if (OverrideBool(settings.SelectedVolumetricNearComposite, "SIRIUS_VOLFOG_NEAR_COMPOSITE",
                "SIRIUS_VOLNEAR_COMPOSITE", "SIRIUS_VOLUMETRIC_NEAR_COMPOSITE"))
            bits |= RenderFeatureBits.VolumetricNearComposite;
        if (OverrideBool(settings.SelectedVolumetricNearDetail, "SIRIUS_VOLFOG_NEAR_DETAIL",
                "SIRIUS_VOLNEAR_DETAIL", "SIRIUS_VOLUMETRIC_NEAR_DETAIL"))
            bits |= RenderFeatureBits.VolumetricNearDetail;
        if (OverrideBool(settings.SelectedAtmosphereLuts, "SIRIUS_ATMOSPHERE_LUTS", "SIRIUS_ATMO_LUTS"))
            bits |= RenderFeatureBits.AtmosphereLuts;
        if (OverrideBool(settings.SelectedRenderDebugMarkers, "SIRIUS_RENDER_DEBUG_MARKERS", "SIRIUS_DEBUG_MARKERS"))
            bits |= RenderFeatureBits.DebugMarkers;
        if (OverrideBool(settings.SelectedRenderCaptureStartup || settings.SelectedRenderCaptureNextFrame,
                "SIRIUS_RENDER_CAPTURE", "SIRIUS_CAPTURE_NEXT_FRAME"))
            bits |= RenderFeatureBits.CaptureTooling;

        // Dependent toggles cannot run without the parent medium.
        if ((bits & RenderFeatureBits.VolumetricNebula) == 0)
        {
            bits &= ~(RenderFeatureBits.VolumetricNearCascade |
                      RenderFeatureBits.VolumetricShipDisplacement |
                      RenderFeatureBits.VolumetricComposite |
                      RenderFeatureBits.VolumetricMaterialFog |
                      RenderFeatureBits.VolumetricLightningChannels |
                      RenderFeatureBits.VolumetricTemporal |
                      RenderFeatureBits.VolumetricReprojection |
                      RenderFeatureBits.VolumetricBlueNoise |
                      RenderFeatureBits.VolumetricNearComposite |
                      RenderFeatureBits.VolumetricNearDetail |
                      RenderFeatureBits.VolumetricWakeHistory);
        }
        if ((bits & RenderFeatureBits.VolumetricTemporal) == 0)
        {
            bits &= ~RenderFeatureBits.VolumetricReprojection;
        }
        if ((bits & (RenderFeatureBits.VolumetricNearCascade | RenderFeatureBits.VolumetricComposite)) !=
            (RenderFeatureBits.VolumetricNearCascade | RenderFeatureBits.VolumetricComposite))
        {
            bits &= ~RenderFeatureBits.VolumetricNearComposite;
        }
        if ((bits & RenderFeatureBits.VolumetricNearCascade) == 0)
        {
            bits &= ~RenderFeatureBits.VolumetricNearDetail;
        }
        if ((bits & (RenderFeatureBits.VolumetricShipDisplacement | RenderFeatureBits.VolumetricNearCascade)) !=
            (RenderFeatureBits.VolumetricShipDisplacement | RenderFeatureBits.VolumetricNearCascade))
        {
            bits &= ~RenderFeatureBits.VolumetricWakeHistory;
        }

        var debugView = ParseDebugView(
            FirstNonEmpty(Environment.GetEnvironmentVariable("SIRIUS_DEBUG_VIEW"), settings.SelectedDebugView));
        var capturePath = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SIRIUS_CAPTURE_PATH"), settings.SelectedRenderCapturePath);
        return new RenderFeatureSet(bits, Math.Clamp(settings.SelectedVolumetricQuality, 0, 3), debugView, capturePath);
    }

    private static string? FirstNonEmpty(string? a, string? b) =>
        string.IsNullOrWhiteSpace(a) ? (string.IsNullOrWhiteSpace(b) ? null : b) : a;

    private static bool OverrideBool(bool fallback, params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("on", StringComparison.OrdinalIgnoreCase);
        }
        return fallback;
    }

    private static RenderDebugView ParseDebugView(string? value) =>
        (value ?? "off").Trim().ToLowerInvariant() switch
        {
            "depth" => RenderDebugView.Depth,
            "normals" or "normal" => RenderDebugView.Normals,
            "albedo" => RenderDebugView.Albedo,
            "roughness" => RenderDebugView.Roughness,
            "metallic" => RenderDebugView.Metallic,
            "shadow" or "shadows" => RenderDebugView.Shadow,
            "ibl" => RenderDebugView.Ibl,
            "vol_zones" or "volzones" or "zones" or "volumetric_zones" or "volume_zones" => RenderDebugView.VolumetricZones,
            "vol_density" or "voldensity" or "density" or "volumetric_density" => RenderDebugView.VolumetricDensity,
            "vol_transmittance" or "voltransmittance" or "transmittance" or "volumetric_transmittance" => RenderDebugView.VolumetricTransmittance,
            "vol_froxels" or "froxels" or "volfroxels" or "froxel" => RenderDebugView.VolumetricFroxels,
            "vol_displacement" or "voldisp" or "displacement" => RenderDebugView.VolumetricDisplacement,
            "vol_displacement_history" or "voldisphistory" or "vol_wake_history" or "wake_history" => RenderDebugView.VolumetricDisplacementHistory,
            "vol_lightning" or "vollightning" or "lightning_channels" or "vol_lightning_channels" => RenderDebugView.VolumetricLightning,
            "vol_history" or "volhistory" or "history" or "volumetric_history" => RenderDebugView.VolumetricHistory,
            "vol_history_confidence" or "volconfidence" or "vol_confidence" or "history_confidence" => RenderDebugView.VolumetricHistoryConfidence,
            "vol_jitter" or "voljitter" or "jitter" or "vol_blue_noise" or "vol_stbn" => RenderDebugView.VolumetricJitter,
            "vol_near" or "volnear" or "near" or "near_froxels" or "volumetric_near" => RenderDebugView.VolumetricNear,
            "vol_near_density" or "volneardensity" or "near_density" or "neardensity" => RenderDebugView.VolumetricNearDensity,
            "atmoluts" or "atmosphere_luts" or "atmo_luts" => RenderDebugView.AtmosphereLuts,
            "atmo_aerial" or "atmoaerial" or "aerial" => RenderDebugView.AtmosphereAerial,
            _ => RenderDebugView.Off
        };
}
