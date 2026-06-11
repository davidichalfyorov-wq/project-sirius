using System;

namespace LibreLancer;

/// <summary>
/// The clock visual animations read instead of raw wall time (tradelane
/// arrows, UI pulses, chat fade, starsphere rotation...). Normally it just
/// passes the game clock through; golden captures freeze it at the moment
/// the director pose is pinned so every run renders the exact same phase.
/// Gameplay logic must keep using the real clock - freezing only affects
/// presentation.
/// </summary>
public static class RenderClock
{
    private static double? frozen;

    public static void Freeze(double time)
    {
        frozen = time;
        FLLog.Info("RenderClock", $"frozen at {time:F3}s for golden capture");
    }

    public static void Unfreeze() => frozen = null;

    public static double Get(double actual) =>
        SiriusAutoplay.GoldenDir != null && frozen.HasValue ? frozen.Value : actual;

    /// <summary>Frozen time when a golden capture has pinned the clock, else null.</summary>
    public static double? Frozen =>
        SiriusAutoplay.GoldenDir != null ? frozen : null;
}
