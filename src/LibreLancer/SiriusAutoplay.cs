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
    /// SIRIUS_GOLDEN_POSE=1 (with SIRIUS_AUTOPLAY + SIRIUS_GOLDEN_DIR):
    /// deterministic PBR "beauty pose". Instead of the wall-clock-timed,
    /// chase-camera autoplay space shot (whose run-to-run jitter swamps any
    /// material change), this pins the ship at DirectorPose, freezes physics
    /// AND animators, mounts a fixed camera, and captures one static frame
    /// (pbr.png) so material/lighting edits are FLIP-measurable.
    /// </summary>
    public static readonly bool GoldenPose =
        Environment.GetEnvironmentVariable("SIRIUS_GOLDEN_POSE") == "1";

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

    /// <summary>
    /// Fixed beauty-camera for the deterministic PBR pose: a WORLD-space
    /// offset (dx,dy,dz) from the pinned ship plus vertical FOV. The camera
    /// sits at ship.Position + offset and looks at the ship. The pinned yaw
    /// is fixed, so a world offset is fully deterministic. Override (no
    /// rebuild) with SIRIUS_GOLDEN_POSE_CAM="dx,dy,dz,fovDeg".
    /// </summary>
    public static (Vector3 Offset, float Fov) GoldenPoseCamera()
    {
        var value = Environment.GetEnvironmentVariable("SIRIUS_GOLDEN_POSE_CAM");
        if (!string.IsNullOrWhiteSpace(value))
        {
            var parts = value.Split(',');
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            const System.Globalization.NumberStyles style =
                System.Globalization.NumberStyles.Float;
            if (parts.Length == 4 &&
                float.TryParse(parts[0], style, inv, out var rx) &&
                float.TryParse(parts[1], style, inv, out var uy) &&
                float.TryParse(parts[2], style, inv, out var bz) &&
                float.TryParse(parts[3], style, inv, out var fov))
                return (new Vector3(rx, uy, bz), fov);
        }
        return (new Vector3(10f, 6f, 22f), 28f);
    }
}
