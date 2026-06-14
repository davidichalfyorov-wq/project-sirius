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
        return EvaluateSnapshot(
            rendererSettings.SelectedVolumetricNebula,
            rstate.HasFeature(GraphicsFeature.Compute),
            rendererSettings.SelectedVolumetricQuality,
            settings.Phase5DebugView,
            rendererSettings.SelectedVolumetricNearCascade,
            rendererSettings.SelectedVolumetricNearDetail,
            rendererSettings.SelectedVolumetricShipDisplacement,
            rendererSettings.SelectedAtmosphereLuts,
            VolumetricNebulaFrameResources.LastDebug);
    }

    public static VolumetricNebulaFrameDebug EvaluateSnapshot(
        bool requested,
        bool computeSupported,
        int quality,
        string debugView,
        bool nearCascade,
        bool nearDetail,
        bool shipDisplacement,
        bool atmosphereLuts,
        VolumetricNebulaResourceDebug resources)
    {
        if (!requested)
        {
            return new VolumetricNebulaFrameDebug(
                false, false, false, "disabled", "", quality, false, false, false, atmosphereLuts, debugView);
        }
        if (!computeSupported)
        {
            return new VolumetricNebulaFrameDebug(
                true, false, true, "backend has no compute feature", "", quality, false, false, false, atmosphereLuts, debugView);
        }

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
                nearCascade,
                nearDetail,
                shipDisplacement,
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
            nearCascade,
            nearDetail,
            shipDisplacement,
            atmosphereLuts,
            debugView);
    }
}
