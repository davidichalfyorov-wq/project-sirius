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
    bool ShipDisplacement,
    bool AtmosphereLuts,
    string DebugView,
    bool ResourcesAllocated,
    string FroxelDimensions,
    string NearFroxelDimensions,
    string ResourceQuality,
    long EstimatedResourceBytes,
    string ResourceOperation,
    int PassSlotCount)
{
    public static VolumetricNebulaFrameDebug Evaluate(GameSettings settings, RenderContext rstate)
    {
        var rendererSettings = (IRendererSettings)settings;
        var requested = rendererSettings.SelectedVolumetricNebula;
        var quality = rendererSettings.SelectedVolumetricQuality;
        var debugView = settings.Phase5DebugView;
        var atmosphereLuts = rendererSettings.SelectedAtmosphereLuts;
        var resources = VolumetricNebulaFrameResources.LastDebug;

        if (!requested)
        {
            return Build(false, false, false, "disabled", "", quality, false, false, atmosphereLuts, debugView, resources);
        }
        if (!rstate.HasFeature(GraphicsFeature.Compute))
        {
            return Build(true, false, true, "backend has no compute feature", "", quality, false, false, atmosphereLuts, debugView, resources);
        }
        return Build(
            true,
            false,
            true,
            resources.Allocated
                ? "froxel resources allocated; composite pass disabled until PR-5.3"
                : resources.LastOperation,
            resources.ActiveProfile,
            quality,
            rendererSettings.SelectedVolumetricNearCascade,
            rendererSettings.SelectedVolumetricShipDisplacement,
            atmosphereLuts,
            debugView,
            resources);
    }

    private static VolumetricNebulaFrameDebug Build(
        bool requested,
        bool active,
        bool legacyFallback,
        string reason,
        string profileNickname,
        int quality,
        bool nearCascade,
        bool shipDisplacement,
        bool atmosphereLuts,
        string debugView,
        VolumetricNebulaResourceDebug resources) =>
        new(
            requested,
            active,
            legacyFallback,
            reason,
            profileNickname,
            quality,
            nearCascade,
            shipDisplacement,
            atmosphereLuts,
            debugView,
            resources.Allocated,
            resources.Dimensions,
            resources.NearDimensions,
            resources.QualityName,
            resources.EstimatedBytes,
            resources.LastOperation,
            resources.PassSlotCount);
}
