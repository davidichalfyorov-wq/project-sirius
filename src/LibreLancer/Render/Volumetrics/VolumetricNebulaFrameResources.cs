using System;
using System.Numerics;
using LibreLancer.Graphics;
using LibreLancer.Shaders;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// PR-5.2 froxel resource owner. This allocates and clears the textures that later
/// density, lighting, integration and temporal reprojection passes will consume.
/// It deliberately does not bind the textures into materials yet, preserving the
/// legacy nebula path until a real composite pass exists.
/// </summary>
public sealed class VolumetricNebulaFrameResources : IDisposable
{
    private const ushort HalfZero = 0x0000;
    private const ushort HalfOne = 0x3C00;
    private const int DisplacementVolumeSize = 64;

    private VolumetricNebulaQualityProfile qualityProfile;
    private FroxelGridDesc mainDesc;
    private FroxelGridDesc nearDesc;
    private int allocatedQuality = -1;
    private int generation;
    private string activeProfile = string.Empty;
    private bool disposed;
    private readonly VolumetricLightningChannelState lightningChannels = new();
    private readonly VolumetricTemporalState temporalState = new();
    private VolumetricBlueNoiseResources? blueNoise;
    private VolumetricImportedDensityFrame importedDensity;
    private Texture3D? importedDensityTexture;
    private string importedDensityTextureKey = string.Empty;
    private Texture2D? fallbackJitterTexture;
    private Texture3D? fallbackDisplacementTexture;

    public Texture3D? Density { get; private set; }
    public Texture3D? Lighting { get; private set; }
    public Texture3D? Integrated { get; private set; }
    public Texture3D? History { get; private set; }
    public Texture3D? HistoryPrevious { get; private set; }
    public Texture3D? HistoryConfidence { get; private set; }
    public Texture3D? NearDensity { get; private set; }
    public Texture3D? NearLighting { get; private set; }
    public Texture3D? NearIntegrated { get; private set; }
    public Texture3D? NearHistory { get; private set; }
    public Texture3D? NearHistoryConfidence { get; private set; }
    public Texture3D? ShipDisplacement { get; private set; }
    public Texture3D? ShipDisplacementHistory { get; private set; }
    public Texture3D? ShipDisplacementHistoryPrevious { get; private set; }
    public Texture3D? WakeVectorField { get; private set; }
    private Texture2D? fallbackDepthTexture;

    public VolumetricNebulaQualityProfile QualityProfile => qualityProfile;
    public FroxelGridDesc MainDesc => mainDesc;
    public FroxelGridDesc NearDesc => nearDesc;
    public bool Allocated => Density != null && Lighting != null && Integrated != null &&
                             History != null && HistoryPrevious != null && HistoryConfidence != null;
    public bool NearAllocated => NearDensity != null && NearLighting != null &&
                                 NearIntegrated != null && NearHistory != null && NearHistoryConfidence != null;
    public bool DisplacementAllocated => ShipDisplacement != null;
    public bool DisplacementHistoryAllocated => ShipDisplacementHistory != null && ShipDisplacementHistoryPrevious != null;
    public bool WakeVectorAllocated => WakeVectorField != null;
    public long EstimatedBytes => Allocated
        ? mainDesc.BytesPerVolume() * 6 +
          (NearAllocated ? nearDesc.BytesPerVolume() * 5 : 0) +
          (ShipDisplacement != null ? TextureBytes(ShipDisplacement) : 0) +
          (DisplacementHistoryAllocated ? TextureBytes(ShipDisplacementHistory!) * 2 : 0) +
          (WakeVectorField != null ? TextureBytes(WakeVectorField) : 0)
        : 0;
    public string ActiveProfile => activeProfile;
    public bool GpuIdentityClearedThisFrame { get; private set; }
    public bool GpuDensityInjectedThisFrame { get; private set; }
    public bool GpuLightingInjectedThisFrame { get; private set; }
    public bool GpuLightningInjectedThisFrame { get; private set; }
    public bool GpuIntegratedThisFrame { get; private set; }
    public bool TemporalAppliedThisFrame { get; private set; }
    public bool TemporalResetThisFrame { get; private set; }
    public Vector2 TemporalJitter { get; private set; }
    public int TemporalFrameIndex { get; private set; }
    public float TemporalHistoryConfidence { get; private set; }
    public bool ReprojectionAppliedThisFrame { get; private set; }
    public bool CompositeAppliedThisFrame { get; private set; }
    public bool DepthAwareCompositeThisFrame { get; private set; }
    public bool NearCompositeAppliedThisFrame { get; private set; }
    public bool NearDetailTunedThisFrame { get; private set; }
    public string NearDetailSummary { get; private set; } = string.Empty;
    public bool DisplacementUpdatedThisFrame { get; private set; }
    public int DisplacementCapsuleCount { get; private set; }
    public string DisplacementDebugSummary { get; private set; } = string.Empty;
    public bool WakeHistoryUpdatedThisFrame { get; private set; }
    public string WakeHistoryDebugSummary { get; private set; } = string.Empty;
    public bool WakeCurlUpdatedThisFrame { get; private set; }
    public string WakeCurlDebugSummary { get; private set; } = string.Empty;
    public bool MaterialFogBoundThisFrame { get; private set; }
    public string MaterialFogDebugSummary { get; private set; } = string.Empty;
    public string LightningDebugSummary { get; private set; } = string.Empty;
    public bool BlueNoiseBoundThisFrame { get; private set; }
    public string BlueNoiseSourceName { get; private set; } = "off";
    public bool ImportedDensityReady => importedDensity.Valid;
    public string ImportedDensitySummary => importedDensity.Valid ? importedDensity.DebugSummary : "off";
    public bool ImportedDensityTextureReady => importedDensityTexture != null;

    public static VolumetricNebulaResourceDebug LastDebug { get; private set; } =
        VolumetricNebulaResourceDebug.Disabled("not initialized");
    public static string LastBlueNoiseSource { get; private set; } = "off";
    public static string LastImportedDensitySource { get; private set; } = "off";
    private float lastDisplacementHistoryTime;
    private Vector3 lastWakeVelocity;

    public static void NoteGodRays(bool applied, string summary)
    {
        LastDebug = LastDebug with
        {
            GodRaysApplied = applied,
            GodRaySummary = summary
        };
    }

    public void Ensure(
        RenderContext rstate,
        int renderWidth,
        int renderHeight,
        global::LibreLancer.Render.RenderFeatureSet features,
        NebulaVolumeProfile? profile,
        float totalTimeSeconds = 0f,
        Vector3 sunDirection = default,
        VolumetricShipDisplacementFrame displacementFrame = default)
    {
        ThrowIfDisposed();

        if (!features.VolumetricNebula)
        {
            DisposeTextures();
            ClearImportedDensity("off");
            LastDebug = VolumetricNebulaResourceDebug.Disabled("volumetric_nebula disabled");
            return;
        }

        if (!rstate.HasFeature(GraphicsFeature.Compute))
        {
            DisposeTextures();
            ClearImportedDensity("backend has no compute feature");
            LastDebug = VolumetricNebulaResourceDebug.Disabled("backend has no compute feature");
            return;
        }

        if (profile is not { IsValid: true } active)
        {
            DisposeTextures();
            ClearImportedDensity("waiting for active nebula profile");
            LastDebug = VolumetricNebulaResourceDebug.Waiting("waiting for active nebula profile");
            return;
        }
        if (importedDensity.Valid && !importedDensity.MatchesProfile(active))
        {
            ClearImportedDensity("profile mismatch");
        }
        LastImportedDensitySource = importedDensity.Valid ? importedDensity.DebugSummary : "off";

        var desiredProfile = VolumetricNebulaQualityProfile.Create(renderWidth, renderHeight, features, active);
        var desired = desiredProfile.MainGrid;
        var desiredNear = desiredProfile.NearGrid;
        var wantsNear = features.VolumetricNearCascade;
        var wantsDisplacement = wantsNear && features.VolumetricShipDisplacement;
        var wantsWakeHistory = wantsDisplacement && features.VolumetricWakeHistory;
        var wantsWakeCurl = wantsWakeHistory && features.VolumetricWakeCurl;
        var needsAllocate = !Allocated ||
                            desired != mainDesc ||
                            allocatedQuality != desired.Quality ||
                            wantsNear != NearAllocated ||
                            (wantsNear && desiredNear != nearDesc) ||
                            wantsDisplacement != DisplacementAllocated ||
                            wantsWakeHistory != DisplacementHistoryAllocated ||
                            wantsWakeCurl != WakeVectorAllocated;

        if (needsAllocate)
        {
            rstate.BeginPassTimer("vol_nebula_allocate");
            try
            {
                DisposeTextures();
                qualityProfile = desiredProfile;
                mainDesc = desired;
                nearDesc = desiredNear;
                allocatedQuality = desired.Quality;
                activeProfile = active.Nickname;

                Density = CreateVolume(rstate, "density", desired);
                Lighting = CreateVolume(rstate, "lighting", desired);
                Integrated = CreateVolume(rstate, "integrated", desired);
                History = CreateVolume(rstate, "history", desired);
                HistoryPrevious = CreateVolume(rstate, "history.previous", desired);
                HistoryConfidence = CreateVolume(rstate, "history.confidence", desired);
                if (wantsNear)
                {
                    NearDensity = CreateVolume(rstate, "near.density", desiredNear);
                    NearLighting = CreateVolume(rstate, "near.lighting", desiredNear);
                    NearIntegrated = CreateVolume(rstate, "near.integrated", desiredNear);
                    NearHistory = CreateVolume(rstate, "near.history", desiredNear);
                    NearHistoryConfidence = CreateVolume(rstate, "near.history.confidence", desiredNear);
                }
                if (wantsDisplacement)
                {
                    ShipDisplacement = CreateDisplacementVolume(rstate);
                    if (wantsWakeHistory)
                    {
                        ShipDisplacementHistory = CreateDisplacementVolume(rstate);
                        ShipDisplacementHistoryPrevious = CreateDisplacementVolume(rstate);
                    }
                    if (wantsWakeCurl)
                    {
                        WakeVectorField = CreateDisplacementVolume(rstate);
                    }
                }
                generation++;
                UploadIdentity(HistoryPrevious);
                UploadIdentity(NearHistory);
                ClearVolume(ShipDisplacement);
                ClearVolume(ShipDisplacementHistory);
                ClearVolume(ShipDisplacementHistoryPrevious);
                ClearVolume(WakeVectorField);
                lastDisplacementHistoryTime = 0f;
                lastWakeVelocity = Vector3.Zero;
            }
            finally
            {
                rstate.EndPassTimer();
            }
        }
        else
        {
            qualityProfile = desiredProfile;
            nearDesc = desiredNear;
            activeProfile = active.Nickname;
        }

        rstate.BeginPassTimer("vol_nebula_clear");
        CompositeAppliedThisFrame = false;
        DepthAwareCompositeThisFrame = false;
        NearCompositeAppliedThisFrame = false;
        NearDetailTunedThisFrame = false;
        NearDetailSummary = string.Empty;
        DisplacementUpdatedThisFrame = false;
        DisplacementCapsuleCount = 0;
        DisplacementDebugSummary = string.Empty;
        WakeHistoryUpdatedThisFrame = false;
        WakeHistoryDebugSummary = string.Empty;
        WakeCurlUpdatedThisFrame = false;
        WakeCurlDebugSummary = string.Empty;
        MaterialFogBoundThisFrame = false;
        MaterialFogDebugSummary = features.VolumetricMaterialFog ? "waiting transparent pass" : "off";
        GpuLightningInjectedThisFrame = false;
        LightningDebugSummary = string.Empty;
        BlueNoiseBoundThisFrame = false;
        BlueNoiseSourceName = features.VolumetricBlueNoise ? "requested" : "off";
        LastBlueNoiseSource = BlueNoiseSourceName;
        TemporalAppliedThisFrame = false;
        TemporalResetThisFrame = false;
        TemporalJitter = Vector2.Zero;
        TemporalHistoryConfidence = 0f;
        ReprojectionAppliedThisFrame = false;
        var gpuCleared = false;
        try
        {
            gpuCleared = ClearIdentityGpu(rstate);
            if (!gpuCleared && needsAllocate)
            {
                ClearIdentityUploads();
            }
        }
        finally
        {
            rstate.EndPassTimer();
        }

        var displacementUpdated = false;
        if (features.VolumetricShipDisplacement)
        {
            rstate.BeginPassTimer("vol_nebula_displacement");
            try
            {
                displacementUpdated = UpdateShipDisplacementGpu(rstate, active, features, displacementFrame,
                    totalTimeSeconds);
            }
            finally
            {
                rstate.EndPassTimer();
            }
        }

        var wakeHistoryUpdated = false;
        if (features.VolumetricWakeHistory)
        {
            rstate.BeginPassTimer("vol_nebula_displacement_history");
            try
            {
                wakeHistoryUpdated = UpdateShipDisplacementHistoryGpu(rstate, features, totalTimeSeconds);
            }
            finally
            {
                rstate.EndPassTimer();
            }
        }

        var wakeCurlUpdated = false;
        if (features.VolumetricWakeCurl)
        {
            rstate.BeginPassTimer("vol_nebula_wake_curl");
            try
            {
                wakeCurlUpdated = UpdateWakeCurlGpu(rstate, features, totalTimeSeconds);
            }
            finally
            {
                rstate.EndPassTimer();
            }
        }

        var densityInjected = false;
        rstate.BeginPassTimer("vol_nebula_density");
        try
        {
            densityInjected = InjectDensityGpu(rstate, active, features, totalTimeSeconds);
        }
        finally
        {
            rstate.EndPassTimer();
        }

        var lightingInjected = false;
        rstate.BeginPassTimer("vol_nebula_light");
        try
        {
            lightingInjected = InjectLightingGpu(rstate, active, sunDirection, totalTimeSeconds);
        }
        finally
        {
            rstate.EndPassTimer();
        }

        var lightningInjected = false;
        if (features.VolumetricLightningChannels)
        {
            rstate.BeginPassTimer("vol_nebula_lightning_channels");
            try
            {
                lightningInjected = InjectLightningChannelsGpu(rstate, active, features, totalTimeSeconds);
            }
            finally
            {
                rstate.EndPassTimer();
            }
        }

        var integrated = false;
        rstate.BeginPassTimer("vol_nebula_integrate");
        try
        {
            integrated = IntegrateScatteringGpu(rstate, active);
        }
        finally
        {
            rstate.EndPassTimer();
        }

        var operation = integrated
            ? lightningInjected
                ? (needsAllocate ? "allocated + GPU lightning integrate" : "GPU lightning integrate")
                : (needsAllocate ? "allocated + GPU light integrate" : "GPU light integrate")
            : lightingInjected
                ? (needsAllocate ? "allocated + GPU light inject" : "GPU light inject")
            : densityInjected
                ? (needsAllocate ? "allocated + GPU density inject" : "GPU density inject")
            : gpuCleared
                ? (needsAllocate ? "allocated + GPU identity clear" : "GPU identity clear")
                : (needsAllocate ? "allocated + identity upload fallback" : "identity clear marker");
        if (displacementUpdated)
        {
            operation += " + displacement";
        }
        if (wakeHistoryUpdated)
        {
            operation += " + wake history";
        }
        if (wakeCurlUpdated)
        {
            operation += " + wake curl";
        }
        if (importedDensity.Valid)
        {
            operation += ImportedDensityTextureReady ? " + openvdb sampled" : " + openvdb ready";
        }

        LastDebug = new VolumetricNebulaResourceDebug(
            true,
            mainDesc.Dimensions,
            nearDesc.Dimensions,
            qualityProfile.Name,
            mainDesc.Quality,
            qualityProfile.Performance.DebugSummary,
            activeProfile,
            EstimatedBytes,
            operation,
            mainDesc.DebugName,
            VolumetricNebulaPassDeclaration.CanonicalOrder.Count,
            NearDetailTunedThisFrame,
            NearDetailSummary,
            DisplacementUpdatedThisFrame,
            DisplacementCapsuleCount,
            DisplacementDebugSummary,
            WakeHistoryUpdatedThisFrame,
            WakeHistoryDebugSummary,
            WakeCurlUpdatedThisFrame,
            WakeCurlDebugSummary,
            LastDebug.GodRaysApplied,
            LastDebug.GodRaySummary,
            MaterialFogBoundThisFrame,
            MaterialFogDebugSummary,
            GpuLightningInjectedThisFrame,
            LightningDebugSummary);
    }

    public bool SetImportedDensity(VolumetricImportedDensityFrame densityFrame, NebulaVolumeProfile activeProfile,
        string canonicalSystem = "")
    {
        if (!densityFrame.Valid || !densityFrame.MatchesProfile(activeProfile, canonicalSystem))
        {
            ClearImportedDensity(densityFrame.Valid ? "profile mismatch" : densityFrame.Error);
            return false;
        }

        var nextKey = ImportedDensityTextureKey(densityFrame.Descriptor);
        if (!string.Equals(importedDensityTextureKey, nextKey, StringComparison.Ordinal))
        {
            DisposeImportedDensityTexture();
        }
        importedDensity = densityFrame;
        LastImportedDensitySource = densityFrame.DebugSummary;
        return true;
    }

    public void ClearImportedDensity(string reason = "off")
    {
        importedDensity = VolumetricImportedDensityFrame.Invalid(reason);
        DisposeImportedDensityTexture();
        LastImportedDensitySource = "off";
    }

    public bool DrawDebugView(RenderContext rstate, RenderDebugView debugView, int renderWidth, int renderHeight)
    {
        if (debugView == RenderDebugView.VolumetricJitter)
        {
            var texture = BindJitterTexture(rstate, enabled: true, out _);
            var jitterWidth = Math.Max(96, Math.Min(256, Math.Max(renderWidth - 32, 1) / 5));
            var list = rstate.Renderer2D.CreateDrawList();
            list.DrawImageStretched(texture, new Rectangle(16, 16, jitterWidth, jitterWidth), Color4.White);
            list.Render();
            return true;
        }

        var mode = DebugModeForView(debugView);
        if (mode == 0 || !Allocated || AllShaders.FroxelDebugSlice == null)
        {
            return false;
        }

        var shader = AllShaders.FroxelDebugSlice.Get(0);
        var nearDebug = debugView is RenderDebugView.VolumetricNear or RenderDebugView.VolumetricNearDensity;
        if (nearDebug && !NearAllocated)
        {
            return false;
        }
        if (debugView == RenderDebugView.VolumetricDisplacement && ShipDisplacement == null)
        {
            return false;
        }
        if (debugView == RenderDebugView.VolumetricDisplacementHistory &&
            ShipDisplacementHistoryPrevious == null && ShipDisplacement == null)
        {
            return false;
        }
        if (debugView == RenderDebugView.VolumetricWakeVectors &&
            WakeVectorField == null && ShipDisplacementHistoryPrevious == null && ShipDisplacement == null)
        {
            return false;
        }
        Texture3D? importedDebugSource = null;
        if (debugView == RenderDebugView.VolumetricOpenVdb)
        {
            if (!importedDensity.Valid)
            {
                return false;
            }
            importedDebugSource = BindImportedDensityTexture(rstate, out var importedActive);
            if (!importedActive)
            {
                return false;
            }
        }
        var debugSource = debugView switch
        {
            RenderDebugView.VolumetricLightning => Lighting,
            RenderDebugView.VolumetricLightningMask => Lighting,
            RenderDebugView.VolumetricHistory => TemporalAppliedThisFrame ? HistoryPrevious : History,
            RenderDebugView.VolumetricHistoryConfidence => HistoryConfidence,
            RenderDebugView.VolumetricDisplacement => ShipDisplacement,
            RenderDebugView.VolumetricDisplacementHistory => ShipDisplacementHistoryPrevious ?? ShipDisplacement,
            RenderDebugView.VolumetricWakeVectors => WakeVectorField ?? ShipDisplacementHistoryPrevious ?? ShipDisplacement,
            RenderDebugView.VolumetricNearDensity => NearDensity,
            RenderDebugView.VolumetricNear => NearDensity,
            RenderDebugView.VolumetricOpenVdb => importedDebugSource,
            _ => Density
        };
        var integratedSource = debugView == RenderDebugView.VolumetricNear && NearIntegrated != null
            ? NearIntegrated
            : Integrated;
        var debugGrid = nearDebug && nearDesc.IsValid ? nearDesc : mainDesc;
        var oldTexture0 = rstate.Textures[0];
        var oldSampler0 = rstate.Samplers[0];
        var oldTexture1 = rstate.Textures[1];
        var oldSampler1 = rstate.Samplers[1];
        rstate.Textures[0] = debugSource;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.Textures[1] = integratedSource;
        rstate.Samplers[1] = SamplerState.LinearClamp;
        var sliceParams = new FroxelDebugSliceParams
        {
            SliceParams = new Vector4(0.5f, mode == 2 ? 1f : 4f, mode, 1f),
            GridParams = new Vector4(debugGrid.Width, debugGrid.Height, debugGrid.Depth, generation)
        };
        shader.SetUniformBlock(3, ref sliceParams);

        var oldCull = rstate.Cull;
        var oldDepth = rstate.DepthEnabled;
        var oldBlend = rstate.BlendMode;
        var width = Math.Max(96, Math.Min(512, Math.Max(renderWidth - 32, 1) / 3));
        var height = Math.Max(72, Math.Min(288, width * 9 / 16));
        try
        {
            rstate.Cull = false;
            rstate.DepthEnabled = false;
            rstate.BlendMode = BlendMode.Opaque;
            rstate.PushViewport(16, 16, width, height);
            rstate.Shader = shader;
            rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
            rstate.PopViewport();
        }
        finally
        {
            rstate.Cull = oldCull;
            rstate.DepthEnabled = oldDepth;
            rstate.BlendMode = oldBlend;
            rstate.Textures[0] = oldTexture0;
            rstate.Samplers[0] = oldSampler0;
            rstate.Textures[1] = oldTexture1;
            rstate.Samplers[1] = oldSampler1;
        }
        return true;
    }

    internal bool ApplyTemporal(RenderContext rstate, global::LibreLancer.Render.RenderFeatureSet features,
        NebulaVolumeProfile profile, global::LibreLancer.ICamera camera, Texture2D? sceneDepth = null)
    {
        TemporalAppliedThisFrame = false;
        TemporalResetThisFrame = false;
        TemporalJitter = Vector2.Zero;
        TemporalHistoryConfidence = 0f;
        ReprojectionAppliedThisFrame = false;
        if (!features.VolumetricTemporal || !GpuIntegratedThisFrame || AllShaders.FroxelTemporal == null ||
            Integrated == null || History == null || HistoryPrevious == null || HistoryConfidence == null)
        {
            return false;
        }

        var temporal = temporalState.BeginFrame(camera, enabled: true, features.VolumetricQuality, profile.Nickname);
        TemporalResetThisFrame = temporal.Reset;
        TemporalJitter = temporal.Jitter;
        TemporalFrameIndex = temporal.FrameIndex;
        var useReprojection = features.VolumetricReprojection && sceneDepth != null && !temporal.Reset;
        ReprojectionAppliedThisFrame = useReprojection;
        TemporalHistoryConfidence = temporal.Reset ? 1f : (useReprojection ? 0.85f : 0.65f);
        if (sceneDepth == null)
        {
            fallbackDepthTexture ??= CreateFallbackDepthTexture(rstate);
            sceneDepth = fallbackDepthTexture;
        }

        rstate.BeginPassTimer("vol_nebula_reproject");
        try
        {
            var shader = AllShaders.FroxelTemporal.Get(0);
            var jitterTexture = BindJitterTexture(rstate, features.VolumetricBlueNoise, out var jitterActive);
            if (!Matrix4x4.Invert(camera.ViewProjection, out var inverseViewProjection))
            {
                inverseViewProjection = Matrix4x4.Identity;
            }
            var uniforms = new FroxelTemporalParams
            {
                GridSize = new Vector4(mainDesc.Width, mainDesc.Height, mainDesc.Depth, temporal.FrameIndex),
                TemporalParams = new Vector4(temporal.HistoryWeight, temporal.ClampSigma, temporal.Reset ? 1f : 0f, 0f),
                JitterParams = new Vector4(temporal.Jitter.X, temporal.Jitter.Y,
                    features.VolumetricQuality, jitterActive ? 1f : 0f),
                ReprojectionParams = new Vector4(useReprojection ? 1f : 0f, temporal.DepthToleranceMeters,
                    mainDesc.NearPlane, mainDesc.FarPlane),
                RejectParams = new Vector4(temporal.ReprojectionRejectStrength,
                    1f / MathF.Max(mainDesc.FarPlane - mainDesc.NearPlane, 1f), 0f, 0f),
                PreviousCameraPosition = new Vector4(temporal.PreviousCameraPosition, 1f),
                CurrentCameraPosition = new Vector4(temporal.CurrentCameraPosition, 1f),
                PreviousViewProjection = temporal.PreviousViewProjection,
                CurrentInverseViewProjection = inverseViewProjection
            };
            shader.SetUniformBlock(3, ref uniforms);
            rstate.Textures[0] = Integrated;
            rstate.Samplers[0] = SamplerState.LinearClamp;
            rstate.Textures[1] = HistoryPrevious;
            rstate.Samplers[1] = SamplerState.LinearClamp;
            rstate.Textures[2] = sceneDepth;
            rstate.Samplers[2] = SamplerState.PointClamp;
            rstate.Textures[3] = jitterTexture;
            rstate.Samplers[3] = SamplerState.PointClamp;
            rstate.SetStorageImage(4, History);
            rstate.SetStorageImage(5, HistoryConfidence);
            rstate.Shader = shader;
            rstate.DispatchCompute(GroupCount(mainDesc.Width), GroupCount(mainDesc.Height), GroupCount(mainDesc.Depth));
            rstate.BarrierComputeToGraphics();
            rstate.Textures[0] = null;
            rstate.Textures[1] = null;
            rstate.Textures[2] = null;
            rstate.Textures[3] = null;
            rstate.SetStorageImage(4, null);
            rstate.SetStorageImage(5, null);
            (History, HistoryPrevious) = (HistoryPrevious, History);
            TemporalAppliedThisFrame = true;
        }
        finally
        {
            rstate.EndPassTimer();
        }

        return TemporalAppliedThisFrame;
    }

    private bool UpdateShipDisplacementGpu(RenderContext rstate, NebulaVolumeProfile profile,
        global::LibreLancer.Render.RenderFeatureSet features,
        VolumetricShipDisplacementFrame frame,
        float totalTimeSeconds)
    {
        DisplacementUpdatedThisFrame = false;
        DisplacementCapsuleCount = 0;
        DisplacementDebugSummary = "off";
        if (!features.VolumetricShipDisplacement || ShipDisplacement == null)
        {
            return false;
        }
        if (AllShaders.FroxelDisplacement == null)
        {
            DisplacementDebugSummary = "shader missing";
            return false;
        }

        var capsuleCount = Math.Min(frame.Count, VolumetricShipDisplacementState.MaxCapsules);
        DisplacementCapsuleCount = capsuleCount;
        var a = new Vector4[VolumetricShipDisplacementState.MaxCapsules];
        var b = new Vector4[VolumetricShipDisplacementState.MaxCapsules];
        var v = new Vector4[VolumetricShipDisplacementState.MaxCapsules];
        var swirl = new Vector4[VolumetricShipDisplacementState.MaxCapsules];
        for (var i = 0; i < capsuleCount; i++)
        {
            var capsule = frame[i];
            a[i] = capsule.PackedA;
            b[i] = capsule.PackedB;
            v[i] = capsule.PackedVelocity;
            swirl[i] = capsule.PackedSwirl;
        }

        var shader = AllShaders.FroxelDisplacement.Get(0);
        var uniforms = new FroxelDisplacementParams
        {
            GridSize = new Vector4(ShipDisplacement.Width, ShipDisplacement.Height, ShipDisplacement.Depth,
                capsuleCount),
            GlobalParams = new Vector4(MathF.Max(nearDesc.FarPlane, 1f),
                Math.Clamp(profile.DisplacementStrength, 0f, 2f), totalTimeSeconds, 0f),
            CapsuleA0 = Pack4(a, 0),
            CapsuleA1 = Pack4(a, 4),
            CapsuleA2 = Pack4(a, 8),
            CapsuleA3 = Pack4(a, 12),
            CapsuleB0 = Pack4(b, 0),
            CapsuleB1 = Pack4(b, 4),
            CapsuleB2 = Pack4(b, 8),
            CapsuleB3 = Pack4(b, 12),
            CapsuleVelocity0 = Pack4(v, 0),
            CapsuleVelocity1 = Pack4(v, 4),
            CapsuleVelocity2 = Pack4(v, 8),
            CapsuleVelocity3 = Pack4(v, 12),
            CapsuleSwirl0 = Pack4(swirl, 0),
            CapsuleSwirl1 = Pack4(swirl, 4),
            CapsuleSwirl2 = Pack4(swirl, 8),
            CapsuleSwirl3 = Pack4(swirl, 12)
        };
        shader.SetUniformBlock(3, ref uniforms);
        rstate.SetStorageImage(5, ShipDisplacement);
        rstate.Shader = shader;
        rstate.DispatchCompute(GroupCount(ShipDisplacement.Width), GroupCount(ShipDisplacement.Height),
            GroupCount(ShipDisplacement.Depth));
        rstate.BarrierComputeToCompute();
        rstate.SetStorageImage(5, null);

        DisplacementUpdatedThisFrame = capsuleCount > 0;
        if (DisplacementUpdatedThisFrame)
        {
            lastWakeVelocity = frame.AverageVelocity;
        }
        DisplacementDebugSummary = frame.HasCapsules ? frame.DebugSummary : "caps=0";
        return true;
    }

    private bool UpdateShipDisplacementHistoryGpu(RenderContext rstate,
        global::LibreLancer.Render.RenderFeatureSet features,
        float totalTimeSeconds)
    {
        WakeHistoryUpdatedThisFrame = false;
        WakeHistoryDebugSummary = "off";
        if (!features.VolumetricWakeHistory || ShipDisplacement == null ||
            ShipDisplacementHistory == null || ShipDisplacementHistoryPrevious == null)
        {
            return false;
        }
        if (AllShaders.FroxelDisplacementHistory == null)
        {
            WakeHistoryDebugSummary = "shader missing";
            return false;
        }

        var dt = lastDisplacementHistoryTime > 0f
            ? Math.Clamp(totalTimeSeconds - lastDisplacementHistoryTime, 1f / 240f, 0.25f)
            : 1f / 60f;
        lastDisplacementHistoryTime = totalTimeSeconds;
        var historyProfile = VolumetricWakeHistoryProfile.ForQuality(features.VolumetricQuality, dt, true);
        var shader = AllShaders.FroxelDisplacementHistory.Get(0);
        var uniforms = new FroxelDisplacementHistoryParams
        {
            GridSize = new Vector4(ShipDisplacementHistory.Width, ShipDisplacementHistory.Height,
                ShipDisplacementHistory.Depth, 0f),
            HistoryParams = historyProfile.ShaderParams,
            HistoryParams2 = historyProfile.ShaderParams2
        };
        shader.SetUniformBlock(3, ref uniforms);
        rstate.Textures[0] = ShipDisplacement;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.Textures[1] = ShipDisplacementHistoryPrevious;
        rstate.Samplers[1] = SamplerState.LinearClamp;
        rstate.SetStorageImage(6, ShipDisplacementHistory);
        rstate.Shader = shader;
        rstate.DispatchCompute(GroupCount(ShipDisplacementHistory.Width),
            GroupCount(ShipDisplacementHistory.Height), GroupCount(ShipDisplacementHistory.Depth));
        rstate.BarrierComputeToCompute();
        rstate.Textures[0] = null;
        rstate.Textures[1] = null;
        rstate.SetStorageImage(6, null);
        (ShipDisplacementHistory, ShipDisplacementHistoryPrevious) =
            (ShipDisplacementHistoryPrevious, ShipDisplacementHistory);

        WakeHistoryUpdatedThisFrame = true;
        WakeHistoryDebugSummary = historyProfile.DebugSummary;
        return true;
    }

    private bool UpdateWakeCurlGpu(RenderContext rstate,
        global::LibreLancer.Render.RenderFeatureSet features,
        float totalTimeSeconds)
    {
        WakeCurlUpdatedThisFrame = false;
        WakeCurlDebugSummary = "off";
        if (!features.VolumetricWakeCurl || WakeVectorField == null)
        {
            return false;
        }
        var source = ShipDisplacementHistoryPrevious ?? ShipDisplacement;
        if (!WakeHistoryUpdatedThisFrame || source == null)
        {
            WakeCurlDebugSummary = "waiting";
            return false;
        }
        if (AllShaders.FroxelWakeCurl == null)
        {
            WakeCurlDebugSummary = "shader missing";
            return false;
        }

        var profile = VolumetricWakeCurlProfile.ForQuality(features.VolumetricQuality, true, lastWakeVelocity);
        var shader = AllShaders.FroxelWakeCurl.Get(0);
        var uniforms = new FroxelWakeCurlParams
        {
            GridSize = new Vector4(WakeVectorField.Width, WakeVectorField.Height, WakeVectorField.Depth,
                totalTimeSeconds),
            CurlParams = profile.ShaderParams,
            CurlParams2 = profile.ShaderParams2
        };
        shader.SetUniformBlock(3, ref uniforms);
        rstate.Textures[0] = source;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.SetStorageImage(7, WakeVectorField);
        rstate.Shader = shader;
        rstate.DispatchCompute(GroupCount(WakeVectorField.Width), GroupCount(WakeVectorField.Height),
            GroupCount(WakeVectorField.Depth));
        rstate.BarrierComputeToCompute();
        rstate.Textures[0] = null;
        rstate.SetStorageImage(7, null);

        WakeCurlUpdatedThisFrame = true;
        WakeCurlDebugSummary = profile.DebugSummary;
        return true;
    }

    private bool InjectLightningChannelsGpu(RenderContext rstate, NebulaVolumeProfile profile,
        global::LibreLancer.Render.RenderFeatureSet features, float totalTimeSeconds)
    {
        GpuLightningInjectedThisFrame = false;
        LightningDebugSummary = string.Empty;
        if (!features.VolumetricLightningChannels)
        {
            return false;
        }

        var frame = lightningChannels.BuildFrame(profile, features,
            VolumetricLightningPolicy.FromFeatures(features, totalTimeSeconds));
        LightningDebugSummary = frame.DebugSummary;
        if (!frame.Active)
        {
            return false;
        }
        if (!Allocated || AllShaders.FroxelLightning == null || Density == null || Lighting == null)
        {
            LightningDebugSummary = "shader/resources missing";
            return false;
        }

        var shader = AllShaders.FroxelLightning.Get(0);
        var lightning = new FroxelLightningParams
        {
            GridSize = new Vector4(mainDesc.Width, mainDesc.Height, mainDesc.Depth, totalTimeSeconds),
            Point0 = frame.Point0,
            Point1 = frame.Point1,
            Point2 = frame.Point2,
            Point3 = frame.Point3,
            Point4 = frame.Point4,
            Point5 = frame.Point5,
            Point6 = frame.Point6,
            Point7 = frame.Point7,
            Params = frame.Params,
            Color = frame.Color,
            Params2 = new Vector4(0f, frame.AfterglowSeconds, (int)frame.DebugColorMode, 0f)
        };
        shader.SetUniformBlock(3, ref lightning);
        rstate.Textures[0] = Density;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.SetStorageImage(1, Lighting);
        rstate.Shader = shader;
        rstate.DispatchCompute(GroupCount(mainDesc.Width), GroupCount(mainDesc.Height), GroupCount(mainDesc.Depth));
        rstate.BarrierComputeToCompute();
        rstate.Textures[0] = null;
        rstate.SetStorageImage(1, null);
        GpuLightningInjectedThisFrame = true;
        return true;
    }

    private bool ClearIdentityGpu(RenderContext rstate)
    {
        GpuIdentityClearedThisFrame = false;
        if (!Allocated || AllShaders.FroxelClear == null)
        {
            return false;
        }

        var shader = AllShaders.FroxelClear.Get(0);
        ClearIdentityGridGpu(rstate, shader, mainDesc, Density!, Lighting!, Integrated!, History!, HistoryConfidence!);
        if (NearAllocated)
        {
            ClearIdentityGridGpu(rstate, shader, nearDesc, NearDensity!, NearLighting!, NearIntegrated!, NearHistory!, NearHistoryConfidence!);
        }
        GpuIdentityClearedThisFrame = true;
        return true;
    }

    private static void ClearIdentityGridGpu(RenderContext rstate, Shader shader, FroxelGridDesc grid,
        Texture3D density, Texture3D lighting, Texture3D integrated, Texture3D history, Texture3D confidence)
    {
        var clear = new FroxelClearParams
        {
            GridSize = new Vector4(grid.Width, grid.Height, grid.Depth, 0f),
            ClearParams = new Vector4(grid.Quality, grid.NearPlane, grid.FarPlane, 0f)
        };
        shader.SetUniformBlock(3, ref clear);
        rstate.SetStorageImage(0, density);
        rstate.SetStorageImage(1, lighting);
        rstate.SetStorageImage(2, integrated);
        rstate.SetStorageImage(3, history);
        rstate.SetStorageImage(4, confidence);
        rstate.Shader = shader;
        rstate.DispatchCompute(GroupCount(grid.Width), GroupCount(grid.Height), GroupCount(grid.Depth));
        rstate.BarrierComputeToGraphics();
        rstate.SetStorageImage(0, null);
        rstate.SetStorageImage(1, null);
        rstate.SetStorageImage(2, null);
        rstate.SetStorageImage(3, null);
        rstate.SetStorageImage(4, null);
    }

    private bool InjectDensityGpu(RenderContext rstate, NebulaVolumeProfile profile,
        global::LibreLancer.Render.RenderFeatureSet features, float totalTimeSeconds)
    {
        GpuDensityInjectedThisFrame = false;
        if (!Allocated || AllShaders.FroxelInject == null)
        {
            return false;
        }

        var shader = AllShaders.FroxelInject.Get(0);
        var jitterTexture = BindJitterTexture(rstate, features.VolumetricBlueNoise, out var jitterActive);
        var importedDensityTexture = BindImportedDensityTexture(rstate, out var importedDensityActive);
        DispatchInjectGrid(rstate, shader, mainDesc, Density!, profile, totalTimeSeconds, jitterTexture, jitterActive,
            VolumetricNearDensityTuning.Disabled, null, null, importedDensityTexture, importedDensityActive);
        if (NearAllocated)
        {
            var tuning = VolumetricNearDensityTuning.ForProfile(profile, features.VolumetricQuality,
                features.VolumetricNearDetail);
            var displacementSource = WakeHistoryUpdatedThisFrame
                ? ShipDisplacementHistoryPrevious
                : DisplacementUpdatedThisFrame ? ShipDisplacement : null;
            var wakeVectors = WakeCurlUpdatedThisFrame ? WakeVectorField : null;
            DispatchInjectGrid(rstate, shader, nearDesc, NearDensity!, profile, totalTimeSeconds, jitterTexture,
                jitterActive, tuning, displacementSource, wakeVectors, importedDensityTexture, importedDensityActive);
            NearDetailTunedThisFrame = tuning.Enabled;
            NearDetailSummary = tuning.DebugSummary;
        }
        rstate.Textures[3] = null;
        rstate.Textures[4] = null;
        rstate.Textures[5] = null;
        rstate.Textures[6] = null;
        rstate.SetStorageImage(0, null);
        GpuDensityInjectedThisFrame = true;
        return true;
    }

    private void DispatchInjectGrid(RenderContext rstate, Shader shader, FroxelGridDesc grid,
        Texture3D density, NebulaVolumeProfile profile, float totalTimeSeconds, Texture2D jitterTexture,
        bool jitterActive, VolumetricNearDensityTuning nearTuning, Texture3D? displacement,
        Texture3D? wakeVectors, Texture3D importedDensityTexture, bool importedDensityActive)
    {
        var displacementActive = displacement != null && grid.Kind == FroxelGridKind.Near;
        var displacementTexture = BindDisplacementTexture(rstate, displacement, out _);
        var wakeVectorActive = wakeVectors != null && grid.Kind == FroxelGridKind.Near;
        var wakeVectorTexture = BindDisplacementTexture(rstate, wakeVectors, out _);
        var wakeWarp = Math.Clamp(grid.Quality, 0, 3) switch
        {
            0 => 0.08f,
            1 => 0.11f,
            3 => 0.17f,
            _ => 0.14f
        };
        var wakeSoftening = Math.Clamp(grid.Quality, 0, 3) switch
        {
            0 => 0.035f,
            1 => 0.05f,
            3 => 0.085f,
            _ => 0.065f
        };
        var inject = new FroxelInjectParams
        {
            GridSize = new Vector4(grid.Width, grid.Height, grid.Depth,
                grid.Kind == FroxelGridKind.Near ? 1f : 0f),
            ZonePositionRadius = new Vector4(profile.Position, MathF.Max(profile.BoundsRadius, 1f)),
            ZoneSizeEdge = new Vector4(
                MathF.Max(profile.Size.X, 1f),
                MathF.Max(profile.Size.Y, 1f),
                MathF.Max(profile.Size.Z, 1f),
                MathF.Max(profile.EdgeFraction, 0.05f)),
            DensityParams = new Vector4(
                MathF.Max(profile.CoreExtinction, 1e-7f),
                MathF.Max(profile.EdgeExtinction, 1e-7f),
                Math.Clamp(profile.Coverage, 0f, 1f),
                Math.Clamp(profile.DetailErosion, 0f, 1f)),
            NoiseParams = new Vector4(
                MathF.Max(profile.BaseNoiseScale, 1e-7f),
                Math.Clamp(profile.DomainWarp, 0f, 4f),
                MathF.Max(profile.DriftSpeed, 0f),
                totalTimeSeconds),
            EffectParams = profile.EffectProfile,
            ColorParams = new Vector4(profile.FogColor.R, profile.FogColor.G, profile.FogColor.B,
                profile.HasLightning ? 1f : 0f),
            JitterParams = new Vector4(jitterActive ? 1f : 0f, VolumetricBlueNoiseResources.TextureSize,
                MathF.Floor(totalTimeSeconds * 30f), grid.Kind == FroxelGridKind.Near ? 1f : 0f),
            NearDetailParams = nearTuning.ShaderDetailParams,
            NearDustParams = nearTuning.ShaderDustParams,
            DisplacementParams = new Vector4(displacementActive ? 1f : 0f,
                Math.Clamp(profile.DisplacementStrength, 0f, 2f), 1f, 0f),
            WakeVectorParams = new Vector4(wakeVectorActive ? 1f : 0f, wakeWarp, wakeSoftening, 0f),
            ImportedDensityParams = new Vector4(importedDensityActive ? 1f : 0f, 0.85f, 1f, 0f)
        };
        shader.SetUniformBlock(3, ref inject);
        rstate.Textures[3] = jitterTexture;
        rstate.Samplers[3] = SamplerState.PointClamp;
        rstate.Textures[4] = displacementTexture;
        rstate.Samplers[4] = SamplerState.LinearClamp;
        rstate.Textures[5] = wakeVectorTexture;
        rstate.Samplers[5] = SamplerState.LinearClamp;
        rstate.Textures[6] = importedDensityTexture;
        rstate.Samplers[6] = SamplerState.LinearClamp;
        rstate.SetStorageImage(0, density);
        rstate.Shader = shader;
        rstate.DispatchCompute(GroupCount(grid.Width), GroupCount(grid.Height), GroupCount(grid.Depth));
        rstate.BarrierComputeToCompute();
    }

    private bool InjectLightingGpu(RenderContext rstate, NebulaVolumeProfile profile, Vector3 sunDirection,
        float totalTimeSeconds)
    {
        GpuLightingInjectedThisFrame = false;
        if (!Allocated || AllShaders.FroxelLight == null)
        {
            return false;
        }

        if (sunDirection.LengthSquared() < 1e-6f)
        {
            sunDirection = Vector3.Normalize(new Vector3(0.35f, 0.18f, -0.92f));
        }
        else
        {
            sunDirection = Vector3.Normalize(sunDirection);
        }

        var shader = AllShaders.FroxelLight.Get(0);
        DispatchLightGrid(rstate, shader, mainDesc, Density!, Lighting!, profile, sunDirection, totalTimeSeconds);
        if (NearAllocated)
        {
            DispatchLightGrid(rstate, shader, nearDesc, NearDensity!, NearLighting!, profile, sunDirection, totalTimeSeconds);
        }
        rstate.Textures[0] = null;
        rstate.SetStorageImage(1, null);
        GpuLightingInjectedThisFrame = true;
        return true;
    }

    private static void DispatchLightGrid(RenderContext rstate, Shader shader, FroxelGridDesc grid,
        Texture3D density, Texture3D lighting, NebulaVolumeProfile profile, Vector3 sunDirection,
        float totalTimeSeconds)
    {
        var light = new FroxelLightParams
        {
            GridSize = new Vector4(grid.Width, grid.Height, grid.Depth,
                grid.Kind == FroxelGridKind.Near ? 1f : 0f),
            SunDirectionPhase = new Vector4(sunDirection, profile.PhaseGForward),
            PhaseParams = new Vector4(profile.PhaseGBackward, profile.PhaseBlend, profile.PowderFactor,
                profile.GodRayStrength),
            LightColorIntensity = new Vector4(profile.Albedo.R, profile.Albedo.G, profile.Albedo.B, 1f),
            AmbientDensity = new Vector4(profile.Ambient.R, profile.Ambient.G, profile.Ambient.B,
                Math.Clamp(profile.Coverage, 0f, 1f)),
            TimeParams = new Vector4(totalTimeSeconds, grid.NearPlane, grid.FarPlane,
                profile.HasLightning ? 1f : 0f)
        };
        shader.SetUniformBlock(3, ref light);
        rstate.Textures[0] = density;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.SetStorageImage(1, lighting);
        rstate.Shader = shader;
        rstate.DispatchCompute(GroupCount(grid.Width), GroupCount(grid.Height), GroupCount(grid.Depth));
        rstate.BarrierComputeToCompute();
    }

    private bool IntegrateScatteringGpu(RenderContext rstate, NebulaVolumeProfile profile)
    {
        GpuIntegratedThisFrame = false;
        if (!Allocated || AllShaders.FroxelIntegrate == null)
        {
            return false;
        }

        var shader = AllShaders.FroxelIntegrate.Get(0);
        DispatchIntegrateGrid(rstate, shader, mainDesc, Density!, Lighting!, Integrated!, History!, profile);
        if (NearAllocated)
        {
            DispatchIntegrateGrid(rstate, shader, nearDesc, NearDensity!, NearLighting!, NearIntegrated!, NearHistory!,
                profile);
        }
        rstate.Textures[0] = null;
        rstate.Textures[1] = null;
        rstate.SetStorageImage(2, null);
        rstate.SetStorageImage(3, null);
        GpuIntegratedThisFrame = true;
        return true;
    }

    private static void DispatchIntegrateGrid(RenderContext rstate, Shader shader, FroxelGridDesc grid,
        Texture3D density, Texture3D lighting, Texture3D integrated, Texture3D history, NebulaVolumeProfile profile)
    {
        var integrate = new FroxelIntegrateParams
        {
            GridSize = new Vector4(grid.Width, grid.Height, grid.Depth,
                grid.Kind == FroxelGridKind.Near ? 1f : 0f),
            DepthParams = new Vector4(grid.NearPlane, grid.FarPlane,
                MathF.Max(profile.CoreExtinction, 1e-7f), MathF.Max(profile.EdgeExtinction, 1e-7f)),
            HistoryParams = new Vector4(0f, 2f, profile.GodRayStrength, profile.PowderFactor),
            AlbedoParams = new Vector4(profile.Albedo.R, profile.Albedo.G, profile.Albedo.B,
                Math.Clamp(profile.Coverage, 0f, 1f))
        };
        shader.SetUniformBlock(3, ref integrate);
        rstate.Textures[0] = density;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.Textures[1] = lighting;
        rstate.Samplers[1] = SamplerState.LinearClamp;
        rstate.SetStorageImage(2, integrated);
        rstate.SetStorageImage(3, history);
        rstate.Shader = shader;
        rstate.DispatchCompute(GroupCount(grid.Width), GroupCount(grid.Height), GroupCount(grid.Depth));
        rstate.BarrierComputeToGraphics();
    }

    internal bool CompositeIntoHdr(global::LibreLancer.Render.HdrFramePipeline hdr,
        global::LibreLancer.Render.RenderFeatureSet features,
        NebulaVolumeProfile profile,
        global::LibreLancer.ICamera camera)
    {
        CompositeAppliedThisFrame = false;
        DepthAwareCompositeThisFrame = false;
        if (!features.VolumetricComposite || !GpuIntegratedThisFrame || Integrated is not { IsDisposed: false })
        {
            return false;
        }
        var sceneDepth = hdr.CopySceneDepthForVolumetrics();
        if (sceneDepth == null)
        {
            return false;
        }

        var intensity = features.VolumetricQuality switch
        {
            0 => 0.35f,
            1 => 0.50f,
            2 => 0.65f,
            _ => 0.78f
        };
        var slice = features.DebugView == RenderDebugView.VolumetricTransmittance ? 0.70f : 0.62f;
        var scatterGain = Math.Clamp(1.15f + profile.GodRayStrength * 0.55f, 0.65f, 2.25f);
        var settings = new Vector4(intensity, slice, scatterGain, 1f);
        var gridParams = new Vector4(mainDesc.Width, mainDesc.Height, mainDesc.Depth, 0f);
        var depthParams = VolumetricDepthMapping.DepthParams(mainDesc, enabled: true);
        Texture3D? nearSource = NearAllocated && NearIntegrated is { IsDisposed: false } ? NearIntegrated : null;
        var nearEnabled = VolumetricNearCascadeMath.CanCompositeNear(features, hasNearIntegrated: nearSource != null);
        var nearParams = VolumetricNearCascadeMath.ShaderParams(nearDesc, nearEnabled);
        if (!Matrix4x4.Invert(camera.Projection, out var inverseProjection))
        {
            inverseProjection = Matrix4x4.Identity;
        }
        var source = TemporalAppliedThisFrame && HistoryPrevious is { IsDisposed: false }
            ? HistoryPrevious
            : Integrated;
        CompositeAppliedThisFrame = hdr.CompositeVolumetricNebula(source, sceneDepth, inverseProjection,
            settings, gridParams, depthParams, nearSource, nearParams);
        DepthAwareCompositeThisFrame = CompositeAppliedThisFrame;
        NearCompositeAppliedThisFrame = CompositeAppliedThisFrame && nearEnabled;
        if (CompositeAppliedThisFrame)
        {
            LastDebug = LastDebug with
            {
                LastOperation = TemporalAppliedThisFrame
                    ? "GPU temporal history + HDR composite"
                    : "GPU light integrate + HDR composite"
            };
        }
        return CompositeAppliedThisFrame;
    }

    internal bool BindMaterialFog(global::LibreLancer.Render.RenderFeatureSet features, NebulaVolumeProfile profile)
    {
        MaterialFogBoundThisFrame = false;
        var integratedAvailable = GpuIntegratedThisFrame && Integrated is { IsDisposed: false };
        var historyAvailable = HistoryPrevious is { IsDisposed: false };
        var binding = VolumetricMaterialFogPolicy.Evaluate(
            features.VolumetricMaterialFog,
            CompositeAppliedThisFrame,
            integratedAvailable,
            TemporalAppliedThisFrame,
            historyAvailable,
            mainDesc,
            profile.CoreExtinction);
        MaterialFogDebugSummary = binding.DebugSummary;
        LastDebug = LastDebug with
        {
            MaterialFogBound = false,
            MaterialFogSummary = MaterialFogDebugSummary
        };
        if (!binding.CanBind)
        {
            return false;
        }

        var source = binding.UsesHistory && HistoryPrevious is { IsDisposed: false }
            ? HistoryPrevious
            : Integrated;
        global::LibreLancer.Render.RenderMaterial.VolumetricFogActive = true;
        global::LibreLancer.Render.RenderMaterial.VolumetricFogMaterialActive = true;
        global::LibreLancer.Render.RenderMaterial.SetVolumetricFogSource(source, binding.Settings);
        MaterialFogBoundThisFrame = true;
        LastDebug = LastDebug with
        {
            MaterialFogBound = true,
            MaterialFogSummary = MaterialFogDebugSummary
        };
        return true;
    }

    private static uint GroupCount(int dimension) => (uint)((dimension + 3) / 4);

    private static int DebugModeForView(RenderDebugView view) => view switch
    {
        RenderDebugView.VolumetricDensity => 1,
        RenderDebugView.VolumetricTransmittance => 2,
        RenderDebugView.VolumetricGodRays => 2,
        RenderDebugView.VolumetricFroxels or RenderDebugView.VolumetricZones => 3,
        RenderDebugView.VolumetricDisplacement => 4,
        RenderDebugView.VolumetricDisplacementHistory => 4,
        RenderDebugView.VolumetricLightning => 5,
        RenderDebugView.VolumetricLightningMask => 11,
        RenderDebugView.VolumetricHistory => 6,
        RenderDebugView.VolumetricHistoryConfidence => 7,
        RenderDebugView.VolumetricNearDensity => 8,
        RenderDebugView.VolumetricNear => 9,
        RenderDebugView.VolumetricWakeVectors => 10,
        RenderDebugView.VolumetricOpenVdb => 1,
        _ => 0
    };

    private static Texture3D CreateVolume(RenderContext rstate, string role, FroxelGridDesc desc) =>
        new(rstate, desc.Width, desc.Height, desc.Depth, SurfaceFormat.HdrBlendable, storage: true);

    private static Texture3D CreateDisplacementVolume(RenderContext rstate) =>
        new(rstate, DisplacementVolumeSize, DisplacementVolumeSize, DisplacementVolumeSize,
            SurfaceFormat.HdrBlendable, storage: true);

    private static long TextureBytes(Texture3D texture) =>
        checked((long)texture.Width * texture.Height * texture.Depth * 8);

    private static Matrix4x4 Pack4(Vector4[] values, int offset) => new(
        values[offset + 0].X, values[offset + 0].Y, values[offset + 0].Z, values[offset + 0].W,
        values[offset + 1].X, values[offset + 1].Y, values[offset + 1].Z, values[offset + 1].W,
        values[offset + 2].X, values[offset + 2].Y, values[offset + 2].Z, values[offset + 2].W,
        values[offset + 3].X, values[offset + 3].Y, values[offset + 3].Z, values[offset + 3].W);

    private struct FroxelClearParams
    {
        public Vector4 GridSize;
        public Vector4 ClearParams;
    }

    private struct FroxelInjectParams
    {
        public Vector4 GridSize;
        public Vector4 ZonePositionRadius;
        public Vector4 ZoneSizeEdge;
        public Vector4 DensityParams;
        public Vector4 NoiseParams;
        public Vector4 EffectParams;
        public Vector4 ColorParams;
        public Vector4 JitterParams;
        public Vector4 NearDetailParams;
        public Vector4 NearDustParams;
        public Vector4 DisplacementParams;
        public Vector4 WakeVectorParams;
        public Vector4 ImportedDensityParams;
    }

    private struct FroxelDisplacementParams
    {
        public Vector4 GridSize;
        public Vector4 GlobalParams;
        public Matrix4x4 CapsuleA0;
        public Matrix4x4 CapsuleA1;
        public Matrix4x4 CapsuleA2;
        public Matrix4x4 CapsuleA3;
        public Matrix4x4 CapsuleB0;
        public Matrix4x4 CapsuleB1;
        public Matrix4x4 CapsuleB2;
        public Matrix4x4 CapsuleB3;
        public Matrix4x4 CapsuleVelocity0;
        public Matrix4x4 CapsuleVelocity1;
        public Matrix4x4 CapsuleVelocity2;
        public Matrix4x4 CapsuleVelocity3;
        public Matrix4x4 CapsuleSwirl0;
        public Matrix4x4 CapsuleSwirl1;
        public Matrix4x4 CapsuleSwirl2;
        public Matrix4x4 CapsuleSwirl3;
    }

    private struct FroxelDisplacementHistoryParams
    {
        public Vector4 GridSize;
        public Vector4 HistoryParams;
        public Vector4 HistoryParams2;
    }

    private struct FroxelWakeCurlParams
    {
        public Vector4 GridSize;
        public Vector4 CurlParams;
        public Vector4 CurlParams2;
    }

    private struct FroxelLightParams
    {
        public Vector4 GridSize;
        public Vector4 SunDirectionPhase;
        public Vector4 PhaseParams;
        public Vector4 LightColorIntensity;
        public Vector4 AmbientDensity;
        public Vector4 TimeParams;
    }

    private struct FroxelIntegrateParams
    {
        public Vector4 GridSize;
        public Vector4 DepthParams;
        public Vector4 HistoryParams;
        public Vector4 AlbedoParams;
    }

    private struct FroxelLightningParams
    {
        public Vector4 GridSize;
        public Vector4 Point0;
        public Vector4 Point1;
        public Vector4 Point2;
        public Vector4 Point3;
        public Vector4 Point4;
        public Vector4 Point5;
        public Vector4 Point6;
        public Vector4 Point7;
        public Vector4 Params;
        public Vector4 Color;
        public Vector4 Params2;
    }

    private struct FroxelTemporalParams
    {
        public Vector4 GridSize;
        public Vector4 TemporalParams;
        public Vector4 JitterParams;
        public Vector4 ReprojectionParams;
        public Vector4 RejectParams;
        public Vector4 PreviousCameraPosition;
        public Vector4 CurrentCameraPosition;
        public Matrix4x4 PreviousViewProjection;
        public Matrix4x4 CurrentInverseViewProjection;
    }

    private struct FroxelDebugSliceParams
    {
        public Vector4 SliceParams;
        public Vector4 GridParams;
    }

    private void ClearIdentityUploads()
    {
        if (!Allocated)
        {
            return;
        }

        var zeros = new ushort[checked(mainDesc.VoxelCount * 4)];
        Array.Fill(zeros, HalfZero);
        Density!.SetData(zeros);
        Lighting!.SetData(zeros);
        HistoryConfidence?.SetData(zeros);

        var identity = new ushort[checked(mainDesc.VoxelCount * 4)];
        for (var i = 0; i < identity.Length; i += 4)
        {
            identity[i + 0] = HalfZero;
            identity[i + 1] = HalfZero;
            identity[i + 2] = HalfZero;
            identity[i + 3] = HalfOne;
        }
        Integrated!.SetData(identity);
        History!.SetData(identity);
        UploadIdentity(HistoryPrevious);
        if (NearAllocated)
        {
            ClearVolume(NearDensity);
            ClearVolume(NearLighting);
            ClearVolume(NearHistoryConfidence);
            UploadIdentity(NearIntegrated);
            UploadIdentity(NearHistory);
        }
        ClearVolume(ShipDisplacement);
        ClearVolume(ShipDisplacementHistory);
        ClearVolume(ShipDisplacementHistoryPrevious);
        ClearVolume(WakeVectorField);
    }

    private static void UploadIdentity(Texture3D? texture)
    {
        if (texture == null)
        {
            return;
        }
        var identity = new ushort[checked(texture.Width * texture.Height * texture.Depth * 4)];
        for (var i = 0; i < identity.Length; i += 4)
        {
            identity[i + 0] = HalfZero;
            identity[i + 1] = HalfZero;
            identity[i + 2] = HalfZero;
            identity[i + 3] = HalfOne;
        }
        texture.SetData(identity);
    }

    private static void ClearVolume(Texture3D? texture)
    {
        if (texture == null)
        {
            return;
        }
        var zeros = new ushort[checked(texture.Width * texture.Height * texture.Depth * 4)];
        texture.SetData(zeros);
    }

    private static Texture2D CreateFallbackDepthTexture(RenderContext rstate)
    {
        var texture = new Texture2D(rstate, 1, 1, false, SurfaceFormat.Depth);
        texture.SetData(new[] { 1f });
        return texture;
    }

    private Texture2D BindJitterTexture(RenderContext rstate, bool enabled, out bool active)
    {
        active = enabled;
        if (enabled)
        {
            blueNoise ??= new VolumetricBlueNoiseResources(rstate);
            BlueNoiseBoundThisFrame = true;
            BlueNoiseSourceName = blueNoise.SourceName;
            LastBlueNoiseSource = BlueNoiseSourceName;
            return blueNoise.Texture;
        }

        fallbackJitterTexture ??= CreateFallbackJitterTexture(rstate);
        if (!BlueNoiseBoundThisFrame)
        {
            BlueNoiseSourceName = "neutral-1x1";
            LastBlueNoiseSource = BlueNoiseSourceName;
        }
        return fallbackJitterTexture;
    }

    private Texture3D BindDisplacementTexture(RenderContext rstate, Texture3D? displacement, out bool active)
    {
        active = displacement != null;
        if (displacement != null)
        {
            return displacement;
        }

        fallbackDisplacementTexture ??= CreateFallbackDisplacementTexture(rstate);
        return fallbackDisplacementTexture;
    }

    private Texture3D BindImportedDensityTexture(RenderContext rstate, out bool active)
    {
        active = false;
        if (importedDensity.Valid)
        {
            var key = ImportedDensityTextureKey(importedDensity.Descriptor);
            if (importedDensityTexture == null ||
                !string.Equals(importedDensityTextureKey, key, StringComparison.Ordinal))
            {
                DisposeImportedDensityTexture();
                importedDensityTexture = CreateImportedDensityTexture(rstate, importedDensity);
                importedDensityTextureKey = key;
            }

            active = importedDensityTexture != null;
            if (active)
            {
                return importedDensityTexture!;
            }
        }

        fallbackDisplacementTexture ??= CreateFallbackDisplacementTexture(rstate);
        return fallbackDisplacementTexture;
    }

    public static ushort[] BuildImportedDensityTextureData(VolumetricImportedDensityFrame densityFrame)
    {
        if (!densityFrame.Valid ||
            densityFrame.UnitDensitySamples.Length != densityFrame.Descriptor.VoxelCount)
        {
            return [];
        }

        var data = new ushort[checked(densityFrame.UnitDensitySamples.Length * 4)];
        for (var i = 0; i < densityFrame.UnitDensitySamples.Length; i++)
        {
            var v = Math.Clamp(densityFrame.UnitDensitySamples[i], 0f, 1f);
            data[(i * 4) + 0] = BitConverter.HalfToUInt16Bits((Half)v);
            data[(i * 4) + 1] = HalfZero;
            data[(i * 4) + 2] = HalfZero;
            data[(i * 4) + 3] = HalfOne;
        }
        return data;
    }

    private static Texture3D CreateImportedDensityTexture(
        RenderContext rstate,
        VolumetricImportedDensityFrame densityFrame)
    {
        var descriptor = densityFrame.Descriptor;
        var texture = new Texture3D(rstate, descriptor.Width, descriptor.Height, descriptor.Depth,
            SurfaceFormat.HdrBlendable, storage: false);
        texture.SetData(BuildImportedDensityTextureData(densityFrame));
        return texture;
    }

    private static string ImportedDensityTextureKey(VolumetricEngineVolumeDescriptor descriptor) =>
        descriptor.Valid
            ? $"{descriptor.Width}x{descriptor.Height}x{descriptor.Depth}:{descriptor.Format}:{descriptor.ContentHash}"
            : "";

    private void DisposeImportedDensityTexture()
    {
        importedDensityTexture?.Dispose();
        importedDensityTexture = null;
        importedDensityTextureKey = string.Empty;
    }

    private static Texture2D CreateFallbackJitterTexture(RenderContext rstate)
    {
        var texture = new Texture2D(rstate, 1, 1, false, SurfaceFormat.Bgra8);
        texture.SetData(new uint[] { 0xFF808080 });
        return texture;
    }

    private static Texture3D CreateFallbackDisplacementTexture(RenderContext rstate)
    {
        var texture = new Texture3D(rstate, 1, 1, 1, SurfaceFormat.HdrBlendable, storage: false);
        texture.SetData(new ushort[] { HalfZero, HalfZero, HalfZero, HalfZero });
        return texture;
    }

    private void DisposeTextures()
    {
        Density?.Dispose();
        Lighting?.Dispose();
        Integrated?.Dispose();
        History?.Dispose();
        HistoryPrevious?.Dispose();
        HistoryConfidence?.Dispose();
        NearDensity?.Dispose();
        NearLighting?.Dispose();
        NearIntegrated?.Dispose();
        NearHistory?.Dispose();
        NearHistoryConfidence?.Dispose();
        ShipDisplacement?.Dispose();
        ShipDisplacementHistory?.Dispose();
        ShipDisplacementHistoryPrevious?.Dispose();
        WakeVectorField?.Dispose();
        fallbackDepthTexture?.Dispose();
        blueNoise?.Dispose();
        fallbackJitterTexture?.Dispose();
        fallbackDisplacementTexture?.Dispose();
        Density = null;
        Lighting = null;
        Integrated = null;
        History = null;
        HistoryPrevious = null;
        HistoryConfidence = null;
        NearDensity = null;
        NearLighting = null;
        NearIntegrated = null;
        NearHistory = null;
        NearHistoryConfidence = null;
        ShipDisplacement = null;
        ShipDisplacementHistory = null;
        ShipDisplacementHistoryPrevious = null;
        WakeVectorField = null;
        fallbackDepthTexture = null;
        blueNoise = null;
        fallbackJitterTexture = null;
        fallbackDisplacementTexture = null;
        qualityProfile = default;
        activeProfile = string.Empty;
        allocatedQuality = -1;
        GpuIdentityClearedThisFrame = false;
        GpuDensityInjectedThisFrame = false;
        GpuLightingInjectedThisFrame = false;
        GpuLightningInjectedThisFrame = false;
        GpuIntegratedThisFrame = false;
        TemporalAppliedThisFrame = false;
        TemporalResetThisFrame = false;
        TemporalJitter = Vector2.Zero;
        TemporalFrameIndex = 0;
        TemporalHistoryConfidence = 0f;
        ReprojectionAppliedThisFrame = false;
        CompositeAppliedThisFrame = false;
        DepthAwareCompositeThisFrame = false;
        MaterialFogBoundThisFrame = false;
        MaterialFogDebugSummary = string.Empty;
        NearCompositeAppliedThisFrame = false;
        NearDetailTunedThisFrame = false;
        NearDetailSummary = string.Empty;
        DisplacementUpdatedThisFrame = false;
        DisplacementCapsuleCount = 0;
        DisplacementDebugSummary = string.Empty;
        WakeHistoryUpdatedThisFrame = false;
        WakeHistoryDebugSummary = string.Empty;
        WakeCurlUpdatedThisFrame = false;
        WakeCurlDebugSummary = string.Empty;
        LightningDebugSummary = string.Empty;
        BlueNoiseBoundThisFrame = false;
        BlueNoiseSourceName = "off";
        LastBlueNoiseSource = "off";
        temporalState.Reset();
        lastDisplacementHistoryTime = 0f;
        lastWakeVelocity = Vector3.Zero;
        mainDesc = default;
        nearDesc = default;
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(VolumetricNebulaFrameResources));
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        DisposeTextures();
        DisposeImportedDensityTexture();
        LastDebug = VolumetricNebulaResourceDebug.Disabled("disposed");
        LastBlueNoiseSource = "off";
        LastImportedDensitySource = "off";
    }
}

public readonly record struct VolumetricNebulaResourceDebug(
    bool Allocated,
    string Dimensions,
    string NearDimensions,
    string QualityName,
    int Quality,
    string PerformanceSummary,
    string ActiveProfile,
    long EstimatedBytes,
    string LastOperation,
    string DebugName,
    int PassSlotCount,
    bool NearDetail,
    string NearDetailSummary,
    bool DisplacementUpdated,
    int DisplacementCapsules,
    string DisplacementSummary,
    bool WakeHistoryUpdated,
    string WakeHistorySummary,
    bool WakeCurlUpdated,
    string WakeCurlSummary,
    bool GodRaysApplied,
    string GodRaySummary,
    bool MaterialFogBound,
    string MaterialFogSummary,
    bool LightningUpdated,
    string LightningSummary)
{
    public static VolumetricNebulaResourceDebug Disabled(string reason) =>
        new(false, "not allocated", "not allocated", "off", -1, "", "", 0, reason, "vol_nebula.none", 0, false, "",
            false, 0, "", false, "", false, "", false, "", false, "", false, "");

    public static VolumetricNebulaResourceDebug Waiting(string reason) =>
        new(false, "waiting", "waiting", "waiting", -1, "", "", 0, reason, "vol_nebula.waiting",
            VolumetricNebulaPassDeclaration.CanonicalOrder.Count, false, "", false, 0, "", false, "", false, "",
            false, "", false, "", false, "");
}
