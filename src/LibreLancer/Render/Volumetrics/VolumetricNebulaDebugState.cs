using System.Collections.Generic;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Lightweight HUD/debug snapshot for PR-5.0. The resource-backed views are
/// stubs until PR-5.2 allocates froxel/density/transmittance textures.
/// </summary>
public sealed class VolumetricNebulaDebugState
{
    public RenderFeatureSet Features { get; private set; }
    public RenderDebugView DebugView { get; private set; }
    public int ProfileCount { get; private set; }
    public string ActiveProfile { get; private set; } = "none";
    public string ActiveArchetype { get; private set; } = "none";
    public bool LegacyFallbackActive { get; private set; } = true;
    public bool FroxelResourcesAllocated { get; private set; }
    public bool DensityViewReady { get; private set; }
    public bool TransmittanceViewReady { get; private set; }
    public bool DisplacementViewReady { get; private set; }
    public bool AtmosphereLutViewReady { get; private set; }

    public void Update(
        RenderFeatureSet features,
        NebulaRenderer? activeNebula,
        IReadOnlyList<NebulaVolumeProfile> profiles,
        bool legacyFallbackActive)
    {
        Features = features;
        DebugView = features.DebugView;
        ProfileCount = profiles.Count;
        LegacyFallbackActive = legacyFallbackActive;
        ActiveProfile = "none";
        ActiveArchetype = "none";

        var nickname = activeNebula?.Nebula.Zone?.Nickname;
        if (!string.IsNullOrWhiteSpace(nickname))
        {
            for (var i = 0; i < profiles.Count; i++)
            {
                if (profiles[i].Nickname == nickname)
                {
                    ActiveProfile = profiles[i].Nickname;
                    ActiveArchetype = profiles[i].Archetype;
                    break;
                }
            }
        }

        // PR-5.0/5.1 metadata only. PR-5.2 flips these when resources exist.
        FroxelResourcesAllocated = false;
        DensityViewReady = false;
        TransmittanceViewReady = false;
        DisplacementViewReady = false;
        AtmosphereLutViewReady = false;
    }
}
