using LibreLancer.Graphics;
using LibreLancer.Shaders;

namespace LibreLancer.Render;

/// <summary>
/// Shared image-based-lighting resources (graphics roadmap 5.3). The BRDF
/// integration LUT renders once on first use and lives for the process
/// (256x256, split-sum scale/bias in RG).
/// </summary>
public static class IblResources
{
    private static Texture2D? brdfLut;
    // Kept alive with the texture: disposing the target retires the
    // texture's image on the Vulkan backend.
    private static RenderTarget2D? brdfTarget;

    public static Texture2D GetBrdfLut(RenderContext rstate)
    {
        if (brdfLut != null && !brdfLut.IsDisposed)
        {
            return brdfLut;
        }
        brdfLut = new Texture2D(rstate, 256, 256, false, SurfaceFormat.HdrBlendable);
        var target = brdfTarget = new RenderTarget2D(rstate, brdfLut);
        var shader = AllShaders.BrdfLut.Get(0);
        var previousTarget = rstate.RenderTarget;
        var oldBlend = rstate.BlendMode;
        var oldCull = rstate.Cull;
        var oldDepth = rstate.DepthEnabled;
        try
        {
            rstate.RenderTarget = target;
            rstate.PushViewport(new Rectangle(0, 0, 256, 256));
            rstate.BlendMode = BlendMode.Opaque;
            rstate.Cull = false;
            rstate.DepthEnabled = false;
            rstate.Shader = shader;
            rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
            rstate.PopViewport();
        }
        finally
        {
            rstate.RenderTarget = previousTarget;
            rstate.BlendMode = oldBlend;
            rstate.Cull = oldCull;
            rstate.DepthEnabled = oldDepth;
        }
        FLLog.Info("Render", "BRDF integration LUT generated (256x256)");
        return brdfLut;
    }
}
