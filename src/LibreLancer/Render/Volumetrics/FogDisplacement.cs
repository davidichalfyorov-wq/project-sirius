using System;
using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Graphics;
using LibreLancer.Shaders;
using LibreLancer.World;

namespace LibreLancer.Render.Volumetrics;

internal sealed class FogDisplacement : IDisposable
{
    private const int Grid = 64;
    private const int MaxCapsules = 8;
    private const float Extent = 480f;
    private const float Snap = 60f;
    private const float DecaySeconds = 3f;
    private const float MaxSegmentLength = 180f;
    private const float Range = Extent * 0.85f;
    private const float RangeSq = Range * Range;

    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("SIRIUS_VOLDISP") != "0";
    private static readonly bool Force =
        Environment.GetEnvironmentVariable("SIRIUS_VOLDISP_FORCE") == "1";

    private readonly Dictionary<int, Vector3> previousPositions = new();
    private readonly HashSet<int> seenObjects = [];
    private readonly List<int> staleObjects = [];
    private readonly List<Candidate> candidates = new(MaxCapsules * 2);
    private readonly Capsule[] capsules = new Capsule[MaxCapsules];

    private Texture3D? fieldA;
    private Texture3D? fieldB;
    private bool flip;
    private bool historyValid;
    private Vector3 currentOrigin;
    private Vector3 previousOrigin;
    private double previousTime;
    private int emptyFrames;

    public Texture3D? Field { get; private set; }
    public Vector4 OriginExtent => new(currentOrigin, Extent);
    public bool Active => Field != null && historyValid;

    public bool Run(RenderContext rstate, Vector3 cameraPos, IReadOnlyList<GameObject> objects, double time)
    {
        Field = null;
        if (!Enabled || AllShaders.FogDisplace == null)
        {
            historyValid = false;
            previousPositions.Clear();
            return false;
        }
        if (SiriusAutoplay.GoldenDir != null && !Force)
        {
            historyValid = false;
            previousPositions.Clear();
            return false;
        }

        EnsureResources(rstate);

        currentOrigin = SnappedOrigin(cameraPos);
        var dt = DeltaTime(time);
        var capsuleCount = CollectCapsules(objects, cameraPos, dt);
        if (capsuleCount == 0 && !historyValid)
        {
            previousOrigin = currentOrigin;
            return false;
        }
        if (capsuleCount == 0 && emptyFrames++ > 240)
        {
            historyValid = false;
            previousOrigin = currentOrigin;
            return false;
        }
        if (capsuleCount > 0)
        {
            emptyFrames = 0;
        }

        var read = flip ? fieldA! : fieldB!;
        var write = flip ? fieldB! : fieldA!;
        flip = !flip;

        rstate.BeginPassTimer("vol.disp");
        var shader = AllShaders.FogDisplace.Get(0);
        var parameters = BuildParams(capsuleCount, dt);
        shader.SetUniformBlock(3, ref parameters);
        rstate.Textures[0] = read;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.SetStorageImage(4, write);
        rstate.Shader = shader;
        rstate.DispatchCompute(Grid / 4, Grid / 4, Grid / 4);
        rstate.BarrierComputeToCompute();
        rstate.EndPassTimer();

        previousOrigin = currentOrigin;
        historyValid = true;
        Field = write;
        return true;
    }

    private void EnsureResources(RenderContext rstate)
    {
        if (fieldA != null)
        {
            return;
        }

        fieldA = new Texture3D(rstate, Grid, Grid, Grid, SurfaceFormat.HdrBlendable, storage: true);
        fieldB = new Texture3D(rstate, Grid, Grid, Grid, SurfaceFormat.HdrBlendable, storage: true);
        FLLog.Info("Volumetrics", $"Fog displacement grid {Grid}^3, extent {Extent}m, snap {Snap}m");
    }

    private static Vector3 SnappedOrigin(Vector3 cameraPos)
    {
        var min = cameraPos - new Vector3(Extent * 0.5f);
        return new Vector3(
            MathF.Floor(min.X / Snap) * Snap,
            MathF.Floor(min.Y / Snap) * Snap,
            MathF.Floor(min.Z / Snap) * Snap);
    }

    private float DeltaTime(double time)
    {
        float dt;
        if (previousTime <= 0 || time <= previousTime)
        {
            dt = 1f / 60f;
        }
        else
        {
            dt = (float)Math.Clamp(time - previousTime, 1.0 / 120.0, 1.0 / 15.0);
        }
        previousTime = time;
        return dt;
    }

    private int CollectCapsules(IReadOnlyList<GameObject> objects, Vector3 cameraPos, float dt)
    {
        candidates.Clear();
        seenObjects.Clear();

        for (var i = 0; i < objects.Count; i++)
        {
            var obj = objects[i];
            if (obj.Kind != GameObjectKind.Ship ||
                !obj.Flags.HasFlag(GameObjectFlags.Exists) ||
                obj.PhysicsComponent?.Body == null)
            {
                continue;
            }

            var body = obj.PhysicsComponent.Body;
            var pos = body.Position;
            seenObjects.Add(obj.Unique);
            var distSq = Vector3.DistanceSquared(pos, cameraPos);
            if (distSq > RangeSq)
            {
                previousPositions[obj.Unique] = pos;
                continue;
            }

            var forcedPlayer = Force && obj.Flags.HasFlag(GameObjectFlags.Player);
            if (forcedPlayer)
            {
                var fwd = Vector3.Transform(-Vector3.UnitZ, obj.WorldTransform.Orientation);
                if (fwd.LengthSquared() < 1e-4f)
                {
                    fwd = -Vector3.UnitZ;
                }
                else
                {
                    fwd = Vector3.Normalize(fwd);
                }
                var forcedRadius = body.Collider.Radius;
                if (forcedRadius <= 0 && obj.Model != null)
                {
                    forcedRadius = obj.Model.RigidModel.GetRadius();
                }
                forcedRadius = Math.Clamp(forcedRadius * 2.5f, 32f, 140f);
                candidates.Add(new Candidate(distSq,
                    new Capsule(pos + fwd * 140f, pos - fwd * 140f, forcedRadius, 1f)));
                previousPositions[obj.Unique] = pos;
                continue;
            }

            var velocity = body.LinearVelocity;
            var speed = velocity.Length();
            if (speed < 5f)
            {
                previousPositions[obj.Unique] = pos;
                continue;
            }

            if (!previousPositions.TryGetValue(obj.Unique, out var prev))
            {
                prev = pos - Vector3.Normalize(velocity) * MathF.Min(speed * dt, MaxSegmentLength);
            }
            var segment = pos - prev;
            var length = segment.Length();
            if (length > MaxSegmentLength)
            {
                prev = pos - segment / length * MaxSegmentLength;
            }

            var radius = body.Collider.Radius;
            if (radius <= 0 && obj.Model != null)
            {
                radius = obj.Model.RigidModel.GetRadius();
            }
            radius = Math.Clamp(radius * 2.25f, 24f, 120f);
            var strength = Math.Clamp(speed / 120f, 0f, 1f);
            candidates.Add(new Candidate(distSq, new Capsule(prev, pos, radius, strength)));
            previousPositions[obj.Unique] = pos;
        }

        staleObjects.Clear();
        foreach (var kvp in previousPositions)
        {
            if (!seenObjects.Contains(kvp.Key))
            {
                staleObjects.Add(kvp.Key);
            }
        }
        for (var i = 0; i < staleObjects.Count; i++)
        {
            previousPositions.Remove(staleObjects[i]);
        }
        candidates.Sort(static (a, b) => a.DistanceSq.CompareTo(b.DistanceSq));

        var count = Math.Min(candidates.Count, MaxCapsules);
        for (var i = 0; i < count; i++)
        {
            capsules[i] = candidates[i].Capsule;
        }
        return count;
    }

    private FogDisplaceParams BuildParams(int capsuleCount, float dt)
    {
        var p = new FogDisplaceParams
        {
            OriginExtent = new Vector4(currentOrigin, Extent),
            PrevOriginExtent = historyValid ? new Vector4(previousOrigin, Extent) : Vector4.Zero,
            GridDt = new Vector4(Grid, Grid, Grid, dt),
            CountsDecay = new Vector4(capsuleCount, DecaySeconds, 0, 0)
        };
        for (var i = 0; i < capsuleCount; i++)
        {
            p.SetCapsule(i, capsules[i]);
        }
        return p;
    }

    public void Dispose()
    {
        fieldA?.Dispose();
        fieldB?.Dispose();
    }

    private readonly record struct Candidate(float DistanceSq, Capsule Capsule);
    private readonly record struct Capsule(Vector3 Start, Vector3 End, float Radius, float Strength);

    private struct FogDisplaceParams
    {
        public Vector4 OriginExtent;
        public Vector4 PrevOriginExtent;
        public Vector4 GridDt;
        public Vector4 CountsDecay;
        public Vector4 Capsule0StartRadius;
        public Vector4 Capsule1StartRadius;
        public Vector4 Capsule2StartRadius;
        public Vector4 Capsule3StartRadius;
        public Vector4 Capsule4StartRadius;
        public Vector4 Capsule5StartRadius;
        public Vector4 Capsule6StartRadius;
        public Vector4 Capsule7StartRadius;
        public Vector4 Capsule0EndStrength;
        public Vector4 Capsule1EndStrength;
        public Vector4 Capsule2EndStrength;
        public Vector4 Capsule3EndStrength;
        public Vector4 Capsule4EndStrength;
        public Vector4 Capsule5EndStrength;
        public Vector4 Capsule6EndStrength;
        public Vector4 Capsule7EndStrength;

        public void SetCapsule(int index, Capsule capsule)
        {
            var start = new Vector4(capsule.Start, capsule.Radius);
            var end = new Vector4(capsule.End, capsule.Strength);
            switch (index)
            {
                case 0: Capsule0StartRadius = start; Capsule0EndStrength = end; break;
                case 1: Capsule1StartRadius = start; Capsule1EndStrength = end; break;
                case 2: Capsule2StartRadius = start; Capsule2EndStrength = end; break;
                case 3: Capsule3StartRadius = start; Capsule3EndStrength = end; break;
                case 4: Capsule4StartRadius = start; Capsule4EndStrength = end; break;
                case 5: Capsule5StartRadius = start; Capsule5EndStrength = end; break;
                case 6: Capsule6StartRadius = start; Capsule6EndStrength = end; break;
                case 7: Capsule7StartRadius = start; Capsule7EndStrength = end; break;
            }
        }
    }
}
