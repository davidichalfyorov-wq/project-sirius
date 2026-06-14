using System;
using System.Runtime.InteropServices;

namespace LibreLancer.Render;

/// <summary>
/// In-application RenderDoc hookup (graphics roadmap 9.1). When the game
/// runs under RenderDoc (or with librenderdoc.so preloaded), F12 captures
/// the next frame and SIRIUS_CAPTURE_FRAME=N captures frame N of a
/// headless run. No-op when the library isn't present.
/// </summary>
public static class RenderDocCapture
{
    private const int ApiVersion_1_6_0 = 10600;

    [DllImport("libdl.so.2", EntryPoint = "dlopen")]
    private static extern IntPtr DlOpen(string file, int mode);

    [DllImport("libdl.so.2", EntryPoint = "dlsym")]
    private static extern IntPtr DlSym(IntPtr handle, string symbol);

    private delegate int GetApiDelegate(int version, out IntPtr apiPointers);

    private static IntPtr triggerCapture;
    private static bool available;
    private static int captureFrame = -1;
    private static int frameCounter;
    private static IntPtr setActiveWindow;
    private static IntPtr startCapture;
    private static IntPtr isFrameCapturing;
    private static IntPtr endCapture;
    private static bool capturing;
    private static bool pending;
    private static IntPtr captureDevice;
    private static IntPtr captureWindow;

    public static bool Available => available;

    public static void Initialize(IntPtr devicePointer = default, IntPtr windowHandle = default)
    {
        if (devicePointer != IntPtr.Zero)
            captureDevice = devicePointer;
        if (windowHandle != IntPtr.Zero)
            captureWindow = windowHandle;
        // RTLD_NOW | RTLD_NOLOAD: only attach when RenderDoc already
        // injected its library; never load it cold (that breaks captures).
        var handle = DlOpen("librenderdoc.so", 0x2 | 0x4);
        if (handle == IntPtr.Zero)
        {
            return;
        }
        var sym = DlSym(handle, "RENDERDOC_GetAPI");
        if (sym == IntPtr.Zero)
        {
            return;
        }
        var getApi = Marshal.GetDelegateForFunctionPointer<GetApiDelegate>(sym);
        if (getApi(ApiVersion_1_6_0, out var api) != 1 || api == IntPtr.Zero)
        {
            return;
        }
        // RENDERDOC_API_1_6_0 layout: TriggerCapture is entry 15
        // (17 is LaunchReplayUI - mixing them up pops the GUI instead of
        // writing a capture). TriggerCapture proved unreliable without a
        // registered active window, so we drive Start/EndFrameCapture
        // (entries 19/21) with the backend device pointer and native
        // window handle when the platform exposes them.
        triggerCapture = Marshal.ReadIntPtr(api, 15 * IntPtr.Size);
        setActiveWindow = Marshal.ReadIntPtr(api, 18 * IntPtr.Size);
        startCapture = Marshal.ReadIntPtr(api, 19 * IntPtr.Size);
        isFrameCapturing = Marshal.ReadIntPtr(api, 20 * IntPtr.Size);
        endCapture = Marshal.ReadIntPtr(api, 21 * IntPtr.Size);
        available = startCapture != IntPtr.Zero && endCapture != IntPtr.Zero;
        if (available)
        {
            var deviceSuffix = captureDevice == IntPtr.Zero ? "without backend device pointer" : "with backend device pointer";
            var windowSuffix = captureWindow == IntPtr.Zero ? "without native window handle" : "with native window handle";
            FLLog.Info("RenderDoc", $"In-app capture API attached (F12 / SIRIUS_CAPTURE_FRAME, {deviceSuffix}, {windowSuffix})");
            if (captureDevice != IntPtr.Zero && captureWindow != IntPtr.Zero && setActiveWindow != IntPtr.Zero)
            {
                unsafe
                {
                    ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)setActiveWindow)(captureDevice, captureWindow);
                }
            }
            // SetCaptureFilePathTemplate (index 11): pin captures to a known
            // location - the default template follows $HOME, which the test
            // rigs redirect to throwaway profiles.
            var pathEnv = Environment.GetEnvironmentVariable("SIRIUS_CAPTURE_PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var setTemplate = Marshal.ReadIntPtr(api, 11 * IntPtr.Size);
                if (setTemplate != IntPtr.Zero)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(pathEnv + "\0");
                    unsafe
                    {
                        fixed (byte* pBytes = bytes)
                        {
                            ((delegate* unmanaged[Cdecl]<byte*, void>)setTemplate)(pBytes);
                        }
                    }
                    FLLog.Info("RenderDoc", $"Capture path template: {pathEnv}");
                }
            }
        }

        var env = Environment.GetEnvironmentVariable("SIRIUS_CAPTURE_FRAME");
        if (int.TryParse(env, out var frame) && frame > 0)
        {
            captureFrame = frame;
        }
    }

    /// <summary>Request a capture of the next frame (F12 handler).</summary>
    public static void RequestCapture() => pending = true;

    /// <summary>Called once per frame; fires queued/scheduled captures.</summary>
    private static bool initTried;

    public static unsafe void Tick()
    {
        if (!available)
        {
            // The capture layer loads librenderdoc with the Vulkan
            // instance, which may happen after our static Initialize -
            // retry the attach lazily for the first frames.
            if (!initTried && frameCounter < 300)
            {
                Initialize(captureDevice, captureWindow);
                initTried = available;
            }
            frameCounter++;
            return;
        }
        frameCounter++;
        if (capturing)
        {
            capturing = false;
            var ok = ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint>)endCapture)(captureDevice, captureWindow);
            FLLog.Info("RenderDoc", $"Capture finished (result {ok})");
            return;
        }
        if (pending || frameCounter == captureFrame)
        {
            pending = false;
            ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)startCapture)(captureDevice, captureWindow);
            capturing = true;
            var active = isFrameCapturing != IntPtr.Zero
                ? ((delegate* unmanaged[Cdecl]<uint>)isFrameCapturing)()
                : 1;
            FLLog.Info("RenderDoc", $"Capture started (frame {frameCounter}, active {active})");
        }
    }
}
