using System;
using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Graphics;
using LibreLancer.Shaders;

namespace LibreLancer.Render;

/// <summary>
/// Unified frame pipeline (graphics roadmap, phase 1). The scene renders
/// into an HDR colour target (RGBA16F, display-referred until the phase 2
/// linear cleanup - see docs/LINEAR_AUDIT.md) and reaches the backbuffer
/// through one final Tonemap pass: the only gamma/grading point of the
/// frame. With Tonemapper = Off the pass is an exact pass-through.
/// </summary>
internal sealed class HdrFramePipeline : IDisposable
{
    private readonly RenderContext rstate;
    private Shader? shader;
    private RenderTarget? restoreTarget;

    public TonemapMode Tonemapper = TonemapMode.Off;
    public float Exposure = 1f;

    // Auto-exposure / eye adaptation (graphics roadmap, "Slice 1"). Opt-in:
    // when enabled, the average scene luminance drives Exposure each frame
    // instead of the fixed value, so dark space and bright sun-side both read
    // well. v1 reads back a downsampled copy of the HDR scene on the CPU and
    // computes a log-average; the Tonemap shader is untouched (it keeps using
    // Exposure). Default OFF -> exact current behaviour. AutoExpPin >= 0 freezes
    // the value (golden/deterministic captures).
    public bool AutoExposureEnabled;
    public float AutoExpKey = 0.18f;          // middle-grey target
    public float AutoExpMinExposure = 0.05f;  // exposure multiplier clamp
    public float AutoExpMaxExposure = 8f;
    public float AutoExpSpeedUp = 3f;         // adaptation rate towards brighter
    public float AutoExpSpeedDown = 1f;       // adaptation rate towards darker
    public float AutoExpCompensation = 0f;    // EV-style bias, in stops
    public float AutoExpPin = -1f;            // >= 0 freezes exposure (determinism)
    public float DeltaSeconds = 1f / 60f;     // set per frame by SystemRenderer
    private float adaptedLuminance = 0.18f;   // temporal adaptation state
    private byte[]? exposureReadback;
    private bool exposureReadbackUnavailable;

    public bool BloomEnabled;
    public float BloomThreshold = 1.2f;
    public float BloomIntensity = 0.18f;
    public float BloomRadius = 0.65f;
    public int BloomMips = 6;

    private Shader? extractShader;
    private Shader? downsampleShader;
    private Shader? upsampleShader;
    private Texture2D? blackTexture;

    public bool GodRaysEnabled;
    public float GodRaysIntensity = 0.35f;
    public float GodRaysDensity = 0.9f;
    public float GodRaysDecay = 0.95f;
    public int GodRaysSamples = 48;
    // Set per frame by SystemRenderer; W <= 0 means no visible sun.
    public Vector4 GodRaysSun = new(0, 0, 0, -1);
    public float GodRaysSunTransmittance = 1f;

    private Shader? rayMaskShader;
    private Shader? rayBlurShader;

    public PostAaMode PostAa = PostAaMode.Off;
    private Shader? fxaaShader;
    private Shader? volumetricCompositeShader;
    private Shader? smaaEdgesShader;
    private Shader? smaaWeightsShader;
    private Shader? smaaBlendShader;

    private struct FroxelCompositeParams
    {
        public Vector4 CompositeParams;
        public Vector4 GridParams;
        public Vector4 DepthParams;
        public Vector4 NearParams;
        public Matrix4x4 InverseProjection;
    }

    /// <summary>
    /// Every render-size-dependent target of the frame, bundled. THN
    /// letterbox cutscenes legitimately alternate between the letterboxed
    /// and the full viewport size every frame; recreating the whole set on
    /// each switch cycled pooled GPU memory underneath in-flight frames
    /// (bands of foreign render targets on screen) and stuttered through
    /// the reallocations. Bundles are cached per size instead.
    /// </summary>
    private sealed class SizedTargets : IDisposable
    {
        public readonly int Width;
        public readonly int Height;
        public readonly RenderTarget2D Scene;

        // G-buffer MRT (graphics phase 0.1): RT1 world-normal + roughness,
        // RT2 linear view-Z (R32F). Allocated only when SIRIUS_GBUFFER is set,
        // so the default frame is byte-identical and pays no VRAM. Bound as
        // extra opaque-pass colour attachments by SystemRenderer; their own
        // depth goes unused.
        public readonly RenderTarget2D? GBufferNormal;
        public readonly RenderTarget2D? GBufferViewZ;

        // Mip chain from half-res down; [0] receives the bright pass and,
        // after the up walk, holds the final bloom for the tonemap
        // composite.
        public readonly List<RenderTarget2D> BloomChain = new();
        public readonly List<Texture2D> BloomTextures = new();
        public int BloomChainMips;

        // Tiny non-thresholded downsample chain for CPU auto-exposure: the
        // smallest level (~<=32px) is read back each frame for a log-average.
        public readonly List<RenderTarget2D> ExposureChain = new();
        public readonly List<Texture2D> ExposureTextures = new();

        public RenderTarget2D? RayMaskTarget;
        public RenderTarget2D? RayBlurTarget;
        public Texture2D? RayMaskTexture;
        public Texture2D? RayBlurTexture;

        public RenderTarget2D? LdrTarget;
        public Texture2D? LdrTexture;

        public RenderTarget2D? VolumetricCompositeTarget;
        public Texture2D? VolumetricCompositeTexture;
        public Texture2D? VolumetricDepthTexture;

        // SMAA intermediates (roadmap 4.7): edges in RG, blending weights
        // in RGBA. Targets stay alive next to their textures (disposing a
        // target retires the texture's image on the Vulkan backend).
        public RenderTarget2D? SmaaEdgesTarget;
        public Texture2D? SmaaEdgesTexture;
        public RenderTarget2D? SmaaWeightsTarget;
        public Texture2D? SmaaWeightsTexture;

        public long LastUsedFrame;

        public SizedTargets(RenderContext rstate, int width, int height)
        {
            Width = width;
            Height = height;
            Scene = new RenderTarget2D(rstate,
                new Texture2D(rstate, width, height, false, SurfaceFormat.HdrBlendable));
            if (RenderMaterial.GBufferActive)
            {
                GBufferNormal = new RenderTarget2D(rstate,
                    new Texture2D(rstate, width, height, false, SurfaceFormat.HdrBlendable));
                GBufferViewZ = new RenderTarget2D(rstate,
                    new Texture2D(rstate, width, height, false, SurfaceFormat.Single));
            }
        }

        public void DisposeBloomChain()
        {
            foreach (var target in BloomChain)
            {
                target.Dispose();
            }
            BloomChain.Clear();
            BloomTextures.Clear();
        }

        public void Dispose()
        {
            Scene.Dispose();
            GBufferNormal?.Dispose();
            GBufferViewZ?.Dispose();
            DisposeBloomChain();
            foreach (var target in ExposureChain)
            {
                target.Dispose();
            }
            ExposureChain.Clear();
            ExposureTextures.Clear();
            RayMaskTarget?.Dispose();
            RayBlurTarget?.Dispose();
            LdrTarget?.Dispose();
            VolumetricCompositeTarget?.Dispose();
            VolumetricDepthTexture?.Dispose();
            SmaaEdgesTarget?.Dispose();
            SmaaWeightsTarget?.Dispose();
        }
    }

    // A frame uses at most two sizes (letterboxed scene + full HUD pass);
    // the third slot absorbs a window resize without instantly evicting
    // either. Beyond that the least recently used bundle goes.
    private const int MaxCachedSizes = 3;
    private readonly List<SizedTargets> targetCache = new();
    private SizedTargets? current;
    private long frameStamp;

    /// <summary>The HDR scene target of the current Begin/End frame -
    /// volumetrics copy its depth for the composite (phase 5).</summary>
    public RenderTarget2D? CurrentSceneTarget => current?.Scene;

    /// <summary>G-buffer RT1 (world-normal + roughness) of the current frame,
    /// or null when SIRIUS_GBUFFER is off (graphics phase 0.1).</summary>
    public RenderTarget2D? CurrentGBufferNormalTarget => current?.GBufferNormal;

    /// <summary>G-buffer RT2 (linear view-Z, R32F) of the current frame, or
    /// null when SIRIUS_GBUFFER is off (graphics phase 0.1).</summary>
    public RenderTarget2D? CurrentGBufferViewZTarget => current?.GBufferViewZ;

    public HdrFramePipeline(RenderContext rstate)
    {
        this.rstate = rstate;
    }

    /// <summary>The scene now renders into the HDR target.</summary>
    public void Begin(int renderWidth, int renderHeight)
    {
        frameStamp++;
        current = null;
        foreach (var bundle in targetCache)
        {
            if (bundle.Width == renderWidth && bundle.Height == renderHeight)
            {
                current = bundle;
                break;
            }
        }
        if (current == null)
        {
            FLLog.Info("Hdr", $"Frame targets created: {renderWidth}x{renderHeight}");
            current = new SizedTargets(rstate, renderWidth, renderHeight);
            current.LastUsedFrame = frameStamp;
            targetCache.Add(current);
            while (targetCache.Count > MaxCachedSizes)
            {
                SizedTargets? oldest = null;
                foreach (var bundle in targetCache)
                {
                    if (ReferenceEquals(bundle, current))
                    {
                        continue;
                    }
                    if (oldest == null || bundle.LastUsedFrame < oldest.LastUsedFrame)
                    {
                        oldest = bundle;
                    }
                }
                if (oldest == null)
                {
                    break;
                }
                targetCache.Remove(oldest);
                oldest.Dispose();
            }
        }
        current.LastUsedFrame = frameStamp;

        restoreTarget = rstate.RenderTarget;
        rstate.PushViewport(new Rectangle(0, 0, renderWidth, renderHeight));
        rstate.PushScissor(new Rectangle(0, 0, renderWidth, renderHeight), false);
        rstate.RenderTarget = current.Scene;
        rstate.ClearAll();
    }

    public Texture2D? CopySceneDepthForVolumetrics()
    {
        var targets = current;
        if (targets == null)
        {
            return null;
        }
        if (targets.VolumetricDepthTexture == null)
        {
            targets.VolumetricDepthTexture = new Texture2D(rstate, targets.Width, targets.Height, false,
                SurfaceFormat.Depth);
            FLLog.Info("Volumetrics", $"Scene depth copy texture created: {targets.Width}x{targets.Height}");
        }
        rstate.BeginPassTimer("vol_nebula_depth_copy");
        try
        {
            rstate.CopyDepth(targets.Scene, targets.VolumetricDepthTexture);
        }
        finally
        {
            rstate.EndPassTimer();
        }
        return targets.VolumetricDepthTexture;
    }

    /// <summary>
    /// Opt-in Phase 5 bridge from integrated froxel volume into the HDR scene.
    /// The pass ping-pongs through a private HDR target so it never samples and
    /// writes the same scene texture.
    /// </summary>
    public bool CompositeVolumetricNebula(Texture3D integrated, Texture2D sceneDepth,
        Matrix4x4 inverseProjection, Vector4 settings, Vector4 gridParams, Vector4 depthParams,
        Texture3D? nearIntegrated = null, Vector4 nearParams = default)
    {
        var targets = current;
        if (targets == null || AllShaders.FroxelComposite == null || integrated.IsDisposed)
        {
            return false;
        }
        if (targets.VolumetricCompositeTarget == null)
        {
            targets.VolumetricCompositeTexture = new Texture2D(rstate, targets.Width, targets.Height, false,
                SurfaceFormat.HdrBlendable);
            targets.VolumetricCompositeTarget = new RenderTarget2D(rstate, targets.VolumetricCompositeTexture);
            FLLog.Info("Volumetrics", $"HDR volumetric composite target created: {targets.Width}x{targets.Height}");
        }

        var oldTarget = rstate.RenderTarget;
        var oldBlend = rstate.BlendMode;
        var oldCull = rstate.Cull;
        var oldDepth = rstate.DepthEnabled;
        var fullRect = new Rectangle(0, 0, targets.Width, targets.Height);
        try
        {
            rstate.BeginPassTimer("vol_nebula_composite");
            rstate.BlendMode = BlendMode.Opaque;
            rstate.Cull = false;
            rstate.DepthEnabled = false;
            volumetricCompositeShader ??= AllShaders.FroxelComposite.Get(0);

            var compositeBlock = new FroxelCompositeParams
            {
                CompositeParams = settings,
                GridParams = gridParams,
                DepthParams = depthParams,
                NearParams = nearParams,
                InverseProjection = inverseProjection
            };
            rstate.RenderTarget = targets.VolumetricCompositeTarget;
            rstate.PushViewport(fullRect);
            rstate.PushScissor(fullRect, false);
            volumetricCompositeShader.SetUniformBlock(3, ref compositeBlock);
            rstate.Textures[0] = targets.Scene.Texture;
            rstate.Samplers[0] = SamplerState.LinearClamp;
            rstate.Textures[1] = integrated;
            rstate.Samplers[1] = SamplerState.LinearClamp;
            rstate.Textures[2] = sceneDepth;
            rstate.Samplers[2] = SamplerState.PointClamp;
            rstate.Textures[3] = nearIntegrated ?? integrated;
            rstate.Samplers[3] = SamplerState.LinearClamp;
            rstate.Shader = volumetricCompositeShader;
            rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
            rstate.PopScissor();
            rstate.PopViewport();

            var copyBlock = new FroxelCompositeParams
            {
                CompositeParams = new Vector4(0f, settings.Y, settings.Z, 0f),
                GridParams = gridParams,
                DepthParams = depthParams with { X = 0f },
                NearParams = nearParams,
                InverseProjection = inverseProjection
            };
            rstate.RenderTarget = targets.Scene;
            rstate.PushViewport(fullRect);
            rstate.PushScissor(fullRect, false);
            volumetricCompositeShader.SetUniformBlock(3, ref copyBlock);
            rstate.Textures[0] = targets.VolumetricCompositeTexture!;
            rstate.Samplers[0] = SamplerState.LinearClamp;
            rstate.Textures[1] = integrated;
            rstate.Samplers[1] = SamplerState.LinearClamp;
            rstate.Textures[2] = sceneDepth;
            rstate.Samplers[2] = SamplerState.PointClamp;
            rstate.Textures[3] = nearIntegrated ?? integrated;
            rstate.Samplers[3] = SamplerState.LinearClamp;
            rstate.Shader = volumetricCompositeShader;
            rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
            rstate.PopScissor();
            rstate.PopViewport();
            return true;
        }
        finally
        {
            rstate.Textures[0] = null;
            rstate.Textures[1] = null;
            rstate.Textures[2] = null;
            rstate.Textures[3] = null;
            rstate.RenderTarget = oldTarget;
            rstate.BlendMode = oldBlend;
            rstate.Cull = oldCull;
            rstate.DepthEnabled = oldDepth;
            rstate.EndPassTimer();
        }
    }

    /// <summary>Resolve the HDR target to the real output via the tonemap pass.</summary>
    public void End()
    {
        var targets = current ?? throw new InvalidOperationException("End() without Begin()");
        rstate.PopViewport();
        rstate.PopScissor();

        var oldBlend = rstate.BlendMode;
        var oldCull = rstate.Cull;
        var oldDepth = rstate.DepthEnabled;
        try
        {
            rstate.Cull = false;
            rstate.DepthEnabled = false;

            var bloomActive = BloomEnabled && BloomIntensity > 0f;
            if (bloomActive)
            {
                rstate.BeginPassTimer("post.bloom");
                RunBloom(targets);
                rstate.EndPassTimer();
                bloomActive = targets.BloomChain.Count > 0;
            }
            var raysActive = GodRaysEnabled && GodRaysIntensity > 0f &&
                             GodRaysSun.W > 0f && GodRaysSunTransmittance > 0.001f;
            if (raysActive)
            {
                rstate.BeginPassTimer("post.godrays");
                RunGodRays(targets);
                rstate.EndPassTimer();
            }

            // Auto-exposure (Slice 1): derive Exposure from the scene before the
            // tonemap consumes it. Pin short-circuits for determinism.
            if (AutoExposureEnabled)
            {
                if (AutoExpPin >= 0f)
                {
                    Exposure = AutoExpPin;
                }
                else
                {
                    rstate.BeginPassTimer("post.auto_exposure");
                    ComputeAutoExposure(targets);
                    rstate.EndPassTimer();
                }
            }

            // Roadmap 4.7 frame order: HDR -> bloom/rays -> tonemap ->
            // post-AA -> UI. With AA on, the tonemap resolves into an LDR
            // buffer and the AA pass produces the real output.
            var aaActive = PostAa is PostAaMode.Fxaa or PostAaMode.Smaa;
            if (aaActive && targets.LdrTarget == null)
            {
                targets.LdrTexture = new Texture2D(rstate, targets.Width, targets.Height, false, SurfaceFormat.Bgra8);
                targets.LdrTarget = new RenderTarget2D(rstate, targets.LdrTexture);
            }

            rstate.BeginPassTimer("post.tonemap");
            rstate.RenderTarget = aaActive ? targets.LdrTarget : restoreTarget;
            if (aaActive)
            {
                var ldrRect = new Rectangle(0, 0, targets.Width, targets.Height);
                rstate.PushViewport(ldrRect);
                rstate.PushScissor(ldrRect, false);
            }
            shader ??= AllShaders.Tonemap.Get(0);
            var parameters = new Vector4(
                Exposure,
                Tonemapper switch { TonemapMode.Aces => 1f, TonemapMode.Filmic => 2f, _ => 0f },
                bloomActive ? BloomIntensity : 0f,
                raysActive ? GodRaysIntensity : 0f);
            shader.SetUniformBlock(3, ref parameters);
            rstate.Textures[0] = targets.Scene.Texture;
            rstate.Samplers[0] = SamplerState.LinearClamp;
            rstate.Textures[1] = bloomActive ? targets.BloomTextures[0] : BlackTexture();
            rstate.Samplers[1] = SamplerState.LinearClamp;
            rstate.Textures[2] = raysActive ? targets.RayBlurTexture : BlackTexture();
            rstate.Samplers[2] = SamplerState.LinearClamp;
            rstate.BlendMode = BlendMode.Opaque;
            rstate.Shader = shader;
            rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
            rstate.EndPassTimer();

            if (aaActive && PostAa == PostAaMode.Fxaa)
            {
                rstate.BeginPassTimer("post.fxaa");
                rstate.PopScissor();
                rstate.PopViewport();
                rstate.RenderTarget = restoreTarget;
                fxaaShader ??= AllShaders.Fxaa.Get(0);
                var rcpFrame = new Vector4(1f / targets.Width, 1f / targets.Height, 0f, 0f);
                fxaaShader.SetUniformBlock(3, ref rcpFrame);
                rstate.Textures[0] = targets.LdrTexture!;
                rstate.Samplers[0] = SamplerState.LinearClamp;
                rstate.Shader = fxaaShader;
                rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
                rstate.EndPassTimer();
            }
            else if (aaActive)
            {
                rstate.PopScissor();
                rstate.PopViewport();
                RunSmaa(targets);
            }

            DrawBloomDebug(targets, bloomActive);
            DrawSmaaDebug(targets);
            DrawGBufferDebug(targets);
        }
        finally
        {
            rstate.BlendMode = oldBlend;
            rstate.Cull = oldCull;
            rstate.DepthEnabled = oldDepth;
        }
    }

    /// <summary>
    /// Bright pass into the half-res chain, 13-tap walk down, additive
    /// tent walk back up (roadmap 4.5). The composite into the scene is
    /// folded into the tonemap pass.
    /// </summary>
    private void RunBloom(SizedTargets targets)
    {
        EnsureBloomChain(targets);
        if (targets.BloomChain.Count == 0)
        {
            return;
        }
        extractShader ??= AllShaders.BloomExtract.Get(0);
        downsampleShader ??= AllShaders.BloomDownsample.Get(0);
        upsampleShader ??= AllShaders.BloomUpsample.Get(0);

        rstate.BlendMode = BlendMode.Opaque;
        var knee = MathF.Max(BloomThreshold * 0.5f, 1e-4f);
        BloomPass(targets, extractShader, targets.Scene.Texture, 0,
            new Vector4(BloomThreshold, knee, 1f / targets.Width, 1f / targets.Height));
        for (var i = 1; i < targets.BloomChain.Count; i++)
        {
            BloomPass(targets, downsampleShader, targets.BloomTextures[i - 1], i,
                new Vector4(1f / targets.BloomTextures[i - 1].Width, 1f / targets.BloomTextures[i - 1].Height, 0f, 0f));
        }

        // Each smaller mip melts into the one above it.
        rstate.BlendMode = BlendMode.Additive;
        var sampleScale = 0.5f + BloomRadius;
        for (var i = targets.BloomChain.Count - 1; i >= 1; i--)
        {
            BloomPass(targets, upsampleShader, targets.BloomTextures[i], i - 1,
                new Vector4(1f / targets.BloomTextures[i].Width, 1f / targets.BloomTextures[i].Height, sampleScale, 0f));
        }
    }

    private void BloomPass(SizedTargets targets, Shader passShader, Texture2D source, int destination, Vector4 parameters)
    {
        rstate.RenderTarget = targets.BloomChain[destination];
        var rect = new Rectangle(0, 0, targets.BloomTextures[destination].Width, targets.BloomTextures[destination].Height);
        rstate.PushViewport(rect);
        rstate.PushScissor(rect, false);
        passShader.SetUniformBlock(3, ref parameters);
        rstate.Textures[0] = source;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.Shader = passShader;
        rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
        rstate.PopScissor();
        rstate.PopViewport();
    }

    private void EnsureExposureChain(SizedTargets targets)
    {
        if (targets.ExposureChain.Count > 0)
        {
            return;
        }
        var w = Math.Max(targets.Width / 2, 1);
        var h = Math.Max(targets.Height / 2, 1);
        // Halve until the largest dimension is small enough for a cheap readback.
        for (var guard = 0; guard < 16; guard++)
        {
            var tex = new Texture2D(rstate, w, h, false, SurfaceFormat.HdrBlendable);
            targets.ExposureTextures.Add(tex);
            targets.ExposureChain.Add(new RenderTarget2D(rstate, tex));
            if (Math.Max(w, h) <= 32)
            {
                break;
            }
            w = Math.Max(w / 2, 1);
            h = Math.Max(h / 2, 1);
        }
    }

    /// <summary>
    /// "Slice 1" auto-exposure: progressively box-downsample the HDR scene
    /// (reusing the non-thresholded bloom downsample shader) into a tiny target,
    /// read it back, take a log-average luminance, adapt over time, and set
    /// Exposure. v1 uses a synchronous CPU readback - opt-in and default off.
    /// </summary>
    private void ComputeAutoExposure(SizedTargets targets)
    {
        if (exposureReadbackUnavailable)
        {
            return;
        }
        EnsureExposureChain(targets);
        if (targets.ExposureChain.Count == 0)
        {
            return;
        }
        downsampleShader ??= AllShaders.BloomDownsample.Get(0);
        rstate.BlendMode = BlendMode.Opaque;

        var src = targets.Scene.Texture;
        for (var i = 0; i < targets.ExposureChain.Count; i++)
        {
            rstate.RenderTarget = targets.ExposureChain[i];
            var dst = targets.ExposureTextures[i];
            var rect = new Rectangle(0, 0, dst.Width, dst.Height);
            rstate.PushViewport(rect);
            rstate.PushScissor(rect, false);
            var p = new Vector4(1f / src.Width, 1f / src.Height, 0f, 0f);
            downsampleShader.SetUniformBlock(3, ref p);
            rstate.Textures[0] = src;
            rstate.Samplers[0] = SamplerState.LinearClamp;
            rstate.Shader = downsampleShader;
            rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
            rstate.PopScissor();
            rstate.PopViewport();
            src = dst;
        }

        var small = targets.ExposureTextures[^1];
        var n = small.Width * small.Height;
        var byteCount = n * 8; // RGBA16F = 8 bytes/texel
        if (exposureReadback == null || exposureReadback.Length != byteCount)
        {
            exposureReadback = new byte[byteCount];
        }
        try
        {
            small.GetData(exposureReadback);
        }
        catch (Exception e)
        {
            exposureReadbackUnavailable = true;
            FLLog.Warning("AutoExposure", $"readback unavailable, using fixed exposure: {e.Message}");
            return;
        }

        double logSum = 0;
        var count = 0;
        for (var i = 0; i < n; i++)
        {
            var o = i * 8;
            float r = (float)BitConverter.ToHalf(exposureReadback, o);
            float g = (float)BitConverter.ToHalf(exposureReadback, o + 2);
            float b = (float)BitConverter.ToHalf(exposureReadback, o + 4);
            var lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            if (!float.IsFinite(lum) || lum < 0f)
            {
                lum = 0f;
            }
            logSum += Math.Log(Math.Max(lum, 1e-4f));
            count++;
        }
        var avgLum = count > 0 ? (float)Math.Exp(logSum / count) : 0.18f;

        adaptedLuminance = AutoExposureMath.AdaptLuminance(adaptedLuminance, avgLum, DeltaSeconds,
            AutoExpSpeedUp, AutoExpSpeedDown);
        Exposure = AutoExposureMath.ExposureForLuminance(adaptedLuminance, AutoExpKey,
            AutoExpCompensation, AutoExpMinExposure, AutoExpMaxExposure);
    }

    private struct GodRaysUniforms
    {
        public Vector4 SunPosition;
        public Vector4 Params;
    }

    /// <summary>
    /// Half-res sun mask (bright scene pixels around the projected disk,
    /// implicitly occluded by geometry) smeared by a radial blur
    /// (roadmap 4.6); composited in the tonemap pass.
    /// </summary>
    private void RunGodRays(SizedTargets targets)
    {
        if (targets.RayMaskTarget == null)
        {
            var w = Math.Max(targets.Width / 2, 8);
            var h = Math.Max(targets.Height / 2, 8);
            targets.RayMaskTexture = new Texture2D(rstate, w, h, false, SurfaceFormat.HdrBlendable);
            targets.RayMaskTarget = new RenderTarget2D(rstate, targets.RayMaskTexture);
            targets.RayBlurTexture = new Texture2D(rstate, w, h, false, SurfaceFormat.HdrBlendable);
            targets.RayBlurTarget = new RenderTarget2D(rstate, targets.RayBlurTexture);
        }
        rayMaskShader ??= AllShaders.GodRaysMask.Get(0);
        rayBlurShader ??= AllShaders.GodRaysBlur.Get(0);
        rstate.BlendMode = BlendMode.Opaque;

        var mask = new GodRaysUniforms
        {
            SunPosition = GodRaysSun with { W = targets.Width / (float)targets.Height },
            // Cutoff keeps dim background out of the smear; the sun core
            // and glow sprites sit way above it (linear-light scale).
            Params = new Vector4(0.05f,
                Math.Clamp(GodRaysSunTransmittance, 0f, 1f), 0f, 0f)
        };
        rstate.RenderTarget = targets.RayMaskTarget;
        var maskRect = new Rectangle(0, 0, targets.RayMaskTexture!.Width, targets.RayMaskTexture.Height);
        rstate.PushViewport(maskRect);
        rstate.PushScissor(maskRect, false);
        rayMaskShader.SetUniformBlock(3, ref mask);
        rstate.Textures[0] = targets.Scene.Texture;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.Shader = rayMaskShader;
        rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
        rstate.PopScissor();
        rstate.PopViewport();

        var blur = new GodRaysUniforms
        {
            SunPosition = new Vector4(GodRaysSun.X, GodRaysSun.Y, 0f, 0f),
            Params = new Vector4(GodRaysDensity, GodRaysDecay, 1f, Math.Clamp(GodRaysSamples, 16, 96))
        };
        rstate.RenderTarget = targets.RayBlurTarget;
        var blurRect = new Rectangle(0, 0, targets.RayBlurTexture!.Width, targets.RayBlurTexture.Height);
        rstate.PushViewport(blurRect);
        rstate.PushScissor(blurRect, false);
        rayBlurShader.SetUniformBlock(3, ref blur);
        rstate.Textures[0] = targets.RayMaskTexture;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.Shader = rayBlurShader;
        rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
        rstate.PopScissor();
        rstate.PopViewport();
    }

    /// <summary>
    /// SMAA 1x (roadmap 4.7): luma edge detection -> blending weights
    /// (AreaTex/SearchTex LUTs) -> neighborhood blending. Runs on the
    /// tonemapped LDR image; every pass is a fullscreen overwrite, so the
    /// intermediates never need clearing.
    /// </summary>
    private void RunSmaa(SizedTargets targets)
    {
        if (targets.SmaaEdgesTarget == null)
        {
            targets.SmaaEdgesTexture = new Texture2D(rstate, targets.Width, targets.Height, false, SurfaceFormat.Bgra8);
            targets.SmaaEdgesTarget = new RenderTarget2D(rstate, targets.SmaaEdgesTexture);
            targets.SmaaWeightsTexture = new Texture2D(rstate, targets.Width, targets.Height, false, SurfaceFormat.Bgra8);
            targets.SmaaWeightsTarget = new RenderTarget2D(rstate, targets.SmaaWeightsTexture);
        }
        smaaEdgesShader ??= AllShaders.SmaaEdges.Get(0);
        smaaWeightsShader ??= AllShaders.SmaaWeights.Get(0);
        smaaBlendShader ??= AllShaders.SmaaBlend.Get(0);

        var metrics = new Vector4(1f / targets.Width, 1f / targets.Height, targets.Width, targets.Height);
        var fullRect = new Rectangle(0, 0, targets.Width, targets.Height);
        rstate.BlendMode = BlendMode.Opaque;

        rstate.BeginPassTimer("post.smaa.edges");
        rstate.RenderTarget = targets.SmaaEdgesTarget;
        rstate.PushViewport(fullRect);
        rstate.PushScissor(fullRect, false);
        smaaEdgesShader.SetUniformBlock(3, ref metrics);
        rstate.Textures[0] = targets.LdrTexture!;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.Shader = smaaEdgesShader;
        rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
        rstate.PopScissor();
        rstate.PopViewport();
        rstate.EndPassTimer();

        rstate.BeginPassTimer("post.smaa.weights");
        rstate.RenderTarget = targets.SmaaWeightsTarget;
        rstate.PushViewport(fullRect);
        rstate.PushScissor(fullRect, false);
        smaaWeightsShader.SetUniformBlock(3, ref metrics);
        rstate.Textures[0] = targets.SmaaEdgesTexture!;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.Textures[1] = SmaaTextures.GetAreaTex(rstate);
        rstate.Samplers[1] = SamplerState.LinearClamp;
        rstate.Textures[2] = SmaaTextures.GetSearchTex(rstate);
        rstate.Samplers[2] = SamplerState.LinearClamp;
        rstate.Shader = smaaWeightsShader;
        rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
        rstate.PopScissor();
        rstate.PopViewport();
        rstate.EndPassTimer();

        rstate.BeginPassTimer("post.smaa.blend");
        rstate.RenderTarget = restoreTarget;
        smaaBlendShader.SetUniformBlock(3, ref metrics);
        rstate.Textures[0] = targets.LdrTexture!;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.Textures[1] = targets.SmaaWeightsTexture!;
        rstate.Samplers[1] = SamplerState.LinearClamp;
        rstate.Shader = smaaBlendShader;
        rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
        rstate.EndPassTimer();
    }

    private static readonly bool debugSmaa =
        Environment.GetEnvironmentVariable("SIRIUS_DEBUG_SMAA") == "1";

    /// <summary>Edges + weights contact sheet over the final image.</summary>
    private void DrawSmaaDebug(SizedTargets targets)
    {
        if (!debugSmaa || PostAa != PostAaMode.Smaa || targets.SmaaEdgesTexture == null)
        {
            return;
        }
        var list = rstate.Renderer2D.CreateDrawList();
        var w = Math.Max(targets.Width / 4, 32);
        var h = Math.Max(targets.Height / 4, 18);
        list.DrawImageStretched(targets.SmaaEdgesTexture, new Rectangle(8, 8, w, h), Color4.White);
        list.DrawImageStretched(targets.SmaaWeightsTexture!, new Rectangle(8 + w + 4, 8, w, h), Color4.White);
        list.Render();
    }

    // SIRIUS_GBUFFER_SHOW: 1 = RT1 normal+roughness, 2 = RT2 viewZ (phase 0.1).
    private static readonly int debugGBuffer =
        Environment.GetEnvironmentVariable("SIRIUS_GBUFFER_SHOW") switch
        {
            "1" => 1,
            "2" => 2,
            _ => 0
        };

    // View-Z debug ramp scale (negative flips sign for -Z-forward views).
    private static readonly float gbufferViewZScale =
        float.TryParse(Environment.GetEnvironmentVariable("SIRIUS_GBUFFER_VIEWZ_SCALE"),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var vzs)
            ? vzs : -0.02f;

    /// <summary>Fullscreen G-buffer debug over the final image via the proven
    /// Renderer2D path (the raw RGBA16F-&gt;LDR vkCmdBlitImage produced display
    /// garbage). Mode 1 = RT1 world-normal; mode 2 = RT2 linear view-Z as a
    /// grayscale ramp (phase 0.1).</summary>
    private void DrawGBufferDebug(SizedTargets targets)
    {
        if (debugGBuffer == 0 || targets.GBufferNormal == null)
        {
            return;
        }
        var list = rstate.Renderer2D.CreateDrawList();
        if (debugGBuffer == 2 && targets.GBufferViewZ != null)
        {
            // Linear view-Z ramp. NOTE: RT2 is R32F and Renderer2D cannot
            // sample single-channel float (returns 0) - a faithful grayscale
            // depth view needs a dedicated R32F->grayscale fullscreen shader
            // (debug-infra follow-up). The RT2 WRITE is verified (3rd MRT
            // attachment captures shader output; VVL=0). Scale/sign tunable
            // via SIRIUS_GBUFFER_VIEWZ_SCALE.
            var s = gbufferViewZScale;
            list.DrawImageStretched(targets.GBufferViewZ.Texture,
                new Rectangle(0, 0, targets.Width, targets.Height),
                new Color4(s, s, s, 1f), mode: BlendMode.Opaque);
        }
        else
        {
            // Opaque: RT1's alpha is roughness, not coverage - alpha-blending
            // it would make the buffer see-through where the surface is smooth.
            list.DrawImageStretched(targets.GBufferNormal.Texture,
                new Rectangle(0, 0, targets.Width, targets.Height), Color4.White,
                mode: BlendMode.Opaque);
        }
        list.Render();
    }

    private void EnsureBloomChain(SizedTargets targets)
    {
        var mips = Math.Clamp(BloomMips, 2, 7);
        if (targets.BloomChain.Count > 0 && targets.BloomChainMips == mips)
        {
            return;
        }
        targets.DisposeBloomChain();
        targets.BloomChainMips = mips;
        var levelWidth = targets.Width / 2;
        var levelHeight = targets.Height / 2;
        for (var i = 0; i < mips && levelWidth >= 8 && levelHeight >= 8; i++)
        {
            var texture = new Texture2D(rstate, levelWidth, levelHeight, false, SurfaceFormat.HdrBlendable);
            targets.BloomTextures.Add(texture);
            targets.BloomChain.Add(new RenderTarget2D(rstate, texture));
            levelWidth /= 2;
            levelHeight /= 2;
        }
    }

    private Texture2D BlackTexture()
    {
        if (blackTexture == null)
        {
            blackTexture = new Texture2D(rstate, 1, 1, false, SurfaceFormat.Bgra8);
            blackTexture.SetData(new uint[] { 0 });
        }
        return blackTexture;
    }

    private static readonly bool debugBloom =
        Environment.GetEnvironmentVariable("SIRIUS_DEBUG_BLOOM") == "1";

    /// <summary>Mip chain contact sheet over the final image.</summary>
    private void DrawBloomDebug(SizedTargets targets, bool bloomActive)
    {
        if (!debugBloom || !bloomActive)
        {
            return;
        }
        // Below the menu logo/HUD chrome so the game UI can't cover it.
        var list = rstate.Renderer2D.CreateDrawList();
        var x = 8;
        foreach (var texture in targets.BloomTextures)
        {
            var w = Math.Max(texture.Width / 4, 16);
            var h = Math.Max(texture.Height / 4, 9);
            list.DrawImageStretched(texture, new Rectangle(x, targets.Height - h - 8, w, h), Color4.White);
            x += w + 4;
        }
        list.Render();
    }

    public void Dispose()
    {
        foreach (var bundle in targetCache)
        {
            bundle.Dispose();
        }
        targetCache.Clear();
        current = null;
        blackTexture?.Dispose();
        blackTexture = null;
    }

    private static readonly bool testPattern =
        Environment.GetEnvironmentVariable("SIRIUS_VK_TESTPATTERN") == "1";

    /// <summary>
    /// Bring-up diagnostic: solid colour quads (vertex path) and the scene
    /// target image (texture+UV path) drawn through the regular 2D stack.
    /// </summary>
    public void DrawTestPattern()
    {
        if (!testPattern)
        {
            return;
        }
        var list = rstate.Renderer2D.CreateDrawList();
        list.FillRectangle(new Rectangle(8, 8, 64, 64), Color4.Red);
        list.FillRectangle(new Rectangle(80, 8, 64, 64), Color4.Lime);
        list.FillRectangle(new Rectangle(152, 8, 64, 64), Color4.Blue);
        if (current != null)
        {
            list.DrawImageStretched(current.Scene.Texture, new Rectangle(8, 80, 256, 192), Color4.White);
        }
        list.Render();
    }
}
