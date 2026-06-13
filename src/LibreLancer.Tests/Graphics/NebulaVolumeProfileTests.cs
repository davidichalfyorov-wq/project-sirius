using System.Numerics;
using LibreLancer.Data.GameData.World;
using LibreLancer.Data.Schema.Universe;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class NebulaVolumeProfileTests
{
    [Fact]
    public void BuildsProfileFromLegacyNebulaData()
    {
        var nebula = MakeNebula("zone_test_generic", ZonePropFlags.Cloud);

        Assert.True(NebulaVolumeProfileMapper.TryCreate(nebula, out var profile));
        Assert.True(profile.IsValid);
        Assert.Equal("zone_test_generic", profile.Nickname);
        Assert.Equal("test_nebula.ini", profile.SourceFile);
        Assert.Equal("cloud", profile.Archetype);
        Assert.Equal(ShapeKind.Sphere, profile.Shape);
        Assert.True(profile.Coverage > 0f);
        Assert.True(profile.CoreExtinction > 0f);
        Assert.True(profile.BoundsRadius > 0f);
        Assert.False(profile.HasLightning);
    }

    [Fact]
    public void BadlandsMapsToDenseRuntimeProfileWithoutNewIniFields()
    {
        var genericNebula = MakeNebula("zone_test_generic", ZonePropFlags.Cloud);
        var badlandsNebula = MakeNebula("zone_badlands", ZonePropFlags.Badlands);

        Assert.True(NebulaVolumeProfileMapper.TryCreate(genericNebula, out var generic));
        Assert.True(NebulaVolumeProfileMapper.TryCreate(badlandsNebula, out var badlands));

        Assert.Equal("badlands", badlands.Archetype);
        Assert.True(badlands.DomainWarp >= generic.DomainWarp);
        Assert.True(badlands.CoreExtinction > 0f);
    }

    [Fact]
    public void MissingZoneDoesNotCreateProfile()
    {
        var nebula = new Nebula
        {
            SourceFile = "test.ini",
            FogColor = Color4.White,
            FogRange = new Vector2(0f, 8000f)
        };

        Assert.False(NebulaVolumeProfileMapper.TryCreate(nebula, out _));
    }

    private static Nebula MakeNebula(string nickname, ZonePropFlags flags)
    {
        var zone = new Zone();
        zone.Nickname = nickname;
        zone.Shape = ShapeKind.Sphere;
        zone.Position = new Vector3(100f, 200f, 300f);
        zone.Size = new Vector3(12000f);
        zone.RotationMatrix = Matrix4x4.Identity;
        zone.EdgeFraction = 0.2f;
        zone.PropertyFlags = flags;
        return new Nebula
        {
            SourceFile = "test_nebula.ini",
            Zone = zone,
            FogEnabled = true,
            FogColor = new Color4(0.10f, 0.20f, 0.30f, 1f),
            FogRange = new Vector2(1000f, 8000f),
            AmbientColor = new Color4(0.02f, 0.03f, 0.04f, 1f)
        };
    }
}
