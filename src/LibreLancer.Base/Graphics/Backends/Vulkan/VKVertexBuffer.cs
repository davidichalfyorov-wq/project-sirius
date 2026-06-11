using System;
using System.Runtime.InteropServices;
using LibreLancer.Graphics.Vertices;

namespace LibreLancer.Graphics.Backends.Vulkan;

internal unsafe class VKElementBuffer : IElementBuffer
{
    private readonly VKRenderContext context;
    public VKBuffer Buffer;
    public int IndexCount { get; private set; }
    public long LastUsedFrame = -1;

    public VKElementBuffer(VKRenderContext context, int count)
    {
        this.context = context;
        IndexCount = count;
        Buffer = context.Memory.CreateHostBuffer((ulong)(count * 2), VKMemory.UsageIndex | VKMemory.UsageTransferDst);
    }

    public void SetData(short[] data) =>
        context.UploadIntoBuffer(Buffer, 0, MemoryMarshal.Cast<short, byte>(data), LastUsedFrame);

    public void SetData(ReadOnlySpan<ushort> data) =>
        context.UploadIntoBuffer(Buffer, 0, MemoryMarshal.Cast<ushort, byte>(data), LastUsedFrame);

    public void SetData(ushort[] data, int count, int start = 0) =>
        context.UploadIntoBuffer(Buffer, start * 2,
            MemoryMarshal.Cast<ushort, byte>(data.AsSpan(0, count)), LastUsedFrame);

    public void Expand(int newSize)
    {
        var old = Buffer;
        var replacement = context.Memory.CreateHostBuffer((ulong)(newSize * 2), VKMemory.UsageIndex | VKMemory.UsageTransferDst);
        old.AsSpan().CopyTo(replacement.AsSpan());
        context.DeferDestroy(old);
        Buffer = replacement;
        IndexCount = newSize;
    }

    public void Dispose() => context.DeferDestroy(Buffer);
}

internal unsafe class VKVertexBuffer : IVertexBuffer
{
    private readonly VKRenderContext context;
    public VKBuffer Buffer;
    public readonly VertexDeclaration Declaration;
    public IVertexType VertexType { get; }
    public int VertexCount { get; private set; }
    public VKElementBuffer? Elements;

    // Streaming contract (mirrors GL's orphan+subdata): the frontend writes
    // into a persistent CPU scratch from vertex 0, EndStreaming publishes
    // those vertices. Each batch gets its own transient GPU buffer so a
    // later batch can't overwrite data an already-recorded draw still needs.
    // Unmanaged like GL's CPU shadow: frontend code may keep a stale
    // pointer across Expand, which must never stomp managed heap memory.
    private IntPtr streamScratch;
    private int streamScratchSize;
    private VKBuffer streamActive;
    public long LastUsedFrame = -1;
    public VKBuffer ActiveBuffer => streamScratch != IntPtr.Zero && streamActive.Buffer != 0 ? streamActive : Buffer;

    public VKVertexBuffer(VKRenderContext context, IVertexType type, int length, bool isStream)
    {
        this.context = context;
        VertexType = type;
        Declaration = type.GetVertexDeclaration();
        VertexCount = length;
        Buffer = context.Memory.CreateHostBuffer((ulong)(length * Declaration.Stride),
            VKMemory.UsageVertex | VKMemory.UsageTransferDst);
        if (isStream)
        {
            streamScratchSize = Math.Max(length, 1) * Declaration.Stride;
            streamScratch = System.Runtime.InteropServices.Marshal.AllocHGlobal(streamScratchSize);
        }
    }

    public void SetData<T>(ReadOnlySpan<T> data, int offset = 0) where T : unmanaged =>
        // offset is in VERTICES (GL: glBufferSubData at offset * stride).
        context.UploadIntoBuffer(Buffer, offset * Declaration.Stride,
            MemoryMarshal.Cast<T, byte>(data), LastUsedFrame);

    public void Expand(int newSize)
    {
        var old = Buffer;
        var replacement = context.Memory.CreateHostBuffer(
            (ulong)(newSize * Declaration.Stride), VKMemory.UsageVertex | VKMemory.UsageTransferDst);
        old.AsSpan().CopyTo(replacement.AsSpan());
        context.DeferDestroy(old);
        Buffer = replacement;
        VertexCount = newSize;
        if (streamScratch != IntPtr.Zero)
        {
            streamScratchSize = newSize * Declaration.Stride;
            streamScratch = System.Runtime.InteropServices.Marshal.ReAllocHGlobal(
                streamScratch, streamScratchSize);
        }
    }

    public IntPtr BeginStreaming() =>
        streamScratch != IntPtr.Zero ? streamScratch : Buffer.Mapped;

    public unsafe void EndStreaming(int count)
    {
        if (streamScratch == IntPtr.Zero || count == 0)
        {
            return;
        }
        var bytes = Math.Min(count * Declaration.Stride, streamScratchSize);
        var transient = context.RentTransient((ulong)bytes, VKMemory.UsageVertex);
        new ReadOnlySpan<byte>((void*)streamScratch, bytes).CopyTo(transient.AsSpan());
        if (streamActive.Buffer != 0)
        {
            context.RecycleTransient(streamActive, VKMemory.UsageVertex);
        }
        streamActive = transient;
    }

    // Mirrors GLVertexBuffer: every draw first flushes the frontend's
    // requested state (shader/blend/depth set by materials) into the
    // backend via RenderContext.Instance.Apply().
    public void Draw(PrimitiveTypes primitiveType, int baseVertex, int startIndex, int primitiveCount)
    {
        RenderContext.Instance.Apply();
        context.RecordDraw(this, primitiveType, baseVertex, startIndex, primitiveCount, indexed: true);
    }

    public void Draw(PrimitiveTypes primitiveType, int primitiveCount)
    {
        RenderContext.Instance.Apply();
        context.RecordDraw(this, primitiveType, 0, 0, primitiveCount, indexed: Elements != null);
    }

    public void Draw(PrimitiveTypes primitiveType, int start, int primitiveCount)
    {
        RenderContext.Instance.Apply();
        context.RecordDraw(this, primitiveType, 0, start, primitiveCount, indexed: Elements != null);
    }

    public void DrawImmediateElements(PrimitiveTypes primitiveTypes, int baseVertex, ReadOnlySpan<ushort> elements)
    {
        RenderContext.Instance.Apply();
        context.RecordDrawImmediate(this, primitiveTypes, baseVertex, elements);
    }

    public void SetElementBuffer(IElementBuffer elements) => Elements = (VKElementBuffer)elements;

    public void UnsetElementBuffer() => Elements = null;

    public void Dispose()
    {
        context.DeferDestroy(Buffer);
        if (streamActive.Buffer != 0)
        {
            // Rented in EndStreaming, so it goes back to the pool.
            context.RecycleTransient(streamActive, VKMemory.UsageVertex);
        }
        if (streamScratch != IntPtr.Zero)
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(streamScratch);
            streamScratch = IntPtr.Zero;
        }
    }
}
