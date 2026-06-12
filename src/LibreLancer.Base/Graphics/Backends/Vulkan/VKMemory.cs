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

    /// <summary>Owning context, for debug object naming. Set right after
    /// construction (VKMemory is created before the context finishes
    /// initialising).</summary>
    public VKRenderContext? Context;

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

    /// <summary>Total device memory across all pools (Dev HUD).</summary>
    public ulong TotalAllocated
    {
        get
        {
            ulong total = 0;
            foreach (var pool in pools.Values)
            {
                total += pool.AllocatedBytes;
            }
            return total;
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
        // VK_OBJECT_TYPE_BUFFER = 9
        Context?.SetObjectName(9, buffer, $"Buf usage=0x{usage:X} {size}b");
        return new VKBuffer(buffer, memory, mapped, size, offset, requirements.Size);
    }

    /// <summary>
    /// Dedicated allocation with VkMemoryAllocateFlagsInfo{DEVICE_ADDRESS}
    /// for ray tracing buffers (BLAS/TLAS inputs, AS storage, scratch).
    /// Lives outside the pools: destroy with DestroyDedicated. PoolSize==0
    /// marks the buffer as dedicated.
    /// </summary>
    public VKBuffer CreateDeviceAddressBuffer(ulong size, uint usage, bool hostVisible)
    {
        size = Math.Max(size, 256);
        var bufferInfo = new VkBufferCreateInfo
        {
            SType = VkStructureType.BufferCreateInfo,
            Size = size,
            Usage = usage | 0x20000 // SHADER_DEVICE_ADDRESS
        };
        ulong buffer;
        Vk.Check(Vk.CreateBuffer(device, &bufferInfo, null, &buffer), "vkCreateBuffer(rt)");

        VkMemoryRequirements requirements;
        Vk.GetBufferMemoryRequirements(device, buffer, &requirements);
        var flags = hostVisible ? (HostVisible | HostCoherent) : 0x1u; // DEVICE_LOCAL
        var typeIndex = FindMemoryType(requirements.MemoryTypeBits, flags);

        var allocFlags = new VkMemoryAllocateFlagsInfo
        {
            SType = VkStructureType.MemoryAllocateFlagsInfo,
            Flags = 0x2 // DEVICE_ADDRESS_BIT
        };
        var allocInfo = new VkMemoryAllocateInfo
        {
            SType = VkStructureType.MemoryAllocateInfo,
            PNext = &allocFlags,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = typeIndex
        };
        ulong memory;
        Vk.Check(Vk.AllocateMemory(device, &allocInfo, null, &memory), "vkAllocateMemory(rt)");
        Vk.Check(Vk.BindBufferMemory(device, buffer, memory, 0), "vkBindBufferMemory(rt)");

        var mapped = IntPtr.Zero;
        if (hostVisible)
        {
            void* pData;
            Vk.Check(Vk.MapMemory(device, memory, 0, requirements.Size, 0, &pData), "vkMapMemory(rt)");
            mapped = (IntPtr)pData;
        }
        Context?.SetObjectName(9, buffer, $"RTBuf usage=0x{usage:X} {size}b");
        return new VKBuffer(buffer, memory, mapped, size, 0, 0);
    }

    /// <summary>Destroys a CreateDeviceAddressBuffer allocation.</summary>
    public void DestroyDedicated(in VKBuffer buffer)
    {
        Vk.DestroyBuffer(device, buffer.Buffer, null);
        Vk.FreeMemory(device, buffer.Memory, null);
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
