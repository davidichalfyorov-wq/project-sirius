// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using LibreLancer.Data.Ini;
using LibreLancer.Render;

namespace LibreLancer;

public partial class GameSettings
{
    // PR-5.0/5.1 feature flags. They live in a partial class so the foundation
    // patch does not rewrite the existing settings file or disturb unrelated
    // graphics fixes. Defaults keep the legacy renderer path unchanged.
    [Entry("volumetric_nebula")]
    public bool VolumetricNebula = false;

    [Entry("volumetric_near_cascade")]
    public bool VolumetricNearCascade = false;

    [Entry("volumetric_ship_displacement")]
    public bool VolumetricShipDisplacement = false;

    [Entry("atmosphere_luts")]
    public bool AtmosphereLuts = false;

    [Entry("debug_view")]
    public string DebugView = "off";

    [Entry("dev_hud")]
    public bool DevHud = false;

    [Entry("pass_timings")]
    public bool PassTimings = false;

    [Entry("renderdoc_capture")]
    public bool RenderDocCapture = true;

    [Entry("renderdoc_capture_frame")]
    public int RenderDocCaptureFrame = -1;

    [Entry("renderdoc_capture_path")]
    public string RenderDocCapturePath = "";

    bool IRendererSettings.SelectedVolumetricNebula => VolumetricNebula;

    bool IRendererSettings.SelectedVolumetricNearCascade => VolumetricNebula && VolumetricNearCascade;

    bool IRendererSettings.SelectedVolumetricShipDisplacement => VolumetricNebula && VolumetricShipDisplacement;

    bool IRendererSettings.SelectedAtmosphereLuts => AtmosphereLuts;

    string IRendererSettings.SelectedDebugView => NormalizePhase5DebugView(DebugView);

    bool IRendererSettings.SelectedRenderDocCapture => RenderDocCapture;

    int IRendererSettings.SelectedRenderDocCaptureFrame => System.Math.Max(-1, RenderDocCaptureFrame);

    string? IRendererSettings.SelectedRenderDocCapturePath =>
        string.IsNullOrWhiteSpace(RenderDocCapturePath) ? null : RenderDocCapturePath;

    bool IRendererSettings.SelectedPassTimings => PassTimings;

    bool IRendererSettings.SelectedDevHud => DevHud;

    private static string NormalizePhase5DebugView(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "0" or "off" or "none" => "off",
            "albedo" or "normals" or "roughness" or "metallic" or "shadow" or "ibl" => value!.Trim().ToLowerInvariant(),
            "vol_density" or "density" => "vol_density",
            "vol_transmittance" or "transmittance" => "vol_transmittance",
            "vol_froxels" or "froxels" => "vol_froxels",
            "vol_zones" or "volzones" or "zones" => "vol_zones",
            "vol_displacement" or "voldisp" or "displacement" => "vol_displacement",
            "atmosphere_luts" or "atmoluts" or "atmo_luts" => "atmosphere_luts",
            _ => "off"
        };
    }
}
