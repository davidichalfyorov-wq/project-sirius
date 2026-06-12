using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using LibreLancer.Interface;

namespace LibreLancer;

/// <summary>
/// Click-coverage autotest for the UI (SIRIUS_UI_AUTOTEST=menu|full).
/// Resolves each target widget BY ID in the live tree, computes its
/// on-screen point, and synthesizes real input through UiContext.
/// Step kinds: Click (press+release), Hover (park the cursor on the point
/// for DwellSeconds - exercises the smooth hover transitions), Wheel
/// (scroll at the point - exercises map zoom).
/// Screenshots every step and logs "UiTest:" lines for the harness script.
///   menu: options -> back, loadgame -> back, multiplayer -> back, EXIT
///         (the game must terminate via the exit button handler).
///   full: same walk, then clicks NEW GAME so the regular autoplay flow
///         continues; after the golden space shots it walks the whole HUD:
///         map open, map hovers, wheel zoom in/out, universe view + hover,
///         info window, reputation window, chat (MP-only, logged), close.
/// Pair with SIRIUS_AUTOPLAY=1 (synchronous screenshot saves); the menu
/// walker suppresses autoplay's own NewGame call.
/// </summary>
public class SiriusUiAutotest
{
    public static readonly string? Mode =
        Environment.GetEnvironmentVariable("SIRIUS_UI_AUTOTEST");

    public static readonly string? OutDir =
        Environment.GetEnvironmentVariable("SIRIUS_UI_AUTOTEST_DIR");

    public static bool MenuActive => Mode == "menu" || Mode == "full";
    public static bool SpaceActive => Mode == "full";

    /// <summary>
    /// True when the UI must ignore the real mouse: any scripted run
    /// (autotest or golden capture) on a live desktop can receive stray
    /// physical clicks/hovers over the fullscreen test window.
    /// </summary>
    public static bool HermeticInput => MenuActive || SiriusAutoplay.GoldenDir != null;

    private enum StepKind
    {
        Click,
        Hover,
        Wheel
    }

    private record Step(double Wait, string[] Ids, string Label, StepKind Kind = StepKind.Click,
        float RelX = 0.5f, float RelY = 0.5f, float Wheel = 0, double Dwell = 1.2);

    private static List<Step> MenuSteps(bool full)
    {
        var steps = new List<Step>
        {
            new(5.0, ["options"], "click_options"),
            new(3.0, ["goback"], "options_goback"),
            new(3.5, ["loadgame"], "click_loadgame"),
            new(3.0, ["goback", "mainmenu"], "loadgame_goback"),
            new(3.5, ["multiplayer"], "click_multiplayer"),
            new(3.0, ["mainmenu"], "serverlist_mainmenu"),
        };
        steps.Add(full
            ? new Step(3.5, ["newgame"], "click_newgame")
            : new Step(3.5, ["exit"], "click_exit"));
        return steps;
    }

    // HUD walk after the golden space captures. Hovers/wheels target points
    // INSIDE the navmap widget (relative coordinates).
    private static List<Step> SpaceSteps() =>
    [
        new(3.0, ["nn_map"], "map_open"),
        new(2.5, ["navmap"], "map_hover_a", StepKind.Hover, 0.50f, 0.42f),
        new(0.6, ["navmap"], "map_hover_b", StepKind.Hover, 0.36f, 0.60f),
        new(0.6, ["navmap"], "map_wheel_in", StepKind.Wheel, 0.50f, 0.45f, +3),
        new(1.6, ["navmap"], "map_wheel_in2", StepKind.Wheel, 0.50f, 0.45f, +3),
        new(1.6, ["navmap"], "map_wheel_out", StepKind.Wheel, 0.50f, 0.45f, -6),
        new(1.6, ["universebutton"], "map_universe"),
        new(2.0, ["navmap"], "uni_hover", StepKind.Hover, 0.52f, 0.45f),
        new(0.8, ["exit"], "map_close"),
        new(2.0, ["nn_info"], "info_open"),
        new(3.0, ["nn_info"], "info_close"),
        new(2.0, ["nn_playerstatus"], "rep_open"),
        new(3.0, ["nn_playerstatus"], "rep_close"),
        new(2.0, ["nn_chat"], "chat_open"),
        new(3.0, ["nn_chat"], "chat_close"),
    ];

    public static SiriusUiAutotest? CreateMenuWalker() =>
        MenuActive ? new SiriusUiAutotest(MenuSteps(Mode == "full"), "menu") : null;

    public static SiriusUiAutotest? CreateSpaceProber() =>
        SpaceActive ? new SiriusUiAutotest(SpaceSteps(), "space") : null;

    private readonly List<Step> steps;
    private readonly string phase;
    private int index;
    private double timer;
    private bool shotTaken;
    private bool pressed;
    private bool hovering;
    private double hoverTime;
    private int pressPx, pressPy;
    private bool finished;
    private bool unfroze;

    private SiriusUiAutotest(List<Step> steps, string phase)
    {
        this.steps = steps;
        this.phase = phase;
        if (!string.IsNullOrEmpty(OutDir))
            Directory.CreateDirectory(OutDir);
        FLLog.Info("UiTest", $"{phase} walker armed: {steps.Count} steps");
    }

    public bool Finished => finished;

    private void Advance()
    {
        index++;
        timer = 0;
        shotTaken = false;
        pressed = false;
        hovering = false;
        hoverTime = 0;
        if (index >= steps.Count)
        {
            finished = true;
            FLLog.Info("UiTest", $"{phase} walk complete");
        }
    }

    public void Update(UiContext ui, FreelancerGame game, double delta)
    {
        if (finished)
            return;

        if (phase == "space" && !unfroze)
        {
            // Golden capture froze the presentation clock for the shots;
            // lua Timer()-driven window animations need it running again.
            unfroze = true;
            RenderClock.Unfreeze();
        }

        var time = RenderClock.Get(game.TotalTime);
        var s = steps[index];

        if (pressed)
        {
            // Release on the frame after the press: same spot, real
            // click pipeline (hover -> up -> click).
            ui.Update(time, pressPx, pressPy, false);
            ui.OnMouseClick();
            ui.OnMouseUp();
            FLLog.Info("UiTest", $"{phase} step {index} released: {s.Label}");
            Advance();
            return;
        }

        if (hovering)
        {
            // Keep the synthetic cursor parked so hover lerps play out.
            ui.Update(time, pressPx, pressPy, false);
            hoverTime += delta;
            if (!shotTaken && hoverTime >= s.Dwell - 0.05)
            {
                shotTaken = true;
                if (!string.IsNullOrEmpty(OutDir))
                    game.Screenshot(Path.Combine(OutDir, $"{phase}_{index:00}_{s.Label}.png"));
            }
            if (hoverTime >= s.Dwell)
            {
                FLLog.Info("UiTest", $"{phase} step {index} released: {s.Label}");
                Advance();
            }
            return;
        }

        timer += delta;

        // Screenshot shortly before acting: shows the scene the previous
        // step landed on, fully settled. (Hover steps shoot at dwell end.)
        if (s.Kind != StepKind.Hover && !shotTaken && timer >= s.Wait - 0.3)
        {
            shotTaken = true;
            if (!string.IsNullOrEmpty(OutDir))
                game.Screenshot(Path.Combine(OutDir, $"{phase}_{index:00}_{s.Label}.png"));
        }

        if (timer < s.Wait)
            return;

        // Resolve the target's true on-screen rectangle by ID (walks the
        // container chain like input dispatch does).
        RectangleF rect = default;
        string? hit = null;
        foreach (var id in s.Ids)
        {
            if (ui.TryGetWidgetScreenRectangle(id, out rect))
            {
                hit = id;
                break;
            }
        }

        if (hit == null)
        {
            FLLog.Error("UiTest", $"{phase} step {index} FAILED: no widget '{string.Join("/", s.Ids)}' in scene");
            Advance();
            return;
        }

        var point = new Vector2(rect.X + (rect.Width * s.RelX), rect.Y + (rect.Height * s.RelY));
        var ratio = game.Height / 480f;
        pressPx = (int)(point.X * ratio);
        pressPy = (int)(point.Y * ratio);

        switch (s.Kind)
        {
            case StepKind.Click:
                ui.Update(time, pressPx, pressPy, true);
                ui.OnMouseDown();
                pressed = true;
                FLLog.Info("UiTest",
                    $"{phase} step {index} pressed: {s.Label} -> '{hit}' at {point.X:F1},{point.Y:F1}pt = {pressPx},{pressPy}px");
                break;
            case StepKind.Hover:
                ui.Update(time, pressPx, pressPy, false);
                hovering = true;
                shotTaken = false;
                FLLog.Info("UiTest",
                    $"{phase} step {index} hovering: {s.Label} -> '{hit}' at {point.X:F1},{point.Y:F1}pt for {s.Dwell:F1}s");
                break;
            case StepKind.Wheel:
                ui.Update(time, pressPx, pressPy, false);
                ui.OnMouseWheel(s.Wheel);
                FLLog.Info("UiTest",
                    $"{phase} step {index} released: {s.Label} -> '{hit}' wheel {s.Wheel:+0;-0} at {point.X:F1},{point.Y:F1}pt");
                Advance();
                break;
        }
    }
}
