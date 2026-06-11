using System;
using System.Runtime.CompilerServices;

namespace LibreLancer.Graphics.Backends.Vulkan;

/// <summary>
/// Engine-visible storage buffer (bone matrices etc.): persistently mapped
/// host memory; BindTo publishes a slice to the context, which routes it
/// into the matching SPIR-V storage-buffer descriptor at draw time.
/// </summary>
internal unsafe class VKStorageBuffer : IStorageBuffer
{
    private readonly VKRenderContext context;
    private readonly int stride;
    public VKBuffer Buffer;

    private const int OffsetAlignment = 256; // conservative minStorageBufferOffsetAlignment

    public VKStorageBuffer(VKRenderContext context, int size, int stride)
    {
        this.context = context;
        this.stride = stride;
        Buffer = context.Memory.CreateHostBuffer((ulong)size, VKMemory.UsageStorage);
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

    public IntPtr BeginStreaming() => Buffer.Mapped;

    public void EndStreaming(int count)
    {
    }

    public void Dispose() => context.DeferDestroy(Buffer);
}
