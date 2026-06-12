using System;
using System.Runtime.CompilerServices;

namespace LibreLancer.Graphics.Backends.Vulkan;

/// <summary>
/// Engine-visible storage buffer (bone matrices, particles): persistently
/// mapped host memory; BindTo publishes a slice to the context, which
/// routes it into the matching SPIR-V storage-buffer descriptor at draw
/// time.
///
/// Streaming clients (BeginStreaming each frame) rotate through a small
/// ring of buffers: the CPU starts writing the next frame's data before
/// EnsureFrameBegun waits on the previous frame's fence, so writing the
/// buffer the GPU is still reading produced torn particle quads (the
/// lava-planet menu flicker). Static clients never call BeginStreaming
/// and stay on buffer 0.
/// </summary>
internal unsafe class VKStorageBuffer : IStorageBuffer
{
    private readonly VKRenderContext context;
    private readonly int stride;
    private readonly VKBuffer[] buffers;
    private int current;

    public VKBuffer Buffer => buffers[current];

    private const int OffsetAlignment = 256; // conservative minStorageBufferOffsetAlignment
    private const int StreamingRing = 3;

    public VKStorageBuffer(VKRenderContext context, int size, int stride)
    {
        this.context = context;
        this.stride = stride;
        buffers = new VKBuffer[StreamingRing];
        for (var i = 0; i < buffers.Length; i++)
        {
            buffers[i] = context.Memory.CreateHostBuffer((ulong)size, VKMemory.UsageStorage);
        }
    }

    public int GetAlignedIndex(int index)
    {
        var bytes = index * stride;
        var aligned = (bytes + OffsetAlignment - 1) & ~(OffsetAlignment - 1);
        return aligned / stride;
    }

    public void BindTo(int binding, int start = 0, int count = 0)
    {
        var offset = (ulong)(start * stride);
        var range = count > 0 ? (ulong)(count * stride) : Buffer.Size - offset;
        context.BindStorageSlot(binding, Buffer.Buffer, offset, range);
    }

    public ref T Data<T>(int i) where T : unmanaged =>
        ref Unsafe.AsRef<T>((byte*)Buffer.Mapped + i * stride);

    public IntPtr BeginStreaming()
    {
        current = (current + 1) % buffers.Length;
        return Buffer.Mapped;
    }

    public void EndStreaming(int count)
    {
    }

    public void Dispose()
    {
        foreach (var buffer in buffers)
        {
            context.DeferDestroy(buffer);
        }
    }
}
