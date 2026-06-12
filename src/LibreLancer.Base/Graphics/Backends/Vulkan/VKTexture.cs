using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LibreLancer.Graphics.Backends.Vulkan;

internal static class VKFormats
{
    public static int ToVk(SurfaceFormat format) => format switch
    {
        SurfaceFormat.Bgra8 => 44,        // B8G8R8A8_UNORM
        SurfaceFormat.R8 => 9,            // R8_UNORM
        // BC1_RGBA, not BC1_RGB: DXT1 carries 1-bit punch-through alpha and
        // GL loads it as COMPRESSED_RGBA_S3TC_DXT1. The RGB variant forced
        // alpha to 1.0, turning every DXT1-alpha texture opaque on Vulkan
        // (hard dark smoke quads swallowing the lava-planet menu fires).
        SurfaceFormat.Dxt1 => 133,        // BC1_RGBA_UNORM_BLOCK
        SurfaceFormat.Dxt3 => 135,        // BC2_UNORM_BLOCK
        SurfaceFormat.Dxt5 => 137,        // BC3_UNORM_BLOCK
        SurfaceFormat.Rgtc1 => 139,       // BC4_UNORM_BLOCK
        SurfaceFormat.Rgtc2 => 141,       // BC5_UNORM_BLOCK
        SurfaceFormat.HdrBlendable => 97, // R16G16B16A16_SFLOAT
        SurfaceFormat.HalfVector4 => 97,
        SurfaceFormat.Vector4 => 109,     // R32G32B32A32_SFLOAT
        SurfaceFormat.Depth => 126,       // D32_SFLOAT
        // Legacy 16-bit formats are expanded to BGRA8 on the CPU at upload
        // (spotty driver support; they triggered native crashes on NVIDIA).
        SurfaceFormat.Bgr565 => 44,
        SurfaceFormat.Bgra5551 => 44,
        SurfaceFormat.Bgra4444 => 44,
        _ => throw new NotSupportedException($"Vulkan backend: no VkFormat mapping for {format}")
    };

    public static bool NeedsExpansion(SurfaceFormat format) =>
        format is SurfaceFormat.Bgr565 or SurfaceFormat.Bgra5551 or SurfaceFormat.Bgra4444;

    public static byte[] ExpandToBgra8(SurfaceFormat format, ReadOnlySpan<byte> source)
    {
        var pixels = source.Length / 2;
        var result = new byte[pixels * 4];
        for (var i = 0; i < pixels; i++)
        {
            var value = (ushort)(source[i * 2] | (source[i * 2 + 1] << 8));
            byte r, g, b, a;
            switch (format)
            {
                case SurfaceFormat.Bgr565:
                    r = (byte)(((value >> 11) & 0x1F) * 255 / 31);
                    g = (byte)(((value >> 5) & 0x3F) * 255 / 63);
                    b = (byte)((value & 0x1F) * 255 / 31);
                    a = 255;
                    break;
                case SurfaceFormat.Bgra5551:
                    a = (byte)((value & 0x8000) != 0 ? 255 : 0);
                    r = (byte)(((value >> 10) & 0x1F) * 255 / 31);
                    g = (byte)(((value >> 5) & 0x1F) * 255 / 31);
                    b = (byte)((value & 0x1F) * 255 / 31);
                    break;
                default: // Bgra4444
                    a = (byte)(((value >> 12) & 0xF) * 17);
                    r = (byte)(((value >> 8) & 0xF) * 17);
                    g = (byte)(((value >> 4) & 0xF) * 17);
                    b = (byte)((value & 0xF) * 17);
                    break;
            }
            result[i * 4 + 0] = b;
            result[i * 4 + 1] = g;
            result[i * 4 + 2] = r;
            result[i * 4 + 3] = a;
        }
        return result;
    }

    public static bool IsCompressed(SurfaceFormat format) =>
        format is SurfaceFormat.Dxt1 or SurfaceFormat.Dxt3 or SurfaceFormat.Dxt5
            or SurfaceFormat.Rgtc1 or SurfaceFormat.Rgtc2;
}

/// <summary>
/// Sampled 2D texture: optimal tiling, device-local, uploaded through a
/// transient staging buffer + one-shot command buffer.
/// </summary>
internal unsafe class VKTexture2D : ITexture2D
{
    protected readonly VKRenderContext context;
    public ulong Image;
    public ulong View;
    public ulong DeviceMemory;
    public ulong PoolOffset;
    public ulong PoolSize;
    public SurfaceFormat Format { get; }
    public int Width { get; }
    public int Height { get; }
    public int LevelCount { get; }
    public bool IsDisposed { get; private set; }

    // GL caps sampling at the highest UPLOADED level via
    // GL_TEXTURE_MAX_LEVEL (content files often ship fewer mips than the
    // full chain). Mirror that: the view only spans uploaded levels and is
    // rebuilt when a deeper level arrives.
    private int maxUploadedLevel;
    private int viewLevels;
    private readonly bool isCube;
    private readonly int layerCount;
    private readonly int texDepth = 1;
    private readonly bool isStorage;
    public bool Dxt1 => Format == SurfaceFormat.Dxt1;
    public int EstimatedTextureMemory => Width * Height * texDepth * 4;

    /// <summary>Layout descriptors must declare for sampling this image.
    /// Storage images live in GENERAL permanently (written by compute,
    /// sampled by anything - no per-pass layout tracking needed).</summary>
    public VkImageLayout DescriptorLayout => isStorage ? (VkImageLayout)1 : (VkImageLayout)5;

    public VKTexture2D(VKRenderContext context, int width, int height, bool hasMipMaps, SurfaceFormat format,
        bool cube = false, int layers = 1, int depth3d = 1, bool storage = false)
    {
        this.context = context;
        Width = width;
        Height = height;
        Format = format;
        texDepth = depth3d;
        isStorage = storage;
        // 3D textures: single level (froxel grids/noise volumes don't mip).
        LevelCount = hasMipMaps && depth3d == 1 ? 1 + (int)Math.Floor(Math.Log2(Math.Max(width, height))) : 1;

        var imageInfo = new VkImageCreateInfo
        {
            SType = VkStructureType.ImageCreateInfo,
            Flags = cube ? 0x10u : 0u, // CUBE_COMPATIBLE
            ImageType = depth3d > 1 ? 2 : 1,
            Format = (VkFormat)VKFormats.ToVk(format),
            Extent2D = new VkExtent2D { Width = (uint)width, Height = (uint)height },
            ExtentDepth = (uint)depth3d,
            MipLevels = (uint)LevelCount,
            ArrayLayers = (uint)layers,
            Samples = 1,
            Tiling = 0,
            // Content textures: sampled + upload target. Attachment usage is
            // added by the render-target milestone for renderable formats
            // only - legacy 16-bit formats can't be attachments on NVIDIA.
            Usage = 0x4 | 0x2, // SAMPLED | TRANSFER_DST
            InitialLayout = VkImageLayout.Undefined
        };
        if (storage)
        {
            imageInfo.Usage |= 0x8; // STORAGE (compute UAV)
        }
        if (format == SurfaceFormat.Depth)
        {
            // SAMPLED | DEPTH_STENCIL_ATTACHMENT | TRANSFER_DST
            // (phase 5: receives scene-depth copies for volumetrics)
            imageInfo.Usage = 0x4 | 0x20 | 0x2;
        }
        else if (depth3d == 1 && format is SurfaceFormat.Bgra8 or SurfaceFormat.HdrBlendable)
        {
            imageInfo.Usage |= 0x10; // COLOR_ATTACHMENT for renderable formats
        }

        ulong image;
        Vk.Check(Vk.CreateImage(context.Device, &imageInfo, null, &image), "vkCreateImage");
        Image = image;
        // VK_OBJECT_TYPE_IMAGE = 10
        context.SetObjectName(10, image, depth3d > 1
            ? $"Tex3D {width}x{height}x{depth3d} {format}{(storage ? " storage" : "")}"
            : $"{(cube ? "TexCube" : "Tex2D")} {width}x{height} {format}");

        (DeviceMemory, PoolOffset, PoolSize) = context.Memory.AllocateImage(Image);

        isCube = cube;
        layerCount = layers;
        viewLevels = 1;
        var viewInfo = new VkImageViewCreateInfo
        {
            SType = VkStructureType.ImageViewCreateInfo,
            Image = Image,
            ViewType = cube ? 3 : depth3d > 1 ? 2 : 1, // 2 = VIEW_TYPE_3D
            Format = imageInfo.Format,
            SubresourceRange = new VkImageSubresourceRange
            {
                AspectMask = format == SurfaceFormat.Depth ? 2u : 1u,
                LevelCount = format == SurfaceFormat.Depth ? (uint)LevelCount : 1u,
                LayerCount = (uint)layers
            }
        };
        ulong view;
        Vk.Check(Vk.CreateImageView(context.Device, &viewInfo, null, &view), "vkCreateImageView");
        View = view;

        if (format != SurfaceFormat.Depth)
        {
            // Descriptors cover EVERY subresource: untouched mip levels must
            // still be in SHADER_READ_ONLY or sampling is undefined
            // behaviour (and a real device-loss on NVIDIA).
            var cmd = context.BeginOneShotCommands();
            context.ImageBarrierAll(cmd, Image, VkImageLayout.Undefined,
                DescriptorLayout, (uint)LevelCount, (uint)layers);
            context.EndOneShotCommands(cmd);
        }
        else
        {
            // Sampled-depth textures (scene-depth copies) rest in
            // SHADER_READ_ONLY between CopyDepthToTexture round-trips.
            var cmd = context.BeginOneShotCommands();
            context.ImageBarrier(cmd, Image, VkImageLayout.Undefined,
                (VkImageLayout)5, 0, 0, aspect: 2);
            context.EndOneShotCommands(cmd);
        }
    }

    protected void Upload<T>(int level, int layer, Rectangle? rect, ReadOnlySpan<T> data) where T : unmanaged
    {
        var bytes = MemoryMarshal.Cast<T, byte>(data);
        var levelWidth = Math.Max(1, Width >> level);
        var levelHeight = Math.Max(1, Height >> level);
        var x = rect?.X ?? 0;
        var y = rect?.Y ?? 0;
        var width = rect?.Width ?? levelWidth;
        var height = rect?.Height ?? levelHeight;

        if (width <= 0 || height <= 0 || bytes.IsEmpty)
        {
            return; // zero-area glyphs (spaces) produce empty copies
        }

        byte[]? expanded = null;
        if (VKFormats.NeedsExpansion(Format))
        {
            expanded = VKFormats.ExpandToBgra8(Format, bytes);
            bytes = expanded;
        }

        if (Environment.GetEnvironmentVariable("SIRIUS_UI_TRACE") == "1" &&
            level == 0 && Width is >= 16 and <= 64 && Format == SurfaceFormat.Bgra8)
        {
            long alphaSum = 0, rgbSum = 0;
            for (var i = 0; i + 3 < bytes.Length; i += 4)
            {
                rgbSum += bytes[i] + bytes[i + 1] + bytes[i + 2];
                alphaSum += bytes[i + 3];
            }
            FLLog.Info("VKUpload", $"#{GetHashCode():X} {Width}x{Height} len={bytes.Length} alphaSum={alphaSum} rgbSum={rgbSum}");
        }

        var staging = context.Memory.CreateHostBuffer((ulong)bytes.Length, VKMemory.UsageTransferSrc);
        bytes.CopyTo(staging.AsSpan());

        var copy = new VkBufferImageCopy
        {
            ImageSubresource = new VkImageSubresourceLayers
            {
                AspectMask = 1,
                MipLevel = (uint)level,
                BaseArrayLayer = (uint)layer,
                LayerCount = 1
            },
            ImageOffsetX = x,
            ImageOffsetY = y,
            ImageExtent = new VkExtent2D { Width = (uint)width, Height = (uint)height },
            ImageExtentDepth = (uint)texDepth
        };

        var cmd = context.BeginOneShotCommands();
        context.ImageBarrier(cmd, Image, DescriptorLayout, VkImageLayout.TransferDstOptimal,
            (uint)level, (uint)layer);
        Vk.CmdCopyBufferToImage(cmd, staging.Buffer, Image, VkImageLayout.TransferDstOptimal, 1, &copy);
        context.ImageBarrier(cmd, Image, VkImageLayout.TransferDstOptimal, DescriptorLayout,
            (uint)level, (uint)layer);
        context.EndOneShotCommands(cmd, staging); // staging freed when the GPU is done

        if (level > maxUploadedLevel)
        {
            maxUploadedLevel = level;
        }
        if (Format != SurfaceFormat.Depth && viewLevels != maxUploadedLevel + 1)
        {
            RebuildView();
        }
    }

    private void RebuildView()
    {
        viewLevels = maxUploadedLevel + 1;
        var viewInfo = new VkImageViewCreateInfo
        {
            SType = VkStructureType.ImageViewCreateInfo,
            Image = Image,
            ViewType = isCube ? 3 : 1,
            Format = (VkFormat)VKFormats.ToVk(Format),
            SubresourceRange = new VkImageSubresourceRange
            {
                AspectMask = 1u,
                LevelCount = (uint)viewLevels,
                LayerCount = (uint)layerCount
            }
        };
        ulong view;
        Vk.Check(Vk.CreateImageView(context.Device, &viewInfo, null, &view), "vkCreateImageView");
        var old = View;
        View = view;
        // image=0/memory=0 are no-ops in the deferred destroyer: only the view dies.
        context.DeferDestroyImage(0, old, 0, 0, 0);
    }

    public void SetData<T>(int level, Rectangle? rect, T[] data, int start, int count) where T : unmanaged =>
        Upload<T>(level, 0, rect, data.AsSpan(start, count));

    public void SetData(int level, Rectangle rect, IntPtr data)
    {
        var size = rect.Width * rect.Height * SurfaceFormatSize();
        Upload<byte>(level, 0, rect, new ReadOnlySpan<byte>((void*)data, size));
    }

    public void SetData<T>(T[] data) where T : unmanaged => Upload<T>(0, 0, null, data);

    private int SurfaceFormatSize() => Format switch
    {
        SurfaceFormat.R8 => 1,
        SurfaceFormat.HdrBlendable or SurfaceFormat.HalfVector4 => 8,
        SurfaceFormat.Vector4 => 16,
        _ => 4
    };

    public Task<byte[]> GetDataAsync() =>
        throw new NotImplementedException("Vulkan backend: texture readback arrives with the screenshot milestone");

    public void GetData<T>(int level, Rectangle? rect, T[] data, int start, int count) where T : struct =>
        throw new NotImplementedException("Vulkan backend: texture readback arrives with the screenshot milestone");

    public void GetData<T>(T[] data) where T : struct =>
        throw new NotImplementedException("Vulkan backend: texture readback arrives with the screenshot milestone");

    public virtual void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }
        IsDisposed = true;
        context.DeferDestroyImage(Image, View, DeviceMemory, PoolOffset, PoolSize);
    }
}

internal sealed unsafe class VKTextureCube : VKTexture2D, ITextureCube
{
    public int Size => Width;

    public VKTextureCube(VKRenderContext context, int size, bool mipMap, SurfaceFormat format)
        : base(context, size, size, mipMap, format, cube: true, layers: 6)
    {
    }

    public void SetData<T>(CubeMapFace face, int level, Rectangle? rect, T[] data, int start, int count)
        where T : unmanaged =>
        Upload<T>(level, (int)face, rect, data.AsSpan(start, count));

    public void SetData<T>(CubeMapFace face, T[] data) where T : unmanaged =>
        Upload<T>(0, (int)face, null, data);
}

/// <summary>
/// Sampled (optionally storage) 3D texture - froxel volumetrics, noise
/// volumes, LUTs. Single mip level; storage images live in GENERAL layout.
/// </summary>
internal sealed unsafe class VKTexture3D : VKTexture2D, ITexture3D
{
    public int Depth { get; }

    public VKTexture3D(VKRenderContext context, int width, int height, int depth, SurfaceFormat format,
        bool storage)
        : base(context, width, height, false, format, depth3d: depth, storage: storage)
    {
        Depth = depth;
    }
}
