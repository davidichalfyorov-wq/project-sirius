// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using LibreLancer.Graphics.Backends;
using LibreLancer.Graphics.Backends.OpenGL;

namespace LibreLancer.Graphics;

public enum DepthFunction
{
    Less = 0x201,
    LessEqual = 0x203
}

//OpenGL Render States
public class RenderContext
{
    public long FrameNumber => frameNumber;
    private long frameNumber = 0;
    private Shader tintShader;

    internal static RenderContext Instance = null!;

    public Color4 ClearColor
    {
        get => requested.ClearColor;
        set => requested.ClearColor = value;
    }

    public bool SupportsWireframe => impl.SupportsWireframe;

    private Point _drawableSize;
    public Point DrawableSize => _drawableSize;

    internal void SetDrawableSize(Point sz) => _drawableSize = sz;

    public bool Wireframe
    {
        get => requested.Wireframe;
        set => requested.Wireframe = value;
    }

    public bool DepthEnabled
    {
        get => requested.DepthEnabled;
        set => requested.DepthEnabled = value;
    }

    private TextureFiltering _preferred = TextureFiltering.Trilinear;
    public TextureFiltering PreferredFilterLevel
    {
        get { return _preferred;  }
        set {
            if (value == TextureFiltering.Anisotropic && MaxAnisotropy == 0)
                _preferred = TextureFiltering.Trilinear;
            else
                _preferred = value;
        }
    }

    public int MaxAnisotropy => impl.MaxAnisotropy;
    public int MaxSamples => impl.MaxSamples;

    public int AnisotropyLevel
    {
        get => impl.AnisotropyLevel;
        set => impl.AnisotropyLevel = value;
    }

    public int[]? GetAnisotropyLevels()
    {
        if (MaxAnisotropy == 0)
        {
            return null;
        }

        var levels = new List<int>();
        int i = 2;
        while(i <= MaxAnisotropy)
        {
            levels.Add(i);
            i *= 2;
        }
        return levels.ToArray();
    }

    public bool DepthWrite
    {
        get => requested.DepthWrite;
        set => requested.DepthWrite = value;
    }
    public ushort BlendMode
    {
        get => requested.BlendMode;
        set => requested.BlendMode = value;
    }

    public DepthFunction DepthFunction
    {
        get => (DepthFunction) requested.DepthFunction;
        set => requested.DepthFunction = (int) value;
    }

    public Vector2 DepthRange
    {
        get => requested.DepthRange;
        set => requested.DepthRange = value;
    }

    public bool ColorWrite
    {
        get => requested.ColorWrite;
        set => requested.ColorWrite = value;
    }
    public bool Cull
    {
        get => requested.CullEnabled;
        set => requested.CullEnabled = value;
    }

    public bool ScissorEnabled
    {
        get => requested.ScissorEnabled;
        private set => requested.ScissorEnabled = value;
    }

    public Rectangle ScissorRectangle
    {
        get => requested.ScissorRect;
        private set => requested.ScissorRect = value;
    }

    private RenderTarget? requestedRenderTarget;
    public RenderTarget? RenderTarget
    {
        get => requestedRenderTarget;
        set
        {
            requestedRenderTarget = value;
            requested.RenderTarget = requestedRenderTarget?.Target;
        }
    }

    private Shader? requestedShader;
    public Shader? Shader
    {
        get => requestedShader;
        set
        {
            requestedShader = value;
            requested.Shader = value?.Backing;
        }
    }

    public CullFaces CullFace
    {
        get => requested.CullFaces;
        set => requested.CullFaces = value;
    }

    public void SetIdentityCamera() => Backend.SetIdentityCamera();
    public void SetCamera(ICamera camera) => Backend.SetCamera(camera);

    public Renderer2D Renderer2D { get; }

    private IRenderContext impl;

    internal IRenderContext Backend => impl;

    public bool HasFeature(GraphicsFeature feature) => impl.HasFeature(feature);

    public TextureSlots Textures { get; private set; }
    public SamplerSlots Samplers { get; private set; }

    internal RenderContext (IRenderContext impl)
    {
        this.impl = impl;
        Instance = this;
        PreferredFilterLevel = TextureFiltering.Trilinear;
        impl.Init(ref requested);
        Renderer2D = new Renderer2D(this);
        Textures = new(this);
        Samplers = new(this);
        SetIdentityCamera();
        tintShader = ShaderBundle.FromResource<RenderContext>(this, "FSTint.bin").Get(0);
    }

    public Rectangle CurrentViewport => requested.Viewport;

    private Stack<Rectangle> viewports = new Stack<Rectangle>();
    private Stack<Rectangle> clips = new Stack<Rectangle>();

    public bool PushScissor(Rectangle clip, bool parentClip = true)
    {
        if(clips.Count > 0 && parentClip) {
            if (!Rectangle.Clip(clip, clips.Peek(), out var newRect))
            {
                return false;
            }
            ScissorRectangle = newRect;
            clips.Push(newRect);
            ScissorEnabled = true;
        }
        else
        {
            ScissorRectangle = clip;
            clips.Push(clip);
            ScissorEnabled = true;
        }
        return true;
    }

    public bool ReplaceScissor(Rectangle newRect, bool parentClip = true)
    {
        if (clips.Count == 0)
            throw new InvalidOperationException();
        clips.Pop();
        return PushScissor(newRect, parentClip);
    }

    public void PopScissor()
    {
        if (clips.Count == 0)
            throw new InvalidOperationException();
        clips.Pop();
        if (clips.Count > 0)
        {
            ScissorRectangle = clips.Peek();
            ScissorEnabled = true;
        }
        else
        {
            ScissorEnabled = false;
        }
    }

    public void ClearScissor()
    {
        ScissorEnabled = false;
        clips.Clear();
    }


    public void PushViewport(Rectangle vp)
    {
        viewports.Push (vp);
        SetViewport(vp);
    }

    public void PushViewport(int x, int y, int width, int height)
    {
        PushViewport(new Rectangle(x,y,width,height));
    }

    public void ReplaceViewport(int x, int y, int width, int height)
    {
        if(viewports.Count >= 1)
            viewports.Pop ();
        PushViewport (x, y, width, height);
    }

    public void PopViewport()
    {
        viewports.Pop ();
        if (viewports.Count > 0) {
            var vp = viewports.Peek();
            SetViewport(new Rectangle(vp.X, vp.Y, vp.Width, vp.Height));
        }
    }

    private void SetViewport(Rectangle vp)
    {
        requested.Viewport = vp;
    }

    public Vector2 PolygonOffset
    {
        get => requested.PolygonOffset;
        set => requested.PolygonOffset = value;
    }

    public void SSBOMemoryBarrier() => impl.MemoryBarrier();

    /// <summary>Dispatches the compute shader set via Shader (phase 5).
    /// Ends any active render pass; gate on GraphicsFeature.Compute.</summary>
    public void DispatchCompute(uint groupsX, uint groupsY, uint groupsZ)
    {
        Apply();
        impl.DispatchCompute(groupsX, groupsY, groupsZ);
    }

    /// <summary>Binds a storage-capable texture to a compute UAV slot
    /// (RWTexture2D/3D at register uN -> slot N).</summary>
    public void SetStorageImage(int slot, Texture? texture) => impl.SetStorageImage(slot, texture);

    /// <summary>Compute writes -> graphics reads barrier.</summary>
    public void BarrierComputeToGraphics() => impl.BarrierComputeToGraphics();

    /// <summary>Graphics writes -> compute reads barrier.</summary>
    public void BarrierGraphicsToCompute() => impl.BarrierGraphicsToCompute();

    /// <summary>Barrier between dependent compute dispatches.</summary>
    public void BarrierComputeToCompute() => impl.BarrierComputeToCompute();

    /// <summary>Copies a render target's depth into a sampled depth
    /// texture (Vulkan-only no-op elsewhere; volumetrics input).</summary>
    public void CopyDepth(RenderTarget2D source, Texture2D destination) =>
        impl.CopyDepthToTexture(source.Backing, destination.Backing);

    /// <summary>Binds extra colour attachments (G-buffer MRT, graphics phase
    /// 0.1) for the opaque pass; pass null to restore the single target.
    /// Vulkan-only; other backends no-op.</summary>
    public void SetGBufferTargets(RenderTarget2D[]? extras)
    {
        if (extras == null || extras.Length == 0)
        {
            impl.SetGBufferTargets(null);
            return;
        }
        var arr = new IRenderTarget2D[extras.Length];
        for (var i = 0; i < extras.Length; i++)
        {
            arr[i] = extras[i].Backing;
        }
        impl.SetGBufferTargets(arr);
    }

    /// <summary>GPU time markers around a render pass (debug overlay).</summary>
    public void BeginPassTimer(string name) => impl.BeginPassTimer(name);

    /// <summary>Device memory held by backend allocators (Dev HUD).</summary>
    public long DeviceMemoryAllocated => impl.DeviceMemoryAllocated;

    /// <summary>Ray tracing services; null without ray-query support.</summary>
    public IRayTracing? RayTracing => impl.RayTracing;

    public void EndPassTimer() => impl.EndPassTimer();

    /// <summary>Per-pass GPU times from a few frames ago.</summary>
    public System.Collections.Generic.IReadOnlyList<Backends.PassTiming> PassTimings => impl.PassTimings;

    public void ClearAll()
    {
        Apply();
        impl.ClearAll();
        frameNumber++;
    }

    public void ClearColorOnly()
    {
        Apply();
        impl.ClearColorOnly();
    }

    public void ClearDepth()
    {
        Apply();
        impl.ClearDepth();
    }

    private GraphicsState requested;

    internal void ApplyViewport() => impl.ApplyViewport(ref requested);

    private bool viewportErrorTriggered;

    internal void EndFrame()
    {
        if (viewports.Count != 1)
        {
            if (!viewportErrorTriggered)
            {
                FLLog.Error("Render", "Internal Error: viewports.Count != 1 at end of frame (single trigger)");
                viewportErrorTriggered = true;
            }
#if DEBUG
                throw new Exception ("viewports.Count != 1 at end of frame");
#else
            viewports = new Stack<Rectangle>([viewports.Peek()]);
#endif
        }
    }

    internal void ApplyScissor() => impl.ApplyScissor(ref requested);

    internal void ApplyRenderTarget() => impl.ApplyRenderTarget(ref requested);

    internal void SetBlendMode(ushort mode) => impl.SetBlendMode(mode);
    internal void Set2DState(bool depth, bool cull) => impl.Set2DState(depth, cull);

    internal void Apply()
    {
        ApplyRenderTarget();
        ApplyViewport();
        impl.ApplyState(ref requested);
    }

    public void TintViewport(Color4 color)
    {
        tintShader.SetUniformBlock(3, ref color);
        ApplyRenderTarget();
        ApplyViewport();
        ApplyScissor();
        impl.Set2DState(false, false);
        impl.SetBlendMode(Graphics.BlendMode.Normal);
        impl.ApplyShader(tintShader.Backing);
        impl.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
    }

    public void DrawMeshTasks(uint groupsX, uint groupsY, uint groupsZ)
    {
        Apply();
        impl.DrawMeshTasks(groupsX, groupsY, groupsZ);
    }

    /// <summary>Per-draw fragment shading rate: 1 = full, 2 = 2x2 coarse.
    /// Takes effect on subsequent draws; no-op without VRS support.</summary>
    public void SetShadingRate(int size) => impl.SetShadingRate(size);

    public void DrawNoVertexBuffer(PrimitiveTypes type, int primitiveCount)
    {
        Apply();
        impl.DrawNoVertexBuffer(type, primitiveCount);
    }
}
