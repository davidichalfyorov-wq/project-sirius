using System;
using System.Numerics;

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

    /// <summary>
    /// The capture pose: SIRIUS_TELEPORT="x,y,z,yawDegrees" override, or
    /// the default director pose. Single source for the server's per-tick
    /// pin AND the client-side prediction pin (orientation drift between
    /// the two randomly flipped the nose for a whole capture).
    /// </summary>
    public static Transform3D DirectorPose()
    {
        var value = Environment.GetEnvironmentVariable("SIRIUS_TELEPORT");
        if (!string.IsNullOrWhiteSpace(value))
        {
            var parts = value.Split(',');
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            const System.Globalization.NumberStyles style =
                System.Globalization.NumberStyles.Float;
            if (parts.Length == 4 &&
                float.TryParse(parts[0], style, inv, out var x) &&
                float.TryParse(parts[1], style, inv, out var y) &&
                float.TryParse(parts[2], style, inv, out var z) &&
                float.TryParse(parts[3], style, inv, out var yaw))
            {
                return new Transform3D(
                    new Vector3(x, y, z),
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathF.PI / 180f));
            }
        }
        return new Transform3D(
            new Vector3(-33000, 500, -28000),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI));
    }
}
