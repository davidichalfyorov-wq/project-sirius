using System;
using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Data.GameData;
using LibreLancer.Graphics;
using LibreLancer.Shaders;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Froxel volumetric nebulae (phase 5, track V): camera-aligned 3D grids
/// filled by compute (inject -> light -> integrate) and composited over
/// the scene before the transparent pass. Vulkan-only behind
/// GraphicsFeature.Compute; the legacy NebulaRenderer remains the
/// fallback and keeps running for puffs until V6/V8 retire them.
/// </summary>
public class VolumetricFog : IDisposable
{
    // V2 baseline grid (the "high" preset of V11). ~5.5M half-float texels
    // per volume = 11 MB x3, written by compute every frame.
    private const int GridX = 160;
    private const int GridY = 90;
    private const int GridZ = 96;
    private const float NearPlane = 8f;
    private const float FarPlane = 14000f;

    // SIRIUS_VOLFOG: 1 forces on, 0 forces off (overrides the INI).
    private static readonly string? envOverride =
        Environment.GetEnvironmentVariable("SIRIUS_VOLFOG");

    private static readonly bool debugZones =
        Environment.GetEnvironmentVariable("SIRIUS_DEBUG_VIEW") == "volzones";
    private static readonly bool debugDepth =
        Environment.GetEnvironmentVariable("SIRIUS_DEBUG_VIEW") == "voldepth";

    private readonly RenderContext rstate;
    private Texture3D? media;
    private Texture3D? scatterA;
    private Texture3D? scatterB;
    private bool scatterFlip;
    private Texture3D? integrated;
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
    private Matrix4x4 prevViewProj;
    private Vector3 prevCameraPos;
    private bool historyValid;
    private Texture3D? distantCurrent;
    private readonly NebulaVolumeData.GpuZone[] zoneScratch =
        new NebulaVolumeData.GpuZone[NebulaVolumeData.MaxZones];
    private uint frameIndex;
    private bool resourcesLogged;
    private bool gateLogged;

    /// <summary>INI/settings master switch (volumetric_nebulae).</summary>
    public bool Enabled;

    /// <summary>True when this frame actually built and wants composite.</summary>
    public bool Active { get; private set; }

    public VolumetricFog(RenderContext rstate)
    {
        this.rstate = rstate;
    }

    private bool FeatureAvailable =>
        envOverride != "0" &&
        (Enabled || envOverride == "1") &&
        rstate.HasFeature(GraphicsFeature.Compute) &&
        AllShaders.FroxelInject != null;

    private struct FroxelParams
    {
        public Matrix4x4 InvViewProj;
        public Vector4 CameraPosNear;
        public Vector4 GridSizeFar;
        public Vector4 Counts;
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
        public Vector4 DebugMode;
    }

    private Matrix4x4 invViewProj;
    private Vector3 cameraPos;

    /// <summary>
    /// Runs the froxel compute chain. Call once per frame after the camera
    /// is set and BEFORE the first scene draw (dispatches end any active
    /// render pass). No-ops (Active=false) when the feature is off or no
    /// nebula volume is near the camera.
    /// </summary>
    public void RunDispatches(ICamera camera, List<NebulaRenderer> nebulae, double driftTime,
        Vector3 sunToScene, Vector3 sunColorIn, bool sunFound,
        ShadowMapRenderer? shadowMaps, bool csmShadowsLive, NebulaRenderer? currentNebula,
        int renderWidth, int renderHeight)
    {
        Active = false;
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

        if (media == null)
        {
            media = new Texture3D(rstate, GridX, GridY, GridZ, SurfaceFormat.HdrBlendable, storage: true);
            scatterA = new Texture3D(rstate, GridX, GridY, GridZ, SurfaceFormat.HdrBlendable, storage: true);
            scatterB = new Texture3D(rstate, GridX, GridY, GridZ, SurfaceFormat.HdrBlendable, storage: true);
            integrated = new Texture3D(rstate, GridX, GridY, GridZ, SurfaceFormat.HdrBlendable, storage: true);
            zoneBuffer = new StorageBuffer(rstate,
                NebulaVolumeData.MaxZones * NebulaVolumeData.GpuZoneSize, NebulaVolumeData.GpuZoneSize);
            FLLog.Info("Volumetrics", $"Froxel grid {GridX}x{GridY}x{GridZ}, range {NearPlane}-{FarPlane}m");
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
        var injectParams = new FroxelParams
        {
            InvViewProj = invViewProj,
            CameraPosNear = new Vector4(cameraPos, NearPlane),
            GridSizeFar = new Vector4(GridX, GridY, GridZ, FarPlane),
            Counts = new Vector4(zoneCount, frameIndex % 64,
                Environment.GetEnvironmentVariable("SIRIUS_VOLFOG_NOISETEST") switch
                {
                    "1" => -1f,
                    "2" => -2f,
                    _ => 1f
                },
                (float)(driftTime % 4096.0))
        };
        inject.SetUniformBlock(3, ref injectParams);
        zoneBuffer.BindTo(9);
        rstate.Textures[0] = noiseBase;
        rstate.Samplers[0] = SamplerState.LinearRepeat;
        rstate.Textures[2] = noiseDetail;
        rstate.Samplers[2] = SamplerState.LinearRepeat;
        rstate.SetStorageImage(4, media);
        rstate.Shader = inject;
        rstate.DispatchCompute((GridX + 3) / 4, (GridY + 3) / 4, (GridZ + 3) / 4);
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
            CameraPosNear = new Vector4(cameraPos, NearPlane),
            GridSizeFar = new Vector4(GridX, GridY, GridZ, FarPlane),
            // Phase-weighted: dual-HG averages ~0.08sr^-1, so the intensity
            // lands the sun term an order above the dim nebula ambient -
            // clouds get real chiaroscuro instead of flat self-glow.
            SunDirAmbient = new Vector4(sunDir, 0.07f),
            SunColorIntensity = new Vector4(sunColor,
                Environment.GetEnvironmentVariable("SIRIUS_VOLFOG_NOISETEST") == "3" ? -1f : 32f),
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
        rstate.DispatchCompute((GridX + 3) / 4, (GridY + 3) / 4, (GridZ + 3) / 4);
        rstate.BarrierComputeToCompute();
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
            SunDirAmbient = new Vector4(sunDir, 0.07f),
            SunColorIntensity = new Vector4(sunColor, 32f),
            ZoneCountsBlend = new Vector4(zoneCount, (float)(driftTime % 4096.0),
                historyValid && !goldenMode ? 0.85f : 0f,
                depthCopy != null ? 1f : 0f)
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
            GridNear = new Vector4(GridX, GridY, GridZ, NearPlane),
            FarPad = new Vector4(FarPlane, 0, 0, 0)
        };
        integrate.SetUniformBlock(3, ref integrateParams);
        rstate.Textures[0] = scatterWrite;
        rstate.Samplers[0] = SamplerState.PointClamp;
        rstate.SetStorageImage(4, integrated);
        rstate.Shader = integrate;
        rstate.DispatchCompute((GridX + 7) / 8, (GridY + 7) / 8, 1);
        rstate.BarrierComputeToGraphics();

        rstate.EndPassTimer();

        if (!resourcesLogged)
        {
            resourcesLogged = true;
            var z0 = zoneScratch[0];
            FLLog.Info("Volumetrics",
                $"Active: {zoneCount} zone(s); zone0 density={z0.ColorDensity.W:F6} noise(scale={z0.NoiseProfile.X:F6}, cov={z0.NoiseProfile.Y:F2}, detail={z0.NoiseProfile.Z:F2}, drift={z0.NoiseProfile.W:F1}); " +
                $"sun found={foundSun} dir=({sunDir.X:F2},{sunDir.Y:F2},{sunDir.Z:F2}) colour=({sunColor.X:F2},{sunColor.Y:F2},{sunColor.Z:F2})");
        }
        Active = true;
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
            CameraPosNear = new Vector4(cameraPos, NearPlane),
            GridSizeFar = new Vector4(GridX, GridY, GridZ, FarPlane),
            DebugMode = new Vector4(debugDepth ? 2 : debugZones ? 1 : 0,
                distantCurrent != null ? 1 : 0, 0, 0)
        };
        shader.SetUniformBlock(3, ref parameters);
        rstate.Cull = false;
        rstate.DepthEnabled = false;
        rstate.BlendMode = debugZones || debugDepth ? BlendMode.Opaque : BlendPremultiplied;
        rstate.Textures[0] = integrated;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.Textures[1] = depthCopy;
        rstate.Samplers[1] = SamplerState.PointClamp;
        rstate.Textures[3] = distantCurrent;
        rstate.Samplers[3] = SamplerState.LinearClamp;
        rstate.Shader = shader;
        rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);

        if (debugZones && AllShaders.Texture3DVis != null)
        {
            // Corner inset: raw base-noise slice (R = Perlin-Worley shape).
            var vis = AllShaders.Texture3DVis.Get(0);
            var visParams = new Vector4(0.5f, 1.0f, 0, 0);
            vis.SetUniformBlock(3, ref visParams);
            rstate.BlendMode = BlendMode.Opaque;
            rstate.Textures[0] = noiseBase;
            rstate.Samplers[0] = SamplerState.LinearRepeat;
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
        media?.Dispose();
        scatterA?.Dispose();
        scatterB?.Dispose();
        integrated?.Dispose();
        noiseBase?.Dispose();
        noiseDetail?.Dispose();
        distantA?.Dispose();
        distantB?.Dispose();
        zoneBuffer?.Dispose();
        depthCopy?.Dispose();
    }
}
