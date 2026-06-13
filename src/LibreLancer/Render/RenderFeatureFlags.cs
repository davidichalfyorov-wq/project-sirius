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
    CaptureTooling = 1 << 5
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
            bits &= ~(RenderFeatureBits.VolumetricNearCascade | RenderFeatureBits.VolumetricShipDisplacement);
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
            "volzones" or "volumetric_zones" or "volume_zones" => RenderDebugView.VolumetricZones,
            "voldensity" or "density" or "volumetric_density" => RenderDebugView.VolumetricDensity,
            "voltransmittance" or "transmittance" => RenderDebugView.VolumetricTransmittance,
            "froxels" or "volfroxels" or "froxel" => RenderDebugView.VolumetricFroxels,
            "voldisp" or "displacement" => RenderDebugView.VolumetricDisplacement,
            "atmoluts" or "atmosphere_luts" => RenderDebugView.AtmosphereLuts,
            "atmoaerial" or "aerial" => RenderDebugView.AtmosphereAerial,
            _ => RenderDebugView.Off
        };
}
