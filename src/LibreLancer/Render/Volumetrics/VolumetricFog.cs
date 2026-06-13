using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using LibreLancer.Data.GameData;
using LibreLancer.Graphics;
using LibreLancer.Shaders;
using LibreLancer.World;

namespace LibreLancer.Render.Volumetrics;

public readonly record struct VolumetricFogStatus(
    bool Enabled,
    bool Active,
    string Quality,
    int ZoneCount,
    int MainX,
    int MainY,
    int MainZ,
    bool NearEnabled,
    int NearX,
    int NearY,
    int NearZ,
    int DistantW,
    int DistantH,
    int DistantSteps);

/// <summary>
/// Froxel volumetric nebulae (phase 5, track V): camera-aligned 3D grids
/// filled by compute (inject -> light -> integrate) and composited over
/// the scene before the transparent pass. Vulkan-only behind
/// GraphicsFeature.Compute; the legacy NebulaRenderer remains the
/// fallback and keeps running for puffs until V6/V8 retire them.
/// </summary>
public class VolumetricFog : IDisposable
{
    private readonly record struct QualityProfile(
        string Name,
        int GridX,
        int GridY,
        int GridZ,
        bool NearEnabled,
        int NearGridX,
        int NearGridY,
        int NearGridZ,
        int DistantSteps,
        float TransmittanceFloor);

    private static readonly QualityProfile[] QualityProfiles =
    [
        new("low", 96, 54, 64, false, 0, 0, 0, 24, 0.18f),
        new("medium", 128, 72, 80, true, 96, 54, 48, 32, 0.15f),
        new("high", 160, 90, 96, true, 128, 72, 64, 48, 0f),
        new("ultra", 192, 108, 128, true, 160, 90, 80, 48, 0f)
    ];

    private const float LegacyNearPlane = 8f;
    private const float MainCascadeNear = 580f;
    private const float FarPlane = 14000f;
    private const float NearCascadeNear = 2f;
    private const float NearCascadeFar = 620f;

    // SIRIUS_VOLFOG: 1 forces on, 0 forces off (overrides the INI).
    private static readonly string? envOverride =
        Environment.GetEnvironmentVariable("SIRIUS_VOLFOG");

    // SIRIUS_VOLNEAR=0 disables the near cascade and restores the
    // original main-cascade near plane for emergency comparisons.
    private static readonly bool nearCascadeAllowed =
        Environment.GetEnvironmentVariable("SIRIUS_VOLNEAR") != "0";

    private static readonly bool debugZones =
        Environment.GetEnvironmentVariable("SIRIUS_DEBUG_VIEW") == "volzones";
    private static readonly bool debugDepth =
        Environment.GetEnvironmentVariable("SIRIUS_DEBUG_VIEW") == "voldepth";
    private static readonly bool debugDisplacement =
        Environment.GetEnvironmentVariable("SIRIUS_DEBUG_VIEW") == "voldisp";

    private readonly RenderContext rstate;
    private Texture3D? media;
    private Texture3D? scatterA;
    private Texture3D? scatterB;
    private bool scatterFlip;
    private Texture3D? integrated;
    private Texture3D? nearMedia;
    private Texture3D? nearScatterA;
    private Texture3D? nearScatterB;
    private bool nearScatterFlip;
    private Texture3D? nearIntegrated;
    private readonly FogDisplacement displacement = new();
    private Texture3D? noiseBase;
    private Texture3D? noiseDetail;
    // Distant layer (V6): half-res 2D march targets, storage-capable
    // (Texture3D with depth 1 = storage 2D image).
    private Texture3D? distantA;
    private Texture3D? distantB;
    private bool distantFlip;
    private int distantW, distantH;
    private StorageBuffer? zoneBuffer;
    private Texture2D? depthCopy;
    private QualityProfile profile = QualityProfiles[2];
    private int appliedQuality = 2;
    private Matrix4x4 prevViewProj;
    private Vector3 prevCameraPos;
    private bool historyValid;
    private Texture3D? distantCurrent;
    private float materialMeanExtinction;
    private readonly NebulaVolumeData.GpuZone[] zoneScratch =
        new NebulaVolumeData.GpuZone[NebulaVolumeData.MaxZones];
    private uint frameIndex;
    private bool resourcesLogged;
    private bool gateLogged;

    /// <summary>INI/settings master switch (volumetric_nebulae).</summary>
    public bool Enabled;

    /// <summary>0 low, 1 medium, 2 high, 3 ultra.</summary>
    public int Quality = 2;

    /// <summary>True when this frame actually built and wants composite.</summary>
    public bool Active { get; private set; }

    public VolumetricFogStatus Status { get; private set; }

    public Texture3D? IntegratedTexture => integrated;

    public Vector4 MaterialFogSettings =>
        new(MainNearPlane, FarPlane, profile.GridZ, materialMeanExtinction);

    public VolumetricFog(RenderContext rstate)
    {
        this.rstate = rstate;
    }

    private bool FeatureAvailable =>
        envOverride != "0" &&
        (Enabled || envOverride == "1") &&
        rstate.HasFeature(GraphicsFeature.Compute) &&
        AllShaders.FroxelInject != null;

    private bool NearCascadeActive => nearCascadeAllowed && profile.NearEnabled;

    private float MainNearPlane => NearCascadeActive ? MainCascadeNear : LegacyNearPlane;

    private struct FroxelParams
    {
        public Matrix4x4 InvViewProj;
        public Vector4 CameraPosNear;
        public Vector4 GridSizeFar;
        public Vector4 Counts;
        public Vector4 DetailParams;
        public Vector4 DisplacementOriginExtent;
        public Vector4 DisplacementParams;
    }

    private struct NoiseGenParams
    {
        public Vector4 SizeMode;
    }

    private struct DistantParams
    {
        public Matrix4x4 InvViewProj;
        public Matrix4x4 PrevViewProj;
        public Vector4 CameraPosStart;
        public Vector4 TargetSizeEnd;
        public Vector4 SunDirAmbient;
        public Vector4 SunColorIntensity;
        public Vector4 ZoneCountsBlend;
        public Vector4 DistantQuality;
    }

    private const float DistantFar = 80000f;

    private struct LightParams
    {
        public Matrix4x4 InvViewProj;
        public Matrix4x4 ViewProj;
        public Matrix4x4 PrevViewProj;
        public Matrix4x4 ShadowMatrix0;
        public Matrix4x4 ShadowMatrix1;
        public Matrix4x4 ShadowMatrix2;
        public Vector4 CameraPosNear;
        public Vector4 GridSizeFar;
        public Vector4 SunDirAmbient;
        public Vector4 SunColorIntensity;
        public Vector4 Temporal;
        public Vector4 ShadowSplits;
        public Vector4 ShadowParams;
        public Vector4 LightningPosRadius;
        public Vector4 LightningColor;
    }

    private struct IntegrateParams
    {
        public Vector4 GridNear;
        public Vector4 FarPad;
    }

    private struct CompositeParams
    {
        public Matrix4x4 InvViewProj;
        public Vector4 CameraPosNear;
        public Vector4 GridSizeFar;
        public Vector4 NearGridSizeFar;
        public Vector4 CascadeBlend;
        public Vector4 DebugMode;
    }

    private Matrix4x4 invViewProj;
    private Vector3 cameraPos;

    private void ApplyQualityProfile()
    {
        var q = Math.Clamp(Quality, 0, QualityProfiles.Length - 1);
        if (q == appliedQuality)
        {
            return;
        }
        appliedQuality = q;
        profile = QualityProfiles[q];
        DisposeVolumeResources();
        FLLog.Info("Volumetrics",
            $"Quality {profile.Name}: main {profile.GridX}x{profile.GridY}x{profile.GridZ}, " +
            $"near={(NearCascadeActive ? $"{profile.NearGridX}x{profile.NearGridY}x{profile.NearGridZ}" : "off")}, " +
            $"distant steps={profile.DistantSteps}, trans floor={profile.TransmittanceFloor:F2}");
    }

    private void DisposeVolumeResources()
    {
        media?.Dispose();
        scatterA?.Dispose();
        scatterB?.Dispose();
        integrated?.Dispose();
        nearMedia?.Dispose();
        nearScatterA?.Dispose();
        nearScatterB?.Dispose();
        nearIntegrated?.Dispose();
        distantA?.Dispose();
        distantB?.Dispose();
        depthCopy?.Dispose();
        media = null;
        scatterA = null;
        scatterB = null;
        integrated = null;
        nearMedia = null;
        nearScatterA = null;
        nearScatterB = null;
        nearIntegrated = null;
        distantA = null;
        distantB = null;
        depthCopy = null;
        distantCurrent = null;
        scatterFlip = false;
        nearScatterFlip = false;
        distantFlip = false;
        distantW = 0;
        distantH = 0;
        historyValid = false;
        resourcesLogged = false;
    }

    private void UpdateStatus(bool active, int zoneCount)
    {
        Status = new VolumetricFogStatus(
            Enabled,
            active,
            profile.Name,
            zoneCount,
            profile.GridX,
            profile.GridY,
            profile.GridZ,
            NearCascadeActive,
            NearCascadeActive ? profile.NearGridX : 0,
            NearCascadeActive ? profile.NearGridY : 0,
            NearCascadeActive ? profile.NearGridZ : 0,
            distantW,
            distantH,
            profile.DistantSteps);
    }

    private static uint Groups4(int size) => (uint)((size + 3) / 4);

    private static uint Groups8(int size) => (uint)((size + 7) / 8);

    /// <summary>
    /// Runs the froxel compute chain. Call once per frame after the camera
    /// is set and BEFORE the first scene draw (dispatches end any active
    /// render pass). No-ops (Active=false) when the feature is off or no
    /// nebula volume is near the camera.
    /// </summary>
    public void RunDispatches(ICamera camera, List<NebulaRenderer> nebulae, double driftTime,
        Vector3 sunToScene, Vector3 sunColorIn, bool sunFound,
        ShadowMapRenderer? shadowMaps, bool csmShadowsLive, NebulaRenderer? currentNebula,
        int renderWidth, int renderHeight, IReadOnlyList<GameObject>? displacementObjects)
    {
        ApplyQualityProfile();
        Active = false;
        materialMeanExtinction = 0f;
        UpdateStatus(false, 0);
        if (!FeatureAvailable)
        {
            if (!gateLogged)
            {
                gateLogged = true;
                FLLog.Info("Volumetrics",
                    $"gated: enabled={Enabled} env={envOverride ?? "unset"} compute={rstate.HasFeature(GraphicsFeature.Compute)} bundles={(AllShaders.FroxelInject != null)}");
            }
            return;
        }
        if (!NebulaVolumeData.AnyVolumeNear(nebulae, camera.Position, FarPlane))
        {
            return; // empty space: zero cost
        }
        var zoneCount = NebulaVolumeData.Collect(nebulae, camera.Position, zoneScratch);
        if (zoneCount == 0)
        {
            return;
        }
        materialMeanExtinction = MeanExtinction(zoneScratch, zoneCount);

        if (media == null)
        {
            media = new Texture3D(rstate, profile.GridX, profile.GridY, profile.GridZ, SurfaceFormat.HdrBlendable, storage: true);
            scatterA = new Texture3D(rstate, profile.GridX, profile.GridY, profile.GridZ, SurfaceFormat.HdrBlendable, storage: true);
            scatterB = new Texture3D(rstate, profile.GridX, profile.GridY, profile.GridZ, SurfaceFormat.HdrBlendable, storage: true);
            integrated = new Texture3D(rstate, profile.GridX, profile.GridY, profile.GridZ, SurfaceFormat.HdrBlendable, storage: true);
            zoneBuffer = new StorageBuffer(rstate,
                NebulaVolumeData.MaxZones * NebulaVolumeData.GpuZoneSize, NebulaVolumeData.GpuZoneSize);
            FLLog.Info("Volumetrics",
                $"Froxel main grid {profile.GridX}x{profile.GridY}x{profile.GridZ}, range {MainNearPlane}-{FarPlane}m, " +
                $"quality={profile.Name}, distant steps={profile.DistantSteps}, trans floor={profile.TransmittanceFloor:F2}");
            BakeNoise();
        }
        if (NearCascadeActive && nearMedia == null)
        {
            nearMedia = new Texture3D(rstate, profile.NearGridX, profile.NearGridY, profile.NearGridZ, SurfaceFormat.HdrBlendable, storage: true);
            nearScatterA = new Texture3D(rstate, profile.NearGridX, profile.NearGridY, profile.NearGridZ, SurfaceFormat.HdrBlendable, storage: true);
            nearScatterB = new Texture3D(rstate, profile.NearGridX, profile.NearGridY, profile.NearGridZ, SurfaceFormat.HdrBlendable, storage: true);
            nearIntegrated = new Texture3D(rstate, profile.NearGridX, profile.NearGridY, profile.NearGridZ, SurfaceFormat.HdrBlendable, storage: true);
            FLLog.Info("Volumetrics", $"Froxel near grid {profile.NearGridX}x{profile.NearGridY}x{profile.NearGridZ}, range {NearCascadeNear}-{NearCascadeFar}m");
        }
        if (noiseBase == null || noiseDetail == null)
        {
            BakeNoise();
        }

        if (!Matrix4x4.Invert(camera.ViewProjection, out invViewProj))
        {
            return;
        }
        cameraPos = camera.Position;
        // Golden captures pin the jitter sequence (history is off there;
        // a scrolling IGN would differ between runs frame-for-frame).
        if (SiriusAutoplay.GoldenDir == null)
        {
            frameIndex++;
        }

        rstate.BeginPassTimer("vol.froxel");

        // Zone SSBO (streamed: BeginStreaming rotates the ring).
        zoneBuffer!.BeginStreaming();
        for (var i = 0; i < zoneCount; i++)
        {
            zoneBuffer.Data<NebulaVolumeData.GpuZone>(i) = zoneScratch[i];
        }
        zoneBuffer.EndStreaming(zoneCount);

        // 1: inject noise-eroded media.
        var inject = AllShaders.FroxelInject!.Get(0);
        var noiseTest = Environment.GetEnvironmentVariable("SIRIUS_VOLFOG_NOISETEST");
        var mediaProbe = noiseTest is "1" or "2" or "4" or "5" or "6" or "7" or "8" or "9";
        var injectParams = new FroxelParams
        {
            InvViewProj = invViewProj,
            CameraPosNear = new Vector4(cameraPos, MainNearPlane),
            GridSizeFar = new Vector4(profile.GridX, profile.GridY, profile.GridZ, FarPlane),
            Counts = new Vector4(zoneCount, frameIndex % 64,
                noiseTest switch
                {
                    "1" => -1f,
                    "2" => -2f,
                    "4" => -4f,
                    "5" => -5f,
                    "6" => -6f,
                    "7" => -7f,
                    "8" => -8f,
                    "9" => -9f,
                    _ => 1f
                },
                (float)(driftTime % 4096.0)),
            DetailParams = new Vector4(1f, 1f, 0f, 0f)
        };
        inject.SetUniformBlock(3, ref injectParams);
        zoneBuffer.BindTo(9);
        rstate.Textures[0] = noiseBase;
        rstate.Samplers[0] = SamplerState.LinearRepeat;
        rstate.Textures[2] = noiseDetail;
        rstate.Samplers[2] = SamplerState.LinearRepeat;
        rstate.Textures[3] = null;
        rstate.Samplers[3] = SamplerState.LinearClamp;
        rstate.SetStorageImage(4, media);
        rstate.Shader = inject;
        rstate.DispatchCompute(Groups4(profile.GridX), Groups4(profile.GridY), Groups4(profile.GridZ));
        rstate.BarrierComputeToCompute();

        // 2: in-scatter - sun with HG phase and self-shadow march (V4).
        // The sun comes from SystemRenderer.FindShadowLight: the SAME
        // source the cascade shadows use, so volumetric light always
        // agrees with surface shadowing.
        var sunDir = sunFound ? -sunToScene : new Vector3(0, 1, 0);
        var sunColor = sunFound ? sunColorIn : new Vector3(1f, 0.98f, 0.92f);
        var foundSun = sunFound;
        // Temporal history (V5): ping-pong scatter volumes; the light pass
        // blends the REPROJECTED previous frame in. Golden captures and
        // hard camera jumps (teleport/system change) run history-free.
        var scatterWrite = scatterFlip ? scatterB! : scatterA!;
        var scatterRead = scatterFlip ? scatterA! : scatterB!;
        scatterFlip = !scatterFlip;
        // SIRIUS_VOLFOG_LIVE=1: keep temporal history inside golden rigs
        // (ghosting tests need teleport + motion + history at once).
        var goldenMode = SiriusAutoplay.GoldenDir != null &&
                         Environment.GetEnvironmentVariable("SIRIUS_VOLFOG_LIVE") != "1";
        if (Vector3.DistanceSquared(cameraPos, prevCameraPos) > 4000f * 4000f)
        {
            historyValid = false;
        }
        var blend = historyValid && !goldenMode ? 0.9f : 0f;

        // Station/ship shadows in the fog (V4b): sample LAST frame's
        // cascade atlas (this frame's pass runs after the dispatches; the
        // one-frame lag is invisible on slow-moving shadows). RT shadows
        // skip the cascades entirely - the atlas would be stale garbage.
        var csm = shadowMaps != null && csmShadowsLive;

        // Dynamic lightning (V4b): the in-zone flash NebulaRenderer already
        // simulates becomes a point light INSIDE the medium - the cloud
        // body glows around the bolt. SIRIUS_VOLFOG_LIGHTNING=1 forces a
        // flash for screenshots (real gaps are 5-10s).
        var lightningPos = Vector4.Zero;
        var lightningColor = Vector4.Zero;
        // Golden rigs run lightning-free (the flash period locks to the
        // capture phase and breaks frame determinism); the env force stays.
        if (SiriusAutoplay.GoldenDir == null &&
            currentNebula != null && currentNebula.DoLightning(out var bolt))
        {
            lightningPos = new Vector4(bolt.Position.X, bolt.Position.Y, bolt.Position.Z,
                MathF.Max(bolt.ColorRange.W, 500f));
            // ~x3 over the sun term at 200m, fading fast - a flash, not a
            // second sun (900 washed the whole frame to white).
            lightningColor = new Vector4(bolt.ColorRange.X, bolt.ColorRange.Y, bolt.ColorRange.Z, 45f);
        }
        else if (Environment.GetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING") == "1")
        {
            var forced = cameraPos + new Vector3(150, 80, -700);
            lightningPos = new Vector4(forced, 1500f);
            lightningColor = new Vector4(0.55f, 0.3f, 0.9f, 45f);
        }

        var light = AllShaders.FroxelLight!.Get(0);
        var lightParams = new LightParams
        {
            InvViewProj = invViewProj,
            ViewProj = camera.ViewProjection,
            PrevViewProj = prevViewProj,
            ShadowMatrix0 = csm ? shadowMaps!.LightViewProjection[0] : Matrix4x4.Identity,
            ShadowMatrix1 = csm ? shadowMaps!.LightViewProjection[1] : Matrix4x4.Identity,
            ShadowMatrix2 = csm ? shadowMaps!.LightViewProjection[2] : Matrix4x4.Identity,
            CameraPosNear = new Vector4(cameraPos, MainNearPlane),
            GridSizeFar = new Vector4(profile.GridX, profile.GridY, profile.GridZ, FarPlane),
            // Phase-weighted: dual-HG averages ~0.08sr^-1, so the intensity
            // lands the sun term an order above the dim nebula ambient -
            // clouds get real chiaroscuro instead of flat self-glow.
            SunDirAmbient = new Vector4(sunDir, 0.025f),
            SunColorIntensity = new Vector4(sunColor,
                noiseTest == "3" ? -1f : mediaProbe ? -2f : 13f),
            Temporal = new Vector4(blend, zoneCount, (float)(driftTime % 4096.0), 0),
            ShadowSplits = csm ? shadowMaps!.CascadeSplits : Vector4.Zero,
            ShadowParams = csm
                ? new Vector4(1f, 1f / ShadowMapRenderer.Cascades, 0.0015f, 0)
                : Vector4.Zero,
            LightningPosRadius = lightningPos,
            LightningColor = lightningColor
        };
        light.SetUniformBlock(3, ref lightParams);
        rstate.Textures[0] = media;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.Textures[1] = noiseBase;
        rstate.Samplers[1] = SamplerState.LinearRepeat;
        rstate.Textures[2] = scatterRead;
        rstate.Samplers[2] = SamplerState.LinearClamp;
        rstate.Textures[3] = noiseDetail;
        rstate.Samplers[3] = SamplerState.LinearRepeat;
        rstate.Textures[6] = csm ? shadowMaps!.AtlasTexture : null;
        rstate.Samplers[6] = SamplerState.LinearClamp;
        rstate.SetStorageImage(4, scatterWrite);
        rstate.Shader = light;
        rstate.DispatchCompute(Groups4(profile.GridX), Groups4(profile.GridY), Groups4(profile.GridZ));
        rstate.BarrierComputeToCompute();

        var displacementActive = false;
        var displacementOriginExtent = Vector4.Zero;
        Texture3D? displacementField = null;
        if (NearCascadeActive && displacementObjects != null)
        {
            displacementActive = displacement.Run(rstate, cameraPos, displacementObjects, driftTime);
            if (displacementActive)
            {
                displacementOriginExtent = displacement.OriginExtent;
                displacementField = displacement.Field;
            }
        }

        if (NearCascadeActive)
        {
            rstate.BeginPassTimer("vol.near");
            // 2a: near cascade (V9) - same medium, shorter range, higher
            // detail erosion. It integrates separately, then composite blends
            // the 580-620m overlap without double-counting optical depth.
            var nearInjectParams = new FroxelParams
            {
                InvViewProj = invViewProj,
                CameraPosNear = new Vector4(cameraPos, NearCascadeNear),
                GridSizeFar = new Vector4(profile.NearGridX, profile.NearGridY, profile.NearGridZ, NearCascadeFar),
                Counts = new Vector4(zoneCount, (frameIndex + 31) % 64,
                    noiseTest switch
                    {
                        "1" => -1f,
                        "2" => -2f,
                        "4" => -4f,
                        "5" => -5f,
                        "6" => -6f,
                        "7" => -7f,
                        "8" => -8f,
                        "9" => -9f,
                        _ => 1f
                    },
                    (float)(driftTime % 4096.0)),
                // Near cascade adds fine wisps up close. 8x freq / 1.75x
                // erosion turned the immediate surroundings into TV static;
                // with edge-only erosion in NoiseDensityStagesEx a modest 3x
                // freq carves readable wisps instead of grain.
                DetailParams = new Vector4(2.2f, 0.8f, 0f, 0f),
                DisplacementOriginExtent = displacementOriginExtent,
                DisplacementParams = displacementActive ? new Vector4(1f, 1f, 25f, 0f) : Vector4.Zero
            };
            inject.SetUniformBlock(3, ref nearInjectParams);
            zoneBuffer.BindTo(9);
            rstate.Textures[0] = noiseBase;
            rstate.Samplers[0] = SamplerState.LinearRepeat;
            rstate.Textures[2] = noiseDetail;
            rstate.Samplers[2] = SamplerState.LinearRepeat;
            rstate.Textures[3] = displacementField;
            rstate.Samplers[3] = SamplerState.LinearClamp;
            rstate.SetStorageImage(4, nearMedia);
            rstate.Shader = inject;
            rstate.DispatchCompute(Groups4(profile.NearGridX), Groups4(profile.NearGridY), Groups4(profile.NearGridZ));
            rstate.BarrierComputeToCompute();

            var nearScatterWrite = nearScatterFlip ? nearScatterB! : nearScatterA!;
            var nearScatterRead = nearScatterFlip ? nearScatterA! : nearScatterB!;
            nearScatterFlip = !nearScatterFlip;
            var nearLightParams = lightParams;
            nearLightParams.CameraPosNear = new Vector4(cameraPos, NearCascadeNear);
            nearLightParams.GridSizeFar = new Vector4(profile.NearGridX, profile.NearGridY, profile.NearGridZ, NearCascadeFar);
            nearLightParams.Temporal = new Vector4(blend, zoneCount, (float)(driftTime % 4096.0), 31f);
            light.SetUniformBlock(3, ref nearLightParams);
            rstate.Textures[0] = nearMedia;
            rstate.Samplers[0] = SamplerState.LinearClamp;
            rstate.Textures[1] = noiseBase;
            rstate.Samplers[1] = SamplerState.LinearRepeat;
            rstate.Textures[2] = nearScatterRead;
            rstate.Samplers[2] = SamplerState.LinearClamp;
            rstate.Textures[3] = noiseDetail;
            rstate.Samplers[3] = SamplerState.LinearRepeat;
            rstate.Textures[6] = csm ? shadowMaps!.AtlasTexture : null;
            rstate.Samplers[6] = SamplerState.LinearClamp;
            rstate.SetStorageImage(4, nearScatterWrite);
            rstate.Shader = light;
            rstate.DispatchCompute(Groups4(profile.NearGridX), Groups4(profile.NearGridY), Groups4(profile.NearGridZ));
            rstate.BarrierComputeToCompute();

            var nearIntegrate = AllShaders.FroxelIntegrate!.Get(0);
            var nearIntegrateParams = new IntegrateParams
            {
                GridNear = new Vector4(profile.NearGridX, profile.NearGridY, profile.NearGridZ, NearCascadeNear),
                FarPad = new Vector4(NearCascadeFar, 0, 0, 0)
            };
            nearIntegrate.SetUniformBlock(3, ref nearIntegrateParams);
            rstate.Textures[0] = nearScatterWrite;
            rstate.Samplers[0] = SamplerState.PointClamp;
            rstate.SetStorageImage(4, nearIntegrated);
            rstate.Shader = nearIntegrate;
            rstate.DispatchCompute(Groups8(profile.NearGridX), Groups8(profile.NearGridY), 1);
            rstate.BarrierComputeToCompute();
            rstate.EndPassTimer();
        }
        prevViewProj = camera.ViewProjection;
        prevCameraPos = cameraPos;
        historyValid = true;

        // 2b: distant layer (V6) - half-res march beyond the froxel far.
        var dw = Math.Max(renderWidth / 2, 64);
        var dh = Math.Max(renderHeight / 2, 64);
        if (distantA == null || distantW != dw || distantH != dh)
        {
            distantA?.Dispose();
            distantB?.Dispose();
            distantA = new Texture3D(rstate, dw, dh, 1, SurfaceFormat.HdrBlendable, storage: true);
            distantB = new Texture3D(rstate, dw, dh, 1, SurfaceFormat.HdrBlendable, storage: true);
            distantW = dw;
            distantH = dh;
        }
        var distantWrite = distantFlip ? distantB! : distantA!;
        var distantRead = distantFlip ? distantA! : distantB!;
        distantFlip = !distantFlip;

        var distant = AllShaders.DistantNebula!.Get(0);
        var distantParams = new DistantParams
        {
            InvViewProj = invViewProj,
            PrevViewProj = prevViewProj,
            CameraPosStart = new Vector4(cameraPos, FarPlane),
            TargetSizeEnd = new Vector4(dw, dh, DistantFar, frameIndex % 64),
            SunDirAmbient = new Vector4(sunDir, 0.025f),
            SunColorIntensity = new Vector4(sunColor, 13f),
            ZoneCountsBlend = new Vector4(zoneCount, (float)(driftTime % 4096.0),
                historyValid && !goldenMode ? 0.85f : 0f,
                0f),
            DistantQuality = new Vector4(profile.DistantSteps, depthCopy != null ? 1f : 0f, 0f, 0f)
        };
        distant.SetUniformBlock(3, ref distantParams);
        zoneBuffer.BindTo(9);
        rstate.Textures[0] = noiseBase;
        rstate.Samplers[0] = SamplerState.LinearRepeat;
        rstate.Textures[2] = noiseDetail;
        rstate.Samplers[2] = SamplerState.LinearRepeat;
        rstate.Textures[6] = distantRead;
        rstate.Samplers[6] = SamplerState.LinearClamp;
        rstate.Textures[8] = depthCopy; // last frame's scene depth
        rstate.Samplers[8] = SamplerState.PointClamp;
        rstate.SetStorageImage(4, distantWrite);
        rstate.Shader = distant;
        rstate.DispatchCompute((uint)((dw + 7) / 8), (uint)((dh + 7) / 8), 1);
        rstate.BarrierComputeToCompute();
        distantCurrent = distantWrite;

        // 3: front-to-back integration along Z.
        var integrate = AllShaders.FroxelIntegrate!.Get(0);
        var integrateParams = new IntegrateParams
        {
            GridNear = new Vector4(profile.GridX, profile.GridY, profile.GridZ, MainNearPlane),
            FarPad = new Vector4(FarPlane, 0, 0, 0)
        };
        integrate.SetUniformBlock(3, ref integrateParams);
        rstate.Textures[0] = scatterWrite;
        rstate.Samplers[0] = SamplerState.PointClamp;
        rstate.SetStorageImage(4, integrated);
        rstate.Shader = integrate;
        rstate.DispatchCompute(Groups8(profile.GridX), Groups8(profile.GridY), 1);

        rstate.BarrierComputeToGraphics();

        rstate.EndPassTimer();

        if (!resourcesLogged)
        {
            resourcesLogged = true;
            var msg = new StringBuilder();
            msg.Append($"Active: {zoneCount} zone(s); stride={NebulaVolumeData.GpuZoneSize}/marshal={System.Runtime.InteropServices.Marshal.SizeOf<NebulaVolumeData.GpuZone>()}; ");
            msg.Append($"sun found={foundSun} dir=({sunDir.X:F2},{sunDir.Y:F2},{sunDir.Z:F2}) colour=({sunColor.X:F2},{sunColor.Y:F2},{sunColor.Z:F2})");
            for (var i = 0; i < zoneCount; i++)
            {
                var z = zoneScratch[i];
                msg.Append(
                    $" zone{i}[density={z.ColorDensity.W:F6}, noise(scale={z.NoiseProfile.X:F6}, cov={z.NoiseProfile.Y:F2}, detail={z.NoiseProfile.Z:F2}, drift={z.NoiseProfile.W:F1}), " +
                    $"shape={z.Params.X:F0}, edge={z.Params.Y:F2}, exclusion={z.Params.Z:F0}, radius={z.BoundsSphere.W:F0}]");
            }
            FLLog.Info("Volumetrics", msg.ToString());
        }
        Active = true;
        UpdateStatus(true, zoneCount);
    }

    private static float MeanExtinction(NebulaVolumeData.GpuZone[] zones, int zoneCount)
    {
        for (var i = 0; i < zoneCount; i++)
        {
            var z = zones[i];
            if (z.Params.Z > 0.5f)
            {
                continue;
            }
            return z.ColorDensity.W * (z.NoiseProfile.Y * 0.4f + 0.05f);
        }
        return 0f;
    }

    /// <summary>Bakes the tiling Perlin-Worley volumes (once, ~1ms).</summary>
    private void BakeNoise()
    {
        noiseBase = new Texture3D(rstate, 128, 128, 128, SurfaceFormat.HdrBlendable, storage: true);
        noiseDetail = new Texture3D(rstate, 32, 32, 32, SurfaceFormat.HdrBlendable, storage: true);
        rstate.BeginPassTimer("vol.noisegen");
        var gen = AllShaders.NoiseGen!.Get(0);

        var baseParams = new NoiseGenParams { SizeMode = new Vector4(128, 128, 128, 0) };
        gen.SetUniformBlock(3, ref baseParams);
        rstate.SetStorageImage(4, noiseBase);
        rstate.Shader = gen;
        rstate.DispatchCompute(32, 32, 32);

        var detailParams = new NoiseGenParams { SizeMode = new Vector4(32, 32, 32, 1) };
        gen.SetUniformBlock(3, ref detailParams);
        rstate.SetStorageImage(4, noiseDetail);
        rstate.Shader = gen;
        rstate.DispatchCompute(8, 8, 8);

        rstate.BarrierComputeToCompute();
        rstate.EndPassTimer();
        FLLog.Info("Volumetrics", "Noise volumes baked: base 128^3 Perlin-Worley, detail 32^3 Worley");
    }

    private const ushort BlendPremultiplied =
        ((ushort)BlendOp.One << 8) | (ushort)BlendOp.SrcAlpha;

    /// <summary>
    /// Composites the integrated volume over the scene. Call after the
    /// starsphere, before the transparent pass, with the scene target
    /// still bound. Copies the target's depth for ray reconstruction.
    /// </summary>
    public void Composite(RenderTarget2D sceneTarget, int width, int height)
    {
        if (!Active)
        {
            return;
        }
        rstate.BeginPassTimer("vol.composite");

        if (depthCopy == null || depthCopy.Width != width || depthCopy.Height != height)
        {
            depthCopy?.Dispose();
            depthCopy = new Texture2D(rstate, width, height, false, SurfaceFormat.Depth);
        }
        rstate.CopyDepth(sceneTarget, depthCopy);

        var shader = AllShaders.VolFogComposite!.Get(0);
        var parameters = new CompositeParams
        {
            InvViewProj = invViewProj,
            CameraPosNear = new Vector4(cameraPos, MainNearPlane),
            GridSizeFar = new Vector4(profile.GridX, profile.GridY, profile.GridZ, FarPlane),
            NearGridSizeFar = new Vector4(profile.NearGridX, profile.NearGridY, profile.NearGridZ, NearCascadeFar),
            CascadeBlend = new Vector4(NearCascadeNear, MainNearPlane,
                NearCascadeActive && nearIntegrated != null ? 1f : 0f, 0f),
            DebugMode = new Vector4(debugDepth ? 2 : debugZones ? 1 : 0,
                distantCurrent != null ? 1 : 0, profile.TransmittanceFloor, 0)
        };
        shader.SetUniformBlock(3, ref parameters);
        rstate.Cull = false;
        rstate.DepthEnabled = false;
        rstate.BlendMode = debugZones || debugDepth ? BlendMode.Opaque : BlendPremultiplied;
        rstate.Textures[0] = integrated;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.Textures[1] = depthCopy;
        rstate.Samplers[1] = SamplerState.PointClamp;
        rstate.Textures[2] = nearIntegrated;
        rstate.Samplers[2] = SamplerState.LinearClamp;
        rstate.Textures[3] = distantCurrent;
        rstate.Samplers[3] = SamplerState.LinearClamp;
        rstate.Shader = shader;
        rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);

        if ((debugZones || (debugDisplacement && displacement.Field != null)) &&
            AllShaders.Texture3DVis != null)
        {
            // Corner inset: raw base-noise slice or displacement push/swirl.
            var vis = AllShaders.Texture3DVis.Get(0);
            var visParams = new Vector4(0.5f, 1.0f, 0, 0);
            vis.SetUniformBlock(3, ref visParams);
            rstate.BlendMode = BlendMode.Opaque;
            rstate.Textures[0] = debugDisplacement ? displacement.Field : noiseBase;
            rstate.Samplers[0] = debugDisplacement ? SamplerState.LinearClamp : SamplerState.LinearRepeat;
            rstate.PushViewport(16, 90, 420, 420);
            rstate.Shader = vis;
            rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
            rstate.PopViewport();
        }

        rstate.Cull = true;
        rstate.DepthEnabled = true;

        rstate.EndPassTimer();
    }

    public void Dispose()
    {
        DisposeVolumeResources();
        displacement.Dispose();
        noiseBase?.Dispose();
        noiseDetail?.Dispose();
        zoneBuffer?.Dispose();
    }
}
