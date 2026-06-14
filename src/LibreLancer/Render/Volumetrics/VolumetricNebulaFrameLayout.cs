using System;
using System.Collections.Generic;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Production-facing Phase 5 quality profile. PR-5.2 only allocates the main
/// froxel textures, but the contract already reserves the fields needed by the
/// later density, lighting, temporal, near-cascade, displacement and atmosphere
/// passes so the renderer does not grow into a one-off Li01 demo.
/// </summary>
public readonly record struct VolumetricNebulaQualityProfile(
    int Quality,
    string Name,
    FroxelGridDesc MainGrid,
    FroxelGridDesc NearGrid,
    VolumetricTemporalProfile Temporal,
    VolumetricJitterProfile Jitter,
    VolumetricDensityAuthoringProfile Density,
    VolumetricLightingProfile Lighting,
    VolumetricDisplacementProfile Displacement,
    VolumetricMaterialFogProfile MaterialFog,
    VolumetricAtmosphereBridgeProfile Atmosphere,
    VolumetricNebulaPerformanceProfile Performance)
{
    public static VolumetricNebulaQualityProfile Create(
        int renderWidth,
        int renderHeight,
        global::LibreLancer.Render.RenderFeatureSet features,
        NebulaVolumeProfile profile)
    {
        var performance = VolumetricNebulaPerformanceProfile.Evaluate(renderWidth, renderHeight, features, profile);
        var q = performance.EffectiveQuality;
        var main = FroxelGridDesc.MainForViewport(renderWidth, renderHeight, q);
        var near = FroxelGridDesc.NearForViewport(renderWidth, renderHeight, q);
        performance = performance.WithGridBudgets(main.VoxelCount, near.VoxelCount,
            main.BytesPerVolume() * 6 + near.BytesPerVolume() * 5);
        return q switch
        {
            0 => Make("low", q, main, near, profile, performance, historyWeight: 0.88f, densityOctaves: 3, detailOctaves: 1,
                selfShadowSteps: 3, localLightBudget: 2, lightningBudget: 1, blueNoiseFrameModulo: 8),
            1 => Make("medium", q, main, near, profile, performance, historyWeight: 0.90f, densityOctaves: 4, detailOctaves: 2,
                selfShadowSteps: 4, localLightBudget: 4, lightningBudget: 2, blueNoiseFrameModulo: 16),
            3 => Make("ultra", q, main, near, profile, performance, historyWeight: 0.94f, densityOctaves: 6, detailOctaves: 4,
                selfShadowSteps: 8, localLightBudget: 12, lightningBudget: 6, blueNoiseFrameModulo: 32),
            _ => Make("high", q, main, near, profile, performance, historyWeight: 0.92f, densityOctaves: 5, detailOctaves: 3,
                selfShadowSteps: 6, localLightBudget: 8, lightningBudget: 4, blueNoiseFrameModulo: 16)
        };
    }

    private static VolumetricNebulaQualityProfile Make(
        string name,
        int quality,
        FroxelGridDesc main,
        FroxelGridDesc near,
        NebulaVolumeProfile profile,
        VolumetricNebulaPerformanceProfile performance,
        float historyWeight,
        int densityOctaves,
        int detailOctaves,
        int selfShadowSteps,
        int localLightBudget,
        int lightningBudget,
        int blueNoiseFrameModulo)
    {
        var temporal = new VolumetricTemporalProfile(
            Enabled: true,
            HistoryWeight: historyWeight,
            ClampMin: 0.75f,
            ClampMax: 1.35f,
            ResetOnCameraCut: true,
            ResetOnSystemChange: true,
            RequiresMotionVectors: true,
            HistoryValidation: VolumetricHistoryValidation.DepthNormalLighting);
        var jitter = new VolumetricJitterProfile(
            Enabled: true,
            Source: VolumetricJitterSource.SpatiotemporalBlueNoise,
            FrameModulo: blueNoiseFrameModulo,
            Strength: quality <= 1 ? 0.65f : 1.0f,
            DeterministicCaptureMode: true);
        var density = new VolumetricDensityAuthoringProfile(
            Source: VolumetricDensitySource.PerlinWorleyRuntime,
            BaseOctaves: densityOctaves,
            DetailOctaves: detailOctaves,
            Coverage: profile.Coverage,
            BaseNoiseScale: profile.BaseNoiseScale,
            DetailErosion: profile.DetailErosion,
            DomainWarp: profile.DomainWarp,
            CurlStrength: MathF.Min(profile.DomainWarp * 0.8f, 0.7f),
            SupportsOpenVdbImport: true,
            SupportsBlenderProfileImport: true);
        var lighting = new VolumetricLightingProfile(
            PhaseFunction: VolumetricPhaseFunction.DualHenyeyGreenstein,
            GForward: profile.PhaseGForward,
            GBackward: profile.PhaseGBackward,
            PhaseBlend: profile.PhaseBlend,
            PowderFactor: profile.PowderFactor,
            SelfShadowSteps: selfShadowSteps,
            LocalLightBudget: localLightBudget,
            LightningChannelBudget: lightningBudget,
            EngineTrailLightBudget: quality >= 2 ? 4 : 0,
            SunLightInjection: true,
            VolumetricShadowing: true);
        var displacement = new VolumetricDisplacementProfile(
            Enabled: false,
            MaxCapsules: quality >= 2 ? 16 : 6,
            MaxWakeSeconds: quality >= 2 ? 3.0f : 1.75f,
            CurlWake: true,
            SdfCapsuleFields: true,
            ReservedNearFieldOnly: true);
        var materialFog = new VolumetricMaterialFogProfile(
            Enabled: false,
            Ships: true,
            Particles: true,
            Beams: true,
            Transparencies: true,
            PremultipliedTransparentSupport: true);
        var atmosphere = new VolumetricAtmosphereBridgeProfile(
            Enabled: false,
            TransmittanceLut: true,
            MultiScatteringLut: true,
            SkyViewLut: true,
            AerialPerspectiveVolume: true,
            CloudShell: quality >= 2);
        var profileName = performance.AdaptiveApplied ? $"{name}-adaptive" : name;
        return new VolumetricNebulaQualityProfile(
            quality, profileName, main, near, temporal, jitter, density, lighting, displacement, materialFog,
            atmosphere, performance);
    }
}

public readonly record struct VolumetricNebulaPerformanceProfile(
    bool AdaptiveEnabled,
    bool AdaptiveApplied,
    int RequestedQuality,
    int EffectiveQuality,
    float ZoneExtentMeters,
    float ViewMegapixels,
    int MainVoxels,
    int NearVoxels,
    long EstimatedBytes,
    string Reason)
{
    public string DebugSummary => !AdaptiveEnabled
        ? FormattableString.Invariant($"fixed q{RequestedQuality} extent={ZoneExtentMeters / 1000f:0}km")
        : AdaptiveApplied
            ? FormattableString.Invariant(
                $"adaptive q{RequestedQuality}->q{EffectiveQuality} {Reason} extent={ZoneExtentMeters / 1000f:0}km mem={EstimatedBytes / (1024.0 * 1024.0):0.0}MB")
            : FormattableString.Invariant(
                $"adaptive q{RequestedQuality} native extent={ZoneExtentMeters / 1000f:0}km mem={EstimatedBytes / (1024.0 * 1024.0):0.0}MB");

    public static VolumetricNebulaPerformanceProfile Evaluate(
        int renderWidth,
        int renderHeight,
        global::LibreLancer.Render.RenderFeatureSet features,
        NebulaVolumeProfile profile)
    {
        var requested = Math.Clamp(features.VolumetricQuality, 0, 3);
        var effective = requested;
        var extent = MathF.Max(profile.BoundsRadius, MathF.Max(profile.FogRange.X, profile.FogRange.Y));
        var megapixels = MathF.Max(1f, renderWidth * renderHeight / 1_000_000f);
        var reason = "native";
        if (features.VolumetricAdaptiveQuality)
        {
            if (extent >= 360_000f)
            {
                effective = Math.Min(effective, 0);
                reason = "giant-zone";
            }
            else if (extent >= 220_000f)
            {
                effective = Math.Max(0, effective - 2);
                reason = "huge-zone";
            }
            else if (extent >= 120_000f)
            {
                effective = Math.Max(0, effective - 1);
                reason = "large-zone";
            }

            if (megapixels >= 7.5f && extent >= 90_000f && effective > 0)
            {
                effective--;
                reason = reason == "native" ? "4k-budget" : $"{reason}+4k";
            }
        }

        return new VolumetricNebulaPerformanceProfile(
            features.VolumetricAdaptiveQuality,
            effective < requested,
            requested,
            effective,
            extent,
            megapixels,
            0,
            0,
            0,
            reason);
    }

    public VolumetricNebulaPerformanceProfile WithGridBudgets(int mainVoxels, int nearVoxels, long estimatedBytes) =>
        this with
        {
            MainVoxels = mainVoxels,
            NearVoxels = nearVoxels,
            EstimatedBytes = estimatedBytes
        };
}

public readonly record struct VolumetricTemporalProfile(
    bool Enabled,
    float HistoryWeight,
    float ClampMin,
    float ClampMax,
    bool ResetOnCameraCut,
    bool ResetOnSystemChange,
    bool RequiresMotionVectors,
    VolumetricHistoryValidation HistoryValidation);

public readonly record struct VolumetricJitterProfile(
    bool Enabled,
    VolumetricJitterSource Source,
    int FrameModulo,
    float Strength,
    bool DeterministicCaptureMode);

public readonly record struct VolumetricDensityAuthoringProfile(
    VolumetricDensitySource Source,
    int BaseOctaves,
    int DetailOctaves,
    float Coverage,
    float BaseNoiseScale,
    float DetailErosion,
    float DomainWarp,
    float CurlStrength,
    bool SupportsOpenVdbImport,
    bool SupportsBlenderProfileImport);

public readonly record struct VolumetricLightingProfile(
    VolumetricPhaseFunction PhaseFunction,
    float GForward,
    float GBackward,
    float PhaseBlend,
    float PowderFactor,
    int SelfShadowSteps,
    int LocalLightBudget,
    int LightningChannelBudget,
    int EngineTrailLightBudget,
    bool SunLightInjection,
    bool VolumetricShadowing);

public readonly record struct VolumetricDisplacementProfile(
    bool Enabled,
    int MaxCapsules,
    float MaxWakeSeconds,
    bool CurlWake,
    bool SdfCapsuleFields,
    bool ReservedNearFieldOnly);

public readonly record struct VolumetricMaterialFogProfile(
    bool Enabled,
    bool Ships,
    bool Particles,
    bool Beams,
    bool Transparencies,
    bool PremultipliedTransparentSupport);

public readonly record struct VolumetricAtmosphereBridgeProfile(
    bool Enabled,
    bool TransmittanceLut,
    bool MultiScatteringLut,
    bool SkyViewLut,
    bool AerialPerspectiveVolume,
    bool CloudShell);

public enum VolumetricHistoryValidation
{
    Off,
    DepthOnly,
    DepthNormal,
    DepthNormalLighting
}

public enum VolumetricJitterSource
{
    Off,
    BlueNoise2D,
    SpatiotemporalBlueNoise
}

public enum VolumetricDensitySource
{
    LegacyProfileOnly,
    PerlinWorleyRuntime,
    BlenderProfileJson,
    OpenVdbImported
}

public enum VolumetricPhaseFunction
{
    Isotropic,
    HenyeyGreenstein,
    DualHenyeyGreenstein
}

public enum VolumetricNebulaResourceSlot
{
    MainDensity,
    MainLighting,
    MainIntegratedScatteringTransmittance,
    MainHistory,
    MainHistoryValidity,
    NearDensity,
    NearLighting,
    NearIntegratedScatteringTransmittance,
    NearHistory,
    NearHistoryValidity,
    BlueNoise,
    PhaseProfileBuffer,
    LightInjectionBuffer,
    LightningChannelBuffer,
    EngineTrailLightBuffer,
    DisplacementSdfCapsules,
    DisplacementField,
    DisplacementHistory,
    WakeVectorField,
    MaterialFogIntegrated,
    AtmosphereTransmittanceLut,
    AtmosphereMultiScatteringLut,
    AtmosphereSkyViewLut,
    AtmosphereAerialPerspective,
    AtmosphereCloudShell
}

public enum VolumetricNebulaPassSlot
{
    Allocate,
    ClearIdentity,
    InjectDensity,
    InjectLocalLights,
    InjectLightningChannels,
    InjectEngineTrails,
    DisplacementSdf,
    DisplacementHistory,
    WakeCurl,
    SelfShadow,
    Integrate,
    TemporalReproject,
    HistoryClamp,
    Composite,
    MaterialFogExport,
    DebugView,
    AtmosphereBridge
}

public readonly record struct VolumetricNebulaPassDeclaration(
    VolumetricNebulaPassSlot Pass,
    string DebugName,
    IReadOnlyList<VolumetricNebulaResourceSlot> Reads,
    IReadOnlyList<VolumetricNebulaResourceSlot> Writes,
    bool StubInPr52)
{
    public static IReadOnlyList<VolumetricNebulaPassDeclaration> CanonicalOrder { get; } =
    [
        new(VolumetricNebulaPassSlot.Allocate, "vol_nebula_allocate", [],
            [VolumetricNebulaResourceSlot.MainDensity, VolumetricNebulaResourceSlot.MainLighting,
             VolumetricNebulaResourceSlot.MainIntegratedScatteringTransmittance, VolumetricNebulaResourceSlot.MainHistory,
             VolumetricNebulaResourceSlot.NearDensity, VolumetricNebulaResourceSlot.NearLighting,
             VolumetricNebulaResourceSlot.NearIntegratedScatteringTransmittance, VolumetricNebulaResourceSlot.NearHistory,
             VolumetricNebulaResourceSlot.DisplacementField, VolumetricNebulaResourceSlot.DisplacementHistory,
             VolumetricNebulaResourceSlot.WakeVectorField], false),
        new(VolumetricNebulaPassSlot.ClearIdentity, "vol_nebula_clear", [],
            [VolumetricNebulaResourceSlot.MainDensity, VolumetricNebulaResourceSlot.MainLighting,
             VolumetricNebulaResourceSlot.MainIntegratedScatteringTransmittance, VolumetricNebulaResourceSlot.MainHistory,
             VolumetricNebulaResourceSlot.NearDensity, VolumetricNebulaResourceSlot.NearLighting,
             VolumetricNebulaResourceSlot.NearIntegratedScatteringTransmittance, VolumetricNebulaResourceSlot.NearHistory,
             VolumetricNebulaResourceSlot.NearHistoryValidity, VolumetricNebulaResourceSlot.DisplacementField,
             VolumetricNebulaResourceSlot.DisplacementHistory, VolumetricNebulaResourceSlot.WakeVectorField], false),
        new(VolumetricNebulaPassSlot.DisplacementSdf, "vol_nebula_displacement", [VolumetricNebulaResourceSlot.DisplacementSdfCapsules],
            [VolumetricNebulaResourceSlot.DisplacementField], true),
        new(VolumetricNebulaPassSlot.DisplacementHistory, "vol_nebula_displacement_history",
            [VolumetricNebulaResourceSlot.DisplacementField, VolumetricNebulaResourceSlot.DisplacementHistory],
            [VolumetricNebulaResourceSlot.DisplacementHistory], true),
        new(VolumetricNebulaPassSlot.WakeCurl, "vol_nebula_wake_curl",
            [VolumetricNebulaResourceSlot.DisplacementHistory],
            [VolumetricNebulaResourceSlot.WakeVectorField], true),
        new(VolumetricNebulaPassSlot.InjectDensity, "vol_nebula_density",
            [VolumetricNebulaResourceSlot.BlueNoise, VolumetricNebulaResourceSlot.DisplacementField,
             VolumetricNebulaResourceSlot.DisplacementHistory, VolumetricNebulaResourceSlot.WakeVectorField],
            [VolumetricNebulaResourceSlot.MainDensity, VolumetricNebulaResourceSlot.NearDensity], true),
        new(VolumetricNebulaPassSlot.InjectLocalLights, "vol_nebula_light", [VolumetricNebulaResourceSlot.LightInjectionBuffer],
            [VolumetricNebulaResourceSlot.MainLighting], true),
        new(VolumetricNebulaPassSlot.InjectLightningChannels, "vol_nebula_lightning_channels", [VolumetricNebulaResourceSlot.LightningChannelBuffer],
            [VolumetricNebulaResourceSlot.MainLighting], true),
        new(VolumetricNebulaPassSlot.InjectEngineTrails, "vol_nebula_engine_trails", [VolumetricNebulaResourceSlot.EngineTrailLightBuffer],
            [VolumetricNebulaResourceSlot.MainLighting], true),
        new(VolumetricNebulaPassSlot.SelfShadow, "vol_nebula_self_shadow", [VolumetricNebulaResourceSlot.MainDensity],
            [VolumetricNebulaResourceSlot.MainLighting], true),
        new(VolumetricNebulaPassSlot.Integrate, "vol_nebula_integrate", [VolumetricNebulaResourceSlot.MainDensity, VolumetricNebulaResourceSlot.MainLighting],
            [VolumetricNebulaResourceSlot.MainIntegratedScatteringTransmittance,
             VolumetricNebulaResourceSlot.NearIntegratedScatteringTransmittance], true),
        new(VolumetricNebulaPassSlot.TemporalReproject, "vol_nebula_reproject", [VolumetricNebulaResourceSlot.MainIntegratedScatteringTransmittance, VolumetricNebulaResourceSlot.MainHistory],
            [VolumetricNebulaResourceSlot.MainHistoryValidity], true),
        new(VolumetricNebulaPassSlot.HistoryClamp, "vol_nebula_history_clamp", [VolumetricNebulaResourceSlot.MainHistoryValidity],
            [VolumetricNebulaResourceSlot.MainHistory], true),
        new(VolumetricNebulaPassSlot.Composite, "vol_nebula_composite", [VolumetricNebulaResourceSlot.MainIntegratedScatteringTransmittance], [], true),
        new(VolumetricNebulaPassSlot.MaterialFogExport, "vol_nebula_material_fog", [VolumetricNebulaResourceSlot.MainIntegratedScatteringTransmittance],
            [VolumetricNebulaResourceSlot.MaterialFogIntegrated], true),
        new(VolumetricNebulaPassSlot.DebugView, "vol_nebula_debug", [VolumetricNebulaResourceSlot.MainDensity,
             VolumetricNebulaResourceSlot.MainIntegratedScatteringTransmittance, VolumetricNebulaResourceSlot.NearDensity,
             VolumetricNebulaResourceSlot.NearIntegratedScatteringTransmittance,
             VolumetricNebulaResourceSlot.DisplacementField, VolumetricNebulaResourceSlot.DisplacementHistory,
             VolumetricNebulaResourceSlot.WakeVectorField], [], true),
        new(VolumetricNebulaPassSlot.AtmosphereBridge, "vol_atmosphere_bridge", [VolumetricNebulaResourceSlot.AtmosphereTransmittanceLut, VolumetricNebulaResourceSlot.AtmosphereAerialPerspective], [], true)
    ];
}
