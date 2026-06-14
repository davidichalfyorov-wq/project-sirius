using System;
using System.Collections.Generic;
using System.Numerics;
using LibreLancer.World;

namespace LibreLancer.Render.Volumetrics;

public sealed class VolumetricShipDisplacementState
{
    public const int MaxCapsules = 16;
    private const float MaxDistanceMeters = 900f;
    private const float DefaultShipRadiusMeters = 35f;
    private const float DefaultShipLengthMeters = 115f;
    private const float HalfLifeSeconds = 2.6f;

    private readonly Dictionary<int, TrackedShip> tracked = new();

    private readonly record struct TrackedShip(Vector3 Position, float Time);

    public VolumetricShipDisplacementFrame BuildFrame(
        IReadOnlyList<GameObject> objects,
        Vector3 cameraPosition,
        float timeSeconds)
    {
        Span<VolumetricDisplacementCapsule> scratch = stackalloc VolumetricDisplacementCapsule[MaxCapsules];
        var count = 0;
        var bestStrength = 0f;
        var velocitySum = Vector3.Zero;

        for (var i = 0; i < objects.Count && count < MaxCapsules; i++)
        {
            var obj = objects[i];
            if (obj.Kind != GameObjectKind.Ship ||
                (obj.Flags & GameObjectFlags.Hidden) != 0 ||
                (obj.Flags & GameObjectFlags.Exists) == 0)
            {
                continue;
            }

            var tr = obj.WorldTransform;
            var position = tr.Position;
            var toCamera = position - cameraPosition;
            if (toCamera.LengthSquared() > MaxDistanceMeters * MaxDistanceMeters)
            {
                continue;
            }

            tracked.TryGetValue(obj.Unique, out var previous);
            var dt = previous.Time > 0f ? MathF.Max(timeSeconds - previous.Time, 1f / 120f) : 1f / 60f;
            var velocity = previous.Time > 0f ? (position - previous.Position) / dt : Vector3.Zero;
            tracked[obj.Unique] = new TrackedShip(position, timeSeconds);

            var speed = velocity.Length();
            var forward = Vector3.Transform(-Vector3.UnitZ, tr.Orientation);
            if (speed > 2f)
            {
                forward = Vector3.Normalize(velocity);
            }
            else if (forward.LengthSquared() < 0.25f)
            {
                forward = Vector3.UnitZ;
            }
            else
            {
                forward = Vector3.Normalize(forward);
            }

            var radius = EstimateRadius(obj);
            var length = MathF.Max(radius * 2.8f, DefaultShipLengthMeters);
            var nose = position + forward * (length * 0.36f);
            var tail = position - forward * (length * 0.62f);
            var speed01 = Math.Clamp(speed / 650f, 0f, 1f);
            var playerBoost = (obj.Flags & GameObjectFlags.Player) != 0 ? 1.25f : 1.0f;
            var strength = Math.Clamp((0.32f + speed01 * 0.68f) * playerBoost, 0f, 1.35f);
            var swirl = Math.Clamp(0.12f + speed01 * 0.42f, 0f, 0.75f);
            scratch[count++] = new VolumetricDisplacementCapsule(
                nose - cameraPosition,
                tail - cameraPosition,
                radius,
                strength,
                velocity,
                HalfLifeSeconds,
                swirl);
            velocitySum += velocity;
            bestStrength = MathF.Max(bestStrength, strength);
        }

        if (tracked.Count > 512)
        {
            tracked.Clear();
        }

        if (count == 0)
        {
            return VolumetricShipDisplacementFrame.Empty;
        }

        var capsules = new VolumetricDisplacementCapsule[count];
        scratch[..count].CopyTo(capsules);
        var averageVelocity = count > 0 ? velocitySum / count : Vector3.Zero;
        return new VolumetricShipDisplacementFrame(capsules, bestStrength,
            FormattableString.Invariant($"caps={count}, maxPush={bestStrength:0.00}"), averageVelocity);
    }

    private static float EstimateRadius(GameObject obj)
    {
        var radius = DefaultShipRadiusMeters;
        if (obj.Model?.RigidModel != null)
        {
            radius = Math.Clamp(obj.Model.RigidModel.GetRadius() * 0.34f, 18f, 130f);
        }
        return radius;
    }
}

public readonly record struct VolumetricDisplacementCapsule(
    Vector3 A,
    Vector3 B,
    float Radius,
    float Strength,
    Vector3 Velocity,
    float WakeHalfLifeSeconds,
    float SwirlStrength)
{
    public Vector4 PackedA => new(A, Radius);
    public Vector4 PackedB => new(B, Strength);
    public Vector4 PackedVelocity => new(Velocity, WakeHalfLifeSeconds);
    public Vector4 PackedSwirl => new(SwirlStrength, 0f, 0f, 0f);

    public bool IsValid => Radius > 0f && Strength > 0f && Vector3.DistanceSquared(A, B) > 1f;

    public static float CapsuleDistance(Vector3 p, Vector3 a, Vector3 b) =>
        CapsuleDistance(p, a, b, out _);

    public static float CapsuleDistance(Vector3 p, Vector3 a, Vector3 b, out float t)
    {
        var ab = b - a;
        var denom = MathF.Max(ab.LengthSquared(), 1e-4f);
        t = Math.Clamp(Vector3.Dot(p - a, ab) / denom, 0f, 1f);
        var closest = a + ab * t;
        return Vector3.Distance(p, closest);
    }
}

public readonly record struct VolumetricShipDisplacementFrame(
    VolumetricDisplacementCapsule[] Capsules,
    float MaxStrength,
    string DebugSummary,
    Vector3 AverageVelocity)
{
    public static readonly VolumetricShipDisplacementFrame Empty = new([], 0f, "none", Vector3.Zero);
    public int Count => Capsules?.Length ?? 0;
    public bool HasCapsules => Count > 0;
    public VolumetricDisplacementCapsule this[int index] => Capsules[index];
}
