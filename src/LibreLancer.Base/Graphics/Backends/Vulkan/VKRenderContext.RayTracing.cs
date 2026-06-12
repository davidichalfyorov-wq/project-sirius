using System;
using System.Collections.Generic;
using System.Numerics;

namespace LibreLancer.Graphics.Backends.Vulkan;

// Ray tracing services (graphics roadmap phase 4): BLAS builds on one-shot
// command buffers, a grow-only per-frame TLAS recorded into the frame
// command buffer, and deferred destruction tied to the frame counter.
// Ray query consumption happens in fragment shaders (no RT pipeline/SBT).
internal unsafe partial class VKRenderContext : IRayTracing
{
    private sealed class BlasEntry
    {
        public ulong Handle;            // VkAccelerationStructureKHR
        public ulong DeviceAddress;
        public VKBuffer AsBuffer;       // dedicated DEVICE_LOCAL
        public VKBuffer InputBuffer;    // dedicated host-visible (kept: rebuild safety)
        public bool Alive;
    }

    private readonly List<BlasEntry> blasEntries = new();
    private readonly Queue<(long Frame, ulong Handle, VKBuffer AsBuffer, VKBuffer InputBuffer)> deferredBlas = new();

    // Grow-only TLAS state. Single-frame semantics (EnsureFrameBegun waits
    // all in-flight fences) make reusing one buffer set race-free; if the
    // frame scheduler ever overlaps frames this needs double-buffering.
    private VKBuffer tlasInstanceBuffer;
    private VKBuffer tlasBuffer;
    private VKBuffer tlasScratch;
    private ulong tlasHandle;
    private ulong tlasCreatedSize;
    private ulong emptyTlasHandle;
    private VKBuffer emptyTlasBuffer;
    private ulong boundTlas; // consumed by the descriptor path (B5)
    private int lastInstanceCount;

    public IRayTracing? RayTracing => rayQuerySupported ? this : null;

    public int BlasCount
    {
        get
        {
            var count = 0;
            foreach (var entry in blasEntries)
            {
                if (entry.Alive)
                {
                    count++;
                }
            }
            return count;
        }
    }

    public int LastInstanceCount => lastInstanceCount;

    private ulong BufferAddress(in VKBuffer buffer)
    {
        var info = new VkBufferDeviceAddressInfo
        {
            SType = VkStructureType.BufferDeviceAddressInfo,
            Buffer = buffer.Buffer
        };
        return Vk.GetBufferDeviceAddress(device, &info);
    }

    private static ulong AlignUp(ulong value, ulong alignment) =>
        (value + alignment - 1) & ~(alignment - 1);

    public BlasHandle CreateBlas(ReadOnlySpan<byte> vertexData, int vertexStride, int vertexCount,
        ReadOnlySpan<ushort> indices, ReadOnlySpan<BlasRange> ranges)
    {
        if (!rayQuerySupported || ranges.Length == 0 || vertexCount == 0 || indices.Length == 0)
        {
            return BlasHandle.Invalid;
        }

        // Input buffer: vertices then indices (16-bit), both device-address
        // visible to the AS build. 4-byte align the index block.
        var vertexBytes = (ulong)vertexData.Length;
        var indexOffset = AlignUp(vertexBytes, 4);
        var totalSize = indexOffset + (ulong)(indices.Length * 2);
        var input = Memory.CreateDeviceAddressBuffer(totalSize,
            0x80000 /*ACCELERATION_STRUCTURE_BUILD_INPUT_READ_ONLY*/, hostVisible: true);
        vertexData.CopyTo(new Span<byte>((void*)input.Mapped, vertexData.Length));
        var indexSpan = new Span<byte>((byte*)input.Mapped + indexOffset, indices.Length * 2);
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(indices).CopyTo(indexSpan);

        var inputAddress = BufferAddress(input);

        var geometries = stackalloc VkAccelerationStructureGeometryKHR_Triangles[ranges.Length];
        var rangeInfos = stackalloc VkAccelerationStructureBuildRangeInfoKHR[ranges.Length];
        var primitiveCounts = stackalloc uint[ranges.Length];
        for (var i = 0; i < ranges.Length; i++)
        {
            geometries[i] = new VkAccelerationStructureGeometryKHR_Triangles
            {
                SType = VkStructureType.AccelerationStructureGeometryKHR,
                GeometryType = 0, // TRIANGLES
                Triangles = new VkAccelerationStructureGeometryTrianglesDataKHR
                {
                    SType = VkStructureType.AccelerationStructureGeometryTrianglesDataKHR,
                    VertexFormat = 106, // R32G32B32_SFLOAT
                    VertexData = inputAddress,
                    VertexStride = (ulong)vertexStride,
                    MaxVertex = (uint)(vertexCount - 1),
                    IndexType = 0, // UINT16
                    IndexData = inputAddress + indexOffset
                },
                Flags = 0x1 // OPAQUE
            };
            rangeInfos[i] = new VkAccelerationStructureBuildRangeInfoKHR
            {
                PrimitiveCount = (uint)ranges[i].TriangleCount,
                PrimitiveOffset = (uint)(ranges[i].FirstIndex * 2),
                FirstVertex = (uint)ranges[i].BaseVertex
            };
            primitiveCounts[i] = (uint)ranges[i].TriangleCount;
        }

        var buildInfo = new VkAccelerationStructureBuildGeometryInfoKHR
        {
            SType = VkStructureType.AccelerationStructureBuildGeometryInfoKHR,
            Type = 1, // BOTTOM_LEVEL
            Flags = 0x4, // PREFER_FAST_TRACE
            Mode = 0, // BUILD
            GeometryCount = (uint)ranges.Length,
            PGeometries = geometries
        };
        var sizes = new VkAccelerationStructureBuildSizesInfoKHR
        {
            SType = VkStructureType.AccelerationStructureBuildSizesInfoKHR
        };
        Vk.GetAccelerationStructureBuildSizesKHR(device, 1 /*DEVICE*/, &buildInfo, primitiveCounts, &sizes);

        var asBuffer = Memory.CreateDeviceAddressBuffer(sizes.AccelerationStructureSize,
            0x100000 /*ACCELERATION_STRUCTURE_STORAGE*/, hostVisible: false);
        var scratch = Memory.CreateDeviceAddressBuffer(
            AlignUp(sizes.BuildScratchSize, asScratchAlignment) + asScratchAlignment,
            0x20 /*STORAGE_BUFFER*/, hostVisible: false);

        var createInfo = new VkAccelerationStructureCreateInfoKHR
        {
            SType = VkStructureType.AccelerationStructureCreateInfoKHR,
            Buffer = asBuffer.Buffer,
            Size = sizes.AccelerationStructureSize,
            Type = 1 // BOTTOM_LEVEL
        };
        ulong asHandle;
        Vk.Check(Vk.CreateAccelerationStructureKHR(device, &createInfo, null, &asHandle),
            "vkCreateAccelerationStructureKHR(blas)");
        SetObjectName(1000150000, asHandle, $"BLAS {ranges.Length}geo {vertexCount}v");

        buildInfo.DstAccelerationStructure = asHandle;
        buildInfo.ScratchData = AlignUp(BufferAddress(scratch), asScratchAlignment);

        var cmd = BeginOneShotCommands();
        var pRanges = rangeInfos;
        Vk.CmdBuildAccelerationStructuresKHR(cmd, 1, &buildInfo, &pRanges);
        // Scratch retires with the one-shot fence; the input buffer stays
        // alive for the BLAS lifetime (rebuild-on-evict safety, B10 trims).
        EndOneShotCommands(cmd, scratch);

        var addressInfo = new VkAccelerationStructureDeviceAddressInfoKHR
        {
            SType = VkStructureType.AccelerationStructureDeviceAddressInfoKHR,
            AccelerationStructure = asHandle
        };
        var entry = new BlasEntry
        {
            Handle = asHandle,
            DeviceAddress = Vk.GetAccelerationStructureDeviceAddressKHR(device, &addressInfo),
            AsBuffer = asBuffer,
            InputBuffer = input,
            Alive = true
        };
        blasEntries.Add(entry);
        return new BlasHandle(blasEntries.Count - 1);
    }

    public void DestroyBlas(BlasHandle handle)
    {
        if (handle.Id < 0 || handle.Id >= blasEntries.Count || !blasEntries[handle.Id].Alive)
        {
            return;
        }
        var entry = blasEntries[handle.Id];
        entry.Alive = false;
        lock (queueLock)
        {
            deferredBlas.Enqueue((frameCounter, entry.Handle, entry.AsBuffer, entry.InputBuffer));
        }
    }

    private void DrainDeferredBlas(long completedFrame)
    {
        while (deferredBlas.Count > 0 && deferredBlas.Peek().Frame <= completedFrame)
        {
            var (_, handle, asBuffer, inputBuffer) = deferredBlas.Dequeue();
            if (handle != 0)
            {
                Vk.DestroyAccelerationStructureKHR(device, handle, null);
            }
            if (asBuffer.Buffer != 0)
            {
                Memory.DestroyDedicated(asBuffer);
            }
            if (inputBuffer.Buffer != 0)
            {
                Memory.DestroyDedicated(inputBuffer);
            }
        }
    }

    /// <summary>An always-valid 0-instance TLAS: descriptor sets must have
    /// something written even before the first real build.</summary>
    private void EnsureEmptyTlas()
    {
        if (emptyTlasHandle != 0)
        {
            return;
        }
        var geometry = new VkAccelerationStructureGeometryKHR_Instances
        {
            SType = VkStructureType.AccelerationStructureGeometryKHR,
            GeometryType = 2, // INSTANCES
            InstSType = VkStructureType.AccelerationStructureGeometryInstancesDataKHR
        };
        var buildInfo = new VkAccelerationStructureBuildGeometryInfoKHR
        {
            SType = VkStructureType.AccelerationStructureBuildGeometryInfoKHR,
            Type = 0, // TOP_LEVEL
            Flags = 0x8, // PREFER_FAST_BUILD
            GeometryCount = 1,
            PGeometries = &geometry
        };
        uint zero = 0;
        var sizes = new VkAccelerationStructureBuildSizesInfoKHR
        {
            SType = VkStructureType.AccelerationStructureBuildSizesInfoKHR
        };
        Vk.GetAccelerationStructureBuildSizesKHR(device, 1, &buildInfo, &zero, &sizes);

        emptyTlasBuffer = Memory.CreateDeviceAddressBuffer(sizes.AccelerationStructureSize,
            0x100000, hostVisible: false);
        var scratch = Memory.CreateDeviceAddressBuffer(
            AlignUp(sizes.BuildScratchSize, asScratchAlignment) + asScratchAlignment,
            0x20, hostVisible: false);
        var createInfo = new VkAccelerationStructureCreateInfoKHR
        {
            SType = VkStructureType.AccelerationStructureCreateInfoKHR,
            Buffer = emptyTlasBuffer.Buffer,
            Size = sizes.AccelerationStructureSize,
            Type = 0
        };
        ulong handle;
        Vk.Check(Vk.CreateAccelerationStructureKHR(device, &createInfo, null, &handle),
            "vkCreateAccelerationStructureKHR(empty tlas)");
        SetObjectName(1000150000, handle, "TLAS empty");
        emptyTlasHandle = handle;

        buildInfo.DstAccelerationStructure = handle;
        buildInfo.ScratchData = AlignUp(BufferAddress(scratch), asScratchAlignment);
        var range = new VkAccelerationStructureBuildRangeInfoKHR();
        var pRange = &range;
        var cmd = BeginOneShotCommands();
        Vk.CmdBuildAccelerationStructuresKHR(cmd, 1, &buildInfo, &pRange);
        EndOneShotCommands(cmd, scratch);
        boundTlas = emptyTlasHandle;
        FLLog.Debug("Vulkan", "Empty TLAS built");
    }

    public void BuildSceneTlas(ReadOnlySpan<TlasInstance> instances)
    {
        if (!rayQuerySupported)
        {
            return;
        }
        EnsureEmptyTlas();
        lastInstanceCount = instances.Length;
        if (instances.Length == 0)
        {
            boundTlas = emptyTlasHandle;
            return;
        }

        // Grow-only instance buffer (64 bytes each).
        var needed = (ulong)(instances.Length * 64);
        if (tlasInstanceBuffer.Buffer == 0 || tlasInstanceBuffer.Size < needed)
        {
            if (tlasInstanceBuffer.Buffer != 0)
            {
                lock (queueLock)
                {
                    deferredBlas.Enqueue((frameCounter, 0, tlasInstanceBuffer, default));
                }
            }
            tlasInstanceBuffer = Memory.CreateDeviceAddressBuffer(
                Math.Max(needed * 2, 256 * 64),
                0x80000, hostVisible: true);
        }

        var dst = (VkAccelerationStructureInstanceKHR*)tlasInstanceBuffer.Mapped;
        for (var i = 0; i < instances.Length; i++)
        {
            var entry = blasEntries[instances[i].Blas.Id];
            var m = instances[i].Transform;
            // VkTransformMatrixKHR: 3 rows x 4 cols applied to column
            // vectors; System.Numerics matrices are row-vector - transpose.
            dst[i].Transform[0] = m.M11; dst[i].Transform[1] = m.M21; dst[i].Transform[2] = m.M31; dst[i].Transform[3] = m.M41;
            dst[i].Transform[4] = m.M12; dst[i].Transform[5] = m.M22; dst[i].Transform[6] = m.M32; dst[i].Transform[7] = m.M42;
            dst[i].Transform[8] = m.M13; dst[i].Transform[9] = m.M23; dst[i].Transform[10] = m.M33; dst[i].Transform[11] = m.M43;
            dst[i].InstanceCustomIndexAndMask = (instances[i].Id & 0xFFFFFF) | 0xFF000000u;
            dst[i].InstanceShaderBindingTableRecordOffsetAndFlags = 0;
            dst[i].AccelerationStructureReference = entry.DeviceAddress;
        }

        var geometry = new VkAccelerationStructureGeometryKHR_Instances
        {
            SType = VkStructureType.AccelerationStructureGeometryKHR,
            GeometryType = 2, // INSTANCES
            InstSType = VkStructureType.AccelerationStructureGeometryInstancesDataKHR,
            Data = BufferAddress(tlasInstanceBuffer)
        };
        var buildInfo = new VkAccelerationStructureBuildGeometryInfoKHR
        {
            SType = VkStructureType.AccelerationStructureBuildGeometryInfoKHR,
            Type = 0, // TOP_LEVEL
            Flags = 0x8, // PREFER_FAST_BUILD
            GeometryCount = 1,
            PGeometries = &geometry
        };
        var instanceCount = (uint)instances.Length;
        var sizes = new VkAccelerationStructureBuildSizesInfoKHR
        {
            SType = VkStructureType.AccelerationStructureBuildSizesInfoKHR
        };
        Vk.GetAccelerationStructureBuildSizesKHR(device, 1, &buildInfo, &instanceCount, &sizes);

        // The AS object is created with a fixed size: recreate handle AND
        // buffer together whenever the build outgrows what was created
        // (validation VUID-...-10126 fires otherwise).
        if (tlasHandle == 0 || tlasCreatedSize < sizes.AccelerationStructureSize)
        {
            if (tlasHandle != 0)
            {
                lock (queueLock)
                {
                    deferredBlas.Enqueue((frameCounter, tlasHandle, tlasBuffer, default));
                }
                tlasHandle = 0;
            }
            tlasBuffer = Memory.CreateDeviceAddressBuffer(sizes.AccelerationStructureSize * 2,
                0x100000, hostVisible: false);
            tlasCreatedSize = tlasBuffer.Size;
            var createInfo = new VkAccelerationStructureCreateInfoKHR
            {
                SType = VkStructureType.AccelerationStructureCreateInfoKHR,
                Buffer = tlasBuffer.Buffer,
                Size = tlasCreatedSize,
                Type = 0
            };
            ulong handle;
            Vk.Check(Vk.CreateAccelerationStructureKHR(device, &createInfo, null, &handle),
                "vkCreateAccelerationStructureKHR(tlas)");
            SetObjectName(1000150000, handle, "TLAS scene");
            tlasHandle = handle;
        }

        var scratchNeeded = AlignUp(sizes.BuildScratchSize, asScratchAlignment) + asScratchAlignment;
        if (tlasScratch.Buffer == 0 || tlasScratch.Size < scratchNeeded)
        {
            if (tlasScratch.Buffer != 0)
            {
                lock (queueLock)
                {
                    deferredBlas.Enqueue((frameCounter, 0, tlasScratch, default));
                }
            }
            tlasScratch = Memory.CreateDeviceAddressBuffer(scratchNeeded * 2, 0x20, hostVisible: false);
        }

        buildInfo.DstAccelerationStructure = tlasHandle;
        buildInfo.ScratchData = AlignUp(BufferAddress(tlasScratch), asScratchAlignment);

        // Record into the frame command buffer, outside any render pass.
        EnsureFrameBegun();
        EndRenderingIfActive();
        var cmd = CurrentCommands;

        // One-shot BLAS builds (graphics queue, earlier submissions) ->
        // TLAS build, then TLAS build -> fragment ray query reads.
        var beforeBarrier = new VkMemoryBarrier2
        {
            SType = VkStructureType.MemoryBarrier2,
            SrcStageMask = 0x02000000ul,  // ACCELERATION_STRUCTURE_BUILD
            SrcAccessMask = 0x400000ul,   // ACCELERATION_STRUCTURE_WRITE (bit 22)
            DstStageMask = 0x02000000ul,
            DstAccessMask = 0x200000ul | 0x400000ul // AS_READ | AS_WRITE (bits 21/22)
        };
        var beforeDependency = new VkDependencyInfo
        {
            SType = VkStructureType.DependencyInfo,
            MemoryBarrierCount = 1,
            PMemoryBarriers = &beforeBarrier
        };
        Vk.CmdPipelineBarrier2(cmd, &beforeDependency);

        var range = new VkAccelerationStructureBuildRangeInfoKHR { PrimitiveCount = instanceCount };
        var pRange = &range;
        Vk.CmdBuildAccelerationStructuresKHR(cmd, 1, &buildInfo, &pRange);

        var afterBarrier = new VkMemoryBarrier2
        {
            SType = VkStructureType.MemoryBarrier2,
            SrcStageMask = 0x02000000ul,
            SrcAccessMask = 0x400000ul, // ACCELERATION_STRUCTURE_WRITE
            DstStageMask = 0x80ul,      // FRAGMENT_SHADER
            DstAccessMask = 0x200000ul  // ACCELERATION_STRUCTURE_READ
        };
        var afterDependency = new VkDependencyInfo
        {
            SType = VkStructureType.DependencyInfo,
            MemoryBarrierCount = 1,
            PMemoryBarriers = &afterBarrier
        };
        Vk.CmdPipelineBarrier2(cmd, &afterDependency);

        boundTlas = tlasHandle;
    }
}
