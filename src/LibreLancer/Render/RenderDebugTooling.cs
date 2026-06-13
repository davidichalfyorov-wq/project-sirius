using LibreLancer.Graphics;

namespace LibreLancer.Render;

/// <summary>
/// Small capture/marker facade for PR-5.0. Vulkan debug labels already flow
/// through pass timers; this layer gives the renderer a stable place to add
/// RenderDoc/GFXReconstruct capture hooks without touching SystemRenderer again.
/// </summary>
public sealed class RenderDebugTooling
{
    private bool startupCaptureConsumed;
    private bool envCaptureConsumed;
    private bool frameGroupOpen;
    private bool captureUnavailableLogged;

    public bool CaptureHookAvailable { get; private set; }
    public string? LastCaptureRequest { get; private set; }

    public void BeginFrame(RenderContext renderContext, IRendererSettings settings, RenderFeatureSet features)
    {
        if (features.DebugMarkers)
        {
            renderContext.PushDebugGroup("Sirius.Frame");
            frameGroupOpen = true;
        }

        var startup = settings.SelectedRenderCaptureStartup && !startupCaptureConsumed;
        if (startup)
        {
            startupCaptureConsumed = true;
        }

        var envCapture = EnvironmentBool("SIRIUS_CAPTURE_NEXT_FRAME") && !envCaptureConsumed;
        if (envCapture)
        {
            envCaptureConsumed = true;
        }

        if (startup || envCapture || settings.SelectedRenderCaptureNextFrame)
        {
            LastCaptureRequest = features.CapturePath;
            RenderDocCapture.RequestCapture();
            CaptureHookAvailable = RenderDocCapture.Available;
            if (!CaptureHookAvailable && !captureUnavailableLogged)
            {
                captureUnavailableLogged = true;
                FLLog.Info("RenderDebug",
                    "Frame capture requested; RenderDoc is not attached yet. The existing RenderDoc hook will retry attachment for the first frames, and external RenderDoc/GFXReconstruct capture remains available.");
            }
        }
    }

    public void EndFrame(RenderContext renderContext)
    {
        if (frameGroupOpen)
        {
            renderContext.PopDebugGroup();
            frameGroupOpen = false;
        }
    }

    private static bool EnvironmentBool(string name)
    {
        var value = System.Environment.GetEnvironmentVariable(name);
        return value != null &&
               (value == "1" ||
                value.Equals("true", System.StringComparison.OrdinalIgnoreCase) ||
                value.Equals("yes", System.StringComparison.OrdinalIgnoreCase) ||
                value.Equals("on", System.StringComparison.OrdinalIgnoreCase));
    }
}
