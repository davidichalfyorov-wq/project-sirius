using LibreLancer.Graphics;
using LibreLancer.Render.Volumetrics;

namespace LibreLancer.Render;

public readonly record struct VolumetricNebulaFrameDebug(
    bool Requested,
    bool Active,
    bool LegacyFallback,
    string Reason,
    string ProfileNickname,
    int Quality,
    bool NearCascade,
    bool NearDetail,
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
                false, false, false, "disabled", "", quality, false, false, false, atmosphereLuts, debugView);
        }
        if (!rstate.HasFeature(GraphicsFeature.Compute))
        {
            return new VolumetricNebulaFrameDebug(
                true, false, true, "backend has no compute feature", "", quality, false, false, false, atmosphereLuts, debugView);
        }

        var resources = VolumetricNebulaFrameResources.LastDebug;
        if (resources.Allocated)
        {
            var compositeActive = resources.LastOperation.Contains("composite", System.StringComparison.OrdinalIgnoreCase);
            return new VolumetricNebulaFrameDebug(
                true,
                compositeActive,
                !compositeActive,
                resources.LastOperation,
                resources.ActiveProfile,
                resources.Quality,
                rendererSettings.SelectedVolumetricNearCascade,
                rendererSettings.SelectedVolumetricNearDetail,
                rendererSettings.SelectedVolumetricShipDisplacement,
                atmosphereLuts,
                debugView);
        }

        return new VolumetricNebulaFrameDebug(
            true,
            false,
            true,
            resources.LastOperation,
            resources.ActiveProfile,
            quality,
            rendererSettings.SelectedVolumetricNearCascade,
            rendererSettings.SelectedVolumetricNearDetail,
            rendererSettings.SelectedVolumetricShipDisplacement,
            atmosphereLuts,
            debugView);
    }
}
