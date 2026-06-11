using System;
using System.Collections.Generic;

namespace LibreLancer.Graphics.Backends.Vulkan;

/// <summary>
/// Sub-allocating device-memory pool. Drivers cap live vkAllocateMemory
/// allocations (typically 4096); a Freelancer system holds thousands of
/// buffers and textures, so resources share large blocks. First-fit
/// free-list with neighbour coalescing - allocation patterns here are
/// coarse (load screens, frame transients), not hot-path critical.
/// </summary>
internal sealed unsafe class VKMemoryPool
{
    private const ulong BlockSize = 64UL * 1024 * 1024;

    private sealed class Block
    {
        public ulong Memory;
        public IntPtr Mapped;
        public readonly List<(ulong Offset, ulong Size)> Free = new();
    }

    private readonly IntPtr device;
    private readonly uint memoryTypeIndex;
    private readonly bool hostVisible;
    private readonly List<Block> blocks = new();
    private readonly object poolLock = new();

    public VKMemoryPool(IntPtr device, uint memoryTypeIndex, bool hostVisible)
    {
        this.device = device;
        this.memoryTypeIndex = memoryTypeIndex;
        this.hostVisible = hostVisible;
    }

    public (ulong Memory, ulong Offset, IntPtr Mapped) Allocate(ulong size, ulong alignment)
    {
        lock (poolLock)
        {
            // Resources larger than a block get a dedicated allocation.
            if (size > BlockSize)
            {
                var dedicated = NewBlock(size);
                dedicated.Free.Clear();
                return (dedicated.Memory, 0, dedicated.Mapped);
            }

            foreach (var block in blocks)
            {
                for (var i = 0; i < block.Free.Count; i++)
                {
                    var (offset, free) = block.Free[i];
                    var aligned = (offset + alignment - 1) & ~(alignment - 1);
                    var padding = aligned - offset;
                    if (free < padding + size)
                    {
                        continue;
                    }

                    block.Free.RemoveAt(i);
                    if (padding > 0)
                    {
                        block.Free.Insert(i, (offset, padding));
                    }
                    var tail = free - padding - size;
                    if (tail > 0)
                    {
                        block.Free.Add((aligned + size, tail));
                    }
                    return (block.Memory, aligned,
                        block.Mapped == IntPtr.Zero ? IntPtr.Zero : block.Mapped + (nint)aligned);
                }
            }

            var fresh = NewBlock(BlockSize);
            fresh.Free.Clear();
            var remainder = BlockSize - size;
            if (remainder > 0)
            {
                fresh.Free.Add((size, remainder));
            }
            return (fresh.Memory, 0, fresh.Mapped);
        }
    }

    public void Free(ulong memory, ulong offset, ulong size)
    {
        lock (poolLock)
        {
            foreach (var block in blocks)
            {
                if (block.Memory != memory)
                {
                    continue;
                }
                foreach (var (freeOffset, freeSize) in block.Free)
                {
                    if (offset < freeOffset + freeSize && freeOffset < offset + size)
                    {
                        FLLog.Error("Vulkan", $"Pool DOUBLE FREE: ({offset},{size}) overlaps free ({freeOffset},{freeSize}) mem={memory:X}");
                    }
                }
                // Insert and coalesce with neighbours.
                var entry = (offset, size);
                for (var i = block.Free.Count - 1; i >= 0; i--)
                {
                    var (freeOffset, freeSize) = block.Free[i];
                    if (freeOffset + freeSize == entry.offset)
                    {
                        entry = (freeOffset, freeSize + entry.size);
                        block.Free.RemoveAt(i);
                    }
                    else if (entry.offset + entry.size == freeOffset)
                    {
                        entry = (entry.offset, entry.size + freeSize);
                        block.Free.RemoveAt(i);
                    }
                }
                block.Free.Add(entry);
                return;
            }
        }
    }

    private Block NewBlock(ulong size)
    {
        var allocateInfo = new VkMemoryAllocateInfo
        {
            SType = VkStructureType.MemoryAllocateInfo,
            AllocationSize = size,
            MemoryTypeIndex = memoryTypeIndex
        };
        ulong memory;
        Vk.Check(Vk.AllocateMemory(device, &allocateInfo, null, &memory), "vkAllocateMemory(pool)");
        var mapped = IntPtr.Zero;
        if (hostVisible)
        {
            void* pointer;
            Vk.Check(Vk.MapMemory(device, memory, 0, VkConst.WholeSize, 0, &pointer), "vkMapMemory(pool)");
            mapped = (IntPtr)pointer;
        }
        var block = new Block { Memory = memory, Mapped = mapped };
        block.Free.Add((0, size));
        blocks.Add(block);
        FLLog.Debug("Vulkan", $"memory pool type {memoryTypeIndex}: new {size / (1024 * 1024)}MB block ({blocks.Count} total)");
        return block;
    }
}
