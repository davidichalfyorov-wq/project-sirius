namespace LibreLancer.Graphics.Backends.Vulkan;

internal unsafe partial class VKRenderContext
{
    public void PushDebugGroup(string name)
    {
        if (!debugUtils)
        {
            return;
        }
        EnsureFrameBegun();
        BeginCmdLabel(name);
    }

    public void PopDebugGroup()
    {
        EndCmdLabel();
    }

    public bool RequestFrameCapture(string? outputPath)
    {
        // PR-5.0 exposes a stable engine hook. A real RenderDoc/vulkan-tools
        // integration can replace this stub without touching renderer call sites.
        return false;
    }
}
