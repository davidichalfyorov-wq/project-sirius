using LibreLancer.Graphics;

namespace LibreLancer.Render;

public static class RenderContextDebugExtensions
{
    public static void PushDebugGroup(this RenderContext renderContext, string name)
    {
        // PR-5.0 stub for OpenGL/legacy. The Vulkan backend already labels
        // timed passes; direct frame-level groups are wired through the backend
        // facade in PR-5.2 when the volumetric passes are allocated.
    }

    public static void PopDebugGroup(this RenderContext renderContext)
    {
    }

    public static bool RequestFrameCapture(this RenderContext renderContext, string? outputPath = null)
    {
        // RenderDoc/GFXReconstruct in-app capture is intentionally a stable
        // hook first. External capture remains the working path in PR-5.0.
        return false;
    }
}
