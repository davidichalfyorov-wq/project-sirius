using System;

namespace LibreLancer.Graphics.Backends.Vulkan;

internal readonly record struct VKBuffer(
    ulong Buffer, ulong Memory, IntPtr Mapped, ulong Size, ulong PoolOffset, ulong PoolSize)
{
    public unsafe Span<byte> AsSpan() => new((void*)Mapped, (int)Size);
}

/// <summary>
/// Device memory services for the Vulkan backend. Buffers the engine
/// streams every frame live in persistently-mapped HOST_VISIBLE|COHERENT
/// memory (the modern resizable-BAR friendly path); DEVICE_LOCAL pools for
/// static data arrive with a later milestone.
/// </summary>
internal unsafe class VKMemory
{
    private readonly IntPtr device;
    private VkPhysicalDeviceMemoryProperties memoryProperties;
    private readonly System.Collections.Generic.Dictionary<uint, VKMemoryPool> pools = new();

    public const uint UsageVertex = 0x80;
    public const uint UsageIndex = 0x40;
    public const uint UsageUniform = 0x10;
    public const uint UsageStorage = 0x20;
    public const uint UsageTransferSrc = 0x1;
    public const uint UsageTransferDst = 0x2;

    private const uint HostVisible = 0x2;
    private const uint HostCoherent = 0x4;

    public VKMemory(IntPtr physicalDevice, IntPtr device)
    {
        this.device = device;
        fixed (VkPhysicalDeviceMemoryProperties* pProperties = &memoryProperties)
        {
            Vk.GetPhysicalDeviceMemoryProperties(physicalDevice, pProperties);
        }
    }

    public VKBuffer CreateHostBuffer(ulong size, uint usage)
    {
        // Engine code allocates zero-length buffers and Expand()s later;
        // Vulkan requires size > 0.
        size = Math.Max(size, 256);
        var bufferInfo = new VkBufferCreateInfo
        {
            SType = VkStructureType.BufferCreateInfo,
            Size = size,
            Usage = usage
        };
        ulong buffer;
        Vk.Check(Vk.CreateBuffer(device, &bufferInfo, null, &buffer), "vkCreateBuffer");

        VkMemoryRequirements requirements;
        Vk.GetBufferMemoryRequirements(device, buffer, &requirements);
        var typeIndex = FindMemoryType(requirements.MemoryTypeBits, HostVisible | HostCoherent);
        var (memory, offset, mapped) = GetPool(typeIndex, hostVisible: true)
            .Allocate(requirements.Size, requirements.Alignment);
        Vk.Check(Vk.BindBufferMemory(device, buffer, memory, offset), "vkBindBufferMemory");
        return new VKBuffer(buffer, memory, mapped, size, offset, requirements.Size);
    }

    public void Destroy(in VKBuffer buffer)
    {
        Vk.DestroyBuffer(device, buffer.Buffer, null);
        FreePooled(buffer.Memory, buffer.PoolOffset, buffer.PoolSize);
    }

    public (ulong Memory, ulong Offset, ulong Size) AllocateImage(ulong image)
    {
        VkMemoryRequirements requirements;
        Vk.GetImageMemoryRequirements(device, image, &requirements);
        var typeIndex = FindMemoryType(requirements.MemoryTypeBits, 0x1); // DEVICE_LOCAL
        var (memory, offset, _) = GetPool(typeIndex, hostVisible: false)
            .Allocate(requirements.Size, requirements.Alignment);
        Vk.Check(Vk.BindImageMemory(device, image, memory, offset), "vkBindImageMemory");
        return (memory, offset, requirements.Size);
    }

    public void FreePooled(ulong memory, ulong offset, ulong size)
    {
        lock (pools)
        {
            foreach (var pool in pools.Values)
            {
                pool.Free(memory, offset, size);
            }
        }
    }

    private VKMemoryPool GetPool(uint typeIndex, bool hostVisible)
    {
        lock (pools)
        {
            if (!pools.TryGetValue(typeIndex, out var pool))
            {
                pools[typeIndex] = pool = new VKMemoryPool(device, typeIndex, hostVisible);
            }
            return pool;
        }
    }

    public uint FindMemoryType(uint typeBits, uint requiredFlags)
    {
        for (var i = 0; i < memoryProperties.MemoryTypeCount; i++)
        {
            if ((typeBits & (1u << i)) != 0 &&
                (memoryProperties.TypeFlags(i) & requiredFlags) == requiredFlags)
            {
                return (uint)i;
            }
        }
        throw new InvalidOperationException("No Vulkan memory type satisfies the request");
    }
}
