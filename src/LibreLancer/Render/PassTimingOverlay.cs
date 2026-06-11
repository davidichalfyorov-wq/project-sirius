using System;
using System.Numerics;
using LibreLancer.Graphics;

namespace LibreLancer.Render;

/// <summary>
/// On-screen per-pass GPU times (roadmap 4.8/9.3), enabled with
/// SIRIUS_PASS_TIMINGS=1. Logs a summary every ~5 seconds for headless
/// captures and states the frame's colour-output convention (the
/// docs/LINEAR_AUDIT.md assert: encode happens in the tonemap pass only).
/// </summary>
public class PassTimingOverlay
{
    public static readonly bool Enabled =
        Environment.GetEnvironmentVariable("SIRIUS_PASS_TIMINGS") == "1";

    private readonly FreelancerGame game;
    private int logCountdown = 300;

    public PassTimingOverlay(FreelancerGame game)
    {
        this.game = game;
    }

    public void Render()
    {
        if (!Enabled)
        {
            return;
        }
        var timings = game.RenderContext.PassTimings;
        var fonts = game.GetService<FontManager>();
        // Fonts resolve to a fallback entry that only exists once loading
        // completes; frames render well before that.
        var font = fonts is { Loaded: true } ? fonts.ResolveNickname("MissionObjective") : null;
        var drawList = font != null ? game.RenderContext.Renderer2D.CreateDrawList() : null;

        var y = 60f;
        var total = 0.0;
        void Line(string text)
        {
            if (drawList == null || font == null)
            {
                return;
            }
            drawList.DrawStringBaseline(font, 13, text, new Vector2(12, y + 1) + Vector2.One, Color4.Black);
            drawList.DrawStringBaseline(font, 13, text, new Vector2(12, y), Color4.LightGreen);
            y += 16;
        }

        Line("output: manual-srgb (tonemap pass)");
        foreach (var pass in timings)
        {
            total += pass.Milliseconds;
            Line(FormattableString.Invariant($"{pass.Name,-14}{pass.Milliseconds,7:0.000} ms"));
        }
        Line(FormattableString.Invariant($"{"gpu total",-14}{total,7:0.000} ms"));
        drawList?.Render();

        if (--logCountdown <= 0)
        {
            logCountdown = 300;
            var summary = new System.Text.StringBuilder("per-pass GPU ms:");
            foreach (var pass in timings)
            {
                summary.Append(FormattableString.Invariant($" {pass.Name}={pass.Milliseconds:0.000}"));
            }
            summary.Append(FormattableString.Invariant($" total={total:0.000}"));
            FLLog.Info("Timings", summary.ToString());
        }
    }
}
