using System;
using System.Globalization;
using System.Numerics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// CPU-side contract for unified material fog binding. Transparent materials
/// may sample the volume only after the fullscreen composite has already fogged
/// the opaque scene; otherwise particles/beams would move to the new medium
/// while ships remain in the legacy path.
/// </summary>
public static class VolumetricMaterialFogPolicy
{
    public static VolumetricMaterialFogBinding Evaluate(
        bool requested,
        bool compositeApplied,
        bool integratedAvailable,
        bool temporalApplied,
        bool historyAvailable,
        FroxelGridDesc grid,
        float meanExtinction)
    {
        if (!requested)
        {
            return VolumetricMaterialFogBinding.Off("off");
        }

        if (!compositeApplied)
        {
            return VolumetricMaterialFogBinding.Waiting("waiting for composite");
        }

        if (!integratedAvailable)
        {
            return VolumetricMaterialFogBinding.Waiting("waiting for integrated volume");
        }

        if (!grid.IsValid)
        {
            return VolumetricMaterialFogBinding.Waiting("invalid froxel grid");
        }

        var settings = VolumetricDepthMapping.MaterialFogSettings(grid, meanExtinction);
        var usesHistory = temporalApplied && historyAvailable;
        var source = usesHistory ? "history" : "current";
        return new VolumetricMaterialFogBinding(
            true,
            true,
            compositeApplied,
            integratedAvailable,
            usesHistory,
            settings,
            "bind",
            string.Format(CultureInfo.InvariantCulture,
                "{0} near={1:0.#} far={2:0.#} z={3:0} ext={4:0.0000}",
                source, settings.X, settings.Y, settings.Z, settings.W));
    }
}

public readonly record struct VolumetricMaterialFogBinding(
    bool CanBind,
    bool Requested,
    bool CompositeApplied,
    bool IntegratedAvailable,
    bool UsesHistory,
    Vector4 Settings,
    string Status,
    string DebugSummary)
{
    public static VolumetricMaterialFogBinding Off(string summary) =>
        new(false, false, false, false, false, Vector4.Zero, "off", summary);

    public static VolumetricMaterialFogBinding Waiting(string summary) =>
        new(false, true, false, false, false, Vector4.Zero, "wait", summary);
}
