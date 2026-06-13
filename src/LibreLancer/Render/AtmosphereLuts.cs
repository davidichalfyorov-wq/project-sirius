using System;
using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Graphics;
using LibreLancer.Render.Materials;
using LibreLancer.Resources;
using LibreLancer.Shaders;
using LibreLancer.World;

namespace LibreLancer.Render;

/// <summary>
/// Phase 5 / W1 atmosphere LUT foundation. Bakes the transmittance and
/// multiple-scattering lookup textures once per loaded system; the visible
/// atmosphere shader samples them when compute support is available.
/// </summary>
internal sealed class AtmosphereLuts : IDisposable
{
    private const int TransmittanceWidth = 256;
    private const int TransmittanceHeight = 64;
    private const int MultiScatteringSize = 32;
    private const int SkyViewWidth = 192;
    private const int SkyViewHeight = 108;
    private const int AerialGridX = 32;
    private const int AerialGridY = 32;
    private const int AerialGridZ = 32;
    private const int CloudNoiseBaseSize = 128;
    private const int CloudNoiseDetailSize = 32;
    private const int CloudSteps = 10;

    public static bool DebugEnabled =>
        Environment.GetEnvironmentVariable("SIRIUS_DEBUG_VIEW") == "atmolut";
    private static bool DebugAerial =>
        Environment.GetEnvironmentVariable("SIRIUS_DEBUG_VIEW") == "atmoaerial";
    private static bool DebugClouds =>
        Environment.GetEnvironmentVariable("SIRIUS_DEBUG_VIEW") == "atmoclouds";
    private static readonly bool AerialEnabled =
        Environment.GetEnvironmentVariable("SIRIUS_ATMO_AERIAL") != "0";
    private static readonly bool CloudsEnabled =
        Environment.GetEnvironmentVariable("SIRIUS_ATMO_CLOUDS") != "0";

    private Texture3D? transmittance;
    private Texture3D? multiScattering;
    private Texture3D? skyView;
    private Texture3D? aerial;
    private Texture3D? cloudNoiseBase;
    private Texture3D? cloudNoiseDetail;
    private Texture3D? cloudsA;
    private Texture3D? cloudsB;
    private Texture3D? cloudsCurrent;
    private AtmosphereKey currentKey;
    private bool hasCurrentKey;
    private bool baked;
    private bool unsupportedLogged;
    private bool aerialUnsupportedLogged;
    private bool cloudUnsupportedLogged;
    private bool resourcesLogged;
    private bool aerialResourcesLogged;
    private bool cloudResourcesLogged;
    private bool cloudNoiseLogged;
    private bool debugLogged;
    private bool aerialLogged;
    private bool cloudLogged;
    private bool cloudFlip;
    private bool cloudHistoryValid;
    private Matrix4x4 cloudPrevViewProj;
    private Vector3 cloudPrevCameraPos;
    private int cloudW;
    private int cloudH;

    private struct LutParams
    {
        public Vector4 SizeMode;
        public Vector4 RayleighMie;
        public Vector4 AbsorptionAlbedo;
    }

    private struct DebugParams
    {
        public Vector4 GainMode;
    }

    private struct CloudCompositeParams
    {
        public Vector4 DebugMode;
    }

    private struct SkyViewParams
    {
        public Vector4 SizeMode;
        public Vector4 SunDirIntensity;
        public Vector4 RayleighMie;
        public Vector4 AbsorptionAlbedo;
    }

    private struct AerialParams
    {
        public Matrix4x4 InvViewProj;
        public Vector4 CameraPosPlanetRadius;
        public Vector4 PlanetCenterShell;
        public Vector4 GridMaxDistance;
        public Vector4 SunDirIntensity;
        public Vector4 RayleighMie;
        public Vector4 AbsorptionAlbedo;
    }

    private struct NoiseGenParams
    {
        public Vector4 SizeMode;
    }

    private struct CloudParams
    {
        public Matrix4x4 InvViewProj;
        public Matrix4x4 PrevViewProj;
        public Vector4 CameraPosPlanetRadius;
        public Vector4 PlanetCenterShell;
        public Vector4 TargetSizeLayer;
        public Vector4 SunDirBlend;
        public Vector4 SunColorTime;
        public Vector4 CloudShape;
    }

    private readonly record struct AtmosphereKey(Color4 Dc, Color4 Ac, float Alpha, float Fade, float Scale);

    private readonly record struct AtmosphereState(
        AtmosphereKey Key,
        Vector3 Center,
        float PlanetRadius,
        float ShellRadius);

    private static readonly AtmosphereKey DefaultKey =
        new(Color4.White, Color4.White, 1f, 1f, 1f);

    public Texture3D? Transmittance => baked ? transmittance : null;
    public Texture3D? MultiScattering => baked ? multiScattering : null;
    public Texture3D? AerialTexture => AerialActive ? aerial : null;
    public Vector4 AerialSettings { get; private set; }
    public bool AerialActive { get; private set; }
    public Texture3D? CloudTexture => CloudsActive ? cloudsCurrent : null;
    public Vector4 CloudSettings { get; private set; }
    public bool CloudsActive { get; private set; }

    public void Invalidate()
    {
        baked = false;
        hasCurrentKey = false;
        resourcesLogged = false;
    }

    public void Ensure(RenderContext rstate, IReadOnlyList<GameObject> objects, ResourceManager resources)
    {
        var state = FindAtmosphereState(objects, resources, null);
        var key = state?.Key;
        if (key == null)
        {
            if (!DebugEnabled)
            {
                return;
            }
            key = DefaultKey;
        }

        if (baked && hasCurrentKey && currentKey.Equals(key.Value))
        {
            return;
        }

        if (!rstate.HasFeature(GraphicsFeature.Compute) ||
            AllShaders.AtmoTransmittance == null ||
            AllShaders.AtmoMultiScattering == null)
        {
            if (!unsupportedLogged)
            {
                FLLog.Info("Atmosphere", "Atmosphere LUT bake skipped: compute shaders unavailable");
                unsupportedLogged = true;
            }
            return;
        }

        currentKey = key.Value;
        hasCurrentKey = true;
        baked = false;
        EnsureResources(rstate);

        rstate.BeginPassTimer("atmo.lut");

        var trans = AllShaders.AtmoTransmittance.Get(0);
        var transParams = BuildParams(currentKey, TransmittanceWidth, TransmittanceHeight);
        trans.SetUniformBlock(3, ref transParams);
        rstate.SetStorageImage(4, transmittance);
        rstate.Shader = trans;
        rstate.DispatchCompute(TransmittanceWidth / 8u, TransmittanceHeight / 8u, 1);
        rstate.BarrierComputeToCompute();

        var multi = AllShaders.AtmoMultiScattering.Get(0);
        var multiParams = transParams;
        multiParams.SizeMode = new Vector4(MultiScatteringSize, MultiScatteringSize,
            transParams.SizeMode.Z, transParams.SizeMode.W);
        multi.SetUniformBlock(3, ref multiParams);
        rstate.Textures[0] = transmittance;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.SetStorageImage(4, multiScattering);
        rstate.Shader = multi;
        rstate.DispatchCompute(MultiScatteringSize / 8u, MultiScatteringSize / 8u, 1);
        rstate.BarrierComputeToGraphics();
        rstate.EndPassTimer();

        baked = true;
        FLLog.Info("Atmosphere",
            $"LUTs baked: transmittance {TransmittanceWidth}x{TransmittanceHeight}, multi-scattering {MultiScatteringSize}x{MultiScatteringSize}, scale={currentKey.Scale:F3}, fade={currentKey.Fade:F3}");
    }

    public void RunAerial(RenderContext rstate, ICamera camera, IReadOnlyList<GameObject> objects,
        ResourceManager resources, Vector3 sunDirection, Vector3 sunColor)
    {
        AerialActive = false;
        AerialSettings = Vector4.Zero;
        if (!AerialEnabled)
        {
            return;
        }

        var state = FindAtmosphereState(objects, resources, camera.Position);
        if (state == null)
        {
            return;
        }

        var activeRadius = state.Value.ShellRadius * 1.5f;
        var cameraDistance = Vector3.Distance(camera.Position, state.Value.Center);
        if (cameraDistance > activeRadius)
        {
            return;
        }

        Ensure(rstate, objects, resources);
        if (!baked || transmittance == null || multiScattering == null)
        {
            return;
        }
        if (!rstate.HasFeature(GraphicsFeature.Compute) ||
            AllShaders.AtmoSkyView == null ||
            AllShaders.AtmoAerial == null)
        {
            if (!aerialUnsupportedLogged)
            {
                FLLog.Info("Atmosphere", "Aerial perspective skipped: compute shaders unavailable");
                aerialUnsupportedLogged = true;
            }
            return;
        }
        if (!Matrix4x4.Invert(camera.ViewProjection, out var invViewProj))
        {
            return;
        }

        EnsureAerialResources(rstate);
        var lutParams = BuildParams(state.Value.Key, SkyViewWidth, SkyViewHeight);
        var sunDir = sunDirection.LengthSquared() > 0.0001f
            ? Vector3.Normalize(sunDirection)
            : Vector3.UnitY;
        var sunIntensity = Math.Clamp(MathF.Max(sunColor.X, MathF.Max(sunColor.Y, sunColor.Z)), 0.35f, 2.5f);
        var maxDistance = Math.Clamp(activeRadius, 1500f, 60000f);

        rstate.BeginPassTimer("atmo.aerial");

        var sky = AllShaders.AtmoSkyView.Get(0);
        var skyParams = new SkyViewParams
        {
            SizeMode = lutParams.SizeMode,
            SunDirIntensity = new Vector4(sunDir, sunIntensity),
            RayleighMie = lutParams.RayleighMie,
            AbsorptionAlbedo = lutParams.AbsorptionAlbedo
        };
        sky.SetUniformBlock(3, ref skyParams);
        rstate.Textures[0] = transmittance;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.Textures[1] = multiScattering;
        rstate.Samplers[1] = SamplerState.LinearClamp;
        rstate.SetStorageImage(4, skyView);
        rstate.Shader = sky;
        rstate.DispatchCompute((uint)((SkyViewWidth + 7) / 8), (uint)((SkyViewHeight + 7) / 8), 1);
        rstate.BarrierComputeToCompute();

        var aerialShader = AllShaders.AtmoAerial.Get(0);
        var aerialParams = new AerialParams
        {
            InvViewProj = invViewProj,
            CameraPosPlanetRadius = new Vector4(camera.Position, state.Value.PlanetRadius),
            PlanetCenterShell = new Vector4(state.Value.Center, state.Value.ShellRadius),
            GridMaxDistance = new Vector4(AerialGridX, AerialGridY, AerialGridZ, maxDistance),
            SunDirIntensity = new Vector4(sunDir, sunIntensity),
            RayleighMie = lutParams.RayleighMie,
            AbsorptionAlbedo = lutParams.AbsorptionAlbedo
        };
        aerialShader.SetUniformBlock(3, ref aerialParams);
        rstate.Textures[0] = transmittance;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.Textures[1] = multiScattering;
        rstate.Samplers[1] = SamplerState.LinearClamp;
        rstate.Textures[2] = skyView;
        rstate.Samplers[2] = SamplerState.LinearClamp;
        rstate.SetStorageImage(4, aerial);
        rstate.Shader = aerialShader;
        rstate.DispatchCompute(AerialGridX / 4u, AerialGridY / 4u, AerialGridZ / 4u);
        rstate.BarrierComputeToGraphics();

        rstate.EndPassTimer();

        AerialActive = true;
        AerialSettings = new Vector4(1f, maxDistance, AerialGridZ, 0f);
        if (!aerialLogged)
        {
            FLLog.Info("Atmosphere",
                $"Aerial perspective active: sky {SkyViewWidth}x{SkyViewHeight}, froxel {AerialGridX}x{AerialGridY}x{AerialGridZ}, cameraDist={cameraDistance:F0}, shell={state.Value.ShellRadius:F0}, maxDist={maxDistance:F0}");
            aerialLogged = true;
        }
    }

    public void RunClouds(RenderContext rstate, ICamera camera, IReadOnlyList<GameObject> objects,
        ResourceManager resources, Vector3 sunDirection, Vector3 sunColor, double driftTime,
        int renderWidth, int renderHeight)
    {
        CloudsActive = false;
        CloudSettings = Vector4.Zero;
        cloudsCurrent = null;
        if (!CloudsEnabled)
        {
            return;
        }

        var state = FindAtmosphereState(objects, resources, camera.Position, includeHidden: true);
        if (state == null)
        {
            return;
        }

        var activeRadius = state.Value.ShellRadius * 1.5f;
        var cameraDistance = Vector3.Distance(camera.Position, state.Value.Center);
        if (cameraDistance > activeRadius)
        {
            cloudHistoryValid = false;
            return;
        }

        if (!rstate.HasFeature(GraphicsFeature.Compute) ||
            AllShaders.AtmoClouds == null ||
            AllShaders.NoiseGen == null)
        {
            if (!cloudUnsupportedLogged)
            {
                FLLog.Info("Atmosphere", "Cloud shell skipped: compute shaders unavailable");
                cloudUnsupportedLogged = true;
            }
            return;
        }
        if (!Matrix4x4.Invert(camera.ViewProjection, out var invViewProj))
        {
            return;
        }

        EnsureCloudResources(rstate, renderWidth, renderHeight);
        BakeCloudNoise(rstate);

        var sunDir = sunDirection.LengthSquared() > 0.0001f
            ? Vector3.Normalize(sunDirection)
            : Vector3.UnitY;
        var sunIntensity = Math.Clamp(MathF.Max(sunColor.X, MathF.Max(sunColor.Y, sunColor.Z)), 0.35f, 2.5f);
        var sunColour = sunColor.LengthSquared() > 0.0001f
            ? sunColor / MathF.Max(MathF.Max(sunColor.X, MathF.Max(sunColor.Y, sunColor.Z)), 0.001f)
            : new Vector3(1f, 0.98f, 0.92f);

        var shell = state.Value.ShellRadius;
        var planet = state.Value.PlanetRadius;
        var thickness = MathF.Max(shell - planet, 1f);
        var cloudInner = planet + thickness * 0.38f;
        var cloudOuter = planet + thickness * 0.68f;
        var layerWidth = MathF.Max(cloudOuter - cloudInner, 1f);
        var densityScale = Math.Clamp(0.0012f * MathF.Max(180f / layerWidth, 0.70f),
            0.0007f, 0.0025f);
        var coverage = Math.Clamp(0.12f - MathF.Min(thickness / MathF.Max(planet, 1f), 0.08f) * 0.45f,
            0.04f, 0.18f);

        if (Vector3.DistanceSquared(camera.Position, cloudPrevCameraPos) > 4000f * 4000f)
        {
            cloudHistoryValid = false;
        }
        var goldenMode = SiriusAutoplay.GoldenDir != null;
        var historyBlend = cloudHistoryValid && !goldenMode ? 0.82f : 0f;

        var write = cloudFlip ? cloudsB! : cloudsA!;
        var read = cloudFlip ? cloudsA! : cloudsB!;
        cloudFlip = !cloudFlip;

        rstate.BeginPassTimer("atmo.clouds");
        var shader = AllShaders.AtmoClouds.Get(0);
        var p = new CloudParams
        {
            InvViewProj = invViewProj,
            PrevViewProj = cloudPrevViewProj,
            CameraPosPlanetRadius = new Vector4(camera.Position, planet),
            PlanetCenterShell = new Vector4(state.Value.Center, shell),
            TargetSizeLayer = new Vector4(cloudW, cloudH, rstate.FrameNumber % 64, CloudSteps),
            SunDirBlend = new Vector4(sunDir, historyBlend),
            SunColorTime = new Vector4(sunColour * sunIntensity, (float)(driftTime % 4096.0)),
            CloudShape = new Vector4(cloudInner, cloudOuter, densityScale, DebugClouds ? -coverage : coverage)
        };
        shader.SetUniformBlock(3, ref p);
        rstate.Textures[0] = cloudNoiseBase;
        rstate.Samplers[0] = SamplerState.LinearRepeat;
        rstate.Textures[1] = cloudNoiseDetail;
        rstate.Samplers[1] = SamplerState.LinearRepeat;
        rstate.Textures[6] = read;
        rstate.Samplers[6] = SamplerState.LinearClamp;
        rstate.SetStorageImage(4, write);
        rstate.Shader = shader;
        rstate.DispatchCompute((uint)((cloudW + 7) / 8), (uint)((cloudH + 7) / 8), 1);
        rstate.BarrierComputeToGraphics();
        rstate.EndPassTimer();

        cloudsCurrent = write;
        CloudsActive = true;
        CloudSettings = new Vector4(1f, cloudW, cloudH, DebugClouds ? 1f : 0f);
        cloudPrevViewProj = camera.ViewProjection;
        cloudPrevCameraPos = camera.Position;
        cloudHistoryValid = true;

        if (!cloudLogged)
        {
            FLLog.Info("Atmosphere",
                $"Cloud shell active: target {cloudW}x{cloudH}, steps={CloudSteps}, layer={cloudInner:F0}-{cloudOuter:F0}, cameraDist={cameraDistance:F0}, shell={shell:F0}");
            cloudLogged = true;
        }
    }

    public void CompositeClouds(RenderContext rstate)
    {
        if (!CloudsActive || cloudsCurrent == null || AllShaders.AtmoCloudComposite == null)
        {
            return;
        }

        rstate.BeginPassTimer("atmo.cloudComposite");
        var shader = AllShaders.AtmoCloudComposite.Get(0);
        var p = new CloudCompositeParams
        {
            DebugMode = new Vector4(DebugClouds ? 1f : 0f, 0f, 0f, 0f)
        };
        shader.SetUniformBlock(3, ref p);
        rstate.Cull = false;
        rstate.DepthEnabled = false;
        rstate.BlendMode = BlendMode.Normal;
        rstate.Textures[0] = cloudsCurrent;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.Shader = shader;
        rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
        rstate.Cull = true;
        rstate.DepthEnabled = true;
        rstate.EndPassTimer();
    }

    public void DrawDebug(RenderContext rstate, int renderWidth, int renderHeight)
    {
        if (DebugAerial)
        {
            DrawAerialDebug(rstate, renderWidth, renderHeight);
            return;
        }
        if (!DebugEnabled || !baked || AllShaders.AtmoLutDebug == null)
        {
            return;
        }

        var availableWidth = Math.Max(64, renderWidth - 32);
        var transW = Math.Min(512, availableWidth);
        var transH = Math.Max(32, transW / 4);
        var msSize = Math.Min(256, Math.Max(96, Math.Min(availableWidth, renderHeight - transH - 48)));
        if (msSize <= 0)
        {
            return;
        }

        var shader = AllShaders.AtmoLutDebug.Get(0);
        var x = Math.Min(160, Math.Max(16, renderWidth - transW - 16));
        var transY = renderHeight - 16 - transH;
        var msY = transY - 8 - msSize;
        if (msY < 16)
        {
            return;
        }

        rstate.Cull = false;
        rstate.DepthEnabled = false;
        rstate.BlendMode = BlendMode.Opaque;
        rstate.Samplers[0] = SamplerState.LinearClamp;

        var debugParams = new DebugParams { GainMode = new Vector4(1.0f, 0, 0, 0) };
        shader.SetUniformBlock(3, ref debugParams);
        rstate.Textures[0] = transmittance;
        rstate.PushViewport(x, transY, transW, transH);
        rstate.Shader = shader;
        rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
        rstate.PopViewport();

        debugParams.GainMode = new Vector4(3.0f, 1, 0, 0);
        shader.SetUniformBlock(3, ref debugParams);
        rstate.Textures[0] = multiScattering;
        rstate.PushViewport(x, msY, msSize, msSize);
        rstate.Shader = shader;
        rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
        rstate.PopViewport();

        rstate.Cull = true;
        rstate.DepthEnabled = true;
        if (!debugLogged)
        {
            FLLog.Info("Atmosphere", "LUT debug contact sheet submitted");
            debugLogged = true;
        }
    }

    private void DrawAerialDebug(RenderContext rstate, int renderWidth, int renderHeight)
    {
        if (!AerialActive || skyView == null || aerial == null ||
            AllShaders.AtmoLutDebug == null || AllShaders.Texture3DVis == null)
        {
            return;
        }

        var skyShader = AllShaders.AtmoLutDebug.Get(0);
        var x = Math.Min(160, Math.Max(16, renderWidth - 512 - 16));
        var skyW = Math.Min(512, Math.Max(128, renderWidth - x - 16));
        var skyH = Math.Max(64, skyW * SkyViewHeight / SkyViewWidth);
        var aerialSize = Math.Min(320, Math.Max(96, renderHeight - skyH - 48));
        var skyY = renderHeight - 16 - skyH;
        var aerialY = skyY - 8 - aerialSize;
        if (aerialY < 16)
        {
            return;
        }

        rstate.Cull = false;
        rstate.DepthEnabled = false;
        rstate.BlendMode = BlendMode.Opaque;
        rstate.Samplers[0] = SamplerState.LinearClamp;

        var debugParams = new DebugParams { GainMode = new Vector4(1.0f, 0, 0, 0) };
        skyShader.SetUniformBlock(3, ref debugParams);
        rstate.Textures[0] = skyView;
        rstate.PushViewport(x, skyY, skyW, skyH);
        rstate.Shader = skyShader;
        rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
        rstate.PopViewport();

        var vis = AllShaders.Texture3DVis.Get(0);
        var visParams = new Vector4(0.35f, 2.0f, 0, 0);
        vis.SetUniformBlock(3, ref visParams);
        rstate.Textures[0] = aerial;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.PushViewport(x, aerialY, aerialSize, aerialSize);
        rstate.Shader = vis;
        rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
        rstate.PopViewport();

        rstate.Cull = true;
        rstate.DepthEnabled = true;
    }

    private void EnsureResources(RenderContext rstate)
    {
        if (transmittance != null && multiScattering != null)
        {
            return;
        }

        transmittance?.Dispose();
        multiScattering?.Dispose();
        transmittance = new Texture3D(rstate, TransmittanceWidth, TransmittanceHeight, 1,
            SurfaceFormat.HdrBlendable, storage: true);
        multiScattering = new Texture3D(rstate, MultiScatteringSize, MultiScatteringSize, 1,
            SurfaceFormat.HdrBlendable, storage: true);
        if (!resourcesLogged)
        {
            FLLog.Info("Atmosphere",
                $"LUT storage allocated: {TransmittanceWidth}x{TransmittanceHeight} + {MultiScatteringSize}x{MultiScatteringSize}");
            resourcesLogged = true;
        }
    }

    private void EnsureAerialResources(RenderContext rstate)
    {
        if (skyView != null && aerial != null)
        {
            return;
        }

        skyView?.Dispose();
        aerial?.Dispose();
        skyView = new Texture3D(rstate, SkyViewWidth, SkyViewHeight, 1,
            SurfaceFormat.HdrBlendable, storage: true);
        aerial = new Texture3D(rstate, AerialGridX, AerialGridY, AerialGridZ,
            SurfaceFormat.HdrBlendable, storage: true);
        if (!aerialResourcesLogged)
        {
            FLLog.Info("Atmosphere",
                $"Aerial storage allocated: sky {SkyViewWidth}x{SkyViewHeight}, froxel {AerialGridX}x{AerialGridY}x{AerialGridZ}");
            aerialResourcesLogged = true;
        }
    }

    private void EnsureCloudResources(RenderContext rstate, int renderWidth, int renderHeight)
    {
        var w = Math.Max(renderWidth / 2, 64);
        var h = Math.Max(renderHeight / 2, 64);
        if (cloudsA != null && cloudsB != null && cloudW == w && cloudH == h)
        {
            return;
        }

        cloudsA?.Dispose();
        cloudsB?.Dispose();
        cloudsA = new Texture3D(rstate, w, h, 1, SurfaceFormat.HdrBlendable, storage: true);
        cloudsB = new Texture3D(rstate, w, h, 1, SurfaceFormat.HdrBlendable, storage: true);
        cloudW = w;
        cloudH = h;
        cloudsCurrent = null;
        cloudFlip = false;
        cloudHistoryValid = false;
        if (!cloudResourcesLogged)
        {
            FLLog.Info("Atmosphere", $"Cloud shell storage allocated: {cloudW}x{cloudH} half-res ping-pong");
            cloudResourcesLogged = true;
        }
    }

    private void BakeCloudNoise(RenderContext rstate)
    {
        if (cloudNoiseBase != null && cloudNoiseDetail != null)
        {
            return;
        }
        cloudNoiseBase?.Dispose();
        cloudNoiseDetail?.Dispose();
        cloudNoiseBase = new Texture3D(rstate, CloudNoiseBaseSize, CloudNoiseBaseSize, CloudNoiseBaseSize,
            SurfaceFormat.HdrBlendable, storage: true);
        cloudNoiseDetail = new Texture3D(rstate, CloudNoiseDetailSize, CloudNoiseDetailSize, CloudNoiseDetailSize,
            SurfaceFormat.HdrBlendable, storage: true);

        rstate.BeginPassTimer("atmo.cloudnoise");
        var gen = AllShaders.NoiseGen!.Get(0);
        var baseParams = new NoiseGenParams { SizeMode = new Vector4(CloudNoiseBaseSize, CloudNoiseBaseSize, CloudNoiseBaseSize, 0) };
        gen.SetUniformBlock(3, ref baseParams);
        rstate.SetStorageImage(4, cloudNoiseBase);
        rstate.Shader = gen;
        rstate.DispatchCompute((uint)(CloudNoiseBaseSize / 4), (uint)(CloudNoiseBaseSize / 4), (uint)(CloudNoiseBaseSize / 4));

        var detailParams = new NoiseGenParams { SizeMode = new Vector4(CloudNoiseDetailSize, CloudNoiseDetailSize, CloudNoiseDetailSize, 1) };
        gen.SetUniformBlock(3, ref detailParams);
        rstate.SetStorageImage(4, cloudNoiseDetail);
        rstate.Shader = gen;
        rstate.DispatchCompute((uint)(CloudNoiseDetailSize / 4), (uint)(CloudNoiseDetailSize / 4), (uint)(CloudNoiseDetailSize / 4));
        rstate.BarrierComputeToCompute();
        rstate.EndPassTimer();

        if (!cloudNoiseLogged)
        {
            FLLog.Info("Atmosphere",
                $"Cloud noise baked: base {CloudNoiseBaseSize}^3 Perlin-Worley, detail {CloudNoiseDetailSize}^3 Worley");
            cloudNoiseLogged = true;
        }
    }

    private static LutParams BuildParams(AtmosphereKey key, int width, int height)
    {
        var dc = ColorSpace.SrgbToLinear(key.Dc);
        var ac = ColorSpace.SrgbToLinear(key.Ac);
        var tint = new Vector3(MathF.Max(dc.R, 0.001f), MathF.Max(dc.G, 0.001f), MathF.Max(dc.B, 0.001f));
        var tintMax = MathF.Max(tint.X, MathF.Max(tint.Y, tint.Z));
        tint /= tintMax;

        var alpha = Math.Clamp(key.Alpha <= 0 ? 1f : key.Alpha, 0.1f, 2.0f);
        var fade = Math.Clamp(key.Fade <= 0 ? 1f : key.Fade, 0.15f, 4.0f);
        var shell = Math.Clamp(key.Scale <= 0 ? 1f : key.Scale, 0.25f, 8.0f);
        var ground = Math.Clamp((ac.R + ac.G + ac.B) / 3f, 0.02f, 1.0f) * 0.06f;

        // Wavelength-weighted extinction: blue falls off fastest so the
        // contact sheet warms/red-shifts near the horizon, with Discovery's
        // existing material tint acting as an art-direction multiplier.
        var rayleighBase = new Vector3(0.58f, 1.35f, 3.31f);
        var rayleigh = rayleighBase * Vector3.Lerp(Vector3.One, tint, 0.35f) *
                       (0.75f + fade * 0.25f);
        var absorption = new Vector3(0.08f, 0.04f, 0.015f) * (0.65f + alpha * 0.35f);

        return new LutParams
        {
            SizeMode = new Vector4(width, height, shell, fade),
            RayleighMie = new Vector4(rayleigh, 0.12f + alpha * 0.05f),
            AbsorptionAlbedo = new Vector4(absorption, ground)
        };
    }

    private static AtmosphereState? FindAtmosphereState(IReadOnlyList<GameObject> objects,
        ResourceManager resources, Vector3? cameraPosition, bool includeHidden = false)
    {
        AtmosphereState? best = null;
        var bestScore = float.MaxValue;
        for (var i = 0; i < objects.Count; i++)
        {
            if (objects[i].RenderComponent is not ModelRenderer { Model: { } model } renderer)
            {
                continue;
            }
            for (var p = 0; p < model.AllParts.Length; p++)
            {
                var part = model.AllParts[p];
                if (!part.Active || part.Mesh?.Levels == null)
                {
                    continue;
                }
                var levels = part.Mesh.Levels;
                for (var l = 0; l < levels.Length; l++)
                {
                    var level = levels[l];
                    if (level == null)
                    {
                        continue;
                    }
                    foreach (var drawcall in level.Drawcalls)
                    {
                        var material = drawcall.GetMaterial(resources);
                        if (material?.Render is AtmosphereMaterial atmosphere &&
                            atmosphere.Scale > 0 &&
                            (includeHidden || atmosphere.Alpha >= 0.01f))
                        {
                            var key = new AtmosphereKey(
                                atmosphere.Dc,
                                atmosphere.Ac,
                                atmosphere.Alpha,
                                atmosphere.Fade,
                                atmosphere.Scale);
                            var world = part.LocalTransform.Matrix() * renderer.World;
                            var worldScale = MaxAxisScale(world);
                            var shellRadius = part.Mesh.Radius * worldScale * atmosphere.Scale;
                            if (shellRadius <= 1f)
                            {
                                shellRadius = part.Mesh.Radius * atmosphere.Scale;
                            }
                            var planetRadius = shellRadius / MathF.Max(atmosphere.Scale, 1.0001f);
                            var state = new AtmosphereState(key, world.Translation, planetRadius, shellRadius);
                            if (cameraPosition == null)
                            {
                                return state;
                            }
                            var activeRadius = shellRadius * 1.5f;
                            var score = Vector3.Distance(cameraPosition.Value, state.Center) / MathF.Max(activeRadius, 1f);
                            if (score < bestScore)
                            {
                                bestScore = score;
                                best = state;
                            }
                        }
                    }
                }
            }
        }
        return best;
    }

    private static float MaxAxisScale(Matrix4x4 matrix)
    {
        var x = new Vector3(matrix.M11, matrix.M12, matrix.M13).Length();
        var y = new Vector3(matrix.M21, matrix.M22, matrix.M23).Length();
        var z = new Vector3(matrix.M31, matrix.M32, matrix.M33).Length();
        var scale = MathF.Max(x, MathF.Max(y, z));
        return scale > 1e-4f ? scale : 1f;
    }

    public void Dispose()
    {
        transmittance?.Dispose();
        multiScattering?.Dispose();
        skyView?.Dispose();
        aerial?.Dispose();
        cloudNoiseBase?.Dispose();
        cloudNoiseDetail?.Dispose();
        cloudsA?.Dispose();
        cloudsB?.Dispose();
    }
}
