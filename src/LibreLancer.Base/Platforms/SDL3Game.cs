// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using LibreLancer.Dialogs;
using LibreLancer.Graphics;
using LibreLancer.Graphics.Backends;
using LibreLancer.Graphics.Backends.OpenGL;
using LibreLancer.Graphics.Backends.Vulkan;

namespace LibreLancer.Platforms;

internal class SDL3Game : IGame
{
    private int height;
    private double totalTime;
    private readonly bool allowScreensaver;
    private bool running = false;
    private string title = "LibreLancer";
    private IntPtr windowptr;
    private double renderFrequency;
    public Mouse Mouse { get; } = new();
    public Keyboard Keyboard { get; } = new();
    private readonly ConcurrentQueue<Action?> actions = new();
    private readonly int mythread;
    private uint wakeEvent;

    public ScreenshotSaveHandler? OnScreenshotSave { get; set; }

    public RenderContext RenderContext { get; private set; } = null!;
    public string? Renderer { get; private set; }

    private const string ARRAY_MIMETYPE = "application/x-librelancer-array";
    private static readonly NativeBuffer _mimeTypes;

    static unsafe SDL3Game()
    {
        _mimeTypes = UnsafeHelpers.Allocate(IntPtr.Size);
        var arrayMimetypeNative1 = UnsafeHelpers.StringToNativeUTF8(ARRAY_MIMETYPE);
        *((IntPtr*)(IntPtr)_mimeTypes) = (IntPtr)arrayMimetypeNative1;
    }

    public unsafe SDL3Game(int w, int h, bool allowScreensaver)
    {
        Width = w;
        height = h;
        mythread = Environment.CurrentManagedThreadId;
        this.allowScreensaver = allowScreensaver;
        windowsCb = WindowsCallback;
    }

    private bool _relativeMouseMode = false;

    public bool RelativeMouseMode
    {
        get => _relativeMouseMode;
        set
        {
            if (value != _relativeMouseMode)
                SDL3.SDL_SetWindowRelativeMouseMode(windowptr, value);
            _relativeMouseMode = value;
        }
    }

    public float DpiScale { get; set; } = 1;
    public int Width { get; private set; }
    public IntPtr WindowHandle => windowptr;
    public IntPtr NativeWindowHandle
    {
        get
        {
            if (windowptr == IntPtr.Zero)
                return IntPtr.Zero;
            var props = SDL3.SDL_GetWindowProperties(windowptr);
            if (props == 0)
                return IntPtr.Zero;
            var win32 = SDL3.SDL_GetPointerProperty(props, SDL3.SDL_PROP_WINDOW_WIN32_HWND_POINTER, IntPtr.Zero);
            if (win32 != IntPtr.Zero)
                return win32;
            var cocoa = SDL3.SDL_GetPointerProperty(props, SDL3.SDL_PROP_WINDOW_COCOA_WINDOW_POINTER, IntPtr.Zero);
            if (cocoa != IntPtr.Zero)
                return cocoa;
            var x11 = SDL3.SDL_GetNumberProperty(props, SDL3.SDL_PROP_WINDOW_X11_WINDOW_NUMBER, 0);
            if (x11 != 0)
                return new IntPtr(x11);
            var wayland = SDL3.SDL_GetPointerProperty(props, SDL3.SDL_PROP_WINDOW_WAYLAND_SURFACE_POINTER, IntPtr.Zero);
            if (wayland != IntPtr.Zero)
                return wayland;
            var waylandEgl = SDL3.SDL_GetPointerProperty(props, SDL3.SDL_PROP_WINDOW_WAYLAND_EGL_WINDOW_POINTER, IntPtr.Zero);
            if (waylandEgl != IntPtr.Zero)
                return waylandEgl;
            return IntPtr.Zero;
        }
    }

    public unsafe void SetWindowIcon(int width, int height, ReadOnlySpan<Bgra8> data)
    {
        IntPtr surface;
        fixed (Bgra8* ptr = &data.GetPinnableReference())
        {
            surface = (IntPtr)SDL3.SDL_CreateSurfaceFrom(width, height, SDL3.SDL_PixelFormat.SDL_PIXELFORMAT_RGBA32,
                (IntPtr)ptr, width * 4);
        }
        SDL3.SDL_SetWindowIcon(windowptr, surface);
        SDL3.SDL_DestroySurface(surface);
    }

    public ClipboardContents ClipboardStatus()
    {
        if (SDL3.SDL_HasClipboardData(ARRAY_MIMETYPE))
        {
            return ClipboardContents.Array;
        }
        else if (SDL3.SDL_HasClipboardText())
        {
            return ClipboardContents.Text;
        }

        return ClipboardContents.None;
    }

    public string? GetClipboardText() => SDL3.SDL_GetClipboardText();

    public void SetClipboardText(string? text) => SDL3.SDL_SetClipboardText(text);

    public byte[]? GetClipboardArray()
    {
        var d = SDL3.SDL_GetClipboardData(ARRAY_MIMETYPE, out var sz);

        if (d == IntPtr.Zero)
        {
            return [];
        }

        var b = new byte[sz];
        Marshal.Copy(d, b, 0, b.Length);
        return b;

    }


    [StructLayout(LayoutKind.Sequential)]
    private struct ClipboardData
    {
        public IntPtr Data;
        public IntPtr Size;
    }

    public unsafe void SetClipboardArray(byte[] array)
    {
        var clipboard = Marshal.AllocHGlobal(sizeof(ClipboardData) + array.Length);
        var c = (ClipboardData*)clipboard;
        c->Data = clipboard + sizeof(ClipboardData);
        c->Size = array.Length;
        Marshal.Copy(array, 0, c->Data, array.Length);
        SDL3.SDL_SetClipboardData(&ClipboardCallback, &ClipboardCleanup, clipboard, (IntPtr)_mimeTypes, 1);
    }

    [UnmanagedCallersOnly]
    private static unsafe void ClipboardCleanup(IntPtr userdata) => Marshal.FreeHGlobal(userdata);

    [UnmanagedCallersOnly]
    private static unsafe IntPtr ClipboardCallback(IntPtr userdata, byte* mime_type, IntPtr size)
    {
        var c = (ClipboardData*)userdata;
        var sz = (IntPtr*)size;
        *sz = c->Size;
        return c->Data;
    }

    private IntPtr curArrow;
    private IntPtr curMove;
    private IntPtr curTextInput;
    private IntPtr curResizeNS;
    private IntPtr curResizeEW;
    private IntPtr curResizeNESW;
    private IntPtr curResizeNWSE;
    private IntPtr curNotAllowed;
    private CursorKind cursorKind = CursorKind.Arrow;

    public CursorKind CursorKind
    {
        get => cursorKind;
        set
        {
            if (cursorKind == value)
            {
                return;
            }

            cursorKind = value;
            switch (cursorKind)
            {
                case CursorKind.Arrow:
                    SDL3.SDL_SetCursor(curArrow);
                    break;
                case CursorKind.Move:
                    SDL3.SDL_SetCursor(curMove);
                    break;
                case CursorKind.TextInput:
                    SDL3.SDL_SetCursor(curTextInput);
                    break;
                case CursorKind.ResizeNS:
                    SDL3.SDL_SetCursor(curResizeNS);
                    break;
                case CursorKind.ResizeEW:
                    SDL3.SDL_SetCursor(curResizeEW);
                    break;
                case CursorKind.ResizeNESW:
                    SDL3.SDL_SetCursor(curResizeNESW);
                    break;
                case CursorKind.ResizeNWSE:
                    SDL3.SDL_SetCursor(curResizeNWSE);
                    break;
                case CursorKind.NotAllowed:
                    SDL3.SDL_SetCursor(curNotAllowed);
                    break;
            }

            if (value == CursorKind.None)
                SDL3.SDL_HideCursor();
            else
                SDL3.SDL_ShowCursor();
        }
    }

    public int Height
    {
        get { return height; }
    }

    public double TotalTime
    {
        get { return totalTime; }
    }

    private Stopwatch timer = null!;
    public double TimerTick => timer.ElapsedMilliseconds / 1000.0;

    public string Title
    {
        get => title;
        set
        {
            title = value;
            if (windowptr != IntPtr.Zero)
                SDL3.SDL_SetWindowTitle(windowptr, title);
        }
    }

    public double RenderFrequency => renderFrequency;
    public double FrameTime => frameTime;

    private double frameTime;

    public void QueueUIThread(Action work)
    {
        actions.Enqueue(work);
        InterruptWait();
    }

    public bool IsUiThread() => Environment.CurrentManagedThreadId == mythread;

    private string? _screenShotPath;
    private bool _screenshot;

    public void Screenshot(string filename)
    {
        _screenShotPath = filename;
        _screenshot = true;
    }

    private unsafe void TakeScreenshot()
    {
        var colordata = new Bgra8[Width * height];
        RenderContext.Backend.ReadBackBuffer(Width, height, colordata);
        for (int i = 0; i < colordata.Length; i++)
        {
            colordata[i].A = 0xFF;
        }

        OnScreenshotSave?.Invoke(_screenShotPath, Width, height, colordata);
    }

    // SIRIUS_DUMPFRAMES=<dir> writes every Nth frame as PNG on ANY backend
    // (SIRIUS_DUMPINTERVAL, default 120) - the backend-agnostic counterpart
    // of SIRIUS_VK_DUMPFRAMES for GL/VK flicker comparisons.
    private static readonly string? dumpFramesDir =
        Environment.GetEnvironmentVariable("SIRIUS_DUMPFRAMES");
    private static readonly int dumpFramesInterval =
        int.TryParse(Environment.GetEnvironmentVariable("SIRIUS_DUMPINTERVAL"), out var dfi) && dfi > 0 ? dfi : 120;
    private long dumpFrameCounter;

    private void DumpFrameIfRequested()
    {
        if (dumpFramesDir == null)
        {
            return;
        }
        dumpFrameCounter++;
        if (dumpFrameCounter % dumpFramesInterval != dumpFramesInterval / 2)
        {
            return;
        }
        var pixels = new Bgra8[Width * height];
        RenderContext.Backend.ReadBackBuffer(Width, height, pixels);
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i].A = 0xFF;
        }
        using var file = System.IO.File.Create(
            System.IO.Path.Combine(dumpFramesDir, $"frame_{dumpFrameCounter:D5}.png"));
        // ReadBackBuffer returns bottom-up rows (GL convention): flip on save.
        ImageLib.PNG.Save(file, Width, height, pixels, true);
    }

    private bool isVsync = false;

    public void SetVSync(bool vsync)
    {
        isVsync = vsync;
    }

    public bool IsFullScreen { get; set; }

    public void SetFullScreen(bool fullscreen)
    {
        SDL3.SDL_SetWindowFullscreen(windowptr, fullscreen);
        IsFullScreen = fullscreen;
    }

    /// <summary>
    /// Deterministic framebuffer size for test/golden captures. Window
    /// managers may maximise or tile a plain window regardless of the
    /// configured size, so instead the window goes fullscreen with an
    /// explicit display mode: Wayland emulates arbitrary modes by viewport
    /// scaling, which keeps the swapchain at exactly the requested size.
    /// SIRIUS_WINDOW_SIZE=WxH picks the size; SIRIUS_GOLDEN_DIR alone
    /// defaults to 2560x1440 (the current baseline resolution).
    /// </summary>
    private void ApplyTestWindowSetup(IntPtr sdlWin)
    {
        var sizeEnv = Environment.GetEnvironmentVariable("SIRIUS_WINDOW_SIZE");
        var golden = Environment.GetEnvironmentVariable("SIRIUS_GOLDEN_DIR") != null;
        if (string.IsNullOrWhiteSpace(sizeEnv) && !golden)
            return;
        int w = 2560, h = 1440;
        if (!string.IsNullOrWhiteSpace(sizeEnv))
        {
            var parts = sizeEnv.Split('x', 'X', ',');
            if (parts.Length != 2 || !int.TryParse(parts[0], out w) || !int.TryParse(parts[1], out h) ||
                w <= 0 || h <= 0)
            {
                FLLog.Warning("Game", $"Bad SIRIUS_WINDOW_SIZE '{sizeEnv}', using 2560x1440");
                w = 2560;
                h = 1440;
            }
        }

        SDL3.SDL_SetWindowResizable(sdlWin, false);
        SDL3.SDL_SetWindowSize(sdlWin, w, h);
        // The mode must come from the display's own list (it carries an
        // internal driver handle); Wayland exposes emulated modes for the
        // common resolutions and scales them with a viewport.
        var display = SDL3.SDL_GetDisplayForWindow(sdlWin);
        if (!SDL3.SDL_GetClosestFullscreenDisplayMode(display, w, h, 0f, false, out var mode))
        {
            FLLog.Warning("Game", $"No fullscreen mode close to {w}x{h}: {SDL3.SDL_GetError()}");
        }
        else
        {
            if (mode.w != w || mode.h != h)
                FLLog.Warning("Game", $"Closest fullscreen mode to {w}x{h} is {mode.w}x{mode.h}");
            if (!SDL3.SDL_SetWindowFullscreenMode(sdlWin, ref mode))
                FLLog.Warning("Game", $"SDL_SetWindowFullscreenMode {mode.w}x{mode.h} failed: {SDL3.SDL_GetError()}");
            if (!SDL3.SDL_SetWindowFullscreen(sdlWin, true))
                FLLog.Warning("Game", $"SDL_SetWindowFullscreen failed: {SDL3.SDL_GetError()}");
            FLLog.Info("Game", $"Test window setup: fullscreen mode {mode.w}x{mode.h}@{mode.refresh_rate}");
        }
        // The initial cursor position is wherever the OS pointer happens
        // to sit over the new window - it differs per run, changes button
        // hover state and draws the cursor sprite at a random spot. Park
        // it in the bottom-right corner.
        SDL3.SDL_WarpMouseInWindow(sdlWin, w - 8, h - 8);
    }

    private Point minWindowSize = Point.Zero;

    public Point MinimumWindowSize
    {
        get => minWindowSize;
        set
        {
            minWindowSize = value;
            if (windowptr != IntPtr.Zero)
            {
                SDL3.SDL_SetWindowMinimumSize(windowptr, value.X, value.Y);
            }
        }
    }

    public void BringToFront()
    {
        if (windowptr != IntPtr.Zero)
            SDL3.SDL_RaiseWindow(windowptr);
    }

    private bool waitForEvent = false;
    private int waitTimeout = 2000;

    public void WaitForEvent(int timeout = 2000)
    {
        waitForEvent = true;
        waitTimeout = timeout;
    }

    public void InterruptWait()
    {
        var ev = new SDL3.SDL_Event();
        ev.type = wakeEvent;
        SDL3.SDL_PushEvent(ref ev);
    }

    public void Yield()
    {
        if (mythread != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException();
        }

        RenderContext!.Backend.QueryFences();
        while (actions.TryDequeue(out Action? work))
            work?.Invoke();
    }

    public bool Focused { get; private set; }
    public bool EventsThisFrame { get; private set; }

    private const uint SDL_WINDOWPOS_CENTERED_MASK = 0x2FFF0000u;
    private static uint SDL_WINDOWPOS_CENTERED_DISPLAY(uint idx) => SDL_WINDOWPOS_CENTERED_MASK | idx;

    private PlatformEvents? events = null!;
    private readonly SDL3.SDL_WindowsMessageHook windowsCb;

    private unsafe bool WindowsCallback(IntPtr userdata, SDL3.MSG* msg)
    {
        // ReSharper disable once AccessToDisposedClosure
        events?.WndProc(msg->message, msg->wParam);
        return true;
    }

    public unsafe void Run(Game loop)
    {
        SDL3.SDL_SetHint(SDL3.SDL_HINT_VIDEO_ALLOW_SCREENSAVER, allowScreensaver ? "1" : "0");
        SDL3.SDL_SetHint(SDL3.SDL_HINT_IME_IMPLEMENTED_UI, "0");

        FLLog.Info("SDL", "Using SDL3");
        FLLog.Info("Engine", "Version: " + Platform.GetInformationalVersion<Game>());
        //TODO: This makes i5-7200U on mesa 18 faster, but this should probably be a configurable option
        bool setMesaThread = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("mesa_glthread"));
        if (setMesaThread) Environment.SetEnvironmentVariable("mesa_glthread", "true");
        if (!SDL3.SDL_Init(SDL3.SDL_InitFlags.SDL_INIT_VIDEO | SDL3.SDL_InitFlags.SDL_INIT_EVENTS))
        {
            FLLog.Error("SDL", "SDL_Init failed, exiting.");
            return;
        }


        KeysExtensions.FillKeyNamesSDL();

        wakeEvent = SDL3.SDL_RegisterEvents(1);
        SDL3.SDL_SetHint(SDL3.SDL_HINT_VIDEO_X11_NET_WM_BYPASS_COMPOSITOR, "0");
        //Set GL states
        SDL3.SDL_GL_SetAttribute(SDL3.SDL_GLAttr.SDL_GL_RED_SIZE, 8);
        SDL3.SDL_GL_SetAttribute(SDL3.SDL_GLAttr.SDL_GL_GREEN_SIZE, 8);
        SDL3.SDL_GL_SetAttribute(SDL3.SDL_GLAttr.SDL_GL_BLUE_SIZE, 8);
        SDL3.SDL_GL_SetAttribute(SDL3.SDL_GLAttr.SDL_GL_DEPTH_SIZE, 24);
        //Create Window

        var hiddenFlag = loop.Splash ? SDL3.SDL_WindowFlags.SDL_WINDOW_HIDDEN : 0;
        var backendFlag = RenderBackendSelector.Kind == RenderBackendKind.Vulkan
            ? (SDL3.SDL_WindowFlags)0x10000000 // SDL_WINDOW_VULKAN
            : SDL3.SDL_WindowFlags.SDL_WINDOW_OPENGL;
        var flags = backendFlag | SDL3.SDL_WindowFlags.SDL_WINDOW_RESIZABLE |
                    SDL3.SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY |
                    hiddenFlag;
        if (IsFullScreen)
            flags |= SDL3.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN;
        var sdlWin = SDL3.SDL_CreateWindow(Title, Width, height, flags);
        var vulkanWindowFailed = false;
        if (sdlWin == IntPtr.Zero && RenderBackendSelector.Kind == RenderBackendKind.Vulkan)
        {
            // No usable Vulkan loader/driver: SDL refuses the VULKAN window
            // flag outright. Retry as an OpenGL window.
            FLLog.Warning("Graphics", $"Vulkan window creation failed ({SDL3.SDL_GetError()}) - falling back to OpenGL");
            vulkanWindowFailed = true;
            flags = (flags & ~(SDL3.SDL_WindowFlags)0x10000000) | SDL3.SDL_WindowFlags.SDL_WINDOW_OPENGL;
            sdlWin = SDL3.SDL_CreateWindow(Title, Width, height, flags);
        }
        if(Platform.RunningOS != OS.Windows)
            FileDialog.Sdl3Handle = sdlWin; //NFD currently handles windows better than SDL3. May change in future.
        Platform.Init(SDL3.SDL_GetCurrentVideoDriver());
        //Cursors
        curArrow = SDL3.SDL_CreateSystemCursor(SDL3.SDL_SystemCursor.SDL_SYSTEM_CURSOR_DEFAULT);
        curMove = SDL3.SDL_CreateSystemCursor(SDL3.SDL_SystemCursor.SDL_SYSTEM_CURSOR_CROSSHAIR);
        curTextInput = SDL3.SDL_CreateSystemCursor(SDL3.SDL_SystemCursor.SDL_SYSTEM_CURSOR_TEXT);
        curResizeNS = SDL3.SDL_CreateSystemCursor(SDL3.SDL_SystemCursor.SDL_SYSTEM_CURSOR_NS_RESIZE);
        curResizeEW = SDL3.SDL_CreateSystemCursor(SDL3.SDL_SystemCursor.SDL_SYSTEM_CURSOR_EW_RESIZE);
        curResizeNESW = SDL3.SDL_CreateSystemCursor(SDL3.SDL_SystemCursor.SDL_SYSTEM_CURSOR_NESW_RESIZE);
        curResizeNWSE = SDL3.SDL_CreateSystemCursor(SDL3.SDL_SystemCursor.SDL_SYSTEM_CURSOR_NWSE_RESIZE);
        curNotAllowed = SDL3.SDL_CreateSystemCursor(SDL3.SDL_SystemCursor.SDL_SYSTEM_CURSOR_NOT_ALLOWED);
        //Window sizing
        if (sdlWin == IntPtr.Zero)
        {
            CrashWindow.Run("Librelancer", "Failed to create SDL window",
                "SDL Error: " + (SDL3.SDL_GetError() ?? ""));
            return;
        }

        if (minWindowSize != Point.Zero)
        {
            SDL3.SDL_SetWindowMinimumSize(sdlWin, minWindowSize.X, minWindowSize.Y);
        }

        SDL3.SDL_SetEventEnabled((uint)SDL3.SDL_EventType.SDL_EVENT_DROP_FILE, true);
        windowptr = sdlWin;
        ApplyTestWindowSetup(sdlWin);
        IRenderContext? renderBackend =
            RenderBackendSelector.Kind == RenderBackendKind.Vulkan && !vulkanWindowFailed
                ? VKRenderContext.Create(sdlWin)
                : GLRenderContext.Create(sdlWin);
        if (renderBackend == null && !vulkanWindowFailed &&
            RenderBackendSelector.Kind == RenderBackendKind.Vulkan)
        {
            // Vulkan unavailable on this machine: rebuild the window with the
            // OpenGL flag (SDL window backend flags are fixed at creation).
            FLLog.Warning("Graphics", "Vulkan init failed - falling back to OpenGL");
            SDL3.SDL_DestroyWindow(sdlWin);
            flags = (flags & ~(SDL3.SDL_WindowFlags)0x10000000) | SDL3.SDL_WindowFlags.SDL_WINDOW_OPENGL;
            sdlWin = SDL3.SDL_CreateWindow(Title, Width, height, flags);
            if (sdlWin == IntPtr.Zero)
            {
                CrashWindow.Run("Librelancer", "Failed to create SDL window",
                    "SDL Error: " + (SDL3.SDL_GetError() ?? ""));
                return;
            }
            if (Platform.RunningOS != OS.Windows)
                FileDialog.Sdl3Handle = sdlWin;
            if (minWindowSize != Point.Zero)
            {
                SDL3.SDL_SetWindowMinimumSize(sdlWin, minWindowSize.X, minWindowSize.Y);
            }
            windowptr = sdlWin;
            ApplyTestWindowSetup(sdlWin);
            renderBackend = GLRenderContext.Create(sdlWin);
        }
        if (renderBackend == null)
        {
            CrashWindow.Run("Librelancer", $"Failed to create {RenderBackendSelector.Kind} context",
                "Your driver or gpu does not support the selected render backend\n" +
                SDL3.SDL_GetError() ?? "");
            return;
        }

        Renderer = renderBackend.GetRenderer();
        FLLog.Info("Graphics", $"Renderer: {Renderer}");
        SetVSync(true);
        //Init game state
        RenderContext = new RenderContext(renderBackend);
        FLLog.Info("Graphics", $"Max Anisotropy: {RenderContext.MaxAnisotropy}");
        FLLog.Info("Graphics", $"Max AA: {RenderContext.MaxSamples}");
        SDL3.SDL_GetWindowSize(sdlWin, out int windowWidth, out int windowHeight);
        var drawable = RenderContext.Backend.GetDrawableSize(sdlWin);
        Width = drawable.X;
        height = drawable.Y;
        RenderContext.SetDrawableSize(drawable);
        var scaleW = (float)Width / windowWidth;
        var scaleH = (float)height / windowHeight;
        DpiScale = Platform.RunningOS != OS.Windows ? scaleH : SDL3.SDL_GetWindowDisplayScale(windowptr);

        FLLog.Info("GL", $"Dpi Scale: {DpiScale:F4}");
        Texture2D? splashTexture;
        if (loop.Splash && (splashTexture = loop.GetSplashInternal()) != null)
        {
            var props = SDL3.SDL_CreateProperties();
            SDL3.SDL_SetStringProperty(props, SDL3.SDL_PROP_WINDOW_CREATE_TITLE_STRING, Title);
            SDL3.SDL_SetNumberProperty(props, SDL3.SDL_PROP_WINDOW_CREATE_X_NUMBER,
                SDL_WINDOWPOS_CENTERED_DISPLAY(0));
            SDL3.SDL_SetNumberProperty(props, SDL3.SDL_PROP_WINDOW_CREATE_Y_NUMBER,
                SDL_WINDOWPOS_CENTERED_DISPLAY(0));
            SDL3.SDL_SetNumberProperty(props, SDL3.SDL_PROP_WINDOW_CREATE_WIDTH_NUMBER, 750);
            SDL3.SDL_SetNumberProperty(props, SDL3.SDL_PROP_WINDOW_CREATE_HEIGHT_NUMBER, 250);
            SDL3.SDL_SetNumberProperty(props, SDL3.SDL_PROP_WINDOW_CREATE_FLAGS_NUMBER, (long)(
                SDL3.SDL_WindowFlags.SDL_WINDOW_OPENGL | SDL3.SDL_WindowFlags.SDL_WINDOW_BORDERLESS
            ));
            var win2 = SDL3.SDL_CreateWindowWithProperties(props);
            SDL3.SDL_DestroyProperties(props);
            RenderContext.Backend.MakeCurrent(win2);
            RenderContext.ClearColor = Color4.Black;
            var dw = RenderContext.Backend.GetDrawableSize(win2);
            RenderContext.ReplaceViewport(0, 0, dw.X, dw.Y);
            RenderContext.ClearAll();
            var dlist = RenderContext.Renderer2D.CreateDrawList();
            dlist.DrawImageStretched(splashTexture, new Rectangle(0, 0, dw.X, dw.Y), Color4.White, true);
            dlist.Render();
            RenderContext.Backend.SwapWindow(win2, false, false);
            loop.OnLoad();
            splashTexture.Dispose();
            RenderContext.Backend.MakeCurrent(sdlWin);
            SDL3.SDL_DestroyWindow(win2);
        }
        else
        {
            loop.OnLoad();
        }

        SDL3.SDL_ShowWindow(sdlWin);
        events = Platform.SubscribeEvents(this);

        if (Platform.RunningOS == OS.Windows)
        {
            SDL3.SDL_SetWindowsMessageHook(windowsCb, 0);
        }

        //kill the value we set so it doesn't crash child processes
        if (setMesaThread) Environment.SetEnvironmentVariable("mesa_glthread", null);
        //Start game
        running = true;
        timer = new Stopwatch();
        timer.Start();
        double last = 0;
        double elapsed = 0;
        // SIRIUS_FIXED_DT decouples content time from the wall clock for
        // offline frame capture: every loop iteration advances by exactly
        // this many seconds no matter how long render + readback take.
        double fixedDt = 0;
        var fixedDtEnv = Environment.GetEnvironmentVariable("SIRIUS_FIXED_DT");
        if (!string.IsNullOrEmpty(fixedDtEnv) &&
            double.TryParse(fixedDtEnv, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedFixedDt) &&
            parsedFixedDt > 0)
        {
            fixedDt = parsedFixedDt;
            FLLog.Info("Timing", $"Fixed timestep {fixedDt:F6}s ({1.0 / fixedDt:F2} fps content time)");
        }
        double fixedTotal = 0;
        SDL3.SDL_Event e = new SDL3.SDL_Event();
        SDL3.SDL_StopTextInput(windowptr);
        MouseButtons doRelease = 0;
        while (running)
        {
            events.Poll();
            //Window State
            var winFlags = (SDL3.SDL_WindowFlags)SDL3.SDL_GetWindowFlags(sdlWin);
            Focused = (winFlags & SDL3.SDL_WindowFlags.SDL_WINDOW_MOUSE_FOCUS) ==
                      SDL3.SDL_WindowFlags.SDL_WINDOW_MOUSE_FOCUS ||
                      (winFlags & SDL3.SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS) ==
                      SDL3.SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS;
            EventsThisFrame = false;
            //Get Size
            SDL3.SDL_GetWindowSize(sdlWin, out windowWidth, out windowHeight);
            var dw = RenderContext.Backend.GetDrawableSize(sdlWin);
            RenderContext.SetDrawableSize(dw);
            Width = dw.X;
            height = dw.Y;
            scaleW = (float)Width / windowWidth;
            scaleH = (float)height / windowHeight;
            if (Platform.RunningOS != OS.Windows) DpiScale = scaleH;
            else
            {
                DpiScale = SDL3.SDL_GetWindowDisplayScale(windowptr);
            }
            //This allows for press/release in same frame to have
            //button down for one frame, e.g. trackpoint middle click on Linux/libinput.
            MouseButtons pressedThisFrame = 0;
            Mouse.Buttons &= ~doRelease;
            doRelease = 0;
            bool eventWaited = false;
            if (waitForEvent)
            {
                waitForEvent = false;
                if (SDL3.SDL_WaitEventTimeout(out e, waitTimeout))
                {
                    eventWaited = true;
                }
            }

            Mouse.Wheel = 0;
            //Pump message queue
            while (eventWaited || SDL3.SDL_PollEvent(out e))
            {
                eventWaited = false;
                EventsThisFrame = true;
                switch ((SDL3.SDL_EventType)e.type)
                {
                    case SDL3.SDL_EventType.SDL_EVENT_QUIT:
                    {
                        if (loop.OnWillClose())
                            running = false;
                        break;
                    }
                    case SDL3.SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                    {
                        var x = RelativeMouseMode ? e.motion.xrel : e.motion.x;
                        var y = RelativeMouseMode ? e.motion.yrel : e.motion.y;
                        Mouse.X = (int)(scaleW * e.motion.x);
                        Mouse.Y = (int)(scaleH * e.motion.y);
                        Mouse.OnMouseMove((int)(scaleW * x), (int)(scaleH * y));
                        break;
                    }
                    case SDL3.SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
                    {
                        Mouse.X = (int)(scaleW * e.button.x);
                        Mouse.Y = (int)(scaleH * e.button.y);
                        var btn = GetMouseButton(e.button.button);
                        Mouse.Buttons |= btn;
                        pressedThisFrame |= btn;
                        Mouse.OnMouseDown(btn);
                        if (e.button.clicks == 2)
                            Mouse.OnMouseDoubleClick(btn);
                        break;
                    }
                    case SDL3.SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                    {
                        Mouse.X = (int)(scaleW * e.button.x);
                        Mouse.Y = (int)(scaleH * e.button.y);
                        var btn = GetMouseButton(e.button.button);
                        if ((pressedThisFrame & btn) == btn)
                            doRelease |= btn;
                        else
                            Mouse.Buttons &= ~btn;
                        Mouse.OnMouseUp(btn);
                        break;
                    }
                    case SDL3.SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                    {
                        Mouse.OnMouseWheel(e.wheel.x, e.wheel.y);
                        break;
                    }
                    case SDL3.SDL_EventType.SDL_EVENT_TEXT_INPUT:
                    {
                        Keyboard.OnTextInput(Marshal.PtrToStringUTF8((IntPtr)e.text.text)!);
                        break;
                    }
                    case SDL3.SDL_EventType.SDL_EVENT_KEY_DOWN:
                    {
                        Keyboard.OnKeyDown(ConvertScancode(e.key.scancode), (KeyModifiers)e.key.mod, e.key.repeat);
                        break;
                    }
                    case SDL3.SDL_EventType.SDL_EVENT_KEY_UP:
                    {
                        Keyboard.OnKeyUp(ConvertScancode(e.key.scancode), (KeyModifiers)e.key.mod);
                        break;
                    }
                    case SDL3.SDL_EventType.SDL_EVENT_KEYMAP_CHANGED:
                        KeysExtensions.ResetKeyNames();
                        break;
                    case SDL3.SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
                        loop.SignalResize();
                        break;
                    case SDL3.SDL_EventType.SDL_EVENT_DROP_FILE:
                    {
                        var file = UnsafeHelpers.PtrToStringUTF8((IntPtr)e.drop.data);
                        loop.SignalDrop(file);
                        break;
                    }
                    case SDL3.SDL_EventType.SDL_EVENT_CLIPBOARD_UPDATE:
                    {
                        loop.SignalClipboardUpdate();
                        break;
                    }
                }
            }

            Mouse.Wheel /= 2.5f;
            //Do game things
            if (!running)
                break;
            RenderContext.Backend.QueryFences();
            while (actions.TryDequeue(out Action? work))
                work?.Invoke();

            totalTime = fixedDt > 0 ? fixedTotal : timer.Elapsed.TotalSeconds;
            var spikeStart = timer.Elapsed.TotalSeconds;
            loop.OnUpdate(elapsed);
            if (!running)
                break;
            var spikeAfterUpdate = timer.Elapsed.TotalSeconds;
            loop.OnDraw(elapsed);
            RenderContext.EndFrame();
            var spikeAfterDraw = timer.Elapsed.TotalSeconds;
            //Frame time before, FPS after
            var tk = timer.Elapsed.TotalSeconds - totalTime;
            frameTime = CalcAverageTime(tk);
            if (_screenshot)
            {
                TakeScreenshot();
                _screenshot = false;
            }
            DumpFrameIfRequested();

            RenderContext.Backend.SwapWindow(sdlWin, isVsync, IsFullScreen);
            // Stutter forensics: one line per slow frame saying where the
            // time went. 40ms = clearly perceptible hitch at any refresh
            // rate; loads produce a handful of lines, smooth play none.
            var spikeAfterSwap = timer.Elapsed.TotalSeconds;
            if (spikeAfterSwap - spikeStart > 0.040)
            {
                FLLog.Info("Timing", FormattableString.Invariant(
                    $"Frame spike {(spikeAfterSwap - spikeStart) * 1000:F1}ms: update={(spikeAfterUpdate - spikeStart) * 1000:F1} draw={(spikeAfterDraw - spikeAfterUpdate) * 1000:F1} present={(spikeAfterSwap - spikeAfterDraw) * 1000:F1}"));
            }

            elapsed = timer.Elapsed.TotalSeconds - last;
            renderFrequency = (1.0 / CalcAverageTick(elapsed));
            last = timer.Elapsed.TotalSeconds;
            totalTime = timer.Elapsed.TotalSeconds;
            if (fixedDt > 0)
            {
                elapsed = fixedDt;
                fixedTotal += fixedDt;
                totalTime = fixedTotal;
            }
            if (elapsed < 0)
            {
                elapsed = 0;
                FLLog.Warning("Timing", "Stopwatch returned negative time!");
            }
        }

        events?.Dispose();
        events = null;
        FileDialog.Sdl3Handle = IntPtr.Zero;
        loop.OnCleanup();
        Platform.Shutdown();
        SDL3.SDL_Quit();
    }

    //TODO: Terrible Hack
    public void Crashed()
    {
        events?.Dispose();
        events = null;
        SDL3.SDL_Quit();
    }

    private const int FPS_MAXSAMPLES = 50;
    private int tickindex = 0;
    private double ticksum = 0;
    private readonly double[] ticklist = new double[FPS_MAXSAMPLES];

    private double CalcAverageTick(double newtick)
    {
        ticksum -= ticklist[tickindex];
        ticksum += newtick;
        ticklist[tickindex] = newtick;
        if (++tickindex == FPS_MAXSAMPLES)
            tickindex = 0;
        return ((double)ticksum / FPS_MAXSAMPLES);
    }

    private int timeindex = 0;
    private double timesum = 0;
    private readonly double[] timelist = new double[FPS_MAXSAMPLES];

    private double CalcAverageTime(double newtick)
    {
        timesum -= timelist[timeindex];
        timesum += newtick;
        timelist[timeindex] = newtick;
        if (++timeindex == FPS_MAXSAMPLES)
            timeindex = 0;
        return ((double)timesum / FPS_MAXSAMPLES);
    }

    private static Keys ConvertScancode(SDL3.SDL_Scancode scancode)
    {
        if (scancode <= SDL3.SDL_Scancode.SDL_SCANCODE_MODE)
        {
            // They're the same
            return (Keys)scancode;
        }

        switch (scancode)
        {
            case SDL3.SDL_Scancode.SDL_SCANCODE_MUTE:
                return Keys.AudioMute;
            case SDL3.SDL_Scancode.SDL_SCANCODE_MEDIA_NEXT_TRACK:
                return Keys.AudioNext;
            case SDL3.SDL_Scancode.SDL_SCANCODE_MEDIA_PLAY:
                return Keys.AudioPlay;
            case SDL3.SDL_Scancode.SDL_SCANCODE_MEDIA_PREVIOUS_TRACK:
                return Keys.AudioPrev;
            case SDL3.SDL_Scancode.SDL_SCANCODE_MEDIA_STOP:
                return Keys.AudioStop;
            case SDL3.SDL_Scancode.SDL_SCANCODE_MEDIA_EJECT:
                return Keys.Eject;
            case SDL3.SDL_Scancode.SDL_SCANCODE_MEDIA_SELECT:
                return Keys.MediaSelect;
            default:
                return Keys.Unknown;
        }
    }

    //Convert from SDL3 button to saner button
    private MouseButtons GetMouseButton(byte b)
    {
        // sic. SDL3 bindings don't contain button const?
        if (b == SDL2.SDL_BUTTON_LEFT)
            return MouseButtons.Left;
        if (b == SDL2.SDL_BUTTON_MIDDLE)
            return MouseButtons.Middle;
        if (b == SDL2.SDL_BUTTON_RIGHT)
            return MouseButtons.Right;
        if (b == SDL2.SDL_BUTTON_X1)
            return MouseButtons.X1;
        if (b == SDL2.SDL_BUTTON_X2)
            return MouseButtons.X2;
        throw new Exception("SDL3 gave undefined mouse button"); //should never happen
    }

    private bool textInputEnabled = false;

    public void EnableTextInput()
    {
        if (!textInputEnabled)
        {
            SDL3.SDL_StartTextInput(windowptr);
            textInputEnabled = true;
        }
    }

    public void DisableTextInput()
    {
        if (textInputEnabled)
        {
            SDL3.SDL_StopTextInput(windowptr);
            textInputEnabled = false;
        }
    }

    public unsafe void SetTextInputRect(Rectangle? rect)
    {
        if (rect == null)
            SDL3.SDL_SetTextInputArea(windowptr, null, 0);
        else
        {
            var sr = new SDL3.SDL_Rect()
            {
                x = rect.Value.X,
                y = rect.Value.Y,
                w = rect.Value.Width,
                h = rect.Value.Height
            };
            SDL3.SDL_SetTextInputArea(windowptr, &sr, 0);
        }
    }

    public void Exit()
    {
        running = false;
    }
}
