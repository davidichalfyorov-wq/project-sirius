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
    private Texture2D? fallbackJitterTexture;

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
    private Texture2D? fallbackDepthTexture;

    public VolumetricNebulaQualityProfile QualityProfile => qualityProfile;
    public FroxelGridDesc MainDesc => mainDesc;
    public FroxelGridDesc NearDesc => nearDesc;
    public bool Allocated => Density != null && Lighting != null && Integrated != null &&
                             History != null && HistoryPrevious != null && HistoryConfidence != null;
    public bool NearAllocated => NearDensity != null && NearLighting != null &&
                                 NearIntegrated != null && NearHistory != null && NearHistoryConfidence != null;
    public long EstimatedBytes => Allocated
        ? mainDesc.BytesPerVolume() * 6 + (NearAllocated ? nearDesc.BytesPerVolume() * 5 : 0)
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
    public bool MaterialFogBoundThisFrame { get; private set; }
    public string LightningDebugSummary { get; private set; } = string.Empty;
    public bool BlueNoiseBoundThisFrame { get; private set; }
    public string BlueNoiseSourceName { get; private set; } = "off";

    public static VolumetricNebulaResourceDebug LastDebug { get; private set; } =
        VolumetricNebulaResourceDebug.Disabled("not initialized");
    public static string LastBlueNoiseSource { get; private set; } = "off";

    public void Ensure(
        RenderContext rstate,
        int renderWidth,
        int renderHeight,
        global::LibreLancer.Render.RenderFeatureSet features,
        NebulaVolumeProfile? profile,
        float totalTimeSeconds = 0f,
        Vector3 sunDirection = default)
    {
        ThrowIfDisposed();

        if (!features.VolumetricNebula)
        {
            DisposeTextures();
            LastDebug = VolumetricNebulaResourceDebug.Disabled("volumetric_nebula disabled");
            return;
        }

        if (!rstate.HasFeature(GraphicsFeature.Compute))
        {
            DisposeTextures();
            LastDebug = VolumetricNebulaResourceDebug.Disabled("backend has no compute feature");
            return;
        }

        if (profile is not { IsValid: true } active)
        {
            LastDebug = VolumetricNebulaResourceDebug.Waiting("waiting for active nebula profile");
            return;
        }

        var desiredProfile = VolumetricNebulaQualityProfile.Create(renderWidth, renderHeight, features, active);
        var desired = desiredProfile.MainGrid;
        var desiredNear = desiredProfile.NearGrid;
        var wantsNear = features.VolumetricNearCascade;
        var needsAllocate = !Allocated ||
                            desired != mainDesc ||
                            allocatedQuality != desired.Quality ||
                            wantsNear != NearAllocated ||
                            (wantsNear && desiredNear != nearDesc);

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
                generation++;
                UploadIdentity(HistoryPrevious);
                UploadIdentity(NearHistory);
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
        MaterialFogBoundThisFrame = false;
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

        LastDebug = new VolumetricNebulaResourceDebug(
            true,
            mainDesc.Dimensions,
            nearDesc.Dimensions,
            qualityProfile.Name,
            mainDesc.Quality,
            activeProfile,
            EstimatedBytes,
            integrated
                ? lightningInjected
                    ? (needsAllocate ? "allocated + GPU lightning integrate" : "GPU lightning integrate")
                    : (needsAllocate ? "allocated + GPU light integrate" : "GPU light integrate")
                : lightingInjected
                    ? (needsAllocate ? "allocated + GPU light inject" : "GPU light inject")
                : densityInjected
                ? (needsAllocate ? "allocated + GPU density inject" : "GPU density inject")
                : gpuCleared
                    ? (needsAllocate ? "allocated + GPU identity clear" : "GPU identity clear")
                    : (needsAllocate ? "allocated + identity upload fallback" : "identity clear marker"),
            mainDesc.DebugName,
            VolumetricNebulaPassDeclaration.CanonicalOrder.Count,
            NearDetailTunedThisFrame,
            NearDetailSummary);
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
        var debugSource = debugView switch
        {
            RenderDebugView.VolumetricLightning => Lighting,
            RenderDebugView.VolumetricHistory => TemporalAppliedThisFrame ? HistoryPrevious : History,
            RenderDebugView.VolumetricHistoryConfidence => HistoryConfidence,
            RenderDebugView.VolumetricNearDensity => NearDensity,
            RenderDebugView.VolumetricNear => NearDensity,
            _ => Density
        };
        var integratedSource = debugView == RenderDebugView.VolumetricNear && NearIntegrated != null
            ? NearIntegrated
            : Integrated;
        var debugGrid = nearDebug && nearDesc.IsValid ? nearDesc : mainDesc;
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

    private bool InjectLightningChannelsGpu(RenderContext rstate, NebulaVolumeProfile profile,
        global::LibreLancer.Render.RenderFeatureSet features, float totalTimeSeconds)
    {
        GpuLightningInjectedThisFrame = false;
        LightningDebugSummary = string.Empty;
        if (!features.VolumetricLightningChannels)
        {
            return false;
        }

        var frame = lightningChannels.BuildFrame(profile, features, totalTimeSeconds);
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
            Params2 = new Vector4(0f, frame.AfterglowSeconds, 0f, 0f)
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
        DispatchInjectGrid(rstate, shader, mainDesc, Density!, profile, totalTimeSeconds, jitterTexture, jitterActive,
            VolumetricNearDensityTuning.Disabled);
        if (NearAllocated)
        {
            var tuning = VolumetricNearDensityTuning.ForProfile(profile, features.VolumetricQuality,
                features.VolumetricNearDetail);
            DispatchInjectGrid(rstate, shader, nearDesc, NearDensity!, profile, totalTimeSeconds, jitterTexture,
                jitterActive, tuning);
            NearDetailTunedThisFrame = tuning.Enabled;
            NearDetailSummary = tuning.DebugSummary;
        }
        rstate.Textures[3] = null;
        rstate.SetStorageImage(0, null);
        GpuDensityInjectedThisFrame = true;
        return true;
    }

    private static void DispatchInjectGrid(RenderContext rstate, Shader shader, FroxelGridDesc grid,
        Texture3D density, NebulaVolumeProfile profile, float totalTimeSeconds, Texture2D jitterTexture,
        bool jitterActive, VolumetricNearDensityTuning nearTuning)
    {
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
            NearDustParams = nearTuning.ShaderDustParams
        };
        shader.SetUniformBlock(3, ref inject);
        rstate.Textures[3] = jitterTexture;
        rstate.Samplers[3] = SamplerState.PointClamp;
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
        if (!features.VolumetricMaterialFog || !GpuIntegratedThisFrame || Integrated is not { IsDisposed: false })
        {
            return false;
        }

        var source = TemporalAppliedThisFrame && HistoryPrevious is { IsDisposed: false }
            ? HistoryPrevious
            : Integrated;
        var settings = VolumetricDepthMapping.MaterialFogSettings(mainDesc, profile.CoreExtinction);
        global::LibreLancer.Render.RenderMaterial.VolumetricFogActive = true;
        global::LibreLancer.Render.RenderMaterial.VolumetricFogMaterialActive = true;
        global::LibreLancer.Render.RenderMaterial.SetVolumetricFogSource(source, settings);
        MaterialFogBoundThisFrame = true;
        return true;
    }

    private static uint GroupCount(int dimension) => (uint)((dimension + 3) / 4);

    private static int DebugModeForView(RenderDebugView view) => view switch
    {
        RenderDebugView.VolumetricDensity => 1,
        RenderDebugView.VolumetricTransmittance => 2,
        RenderDebugView.VolumetricFroxels or RenderDebugView.VolumetricZones => 3,
        RenderDebugView.VolumetricDisplacement => 4,
        RenderDebugView.VolumetricLightning => 5,
        RenderDebugView.VolumetricHistory => 6,
        RenderDebugView.VolumetricHistoryConfidence => 7,
        RenderDebugView.VolumetricNearDensity => 8,
        RenderDebugView.VolumetricNear => 9,
        _ => 0
    };

    private static Texture3D CreateVolume(RenderContext rstate, string role, FroxelGridDesc desc) =>
        new(rstate, desc.Width, desc.Height, desc.Depth, SurfaceFormat.HdrBlendable, storage: true);

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

    private static Texture2D CreateFallbackJitterTexture(RenderContext rstate)
    {
        var texture = new Texture2D(rstate, 1, 1, false, SurfaceFormat.Bgra8);
        texture.SetData(new uint[] { 0xFF808080 });
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
        fallbackDepthTexture?.Dispose();
        blueNoise?.Dispose();
        fallbackJitterTexture?.Dispose();
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
        fallbackDepthTexture = null;
        blueNoise = null;
        fallbackJitterTexture = null;
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
        NearCompositeAppliedThisFrame = false;
        NearDetailTunedThisFrame = false;
        NearDetailSummary = string.Empty;
        LightningDebugSummary = string.Empty;
        BlueNoiseBoundThisFrame = false;
        BlueNoiseSourceName = "off";
        LastBlueNoiseSource = "off";
        temporalState.Reset();
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
        LastDebug = VolumetricNebulaResourceDebug.Disabled("disposed");
        LastBlueNoiseSource = "off";
    }
}

public readonly record struct VolumetricNebulaResourceDebug(
    bool Allocated,
    string Dimensions,
    string NearDimensions,
    string QualityName,
    int Quality,
    string ActiveProfile,
    long EstimatedBytes,
    string LastOperation,
    string DebugName,
    int PassSlotCount,
    bool NearDetail,
    string NearDetailSummary)
{
    public static VolumetricNebulaResourceDebug Disabled(string reason) =>
        new(false, "not allocated", "not allocated", "off", -1, "", 0, reason, "vol_nebula.none", 0, false, "");

    public static VolumetricNebulaResourceDebug Waiting(string reason) =>
        new(false, "waiting", "waiting", "waiting", -1, "", 0, reason, "vol_nebula.waiting",
            VolumetricNebulaPassDeclaration.CanonicalOrder.Count, false, "");
}
