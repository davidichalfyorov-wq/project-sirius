using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace LibreLancer;

/// <summary>
/// Offline cinematic recorder for capturing teaser/trailer footage.
/// SIRIUS_RECORD="/abs/output/dir" enables it: once the player reaches space,
/// a scripted camera (SIRIUS_CAMPATH json) flies a spline and every rendered
/// frame is dumped as a numbered PNG, then the game exits. Pair with
/// SIRIUS_FIXED_DT=1/fps so content time advances exactly one frame per dump
/// regardless of how long render + readback + PNG encode actually take.
/// SIRIUS_RECORD_RES="1920x1080" picks the capture resolution (the swapchain
/// is read back, so capture size == window size).
/// </summary>
public static class SiriusRecorder
{
    public static readonly string? OutDir =
        Environment.GetEnvironmentVariable("SIRIUS_RECORD");

    public static readonly string? CamPathFile =
        Environment.GetEnvironmentVariable("SIRIUS_CAMPATH");

    public static bool Enabled => !string.IsNullOrWhiteSpace(OutDir);

    /// <summary>
    /// Window/capture resolution override while recording. Null when the
    /// recorder is inactive.
    /// </summary>
    public static Point? RecordResolution
    {
        get
        {
            if (!Enabled)
                return null;
            var env = Environment.GetEnvironmentVariable("SIRIUS_RECORD_RES");
            if (!string.IsNullOrWhiteSpace(env))
            {
                var parts = env.Split('x', 'X', ',');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) &&
                    int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) &&
                    w > 0 && h > 0)
                    return new Point(w, h);
                FLLog.Warning("Recorder", $"Bad SIRIUS_RECORD_RES '{env}', using 1920x1080");
            }
            return new Point(1920, 1080);
        }
    }
}

/// <summary>
/// Camera flight path: keyframes interpolated with Catmull-Rom splines for
/// position and look-at target, smoothstep for fov/roll. Times in seconds,
/// fov is vertical degrees, world units match SIRIUS_TELEPORT coordinates.
/// </summary>
public class SiriusCameraPath
{
    public double Fps = 60;
    /// <summary>Seconds of simulation before the first dumped frame, so
    /// teleport pinning, async uploads and IBL probes settle.</summary>
    public double Warmup = 8;
    public List<Key> Keyframes = new();

    public class Key
    {
        public double T;
        public float[] Pos = [0, 0, 0];
        public float[] Look = [0, 0, 0];
        public float Fov = 26;
        public float Roll = 0;
    }

    public double Duration => Keyframes.Count == 0 ? 0 : Keyframes[^1].T;

    public static SiriusCameraPath Load(string file)
    {
        var path = JsonSerializer.Deserialize<SiriusCameraPath>(File.ReadAllText(file),
            new JsonSerializerOptions
            {
                IncludeFields = true,
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });
        if (path == null || path.Keyframes.Count < 2)
            throw new InvalidDataException($"Camera path '{file}' needs at least 2 keyframes");
        path.Keyframes.Sort((a, b) => a.T.CompareTo(b.T));
        return path;
    }

    static Vector3 V(float[] a) => new(a[0], a[1], a[2]);

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
    {
        var u2 = u * u;
        var u3 = u2 * u;
        return 0.5f * ((2f * p1) + (-p0 + p2) * u +
                       (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 +
                       (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
    }

    public (Vector3 Pos, Vector3 Look, float Fov, float Roll) Evaluate(double t)
    {
        var keys = Keyframes;
        if (t <= keys[0].T)
            return (V(keys[0].Pos), V(keys[0].Look), keys[0].Fov, keys[0].Roll);
        if (t >= keys[^1].T)
            return (V(keys[^1].Pos), V(keys[^1].Look), keys[^1].Fov, keys[^1].Roll);
        int i = 0;
        while (i < keys.Count - 2 && t >= keys[i + 1].T)
            i++;
        var k1 = keys[i];
        var k2 = keys[i + 1];
        var span = k2.T - k1.T;
        var u = span <= 0 ? 0f : (float)((t - k1.T) / span);
        var k0 = keys[Math.Max(i - 1, 0)];
        var k3 = keys[Math.Min(i + 2, keys.Count - 1)];
        var pos = CatmullRom(V(k0.Pos), V(k1.Pos), V(k2.Pos), V(k3.Pos), u);
        var look = CatmullRom(V(k0.Look), V(k1.Look), V(k2.Look), V(k3.Look), u);
        var s = u * u * (3f - 2f * u);
        var fov = MathHelper.Lerp(k1.Fov, k2.Fov, s);
        var roll = MathHelper.Lerp(k1.Roll, k2.Roll, s);
        return (pos, look, fov, roll);
    }
}

/// <summary>
/// ICamera fed directly from an evaluated camera path frame.
/// </summary>
public class SiriusRecorderCamera : ICamera
{
    public Matrix4x4 Projection { get; private set; } = Matrix4x4.Identity;
    public Matrix4x4 View { get; private set; } = Matrix4x4.Identity;
    public Vector3 Position { get; private set; }

    private Matrix4x4 vp = Matrix4x4.Identity;
    private BoundingFrustum frustum = new(Matrix4x4.Identity);
    private bool dirty = true;

    public void SetFrame(Vector3 pos, Vector3 look, float fovDeg, float rollDeg, float aspect)
    {
        Position = pos;
        var fwd = look - pos;
        fwd = fwd.LengthSquared() < 1e-6f ? -Vector3.UnitZ : Vector3.Normalize(fwd);
        var up = MathF.Abs(Vector3.Dot(fwd, Vector3.UnitY)) > 0.999f ? Vector3.UnitZ : Vector3.UnitY;
        if (rollDeg != 0)
            up = Vector3.TransformNormal(up,
                Matrix4x4.CreateFromAxisAngle(fwd, MathHelper.DegreesToRadians(rollDeg)));
        View = Matrix4x4.CreateLookAt(pos, pos + fwd, up);
        Projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(fovDeg), aspect, 3f, 10_000_000f);
        dirty = true;
    }

    public Matrix4x4 ViewProjection
    {
        get
        {
            if (dirty) UpdateVp();
            return vp;
        }
    }

    private void UpdateVp()
    {
        vp = View * Projection;
        frustum = new BoundingFrustum(vp);
        dirty = false;
    }

    public bool FrustumCheck(BoundingSphere sphere)
    {
        if (dirty) UpdateVp();
        return frustum.Intersects(sphere);
    }

    public bool FrustumCheck(BoundingBox box)
    {
        if (dirty) UpdateVp();
        return frustum.Intersects(box);
    }
}

/// <summary>
/// Per-session recorder state machine, owned by SpaceGameplay. Warms up,
/// flies the path while dumping every frame, then exits the game.
/// </summary>
public class SiriusRecorderDriver
{
    public SiriusRecorderCamera Camera { get; } = new();

    private readonly FreelancerGame game;
    private readonly SiriusCameraPath path;
    private readonly string outDir;
    private readonly int totalFrames;
    private double time;
    private int frame;
    private bool exiting;

    public SiriusRecorderDriver(FreelancerGame game)
    {
        this.game = game;
        outDir = SiriusRecorder.OutDir!;
        if (string.IsNullOrWhiteSpace(SiriusRecorder.CamPathFile))
            throw new InvalidDataException("SIRIUS_RECORD is set but SIRIUS_CAMPATH is not");
        path = SiriusCameraPath.Load(SiriusRecorder.CamPathFile);
        Directory.CreateDirectory(outDir);
        totalFrames = (int)Math.Round(path.Duration * path.Fps) + 1;
        FLLog.Info("Recorder",
            $"Recording {totalFrames} frames @{path.Fps}fps to {outDir} after {path.Warmup}s warmup");
    }

    public void Update(double delta)
    {
        if (exiting)
            return;
        time += delta;
        var t = time - path.Warmup;
        var (pos, look, fov, roll) = path.Evaluate(Math.Clamp(t, 0, path.Duration));
        Camera.SetFrame(pos, look, fov, roll, game.RenderContext.CurrentViewport.AspectRatio);
        if (t < 0)
            return;
        if (frame >= totalFrames)
        {
            exiting = true;
            FLLog.Info("Recorder", $"Done: {frame} frames -> {outDir}");
            game.Exit();
            return;
        }
        game.Screenshot(Path.Combine(outDir, $"frame_{frame:D6}.png"));
        frame++;
    }
}
