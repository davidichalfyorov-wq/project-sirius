using System.Numerics;
using LibreLancer.Render.Volumetrics;
using LibreLancer.World;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricShipDisplacementTests
{
    [Fact]
    public void CapsuleDistanceReturnsZeroInsideSegment()
    {
        var d = VolumetricDisplacementCapsule.CapsuleDistance(
            new Vector3(0f, 0f, 5f),
            Vector3.Zero,
            new Vector3(0f, 0f, 10f),
            out var t);

        Assert.Equal(0f, d, 5);
        Assert.InRange(t, 0.49f, 0.51f);
    }

    [Fact]
    public void CollectorIgnoresNonExistingShips()
    {
        var state = new VolumetricShipDisplacementState();
        var ship = MakeShip(Vector3.Zero, GameObjectFlags.Player);

        var frame = state.BuildFrame([ship], Vector3.Zero, 1f);

        Assert.False(frame.HasCapsules);
    }

    [Fact]
    public void CollectorBuildsCameraRelativeCapsuleForExistingShip()
    {
        var state = new VolumetricShipDisplacementState();
        var ship = MakeShip(new Vector3(0f, 0f, -120f), GameObjectFlags.Player | GameObjectFlags.Exists);

        var frame = state.BuildFrame([ship], Vector3.Zero, 1f);

        Assert.True(frame.HasCapsules);
        Assert.Equal(1, frame.Count);
        Assert.True(frame[0].IsValid);
        Assert.True(frame[0].Radius > 0f);
        Assert.True(frame[0].Strength > 0f);
        Assert.Contains("caps=1", frame.DebugSummary);
    }

    [Fact]
    public void MovingShipProducesVelocityPackedCapsule()
    {
        var state = new VolumetricShipDisplacementState();
        var ship = MakeShip(new Vector3(0f, 0f, -160f), GameObjectFlags.Player | GameObjectFlags.Exists);
        state.BuildFrame([ship], Vector3.Zero, 1f);

        ship.SetLocalTransform(new Transform3D(new Vector3(0f, 0f, -80f), Quaternion.Identity));
        var frame = state.BuildFrame([ship], Vector3.Zero, 1.25f);

        Assert.True(frame.HasCapsules);
        Assert.True(frame[0].Velocity.Length() > 0f);
        Assert.InRange(frame[0].PackedVelocity.W, 2.0f, 3.5f);
        Assert.InRange(frame[0].PackedSwirl.X, 0f, 1f);
    }

    private static GameObject MakeShip(Vector3 position, GameObjectFlags flags)
    {
        var ship = new GameObject
        {
            Kind = GameObjectKind.Ship,
            Flags = flags
        };
        ship.SetLocalTransform(new Transform3D(position, Quaternion.Identity));
        return ship;
    }
}
