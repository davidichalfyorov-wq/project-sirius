// Minimal raw Vulkan 1.3 bindings for the render backend.
// Hand-written against the Vulkan specification in the SDL3.cs style:
// commands are unmanaged function pointers resolved through
// vkGetInstanceProcAddr (obtained from SDL), device-level entry points are
// re-resolved through vkGetDeviceProcAddr for direct dispatch.

using System;
using System.Runtime.InteropServices;

namespace LibreLancer.Graphics.Backends.Vulkan;

internal enum VkResult
{
    Success = 0,
    NotReady = 1,
    Timeout = 2,
    Incomplete = 5,
    ErrorExtensionNotPresent = -7,
    ErrorOutOfDateKHR = -1000001004,
    SuboptimalKHR = 1000001003
}

internal enum VkStructureType
{
    ApplicationInfo = 0,
    DebugUtilsObjectNameInfoEXT = 1000128000,
    DebugUtilsLabelEXT = 1000128002,
    PhysicalDeviceVulkan12Features = 51,
    PhysicalDeviceProperties2 = 1000059001,
    MemoryAllocateFlagsInfo = 1000060000,
    BufferDeviceAddressInfo = 1000244001,
    AccelerationStructureBuildGeometryInfoKHR = 1000150000,
    AccelerationStructureDeviceAddressInfoKHR = 1000150002,
    AccelerationStructureGeometryInstancesDataKHR = 1000150004,
    AccelerationStructureGeometryTrianglesDataKHR = 1000150005,
    AccelerationStructureGeometryKHR = 1000150006,
    WriteDescriptorSetAccelerationStructureKHR = 1000150007,
    PhysicalDeviceAccelerationStructureFeaturesKHR = 1000150013,
    PhysicalDeviceAccelerationStructurePropertiesKHR = 1000150014,
    AccelerationStructureCreateInfoKHR = 1000150017,
    AccelerationStructureBuildSizesInfoKHR = 1000150020,
    PhysicalDeviceRayQueryFeaturesKHR = 1000348013,
    PhysicalDeviceMeshShaderFeaturesEXT = 1000328000,
    PhysicalDeviceMeshShaderPropertiesEXT = 1000328001,
    PhysicalDeviceFragmentShadingRateFeaturesKHR = 1000226003,
    PhysicalDeviceFragmentShadingRatePropertiesKHR = 1000226004,
    InstanceCreateInfo = 1,
    DeviceQueueCreateInfo = 2,
    DeviceCreateInfo = 3,
    SubmitInfo = 4,
    MemoryAllocateInfo = 5,
    FenceCreateInfo = 8,
    SemaphoreCreateInfo = 9,
    QueryPoolCreateInfo = 11,
    BufferCreateInfo = 12,
    ImageCreateInfo = 14,
    ImageViewCreateInfo = 15,
    ShaderModuleCreateInfo = 16,
    PipelineShaderStageCreateInfo = 18,
    PipelineVertexInputStateCreateInfo = 19,
    PipelineInputAssemblyStateCreateInfo = 20,
    PipelineViewportStateCreateInfo = 22,
    PipelineRasterizationStateCreateInfo = 23,
    PipelineMultisampleStateCreateInfo = 24,
    PipelineDepthStencilStateCreateInfo = 25,
    PipelineColorBlendStateCreateInfo = 26,
    PipelineDynamicStateCreateInfo = 27,
    GraphicsPipelineCreateInfo = 28,
    ComputePipelineCreateInfo = 29,
    PipelineLayoutCreateInfo = 30,
    SamplerCreateInfo = 31,
    DescriptorSetLayoutCreateInfo = 32,
    DescriptorPoolCreateInfo = 33,
    DescriptorSetAllocateInfo = 34,
    WriteDescriptorSet = 35,
    CommandPoolCreateInfo = 39,
    CommandBufferAllocateInfo = 40,
    CommandBufferBeginInfo = 42,
    PhysicalDeviceFeatures2 = 1000059000,
    PhysicalDeviceVulkan13Features = 53,
    SwapchainCreateInfoKHR = 1000001000,
    PresentInfoKHR = 1000001001,
    MemoryBarrier2 = 1000314000,
    ImageMemoryBarrier2 = 1000314002,
    DependencyInfo = 1000314003,
    SemaphoreSubmitInfo = 1000314005,
    CommandBufferSubmitInfo = 1000314006,
    SubmitInfo2 = 1000314004,
    RenderingInfo = 1000044000,
    RenderingAttachmentInfo = 1000044001,
    PipelineRenderingCreateInfo = 1000044002
}

internal enum VkFormat
{
    Undefined = 0,
    B8G8R8A8Unorm = 44,
    B8G8R8A8Srgb = 50,
    R8G8B8A8Unorm = 37
}

internal enum VkImageLayout
{
    Undefined = 0,
    TransferDstOptimal = 7,
    PresentSrcKHR = 1000001002
}

internal static class VkConst
{
    public const uint QueueGraphicsBit = 1;
    public const uint ImageUsageColorAttachment = 0x10;
    public const uint ImageUsageTransferDst = 0x2;
    public const uint ImageAspectColor = 1;
    public const uint CompositeAlphaOpaque = 1;
    public const int PresentModeFifo = 2;
    public const uint CommandPoolResetCommandBuffer = 2;
    public const uint FenceCreateSignaled = 1;
    public const ulong StageAllCommands = 0x10000UL;
    public const ulong StageClear = 0x80000UL;
    public const ulong StageColorAttachmentOutput = 0x400UL;
    public const ulong StageComputeShader = 0x800UL;
    public const ulong StageFragmentShader = 0x80UL;
    public const ulong AccessTransferWrite = 0x1000UL;
    public const ulong AccessMemoryRead = 0x8000UL;
    public const ulong AccessMemoryWrite = 0x10000UL;
    public const ulong AccessShaderRead = 0x20UL;
    public const ulong AccessShaderWrite = 0x40UL;
    public const ulong WholeSize = ~0UL;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkApplicationInfo
{
    public VkStructureType SType;
    public void* PNext;
    public byte* PApplicationName;
    public uint ApplicationVersion;
    public byte* PEngineName;
    public uint EngineVersion;
    public uint ApiVersion;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkInstanceCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public VkApplicationInfo* PApplicationInfo;
    public uint EnabledLayerCount;
    public byte** PpEnabledLayerNames;
    public uint EnabledExtensionCount;
    public byte** PpEnabledExtensionNames;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkDeviceQueueCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public uint QueueFamilyIndex;
    public uint QueueCount;
    public float* PQueuePriorities;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkDeviceCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public uint QueueCreateInfoCount;
    public VkDeviceQueueCreateInfo* PQueueCreateInfos;
    public uint EnabledLayerCount;
    public byte** PpEnabledLayerNames;
    public uint EnabledExtensionCount;
    public byte** PpEnabledExtensionNames;
    public void* PEnabledFeatures;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPhysicalDeviceVulkan13Features
{
    public VkStructureType SType;
    public void* PNext;
    public uint RobustImageAccess;
    public uint InlineUniformBlock;
    public uint DescriptorBindingInlineUniformBlockUpdateAfterBind;
    public uint PipelineCreationCacheControl;
    public uint PrivateData;
    public uint ShaderDemoteToHelperInvocation;
    public uint ShaderTerminateInvocation;
    public uint SubgroupSizeControl;
    public uint ComputeFullSubgroups;
    public uint Synchronization2;
    public uint TextureCompressionASTC_HDR;
    public uint ShaderZeroInitializeWorkgroupMemory;
    public uint DynamicRendering;
    public uint ShaderIntegerDotProduct;
    public uint Maintenance4;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPhysicalDeviceFeatures2
{
    public VkStructureType SType;
    public void* PNext;
    public fixed uint Features[55]; // VkPhysicalDeviceFeatures: 55 VkBool32 members
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkExtent2D
{
    public uint Width;
    public uint Height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkSurfaceCapabilitiesKHR
{
    public uint MinImageCount;
    public uint MaxImageCount;
    public VkExtent2D CurrentExtent;
    public VkExtent2D MinImageExtent;
    public VkExtent2D MaxImageExtent;
    public uint MaxImageArrayLayers;
    public uint SupportedTransforms;
    public uint CurrentTransform;
    public uint SupportedCompositeAlpha;
    public uint SupportedUsageFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkSurfaceFormatKHR
{
    public VkFormat Format;
    public int ColorSpace;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkSwapchainCreateInfoKHR
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public ulong Surface;
    public uint MinImageCount;
    public VkFormat ImageFormat;
    public int ImageColorSpace;
    public VkExtent2D ImageExtent;
    public uint ImageArrayLayers;
    public uint ImageUsage;
    public int ImageSharingMode;
    public uint QueueFamilyIndexCount;
    public uint* PQueueFamilyIndices;
    public uint PreTransform;
    public uint CompositeAlpha;
    public int PresentMode;
    public uint Clipped;
    public ulong OldSwapchain;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkCommandPoolCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public uint QueueFamilyIndex;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkCommandBufferAllocateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public ulong CommandPool;
    public int Level;
    public uint CommandBufferCount;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkCommandBufferBeginInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public void* PInheritanceInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkSemaphoreCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkFenceCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkQueryPoolCreateInfo
{
    public VkStructureType SType; // 11
    public void* PNext;
    public uint Flags;
    public int QueryType; // VK_QUERY_TYPE_TIMESTAMP = 2
    public uint QueryCount;
    public uint PipelineStatistics;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkImageSubresourceRange
{
    public uint AspectMask;
    public uint BaseMipLevel;
    public uint LevelCount;
    public uint BaseArrayLayer;
    public uint LayerCount;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkImageMemoryBarrier2
{
    public VkStructureType SType;
    public void* PNext;
    public ulong SrcStageMask;
    public ulong SrcAccessMask;
    public ulong DstStageMask;
    public ulong DstAccessMask;
    public VkImageLayout OldLayout;
    public VkImageLayout NewLayout;
    public uint SrcQueueFamilyIndex;
    public uint DstQueueFamilyIndex;
    public ulong Image;
    public VkImageSubresourceRange SubresourceRange;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkMemoryBarrier2
{
    public VkStructureType SType;
    public void* PNext;
    public ulong SrcStageMask;
    public ulong SrcAccessMask;
    public ulong DstStageMask;
    public ulong DstAccessMask;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkDependencyInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint DependencyFlags;
    public uint MemoryBarrierCount;
    public void* PMemoryBarriers;
    public uint BufferMemoryBarrierCount;
    public void* PBufferMemoryBarriers;
    public uint ImageMemoryBarrierCount;
    public VkImageMemoryBarrier2* PImageMemoryBarriers;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkSemaphoreSubmitInfo
{
    public VkStructureType SType;
    public void* PNext;
    public ulong Semaphore;
    public ulong Value;
    public ulong StageMask;
    public uint DeviceIndex;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkCommandBufferSubmitInfo
{
    public VkStructureType SType;
    public void* PNext;
    public IntPtr CommandBuffer;
    public uint DeviceMask;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkSubmitInfo2
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public uint WaitSemaphoreInfoCount;
    public VkSemaphoreSubmitInfo* PWaitSemaphoreInfos;
    public uint CommandBufferInfoCount;
    public VkCommandBufferSubmitInfo* PCommandBufferInfos;
    public uint SignalSemaphoreInfoCount;
    public VkSemaphoreSubmitInfo* PSignalSemaphoreInfos;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPresentInfoKHR
{
    public VkStructureType SType;
    public void* PNext;
    public uint WaitSemaphoreCount;
    public ulong* PWaitSemaphores;
    public uint SwapchainCount;
    public ulong* PSwapchains;
    public uint* PImageIndices;
    public VkResult* PResults;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkClearColorValue
{
    public float R;
    public float G;
    public float B;
    public float A;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkClearDepthStencilValue
{
    public float Depth;
    public uint Stencil;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkBufferCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public ulong Size;
    public uint Usage;
    public int SharingMode;
    public uint QueueFamilyIndexCount;
    public uint* PQueueFamilyIndices;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkMemoryRequirements
{
    public ulong Size;
    public ulong Alignment;
    public uint MemoryTypeBits;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkMemoryAllocateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public ulong AllocationSize;
    public uint MemoryTypeIndex;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPhysicalDeviceMemoryProperties
{
    public uint MemoryTypeCount;
    public fixed byte MemoryTypes[32 * 8];   // VkMemoryType { propertyFlags, heapIndex } x32
    public uint MemoryHeapCount;
    public fixed byte MemoryHeaps[16 * 16];  // VkMemoryHeap { size, flags } x16

    public uint TypeFlags(int index)
    {
        fixed (byte* p = MemoryTypes)
        {
            return *(uint*)(p + index * 8);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkShaderModuleCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public nuint CodeSize;
    public uint* PCode;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPipelineShaderStageCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public uint Stage; // 1 = vertex, 16 = fragment, 0x20 = compute, 0x80 = mesh
    public ulong Module;
    public byte* PName;
    public void* PSpecializationInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkComputePipelineCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public VkPipelineShaderStageCreateInfo Stage; // by value, not a pointer
    public ulong Layout;
    public ulong BasePipelineHandle;
    public int BasePipelineIndex;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkVertexInputBindingDescription
{
    public uint Binding;
    public uint Stride;
    public int InputRate;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkVertexInputAttributeDescription
{
    public uint Location;
    public uint Binding;
    public int Format;
    public uint Offset;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPipelineVertexInputStateCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public uint VertexBindingDescriptionCount;
    public VkVertexInputBindingDescription* PVertexBindingDescriptions;
    public uint VertexAttributeDescriptionCount;
    public VkVertexInputAttributeDescription* PVertexAttributeDescriptions;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPipelineInputAssemblyStateCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public int Topology;
    public uint PrimitiveRestartEnable;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPipelineViewportStateCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public uint ViewportCount;
    public void* PViewports;
    public uint ScissorCount;
    public void* PScissors;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPipelineRasterizationStateCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public uint DepthClampEnable;
    public uint RasterizerDiscardEnable;
    public int PolygonMode;
    public uint CullMode;
    public int FrontFace;
    public uint DepthBiasEnable;
    public float DepthBiasConstantFactor;
    public float DepthBiasClamp;
    public float DepthBiasSlopeFactor;
    public float LineWidth;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPipelineMultisampleStateCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public int RasterizationSamples;
    public uint SampleShadingEnable;
    public float MinSampleShading;
    public void* PSampleMask;
    public uint AlphaToCoverageEnable;
    public uint AlphaToOneEnable;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkStencilOpState
{
    public int FailOp;
    public int PassOp;
    public int DepthFailOp;
    public int CompareOp;
    public uint CompareMask;
    public uint WriteMask;
    public uint Reference;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPipelineDepthStencilStateCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public uint DepthTestEnable;
    public uint DepthWriteEnable;
    public int DepthCompareOp;
    public uint DepthBoundsTestEnable;
    public uint StencilTestEnable;
    public VkStencilOpState Front;
    public VkStencilOpState Back;
    public float MinDepthBounds;
    public float MaxDepthBounds;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkPipelineColorBlendAttachmentState
{
    public uint BlendEnable;
    public int SrcColorBlendFactor;
    public int DstColorBlendFactor;
    public int ColorBlendOp;
    public int SrcAlphaBlendFactor;
    public int DstAlphaBlendFactor;
    public int AlphaBlendOp;
    public uint ColorWriteMask;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPipelineColorBlendStateCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public uint LogicOpEnable;
    public int LogicOp;
    public uint AttachmentCount;
    public VkPipelineColorBlendAttachmentState* PAttachments;
    public float BlendConstant0;
    public float BlendConstant1;
    public float BlendConstant2;
    public float BlendConstant3;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPipelineDynamicStateCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public uint DynamicStateCount;
    public int* PDynamicStates;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPipelineRenderingCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint ViewMask;
    public uint ColorAttachmentCount;
    public VkFormat* PColorAttachmentFormats;
    public VkFormat DepthAttachmentFormat;
    public VkFormat StencilAttachmentFormat;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkGraphicsPipelineCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public uint StageCount;
    public VkPipelineShaderStageCreateInfo* PStages;
    public VkPipelineVertexInputStateCreateInfo* PVertexInputState;
    public VkPipelineInputAssemblyStateCreateInfo* PInputAssemblyState;
    public void* PTessellationState;
    public VkPipelineViewportStateCreateInfo* PViewportState;
    public VkPipelineRasterizationStateCreateInfo* PRasterizationState;
    public VkPipelineMultisampleStateCreateInfo* PMultisampleState;
    public VkPipelineDepthStencilStateCreateInfo* PDepthStencilState;
    public VkPipelineColorBlendStateCreateInfo* PColorBlendState;
    public VkPipelineDynamicStateCreateInfo* PDynamicState;
    public ulong Layout;
    public ulong RenderPass;
    public uint Subpass;
    public ulong BasePipelineHandle;
    public int BasePipelineIndex;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPipelineLayoutCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public uint SetLayoutCount;
    public ulong* PSetLayouts;
    public uint PushConstantRangeCount;
    public void* PPushConstantRanges;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkDescriptorSetLayoutBinding
{
    public uint Binding;
    public int DescriptorType;
    public uint DescriptorCount;
    public uint StageFlags;
    public IntPtr PImmutableSamplers;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkDescriptorSetLayoutCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public uint BindingCount;
    public VkDescriptorSetLayoutBinding* PBindings;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkDescriptorPoolSize
{
    public int Type;
    public uint DescriptorCount;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkDescriptorPoolCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public uint MaxSets;
    public uint PoolSizeCount;
    public VkDescriptorPoolSize* PPoolSizes;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkDescriptorSetAllocateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public ulong DescriptorPool;
    public uint DescriptorSetCount;
    public ulong* PSetLayouts;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkDescriptorBufferInfo
{
    public ulong Buffer;
    public ulong Offset;
    public ulong Range;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkDescriptorImageInfo
{
    public ulong Sampler;
    public ulong ImageView;
    public VkImageLayout ImageLayout;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkWriteDescriptorSet
{
    public VkStructureType SType;
    public void* PNext;
    public ulong DstSet;
    public uint DstBinding;
    public uint DstArrayElement;
    public uint DescriptorCount;
    public int DescriptorType;
    public VkDescriptorImageInfo* PImageInfo;
    public VkDescriptorBufferInfo* PBufferInfo;
    public void* PTexelBufferView;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkViewport
{
    public float X;
    public float Y;
    public float Width;
    public float Height;
    public float MinDepth;
    public float MaxDepth;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkOffset2D
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkRect2D
{
    public VkOffset2D Offset;
    public VkExtent2D Extent;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkRenderingAttachmentInfo
{
    public VkStructureType SType;
    public void* PNext;
    public ulong ImageView;
    public VkImageLayout ImageLayout;
    public int ResolveMode;
    public ulong ResolveImageView;
    public VkImageLayout ResolveImageLayout;
    public int LoadOp;  // 0 load, 1 clear, 2 dont care
    public int StoreOp; // 0 store, 1 dont care
    public VkClearColorValue ClearValue; // also reused for depth in .R
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkRenderingInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public VkRect2D RenderArea;
    public uint LayerCount;
    public uint ViewMask;
    public uint ColorAttachmentCount;
    public VkRenderingAttachmentInfo* PColorAttachments;
    public VkRenderingAttachmentInfo* PDepthAttachment;
    public VkRenderingAttachmentInfo* PStencilAttachment;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkImageCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public int ImageType; // 1 = 2D
    public VkFormat Format;
    public VkExtent2D Extent2D;
    public uint ExtentDepth;
    public uint MipLevels;
    public uint ArrayLayers;
    public int Samples; // 1, 2, 4, 8...
    public int Tiling;  // 0 optimal
    public uint Usage;
    public int SharingMode;
    public uint QueueFamilyIndexCount;
    public uint* PQueueFamilyIndices;
    public VkImageLayout InitialLayout;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkImageViewCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public ulong Image;
    public int ViewType; // 1 = 2D, 3 = cube
    public VkFormat Format;
    public uint ComponentsR;
    public uint ComponentsG;
    public uint ComponentsB;
    public uint ComponentsA;
    public VkImageSubresourceRange SubresourceRange;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkSamplerCreateInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public int MagFilter;  // 0 nearest, 1 linear
    public int MinFilter;
    public int MipmapMode; // 0 nearest, 1 linear
    public int AddressModeU; // 0 repeat, 2 clamp-to-edge
    public int AddressModeV;
    public int AddressModeW;
    public float MipLodBias;
    public uint AnisotropyEnable;
    public float MaxAnisotropy;
    public uint CompareEnable;
    public int CompareOp;
    public float MinLod;
    public float MaxLod;
    public int BorderColor;
    public uint UnnormalizedCoordinates;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkImageSubresourceLayers
{
    public uint AspectMask;
    public uint MipLevel;
    public uint BaseArrayLayer;
    public uint LayerCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkBufferImageCopy
{
    public ulong BufferOffset;
    public uint BufferRowLength;
    public uint BufferImageHeight;
    public VkImageSubresourceLayers ImageSubresource;
    public int ImageOffsetX;
    public int ImageOffsetY;
    public int ImageOffsetZ;
    public VkExtent2D ImageExtent;
    public uint ImageExtentDepth;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkBufferCopy
{
    public ulong SrcOffset;
    public ulong DstOffset;
    public ulong Size;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkClearAttachment
{
    public uint AspectMask;
    public uint ColorAttachment;
    public VkClearColorValue ClearValue; // depth uses .R
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkClearRect
{
    public VkRect2D Rect;
    public uint BaseArrayLayer;
    public uint LayerCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkOffset3D
{
    public int X;
    public int Y;
    public int Z;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkImageBlit
{
    public VkImageSubresourceLayers SrcSubresource;
    public VkOffset3D SrcOffset0;
    public VkOffset3D SrcOffset1;
    public VkImageSubresourceLayers DstSubresource;
    public VkOffset3D DstOffset0;
    public VkOffset3D DstOffset1;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkImageCopy
{
    public VkImageSubresourceLayers SrcSubresource;
    public VkOffset3D SrcOffset;
    public VkImageSubresourceLayers DstSubresource;
    public VkOffset3D DstOffset;
    public uint ExtentWidth;
    public uint ExtentHeight;
    public uint ExtentDepth;
}

/// <summary>
/// Vulkan command table. Instance-level pointers are resolved from
/// SDL_Vulkan_GetVkGetInstanceProcAddr; device-level pointers are
/// re-resolved per device for direct dispatch.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkDebugUtilsObjectNameInfoEXT
{
    public VkStructureType SType; // 1000128000
    public void* PNext;
    public int ObjectType; // VkObjectType
    public ulong ObjectHandle;
    public byte* PObjectName;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkDebugUtilsLabelEXT
{
    public VkStructureType SType; // 1000128002
    public void* PNext;
    public byte* PLabelName;
    public fixed float Color[4];
}

/// <summary>Vulkan 1.2 core feature toggles as a flat VkBool32 array (47
/// entries, spec order) - safer than 47 hand-counted named fields. Indexed
/// by the constants below.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPhysicalDeviceVulkan12Features
{
    public VkStructureType SType;
    public void* PNext;
    public fixed uint Features[47];

    public const int DescriptorIndexing = 9;
    public const int BufferDeviceAddress = 38;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPhysicalDeviceAccelerationStructureFeaturesKHR
{
    public VkStructureType SType;
    public void* PNext;
    public uint AccelerationStructure;
    public uint AccelerationStructureCaptureReplay;
    public uint AccelerationStructureIndirectBuild;
    public uint AccelerationStructureHostCommands;
    public uint DescriptorBindingAccelerationStructureUpdateAfterBind;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPhysicalDeviceRayQueryFeaturesKHR
{
    public VkStructureType SType;
    public void* PNext;
    public uint RayQuery;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPipelineFragmentShadingRateStateCreateInfoKHR
{
    public VkStructureType SType;
    public void* PNext;
    public uint FragmentSizeWidth;
    public uint FragmentSizeHeight;
    public int CombinerOp0; // KEEP
    public int CombinerOp1; // KEEP
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPhysicalDeviceFragmentShadingRateFeaturesKHR
{
    public VkStructureType SType;
    public void* PNext;
    public uint PipelineFragmentShadingRate;
    public uint PrimitiveFragmentShadingRate;
    public uint AttachmentFragmentShadingRate;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPhysicalDeviceFragmentShadingRateKHR
{
    public VkStructureType SType; // 1000226004
    public void* PNext;
    public uint SampleCounts;
    public uint FragmentSizeWidth;
    public uint FragmentSizeHeight;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPhysicalDeviceMeshShaderFeaturesEXT
{
    public VkStructureType SType;
    public void* PNext;
    public uint TaskShader;
    public uint MeshShader;
    public uint MultiviewMeshShader;
    public uint PrimitiveFragmentShadingRateMeshShader;
    public uint MeshShaderQueries;
}

// VK_EXT_mesh_shader properties: 32 uints + 4 bools, addressed by index
// (the asteroid pipeline only reads the output vertex/primitive caps).
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPhysicalDeviceMeshShaderPropertiesEXT
{
    public VkStructureType SType;
    public void* PNext;
    public fixed uint Values[36];

    public const int MaxMeshWorkGroupTotalCount = 11;
    public const int MaxMeshOutputVertices = 24;
    public const int MaxMeshOutputPrimitives = 25;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPhysicalDeviceAccelerationStructurePropertiesKHR
{
    public VkStructureType SType;
    public void* PNext;
    public ulong MaxGeometryCount;
    public ulong MaxInstanceCount;
    public ulong MaxPrimitiveCount;
    public uint MaxPerStageDescriptorAccelerationStructures;
    public uint MaxPerStageDescriptorUpdateAfterBindAccelerationStructures;
    public uint MaxDescriptorSetAccelerationStructures;
    public uint MaxDescriptorSetUpdateAfterBindAccelerationStructures;
    public uint MinAccelerationStructureScratchOffsetAlignment;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkPhysicalDeviceProperties2
{
    public VkStructureType SType;
    public void* PNext;
    public fixed byte Properties[824];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkExtensionProperties
{
    public fixed byte ExtensionName[256];
    public uint SpecVersion;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkMemoryAllocateFlagsInfo
{
    public VkStructureType SType;
    public void* PNext;
    public uint Flags; // 0x2 = DEVICE_ADDRESS_BIT
    public uint DeviceMask;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkBufferDeviceAddressInfo
{
    public VkStructureType SType;
    public void* PNext;
    public ulong Buffer;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkAccelerationStructureCreateInfoKHR
{
    public VkStructureType SType;
    public void* PNext;
    public uint CreateFlags;
    public ulong Buffer;
    public ulong Offset;
    public ulong Size;
    public int Type; // 0 = TOP_LEVEL, 1 = BOTTOM_LEVEL
    public ulong DeviceAddress;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkAccelerationStructureGeometryTrianglesDataKHR
{
    public VkStructureType SType;
    public void* PNext;
    public int VertexFormat; // 106 = R32G32B32_SFLOAT
    public ulong VertexData; // device address
    public ulong VertexStride;
    public uint MaxVertex;
    public int IndexType; // 0 = UINT16, 1 = UINT32
    public ulong IndexData;
    public ulong TransformData;
}

/// <summary>VkAccelerationStructureGeometryKHR with the triangles union
/// member. The union is 64 bytes (triangles is the largest member);
/// triangles fills it exactly.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkAccelerationStructureGeometryKHR_Triangles
{
    public VkStructureType SType; // AccelerationStructureGeometryKHR
    public void* PNext;
    public int GeometryType; // 0 = TRIANGLES
    public VkAccelerationStructureGeometryTrianglesDataKHR Triangles;
    public uint Flags; // 0x1 = OPAQUE
}

/// <summary>VkAccelerationStructureGeometryKHR with the instances union
/// member (32 bytes used, padded to the 64-byte union size).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkAccelerationStructureGeometryKHR_Instances
{
    public VkStructureType SType; // AccelerationStructureGeometryKHR
    public void* PNext;
    public int GeometryType; // 2 = INSTANCES
    // The union member starts 8-aligned (it contains pointers); C# packs
    // the next int right after GeometryType without this explicit pad.
    public uint GeometryTypePad;
    public VkStructureType InstSType; // AccelerationStructureGeometryInstancesDataKHR
    public void* InstPNext;
    public uint ArrayOfPointers;
    public ulong Data; // device address of VkAccelerationStructureInstanceKHR array
    public fixed byte UnionPad[32];
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkAccelerationStructureBuildGeometryInfoKHR
{
    public VkStructureType SType;
    public void* PNext;
    public int Type; // 0 = TOP_LEVEL, 1 = BOTTOM_LEVEL
    public uint Flags; // 0x1 PREFER_FAST_TRACE, 0x8 PREFER_FAST_BUILD (build-pref bits: 4/8)
    public int Mode; // 0 = BUILD
    public ulong SrcAccelerationStructure;
    public ulong DstAccelerationStructure;
    public uint GeometryCount;
    public void* PGeometries;
    public void** PpGeometries;
    public ulong ScratchData; // device address
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkAccelerationStructureBuildRangeInfoKHR
{
    public uint PrimitiveCount;
    public uint PrimitiveOffset;
    public uint FirstVertex;
    public uint TransformOffset;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkAccelerationStructureBuildSizesInfoKHR
{
    public VkStructureType SType;
    public void* PNext;
    public ulong AccelerationStructureSize;
    public ulong UpdateScratchSize;
    public ulong BuildScratchSize;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkAccelerationStructureDeviceAddressInfoKHR
{
    public VkStructureType SType;
    public void* PNext;
    public ulong AccelerationStructure;
}

/// <summary>TLAS instance record (64 bytes): 3x4 row-major transform for
/// column vectors - transpose System.Numerics row-vector matrices.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkAccelerationStructureInstanceKHR
{
    public fixed float Transform[12];
    public uint InstanceCustomIndexAndMask; // low 24: custom index, high 8: mask
    public uint InstanceShaderBindingTableRecordOffsetAndFlags;
    public ulong AccelerationStructureReference;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VkWriteDescriptorSetAccelerationStructureKHR
{
    public VkStructureType SType;
    public void* PNext;
    public uint AccelerationStructureCount;
    public ulong* PAccelerationStructures;
}

internal static unsafe class Vk
{
    private static delegate* unmanaged[Cdecl]<IntPtr, byte*, IntPtr> getInstanceProcAddr;

    public static delegate* unmanaged[Cdecl]<VkInstanceCreateInfo*, void*, IntPtr*, VkResult> CreateInstance;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint*, IntPtr*, VkResult> EnumeratePhysicalDevices;
    public static delegate* unmanaged[Cdecl]<IntPtr, void*, void> GetPhysicalDeviceProperties;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint*, void*, void> GetPhysicalDeviceQueueFamilyProperties;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint, ulong, uint*, VkResult> GetPhysicalDeviceSurfaceSupportKHR;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, VkSurfaceCapabilitiesKHR*, VkResult> GetPhysicalDeviceSurfaceCapabilitiesKHR;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, uint*, VkSurfaceFormatKHR*, VkResult> GetPhysicalDeviceSurfaceFormatsKHR;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkDeviceCreateInfo*, void*, IntPtr*, VkResult> CreateDevice;
    public static delegate* unmanaged[Cdecl]<IntPtr, byte*, IntPtr> GetDeviceProcAddr;

    // Device-level (resolved via GetDeviceProcAddr)
    public static delegate* unmanaged[Cdecl]<IntPtr, uint, uint, IntPtr*, void> GetDeviceQueue;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkSwapchainCreateInfoKHR*, void*, ulong*, VkResult> CreateSwapchainKHR;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, void*, void> DestroySwapchainKHR;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, uint*, ulong*, VkResult> GetSwapchainImagesKHR;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, ulong, ulong, ulong, uint*, VkResult> AcquireNextImageKHR;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkCommandPoolCreateInfo*, void*, ulong*, VkResult> CreateCommandPool;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkCommandBufferAllocateInfo*, IntPtr*, VkResult> AllocateCommandBuffers;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkCommandBufferBeginInfo*, VkResult> BeginCommandBuffer;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkResult> EndCommandBuffer;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint, VkResult> ResetCommandBuffer;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkSemaphoreCreateInfo*, void*, ulong*, VkResult> CreateSemaphore;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, void*, void> DestroySemaphore;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkFenceCreateInfo*, void*, ulong*, VkResult> CreateFence;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint, ulong*, uint, ulong, VkResult> WaitForFences;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint, ulong*, VkResult> ResetFences;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, VkResult> GetFenceStatus;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, uint, IntPtr*, void> FreeCommandBuffers;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkDependencyInfo*, void> CmdPipelineBarrier2;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkQueryPoolCreateInfo*, void*, ulong*, VkResult> CreateQueryPool;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, uint, uint, void> CmdResetQueryPool;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, ulong, uint, void> CmdWriteTimestamp2;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, uint, uint, nuint, void*, ulong, uint, VkResult> GetQueryPoolResults;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, VkImageLayout, VkClearColorValue*, uint, VkImageSubresourceRange*, void> CmdClearColorImage;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, VkImageLayout, VkClearDepthStencilValue*, uint, VkImageSubresourceRange*, void> CmdClearDepthStencilImage;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint, VkSubmitInfo2*, ulong, VkResult> QueueSubmit2;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkPresentInfoKHR*, VkResult> QueuePresentKHR;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkResult> DeviceWaitIdle;

    // Milestone 3.2+: resources, pipelines, dynamic rendering, draws
    public static delegate* unmanaged[Cdecl]<IntPtr, void*, void> GetPhysicalDeviceMemoryProperties;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkBufferCreateInfo*, void*, ulong*, VkResult> CreateBuffer;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, void*, void> DestroyBuffer;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, VkMemoryRequirements*, void> GetBufferMemoryRequirements;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, VkMemoryRequirements*, void> GetImageMemoryRequirements;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkMemoryAllocateInfo*, void*, ulong*, VkResult> AllocateMemory;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, void*, void> FreeMemory;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, ulong, ulong, VkResult> BindBufferMemory;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, ulong, ulong, VkResult> BindImageMemory;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, ulong, ulong, uint, void**, VkResult> MapMemory;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkShaderModuleCreateInfo*, void*, ulong*, VkResult> CreateShaderModule;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkDescriptorSetLayoutCreateInfo*, void*, ulong*, VkResult> CreateDescriptorSetLayout;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkPipelineLayoutCreateInfo*, void*, ulong*, VkResult> CreatePipelineLayout;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, uint, VkGraphicsPipelineCreateInfo*, void*, ulong*, VkResult> CreateGraphicsPipelines;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, uint, VkComputePipelineCreateInfo*, void*, ulong*, VkResult> CreateComputePipelines;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkDescriptorPoolCreateInfo*, void*, ulong*, VkResult> CreateDescriptorPool;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, uint, VkResult> ResetDescriptorPool;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkDescriptorSetAllocateInfo*, ulong*, VkResult> AllocateDescriptorSets;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint, VkWriteDescriptorSet*, uint, void*, void> UpdateDescriptorSets;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkImageCreateInfo*, void*, ulong*, VkResult> CreateImage;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, void*, void> DestroyImage;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkImageViewCreateInfo*, void*, ulong*, VkResult> CreateImageView;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, void*, void> DestroyImageView;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkSamplerCreateInfo*, void*, ulong*, VkResult> CreateSampler;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkRenderingInfo*, void> CmdBeginRendering;
    public static delegate* unmanaged[Cdecl]<IntPtr, void> CmdEndRendering;
    public static delegate* unmanaged[Cdecl]<IntPtr, int, ulong, void> CmdBindPipeline;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint, uint, VkViewport*, void> CmdSetViewport;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint, uint, VkRect2D*, void> CmdSetScissor;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint, uint, ulong*, ulong*, void> CmdBindVertexBuffers;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, ulong, int, void> CmdBindIndexBuffer;
    public static delegate* unmanaged[Cdecl]<IntPtr, int, ulong, uint, uint, ulong*, uint, uint*, void> CmdBindDescriptorSets;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint, uint, uint, uint, void> CmdDraw;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint, uint, uint, int, uint, void> CmdDrawIndexed;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, ulong, uint, VkBufferCopy*, void> CmdCopyBuffer;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, ulong, VkImageLayout, uint, VkBufferImageCopy*, void> CmdCopyBufferToImage;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, VkImageLayout, ulong, VkImageLayout, uint, VkImageBlit*, int, void> CmdBlitImage;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, VkImageLayout, ulong, VkImageLayout, uint, VkImageCopy*, void> CmdCopyImage;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint, uint, uint, void> CmdDispatch;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, VkImageLayout, ulong, uint, VkBufferImageCopy*, void> CmdCopyImageToBuffer;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint, VkClearAttachment*, uint, VkClearRect*, void> CmdClearAttachments;

    // Instance-level probing entry points (loaded in LoadInstance)
    public static delegate* unmanaged[Cdecl]<IntPtr, byte*, uint*, VkExtensionProperties*, VkResult> EnumerateDeviceExtensionProperties;
    public static delegate* unmanaged[Cdecl]<IntPtr, void*, void> GetPhysicalDeviceFeatures2;
    public static delegate* unmanaged[Cdecl]<IntPtr, void*, void> GetPhysicalDeviceProperties2;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint*, VkPhysicalDeviceFragmentShadingRateKHR*, VkResult> GetPhysicalDeviceFragmentShadingRatesKHR;

    // VK_KHR_acceleration_structure / ray_query / buffer_device_address
    // (optional; loaded via LoadDeviceRT)
    public static delegate* unmanaged[Cdecl]<IntPtr, VkAccelerationStructureCreateInfoKHR*, void*, ulong*, VkResult> CreateAccelerationStructureKHR;
    public static delegate* unmanaged[Cdecl]<IntPtr, ulong, void*, void> DestroyAccelerationStructureKHR;
    public static delegate* unmanaged[Cdecl]<IntPtr, int, VkAccelerationStructureBuildGeometryInfoKHR*, uint*, VkAccelerationStructureBuildSizesInfoKHR*, void> GetAccelerationStructureBuildSizesKHR;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkAccelerationStructureDeviceAddressInfoKHR*, ulong> GetAccelerationStructureDeviceAddressKHR;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint, VkAccelerationStructureBuildGeometryInfoKHR*, VkAccelerationStructureBuildRangeInfoKHR**, void> CmdBuildAccelerationStructuresKHR;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkBufferDeviceAddressInfo*, ulong> GetBufferDeviceAddress;

    /// <summary>Device-level entry point that may be absent: IntPtr.Zero
    /// instead of throwing.</summary>
    public static IntPtr DeviceOptional(IntPtr device, string name)
    {
        Span<byte> utf8 = stackalloc byte[name.Length + 1];
        for (var i = 0; i < name.Length; i++)
        {
            utf8[i] = (byte)name[i];
        }
        utf8[name.Length] = 0;
        fixed (byte* p = utf8)
        {
            return GetDeviceProcAddr(device, p);
        }
    }

    /// <summary>Loads ray-query path entry points; returns false when any
    /// are missing (extensions not enabled).</summary>
    public static bool LoadDeviceRT(IntPtr device)
    {
        CreateAccelerationStructureKHR = (delegate* unmanaged[Cdecl]<IntPtr, VkAccelerationStructureCreateInfoKHR*, void*, ulong*, VkResult>)
            DeviceOptional(device, "vkCreateAccelerationStructureKHR");
        DestroyAccelerationStructureKHR = (delegate* unmanaged[Cdecl]<IntPtr, ulong, void*, void>)
            DeviceOptional(device, "vkDestroyAccelerationStructureKHR");
        GetAccelerationStructureBuildSizesKHR = (delegate* unmanaged[Cdecl]<IntPtr, int, VkAccelerationStructureBuildGeometryInfoKHR*, uint*, VkAccelerationStructureBuildSizesInfoKHR*, void>)
            DeviceOptional(device, "vkGetAccelerationStructureBuildSizesKHR");
        GetAccelerationStructureDeviceAddressKHR = (delegate* unmanaged[Cdecl]<IntPtr, VkAccelerationStructureDeviceAddressInfoKHR*, ulong>)
            DeviceOptional(device, "vkGetAccelerationStructureDeviceAddressKHR");
        CmdBuildAccelerationStructuresKHR = (delegate* unmanaged[Cdecl]<IntPtr, uint, VkAccelerationStructureBuildGeometryInfoKHR*, VkAccelerationStructureBuildRangeInfoKHR**, void>)
            DeviceOptional(device, "vkCmdBuildAccelerationStructuresKHR");
        GetBufferDeviceAddress = (delegate* unmanaged[Cdecl]<IntPtr, VkBufferDeviceAddressInfo*, ulong>)
            DeviceOptional(device, "vkGetBufferDeviceAddress");
        return CreateAccelerationStructureKHR != null &&
               DestroyAccelerationStructureKHR != null &&
               GetAccelerationStructureBuildSizesKHR != null &&
               GetAccelerationStructureDeviceAddressKHR != null &&
               CmdBuildAccelerationStructuresKHR != null &&
               GetBufferDeviceAddress != null;
    }

    // VK_KHR_fragment_shading_rate (optional; loaded via LoadDeviceVrs)
    public static delegate* unmanaged[Cdecl]<IntPtr, VkExtent2D*, int*, void> CmdSetFragmentShadingRateKHR;

    /// <summary>Loads the per-draw shading rate entry point.</summary>
    public static bool LoadDeviceVrs(IntPtr device)
    {
        CmdSetFragmentShadingRateKHR = (delegate* unmanaged[Cdecl]<IntPtr, VkExtent2D*, int*, void>)
            DeviceOptional(device, "vkCmdSetFragmentShadingRateKHR");
        return CmdSetFragmentShadingRateKHR != null;
    }

    // VK_EXT_mesh_shader (optional; loaded via LoadDeviceMesh)
    public static delegate* unmanaged[Cdecl]<IntPtr, uint, uint, uint, void> CmdDrawMeshTasksEXT;

    /// <summary>Loads the mesh shader draw entry point; false when the
    /// extension was not enabled on the device.</summary>
    public static bool LoadDeviceMesh(IntPtr device)
    {
        CmdDrawMeshTasksEXT = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint, uint, void>)
            DeviceOptional(device, "vkCmdDrawMeshTasksEXT");
        return CmdDrawMeshTasksEXT != null;
    }

    // VK_EXT_debug_utils (optional; loaded via LoadDebugUtils)
    public static delegate* unmanaged[Cdecl]<IntPtr, VkDebugUtilsObjectNameInfoEXT*, VkResult> SetDebugUtilsObjectNameEXT;
    public static delegate* unmanaged[Cdecl]<IntPtr, VkDebugUtilsLabelEXT*, void> CmdBeginDebugUtilsLabelEXT;
    public static delegate* unmanaged[Cdecl]<IntPtr, void> CmdEndDebugUtilsLabelEXT;

    /// <summary>Loads VK_EXT_debug_utils entry points (instance extension);
    /// returns false when the extension wasn't enabled.</summary>
    public static bool LoadDebugUtils(IntPtr instance)
    {
        SetDebugUtilsObjectNameEXT = (delegate* unmanaged[Cdecl]<IntPtr, VkDebugUtilsObjectNameInfoEXT*, VkResult>)
            InstanceOptional(instance, "vkSetDebugUtilsObjectNameEXT");
        CmdBeginDebugUtilsLabelEXT = (delegate* unmanaged[Cdecl]<IntPtr, VkDebugUtilsLabelEXT*, void>)
            InstanceOptional(instance, "vkCmdBeginDebugUtilsLabelEXT");
        CmdEndDebugUtilsLabelEXT = (delegate* unmanaged[Cdecl]<IntPtr, void>)
            InstanceOptional(instance, "vkCmdEndDebugUtilsLabelEXT");
        return SetDebugUtilsObjectNameEXT != null &&
               CmdBeginDebugUtilsLabelEXT != null &&
               CmdEndDebugUtilsLabelEXT != null;
    }

    public static void LoadGlobal(IntPtr vkGetInstanceProcAddr)
    {
        getInstanceProcAddr = (delegate* unmanaged[Cdecl]<IntPtr, byte*, IntPtr>)vkGetInstanceProcAddr;
        CreateInstance = (delegate* unmanaged[Cdecl]<VkInstanceCreateInfo*, void*, IntPtr*, VkResult>)
            Instance(IntPtr.Zero, "vkCreateInstance");
    }

    public static void LoadInstance(IntPtr instance)
    {
        EnumeratePhysicalDevices = (delegate* unmanaged[Cdecl]<IntPtr, uint*, IntPtr*, VkResult>)
            Instance(instance, "vkEnumeratePhysicalDevices");
        GetPhysicalDeviceProperties = (delegate* unmanaged[Cdecl]<IntPtr, void*, void>)
            Instance(instance, "vkGetPhysicalDeviceProperties");
        GetPhysicalDeviceQueueFamilyProperties = (delegate* unmanaged[Cdecl]<IntPtr, uint*, void*, void>)
            Instance(instance, "vkGetPhysicalDeviceQueueFamilyProperties");
        GetPhysicalDeviceSurfaceSupportKHR = (delegate* unmanaged[Cdecl]<IntPtr, uint, ulong, uint*, VkResult>)
            Instance(instance, "vkGetPhysicalDeviceSurfaceSupportKHR");
        GetPhysicalDeviceSurfaceCapabilitiesKHR = (delegate* unmanaged[Cdecl]<IntPtr, ulong, VkSurfaceCapabilitiesKHR*, VkResult>)
            Instance(instance, "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
        GetPhysicalDeviceSurfaceFormatsKHR = (delegate* unmanaged[Cdecl]<IntPtr, ulong, uint*, VkSurfaceFormatKHR*, VkResult>)
            Instance(instance, "vkGetPhysicalDeviceSurfaceFormatsKHR");
        CreateDevice = (delegate* unmanaged[Cdecl]<IntPtr, VkDeviceCreateInfo*, void*, IntPtr*, VkResult>)
            Instance(instance, "vkCreateDevice");
        GetDeviceProcAddr = (delegate* unmanaged[Cdecl]<IntPtr, byte*, IntPtr>)
            Instance(instance, "vkGetDeviceProcAddr");
        GetPhysicalDeviceMemoryProperties = (delegate* unmanaged[Cdecl]<IntPtr, void*, void>)
            Instance(instance, "vkGetPhysicalDeviceMemoryProperties");
        EnumerateDeviceExtensionProperties = (delegate* unmanaged[Cdecl]<IntPtr, byte*, uint*, VkExtensionProperties*, VkResult>)
            Instance(instance, "vkEnumerateDeviceExtensionProperties");
        GetPhysicalDeviceFeatures2 = (delegate* unmanaged[Cdecl]<IntPtr, void*, void>)
            Instance(instance, "vkGetPhysicalDeviceFeatures2");
        GetPhysicalDeviceProperties2 = (delegate* unmanaged[Cdecl]<IntPtr, void*, void>)
            Instance(instance, "vkGetPhysicalDeviceProperties2");
        GetPhysicalDeviceFragmentShadingRatesKHR = (delegate* unmanaged[Cdecl]<IntPtr, uint*, VkPhysicalDeviceFragmentShadingRateKHR*, VkResult>)
            Instance(instance, "vkGetPhysicalDeviceFragmentShadingRatesKHR");
    }

    public static void LoadDevice(IntPtr device)
    {
        GetDeviceQueue = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint, IntPtr*, void>)Device(device, "vkGetDeviceQueue");
        CreateSwapchainKHR = (delegate* unmanaged[Cdecl]<IntPtr, VkSwapchainCreateInfoKHR*, void*, ulong*, VkResult>)Device(device, "vkCreateSwapchainKHR");
        DestroySwapchainKHR = (delegate* unmanaged[Cdecl]<IntPtr, ulong, void*, void>)Device(device, "vkDestroySwapchainKHR");
        GetSwapchainImagesKHR = (delegate* unmanaged[Cdecl]<IntPtr, ulong, uint*, ulong*, VkResult>)Device(device, "vkGetSwapchainImagesKHR");
        AcquireNextImageKHR = (delegate* unmanaged[Cdecl]<IntPtr, ulong, ulong, ulong, ulong, uint*, VkResult>)Device(device, "vkAcquireNextImageKHR");
        CreateCommandPool = (delegate* unmanaged[Cdecl]<IntPtr, VkCommandPoolCreateInfo*, void*, ulong*, VkResult>)Device(device, "vkCreateCommandPool");
        AllocateCommandBuffers = (delegate* unmanaged[Cdecl]<IntPtr, VkCommandBufferAllocateInfo*, IntPtr*, VkResult>)Device(device, "vkAllocateCommandBuffers");
        BeginCommandBuffer = (delegate* unmanaged[Cdecl]<IntPtr, VkCommandBufferBeginInfo*, VkResult>)Device(device, "vkBeginCommandBuffer");
        EndCommandBuffer = (delegate* unmanaged[Cdecl]<IntPtr, VkResult>)Device(device, "vkEndCommandBuffer");
        ResetCommandBuffer = (delegate* unmanaged[Cdecl]<IntPtr, uint, VkResult>)Device(device, "vkResetCommandBuffer");
        CreateSemaphore = (delegate* unmanaged[Cdecl]<IntPtr, VkSemaphoreCreateInfo*, void*, ulong*, VkResult>)Device(device, "vkCreateSemaphore");
        DestroySemaphore = (delegate* unmanaged[Cdecl]<IntPtr, ulong, void*, void>)Device(device, "vkDestroySemaphore");
        CreateFence = (delegate* unmanaged[Cdecl]<IntPtr, VkFenceCreateInfo*, void*, ulong*, VkResult>)Device(device, "vkCreateFence");
        WaitForFences = (delegate* unmanaged[Cdecl]<IntPtr, uint, ulong*, uint, ulong, VkResult>)Device(device, "vkWaitForFences");
        ResetFences = (delegate* unmanaged[Cdecl]<IntPtr, uint, ulong*, VkResult>)Device(device, "vkResetFences");
        GetFenceStatus = (delegate* unmanaged[Cdecl]<IntPtr, ulong, VkResult>)Device(device, "vkGetFenceStatus");
        FreeCommandBuffers = (delegate* unmanaged[Cdecl]<IntPtr, ulong, uint, IntPtr*, void>)Device(device, "vkFreeCommandBuffers");
        CmdPipelineBarrier2 = (delegate* unmanaged[Cdecl]<IntPtr, VkDependencyInfo*, void>)Device(device, "vkCmdPipelineBarrier2");
        CmdClearColorImage = (delegate* unmanaged[Cdecl]<IntPtr, ulong, VkImageLayout, VkClearColorValue*, uint, VkImageSubresourceRange*, void>)Device(device, "vkCmdClearColorImage");
        CmdClearDepthStencilImage = (delegate* unmanaged[Cdecl]<IntPtr, ulong, VkImageLayout, VkClearDepthStencilValue*, uint, VkImageSubresourceRange*, void>)Device(device, "vkCmdClearDepthStencilImage");
        QueueSubmit2 = (delegate* unmanaged[Cdecl]<IntPtr, uint, VkSubmitInfo2*, ulong, VkResult>)Device(device, "vkQueueSubmit2");
        QueuePresentKHR = (delegate* unmanaged[Cdecl]<IntPtr, VkPresentInfoKHR*, VkResult>)Device(device, "vkQueuePresentKHR");
        DeviceWaitIdle = (delegate* unmanaged[Cdecl]<IntPtr, VkResult>)Device(device, "vkDeviceWaitIdle");
        CreateBuffer = (delegate* unmanaged[Cdecl]<IntPtr, VkBufferCreateInfo*, void*, ulong*, VkResult>)Device(device, "vkCreateBuffer");
        DestroyBuffer = (delegate* unmanaged[Cdecl]<IntPtr, ulong, void*, void>)Device(device, "vkDestroyBuffer");
        GetBufferMemoryRequirements = (delegate* unmanaged[Cdecl]<IntPtr, ulong, VkMemoryRequirements*, void>)Device(device, "vkGetBufferMemoryRequirements");
        GetImageMemoryRequirements = (delegate* unmanaged[Cdecl]<IntPtr, ulong, VkMemoryRequirements*, void>)Device(device, "vkGetImageMemoryRequirements");
        AllocateMemory = (delegate* unmanaged[Cdecl]<IntPtr, VkMemoryAllocateInfo*, void*, ulong*, VkResult>)Device(device, "vkAllocateMemory");
        FreeMemory = (delegate* unmanaged[Cdecl]<IntPtr, ulong, void*, void>)Device(device, "vkFreeMemory");
        BindBufferMemory = (delegate* unmanaged[Cdecl]<IntPtr, ulong, ulong, ulong, VkResult>)Device(device, "vkBindBufferMemory");
        BindImageMemory = (delegate* unmanaged[Cdecl]<IntPtr, ulong, ulong, ulong, VkResult>)Device(device, "vkBindImageMemory");
        MapMemory = (delegate* unmanaged[Cdecl]<IntPtr, ulong, ulong, ulong, uint, void**, VkResult>)Device(device, "vkMapMemory");
        CreateShaderModule = (delegate* unmanaged[Cdecl]<IntPtr, VkShaderModuleCreateInfo*, void*, ulong*, VkResult>)Device(device, "vkCreateShaderModule");
        CreateDescriptorSetLayout = (delegate* unmanaged[Cdecl]<IntPtr, VkDescriptorSetLayoutCreateInfo*, void*, ulong*, VkResult>)Device(device, "vkCreateDescriptorSetLayout");
        CreatePipelineLayout = (delegate* unmanaged[Cdecl]<IntPtr, VkPipelineLayoutCreateInfo*, void*, ulong*, VkResult>)Device(device, "vkCreatePipelineLayout");
        CreateGraphicsPipelines = (delegate* unmanaged[Cdecl]<IntPtr, ulong, uint, VkGraphicsPipelineCreateInfo*, void*, ulong*, VkResult>)Device(device, "vkCreateGraphicsPipelines");
        CreateComputePipelines = (delegate* unmanaged[Cdecl]<IntPtr, ulong, uint, VkComputePipelineCreateInfo*, void*, ulong*, VkResult>)Device(device, "vkCreateComputePipelines");
        CreateDescriptorPool = (delegate* unmanaged[Cdecl]<IntPtr, VkDescriptorPoolCreateInfo*, void*, ulong*, VkResult>)Device(device, "vkCreateDescriptorPool");
        ResetDescriptorPool = (delegate* unmanaged[Cdecl]<IntPtr, ulong, uint, VkResult>)Device(device, "vkResetDescriptorPool");
        AllocateDescriptorSets = (delegate* unmanaged[Cdecl]<IntPtr, VkDescriptorSetAllocateInfo*, ulong*, VkResult>)Device(device, "vkAllocateDescriptorSets");
        UpdateDescriptorSets = (delegate* unmanaged[Cdecl]<IntPtr, uint, VkWriteDescriptorSet*, uint, void*, void>)Device(device, "vkUpdateDescriptorSets");
        CreateImage = (delegate* unmanaged[Cdecl]<IntPtr, VkImageCreateInfo*, void*, ulong*, VkResult>)Device(device, "vkCreateImage");
        DestroyImage = (delegate* unmanaged[Cdecl]<IntPtr, ulong, void*, void>)Device(device, "vkDestroyImage");
        CreateImageView = (delegate* unmanaged[Cdecl]<IntPtr, VkImageViewCreateInfo*, void*, ulong*, VkResult>)Device(device, "vkCreateImageView");
        DestroyImageView = (delegate* unmanaged[Cdecl]<IntPtr, ulong, void*, void>)Device(device, "vkDestroyImageView");
        CreateSampler = (delegate* unmanaged[Cdecl]<IntPtr, VkSamplerCreateInfo*, void*, ulong*, VkResult>)Device(device, "vkCreateSampler");
        CmdBeginRendering = (delegate* unmanaged[Cdecl]<IntPtr, VkRenderingInfo*, void>)Device(device, "vkCmdBeginRendering");
        CmdEndRendering = (delegate* unmanaged[Cdecl]<IntPtr, void>)Device(device, "vkCmdEndRendering");
        CmdBindPipeline = (delegate* unmanaged[Cdecl]<IntPtr, int, ulong, void>)Device(device, "vkCmdBindPipeline");
        CmdSetViewport = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint, VkViewport*, void>)Device(device, "vkCmdSetViewport");
        CmdSetScissor = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint, VkRect2D*, void>)Device(device, "vkCmdSetScissor");
        CmdBindVertexBuffers = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint, ulong*, ulong*, void>)Device(device, "vkCmdBindVertexBuffers");
        CmdBindIndexBuffer = (delegate* unmanaged[Cdecl]<IntPtr, ulong, ulong, int, void>)Device(device, "vkCmdBindIndexBuffer");
        CmdBindDescriptorSets = (delegate* unmanaged[Cdecl]<IntPtr, int, ulong, uint, uint, ulong*, uint, uint*, void>)Device(device, "vkCmdBindDescriptorSets");
        CmdDraw = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint, uint, uint, void>)Device(device, "vkCmdDraw");
        CmdDrawIndexed = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint, uint, int, uint, void>)Device(device, "vkCmdDrawIndexed");
        CmdCopyBuffer = (delegate* unmanaged[Cdecl]<IntPtr, ulong, ulong, uint, VkBufferCopy*, void>)Device(device, "vkCmdCopyBuffer");
        CmdCopyBufferToImage = (delegate* unmanaged[Cdecl]<IntPtr, ulong, ulong, VkImageLayout, uint, VkBufferImageCopy*, void>)Device(device, "vkCmdCopyBufferToImage");
        CmdBlitImage = (delegate* unmanaged[Cdecl]<IntPtr, ulong, VkImageLayout, ulong, VkImageLayout, uint, VkImageBlit*, int, void>)Device(device, "vkCmdBlitImage");
        CmdCopyImage = (delegate* unmanaged[Cdecl]<IntPtr, ulong, VkImageLayout, ulong, VkImageLayout, uint, VkImageCopy*, void>)Device(device, "vkCmdCopyImage");
        CmdDispatch = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint, uint, void>)Device(device, "vkCmdDispatch");
        CmdCopyImageToBuffer = (delegate* unmanaged[Cdecl]<IntPtr, ulong, VkImageLayout, ulong, uint, VkBufferImageCopy*, void>)Device(device, "vkCmdCopyImageToBuffer");
        CmdClearAttachments = (delegate* unmanaged[Cdecl]<IntPtr, uint, VkClearAttachment*, uint, VkClearRect*, void>)Device(device, "vkCmdClearAttachments");
        CreateQueryPool = (delegate* unmanaged[Cdecl]<IntPtr, VkQueryPoolCreateInfo*, void*, ulong*, VkResult>)Device(device, "vkCreateQueryPool");
        CmdResetQueryPool = (delegate* unmanaged[Cdecl]<IntPtr, ulong, uint, uint, void>)Device(device, "vkCmdResetQueryPool");
        CmdWriteTimestamp2 = (delegate* unmanaged[Cdecl]<IntPtr, ulong, ulong, uint, void>)Device(device, "vkCmdWriteTimestamp2");
        GetQueryPoolResults = (delegate* unmanaged[Cdecl]<IntPtr, ulong, uint, uint, nuint, void*, ulong, uint, VkResult>)Device(device, "vkGetQueryPoolResults");
    }

    private static IntPtr Instance(IntPtr instance, string name)
    {
        Span<byte> utf8 = stackalloc byte[name.Length + 1];
        for (var i = 0; i < name.Length; i++)
        {
            utf8[i] = (byte)name[i];
        }
        utf8[name.Length] = 0;
        fixed (byte* p = utf8)
        {
            var fn = getInstanceProcAddr(instance, p);
            if (fn == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Vulkan entry point '{name}' not found");
            }
            return fn;
        }
    }

    private static IntPtr Device(IntPtr device, string name)
    {
        Span<byte> utf8 = stackalloc byte[name.Length + 1];
        for (var i = 0; i < name.Length; i++)
        {
            utf8[i] = (byte)name[i];
        }
        utf8[name.Length] = 0;
        fixed (byte* p = utf8)
        {
            var fn = GetDeviceProcAddr(device, p);
            if (fn == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Vulkan device entry point '{name}' not found");
            }
            return fn;
        }
    }

    /// <summary>Instance-level entry point that may be absent (extension
    /// not enabled): returns IntPtr.Zero instead of throwing.</summary>
    public static IntPtr InstanceOptional(IntPtr instance, string name)
    {
        Span<byte> utf8 = stackalloc byte[name.Length + 1];
        for (var i = 0; i < name.Length; i++)
        {
            utf8[i] = (byte)name[i];
        }
        utf8[name.Length] = 0;
        fixed (byte* p = utf8)
        {
            return getInstanceProcAddr(instance, p);
        }
    }

    public static void Check(VkResult result, string operation)
    {
        if (result != VkResult.Success)
        {
            throw new InvalidOperationException($"{operation} failed: {result}");
        }
    }
}
