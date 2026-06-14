using System;
using System.Numerics;
using LibreLancer.Graphics;
using LibreLancer.Render.Volumetrics;

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
        var froxels = VolumetricNebulaFrameResources.LastDebug;
        var volStatus = vol.Active ? "active" : vol.LegacyFallback ? "legacy" : "off";
        Line(FormattableString.Invariant($"vol nebula {volStatus,8}"), vol.LegacyFallback ? Color4.LightYellow : Color4.LightBlue);
        if (vol.Requested || settings.Phase5DebugView != "off" || froxels.Allocated)
        {
            Line(FormattableString.Invariant($"vol q/n/nd/d/a {vol.Quality}/{vol.NearCascade}/{vol.NearDetail}/{vol.ShipDisplacement}/{vol.AtmosphereLuts}"), Color4.LightBlue);
            Line($"debug view {settings.Phase5DebugView}", Color4.LightBlue);
            Line($"vol reason {vol.Reason}", vol.LegacyFallback ? Color4.LightYellow : Color4.LightBlue);
            Line(FormattableString.Invariant($"froxels    {froxels.Dimensions}"), froxels.Allocated ? Color4.LightGreen : Color4.Orange);
            Line(FormattableString.Invariant($"froxel q   {froxels.Quality}"), froxels.Allocated ? Color4.LightGreen : Color4.Orange);
            if (!string.IsNullOrWhiteSpace(froxels.PerformanceSummary))
            {
                Line($"vol perf   {froxels.PerformanceSummary}", froxels.Allocated ? Color4.LightGreen : Color4.LightBlue);
            }
            if (!string.IsNullOrWhiteSpace(froxels.ActiveProfile))
            {
                Line($"vol profile {froxels.ActiveProfile}", Color4.LightGreen);
            }
            if (froxels.EstimatedBytes > 0)
            {
                Line(FormattableString.Invariant($"vol memory {froxels.EstimatedBytes / (1024.0 * 1024.0),8:0.0} MB"), Color4.LightGreen);
            }
            Line($"vol op     {froxels.LastOperation}", froxels.Allocated ? Color4.LightGreen : Color4.Orange);
            if (froxels.NearDetail)
            {
                Line($"vol near detail {froxels.NearDetailSummary}", Color4.LightGreen);
            }
            if (froxels.DisplacementCapsules > 0 || !string.IsNullOrWhiteSpace(froxels.DisplacementSummary))
            {
                Line(FormattableString.Invariant($"vol disp   {froxels.DisplacementCapsules} capsules"),
                    froxels.DisplacementUpdated ? Color4.LightGreen : Color4.LightBlue);
                Line($"vol wake   {froxels.DisplacementSummary}",
                    froxels.DisplacementUpdated ? Color4.LightGreen : Color4.LightBlue);
            }
            if (froxels.WakeHistoryUpdated || !string.IsNullOrWhiteSpace(froxels.WakeHistorySummary))
            {
                Line($"vol wakeH  {(froxels.WakeHistoryUpdated ? "hist" : "off")}",
                    froxels.WakeHistoryUpdated ? Color4.LightGreen : Color4.LightBlue);
                Line($"vol trail  {froxels.WakeHistorySummary}",
                    froxels.WakeHistoryUpdated ? Color4.LightGreen : Color4.LightBlue);
            }
            if (froxels.WakeCurlUpdated || !string.IsNullOrWhiteSpace(froxels.WakeCurlSummary))
            {
                Line($"vol curl   {(froxels.WakeCurlUpdated ? "vec" : "off")}",
                    froxels.WakeCurlUpdated ? Color4.LightGreen : Color4.LightBlue);
                Line($"vol curl   {froxels.WakeCurlSummary}",
                    froxels.WakeCurlUpdated ? Color4.LightGreen : Color4.LightBlue);
            }
            if (froxels.GodRaysApplied || !string.IsNullOrWhiteSpace(froxels.GodRaySummary))
            {
                Line($"vol rays   {(froxels.GodRaysApplied ? "medium" : "off")}",
                    froxels.GodRaysApplied ? Color4.LightGreen : Color4.LightBlue);
                Line($"vol rays   {froxels.GodRaySummary}",
                    froxels.GodRaysApplied ? Color4.LightGreen : Color4.LightBlue);
            }
            if (froxels.MaterialFogBound || !string.IsNullOrWhiteSpace(froxels.MaterialFogSummary))
            {
                Line($"vol matfog {(froxels.MaterialFogBound ? "bind" : "wait")}",
                    froxels.MaterialFogBound ? Color4.LightGreen : Color4.LightBlue);
                Line($"vol matfog {froxels.MaterialFogSummary}",
                    froxels.MaterialFogBound ? Color4.LightGreen : Color4.LightBlue);
            }
            if (froxels.LightningUpdated || !string.IsNullOrWhiteSpace(froxels.LightningSummary))
            {
                Line($"vol bolt   {(froxels.LightningUpdated ? "chan" : "off")}",
                    froxels.LightningUpdated ? Color4.LightGreen : Color4.LightBlue);
                Line($"vol bolt   {froxels.LightningSummary}",
                    froxels.LightningUpdated ? Color4.LightGreen : Color4.LightBlue);
            }
            Line($"vol comp   {(froxels.Allocated ? (froxels.LastOperation.Contains("composite", StringComparison.OrdinalIgnoreCase) ? "hdr" : "off") : "off")}",
                froxels.LastOperation.Contains("composite", StringComparison.OrdinalIgnoreCase) ? Color4.LightGreen : Color4.LightBlue);
            Line($"vol depth  {(froxels.LastOperation.Contains("composite", StringComparison.OrdinalIgnoreCase) ? "copy" : "no")}",
                froxels.LastOperation.Contains("composite", StringComparison.OrdinalIgnoreCase) ? Color4.LightGreen : Color4.LightBlue);
        }

        var memory = game.RenderContext.DeviceMemoryAllocated;
        if (memory > 0)
        {
            Line(FormattableString.Invariant($"gpu memory {memory / (1024.0 * 1024.0),8:0.0} MB"));
        }

        if (game.GetService<IRendererSettings>() is { } rendererSettings)
        {
            var features = RenderFeatureSet.FromSettings(rendererSettings);
            Line(FormattableString.Invariant($"debug view {features.DebugView}"), Color4.LightSkyBlue);
            Line($"vol nebula {(features.VolumetricNebula ? "req" : "off")}", Color4.LightSkyBlue);
            Line($"vol comp   {(features.VolumetricComposite ? "req" : "off")}", Color4.LightSkyBlue);
            Line($"vol nearc  {(features.VolumetricNearComposite ? "req" : "off")}", Color4.LightSkyBlue);
            Line($"vol nearD  {(features.VolumetricNearDetail ? "req" : "off")}", Color4.LightSkyBlue);
            Line($"vol wakeH  {(features.VolumetricWakeHistory ? "req" : "off")}", Color4.LightSkyBlue);
            Line($"vol curl   {(features.VolumetricWakeCurl ? "req" : "off")}", Color4.LightSkyBlue);
            Line($"vol rays   {(features.VolumetricGodRays ? "req" : "off")}", Color4.LightSkyBlue);
            Line($"vol matfog {(features.VolumetricMaterialFog ? "req" : "off")}", Color4.LightSkyBlue);
            Line($"vol bolt   {(features.VolumetricLightningChannels ? "req" : "off")}", Color4.LightSkyBlue);
            if (features.VolumetricLightningChannels)
            {
                var lightningPolicy = VolumetricLightningPolicy.FromFeatures(features, 0f);
                Line($"vol boltP  {lightningPolicy.HudMode}", Color4.LightSkyBlue);
            }
            Line($"vol temp   {(features.VolumetricTemporal ? (volumetricHistoryActive(froxels) ? "hist" : "req") : "off")}",
                features.VolumetricTemporal ? Color4.LightYellow : Color4.LightSkyBlue);
            Line($"vol reproj {(features.VolumetricReprojection ? "req" : "off")}", Color4.LightSkyBlue);
            Line($"vol noise  {(features.VolumetricBlueNoise ? VolumetricNebulaFrameResources.LastBlueNoiseSource : "off")}",
                features.VolumetricBlueNoise ? Color4.LightYellow : Color4.LightSkyBlue);
            Line($"vol near   {(features.VolumetricNearCascade ? "req" : "off")}", Color4.LightSkyBlue);
            Line($"vol ship   {(features.VolumetricShipDisplacement ? "req" : "off")}", Color4.LightSkyBlue);
            Line($"atmo luts  {(features.AtmosphereLuts ? "req" : "off")}", Color4.LightSkyBlue);
            Line($"atmo aer   {(features.AtmosphereLuts ? (features.AtmosphereAerialPerspective ? "profile" : "identity") : "off")}",
                Color4.LightSkyBlue);
            Line($"atmo cloud {(features.AtmosphereLuts ? (features.AtmosphereCloudShell ? "budget" : "off") : "off")}",
                Color4.LightSkyBlue);
            if (features.AtmosphereLuts)
            {
                var viewport = game.RenderContext.CurrentViewport;
                var budget = VolumetricAtmosphereLutBudget.Create(
                    requested: true,
                    computeSupported: game.RenderContext.HasFeature(GraphicsFeature.Compute),
                    features.VolumetricQuality,
                    viewport.Width,
                    viewport.Height,
                    cloudShellRequested: features.AtmosphereCloudShell);
                Line($"atmo plan  {budget.DebugSummary}",
                    budget.Enabled ? Color4.LightYellow : Color4.Orange);
                var atmo = VolumetricAtmosphereFrameResources.LastDebug;
                Line($"atmo data  {(atmo.Allocated ? "allocated" : "off")}",
                    atmo.Allocated ? Color4.LightGreen : Color4.Orange);
                Line($"atmo data  {atmo.Summary}",
                    atmo.Allocated ? Color4.LightGreen : Color4.Orange);
            }
            if (features.DebugView is RenderDebugView.VolumetricDensity or
                RenderDebugView.VolumetricTransmittance or
                RenderDebugView.VolumetricFroxels or
                RenderDebugView.VolumetricDisplacement or
                RenderDebugView.VolumetricDisplacementHistory or
                RenderDebugView.VolumetricWakeVectors or
                RenderDebugView.VolumetricGodRays or
                RenderDebugView.VolumetricLightning or
                RenderDebugView.VolumetricLightningMask or
                RenderDebugView.VolumetricHistory or
                RenderDebugView.VolumetricHistoryConfidence or
                RenderDebugView.VolumetricJitter or
                RenderDebugView.VolumetricNear or
                RenderDebugView.VolumetricNearDensity)
            {
                Line(froxels.Allocated ? "view data  allocated" : "view data  not allocated", froxels.Allocated ? Color4.LightGreen : Color4.Orange);
            }
            if (features.DebugView is RenderDebugView.AtmosphereLuts or
                RenderDebugView.AtmosphereSkyView or
                RenderDebugView.AtmosphereAerial or
                RenderDebugView.AtmosphereCloudShell)
            {
                var atmo = VolumetricAtmosphereFrameResources.LastDebug;
                Line(atmo.Allocated ? "view data  atmosphere/allocated" : "view data  atmosphere/not allocated",
                    atmo.Allocated ? Color4.LightGreen : Color4.Orange);
            }
        }
        drawList.Render();

        static bool volumetricHistoryActive(VolumetricNebulaResourceDebug debug) =>
            debug.LastOperation.Contains("temporal", StringComparison.OrdinalIgnoreCase);
    }
}
