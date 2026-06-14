using System;
using System.Runtime.InteropServices;
using System.Text;

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
    private static IntPtr getNumCaptures;
    private static IntPtr getCapture;
    private static IntPtr setCaptureTitle;
    private static bool capturing;
    private static bool pending;
    private static IntPtr captureDevice;
    private static IntPtr captureWindow;
    private static IntPtr activeCaptureDevice;
    private static IntPtr activeCaptureWindow;
    private static uint captureCountAtStart;
    private static int triggerFallbackPolls;

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
        getNumCaptures = Marshal.ReadIntPtr(api, 13 * IntPtr.Size);
        getCapture = Marshal.ReadIntPtr(api, 14 * IntPtr.Size);
        triggerCapture = Marshal.ReadIntPtr(api, 15 * IntPtr.Size);
        setActiveWindow = Marshal.ReadIntPtr(api, 18 * IntPtr.Size);
        startCapture = Marshal.ReadIntPtr(api, 19 * IntPtr.Size);
        isFrameCapturing = Marshal.ReadIntPtr(api, 20 * IntPtr.Size);
        endCapture = Marshal.ReadIntPtr(api, 21 * IntPtr.Size);
        setCaptureTitle = Marshal.ReadIntPtr(api, 26 * IntPtr.Size);
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
        if (triggerFallbackPolls > 0)
        {
            if (LogNewCapture("TriggerCapture fallback"))
            {
                triggerFallbackPolls = 0;
            }
            else if (--triggerFallbackPolls == 0)
            {
                FLLog.Warning("RenderDoc",
                    $"TriggerCapture fallback did not report a new capture (before={captureCountAtStart}, current={GetCaptureCount()}, device=0x{captureDevice.ToInt64():X}, window=0x{captureWindow.ToInt64():X})");
            }
        }
        if (capturing)
        {
            capturing = false;
            var ok = ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint>)endCapture)(activeCaptureDevice, activeCaptureWindow);
            if (!LogNewCapture($"Capture finished (result {ok})"))
            {
                FLLog.Info("RenderDoc",
                    $"Capture finished (result {ok}, before={captureCountAtStart}, current={GetCaptureCount()}, device=0x{activeCaptureDevice.ToInt64():X}, window=0x{activeCaptureWindow.ToInt64():X})");
            }
            return;
        }
        if (pending || frameCounter == captureFrame)
        {
            pending = false;
            captureCountAtStart = GetCaptureCount();
            ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)startCapture)(captureDevice, captureWindow);
            SetCaptureTitle(frameCounter);
            var active = isFrameCapturing != IntPtr.Zero
                ? ((delegate* unmanaged[Cdecl]<uint>)isFrameCapturing)()
                : 1;
            if (active == 0 && (captureDevice != IntPtr.Zero || captureWindow != IntPtr.Zero))
            {
                FLLog.Warning("RenderDoc",
                    $"StartFrameCapture did not match device/window; retrying wildcard capture (before={captureCountAtStart}, device=0x{captureDevice.ToInt64():X}, window=0x{captureWindow.ToInt64():X})");
                ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)startCapture)(IntPtr.Zero, IntPtr.Zero);
                SetCaptureTitle(frameCounter);
                active = isFrameCapturing != IntPtr.Zero
                    ? ((delegate* unmanaged[Cdecl]<uint>)isFrameCapturing)()
                    : 1;
                if (active != 0)
                {
                    activeCaptureDevice = IntPtr.Zero;
                    activeCaptureWindow = IntPtr.Zero;
                }
            }
            else
            {
                activeCaptureDevice = captureDevice;
                activeCaptureWindow = captureWindow;
            }

            if (active != 0)
            {
                capturing = true;
                FLLog.Info("RenderDoc",
                    $"Capture started (frame {frameCounter}, active {active}, device=0x{activeCaptureDevice.ToInt64():X}, window=0x{activeCaptureWindow.ToInt64():X})");
            }
            else if (triggerCapture != IntPtr.Zero)
            {
                ((delegate* unmanaged[Cdecl]<void>)triggerCapture)();
                triggerFallbackPolls = 4;
                FLLog.Warning("RenderDoc",
                    $"StartFrameCapture did not become active; requested TriggerCapture fallback (before={captureCountAtStart}, device=0x{captureDevice.ToInt64():X}, window=0x{captureWindow.ToInt64():X})");
            }
        }
    }

    private static unsafe uint GetCaptureCount()
    {
        return getNumCaptures == IntPtr.Zero
            ? 0
            : ((delegate* unmanaged[Cdecl]<uint>)getNumCaptures)();
    }

    private static unsafe bool LogNewCapture(string prefix)
    {
        var count = GetCaptureCount();
        if (count <= captureCountAtStart)
        {
            return false;
        }

        var index = count - 1;
        var path = GetCapturePath(index, out var timestamp);
        FLLog.Info("RenderDoc",
            string.IsNullOrWhiteSpace(path)
                ? $"{prefix}: capture count {captureCountAtStart}->{count}, latest index {index}, timestamp {timestamp}"
                : $"{prefix}: capture count {captureCountAtStart}->{count}, latest '{path}', timestamp {timestamp}");
        captureCountAtStart = count;
        return true;
    }

    private static unsafe string? GetCapturePath(uint index, out ulong timestamp)
    {
        timestamp = 0;
        if (getCapture == IntPtr.Zero)
        {
            return null;
        }

        uint length = 0;
        ulong captureTimestamp = 0;
        ((delegate* unmanaged[Cdecl]<uint, byte*, uint*, ulong*, uint>)getCapture)(
            index, null, &length, &captureTimestamp);
        if (length == 0)
        {
            return null;
        }

        var bytes = new byte[length];
        fixed (byte* pBytes = bytes)
        {
            var ok = ((delegate* unmanaged[Cdecl]<uint, byte*, uint*, ulong*, uint>)getCapture)(
                index, pBytes, &length, &captureTimestamp);
            if (ok == 0 || length == 0)
            {
                return null;
            }
        }

        timestamp = captureTimestamp;
        var stringLength = (int)length;
        if (stringLength > 0 && bytes[stringLength - 1] == 0)
        {
            stringLength--;
        }
        return Encoding.UTF8.GetString(bytes, 0, stringLength);
    }

    private static unsafe void SetCaptureTitle(int frame)
    {
        if (setCaptureTitle == IntPtr.Zero)
        {
            return;
        }

        var title = Encoding.UTF8.GetBytes($"Project Sirius frame {frame}\0");
        fixed (byte* pTitle = title)
        {
            ((delegate* unmanaged[Cdecl]<byte*, void>)setCaptureTitle)(pTitle);
        }
    }
}
