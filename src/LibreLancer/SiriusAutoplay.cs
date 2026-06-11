using System;

namespace LibreLancer;

/// <summary>
/// Dev/CI helper: when the environment variable SIRIUS_AUTOPLAY=1 is set,
/// the game drives itself into gameplay with no input — main menu starts a
/// new game, and the base auto-launches into space a few seconds later.
/// Used for headless runtime testing (NPC traffic, GCS chatter, server logs).
/// Has no effect unless the variable is explicitly set.
/// </summary>
public static class SiriusAutoplay
{
    public static readonly bool Enabled =
        Environment.GetEnvironmentVariable("SIRIUS_AUTOPLAY") == "1";

    /// <summary>
    /// When set, autoplay captures deterministic golden screenshots
    /// (menu.png, space.png) into this directory for backend comparison.
    /// </summary>
    public static readonly string? GoldenDir =
        Environment.GetEnvironmentVariable("SIRIUS_GOLDEN_DIR");
}
