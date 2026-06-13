using LibreLancer.Graphics;

namespace LibreLancer.Render;

public readonly record struct VolumetricNebulaFrameDebug(
    bool Requested,
    bool Active,
    bool LegacyFallback,
    string Reason,
    string ProfileNickname,
    int Quality,
    bool NearCascade,
    bool ShipDisplacement,
    bool AtmosphereLuts,
    string DebugView)
{
    public static VolumetricNebulaFrameDebug Evaluate(GameSettings settings, RenderContext rstate)
    {
        var rendererSettings = (IRendererSettings)settings;
        var requested = rendererSettings.SelectedVolumetricNebula;
        var quality = rendererSettings.SelectedVolumetricQuality;
        var debugView = settings.Phase5DebugView;
        var atmosphereLuts = rendererSettings.SelectedAtmosphereLuts;

        if (!requested)
        {
            return new VolumetricNebulaFrameDebug(
                false, false, false, "disabled", "", quality, false, false, atmosphereLuts, debugView);
        }
        if (!rstate.HasFeature(GraphicsFeature.Compute))
        {
            return new VolumetricNebulaFrameDebug(
                true, false, true, "backend has no compute feature", "", quality, false, false, atmosphereLuts, debugView);
        }
        return new VolumetricNebulaFrameDebug(
            true,
            false,
            true,
            "froxel resources are scheduled for PR-5.2",
            "",
            quality,
            rendererSettings.SelectedVolumetricNearCascade,
            rendererSettings.SelectedVolumetricShipDisplacement,
            atmosphereLuts,
            debugView);
    }
}
