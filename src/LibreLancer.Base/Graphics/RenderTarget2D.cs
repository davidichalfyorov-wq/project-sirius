// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using LibreLancer.Graphics.Backends;

namespace LibreLancer.Graphics;

public class RenderTarget2D : RenderTarget
{
    public DepthBuffer DepthBuffer { get; private set; }
    public Texture2D Texture { get; private set; }
    public int Width => Texture.Width;
    public int Height => Texture.Height;

    internal readonly IRenderTarget2D Backing;
    public RenderTarget2D (RenderContext context, int width, int height)
        : this(context, new Texture2D(context, width, height))
    {
    }

    /// <summary>
    /// Render target over a caller-created colour texture, e.g. an
    /// HdrBlendable scene target for the unified frame pipeline.
    /// </summary>
    public RenderTarget2D(RenderContext context, Texture2D texture)
    {
        Texture = texture;
        DepthBuffer = new DepthBuffer(context, texture.Width, texture.Height);
        Backing = context.Backend.CreateRenderTarget2D(Texture.Backing, DepthBuffer.Backing);
        Target = Backing;
    }

    public void BlitToScreen() => Backing.BlitToScreen();

    public void BlitToBuffer(RenderTarget2D other, Point offset) => Backing.BlitToBuffer(other, offset);

    public void BlitToScreen(Point offset) => Backing.BlitToScreen(offset);

    public override void Dispose ()
    {
        Dispose(false);
    }

    public void Dispose(bool keepTexture)
    {
        Backing.Dispose();
        DepthBuffer.Dispose();
        if(!keepTexture)
            Texture.Dispose();
    }
}
