using System;
using System.Collections.Generic;
using LibreLancer.Graphics.Vertices;

namespace LibreLancer.Graphics.Backends;

public readonly record struct PassTiming(string Name, double Milliseconds);

internal interface IRenderContext
{
    int MaxSamples { get; }
    int MaxAnisotropy { get; }
    int AnisotropyLevel { get; set; }
    bool SupportsWireframe { get; }
    void Init(ref GraphicsState requested);
    void ApplyState(ref GraphicsState requested);
    void ApplyViewport(ref GraphicsState requested);
    void ApplyScissor(ref GraphicsState requested);
    void ApplyRenderTarget(ref GraphicsState requested);
    void ApplyShader(IShader shader);

    void Set2DState(bool cull, bool depth);

    void SetBlendMode(ushort mode);

    void ClearAll();
    void ClearColorOnly();
    void ClearDepth();
    void MemoryBarrier();

    IShader CreateShader(ReadOnlySpan<byte> program);

    IElementBuffer CreateElementBuffer(int count, bool isDynamic = false);
    IVertexBuffer CreateVertexBuffer(Type type, int length, bool isStream = false);
    IVertexBuffer CreateVertexBuffer(IVertexType type, int length, bool isStream = false);

    ITexture2D CreateTexture2D(int width, int height, bool hasMipMaps, SurfaceFormat format);

    ITextureCube CreateTextureCube(int size, bool mipMap, SurfaceFormat format);

    /// <summary>3D textures (phase 5 volumetrics). Vulkan-only: gate with
    /// HasFeature(GraphicsFeature.Compute) before creating.</summary>
    ITexture3D CreateTexture3D(int width, int height, int depth, SurfaceFormat format, bool storage = false) =>
        throw new NotSupportedException("Texture3D requires the Vulkan backend");

    IDepthBuffer CreateDepthBuffer(int width, int height);

    IRenderTarget2D CreateRenderTarget2D(ITexture2D texture, IDepthBuffer buffer);

    IMultisampleTarget CreateMultisampleTarget(int width, int height, int samples);

    IStorageBuffer CreateStorageBuffer(int size, int stride);

    void SetCamera(ICamera camera);
    void SetIdentityCamera();

    bool HasFeature(GraphicsFeature feature);

    string GetRenderer();

    void MakeCurrent(IntPtr sdlWindow);
    void SwapWindow(IntPtr sdlWindow, bool vsync, bool fullscreen);

    Point GetDrawableSize(IntPtr sdlWindow);

    void QueryFences();

    void SetTextureSlot(int slot, Texture? texture);
    void SetSamplerState(int slot, SamplerState state);
    void DrawNoVertexBuffer(PrimitiveTypes type, int primitiveCount);
    void DrawMeshTasks(uint groupsX, uint groupsY, uint groupsZ);
    void SetShadingRate(int size);

    // Compute (phase 5 foundation). No-ops/throws on backends without
    // GraphicsFeature.Compute - gate call sites on HasFeature.
    void DispatchCompute(uint groupsX, uint groupsY, uint groupsZ) { }
    void SetStorageImage(int slot, Texture? texture) { }
    void BarrierComputeToGraphics() { }
    void BarrierGraphicsToCompute() { }
    void BarrierComputeToCompute() { }

    /// <summary>Copies a render target's depth into a sampled depth
    /// texture (Vulkan-only; volumetric composite input).</summary>
    void CopyDepthToTexture(IRenderTarget2D source, ITexture2D destination) { }

    /// <summary>Binds extra colour attachments (G-buffer MRT, graphics phase
    /// 0.1) alongside the current render target for the opaque pass. Pass
    /// null/empty to restore the single-attachment path. Vulkan-only; other
    /// backends no-op (the GBUFFER shader permutation aliases to the base
    /// single-target shader there). Targets carry their own layout tracking,
    /// shared with the blit path.</summary>
    void SetGBufferTargets(IRenderTarget2D[]? extras) { }

    /// <summary>Sets the previous-frame main-camera ViewProjection in the
    /// camera cbuffer for motion vectors (graphics phase 0.2). Call before the
    /// main-camera SetCamera. Default no-op for backends without history.</summary>
    void SetPrevViewProjection(System.Numerics.Matrix4x4 viewProjection) { }

    /// <summary>Read the current backbuffer contents (screenshots).</summary>
    void ReadBackBuffer(int width, int height, Bgra8[] destination);

    // GPU pass timers (graphics roadmap 9.3). Results arrive with a
    // frames-in-flight delay; backends without timer support no-op.
    void BeginPassTimer(string name) { }
    void EndPassTimer() { }

    /// <summary>Backend debug marker group. Vulkan maps this to VK_EXT_debug_utils; other backends no-op.</summary>
    void PushDebugGroup(string name) { }
    void PopDebugGroup() { }

    /// <summary>Requests capture of the current/next frame. Returns false until a backend capture API is linked.</summary>
    bool RequestFrameCapture(string? outputPath) => false;

    /// <summary>RenderDoc API device pointer for backend-specific in-app captures.</summary>
    IntPtr RenderDocDevicePointer => IntPtr.Zero;

    /// <summary>Device memory held by backend allocators (0 when untracked).</summary>
    long DeviceMemoryAllocated => 0;

    /// <summary>Ray tracing services; null without ray-query support.</summary>
    IRayTracing? RayTracing => null;
    IReadOnlyList<PassTiming> PassTimings => Array.Empty<PassTiming>();
}
