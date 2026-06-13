using System;
using System.Numerics;
using LibreLancer.Graphics;

namespace LibreLancer.Render;

/// <summary>
/// Engineering Dev HUD (graphics roadmap 9.x): FPS, CPU/GPU frame times,
/// per-pass GPU spans, draw calls and backend memory - visible without an
/// external profiler. Toggled with F11 at runtime or SIRIUS_DEV_HUD=1 from
/// the environment. Text-only (DrawList2D): no ImGui dependency, works on
/// both backends.
/// </summary>
public class DevHudOverlay
{
    private static readonly bool startEnabled =
        Environment.GetEnvironmentVariable("SIRIUS_DEV_HUD") == "1";

    private readonly FreelancerGame game;
    private bool enabled;
    private double cpuMsAvg = 16.6;
    private double fpsAvg = 60;

    public DevHudOverlay(FreelancerGame game)
    {
        this.game = game;
        enabled = startEnabled || game.Config.Settings.DevHud;
        game.Keyboard.KeyDown += e =>
        {
            if (e.Key == Keys.F11)
            {
                enabled = !enabled;
            }
        };
    }

    public void Render(double elapsed)
    {
        if (elapsed > 0)
        {
            // EMA smoothing so the numbers are readable.
            cpuMsAvg = (cpuMsAvg * 0.95) + (elapsed * 1000.0 * 0.05);
            fpsAvg = (fpsAvg * 0.95) + ((1.0 / elapsed) * 0.05);
        }
        if (!enabled)
        {
            return;
        }

        var fonts = game.GetService<FontManager>();
        var font = fonts is { Loaded: true } ? fonts.ResolveNickname("MissionObjective") : null;
        if (font == null)
        {
            return;
        }
        var drawList = game.RenderContext.Renderer2D.CreateDrawList();
        var x = game.RenderContext.CurrentViewport.Width - 280;
        var y = 60f;

        void Line(string text, Color4? color = null)
        {
            drawList.DrawStringBaseline(font, 13, text, new Vector2(x, y) + Vector2.One, Color4.Black);
            drawList.DrawStringBaseline(font, 13, text, new Vector2(x, y), color ?? Color4.White);
            y += 16;
        }

        Line("== DEV HUD (F11) ==", Color4.Yellow);
        Line(FormattableString.Invariant($"fps        {fpsAvg,8:0.0}"));
        Line(FormattableString.Invariant($"cpu frame  {cpuMsAvg,8:0.000} ms"));

        var total = 0.0;
        foreach (var pass in game.RenderContext.PassTimings)
        {
            total += pass.Milliseconds;
            Line(FormattableString.Invariant($"{pass.Name,-12}{pass.Milliseconds,7:0.000} ms"), Color4.LightGreen);
        }
        if (total > 0)
        {
            Line(FormattableString.Invariant($"gpu total  {total,8:0.000} ms"), Color4.LightGreen);
        }

        Line(FormattableString.Invariant($"draw calls {game.DrawCallsPerFrame,8}"));
        if (game.RenderContext.RayTracing is { } rayTracing)
        {
            Line(FormattableString.Invariant(
                $"rt blas    {rayTracing.BlasCount,8}"));
            Line(FormattableString.Invariant(
                $"rt inst    {rayTracing.LastInstanceCount,8}"));
        }

        var settings = game.Config.Settings;
        var vol = VolumetricNebulaFrameDebug.Evaluate(settings, game.RenderContext);
        var volStatus = vol.Active ? "active" : vol.LegacyFallback ? "legacy" : "off";
        Line(FormattableString.Invariant($"vol nebula {volStatus,>8}"), vol.LegacyFallback ? Color4.LightYellow : Color4.LightBlue);
        if (vol.Requested || settings.Phase5DebugView != "off")
        {
            Line(FormattableString.Invariant($"vol q/n/d/a {vol.Quality}/{vol.NearCascade}/{vol.ShipDisplacement}/{vol.AtmosphereLuts}"), Color4.LightBlue);
            Line($"debug view {settings.Phase5DebugView}", Color4.LightBlue);
            Line($"vol reason {vol.Reason}", vol.LegacyFallback ? Color4.LightYellow : Color4.LightBlue);
        }

        var memory = game.RenderContext.DeviceMemoryAllocated;
        if (memory > 0)
        {
            Line(FormattableString.Invariant($"gpu memory {memory / (1024.0 * 1024.0),8:0.0} MB"));
        }
        drawList.Render();
    }
}
