using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using LibreLancer.Graphics.Vertices;

namespace LibreLancer.Graphics.Backends.Vulkan;

/// <summary>
/// Raw Vulkan 1.3 render backend (graphics roadmap, phase 3).
/// Milestone 3.1: instance, device, swapchain and a cleared, presented
/// frame with synchronization2 - the structural skeleton every later
/// milestone (pipelines, resources, draws) plugs into. Resource factories
/// throw until their milestone lands.
/// </summary>
internal unsafe partial class VKRenderContext : IRenderContext
{
    private IntPtr instance;
    private IntPtr physicalDevice;
    private IntPtr device;
    private IntPtr graphicsQueue;
    private uint graphicsFamily;
    private ulong surface;
    private string deviceName = "Vulkan";

    private ulong swapchain;
    private VkFormat swapchainFormat;
    private VkExtent2D swapchainExtent;
    private ulong[] swapchainImages = [];

    // Two slots structurally, but EnsureFrameBegun waits on BOTH fences:
    // the engine (QuadBuffer, bone matrices, per-frame SetData writers)
    // updates persistently-mapped memory in place, so only one frame may
    // be in flight - GL's immediate semantics. Pipelining those writers
    // via transient buffers is a post-parity optimisation.
    private const int FramesInFlight = 2;
    private ulong commandPool;
    private readonly IntPtr[] commandBuffers = new IntPtr[FramesInFlight];
    // Present consumption isn't fence-ordered: rotate acquire semaphores
    // across more slots than frames-in-flight.
    private ulong[] acquireSemaphores = [];
    // Signaled by the frame's submit, consumed by present: present
    // consumption is only ordered against the IMAGE, so these must be
    // per-swapchain-image, not per-frame-in-flight (VUID 03868).
    private ulong[] renderSemaphores = [];
    private readonly ulong[] frameFences = new ulong[FramesInFlight];
    private int frameIndex;

    private GraphicsState applied;
    private Color4 clearColor = Color4.Black;

    // Milestone 3.2: resources, pipelines, frame recording
    public IntPtr Device => device;
    public VKMemory Memory = null!;
    private VkFormat depthFormat = (VkFormat)126; // D32_SFLOAT
    private ulong depthImage;
    private ulong depthView;
    private ulong depthMemory;
    private ulong depthMemoryOffset;
    private ulong depthMemorySize;
    private readonly System.Collections.Generic.List<VKBuffer>[] uniformRings =
        [new(), new()];
    private int uniformRingIndex;
    // Pools grow as a chain when a heavy frame exhausts the current one.
    private readonly System.Collections.Generic.List<ulong>[] descriptorPools =
        [new(), new()];
    private int descriptorPoolIndex;
    private int uniformOffset;
    private const int UniformRingSize = 8 * 1024 * 1024;
    private const ulong UniformAlign = 256;
    // Uniform descriptors are UNIFORM_BUFFER_DYNAMIC with this fixed window
    // (the spec-guaranteed maxUniformBufferRange minimum); draws move through
    // the ring via dynamic offsets, so the sets themselves never change and
    // live forever in their own pool chain.
    private const int UniformBlockRange = 16384;
    private readonly System.Collections.Generic.List<ulong> persistentPools = new();
    private readonly System.Collections.Generic.Dictionary<(int ShaderId, int Frame, int Ring),
        (ulong VertexSet, ulong FragmentSet)> uniformSetCache = new();
    // Texture/sampler/storage sets are content-addressed per frame slot: a
    // draw whose bindings hash to a set already written this frame rebinds
    // it instead of allocating. Reset together with the slot's pools.
    private readonly System.Collections.Generic.Dictionary<TextureSetKey,
        (ulong VertexSet, ulong FragmentSet)>[] textureSetCache =
        [new(TextureSetKeyComparer.Instance), new(TextureSetKeyComparer.Instance)];
    private static readonly bool descriptorStats =
        Environment.GetEnvironmentVariable("SIRIUS_VK_STATS") == "1";

    public IntPtr RenderDocDevicePointer =>
        instance == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(instance);

    // --- GPU pass timers (roadmap 9.3) ---------------------------------
    // 64 timestamp pairs per frame slot; results read back when the slot
    // recycles (the fence wait guarantees availability in practice, but
    // unavailable queries are simply skipped).
    private const int TimerQueriesPerSlot = 128;
    private ulong timestampPool;
    private float timestampPeriod = 1f;
    private readonly System.Collections.Generic.List<(string Name, int Begin, int End)>[] timerSpans =
        [new(), new()];
    private readonly int[] timerQueryCount = new int[FramesInFlight];
    private readonly System.Collections.Generic.Stack<(string Name, int Begin)> timerStack = new();
    private readonly System.Collections.Generic.List<PassTiming> lastTimings = new();

    public System.Collections.Generic.IReadOnlyList<PassTiming> PassTimings => lastTimings;

    private bool debugUtils;
    private int debugLabelDepth;

    public long DeviceMemoryAllocated => (long)Memory.TotalAllocated;

    /// <summary>Names a Vulkan object in captures/validation messages
    /// (VK_EXT_debug_utils; no-op when unavailable).</summary>
    public void SetObjectName(int objectType, ulong handle, string name)
    {
        if (!debugUtils || handle == 0)
        {
            return;
        }
        Span<byte> utf8 = stackalloc byte[256];
        var len = Math.Min(name.Length, 255);
        for (var i = 0; i < len; i++)
        {
            utf8[i] = (byte)name[i];
        }
        utf8[len] = 0;
        fixed (byte* p = utf8)
        {
            var info = new VkDebugUtilsObjectNameInfoEXT
            {
                SType = VkStructureType.DebugUtilsObjectNameInfoEXT,
                ObjectType = objectType,
                ObjectHandle = handle,
                PObjectName = p
            };
            Vk.SetDebugUtilsObjectNameEXT(device, &info);
        }
    }

    private void BeginCmdLabel(string name)
    {
        if (!debugUtils)
        {
            return;
        }
        Span<byte> utf8 = stackalloc byte[128];
        var len = Math.Min(name.Length, 127);
        for (var i = 0; i < len; i++)
        {
            utf8[i] = (byte)name[i];
        }
        utf8[len] = 0;
        fixed (byte* p = utf8)
        {
            var label = new VkDebugUtilsLabelEXT
            {
                SType = VkStructureType.DebugUtilsLabelEXT,
                PLabelName = p
            };
            Vk.CmdBeginDebugUtilsLabelEXT(CurrentCommands, &label);
        }
        debugLabelDepth++;
    }

    private void EndCmdLabel()
    {
        if (!debugUtils || debugLabelDepth == 0)
        {
            return;
        }
        debugLabelDepth--;
        Vk.CmdEndDebugUtilsLabelEXT(CurrentCommands);
    }

    public void BeginPassTimer(string name)
    {
        // Pass labels ride the timer call sites: every BeginPassTimer is a
        // logical pass boundary, and debug labels want to exist even when
        // SIRIUS_PASS_TIMINGS is off (RenderDoc readability).
        if (debugUtils)
        {
            EnsureFrameBegun();
            BeginCmdLabel(name);
        }
        if (timestampPool == 0)
        {
            return;
        }
        // The frame begins lazily on the first draw; a timer can be the
        // first command of the frame (scene pass before any clear lands).
        EnsureFrameBegun();
        if (timerQueryCount[frameIndex] + 2 > TimerQueriesPerSlot)
        {
            return;
        }
        var queryIndex = frameIndex * TimerQueriesPerSlot + timerQueryCount[frameIndex];
        timerQueryCount[frameIndex]++;
        // TOP_OF_PIPE for the begin stamp, so the span covers the pass's
        // own work rather than waiting for earlier stages to drain.
        Vk.CmdWriteTimestamp2(CurrentCommands, 0x1ul, timestampPool, (uint)queryIndex);
        timerStack.Push((name, queryIndex));
    }

    public void EndPassTimer()
    {
        EndCmdLabel();
        if (timestampPool == 0 || timerStack.Count == 0 ||
            timerQueryCount[frameIndex] + 1 > TimerQueriesPerSlot)
        {
            return;
        }
        var (name, begin) = timerStack.Pop();
        var queryIndex = frameIndex * TimerQueriesPerSlot + timerQueryCount[frameIndex];
        timerQueryCount[frameIndex]++;
        Vk.CmdWriteTimestamp2(CurrentCommands, 0x2000ul /*BOTTOM_OF_PIPE*/, timestampPool, (uint)queryIndex);
        timerSpans[frameIndex].Add((name, begin, queryIndex));
    }

    private unsafe void CreateTimestampPool()
    {
        var poolInfo = new VkQueryPoolCreateInfo
        {
            SType = VkStructureType.QueryPoolCreateInfo,
            QueryType = 2, // VK_QUERY_TYPE_TIMESTAMP
            QueryCount = TimerQueriesPerSlot * FramesInFlight
        };
        ulong pool;
        var created = Vk.CreateQueryPool(device, &poolInfo, null, &pool);
        if (created == VkResult.Success)
        {
            timestampPool = pool;
        }
        else
        {
            FLLog.Info("Vulkan", $"Timestamp queries unavailable: {created}");
        }
    }

    /// <summary>
    /// Reads the recycled slot's spans (retired by the fence wait) and
    /// records the reset for this frame's queries. Call with the frame's
    /// command buffer already recording.
    /// </summary>
    private unsafe void CollectPassTimings(IntPtr cmd)
    {
        if (timestampPool == 0)
        {
            return;
        }
        var spans = timerSpans[frameIndex];
        if (spans.Count > 0)
        {
            // (value, availability) pairs for the whole slot.
            var results = stackalloc ulong[TimerQueriesPerSlot * 2];
            var status = Vk.GetQueryPoolResults(device, timestampPool,
                (uint)(frameIndex * TimerQueriesPerSlot), (uint)timerQueryCount[frameIndex],
                (nuint)(timerQueryCount[frameIndex] * 16), results, 16,
                0x1 | 0x4); // 64_BIT | WITH_AVAILABILITY
            lastTimings.Clear();
            if (status == VkResult.Success)
            {
                foreach (var (name, begin, end) in spans)
                {
                    var beginSlot = begin - frameIndex * TimerQueriesPerSlot;
                    var endSlot = end - frameIndex * TimerQueriesPerSlot;
                    if (results[beginSlot * 2 + 1] == 0 || results[endSlot * 2 + 1] == 0)
                    {
                        continue; // not available: skip rather than stall
                    }
                    var nanoseconds = (results[endSlot * 2] - results[beginSlot * 2]) * (double)timestampPeriod;
                    lastTimings.Add(new PassTiming(name, nanoseconds / 1_000_000.0));
                }
            }
            spans.Clear();
        }
        timerQueryCount[frameIndex] = 0;
        timerStack.Clear();
        Vk.CmdResetQueryPool(cmd, timestampPool, (uint)(frameIndex * TimerQueriesPerSlot), TimerQueriesPerSlot);
    }
    private long statDraws, statTextureSetBuilds, statUniformSetBuilds, statFrames;
    private long statTransientHits, statTransientCreates;

    private readonly struct TextureSetKey(ulong[] values, int length, int hash)
    {
        public readonly ulong[] Values = values;
        public readonly int Length = length;
        public readonly int Hash = hash;
    }

    private sealed class TextureSetKeyComparer :
        System.Collections.Generic.IEqualityComparer<TextureSetKey>,
        System.Collections.Generic.IAlternateEqualityComparer<ReadOnlySpan<ulong>, TextureSetKey>
    {
        public static readonly TextureSetKeyComparer Instance = new();

        public bool Equals(TextureSetKey x, TextureSetKey y) =>
            x.Values.AsSpan(0, x.Length).SequenceEqual(y.Values.AsSpan(0, y.Length));

        public int GetHashCode(TextureSetKey key) => key.Hash;

        public bool Equals(ReadOnlySpan<ulong> alternate, TextureSetKey other) =>
            alternate.SequenceEqual(other.Values.AsSpan(0, other.Length));

        public int GetHashCode(ReadOnlySpan<ulong> alternate)
        {
            var hash = new HashCode();
            foreach (var value in alternate)
            {
                hash.Add(value);
            }
            return hash.ToHashCode();
        }

        // Reached on cache misses only, so the copy is cold-path.
        public TextureSetKey Create(ReadOnlySpan<ulong> alternate) =>
            new(alternate.ToArray(), alternate.Length, GetHashCode(alternate));
    }
    private readonly System.Collections.Generic.Dictionary<PipelineKey, ulong> pipelines = new();
    private readonly System.Collections.Generic.Dictionary<SamplerState, ulong> samplers = new();
    // Frame-stamped garbage: an entry queued during frame N is destroyed
    // once N is provably complete (the wait on this slot's fence implies
    // every frame up to frameCounter-FramesInFlight has retired).
    private readonly System.Collections.Generic.Queue<(long Frame, VKBuffer Buffer)> deferredBuffers = new();
    private readonly System.Collections.Generic.Queue<(long Frame, ulong Image, ulong View, ulong Memory, ulong Offset, ulong Size)> deferredImages = new();
    private VKShader? currentShader;
    private readonly VKTexture2D?[] textureSlots = new VKTexture2D?[16];
    private readonly SamplerState[] samplerSlots = new SamplerState[16];
    // Compute UAV slots (RWTexture2D/3D uN -> slot N, image rule 2N).
    private readonly VKTexture2D?[] storageImageSlots = new VKTexture2D?[8];
    private bool frameBegun;
    private bool renderingActive;
    private bool clearPending = true;
    // A deferred clear belongs to the target that was bound when it was
    // requested: the first draw of a frame may hit a DIFFERENT target
    // (UI on the swapchain before the HDR scene pass), and consuming the
    // clear there leaves the scene target with last frame's depth.
    private object? clearPendingTarget;
    private uint currentImage;
    private ulong oneShotFence;
    private ulong readbackFence;
    private ulong oneShotPool;
    // vkQueue and command pools are externally synchronized objects;
    // resource uploads arrive from loader threads.
    private readonly object queueLock = new();

    private readonly record struct PipelineKey(
        int ShaderId, int VertexHash, ushort Blend, bool DepthTest, bool DepthWrite,
        int DepthFunc, bool Cull, CullFaces CullFace, int Topology, VkFormat Color,
        bool Swapchain, bool ColorWrite,
        int ShadingRate, int ExtraColor);

    public static VKRenderContext? Create(IntPtr sdlWindow)
    {
        try
        {
            var context = new VKRenderContext();
            context.Initialize(sdlWindow);
            return context;
        }
        catch (Exception ex)
        {
            FLLog.Error("Vulkan", $"Backend init failed: {ex.Message}");
            return null;
        }
    }

    private IntPtr window;

    private void Initialize(IntPtr sdlWindow)
    {
        window = sdlWindow;
        if (!SDL3.SDL_Vulkan_LoadLibrary(null))
        {
            throw new InvalidOperationException($"SDL_Vulkan_LoadLibrary: {SDL3.SDL_GetError()}");
        }

        Vk.LoadGlobal(SDL3.SDL_Vulkan_GetVkGetInstanceProcAddr());
        CreateInstance();
        Vk.LoadInstance(instance);

        if (!SDL3.SDL_Vulkan_CreateSurface(sdlWindow, instance, IntPtr.Zero, out surface))
        {
            throw new InvalidOperationException($"SDL_Vulkan_CreateSurface: {SDL3.SDL_GetError()}");
        }

        PickPhysicalDevice();
        ProbeRayQuery();
        ProbeMeshShaders();
        ProbeVrs();
        CreateDevice();
        Vk.LoadDevice(device);

        IntPtr queue;
        Vk.GetDeviceQueue(device, graphicsFamily, 0, &queue);
        graphicsQueue = queue;

        CreateSwapchain(sdlWindow);
        CreateFrameResources();
        Memory = new VKMemory(physicalDevice, device);
        Memory.Context = this;
        CreateFrameStreamingResources();
        CreateDepthBuffer();
        CreateFallbackResources();
        CreateTimestampPool();
        FLLog.Info("Vulkan", $"Initialized: {deviceName}, swapchain {swapchainExtent.Width}x{swapchainExtent.Height}, {swapchainImages.Length} images");
    }

    private void CreateFrameStreamingResources()
    {
        var poolSizes = stackalloc VkDescriptorPoolSize[5]
        {
            new() { Type = 0, DescriptorCount = 4096 }, // samplers
            new() { Type = 2, DescriptorCount = 4096 }, // sampled images
            new() { Type = 6, DescriptorCount = 4096 }, // uniform buffers
            new() { Type = 7, DescriptorCount = 512 },  // storage buffers
            new() { Type = 3, DescriptorCount = 256 }   // storage images (compute)
        };
        for (var i = 0; i < FramesInFlight; i++)
        {
            uniformRings[i].Add(Memory.CreateHostBuffer(UniformRingSize, VKMemory.UsageUniform));
            descriptorPools[i].Add(CreateDescriptorPool());
        }
        persistentPools.Add(CreatePersistentDescriptorPool());

        var fenceInfo = new VkFenceCreateInfo { SType = VkStructureType.FenceCreateInfo };
        ulong fence;
        Vk.Check(Vk.CreateFence(device, &fenceInfo, null, &fence), "vkCreateFence");
        oneShotFence = fence;
        Vk.Check(Vk.CreateFence(device, &fenceInfo, null, &fence), "vkCreateFence");
        readbackFence = fence;

        var oneShotPoolInfo = new VkCommandPoolCreateInfo
        {
            SType = VkStructureType.CommandPoolCreateInfo,
            Flags = VkConst.CommandPoolResetCommandBuffer,
            QueueFamilyIndex = graphicsFamily
        };
        ulong transientPool;
        Vk.Check(Vk.CreateCommandPool(device, &oneShotPoolInfo, null, &transientPool), "vkCreateCommandPool(oneshot)");
        oneShotPool = transientPool;
    }

    private VKTexture2D fallbackTexture = null!;
    private VKTextureCube fallbackCube = null!;
    private VKTexture3D fallbackTexture3D = null!;
    private VKTexture3D fallbackStorage3D = null!;
    private VKBuffer zeroVertexBuffer;
    private VKBuffer fallbackStorage;

    private void CreateFallbackResources()
    {
        // Vulkan requires every descriptor in the layout to be written;
        // engine code legitimately leaves slots unbound (GL just samples
        // black). 1x1 white textures and a zeroed uniform slice stand in.
        fallbackTexture = new VKTexture2D(this, 1, 1, false, SurfaceFormat.Bgra8);
        fallbackTexture.SetData(new uint[] { 0xFFFFFFFF });
        fallbackCube = new VKTextureCube(this, 1, false, SurfaceFormat.Bgra8);
        for (var face = 0; face < 6; face++)
        {
            fallbackCube.SetData((CubeMapFace)face, new uint[] { 0xFFFFFFFF });
        }
        // 3D sampled fallback is white (volumetric transmittance 1). It is
        // needed even when compute is disabled because graphics shaders can
        // still declare Texture3D descriptors for their fallback paths.
        fallbackTexture3D = new VKTexture3D(this, 1, 1, 2, SurfaceFormat.HdrBlendable, storage: false);
        fallbackTexture3D.SetData(new ushort[] { 0x3C00, 0x3C00, 0x3C00, 0x3C00, 0x3C00, 0x3C00, 0x3C00, 0x3C00 });
        if (computeSupported)
        {
            // The storage fallback satisfies unbound RWTexture3D bindings.
            fallbackStorage3D = new VKTexture3D(this, 1, 1, 2, SurfaceFormat.HdrBlendable, storage: true);
        }
        // Shader inputs the bound vertex declaration doesn't provide read
        // zeroes from here via a stride-0 binding.
        zeroVertexBuffer = Memory.CreateHostBuffer(64, VKMemory.UsageVertex);
        zeroVertexBuffer.AsSpan().Clear();
        fallbackStorage = Memory.CreateHostBuffer(256, VKMemory.UsageStorage);
        fallbackStorage.AsSpan().Clear();
    }

    private ulong CreateDescriptorPool()
    {
        var poolSizes = stackalloc VkDescriptorPoolSize[6]
        {
            new() { Type = 0, DescriptorCount = 8192 },
            new() { Type = 2, DescriptorCount = 8192 },
            new() { Type = 6, DescriptorCount = 8192 },
            new() { Type = 7, DescriptorCount = 1024 },
            new() { Type = 3, DescriptorCount = 256 },        // storage images (compute)
            new() { Type = 1000150000, DescriptorCount = 64 } // acceleration structures (keep last: count gated)
        };
        var poolInfo = new VkDescriptorPoolCreateInfo
        {
            SType = VkStructureType.DescriptorPoolCreateInfo,
            MaxSets = 8192,
            PoolSizeCount = rayQuerySupported ? 6u : 5u,
            PPoolSizes = poolSizes
        };
        ulong pool;
        Vk.Check(Vk.CreateDescriptorPool(device, &poolInfo, null, &pool), "vkCreateDescriptorPool");
        return pool;
    }

    private ulong CreatePersistentDescriptorPool()
    {
        // Never reset: holds the uniform sets, one pair per
        // shader x ring buffer x frame slot.
        var poolSize = new VkDescriptorPoolSize { Type = 8, DescriptorCount = 8192 }; // UNIFORM_BUFFER_DYNAMIC
        var poolInfo = new VkDescriptorPoolCreateInfo
        {
            SType = VkStructureType.DescriptorPoolCreateInfo,
            MaxSets = 4096,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize
        };
        ulong pool;
        Vk.Check(Vk.CreateDescriptorPool(device, &poolInfo, null, &pool),
            "vkCreateDescriptorPool(persistent)");
        return pool;
    }

    private void CreateDepthBuffer()
    {
        var imageInfo = new VkImageCreateInfo
        {
            SType = VkStructureType.ImageCreateInfo,
            ImageType = 1,
            Format = depthFormat,
            Extent2D = swapchainExtent,
            ExtentDepth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = 1,
            Usage = 0x22, // DEPTH_STENCIL_ATTACHMENT | TRANSFER_DST (initial clear)
            InitialLayout = VkImageLayout.Undefined
        };
        ulong image;
        Vk.Check(Vk.CreateImage(device, &imageInfo, null, &image), "vkCreateImage(depth)");
        depthImage = image;

        (depthMemory, depthMemoryOffset, depthMemorySize) = Memory.AllocateImage(depthImage);

        var viewInfo = new VkImageViewCreateInfo
        {
            SType = VkStructureType.ImageViewCreateInfo,
            Image = depthImage,
            ViewType = 1,
            Format = depthFormat,
            SubresourceRange = new VkImageSubresourceRange { AspectMask = 2, LevelCount = 1, LayerCount = 1 }
        };
        ulong view;
        Vk.Check(Vk.CreateImageView(device, &viewInfo, null, &view), "vkCreateImageView(depth)");
        depthView = view;

        // Depth is rendered to from the first frame, and pool memory is
        // recycled without scrubbing: a recreated buffer (window resize /
        // OUT_OF_DATE swapchain rebuild) would otherwise depth-test against
        // stale garbage until the next full clear. Clear it up front.
        var cmd = BeginOneShotCommands();
        ImageBarrier(cmd, depthImage, VkImageLayout.Undefined,
            VkImageLayout.TransferDstOptimal, 0, 0, aspect: 2);
        var depthClear = new VkClearDepthStencilValue { Depth = 1f, Stencil = 0 };
        var depthRange = new VkImageSubresourceRange { AspectMask = 2, LevelCount = 1, LayerCount = 1 };
        Vk.CmdClearDepthStencilImage(cmd, depthImage, VkImageLayout.TransferDstOptimal, &depthClear, 1, &depthRange);
        ImageBarrier(cmd, depthImage, VkImageLayout.TransferDstOptimal,
            (VkImageLayout)3 /*DEPTH_STENCIL_ATTACHMENT_OPTIMAL*/, 0, 0, aspect: 2);
        EndOneShotCommands(cmd);
    }

    /// <summary>Begin a transient command buffer for resource uploads.
    /// The pool/queue pair is guarded by a lock: uploads run on loader
    /// threads while the main thread records frames.</summary>
    public IntPtr BeginOneShotCommands()
    {
        System.Threading.Monitor.Enter(queueLock);
        var allocateInfo = new VkCommandBufferAllocateInfo
        {
            SType = VkStructureType.CommandBufferAllocateInfo,
            CommandPool = oneShotPool,
            Level = 0,
            CommandBufferCount = 1
        };
        IntPtr cmd;
        Vk.Check(Vk.AllocateCommandBuffers(device, &allocateInfo, &cmd), "vkAllocateCommandBuffers(oneshot)");
        var beginInfo = new VkCommandBufferBeginInfo
        {
            SType = VkStructureType.CommandBufferBeginInfo,
            Flags = 1
        };
        Vk.BeginCommandBuffer(cmd, &beginInfo);
        return cmd;
    }

    /// <summary>Submit a one-shot buffer without blocking on the GPU.</summary>
    public void EndOneShotCommands(IntPtr cmd) => EndOneShotCommands(cmd, default);

    // Uploads in flight: completion is reaped lazily (fence pool), the
    // optional staging buffer is destroyed once the GPU is done with it.
    private readonly Queue<(ulong Fence, IntPtr Cmd, VKBuffer Staging)> pendingOneShots = new();
    private readonly Stack<ulong> oneShotFencePool = new();

    /// <summary>
    /// Submit a one-shot buffer; ownership of <paramref name="staging"/>
    /// (if any) passes to the reaper. No synchronous wait: loader threads
    /// used to hold the queue lock for a full GPU round-trip per texture,
    /// stalling the main thread's submit/present mid-flight (streaming
    /// stutter). Same-queue submission order still puts every upload ahead
    /// of any frame that samples the resource.
    /// </summary>
    public void EndOneShotCommands(IntPtr cmd, VKBuffer staging)
    {
        try
        {
            Vk.EndCommandBuffer(cmd);
            ReapOneShots();
            if (pendingOneShots.Count >= 64)
            {
                // Bound in-flight uploads; waiting on the oldest fence keeps
                // staging memory from ballooning during heavy streaming.
                var oldest = pendingOneShots.Peek().Fence;
                Vk.WaitForFences(device, 1, &oldest, 1, ulong.MaxValue);
                ReapOneShots();
            }
            ulong fence;
            if (oneShotFencePool.Count > 0)
            {
                fence = oneShotFencePool.Pop();
            }
            else
            {
                var fenceInfo = new VkFenceCreateInfo { SType = VkStructureType.FenceCreateInfo };
                ulong created;
                Vk.Check(Vk.CreateFence(device, &fenceInfo, null, &created), "vkCreateFence(oneshot)");
                fence = created;
            }
            var cmdInfo = new VkCommandBufferSubmitInfo
            {
                SType = VkStructureType.CommandBufferSubmitInfo,
                CommandBuffer = cmd
            };
            var submit = new VkSubmitInfo2
            {
                SType = VkStructureType.SubmitInfo2,
                CommandBufferInfoCount = 1,
                PCommandBufferInfos = &cmdInfo
            };
            Vk.Check(Vk.QueueSubmit2(graphicsQueue, 1, &submit, fence), "vkQueueSubmit2(oneshot)");
            pendingOneShots.Enqueue((fence, cmd, staging));
        }
        finally
        {
            System.Threading.Monitor.Exit(queueLock);
        }
    }

    /// <summary>Recycle finished one-shot submissions. Call under queueLock.</summary>
    private void ReapOneShots()
    {
        while (pendingOneShots.Count > 0)
        {
            var (fence, cmd, staging) = pendingOneShots.Peek();
            if (Vk.GetFenceStatus(device, fence) != VkResult.Success)
            {
                break;
            }
            pendingOneShots.Dequeue();
            Vk.ResetFences(device, 1, &fence);
            oneShotFencePool.Push(fence);
            Vk.FreeCommandBuffers(device, oneShotPool, 1, &cmd);
            if (staging.Buffer != 0)
            {
                Memory.Destroy(staging);
            }
        }
    }

    public void ImageBarrier(IntPtr cmd, ulong image, VkImageLayout from, VkImageLayout to, uint level, uint layer,
        uint aspect = 1)
    {
        var barrier = new VkImageMemoryBarrier2
        {
            SType = VkStructureType.ImageMemoryBarrier2,
            SrcStageMask = VkConst.StageAllCommands,
            SrcAccessMask = VkConst.AccessMemoryWrite,
            DstStageMask = VkConst.StageAllCommands,
            DstAccessMask = VkConst.AccessMemoryRead | VkConst.AccessMemoryWrite,
            OldLayout = from,
            NewLayout = to,
            Image = image,
            SubresourceRange = new VkImageSubresourceRange
            {
                AspectMask = aspect,
                BaseMipLevel = level,
                LevelCount = 1,
                BaseArrayLayer = layer,
                LayerCount = 1
            }
        };
        var dependency = new VkDependencyInfo
        {
            SType = VkStructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &barrier
        };
        Vk.CmdPipelineBarrier2(cmd, &dependency);
    }

    public void ImageBarrierAll(IntPtr cmd, ulong image, VkImageLayout from, VkImageLayout to,
        uint levels, uint layers)
    {
        var barrier = new VkImageMemoryBarrier2
        {
            SType = VkStructureType.ImageMemoryBarrier2,
            SrcStageMask = VkConst.StageAllCommands,
            SrcAccessMask = VkConst.AccessMemoryWrite,
            DstStageMask = VkConst.StageAllCommands,
            DstAccessMask = VkConst.AccessMemoryRead | VkConst.AccessMemoryWrite,
            OldLayout = from,
            NewLayout = to,
            Image = image,
            SubresourceRange = new VkImageSubresourceRange
            {
                AspectMask = 1,
                LevelCount = levels,
                LayerCount = layers
            }
        };
        var dependency = new VkDependencyInfo
        {
            SType = VkStructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &barrier
        };
        Vk.CmdPipelineBarrier2(cmd, &dependency);
    }

    public void DeferDestroy(in VKBuffer buffer)
    {
        lock (queueLock)
        {
            deferredBuffers.Enqueue((frameCounter, buffer));
        }
    }

    // --- Transient buffer pool -------------------------------------------
    // 2D streaming batches and busy-buffer SetData staging need a fresh GPU
    // buffer many times per frame; create+deferred-destroy puts a driver
    // allocation in the hottest paths. Retired transients park in free
    // lists keyed by (usage, power-of-two size class) and get handed back
    // out. Rent always sizes the buffer to its class so the key survives
    // the round trip.
    private readonly System.Collections.Generic.Dictionary<(uint Usage, ulong SizeClass),
        System.Collections.Generic.Stack<VKBuffer>> freeTransients = new();
    private readonly System.Collections.Generic.Queue<(long Frame, VKBuffer Buffer, uint Usage)>
        pendingTransients = new();
    private long pooledTransientBytes;
    // Generous for per-frame traffic; a loading spike falls back to
    // plain destruction instead of pinning host memory forever.
    private const long MaxPooledTransientBytes = 64 * 1024 * 1024;

    internal VKBuffer RentTransient(ulong size, uint usage)
    {
        var sizeClass = TransientSizeClass(size);
        lock (queueLock)
        {
            if (freeTransients.TryGetValue((usage, sizeClass), out var stack) && stack.Count > 0)
            {
                var pooled = stack.Pop();
                pooledTransientBytes -= (long)pooled.Size;
                statTransientHits++;
                return pooled;
            }
        }
        statTransientCreates++;
        return Memory.CreateHostBuffer(sizeClass, usage);
    }

    /// <summary>Only for buffers obtained from RentTransient.</summary>
    internal void RecycleTransient(in VKBuffer buffer, uint usage)
    {
        lock (queueLock)
        {
            pendingTransients.Enqueue((frameCounter, buffer, usage));
        }
    }

    private static ulong TransientSizeClass(ulong size)
    {
        var sizeClass = 4096ul;
        while (sizeClass < size)
        {
            sizeClass <<= 1;
        }
        return sizeClass;
    }

    public void DeferDestroyImage(ulong image, ulong view, ulong memory, ulong offset, ulong size)
    {
        lock (queueLock)
        {
            deferredImages.Enqueue((frameCounter, image, view, memory, offset, size));
        }
    }

    private void CreateInstance()
    {
        var sdlExtensions = SDL3.SDL_Vulkan_GetInstanceExtensions(out var extensionCount);
        // Request VK_EXT_debug_utils on top of SDL's surface extensions:
        // object names + pass labels make RenderDoc/Nsight captures and
        // validation messages readable (roadmap 9.1). Falls back cleanly
        // when the loader doesn't expose it.
        var debugUtilsName = "VK_EXT_debug_utils\0"u8;
        var appName = "Project Sirius\0"u8;
        var engineName = "LibreLancer\0"u8;
        fixed (byte* pAppName = appName)
        fixed (byte* pEngineName = engineName)
        {
            var appInfo = new VkApplicationInfo
            {
                SType = VkStructureType.ApplicationInfo,
                PApplicationName = pAppName,
                ApplicationVersion = 1,
                PEngineName = pEngineName,
                EngineVersion = 1,
                ApiVersion = (1u << 22) | (3u << 12) // VK_API_VERSION_1_3
            };

            var validation = Environment.GetEnvironmentVariable("SIRIUS_VK_VALIDATION") == "1";
            var layerName = "VK_LAYER_KHRONOS_validation\0"u8;
            fixed (byte* pLayerName = layerName)
            fixed (byte* pDebugUtils = debugUtilsName)
            {
                var layers = stackalloc byte*[1] { pLayerName };
                var extensions = stackalloc byte*[(int)extensionCount + 1];
                for (var i = 0; i < extensionCount; i++)
                {
                    extensions[i] = sdlExtensions[i];
                }
                extensions[extensionCount] = pDebugUtils;

                var createInfo = new VkInstanceCreateInfo
                {
                    SType = VkStructureType.InstanceCreateInfo,
                    PApplicationInfo = &appInfo,
                    EnabledExtensionCount = extensionCount + 1,
                    PpEnabledExtensionNames = extensions,
                    EnabledLayerCount = validation ? 1u : 0u,
                    PpEnabledLayerNames = validation ? layers : null
                };

                IntPtr created;
                var result = Vk.CreateInstance(&createInfo, null, &created);
                if (result == VkResult.ErrorExtensionNotPresent)
                {
                    FLLog.Warning("Vulkan", "VK_EXT_debug_utils unavailable, continuing without debug names");
                    createInfo.EnabledExtensionCount = extensionCount;
                    createInfo.PpEnabledExtensionNames = sdlExtensions;
                    result = Vk.CreateInstance(&createInfo, null, &created);
                }
                if (validation && result != VkResult.Success)
                {
                    // VK_ERROR_LAYER_NOT_PRESENT and friends: run without
                    // validation rather than refusing to start.
                    FLLog.Warning("Vulkan", $"Validation layer unavailable ({result}), continuing without it");
                    createInfo.EnabledLayerCount = 0;
                    createInfo.PpEnabledLayerNames = null;
                    result = Vk.CreateInstance(&createInfo, null, &created);
                }
                Vk.Check(result, "vkCreateInstance");
                instance = created;
                debugUtils = createInfo.EnabledExtensionCount == extensionCount + 1 && Vk.LoadDebugUtils(created);
                if (debugUtils)
                {
                    FLLog.Info("Vulkan", "Debug utils enabled (object names + pass labels)");
                }
                if (validation && createInfo.EnabledLayerCount == 1)
                {
                    FLLog.Info("Vulkan", "Validation layer enabled (SIRIUS_VK_VALIDATION=1)");
                }
            }
        }
    }

    private void PickPhysicalDevice()
    {
        uint count = 0;
        Vk.Check(Vk.EnumeratePhysicalDevices(instance, &count, null), "vkEnumeratePhysicalDevices");
        if (count == 0)
        {
            throw new InvalidOperationException("No Vulkan devices present");
        }

        var devices = stackalloc IntPtr[(int)count];
        Vk.Check(Vk.EnumeratePhysicalDevices(instance, &count, devices), "vkEnumeratePhysicalDevices");

        // VkPhysicalDeviceProperties: deviceType at byte offset 16,
        // deviceName (256 chars) at offset 20.
        var properties = stackalloc byte[824];
        var best = IntPtr.Zero;
        var bestScore = -1;
        for (var i = 0; i < count; i++)
        {
            Vk.GetPhysicalDeviceProperties(devices[i], properties);
            var deviceType = *(int*)(properties + 16);
            var score = deviceType == 2 ? 100 : deviceType == 1 ? 50 : 10; // discrete > integrated > other
            if (score > bestScore && FindGraphicsFamily(devices[i]) >= 0)
            {
                bestScore = score;
                best = devices[i];
                deviceName = Marshal.PtrToStringUTF8((IntPtr)(properties + 20)) ?? "Vulkan device";
                // limits.timestampPeriod: limits start at 292 (20 + 256 name
                // + 16 uuid), the float sits 424 bytes in. Sanity-check the
                // hand-computed offset rather than trusting it blindly.
                var period = *(float*)(properties + 716);
                timestampPeriod = period is > 0.001f and < 10000f ? period : 1f;
            }
        }

        physicalDevice = best != IntPtr.Zero
            ? best
            : throw new InvalidOperationException("No Vulkan device with graphics+present support");
        graphicsFamily = (uint)FindGraphicsFamily(physicalDevice);
        // Compute rides on the graphics queue (no async compute yet). The
        // spec all but guarantees graphics+compute on desktop; probe anyway.
        computeSupported = (selectedFamilyFlags & 0x2) != 0;
        if (Environment.GetEnvironmentVariable("SIRIUS_NO_COMPUTE") == "1")
        {
            computeSupported = false;
            FLLog.Info("Vulkan", "Compute: disabled (SIRIUS_NO_COMPUTE=1)");
        }
        else
        {
            FLLog.Info("Vulkan", computeSupported
                ? "Compute: supported"
                : "Compute: queue family lacks compute bit, feature disabled");
        }
    }

    private uint selectedFamilyFlags;
    private bool computeSupported;

    private int FindGraphicsFamily(IntPtr gpu)
    {
        uint familyCount = 0;
        Vk.GetPhysicalDeviceQueueFamilyProperties(gpu, &familyCount, null);
        // VkQueueFamilyProperties is 24 bytes; queueFlags is the first field.
        var families = stackalloc byte[(int)(familyCount * 24)];
        Vk.GetPhysicalDeviceQueueFamilyProperties(gpu, &familyCount, families);
        for (uint i = 0; i < familyCount; i++)
        {
            var flags = *(uint*)(families + i * 24);
            if ((flags & VkConst.QueueGraphicsBit) == 0)
            {
                continue;
            }
            uint presentSupport = 0;
            Vk.GetPhysicalDeviceSurfaceSupportKHR(gpu, i, surface, &presentSupport);
            if (presentSupport != 0)
            {
                selectedFamilyFlags = flags;
                return (int)i;
            }
        }
        return -1;
    }

    private bool rayQuerySupported;
    private uint asScratchAlignment = 256;

    public bool RayQuerySupported => rayQuerySupported;
    public uint AccelerationScratchAlignment => asScratchAlignment;

    /// <summary>
    /// Probes the ray-query path (roadmap phase 4): device extensions
    /// VK_KHR_acceleration_structure/ray_query/deferred_host_operations and
    /// the accelerationStructure+rayQuery+bufferDeviceAddress features.
    /// SIRIUS_NO_RT=1 simulates an unsupported device.
    /// </summary>
    private void ProbeRayQuery()
    {
        rayQuerySupported = false;
        if (Environment.GetEnvironmentVariable("SIRIUS_NO_RT") == "1")
        {
            FLLog.Info("Vulkan", "Ray query: disabled (SIRIUS_NO_RT=1)");
            return;
        }

        uint count = 0;
        if (Vk.EnumerateDeviceExtensionProperties(physicalDevice, null, &count, null) != VkResult.Success || count == 0)
        {
            FLLog.Info("Vulkan", "Ray query: unavailable (no device extensions)");
            return;
        }
        var props = new VkExtensionProperties[count];
        fixed (VkExtensionProperties* pProps = props)
        {
            Vk.EnumerateDeviceExtensionProperties(physicalDevice, null, &count, pProps);
        }
        bool hasAccel = false, hasRayQuery = false, hasDeferred = false;
        fixed (VkExtensionProperties* pAll = props)
        {
            for (var i = 0; i < count; i++)
            {
                var name = Marshal.PtrToStringUTF8((IntPtr)pAll[i].ExtensionName) ?? "";
                hasAccel |= name == "VK_KHR_acceleration_structure";
                hasRayQuery |= name == "VK_KHR_ray_query";
                hasDeferred |= name == "VK_KHR_deferred_host_operations";
            }
        }
        if (!hasAccel || !hasRayQuery || !hasDeferred)
        {
            FLLog.Info("Vulkan", "Ray query: unavailable (extensions missing)");
            return;
        }

        var asFeatures = new VkPhysicalDeviceAccelerationStructureFeaturesKHR
        {
            SType = VkStructureType.PhysicalDeviceAccelerationStructureFeaturesKHR
        };
        var rqFeatures = new VkPhysicalDeviceRayQueryFeaturesKHR
        {
            SType = VkStructureType.PhysicalDeviceRayQueryFeaturesKHR,
            PNext = &asFeatures
        };
        var features12 = new VkPhysicalDeviceVulkan12Features
        {
            SType = VkStructureType.PhysicalDeviceVulkan12Features,
            PNext = &rqFeatures
        };
        var features2 = new VkPhysicalDeviceFeatures2
        {
            SType = VkStructureType.PhysicalDeviceFeatures2,
            PNext = &features12
        };
        Vk.GetPhysicalDeviceFeatures2(physicalDevice, &features2);
        if (asFeatures.AccelerationStructure == 0 || rqFeatures.RayQuery == 0 ||
            features12.Features[VkPhysicalDeviceVulkan12Features.BufferDeviceAddress] == 0)
        {
            FLLog.Info("Vulkan", "Ray query: unavailable (features missing)");
            return;
        }

        var asProps = new VkPhysicalDeviceAccelerationStructurePropertiesKHR
        {
            SType = VkStructureType.PhysicalDeviceAccelerationStructurePropertiesKHR
        };
        var props2 = new VkPhysicalDeviceProperties2
        {
            SType = VkStructureType.PhysicalDeviceProperties2,
            PNext = &asProps
        };
        Vk.GetPhysicalDeviceProperties2(physicalDevice, &props2);
        asScratchAlignment = Math.Max(asProps.MinAccelerationStructureScratchOffsetAlignment, 1u);

        rayQuerySupported = true;
        FLLog.Info("Vulkan",
            $"Ray query: supported (scratch alignment {asScratchAlignment}, max instances {asProps.MaxInstanceCount})");
    }

    private bool meshShadersSupported;
    private uint maxMeshOutputVertices, maxMeshOutputPrimitives;
    private bool vrsSupported;

    private void ProbeVrs()
    {
        vrsSupported = false;
        if (Environment.GetEnvironmentVariable("SIRIUS_NO_VRS") == "1")
        {
            FLLog.Info("Vulkan", "VRS: disabled (SIRIUS_NO_VRS=1)");
            return;
        }
        uint count = 0;
        if (Vk.EnumerateDeviceExtensionProperties(physicalDevice, null, &count, null) != VkResult.Success || count == 0)
        {
            return;
        }
        var props = new VkExtensionProperties[count];
        fixed (VkExtensionProperties* pProps = props)
        {
            Vk.EnumerateDeviceExtensionProperties(physicalDevice, null, &count, pProps);
        }
        var hasVrs = false;
        fixed (VkExtensionProperties* pAll = props)
        {
            for (var i = 0; i < count; i++)
            {
                if ((Marshal.PtrToStringUTF8((IntPtr)pAll[i].ExtensionName) ?? "") == "VK_KHR_fragment_shading_rate")
                {
                    hasVrs = true;
                    break;
                }
            }
        }
        if (!hasVrs)
        {
            FLLog.Info("Vulkan", "VRS: unavailable (extension missing)");
            return;
        }
        var vrsFeatures = new VkPhysicalDeviceFragmentShadingRateFeaturesKHR
        {
            SType = VkStructureType.PhysicalDeviceFragmentShadingRateFeaturesKHR
        };
        var features2 = new VkPhysicalDeviceFeatures2
        {
            SType = VkStructureType.PhysicalDeviceFeatures2,
            PNext = &vrsFeatures
        };
        Vk.GetPhysicalDeviceFeatures2(physicalDevice, &features2);
        if (vrsFeatures.PipelineFragmentShadingRate == 0)
        {
            FLLog.Info("Vulkan", "VRS: unavailable (pipeline rate not supported)");
            return;
        }
        vrsSupported = true;
        FLLog.Info("Vulkan",
            $"VRS: supported (pipeline rate; attachment tier {(vrsFeatures.AttachmentFragmentShadingRate != 0 ? "yes" : "no")})");
        // The feature bit alone doesn't tell which coarse sizes the driver
        // accepts - enumerate them so a missing 2x2 is visible in the log.
        if (Vk.GetPhysicalDeviceFragmentShadingRatesKHR != null)
        {
            uint rateCount = 0;
            Vk.GetPhysicalDeviceFragmentShadingRatesKHR(physicalDevice, &rateCount, null);
            if (rateCount > 0)
            {
                var rates = new VkPhysicalDeviceFragmentShadingRateKHR[rateCount];
                for (var i = 0; i < rateCount; i++)
                    rates[i].SType = (VkStructureType)1000226004; // PHYSICAL_DEVICE_FRAGMENT_SHADING_RATE_KHR
                fixed (VkPhysicalDeviceFragmentShadingRateKHR* pRates = rates)
                {
                    Vk.GetPhysicalDeviceFragmentShadingRatesKHR(physicalDevice, &rateCount, pRates);
                }
                var sb = new System.Text.StringBuilder();
                for (var i = 0; i < rateCount; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append($"{rates[i].FragmentSizeWidth}x{rates[i].FragmentSizeHeight}(s{rates[i].SampleCounts:X})");
                }
                FLLog.Info("Vulkan", $"VRS: available rates: {sb}");
            }
        }
    }

    private void ProbeMeshShaders()
    {
        meshShadersSupported = false;
        if (Environment.GetEnvironmentVariable("SIRIUS_NO_MS") == "1")
        {
            FLLog.Info("Vulkan", "Mesh shaders: disabled (SIRIUS_NO_MS=1)");
            return;
        }
        uint count = 0;
        if (Vk.EnumerateDeviceExtensionProperties(physicalDevice, null, &count, null) != VkResult.Success || count == 0)
        {
            FLLog.Info("Vulkan", "Mesh shaders: unavailable (no device extensions)");
            return;
        }
        var props = new VkExtensionProperties[count];
        fixed (VkExtensionProperties* pProps = props)
        {
            Vk.EnumerateDeviceExtensionProperties(physicalDevice, null, &count, pProps);
        }
        var hasMesh = false;
        fixed (VkExtensionProperties* pAll = props)
        {
            for (var i = 0; i < count; i++)
            {
                if ((Marshal.PtrToStringUTF8((IntPtr)pAll[i].ExtensionName) ?? "") == "VK_EXT_mesh_shader")
                {
                    hasMesh = true;
                    break;
                }
            }
        }
        if (!hasMesh)
        {
            FLLog.Info("Vulkan", "Mesh shaders: unavailable (VK_EXT_mesh_shader missing)");
            return;
        }

        var meshFeatures = new VkPhysicalDeviceMeshShaderFeaturesEXT
        {
            SType = VkStructureType.PhysicalDeviceMeshShaderFeaturesEXT
        };
        var features2 = new VkPhysicalDeviceFeatures2
        {
            SType = VkStructureType.PhysicalDeviceFeatures2,
            PNext = &meshFeatures
        };
        Vk.GetPhysicalDeviceFeatures2(physicalDevice, &features2);
        if (meshFeatures.MeshShader == 0 || meshFeatures.TaskShader == 0)
        {
            FLLog.Info("Vulkan", "Mesh shaders: unavailable (features not supported)");
            return;
        }

        var meshProps = new VkPhysicalDeviceMeshShaderPropertiesEXT
        {
            SType = VkStructureType.PhysicalDeviceMeshShaderPropertiesEXT
        };
        var props2 = new VkPhysicalDeviceProperties2
        {
            SType = VkStructureType.PhysicalDeviceProperties2,
            PNext = &meshProps
        };
        Vk.GetPhysicalDeviceProperties2(physicalDevice, &props2);
        maxMeshOutputVertices = meshProps.Values[VkPhysicalDeviceMeshShaderPropertiesEXT.MaxMeshOutputVertices];
        maxMeshOutputPrimitives = meshProps.Values[VkPhysicalDeviceMeshShaderPropertiesEXT.MaxMeshOutputPrimitives];
        meshShadersSupported = true;
        FLLog.Info("Vulkan",
            $"Mesh shaders: supported (max output {maxMeshOutputVertices} verts / {maxMeshOutputPrimitives} prims)");
    }

    private void CreateDevice()
    {
        var priority = 1f;
        var queueInfo = new VkDeviceQueueCreateInfo
        {
            SType = VkStructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = graphicsFamily,
            QueueCount = 1,
            PQueuePriorities = &priority
        };

        var features13 = new VkPhysicalDeviceVulkan13Features
        {
            SType = VkStructureType.PhysicalDeviceVulkan13Features,
            Synchronization2 = 1,
            DynamicRendering = 1
        };
        // VRS chain (roadmap 7.6), only when probed as supported.
        var vrsFeatures = new VkPhysicalDeviceFragmentShadingRateFeaturesKHR
        {
            SType = VkStructureType.PhysicalDeviceFragmentShadingRateFeaturesKHR,
            PNext = &features13,
            PipelineFragmentShadingRate = 1
        };
        void* afterVrs = vrsSupported ? &vrsFeatures : (void*)&features13;
        // Mesh shader chain (roadmap 7.5), only when probed as supported.
        var meshFeatures = new VkPhysicalDeviceMeshShaderFeaturesEXT
        {
            SType = VkStructureType.PhysicalDeviceMeshShaderFeaturesEXT,
            PNext = afterVrs,
            TaskShader = 1,
            MeshShader = 1
        };
        void* afterMesh = meshShadersSupported ? &meshFeatures : afterVrs;
        // Ray-query chain (roadmap phase 4), only when probed as supported:
        // Vulkan12(BDA + descriptor indexing) -> AS -> ray query -> 1.3.
        var asFeatures = new VkPhysicalDeviceAccelerationStructureFeaturesKHR
        {
            SType = VkStructureType.PhysicalDeviceAccelerationStructureFeaturesKHR,
            PNext = afterMesh,
            AccelerationStructure = 1
        };
        var rqFeatures = new VkPhysicalDeviceRayQueryFeaturesKHR
        {
            SType = VkStructureType.PhysicalDeviceRayQueryFeaturesKHR,
            PNext = &asFeatures,
            RayQuery = 1
        };
        var features12 = new VkPhysicalDeviceVulkan12Features
        {
            SType = VkStructureType.PhysicalDeviceVulkan12Features,
            PNext = &rqFeatures
        };
        features12.Features[VkPhysicalDeviceVulkan12Features.BufferDeviceAddress] = 1;
        features12.Features[VkPhysicalDeviceVulkan12Features.DescriptorIndexing] = 1;

        var features2 = new VkPhysicalDeviceFeatures2
        {
            SType = VkStructureType.PhysicalDeviceFeatures2,
            PNext = rayQuerySupported ? &features12 : afterMesh
        };

        var swapchainExtension = "VK_KHR_swapchain\0"u8;
        var accelExtension = "VK_KHR_acceleration_structure\0"u8;
        var rayQueryExtension = "VK_KHR_ray_query\0"u8;
        var deferredExtension = "VK_KHR_deferred_host_operations\0"u8;
        var meshExtension = "VK_EXT_mesh_shader\0"u8;
        var vrsExtension = "VK_KHR_fragment_shading_rate\0"u8;
        fixed (byte* pSwapchainExtension = swapchainExtension)
        fixed (byte* pAccel = accelExtension)
        fixed (byte* pRayQuery = rayQueryExtension)
        fixed (byte* pDeferred = deferredExtension)
        fixed (byte* pMesh = meshExtension)
        fixed (byte* pVrs = vrsExtension)
        {
            // Compacted extension list: optional entries appended in order.
            var extensions = stackalloc byte*[6];
            extensions[0] = pSwapchainExtension;
            var extCount = 1u;
            if (rayQuerySupported)
            {
                extensions[extCount++] = pAccel;
                extensions[extCount++] = pRayQuery;
                extensions[extCount++] = pDeferred;
            }
            if (meshShadersSupported)
            {
                extensions[extCount++] = pMesh;
            }
            if (vrsSupported)
            {
                extensions[extCount++] = pVrs;
            }
            var createInfo = new VkDeviceCreateInfo
            {
                SType = VkStructureType.DeviceCreateInfo,
                PNext = &features2,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueInfo,
                EnabledExtensionCount = extCount,
                PpEnabledExtensionNames = extensions
            };

            IntPtr created;
            Vk.Check(Vk.CreateDevice(physicalDevice, &createInfo, null, &created), "vkCreateDevice");
            device = created;
        }

        if (rayQuerySupported && !Vk.LoadDeviceRT(device))
        {
            FLLog.Warning("Vulkan", "Ray query entry points missing after device creation; disabling");
            rayQuerySupported = false;
        }
        if (meshShadersSupported && !Vk.LoadDeviceMesh(device))
        {
            FLLog.Warning("Vulkan", "Mesh shader entry points missing after device creation; disabling");
            meshShadersSupported = false;
        }
        if (vrsSupported && !Vk.LoadDeviceVrs(device))
        {
            FLLog.Warning("Vulkan", "VRS entry point missing after device creation; disabling");
            vrsSupported = false;
        }
    }

    private void CreateSwapchain(IntPtr sdlWindow)
    {
        VkSurfaceCapabilitiesKHR caps;
        Vk.Check(Vk.GetPhysicalDeviceSurfaceCapabilitiesKHR(physicalDevice, surface, &caps),
            "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");

        uint formatCount = 0;
        Vk.GetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, surface, &formatCount, null);
        var formats = stackalloc VkSurfaceFormatKHR[(int)formatCount];
        Vk.GetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, surface, &formatCount, formats);

        var chosen = formats[0];
        for (var i = 0; i < formatCount; i++)
        {
            if (formats[i].Format == VkFormat.B8G8R8A8Unorm)
            {
                chosen = formats[i];
                break;
            }
        }

        var extent = caps.CurrentExtent;
        if (extent.Width == uint.MaxValue)
        {
            SDL3.SDL_GetWindowSizeInPixels(sdlWindow, out var w, out var h);
            extent = new VkExtent2D { Width = (uint)w, Height = (uint)h };
        }

        var imageCount = Math.Max(caps.MinImageCount + 1, 2u);
        if (caps.MaxImageCount > 0 && imageCount > caps.MaxImageCount)
        {
            imageCount = caps.MaxImageCount;
        }

        var createInfo = new VkSwapchainCreateInfoKHR
        {
            SType = VkStructureType.SwapchainCreateInfoKHR,
            Surface = surface,
            MinImageCount = imageCount,
            ImageFormat = chosen.Format,
            ImageColorSpace = chosen.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            // TRANSFER_SRC: screenshots read the presented image back.
            ImageUsage = VkConst.ImageUsageColorAttachment | VkConst.ImageUsageTransferDst | 0x1,
            ImageSharingMode = 0,
            PreTransform = caps.CurrentTransform,
            CompositeAlpha = VkConst.CompositeAlphaOpaque,
            PresentMode = VkConst.PresentModeFifo,
            Clipped = 1
        };

        ulong created;
        Vk.Check(Vk.CreateSwapchainKHR(device, &createInfo, null, &created), "vkCreateSwapchainKHR");
        swapchain = created;
        swapchainFormat = chosen.Format;
        swapchainExtent = extent;

        uint actualCount = 0;
        Vk.GetSwapchainImagesKHR(device, swapchain, &actualCount, null);
        swapchainImages = new ulong[actualCount];
        fixed (ulong* pImages = swapchainImages)
        {
            Vk.GetSwapchainImagesKHR(device, swapchain, &actualCount, pImages);
        }
    }

    private void CreateFrameResources()
    {
        var poolInfo = new VkCommandPoolCreateInfo
        {
            SType = VkStructureType.CommandPoolCreateInfo,
            Flags = VkConst.CommandPoolResetCommandBuffer,
            QueueFamilyIndex = graphicsFamily
        };
        ulong pool;
        Vk.Check(Vk.CreateCommandPool(device, &poolInfo, null, &pool), "vkCreateCommandPool");
        commandPool = pool;

        var allocateInfo = new VkCommandBufferAllocateInfo
        {
            SType = VkStructureType.CommandBufferAllocateInfo,
            CommandPool = commandPool,
            Level = 0,
            CommandBufferCount = FramesInFlight
        };
        fixed (IntPtr* pBuffers = commandBuffers)
        {
            Vk.Check(Vk.AllocateCommandBuffers(device, &allocateInfo, pBuffers), "vkAllocateCommandBuffers");
        }

        var fenceInfo = new VkFenceCreateInfo
        {
            SType = VkStructureType.FenceCreateInfo,
            Flags = VkConst.FenceCreateSignaled
        };
        for (var i = 0; i < FramesInFlight; i++)
        {
            ulong fence;
            Vk.Check(Vk.CreateFence(device, &fenceInfo, null, &fence), "vkCreateFence");
            frameFences[i] = fence;
        }

        CreateSwapchainSemaphores();
    }

    private void CreateSwapchainSemaphores()
    {
        var semaphoreInfo = new VkSemaphoreCreateInfo { SType = VkStructureType.SemaphoreCreateInfo };
        renderSemaphores = new ulong[swapchainImages.Length];
        acquireSemaphores = new ulong[swapchainImages.Length + 1];
        for (var i = 0; i < renderSemaphores.Length; i++)
        {
            ulong semaphore;
            Vk.Check(Vk.CreateSemaphore(device, &semaphoreInfo, null, &semaphore), "vkCreateSemaphore");
            renderSemaphores[i] = semaphore;
        }
        for (var i = 0; i < acquireSemaphores.Length; i++)
        {
            ulong semaphore;
            Vk.Check(Vk.CreateSemaphore(device, &semaphoreInfo, null, &semaphore), "vkCreateSemaphore");
            acquireSemaphores[i] = semaphore;
        }
    }

    /// <summary>
    /// The window was resized: throw the old swapchain (and its depth
    /// buffer, views and semaphores) away and rebuild at the new size.
    /// Called between frames, so a full device drain is acceptable.
    /// </summary>
    private void RecreateSwapchain(IntPtr sdlWindow)
    {
        // queueLock: loader threads submit one-shot uploads concurrently;
        // device-idle + teardown must not interleave with those.
        lock (queueLock)
        {
        Vk.DeviceWaitIdle(device);

        foreach (var view in swapchainViews.Values)
        {
            Vk.DestroyImageView(device, view, null);
        }
        swapchainViews.Clear();
        Vk.DestroyImageView(device, depthView, null);
        Vk.DestroyImage(device, depthImage, null);
        Memory.FreePooled(depthMemory, depthMemoryOffset, depthMemorySize);
        foreach (var semaphore in renderSemaphores)
        {
            Vk.DestroySemaphore(device, semaphore, null);
        }
        foreach (var semaphore in acquireSemaphores)
        {
            Vk.DestroySemaphore(device, semaphore, null);
        }
        Vk.DestroySwapchainKHR(device, swapchain, null);

        CreateSwapchain(sdlWindow);
        CreateDepthBuffer();
        CreateSwapchainSemaphores();
        FLLog.Info("Vulkan", $"Swapchain recreated: {swapchainExtent.Width}x{swapchainExtent.Height}");
        }
    }

    // --- Frame recording state machine ----------------------------------

    private IntPtr CurrentCommands => commandBuffers[frameIndex];

    private void EnsureFrameBegun()
    {
        if (frameBegun)
        {
            return;
        }

        // Wait for EVERY in-flight frame (single-frame semantics, see the
        // FramesInFlight comment), then reset only this slot's fence.
        fixed (ulong* pFences = frameFences)
        {
            Vk.WaitForFences(device, FramesInFlight, pFences, 1, ulong.MaxValue);
        }
        fixed (ulong* pFence = &frameFences[frameIndex])
        {
            Vk.ResetFences(device, 1, pFence);
        }

        uint imageIndex = 0;
        for (var attempt = 0; ; attempt++)
        {
            currentAcquire = acquireSemaphores[frameCounter % acquireSemaphores.Length];
            var acquired = Vk.AcquireNextImageKHR(device, swapchain, ulong.MaxValue,
                currentAcquire, 0, &imageIndex);
            if (acquired == VkResult.Success || acquired == (VkResult)1000001003 /*SUBOPTIMAL*/)
            {
                break;
            }
            // OUT_OF_DATE (window resized mid-frame): the semaphore was NOT
            // signalled - submitting a wait on it would deadlock the GPU.
            // Rebuild the swapchain and acquire again.
            if (acquired == (VkResult)(-1000001004) && attempt < 3)
            {
                FLLog.Info("Vulkan", "vkAcquireNextImageKHR returned OUT_OF_DATE");
                RecreateSwapchain(window);
                continue;
            }
            Vk.Check(acquired, "vkAcquireNextImageKHR");
            break;
        }
        currentImage = imageIndex;

        // Everything queued up to (frameCounter - FramesInFlight) has
        // provably retired now that this slot's fence signalled.
        var completedFrame = frameCounter - FramesInFlight;
        lock (queueLock)
        {
            ReapOneShots();
            DrainDeferredBlas(completedFrame);
            while (deferredBuffers.Count > 0 && deferredBuffers.Peek().Frame <= completedFrame)
            {
                Memory.Destroy(deferredBuffers.Dequeue().Buffer);
            }
            while (deferredImages.Count > 0 && deferredImages.Peek().Frame <= completedFrame)
            {
                var (_, image, view, memory, offset, size) = deferredImages.Dequeue();
                Vk.DestroyImageView(device, view, null);
                Vk.DestroyImage(device, image, null);
                Memory.FreePooled(memory, offset, size);
            }
            while (pendingTransients.Count > 0 && pendingTransients.Peek().Frame <= completedFrame)
            {
                var (_, buffer, usage) = pendingTransients.Dequeue();
                if (pooledTransientBytes + (long)buffer.Size > MaxPooledTransientBytes)
                {
                    Memory.Destroy(buffer);
                    continue;
                }
                pooledTransientBytes += (long)buffer.Size;
                // Rent sized the buffer to its class, so Size IS the class.
                var key = (usage, buffer.Size);
                if (!freeTransients.TryGetValue(key, out var stack))
                {
                    freeTransients[key] = stack = new();
                }
                stack.Push(buffer);
            }
        }
        foreach (var pool in descriptorPools[frameIndex])
        {
            Vk.ResetDescriptorPool(device, pool, 0);
        }
        // The pool reset just invalidated every set this cache references.
        textureSetCache[frameIndex].Clear();
        descriptorPoolIndex = 0;
        uniformRingIndex = 0;
        uniformOffset = 0;

        var cmd = CurrentCommands;
        Vk.ResetCommandBuffer(cmd, 0);
        var beginInfo = new VkCommandBufferBeginInfo
        {
            SType = VkStructureType.CommandBufferBeginInfo,
            Flags = 1
        };
        Vk.BeginCommandBuffer(cmd, &beginInfo);
        CollectPassTimings(cmd);
        Barrier(cmd, swapchainImages[currentImage], VkImageLayout.Undefined,
            (VkImageLayout)2 /*COLOR_ATTACHMENT_OPTIMAL*/, 0, VkConst.AccessMemoryWrite);
        frameBegun = true;
    }

    private VKRenderTarget2D? CurrentTarget => applied.RenderTarget as VKRenderTarget2D;
    private VKRenderTarget2D? activeTarget;

    // G-buffer MRT (graphics phase 0.1): extra colour attachments bound for
    // the opaque pass only. null => single-attachment path stays byte-
    // identical. Targets carry their own ColorLayout (shared with the blit
    // path), so no separate layout bookkeeping is needed.
    private VKRenderTarget2D[]? gbufferExtra;
    private VKRenderTarget2D[]? activeGbufferExtra;

    public VkExtent2D CurrentTargetExtent()
    {
        var target = renderingActive ? activeTarget : CurrentTarget;
        return target != null
            ? new VkExtent2D { Width = (uint)target.Width, Height = (uint)target.Height }
            : swapchainExtent;
    }

    private VkFormat CurrentColorFormat()
    {
        var target = renderingActive ? activeTarget : CurrentTarget;
        return target != null ? (VkFormat)VKFormats.ToVk(target.Texture.Format) : swapchainFormat;
    }

    private void EnsureRendering()
    {
        EnsureFrameBegun();
        if (renderingActive)
        {
            if (activeTarget == CurrentTarget)
            {
                return;
            }
            EndRenderingIfActive(); // target switched mid-frame
        }

        var target = CurrentTarget;
        ulong colorViewHandle;
        ulong depthViewHandle;
        var extent = swapchainExtent;
        if (target != null)
        {
            // The colour image may have been sampled last frame: move it
            // (back) into attachment layout before rendering into it.
            if (target.ColorLayout != (VkImageLayout)2)
            {
                Barrier(CurrentCommands, target.Texture.Image, target.ColorLayout,
                    (VkImageLayout)2, VkConst.AccessMemoryRead, VkConst.AccessMemoryWrite);
                target.ColorLayout = (VkImageLayout)2;
            }
            colorViewHandle = target.Texture.View;
            depthViewHandle = target.Depth.View;
            extent = new VkExtent2D { Width = (uint)target.Width, Height = (uint)target.Height };
        }
        else
        {
            colorViewHandle = GetSwapchainView(currentImage);
            depthViewHandle = depthView;
        }

        var consumeClear = clearPending && clearPendingTarget == target;
        var colorAttachment = new VkRenderingAttachmentInfo
        {
            SType = VkStructureType.RenderingAttachmentInfo,
            ImageView = colorViewHandle,
            ImageLayout = (VkImageLayout)2,
            LoadOp = consumeClear ? 1 : 0,
            StoreOp = 0,
            ClearValue = new VkClearColorValue
            {
                R = clearColor.R, G = clearColor.G, B = clearColor.B, A = clearColor.A
            }
        };
        var depthAttachment = new VkRenderingAttachmentInfo
        {
            SType = VkStructureType.RenderingAttachmentInfo,
            ImageView = depthViewHandle,
            ImageLayout = (VkImageLayout)3, // DEPTH_STENCIL_ATTACHMENT_OPTIMAL
            LoadOp = consumeClear ? 1 : 0,
            StoreOp = 0,
            ClearValue = new VkClearColorValue { R = 1f } // depth clear = 1.0
        };
        if (consumeClear)
        {
            clearPending = false;
        }

        // G-buffer MRT: append extra colour attachments (RT1 normal+roughness,
        // graphics phase 0.1) when bound and rendering into an offscreen
        // target. Each extra is moved SHADER_READ_ONLY -> COLOR and cleared so
        // non-PBR opaque pixels read back zero.
        var extraCount = (target != null && gbufferExtra != null) ? gbufferExtra.Length : 0;
        var colorAttachments = stackalloc VkRenderingAttachmentInfo[1 + extraCount];
        colorAttachments[0] = colorAttachment;
        for (var i = 0; i < extraCount; i++)
        {
            var ex = gbufferExtra![i];
            if (ex.ColorLayout != (VkImageLayout)2)
            {
                Barrier(CurrentCommands, ex.Texture.Image, ex.ColorLayout,
                    (VkImageLayout)2, VkConst.AccessMemoryRead, VkConst.AccessMemoryWrite);
                ex.ColorLayout = (VkImageLayout)2;
            }
            colorAttachments[i + 1] = new VkRenderingAttachmentInfo
            {
                SType = VkStructureType.RenderingAttachmentInfo,
                ImageView = ex.Texture.View,
                ImageLayout = (VkImageLayout)2,
                LoadOp = 1, // CLEAR
                StoreOp = 0, // STORE
                ClearValue = new VkClearColorValue { R = 0, G = 0, B = 0, A = 0 }
            };
        }

        var renderingInfo = new VkRenderingInfo
        {
            SType = VkStructureType.RenderingInfo,
            RenderArea = new VkRect2D { Extent = extent },
            LayerCount = 1,
            ColorAttachmentCount = (uint)(1 + extraCount),
            PColorAttachments = colorAttachments,
            PDepthAttachment = &depthAttachment
        };
        Vk.CmdBeginRendering(CurrentCommands, &renderingInfo);
        renderingActive = true;
        activeTarget = target;
        activeGbufferExtra = extraCount > 0 ? gbufferExtra : null;
    }

    private void EndRenderingIfActive()
    {
        if (!renderingActive)
        {
            return;
        }
        Vk.CmdEndRendering(CurrentCommands);
        renderingActive = false;
        if (activeTarget != null)
        {
            // The target's next life is as a sampled texture (tonemap pass,
            // UI composition): transition right away.
            Barrier(CurrentCommands, activeTarget.Texture.Image, (VkImageLayout)2,
                (VkImageLayout)5 /*SHADER_READ_ONLY*/, VkConst.AccessMemoryWrite, VkConst.AccessMemoryRead);
            activeTarget.ColorLayout = (VkImageLayout)5;
            activeTarget = null;
        }
        // G-buffer MRT: extra attachments become sampled sources (debug view /
        // future GI input) - move them back to SHADER_READ_ONLY too.
        if (activeGbufferExtra != null)
        {
            foreach (var ex in activeGbufferExtra)
            {
                Barrier(CurrentCommands, ex.Texture.Image, (VkImageLayout)2,
                    (VkImageLayout)5, VkConst.AccessMemoryWrite, VkConst.AccessMemoryRead);
                ex.ColorLayout = (VkImageLayout)5;
            }
            activeGbufferExtra = null;
        }
    }

    public void SetGBufferTargets(IRenderTarget2D[]? extras)
    {
        // Switching the attachment set must restart the dynamic-rendering
        // scope so the next draw rebuilds VkRenderingInfo with the new count.
        EndRenderingIfActive();
        if (extras == null || extras.Length == 0)
        {
            gbufferExtra = null;
            return;
        }
        var arr = new VKRenderTarget2D[extras.Length];
        for (var i = 0; i < extras.Length; i++)
        {
            arr[i] = (VKRenderTarget2D)extras[i];
        }
        gbufferExtra = arr;
    }

    public void BlitTargetToCurrent(VKRenderTarget2D source, Point offset)
    {
        EnsureFrameBegun();
        EndRenderingIfActive();
        var cmd = CurrentCommands;
        var dstTarget = CurrentTarget;
        var dstImage = dstTarget?.Texture.Image ?? swapchainImages[currentImage];
        var dstExtent = dstTarget != null
            ? new VkExtent2D { Width = (uint)dstTarget.Width, Height = (uint)dstTarget.Height }
            : swapchainExtent;

        Barrier(cmd, source.Texture.Image, source.ColorLayout, (VkImageLayout)6 /*TRANSFER_SRC*/,
            VkConst.AccessMemoryWrite, VkConst.AccessMemoryRead);
        Barrier(cmd, dstImage, (VkImageLayout)2, VkImageLayout.TransferDstOptimal,
            VkConst.AccessMemoryRead | VkConst.AccessMemoryWrite, VkConst.AccessMemoryWrite);

        var blit = new VkImageBlit
        {
            SrcSubresource = new VkImageSubresourceLayers { AspectMask = 1, LayerCount = 1 },
            SrcOffset1 = new VkOffset3D { X = source.Width, Y = source.Height, Z = 1 },
            DstSubresource = new VkImageSubresourceLayers { AspectMask = 1, LayerCount = 1 },
            DstOffset0 = new VkOffset3D { X = offset.X, Y = offset.Y, Z = 0 },
            DstOffset1 = new VkOffset3D
            {
                X = Math.Min(offset.X + source.Width, (int)dstExtent.Width),
                Y = Math.Min(offset.Y + source.Height, (int)dstExtent.Height),
                Z = 1
            }
        };
        Vk.CmdBlitImage(cmd, source.Texture.Image, (VkImageLayout)6,
            dstImage, VkImageLayout.TransferDstOptimal, 1, &blit, 0);

        Barrier(cmd, source.Texture.Image, (VkImageLayout)6, (VkImageLayout)5,
            VkConst.AccessMemoryRead, VkConst.AccessMemoryRead);
        source.ColorLayout = (VkImageLayout)5;
        Barrier(cmd, dstImage, VkImageLayout.TransferDstOptimal, (VkImageLayout)2,
            VkConst.AccessMemoryWrite, VkConst.AccessMemoryRead | VkConst.AccessMemoryWrite);
        if (dstTarget != null)
        {
            dstTarget.ColorLayout = (VkImageLayout)2;
        }
    }

    public void BlitTargetToTarget(VKRenderTarget2D source, VKRenderTarget2D destination, Point offset)
    {
        EnsureFrameBegun();
        EndRenderingIfActive();
        var cmd = CurrentCommands;
        Barrier(cmd, source.Texture.Image, source.ColorLayout, (VkImageLayout)6,
            VkConst.AccessMemoryWrite, VkConst.AccessMemoryRead);
        Barrier(cmd, destination.Texture.Image, destination.ColorLayout, VkImageLayout.TransferDstOptimal,
            VkConst.AccessMemoryRead | VkConst.AccessMemoryWrite, VkConst.AccessMemoryWrite);

        var blit = new VkImageBlit
        {
            SrcSubresource = new VkImageSubresourceLayers { AspectMask = 1, LayerCount = 1 },
            SrcOffset1 = new VkOffset3D { X = source.Width, Y = source.Height, Z = 1 },
            DstSubresource = new VkImageSubresourceLayers { AspectMask = 1, LayerCount = 1 },
            DstOffset0 = new VkOffset3D { X = offset.X, Y = offset.Y, Z = 0 },
            DstOffset1 = new VkOffset3D
            {
                X = Math.Min(offset.X + source.Width, destination.Width),
                Y = Math.Min(offset.Y + source.Height, destination.Height),
                Z = 1
            }
        };
        Vk.CmdBlitImage(cmd, source.Texture.Image, (VkImageLayout)6,
            destination.Texture.Image, VkImageLayout.TransferDstOptimal, 1, &blit, 0);

        Barrier(cmd, source.Texture.Image, (VkImageLayout)6, (VkImageLayout)5,
            VkConst.AccessMemoryRead, VkConst.AccessMemoryRead);
        source.ColorLayout = (VkImageLayout)5;
        Barrier(cmd, destination.Texture.Image, VkImageLayout.TransferDstOptimal, (VkImageLayout)5,
            VkConst.AccessMemoryWrite, VkConst.AccessMemoryRead);
        destination.ColorLayout = (VkImageLayout)5;
    }

    private readonly System.Collections.Generic.Dictionary<uint, ulong> swapchainViews = new();

    private ulong GetSwapchainView(uint index)
    {
        if (swapchainViews.TryGetValue(index, out var existing))
        {
            return existing;
        }
        var viewInfo = new VkImageViewCreateInfo
        {
            SType = VkStructureType.ImageViewCreateInfo,
            Image = swapchainImages[index],
            ViewType = 1,
            Format = swapchainFormat,
            SubresourceRange = new VkImageSubresourceRange
            {
                AspectMask = 1, LevelCount = 1, LayerCount = 1
            }
        };
        ulong view;
        Vk.Check(Vk.CreateImageView(device, &viewInfo, null, &view), "vkCreateImageView(swapchain)");
        swapchainViews[index] = view;
        return view;
    }

    private bool acquireWaited;
    private ulong currentAcquire;

    public void ReadBackBuffer(int width, int height, Bgra8[] destination)
    {
        // Split the frame: flush everything recorded so far with a copy of
        // the swapchain image appended, wait, read the staging buffer, then
        // reopen the command buffer so SwapWindow can finish the frame.
        EnsureFrameBegun();
        EnsureRendering();
        EndRenderingIfActive();

        var cmd = CurrentCommands;
        var staging = Memory.CreateHostBuffer((ulong)(width * height * 4), VKMemory.UsageTransferDst);
        Barrier(cmd, swapchainImages[currentImage], (VkImageLayout)2,
            (VkImageLayout)6 /*TRANSFER_SRC*/, VkConst.AccessTransferWrite, VkConst.AccessTransferWrite);
        var copy = new VkBufferImageCopy
        {
            ImageSubresource = new VkImageSubresourceLayers { AspectMask = 1, LayerCount = 1 },
            ImageExtent = new VkExtent2D { Width = (uint)width, Height = (uint)height },
            ImageExtentDepth = 1
        };
        Vk.CmdCopyImageToBuffer(cmd, swapchainImages[currentImage], (VkImageLayout)6, staging.Buffer, 1, &copy);
        Barrier(cmd, swapchainImages[currentImage], (VkImageLayout)6,
            (VkImageLayout)2, VkConst.AccessTransferWrite, VkConst.AccessTransferWrite);
        Vk.EndCommandBuffer(cmd);
        SubmitFrame(cmd, signalPresent: false, fence: readbackFence);
        fixed (ulong* pFence = &readbackFence)
        {
            Vk.WaitForFences(device, 1, pFence, 1, ulong.MaxValue);
            Vk.ResetFences(device, 1, pFence);
        }

        // Vulkan reads top-down; the GL convention this API exposes is
        // bottom-up. Flip rows while converting.
        var source = staging.AsSpan();
        for (var row = 0; row < height; row++)
        {
            var sourceRow = source.Slice(row * width * 4, width * 4);
            var destinationRow = System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                destination.AsSpan((height - 1 - row) * width, width));
            sourceRow.CopyTo(destinationRow);
        }
        Memory.Destroy(staging);

        // Reopen the frame for the present transition in SwapWindow.
        var beginInfo = new VkCommandBufferBeginInfo
        {
            SType = VkStructureType.CommandBufferBeginInfo,
            Flags = 1
        };
        Vk.BeginCommandBuffer(cmd, &beginInfo);
    }

    private void SubmitFrame(IntPtr cmd, bool signalPresent, ulong fence)
    {
        var waitInfo = new VkSemaphoreSubmitInfo
        {
            SType = VkStructureType.SemaphoreSubmitInfo,
            Semaphore = currentAcquire,
            StageMask = VkConst.StageAllCommands
        };
        var signalInfo = new VkSemaphoreSubmitInfo
        {
            SType = VkStructureType.SemaphoreSubmitInfo,
            Semaphore = renderSemaphores[currentImage],
            StageMask = VkConst.StageAllCommands
        };
        var cmdInfo = new VkCommandBufferSubmitInfo
        {
            SType = VkStructureType.CommandBufferSubmitInfo,
            CommandBuffer = cmd
        };
        var submit = new VkSubmitInfo2
        {
            SType = VkStructureType.SubmitInfo2,
            WaitSemaphoreInfoCount = acquireWaited ? 0u : 1u,
            PWaitSemaphoreInfos = &waitInfo,
            CommandBufferInfoCount = 1,
            PCommandBufferInfos = &cmdInfo,
            SignalSemaphoreInfoCount = signalPresent ? 1u : 0u,
            PSignalSemaphoreInfos = &signalInfo
        };
        lock (queueLock)
        {
            Vk.Check(Vk.QueueSubmit2(graphicsQueue, 1, &submit, fence), "vkQueueSubmit2");
        }
        acquireWaited = true;
    }

    private long frameCounter;

    private static readonly string? dumpFrames =
        Environment.GetEnvironmentVariable("SIRIUS_VK_DUMPFRAMES");

    // SIRIUS_VK_DUMPINTERVAL=N dumps every Nth frame (default 120) -
    // tight intervals catch flicker/temporal artifacts between frames.
    private static readonly int dumpInterval =
        int.TryParse(Environment.GetEnvironmentVariable("SIRIUS_VK_DUMPINTERVAL"), out var di) && di > 0 ? di : 120;

    // Repro tool: force a same-size swapchain rebuild every N frames to
    // reproduce mid-session recreations (fullscreen OUT_OF_DATE storms)
    // in a windowed test run.
    private static readonly int forceRecreateInterval =
        int.TryParse(Environment.GetEnvironmentVariable("SIRIUS_VK_FORCE_RECREATE"), out var fri) ? fri : 0;

    public void SwapWindow(IntPtr sdlWindow, bool vsync, bool fullscreen)
    {
        if (dumpFrames != null && frameCounter % dumpInterval == dumpInterval / 2)
        {
            var pixels = new Bgra8[swapchainExtent.Width * swapchainExtent.Height];
            ReadBackBuffer((int)swapchainExtent.Width, (int)swapchainExtent.Height, pixels);
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i].A = 0xFF;
            }
            using var file = System.IO.File.Create(
                System.IO.Path.Combine(dumpFrames, $"frame_{frameCounter:D5}.png"));
            ImageLib.PNG.Save(file, (int)swapchainExtent.Width, (int)swapchainExtent.Height,
                pixels, true);
        }
        frameCounter++;
        EnsureFrameBegun(); // empty frames still clear + present
        EnsureRendering();  // applies any pending clear
        EndRenderingIfActive();

        var cmd = CurrentCommands;
        Barrier(cmd, swapchainImages[currentImage], (VkImageLayout)2,
            VkImageLayout.PresentSrcKHR, VkConst.AccessMemoryWrite, 0);
        Vk.EndCommandBuffer(cmd);
        SubmitFrame(cmd, signalPresent: true, fence: frameFences[frameIndex]);

        fixed (ulong* pSwapchain = &swapchain)
        {
            var renderSemaphore = renderSemaphores[currentImage];
            var presentImage = currentImage;
            var presentInfo = new VkPresentInfoKHR
            {
                SType = VkStructureType.PresentInfoKHR,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = &renderSemaphore,
                SwapchainCount = 1,
                PSwapchains = pSwapchain,
                PImageIndices = &presentImage
            };
            VkResult presented;
            lock (queueLock)
            {
                presented = Vk.QueuePresentKHR(graphicsQueue, &presentInfo);
            }
            if (presented == (VkResult)(-1000001004) || presented == (VkResult)1000001003)
            {
                // The size check below will rebuild; nothing waits on the
                // unconsumed present this frame.
                FLLog.Debug("Vulkan", "present returned out-of-date/suboptimal");
            }
        }

        frameIndex = (frameIndex + 1) % FramesInFlight;
        frameBegun = false;
        clearPending = true;
        clearPendingTarget = null; // next frame's implicit clear targets the swapchain
        acquireWaited = false;

        if (descriptorStats && ++statFrames == 300)
        {
            // Info: the whole report is opt-in (SIRIUS_VK_STATS=1) and
            // Release builds drop Debug severity.
            FLLog.Info("Vulkan", FormattableString.Invariant(
                $"Descriptors over 300 frames: {statDraws} draws, {statTextureSetBuilds} texture set builds, {statUniformSetBuilds} uniform set builds, {statDispatches} compute dispatches"));
            FLLog.Info("Vulkan", FormattableString.Invariant(
                $"Transients over 300 frames: {statTransientHits} pooled, {statTransientCreates} created, {pooledTransientBytes / 1024} KiB parked"));
            statFrames = statDraws = statTextureSetBuilds = statUniformSetBuilds = 0;
            statTransientHits = statTransientCreates = 0;
            statDispatches = 0;
        }

        // Window resize: the surface size no longer matches the swapchain
        // (X11 happily stretches the old-size image instead of failing the
        // present, so poll the actual window size every frame).
        SDL3.SDL_GetWindowSizeInPixels(sdlWindow, out var windowWidth, out var windowHeight);
        if (windowWidth > 0 && windowHeight > 0 &&
            ((uint)windowWidth != swapchainExtent.Width || (uint)windowHeight != swapchainExtent.Height))
        {
            FLLog.Info("Vulkan", $"Swapchain size mismatch: window {windowWidth}x{windowHeight} vs swapchain {swapchainExtent.Width}x{swapchainExtent.Height}");
            RecreateSwapchain(sdlWindow);
        }
        else if (forceRecreateInterval > 0 && frameCounter % forceRecreateInterval == 0)
        {
            RecreateSwapchain(sdlWindow);
        }
    }

    private static void Barrier(IntPtr cmd, ulong image, VkImageLayout from, VkImageLayout to,
        ulong srcAccess, ulong dstAccess)
    {
        var barrier = new VkImageMemoryBarrier2
        {
            SType = VkStructureType.ImageMemoryBarrier2,
            SrcStageMask = VkConst.StageAllCommands,
            SrcAccessMask = srcAccess,
            DstStageMask = VkConst.StageAllCommands,
            DstAccessMask = dstAccess,
            OldLayout = from,
            NewLayout = to,
            Image = image,
            SubresourceRange = new VkImageSubresourceRange
            {
                AspectMask = VkConst.ImageAspectColor,
                LevelCount = 1,
                LayerCount = 1
            }
        };
        var dependency = new VkDependencyInfo
        {
            SType = VkStructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &barrier
        };
        Vk.CmdPipelineBarrier2(cmd, &dependency);
    }

    // --- IRenderContext state surface ----------------------------------

    public int MaxSamples => 1;
    public int MaxAnisotropy => 16;
    public int AnisotropyLevel { get; set; }
    public bool SupportsWireframe => true;

    public void Init(ref GraphicsState requested)
    {
        // Seed engine-default state back into the frontend exactly like
        // GLRenderContext.Init does (enum defaults differ: CullFaces(0)
        // is Front, but the engine assumes Back unless told otherwise).
        requested.CullFaces = CullFaces.Back;
        requested.ColorWrite = true;
        if (requested.DepthFunction == 0)
        {
            requested.DepthFunction = 0x0203; // LEQUAL
        }
        if (requested.DepthRange == default)
        {
            requested.DepthRange = new Vector2(0, 1);
        }
        applied = requested;
    }

    public void ApplyState(ref GraphicsState requested)
    {
        clearColor = requested.ClearColor;
        // Mirror the GL diff semantics: the frontend's Shader property only
        // takes effect when it CHANGES - materials activate their programs
        // through ApplyShader between Apply() calls and must survive it.
        if (requested.Shader != null)
        {
            PushCameraBlock(requested.Shader);
            if (!ReferenceEquals(applied.Shader, requested.Shader))
            {
                currentShader = requested.Shader as VKShader;
            }
        }
        applied = requested;
    }

    public void ApplyViewport(ref GraphicsState requested) => applied.Viewport = requested.Viewport;

    public void ApplyScissor(ref GraphicsState requested)
    {
        applied.ScissorEnabled = requested.ScissorEnabled;
        applied.ScissorRect = requested.ScissorRect;
    }

    public void ApplyRenderTarget(ref GraphicsState requested) => applied.RenderTarget = requested.RenderTarget;

    public void ApplyShader(IShader shader)
    {
        // Direct activation by materials. Must also update the cached state:
        // the next ApplyState diff has to notice when the frontend's Shader
        // property differs and switch back (mirrors GLRenderContext).
        PushCameraBlock(shader);
        currentShader = (VKShader)shader;
        applied.Shader = shader;
    }

    public void Set2DState(bool cull, bool depth)
    {
        // Immediate path (bypasses the requested-state diff), used around
        // 2D/blit work exactly like GLRenderContext.
        applied.CullEnabled = cull;
        applied.DepthEnabled = depth;
    }

    public void SetBlendMode(ushort mode)
    {
        // Immediate path used by materials between Apply() calls; the next
        // ApplyState diff restores the frontend value.
        applied.BlendMode = mode;
    }

    public void ClearAll()
    {
        clearColor = applied.ClearColor;
        // Only clear in-place when the ACTIVE pass targets the same
        // attachment the engine asked to clear: HdrFramePipeline.Begin runs
        // while the previous (swapchain) pass is still open, and clearing
        // there would wipe the wrong image and leave the HDR target dirty.
        if (renderingActive && activeTarget == CurrentTarget)
        {
            ClearAttachments(color: true, depth: true);
        }
        else
        {
            clearPending = true;
            clearPendingTarget = CurrentTarget;
        }
    }

    public void ClearColorOnly()
    {
        clearColor = applied.ClearColor;
        if (renderingActive && activeTarget == CurrentTarget)
        {
            ClearAttachments(color: true, depth: false);
        }
        else
        {
            EnsureRendering(); // switches to the requested target
            ClearAttachments(color: true, depth: false);
        }
    }

    public void ClearDepth()
    {
        // GL clears immediately mid-frame (the player-ship pass relies on
        // it); inside dynamic rendering that's vkCmdClearAttachments.
        if (renderingActive && activeTarget == CurrentTarget)
        {
            ClearAttachments(color: false, depth: true);
        }
        else
        {
            EnsureRendering();
            ClearAttachments(color: false, depth: true);
        }
    }

    private void ClearAttachments(bool color, bool depth)
    {
        var attachments = stackalloc VkClearAttachment[2];
        var count = 0u;
        if (color)
        {
            attachments[count++] = new VkClearAttachment
            {
                AspectMask = 1,
                ClearValue = new VkClearColorValue
                {
                    R = clearColor.R, G = clearColor.G, B = clearColor.B, A = clearColor.A
                }
            };
        }
        if (depth)
        {
            attachments[count++] = new VkClearAttachment
            {
                AspectMask = 2,
                ClearValue = new VkClearColorValue { R = 1f }
            };
        }
        var targetExtent = CurrentTargetExtent();
        var clearRect = new VkRect2D { Extent = targetExtent };
        if (applied.ScissorEnabled)
        {
            var x = Math.Clamp(applied.ScissorRect.X, 0, (int)targetExtent.Width);
            var y = Math.Clamp(applied.ScissorRect.Y, 0, (int)targetExtent.Height);
            var right = Math.Clamp(applied.ScissorRect.X + applied.ScissorRect.Width, 0, (int)targetExtent.Width);
            var bottom = Math.Clamp(applied.ScissorRect.Y + applied.ScissorRect.Height, 0, (int)targetExtent.Height);
            if (right <= x || bottom <= y)
            {
                return;
            }
            clearRect = new VkRect2D
            {
                Offset = new VkOffset2D { X = x, Y = y },
                Extent = new VkExtent2D { Width = (uint)(right - x), Height = (uint)(bottom - y) }
            };
        }
        var rect = new VkClearRect
        {
            Rect = clearRect,
            LayerCount = 1
        };
        Vk.CmdClearAttachments(CurrentCommands, count, attachments, 1, &rect);
    }

    public void MemoryBarrier() =>
        GlobalBarrier(VkConst.StageAllCommands, VkConst.AccessMemoryWrite | VkConst.AccessShaderWrite,
            VkConst.StageAllCommands, VkConst.AccessMemoryRead | VkConst.AccessShaderRead);

    private static Exception Milestone(string what) =>
        new NotImplementedException($"Vulkan backend: {what} arrives in a later phase-3 milestone");

    public IShader CreateShader(ReadOnlySpan<byte> program)
    {
        var shader = new VKShader(device, program);
        FLLog.Info("VKShader", $"created id={shader.Id} resources={shader.Resources.Count}");
        return shader;
    }

    public IElementBuffer CreateElementBuffer(int count, bool isDynamic = false) => new VKElementBuffer(this, count);

    public IVertexBuffer CreateVertexBuffer(Type type, int length, bool isStream = false) =>
        new VKVertexBuffer(this, (IVertexType)Activator.CreateInstance(type)!, length, isStream);

    public IVertexBuffer CreateVertexBuffer(IVertexType type, int length, bool isStream = false) =>
        new VKVertexBuffer(this, type, length, isStream);

    public ITexture2D CreateTexture2D(int width, int height, bool hasMipMaps, SurfaceFormat format) =>
        new VKTexture2D(this, width, height, hasMipMaps, format);

    public ITextureCube CreateTextureCube(int size, bool mipMap, SurfaceFormat format) =>
        new VKTextureCube(this, size, mipMap, format);

    public ITexture3D CreateTexture3D(int width, int height, int depth, SurfaceFormat format, bool storage = false) =>
        new VKTexture3D(this, width, height, depth, format, storage);

    public IDepthBuffer CreateDepthBuffer(int width, int height) => new VKDepthBuffer(this, width, height);

    public IRenderTarget2D CreateRenderTarget2D(ITexture2D texture, IDepthBuffer buffer) =>
        new VKRenderTarget2D(this, texture, buffer);

    public IMultisampleTarget CreateMultisampleTarget(int width, int height, int samples) => throw Milestone("msaa targets");

    public IStorageBuffer CreateStorageBuffer(int size, int stride) => new VKStorageBuffer(this, size, stride);

    private int storageFallbackLog = 40;
    private readonly (ulong Buffer, ulong Offset, ulong Range)[] storageSlots =
        new (ulong, ulong, ulong)[16]; // engine binds bones/particles at slot 9

    public void BindStorageSlot(int binding, ulong buffer, ulong offset, ulong range)
    {
        if (binding < storageSlots.Length)
        {
            storageSlots[binding] = (buffer, offset, range);
        }
    }

    // --- Buffer updates --------------------------------------------------

    private int mainThreadId = Environment.CurrentManagedThreadId;
    internal long CurrentFrame => frameCounter;

    /// <summary>
    /// GL-semantics buffer write: glBufferSubData after a draw call never
    /// affects that draw (the driver shadows the data). Vulkan executes the
    /// whole frame later, so a CPU write to memory an already-recorded draw
    /// reads WOULD corrupt it. When the destination was used by a draw in
    /// the frame being recorded, route the update through a staging copy
    /// recorded in command order instead of writing the live memory.
    /// </summary>
    internal void UploadIntoBuffer(in VKBuffer destination, int destinationOffset,
        ReadOnlySpan<byte> data, long bufferLastUsedFrame)
    {
        if (data.IsEmpty)
        {
            return;
        }
        if (!frameBegun || bufferLastUsedFrame != frameCounter ||
            Environment.CurrentManagedThreadId != mainThreadId)
        {
            // Not visible to any recorded draw yet (or loader thread):
            // direct write is safe and cheap.
            data.CopyTo(destination.AsSpan().Slice(destinationOffset));
            return;
        }

        EndRenderingIfActive();
        var staging = RentTransient((ulong)data.Length, VKMemory.UsageTransferSrc);
        data.CopyTo(staging.AsSpan());
        RecycleTransient(staging, VKMemory.UsageTransferSrc);
        var copy = new VkBufferCopy
        {
            SrcOffset = 0,
            DstOffset = (ulong)destinationOffset,
            Size = (ulong)data.Length
        };
        var cmd = CurrentCommands;
        Vk.CmdCopyBuffer(cmd, staging.Buffer, destination.Buffer, 1, &copy);
        var barrier = new VkMemoryBarrier2
        {
            SType = VkStructureType.MemoryBarrier2,
            SrcStageMask = VkConst.StageAllCommands,
            SrcAccessMask = VkConst.AccessTransferWrite,
            DstStageMask = VkConst.StageAllCommands,
            DstAccessMask = 0x8000 // MEMORY_READ
        };
        var dependency = new VkDependencyInfo
        {
            SType = VkStructureType.DependencyInfo,
            MemoryBarrierCount = 1,
            PMemoryBarriers = &barrier
        };
        Vk.CmdPipelineBarrier2(cmd, &dependency);
    }

    // --- Draw recording -------------------------------------------------

    public void RecordDraw(VKVertexBuffer buffer, PrimitiveTypes primitive, int baseVertex,
        int startIndex, int primitiveCount, bool indexed)
    {
        if (currentShader == null)
        {
            return;
        }
        EnsureRendering();
        var cmd = CurrentCommands;
        BindPipeline(cmd, buffer.Declaration, primitive);
        BindDynamicState(cmd);
        BindDescriptors(cmd);

        var offsets = stackalloc ulong[2] { 0, 0 };
        var vertexBuffers = stackalloc ulong[2] { buffer.ActiveBuffer.Buffer, zeroVertexBuffer.Buffer };
        Vk.CmdBindVertexBuffers(cmd, 0, 2, vertexBuffers, offsets);
        buffer.LastUsedFrame = frameCounter;
        var indexCount = (uint)IndexCount(primitive, primitiveCount);
        if (indexed && buffer.Elements != null)
        {
            buffer.Elements.LastUsedFrame = frameCounter;
            Vk.CmdBindIndexBuffer(cmd, buffer.Elements.Buffer.Buffer, 0, 0 /*UINT16*/);
            Vk.CmdDrawIndexed(cmd, indexCount, 1, (uint)startIndex, baseVertex, 0);
        }
        else
        {
            Vk.CmdDraw(cmd, indexCount, 1, (uint)startIndex, 0);
        }

    }


    public void RecordDrawImmediate(VKVertexBuffer buffer, PrimitiveTypes primitive, int baseVertex,
        ReadOnlySpan<ushort> elements)
    {
        if (currentShader == null)
        {
            return;
        }
        EnsureRendering();
        var cmd = CurrentCommands;
        BindPipeline(cmd, buffer.Declaration, primitive);
        BindDynamicState(cmd);
        BindDescriptors(cmd);

        var transient = Memory.CreateHostBuffer((ulong)(elements.Length * 2), VKMemory.UsageIndex);
        System.Runtime.InteropServices.MemoryMarshal.Cast<ushort, byte>(elements).CopyTo(transient.AsSpan());
        DeferDestroy(transient);

        var offsets = stackalloc ulong[2] { 0, 0 };
        var vertexBuffers = stackalloc ulong[2] { buffer.ActiveBuffer.Buffer, zeroVertexBuffer.Buffer };
        Vk.CmdBindVertexBuffers(cmd, 0, 2, vertexBuffers, offsets);
        buffer.LastUsedFrame = frameCounter;
        Vk.CmdBindIndexBuffer(cmd, transient.Buffer, 0, 0);
        Vk.CmdDrawIndexed(cmd, (uint)elements.Length, 1, 0, baseVertex, 0);
    }

    private void BindPipeline(IntPtr cmd, VertexDeclaration declaration, PrimitiveTypes primitive)
    {
        var key = new PipelineKey(
            currentShader!.Id,
            declaration.GetHashCode(),
            applied.BlendMode,
            applied.DepthEnabled,
            applied.DepthWrite,
            applied.DepthFunction,
            applied.CullEnabled,
            applied.CullFaces,
            (int)primitive,
            CurrentColorFormat(),
            CurrentTarget == null,
            applied.ColorWrite,
            currentShadingRate,
            (CurrentTarget != null && gbufferExtra != null) ? gbufferExtra.Length : 0);
        if (!pipelines.TryGetValue(key, out var pipeline))
        {
            pipeline = CreatePipeline(declaration, primitive);
            pipelines[key] = pipeline;
        }
        Vk.CmdBindPipeline(cmd, 0, pipeline);
    }

    // --- Compute (roadmap phase 5 foundation) ---
    // Compute pipelines have no graphics state: keyed by shader id alone,
    // NOT by PipelineKey (whose blend/depth/vertex fields would only add
    // false cache misses).
    private readonly Dictionary<int, ulong> computePipelines = new();
    private long statDispatches;

    private ulong GetComputePipeline(VKShader shader)
    {
        if (computePipelines.TryGetValue(shader.Id, out var pipeline))
        {
            return pipeline;
        }
        var entryName = "main\0"u8;
        fixed (byte* pEntryName = entryName)
        {
            var createInfo = new VkComputePipelineCreateInfo
            {
                SType = VkStructureType.ComputePipelineCreateInfo,
                Stage = new VkPipelineShaderStageCreateInfo
                {
                    SType = VkStructureType.PipelineShaderStageCreateInfo,
                    Stage = 0x20, // COMPUTE
                    Module = shader.VertexModule, // compute module rides the vertex slot
                    PName = pEntryName
                },
                Layout = shader.PipelineLayout
            };
            ulong created;
            Vk.Check(Vk.CreateComputePipelines(device, 0, 1, &createInfo, null, &created),
                "vkCreateComputePipelines");
            // VK_OBJECT_TYPE_PIPELINE = 19
            SetObjectName(19, created, $"compute shader {shader.Id}");
            computePipelines[shader.Id] = created;
            return created;
        }
    }

    /// <summary>
    /// Dispatches the currently applied compute shader. Must be called
    /// outside vkCmdBeginRendering: any active dynamic-rendering pass is
    /// ended first (the FSM already supports mid-frame breaks - blits and
    /// buffer uploads do the same) and the next draw reopens it.
    /// </summary>
    public void DispatchCompute(uint groupsX, uint groupsY, uint groupsZ)
    {
        if (currentShader is not { IsComputePipeline: true } shader)
        {
            FLLog.Warning("Vulkan", "DispatchCompute called without a compute shader applied");
            return;
        }
        EnsureFrameBegun();
        EndRenderingIfActive();
        var cmd = CurrentCommands;
        var pipeline = GetComputePipeline(shader);
        Vk.CmdBindPipeline(cmd, 1 /*COMPUTE*/, pipeline);
        BindDescriptors(cmd, bindPoint: 1);
        Vk.CmdDispatch(cmd, groupsX, groupsY, groupsZ);
        if (statDispatches == 0)
        {
            FLLog.Info("Vulkan", $"First compute dispatch: shader {shader.Id}, groups {groupsX}x{groupsY}x{groupsZ}, pipeline 0x{pipeline:X}");
        }
        statDispatches++;
    }

    /// <summary>Global memory barrier (sync2). Storage images stay in
    /// GENERAL so a memory barrier alone covers image and buffer hazards -
    /// no per-image layout transitions needed between passes.</summary>
    private void GlobalBarrier(ulong srcStage, ulong srcAccess, ulong dstStage, ulong dstAccess)
    {
        EnsureFrameBegun();
        EndRenderingIfActive();
        var barrier = new VkMemoryBarrier2
        {
            SType = VkStructureType.MemoryBarrier2,
            SrcStageMask = srcStage,
            SrcAccessMask = srcAccess,
            DstStageMask = dstStage,
            DstAccessMask = dstAccess
        };
        var dependency = new VkDependencyInfo
        {
            SType = VkStructureType.DependencyInfo,
            MemoryBarrierCount = 1,
            PMemoryBarriers = &barrier
        };
        Vk.CmdPipelineBarrier2(CurrentCommands, &dependency);
    }

    /// <summary>Compute writes -> fragment/vertex reads (composite passes
    /// sampling froxel volumes).</summary>
    public void BarrierComputeToGraphics() =>
        GlobalBarrier(VkConst.StageComputeShader, VkConst.AccessShaderWrite,
            VkConst.StageAllCommands, VkConst.AccessShaderRead | VkConst.AccessMemoryRead);

    /// <summary>Graphics writes -> compute reads (depth copy, scene inputs).</summary>
    public void BarrierGraphicsToCompute() =>
        GlobalBarrier(VkConst.StageAllCommands, VkConst.AccessMemoryWrite,
            VkConst.StageComputeShader, VkConst.AccessShaderRead);

    /// <summary>Between dependent dispatches (inject -> light -> integrate).</summary>
    public void BarrierComputeToCompute() =>
        GlobalBarrier(VkConst.StageComputeShader, VkConst.AccessShaderWrite,
            VkConst.StageComputeShader, VkConst.AccessShaderRead | VkConst.AccessShaderWrite);

    /// <summary>
    /// Copies a render target's depth attachment into a sampled depth
    /// texture (phase 5: volumetric composite needs scene depth, and the
    /// engine's live depth attachments are not sampleable by design).
    /// D32->D32 vkCmdCopyImage, ~0.05ms at 1440p; no render-FSM changes.
    /// </summary>
    public void CopyDepthToTexture(IRenderTarget2D source, ITexture2D destination)
    {
        if (source is not VKRenderTarget2D rt || destination is not VKTexture2D tex ||
            tex.Format != SurfaceFormat.Depth)
        {
            return;
        }
        EnsureFrameBegun();
        EndRenderingIfActive();
        var cmd = CurrentCommands;
        // Depth attachments live in DEPTH_STENCIL_ATTACHMENT_OPTIMAL (3);
        // the destination rests in SHADER_READ_ONLY (5).
        ImageBarrier(cmd, rt.Depth.Image, (VkImageLayout)3, (VkImageLayout)6 /*TRANSFER_SRC*/, 0, 0, aspect: 2);
        ImageBarrier(cmd, tex.Image, (VkImageLayout)5, VkImageLayout.TransferDstOptimal, 0, 0, aspect: 2);
        var region = new VkImageCopy
        {
            SrcSubresource = new VkImageSubresourceLayers { AspectMask = 2, LayerCount = 1 },
            DstSubresource = new VkImageSubresourceLayers { AspectMask = 2, LayerCount = 1 },
            ExtentWidth = (uint)Math.Min(rt.Width, tex.Width),
            ExtentHeight = (uint)Math.Min(rt.Height, tex.Height),
            ExtentDepth = 1
        };
        Vk.CmdCopyImage(cmd, rt.Depth.Image, (VkImageLayout)6,
            tex.Image, VkImageLayout.TransferDstOptimal, 1, &region);
        ImageBarrier(cmd, rt.Depth.Image, (VkImageLayout)6, (VkImageLayout)3, 0, 0, aspect: 2);
        ImageBarrier(cmd, tex.Image, VkImageLayout.TransferDstOptimal, (VkImageLayout)5, 0, 0, aspect: 2);
    }

    private int currentShadingRate = 1;

    /// <summary>Per-draw shading rate (1 = 1x1 full rate, 2 = 2x2): part
    /// of the pipeline cache key. No-op without VRS support.</summary>
    public void SetShadingRate(int size)
    {
        currentShadingRate = size;
    }

    private void BindDynamicState(IntPtr cmd)
    {
        // The engine reasons in GL clip/window space. The classic port
        // rule: flip (negative viewport height) ONLY on the final
        // swapchain pass - offscreen targets stay unflipped so their
        // texture memory keeps the GL orientation the engine samples with.
        // This mirrors GLRenderContext, which also only flips when
        // RenderTarget == null.
        var flip = activeTarget == null;
        var targetExtent = CurrentTargetExtent();
        var viewportRect = applied.Viewport;
        // DepthRange maps to viewport depth bounds (the starsphere pass
        // pins itself to the far plane with DepthRange(1,1) exactly like
        // glDepthRange).
        var viewport = flip
            ? new VkViewport
            {
                X = viewportRect.X,
                Y = targetExtent.Height - viewportRect.Y,
                Width = viewportRect.Width,
                Height = -viewportRect.Height,
                MinDepth = applied.DepthRange.X,
                MaxDepth = applied.DepthRange.Y
            }
            : new VkViewport
            {
                X = viewportRect.X,
                Y = viewportRect.Y,
                Width = viewportRect.Width,
                Height = viewportRect.Height,
                MinDepth = applied.DepthRange.X,
                MaxDepth = applied.DepthRange.Y
            };
        Vk.CmdSetViewport(cmd, 0, 1, &viewport);

        // Scissor rectangles arrive in the engine's top-left window space,
        // which is ALSO Vulkan's native framebuffer convention - unlike GL,
        // no Y conversion is ever needed (GL flipped to bottom-left).
        //
        // The no-scissor fallback clips to the VIEWPORT rect instead, and
        // that one is in the engine's GL bottom-left space: on the flipped
        // swapchain pass its Y must be converted, or a non-fullscreen
        // PushViewport gets an empty viewport/scissor intersection and
        // silently draws nothing (fullscreen rects match in both
        // conventions, which is why this never showed before phase 5).
        var scissorSource = applied.ScissorEnabled ? applied.ScissorRect : viewportRect;
        var scissorY = applied.ScissorEnabled || !flip
            ? scissorSource.Y
            : (int)targetExtent.Height - scissorSource.Y - scissorSource.Height;
        var scissor = new VkRect2D
        {
            Offset = new VkOffset2D
            {
                X = Math.Max(0, scissorSource.X),
                Y = Math.Max(0, scissorY)
            },
            Extent = new VkExtent2D
            {
                Width = (uint)Math.Max(0, scissorSource.Width),
                Height = (uint)Math.Max(0, scissorSource.Height)
            }
        };
        Vk.CmdSetScissor(cmd, 0, 1, &scissor);
    }

    private ulong CreatePipeline(VertexDeclaration declaration, PrimitiveTypes primitive)
    {
        var entryName = "main\0"u8;
        fixed (byte* pEntryName = entryName)
        {
            var stages = stackalloc VkPipelineShaderStageCreateInfo[2]
            {
                new()
                {
                    SType = VkStructureType.PipelineShaderStageCreateInfo,
                    // Mesh bundles carry the mesh module in the vertex slot.
                    Stage = currentShader!.IsMeshPipeline ? 0x80u : 1u,
                    Module = currentShader!.VertexModule,
                    PName = pEntryName
                },
                new()
                {
                    SType = VkStructureType.PipelineShaderStageCreateInfo,
                    Stage = 16,
                    Module = currentShader.FragmentModule,
                    PName = pEntryName
                }
            };

            var bindings = stackalloc VkVertexInputBindingDescription[2]
            {
                new() { Binding = 0, Stride = (uint)declaration.Stride, InputRate = 0 },
                // Stride-0 zero buffer: shader inputs the declaration does
                // not provide read constant zeroes (GL's default-attribute
                // behaviour, which Vulkan does not have).
                new() { Binding = 1, Stride = 0, InputRate = 0 }
            };
            var shaderInputs = 0u;
            foreach (var location in currentShader!.VertexInputLocations)
            {
                shaderInputs |= 1u << (int)location;
            }
            var attributes = stackalloc VkVertexInputAttributeDescription[16];
            var attributeCount = 0;
            var covered = 0u;
            foreach (var element in declaration.Elements)
            {
                // Attributes the shader never reads stay unbound: vertex-
                // pulling shaders (particles) draw thousands of vertices
                // against a dummy buffer, and fetching past its end is UB.
                if ((shaderInputs & (1u << element.Slot)) == 0)
                {
                    continue;
                }
                attributes[attributeCount++] = new VkVertexInputAttributeDescription
                {
                    Location = (uint)element.Slot,
                    Binding = 0,
                    Format = VertexFormat(element),
                    Offset = (uint)element.Offset
                };
                covered |= 1u << element.Slot;
            }
            foreach (var location in currentShader!.VertexInputLocations)
            {
                if ((covered & (1u << (int)location)) == 0)
                {
                    attributes[attributeCount++] = new VkVertexInputAttributeDescription
                    {
                        Location = location,
                        Binding = 1,
                        Format = 109, // R32G32B32A32_SFLOAT zeroes
                        Offset = 0
                    };
                }
            }
            var vertexInput = new VkPipelineVertexInputStateCreateInfo
            {
                SType = VkStructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 2,
                PVertexBindingDescriptions = bindings,
                VertexAttributeDescriptionCount = (uint)attributeCount,
                PVertexAttributeDescriptions = attributes
            };

            var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo
            {
                SType = VkStructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = primitive switch
                {
                    PrimitiveTypes.TriangleList => 3,
                    PrimitiveTypes.TriangleStrip => 4,
                    PrimitiveTypes.LineList => 1,
                    PrimitiveTypes.LineStrip => 2,
                    _ => 0 // points
                }
            };

            var viewportState = new VkPipelineViewportStateCreateInfo
            {
                SType = VkStructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                ScissorCount = 1
            };

            var rasterization = new VkPipelineRasterizationStateCreateInfo
            {
                SType = VkStructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = applied.Wireframe ? 1 : 0,
                CullMode = applied.CullEnabled
                    ? (applied.CullFaces == CullFaces.Front ? 1u : 2u)
                    : 0u,
                // The swapchain pass flips Y via the negative-height
                // viewport, which also flips winding; offscreen passes don't
                // flip, so the SAME GL-convention geometry comes out mirrored
                // there. Match GL's effective orientation per pass kind.
                FrontFace = CurrentTarget == null ? 0 : 1, // swapchain: CCW, offscreen: CW
                LineWidth = 1
            };

            var multisample = new VkPipelineMultisampleStateCreateInfo
            {
                SType = VkStructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = 1
            };

            var depthStencil = new VkPipelineDepthStencilStateCreateInfo
            {
                SType = VkStructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = applied.DepthEnabled ? 1u : 0u,
                DepthWriteEnable = applied.DepthWrite ? 1u : 0u,
                DepthCompareOp = TranslateCompare(applied.DepthFunction),
                MaxDepthBounds = 1
            };

            var blendAttachment = TranslateBlend(applied.BlendMode);
            if (!applied.ColorWrite)
            {
                // Depth-only passes (glColorMask(false...) on GL).
                blendAttachment.ColorWriteMask = 0;
            }
            // G-buffer MRT (graphics phase 0.1): extra attachments write raw
            // values (no blending, full mask). Their count must match the
            // bound attachment count or vkCmdBeginRendering mismatches.
            var extraColor = (CurrentTarget != null && gbufferExtra != null) ? gbufferExtra.Length : 0;
            var blendAttachments = stackalloc VkPipelineColorBlendAttachmentState[1 + extraColor];
            blendAttachments[0] = blendAttachment;
            for (var i = 0; i < extraColor; i++)
            {
                // Mirror the primary attachment's blend. Without the
                // independentBlend device feature all attachments must be
                // identical (VUID-00605). The G-buffer is only meaningfully
                // written by opaque PBR materials (primary == opaque -> RT1
                // gets a full-mask opaque write); non-PBR draws in the pass
                // (beams) never output SV_Target1, so RT1 is untouched there.
                blendAttachments[i + 1] = blendAttachment;
            }
            var colorBlend = new VkPipelineColorBlendStateCreateInfo
            {
                SType = VkStructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = (uint)(1 + extraColor),
                PAttachments = blendAttachments
            };

            var dynamicStates = stackalloc int[2] { 0, 1 }; // VIEWPORT, SCISSOR
            var dynamicState = new VkPipelineDynamicStateCreateInfo
            {
                SType = VkStructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates = dynamicStates
            };

            var colorFormats = stackalloc VkFormat[1 + extraColor];
            colorFormats[0] = CurrentColorFormat();
            for (var i = 0; i < extraColor; i++)
            {
                colorFormats[i + 1] = (VkFormat)VKFormats.ToVk(gbufferExtra![i].Texture.Format);
            }
            var rendering = new VkPipelineRenderingCreateInfo
            {
                SType = VkStructureType.PipelineRenderingCreateInfo,
                ColorAttachmentCount = (uint)(1 + extraColor),
                PColorAttachmentFormats = colorFormats,
                DepthAttachmentFormat = depthFormat
            };
            // VRS pipeline-tier (roadmap 7.6): the rate is STATIC pipeline
            // state keyed into the cache - a dynamic-state approach left
            // one-shot passes (cubemap bakes) with unset state -> UB frames.
            // Rate-1 pipelines skip the struct entirely so the default path
            // stays byte-identical to the pre-VRS create chain.
            bool wantVrs = vrsSupported && currentShadingRate != 1;
            var shadingRate = new VkPipelineFragmentShadingRateStateCreateInfoKHR
            {
                SType = (VkStructureType)1000226001, // PIPELINE_FRAGMENT_SHADING_RATE_STATE_CREATE_INFO_KHR
                FragmentSizeWidth = (uint)currentShadingRate,
                FragmentSizeHeight = (uint)currentShadingRate,
                CombinerOp0 = 0, // KEEP: pipeline rate wins
                CombinerOp1 = 0
            };
            if (wantVrs)
            {
                shadingRate.PNext = &rendering;
            }

            var pipelineInfo = new VkGraphicsPipelineCreateInfo
            {
                SType = VkStructureType.GraphicsPipelineCreateInfo,
                PNext = wantVrs ? &shadingRate : (void*)&rendering,
                StageCount = 2,
                PStages = stages,
                // Mesh pipelines have no vertex input or input assembly.
                PVertexInputState = currentShader.IsMeshPipeline ? null : &vertexInput,
                PInputAssemblyState = currentShader.IsMeshPipeline ? null : &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterization,
                PMultisampleState = &multisample,
                PDepthStencilState = &depthStencil,
                PColorBlendState = &colorBlend,
                PDynamicState = &dynamicState,
                Layout = currentShader.PipelineLayout
            };

            ulong pipeline;
            Vk.Check(Vk.CreateGraphicsPipelines(device, 0, 1, &pipelineInfo, null, &pipeline),
                "vkCreateGraphicsPipelines");
            if (currentShadingRate != 1)
                FLLog.Info("Vulkan", $"Pipeline created with shading rate {currentShadingRate}x{currentShadingRate}");
            return pipeline;
        }
    }

    private static int VertexFormat(in VertexElement element) => (element.Type, element.Elements, element.Normalized) switch
    {
        (VertexElementType.Float, 1, _) => 100,           // R32_SFLOAT
        (VertexElementType.Float, 2, _) => 103,           // R32G32_SFLOAT
        (VertexElementType.Float, 3, _) => 106,           // R32G32B32_SFLOAT
        (VertexElementType.Float, 4, _) => 109,           // R32G32B32A32_SFLOAT
        (VertexElementType.UnsignedByte, 4, true) => 37,  // R8G8B8A8_UNORM
        (VertexElementType.UnsignedByte, 4, false) => 41, // R8G8B8A8_UINT
        (VertexElementType.UnsignedShort, 2, true) => 77, // R16G16_UNORM
        (VertexElementType.UnsignedShort, 2, false) => 81,// R16G16_UINT
        (VertexElementType.Short, 2, true) => 78,         // R16G16_SNORM
        (VertexElementType.Short, 4, true) => 92,         // R16G16B16A16_SNORM
        _ => 109
    };

    private static int TranslateCompare(int glFunc) => glFunc switch
    {
        0x0200 => 0, // NEVER
        0x0201 => 1, // LESS
        0x0202 => 2, // EQUAL
        0x0203 => 3, // LEQUAL
        0x0204 => 4, // GREATER
        0x0205 => 5, // NOTEQUAL
        0x0206 => 6, // GEQUAL
        0x0207 => 7, // ALWAYS
        _ => 3
    };

    private static VkPipelineColorBlendAttachmentState TranslateBlend(ushort mode)
    {
        var attachment = new VkPipelineColorBlendAttachmentState { ColorWriteMask = 0xF };
        if (mode == BlendMode.Opaque)
        {
            return attachment;
        }
        var (src, dst) = BlendMode.Deconstruct(mode);
        attachment.BlendEnable = 1;
        attachment.SrcColorBlendFactor = TranslateFactor(src);
        attachment.DstColorBlendFactor = TranslateFactor(dst);
        attachment.SrcAlphaBlendFactor = attachment.SrcColorBlendFactor;
        attachment.DstAlphaBlendFactor = attachment.DstColorBlendFactor;
        return attachment;
    }

    private static int TranslateFactor(BlendOp op) => op switch
    {
        BlendOp.Zero => 0,
        BlendOp.One => 1,
        BlendOp.SrcColor => 2,
        BlendOp.InvSrcColor => 3,
        BlendOp.DstColor => 4,
        BlendOp.InvDstColor => 5,
        BlendOp.SrcAlpha => 6,
        BlendOp.InvSrcAlpha => 7,
        BlendOp.DstAlpha => 8,
        BlendOp.InvDstAlpha => 9,
        BlendOp.SrcAlphaSat => 14,
        _ => 1
    };

    internal static int IndexCount(PrimitiveTypes primitive, int primitiveCount) => primitive switch
    {
        PrimitiveTypes.TriangleList => primitiveCount * 3,
        PrimitiveTypes.TriangleStrip => primitiveCount + 2,
        PrimitiveTypes.LineList => primitiveCount * 2,
        PrimitiveTypes.LineStrip => primitiveCount + 1,
        _ => primitiveCount
    };

    private void BindDescriptors(IntPtr cmd, int bindPoint = 0)
    {
        var shader = currentShader!;
        statDraws++;

        // --- Uniforms: persistent dynamic sets, fresh offsets per draw. ---
        var blocks = shader.UniformBlocks;
        // All of this draw's blocks must land in ONE ring (the cached set
        // references a single buffer), and every dynamic offset must satisfy
        // offset + UniformBlockRange <= ring size: reserve the worst case
        // up front instead of switching rings mid-draw.
        var worstCase = blocks.Count * (UniformBlockRange + (int)UniformAlign);
        if (uniformOffset + worstCase > UniformRingSize)
        {
            if (++uniformRingIndex == uniformRings[frameIndex].Count)
            {
                uniformRings[frameIndex].Add(
                    Memory.CreateHostBuffer(UniformRingSize, VKMemory.UsageUniform));
            }
            uniformOffset = 0;
        }
        var ring = uniformRings[frameIndex][uniformRingIndex];
        var uniformSets = GetUniformSets(shader, ring);

        var dynamicOffsets = stackalloc uint[16];
        for (var i = 0; i < blocks.Count; i++)
        {
            var data = shader.BlockData((int)blocks[i].Binding);
            if (data.Length > UniformBlockRange)
            {
                throw new InvalidOperationException(
                    $"Uniform block {blocks[i].Binding} of shader {shader.Id} is {data.Length} bytes; dynamic descriptors are capped at {UniformBlockRange}");
            }
            uniformOffset = (int)((uniformOffset + (int)UniformAlign - 1) & ~((int)UniformAlign - 1));
            if (data.IsEmpty)
            {
                ring.AsSpan().Slice(uniformOffset, 16).Clear();
            }
            else
            {
                data.CopyTo(ring.AsSpan().Slice(uniformOffset));
            }
            dynamicOffsets[i] = (uint)uniformOffset;
            uniformOffset += Math.Max(data.Length, 16);
        }

        // --- Textures/samplers/storage: content-addressed per-frame sets. ---
        var textureSets = GetTextureSets(shader);

        var sets = stackalloc ulong[VKShader.SetCount];
        sets[VKShader.SetVertexTextures] = textureSets.VertexSet;
        sets[VKShader.SetVertexUniforms] = uniformSets.VertexSet;
        sets[VKShader.SetFragmentTextures] = textureSets.FragmentSet;
        sets[VKShader.SetFragmentUniforms] = uniformSets.FragmentSet;
        Vk.CmdBindDescriptorSets(cmd, bindPoint, shader.PipelineLayout, 0, VKShader.SetCount, sets,
            (uint)blocks.Count, dynamicOffsets);
    }

    private (ulong VertexSet, ulong FragmentSet) GetUniformSets(VKShader shader, in VKBuffer ring)
    {
        var key = (shader.Id, frameIndex, uniformRingIndex);
        if (uniformSetCache.TryGetValue(key, out var sets))
        {
            return sets;
        }
        statUniformSetBuilds++;
        var layouts = stackalloc ulong[2]
        {
            shader.SetLayouts[VKShader.SetVertexUniforms],
            shader.SetLayouts[VKShader.SetFragmentUniforms]
        };
        var allocated = stackalloc ulong[2];
        var allocateInfo = new VkDescriptorSetAllocateInfo
        {
            SType = VkStructureType.DescriptorSetAllocateInfo,
            DescriptorPool = persistentPools[^1],
            DescriptorSetCount = 2,
            PSetLayouts = layouts
        };
        var result = Vk.AllocateDescriptorSets(device, &allocateInfo, allocated);
        if (result == (VkResult)(-1000069000)) // OUT_OF_POOL_MEMORY
        {
            persistentPools.Add(CreatePersistentDescriptorPool());
            allocateInfo.DescriptorPool = persistentPools[^1];
            result = Vk.AllocateDescriptorSets(device, &allocateInfo, allocated);
        }
        Vk.Check(result, "vkAllocateDescriptorSets(uniform)");

        // Every block points at the ring base; the per-draw location is the
        // dynamic offset supplied at bind time.
        var writes = stackalloc VkWriteDescriptorSet[16];
        var bufferInfos = stackalloc VkDescriptorBufferInfo[16];
        var count = 0;
        foreach (var block in shader.UniformBlocks)
        {
            bufferInfos[count] = new VkDescriptorBufferInfo
            {
                Buffer = ring.Buffer,
                Offset = 0,
                Range = UniformBlockRange
            };
            writes[count] = new VkWriteDescriptorSet
            {
                SType = VkStructureType.WriteDescriptorSet,
                DstSet = block.Set == VKShader.SetVertexUniforms ? allocated[0] : allocated[1],
                DstBinding = block.Binding,
                DescriptorCount = 1,
                DescriptorType = 8, // UNIFORM_BUFFER_DYNAMIC
                PBufferInfo = &bufferInfos[count]
            };
            count++;
        }
        if (count > 0)
        {
            Vk.UpdateDescriptorSets(device, (uint)count, writes, 0, null);
        }
        sets = (allocated[0], allocated[1]);
        uniformSetCache[key] = sets;
        return sets;
    }

    private (ulong VertexSet, ulong FragmentSet) GetTextureSets(VKShader shader)
    {
        if (shader.Resources.Count > 60)
        {
            throw new InvalidOperationException(
                $"Shader {shader.Id} exceeds descriptor scratch capacity ({shader.Resources.Count})");
        }
        // Key on the handles each binding resolves to RIGHT NOW, after
        // fallback substitution - so disposal, slot changes and mip-driven
        // view rebuilds all land on a different set, while repeated
        // materials rebind the one already written this frame.
        var key = stackalloc ulong[64];
        key[0] = (ulong)(uint)shader.Id;
        var keyLength = 1;
        foreach (var resource in shader.Resources)
        {
            if (resource.IsVertexInput || resource.Kind == SpirvResourceKind.UniformBuffer)
            {
                continue;
            }
            switch (resource.Kind)
            {
                case SpirvResourceKind.SampledImage:
                {
                    var texture = textureSlots[(int)(resource.Binding / 2)];
                    if (texture == null || texture.IsDisposed ||
                        (resource.IsCube && texture is not VKTextureCube) ||
                        (resource.Is3D && texture is not VKTexture3D))
                    {
                        // Stale slots happen on scene unload: the deferred
                        // destroy retires the view while the engine still
                        // points the slot at the dead texture (GL survives
                        // because handles get recycled).
                        texture = resource.IsCube ? fallbackCube
                            : resource.Is3D ? fallbackTexture3D
                            : fallbackTexture;
                    }
                    key[keyLength++] = texture.View;
                    break;
                }
                case SpirvResourceKind.StorageImage:
                {
                    var image = storageImageSlots[(int)(resource.Binding / 2)];
                    if (image == null || image.IsDisposed)
                    {
                        image = fallbackStorage3D;
                    }
                    key[keyLength++] = image.View;
                    break;
                }
                case SpirvResourceKind.Sampler:
                    key[keyLength++] = GetSampler(samplerSlots[(int)(resource.Binding / 2)]);
                    break;
                case SpirvResourceKind.StorageBuffer:
                {
                    var slot = resource.Binding < storageSlots.Length
                        ? storageSlots[resource.Binding]
                        : default;
                    if (slot.Buffer == 0)
                    {
                        // Zeroed stand-in keeps the descriptor set complete.
                        slot = (fallbackStorage.Buffer, 0, 256);
                    }
                    key[keyLength++] = slot.Buffer;
                    key[keyLength++] = slot.Offset;
                    key[keyLength++] = slot.Range;
                    break;
                }
                case SpirvResourceKind.AccelerationStructure:
                    key[keyLength++] = boundTlas;
                    break;
            }
        }

        var lookup = textureSetCache[frameIndex]
            .GetAlternateLookup<ReadOnlySpan<ulong>>();
        var keySpan = new ReadOnlySpan<ulong>(key, keyLength);
        if (lookup.TryGetValue(keySpan, out var sets))
        {
            return sets;
        }
        statTextureSetBuilds++;
        sets = AllocateAndWriteTextureSets(shader);
        lookup.TryAdd(keySpan, sets);
        return sets;
    }

    private (ulong VertexSet, ulong FragmentSet) AllocateAndWriteTextureSets(VKShader shader)
    {
        var layouts = stackalloc ulong[2]
        {
            shader.SetLayouts[VKShader.SetVertexTextures],
            shader.SetLayouts[VKShader.SetFragmentTextures]
        };
        var allocated = stackalloc ulong[2];
        var framePools = descriptorPools[frameIndex];
        var allocateInfo = new VkDescriptorSetAllocateInfo
        {
            SType = VkStructureType.DescriptorSetAllocateInfo,
            DescriptorPool = framePools[descriptorPoolIndex],
            DescriptorSetCount = 2,
            PSetLayouts = layouts
        };
        var result = Vk.AllocateDescriptorSets(device, &allocateInfo, allocated);
        if (result == (VkResult)(-1000069000)) // OUT_OF_POOL_MEMORY
        {
            if (++descriptorPoolIndex == framePools.Count)
            {
                framePools.Add(CreateDescriptorPool());
            }
            allocateInfo.DescriptorPool = framePools[descriptorPoolIndex];
            result = Vk.AllocateDescriptorSets(device, &allocateInfo, allocated);
        }
        Vk.Check(result, "vkAllocateDescriptorSets(texture)");

        var writes = stackalloc VkWriteDescriptorSet[60];
        var bufferInfos = stackalloc VkDescriptorBufferInfo[20];
        var imageInfos = stackalloc VkDescriptorImageInfo[48];
        var asInfos = stackalloc VkWriteDescriptorSetAccelerationStructureKHR[4];
        var asHandles = stackalloc ulong[4];
        var writeCount = 0;
        var bufferCount = 0;
        var imageCount = 0;
        var asCount = 0;
        foreach (var resource in shader.Resources)
        {
            if (resource.IsVertexInput || resource.Kind == SpirvResourceKind.UniformBuffer)
            {
                continue;
            }
            var write = new VkWriteDescriptorSet
            {
                SType = VkStructureType.WriteDescriptorSet,
                DstSet = resource.Set == VKShader.SetVertexTextures ? allocated[0] : allocated[1],
                DstBinding = resource.Binding,
                DescriptorCount = 1,
                DescriptorType = VKShader.DescriptorType(resource.Kind)
            };
            switch (resource.Kind)
            {
                case SpirvResourceKind.SampledImage:
                {
                    var texture = textureSlots[(int)(resource.Binding / 2)];
                    if (texture == null || texture.IsDisposed ||
                        (resource.IsCube && texture is not VKTextureCube) ||
                        (resource.Is3D && texture is not VKTexture3D))
                    {
                        texture = resource.IsCube ? fallbackCube
                            : resource.Is3D ? fallbackTexture3D
                            : fallbackTexture;
                    }
                    imageInfos[imageCount] = new VkDescriptorImageInfo
                    {
                        ImageView = texture.View,
                        // Storage-capable textures live in GENERAL.
                        ImageLayout = texture.DescriptorLayout
                    };
                    write.PImageInfo = &imageInfos[imageCount++];
                    break;
                }
                case SpirvResourceKind.StorageImage:
                {
                    var image = storageImageSlots[(int)(resource.Binding / 2)];
                    if (image == null || image.IsDisposed)
                    {
                        image = fallbackStorage3D;
                    }
                    imageInfos[imageCount] = new VkDescriptorImageInfo
                    {
                        ImageView = image.View,
                        ImageLayout = (VkImageLayout)1 // GENERAL
                    };
                    write.PImageInfo = &imageInfos[imageCount++];
                    break;
                }
                case SpirvResourceKind.Sampler:
                {
                    imageInfos[imageCount] = new VkDescriptorImageInfo
                    {
                        Sampler = GetSampler(samplerSlots[(int)(resource.Binding / 2)])
                    };
                    write.PImageInfo = &imageInfos[imageCount++];
                    break;
                }
                case SpirvResourceKind.StorageBuffer:
                {
                    var slot = resource.Binding < storageSlots.Length
                        ? storageSlots[resource.Binding]
                        : default;
                    if (slot.Buffer == 0)
                    {
                        slot = (fallbackStorage.Buffer, 0, 256);
                        if (storageFallbackLog-- > 0)
                        {
                            FLLog.Info("Vulkan",
                                $"storage fallback: shader {currentShader?.Id} binding {resource.Binding} ({resource.Name})");
                        }
                    }
                    bufferInfos[bufferCount] = new VkDescriptorBufferInfo
                    {
                        Buffer = slot.Buffer,
                        Offset = slot.Offset,
                        Range = slot.Range
                    };
                    write.PBufferInfo = &bufferInfos[bufferCount++];
                    break;
                }
                case SpirvResourceKind.AccelerationStructure:
                {
                    // The TLAS rides VkWriteDescriptorSet.PNext; the empty
                    // TLAS substitutes until the first scene build.
                    asHandles[asCount] = boundTlas;
                    asInfos[asCount] = new VkWriteDescriptorSetAccelerationStructureKHR
                    {
                        SType = VkStructureType.WriteDescriptorSetAccelerationStructureKHR,
                        AccelerationStructureCount = 1,
                        PAccelerationStructures = &asHandles[asCount]
                    };
                    write.PNext = &asInfos[asCount++];
                    break;
                }
                default:
                    continue;
            }
            writes[writeCount++] = write;
        }
        if (writeCount > 0)
        {
            Vk.UpdateDescriptorSets(device, (uint)writeCount, writes, 0, null);
        }
        return (allocated[0], allocated[1]);
    }

    private ulong GetSampler(SamplerState state)
    {
        if (samplers.TryGetValue(state, out var existing))
        {
            return existing;
        }
        var linear = state.Filtering != TextureFiltering.Nearest;
        var usesMipmaps = state.Filtering is TextureFiltering.Bilinear
            or TextureFiltering.Trilinear
            or TextureFiltering.Anisotropic;
        var linearMipmaps = state.Filtering is TextureFiltering.Trilinear
            or TextureFiltering.Anisotropic;
        var addressU = state.WrapS == WrapMode.Repeat ? 0 : 2;
        var addressV = state.WrapT == WrapMode.Repeat ? 0 : 2;
        var createInfo = new VkSamplerCreateInfo
        {
            SType = VkStructureType.SamplerCreateInfo,
            MagFilter = linear ? 1 : 0,
            MinFilter = linear ? 1 : 0,
            MipmapMode = linearMipmaps ? 1 : 0,
            AddressModeU = addressU,
            AddressModeV = addressV,
            AddressModeW = addressV,
            MaxLod = usesMipmaps ? 1000f : 0f
        };
        ulong sampler;
        Vk.Check(Vk.CreateSampler(device, &createInfo, null, &sampler), "vkCreateSampler");
        samplers[state] = sampler;
        return sampler;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct CameraMatrices
    {
        public Matrix4x4 View;
        public Matrix4x4 Projection;
        public Matrix4x4 ViewProjection;
        public Vector3 CameraPosition;
        private float _padding;
    }

    private CameraMatrices cameraMatrices;
    private ulong cameraTag;
    private ulong setCameraTag;

    // GL projections emit z in [-1,1]; Vulkan clips depth to [0,1].
    // Post-multiplying by this matrix remaps z' = 0.5z + 0.5w, so the
    // engine's GL-convention matrices work unchanged (row-vector order).
    private static readonly Matrix4x4 DepthZeroToOne = new(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 0.5f, 0,
        0, 0, 0.5f, 1);

    public void SetCamera(ICamera camera)
    {
        setCameraTag++;
        setCameraTag &= 0x3FFFFFFFFFFFFFFF;
        cameraTag = (setCameraTag << 1) | 0x1;
        cameraMatrices.View = camera.View;
        cameraMatrices.Projection = camera.Projection * DepthZeroToOne;
        cameraMatrices.ViewProjection = camera.ViewProjection * DepthZeroToOne;
        cameraMatrices.CameraPosition = camera.Position;
    }

    public void SetIdentityCamera()
    {
        if (cameraTag == ulong.MaxValue)
        {
            return;
        }
        cameraTag = ulong.MaxValue;
        cameraMatrices.View = Matrix4x4.Identity;
        // The GL identity camera leaves z in [-1,1]; Vulkan clips depth to
        // [0,1], so even z=-1e-9 (common in UI meshes) would be discarded.
        // Remap exactly like SetCamera does.
        cameraMatrices.Projection = DepthZeroToOne;
        cameraMatrices.ViewProjection = DepthZeroToOne;
        cameraMatrices.CameraPosition = Vector3.Zero;
    }

    private void PushCameraBlock(IShader shader)
    {
        // Block 1 is the engine-wide camera block (see GLRenderContext):
        // pushed on shader activation, tag-cached to skip rewrites.
        if (shader.HasUniformBlock(1) && shader.UniformBlockTag(1) != cameraTag)
        {
            shader.UniformBlockTag(1) = cameraTag;
            shader.SetUniformBlock(1, ref cameraMatrices);
        }
    }

    public bool HasFeature(GraphicsFeature feature) => feature switch
    {
        GraphicsFeature.RayQuery => rayQuerySupported,
        GraphicsFeature.MeshShaders => meshShadersSupported,
        GraphicsFeature.VariableRateShading => vrsSupported,
        GraphicsFeature.SceneShadows => true,
        GraphicsFeature.Compute => computeSupported,
        _ => false
    };

    public string GetRenderer() => $"Vulkan 1.3 ({deviceName})";

    public void MakeCurrent(IntPtr sdlWindow)
    {
    }

    public Point GetDrawableSize(IntPtr sdlWindow)
    {
        SDL3.SDL_GetWindowSizeInPixels(sdlWindow, out var width, out var height);
        return new Point(width, height);
    }

    public void QueryFences()
    {
    }

    public void SetTextureSlot(int slot, Texture? texture)
    {
        if (slot < textureSlots.Length)
        {
            textureSlots[slot] = texture?.Backing as VKTexture2D;
        }
    }

    public void SetStorageImage(int slot, Texture? texture)
    {
        if (slot < storageImageSlots.Length)
        {
            storageImageSlots[slot] = texture?.Backing as VKTexture2D;
        }
    }

    public void SetSamplerState(int slot, SamplerState state)
    {
        if (slot < samplerSlots.Length)
        {
            samplerSlots[slot] = state;
        }
    }

    public void DrawNoVertexBuffer(PrimitiveTypes type, int primitiveCount)
    {
        if (currentShader == null)
        {
            return;
        }
        EnsureRendering();
        var cmd = CurrentCommands;
        BindPipelineNoVertices(cmd, type);
        BindDynamicState(cmd);
        BindDescriptors(cmd);
        Vk.CmdDraw(cmd, (uint)IndexCount(type, primitiveCount), 1, 0, 0);
    }

    private readonly VertexDeclaration emptyDeclaration = new(0);

    private void BindPipelineNoVertices(IntPtr cmd, PrimitiveTypes primitive) =>
        BindPipeline(cmd, emptyDeclaration, primitive);

    public void DrawMeshTasks(uint groupsX, uint groupsY, uint groupsZ)
    {
        if (currentShader is not { IsMeshPipeline: true } || Vk.CmdDrawMeshTasksEXT == null)
        {
            return;
        }
        EnsureRendering();
        var cmd = CurrentCommands;
        BindPipelineNoVertices(cmd, PrimitiveTypes.TriangleList);
        BindDynamicState(cmd);
        BindDescriptors(cmd);
        Vk.CmdDrawMeshTasksEXT(cmd, groupsX, groupsY, groupsZ);
    }
}
