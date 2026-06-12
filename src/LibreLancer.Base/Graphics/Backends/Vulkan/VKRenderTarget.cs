using System;

namespace LibreLancer.Graphics.Backends.Vulkan;

internal unsafe class VKDepthBuffer : IDepthBuffer
{
    public ulong Image;
    public ulong View;
    public ulong DeviceMemory;
    public ulong PoolOffset;
    public ulong PoolSize;
    private readonly VKRenderContext context;

    public VKDepthBuffer(VKRenderContext context, int width, int height)
    {
        this.context = context;
        var imageInfo = new VkImageCreateInfo
        {
            SType = VkStructureType.ImageCreateInfo,
            ImageType = 1,
            Format = (VkFormat)126, // D32_SFLOAT
            Extent2D = new VkExtent2D { Width = (uint)width, Height = (uint)height },
            ExtentDepth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = 1,
            // DEPTH_STENCIL_ATTACHMENT | TRANSFER_DST (initial clear)
            // | TRANSFER_SRC (phase 5 depth copy for volumetric composite)
            Usage = 0x23,
            InitialLayout = VkImageLayout.Undefined
        };
        ulong image;
        Vk.Check(Vk.CreateImage(context.Device, &imageInfo, null, &image), "vkCreateImage(depth)");
        Image = image;

        (DeviceMemory, PoolOffset, PoolSize) = context.Memory.AllocateImage(Image);

        var viewInfo = new VkImageViewCreateInfo
        {
            SType = VkStructureType.ImageViewCreateInfo,
            Image = Image,
            ViewType = 1,
            Format = (VkFormat)126,
            SubresourceRange = new VkImageSubresourceRange { AspectMask = 2, LevelCount = 1, LayerCount = 1 }
        };
        ulong view;
        Vk.Check(Vk.CreateImageView(context.Device, &viewInfo, null, &view), "vkCreateImageView(depth)");
        View = view;

        // Pool memory is recycled without scrubbing; clear the fresh image
        // so the first frames never depth-test against stale garbage.
        var cmd = context.BeginOneShotCommands();
        context.ImageBarrier(cmd, Image, VkImageLayout.Undefined,
            VkImageLayout.TransferDstOptimal, 0, 0, aspect: 2);
        var depthClear = new VkClearDepthStencilValue { Depth = 1f, Stencil = 0 };
        var depthRange = new VkImageSubresourceRange { AspectMask = 2, LevelCount = 1, LayerCount = 1 };
        Vk.CmdClearDepthStencilImage(cmd, Image, VkImageLayout.TransferDstOptimal, &depthClear, 1, &depthRange);
        context.ImageBarrier(cmd, Image, VkImageLayout.TransferDstOptimal,
            (VkImageLayout)3 /*DEPTH_STENCIL_ATTACHMENT_OPTIMAL*/, 0, 0, aspect: 2);
        context.EndOneShotCommands(cmd);
    }

    public void Dispose() => context.DeferDestroyImage(Image, View, DeviceMemory, PoolOffset, PoolSize);
}

/// <summary>
/// Offscreen colour+depth target. Rendering routes through the context's
/// frame state machine; blits use vkCmdBlitImage with explicit layout
/// round-trips.
/// </summary>
internal unsafe class VKRenderTarget2D : IRenderTarget2D
{
    private readonly VKRenderContext context;
    public readonly VKTexture2D Texture;
    public readonly VKDepthBuffer Depth;
    public int Width { get; }
    public int Height { get; }

    // Tracks which layout the colour image was left in by the FSM.
    // VKTexture2D's constructor leaves every subresource SHADER_READ_ONLY.
    public VkImageLayout ColorLayout = (VkImageLayout)5;

    public VKRenderTarget2D(VKRenderContext context, ITexture2D texture, IDepthBuffer depth)
    {
        this.context = context;
        Texture = (VKTexture2D)texture;
        Depth = (VKDepthBuffer)depth;
        Width = Texture.Width;
        Height = Texture.Height;
    }

    public void BlitToScreen() => context.BlitTargetToCurrent(this, Point.Zero);

    public void BlitToScreen(Point offset) => context.BlitTargetToCurrent(this, offset);

    public void BlitToBuffer(RenderTarget2D other, Point offset) =>
        context.BlitTargetToTarget(this, (VKRenderTarget2D)other.Backing, offset);

    public void Dispose()
    {
        // Texture and depth lifetimes belong to the engine-side wrapper.
    }
}
