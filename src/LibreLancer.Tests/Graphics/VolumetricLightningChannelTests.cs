using System;
using System.Numerics;
using LibreLancer.Data.GameData.World;
using LibreLancer.Render;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricLightningChannelTests
{
    [Fact]
    public void PulseHasFlashAndAfterglow()
    {
        var flash = VolumetricLightningChannelState.EvaluatePulse(0.01f, 3f, 0.05f, 0.4f,
            out _, out var flashIndex);
        var after = VolumetricLightningChannelState.EvaluatePulse(0.22f, 3f, 0.05f, 0.4f,
            out _, out var afterIndex);
        var idle = VolumetricLightningChannelState.EvaluatePulse(1.2f, 3f, 0.05f, 0.4f,
            out _, out var idleIndex);

        Assert.True(flash > 0.5f);
        Assert.Equal(0, flashIndex);
        Assert.True(after > 0f);
        Assert.Equal(3, afterIndex);
        Assert.Equal(0f, idle);
        Assert.Equal(-1, idleIndex);
    }

    [Fact]
    public void PulseSupportsFourFlashElectricProfiles()
    {
        var pulse = VolumetricLightningChannelState.EvaluatePulse(0.055f * 4.50f, 3f, 0.055f, 0.4f,
            flashCount: 4, out _, out var flashIndex);

        Assert.True(pulse > 0f);
        Assert.Equal(3, flashIndex);
    }

    [Fact]
    public void ArtProfilesTuneArchetypeColorAndShape()
    {
        var crow = VolumetricLightningArtProfile.ForArchetype("crow", 2, new Vector4(0.4f, 0.5f, 0.6f, 1f));
        var nomad = VolumetricLightningArtProfile.ForArchetype("nomad", 2, new Vector4(0.4f, 0.5f, 0.6f, 1f));
        var generic = VolumetricLightningArtProfile.ForArchetype("badlands", 2, new Vector4(0.4f, 0.5f, 0.6f, 1f));

        Assert.Equal("crow-electric", crow.Name);
        Assert.Equal("nomad-plasma", nomad.Name);
        Assert.Equal("generic-nebula", generic.Name);
        Assert.True(crow.VerticalBlend > generic.VerticalBlend);
        Assert.True(nomad.BranchAmplitude > generic.BranchAmplitude);
        Assert.NotEqual(crow.Color, nomad.Color);
    }

    [Fact]
    public void ChannelBuildsEightPointsInNormalizedVolume()
    {
        Span<Vector4> points = stackalloc Vector4[VolumetricLightningChannelState.MaxPoints];
        VolumetricLightningChannelState.BuildChannel(points, 1234, 0.1f, "crow");
        for (var i = 0; i < points.Length; i++)
        {
            Assert.InRange(points[i].X, -1f, 1f);
            Assert.InRange(points[i].Y, -1f, 1f);
            Assert.InRange(points[i].Z, -1f, 1f);
            Assert.Equal(1f, points[i].W);
        }
        Assert.True(points[^1].Y > points[0].Y);
    }

    [Fact]
    public void NoLegacyLightningMeansInactiveFrame()
    {
        var state = new VolumetricLightningChannelState();
        var frame = state.BuildFrame(MakeProfile(hasLightning: false), MakeFeatures(), 0f);
        Assert.False(frame.Active);
    }

    [Fact]
    public void LightningProfileBuildsActiveChannelDuringFlash()
    {
        var state = new VolumetricLightningChannelState();
        var frame = state.BuildFrame(MakeProfile(hasLightning: true), MakeFeatures(), 0.01f);
        Assert.True(frame.Active);
        Assert.Equal(VolumetricLightningChannelState.MaxPoints, frame.PointCount);
        Assert.True(frame.Intensity > 0f);
        Assert.True(frame.Radius > 0f);
        Assert.Equal("crow-electric", frame.ArtProfileName);
        Assert.Contains("art=crow-electric", frame.DebugSummary);
    }

    [Fact]
    public void GoldenDisableSuppressesLightningFrame()
    {
        var state = new VolumetricLightningChannelState();
        var policy = VolumetricLightningPolicy.ForTesting(0.01f, deterministic: true,
            goldenCapture: true, goldenDisable: true);

        var frame = state.BuildFrame(MakeProfile(hasLightning: true), MakeFeatures(), policy);

        Assert.False(frame.Active);
        Assert.Contains("golden disabled", frame.DebugSummary);
    }

    [Fact]
    public void GoldenDisableDoesNotSuppressLiveLightning()
    {
        var state = new VolumetricLightningChannelState();
        var policy = VolumetricLightningPolicy.ForTesting(0.01f, deterministic: true,
            goldenCapture: false, goldenDisable: true);

        var frame = state.BuildFrame(MakeProfile(hasLightning: true), MakeFeatures(), policy);

        Assert.True(policy.Enabled);
        Assert.True(frame.Active);
        Assert.Contains("replay", frame.DebugSummary);
    }

    [Fact]
    public void ReplayTimeBuildsStableChannel()
    {
        var state = new VolumetricLightningChannelState();
        var policy = VolumetricLightningPolicy.ForTesting(0.01f, deterministic: true, seedSalt: 77);

        var frameA = state.BuildFrame(MakeProfile(hasLightning: true), MakeFeatures(), policy);
        var frameB = state.BuildFrame(MakeProfile(hasLightning: true), MakeFeatures(), policy);

        Assert.True(frameA.Active);
        Assert.Equal(frameA.Point0, frameB.Point0);
        Assert.Equal(frameA.Point7, frameB.Point7);
        Assert.Equal(frameA.Intensity, frameB.Intensity);
        Assert.Contains("replay", frameA.DebugSummary);
    }

    [Fact]
    public void ReplaySeedChangesChannelShape()
    {
        var state = new VolumetricLightningChannelState();
        var profile = MakeProfile(hasLightning: true);
        var frameA = state.BuildFrame(profile, MakeFeatures(),
            VolumetricLightningPolicy.ForTesting(0.01f, deterministic: true, seedSalt: 11));
        var frameB = state.BuildFrame(profile, MakeFeatures(),
            VolumetricLightningPolicy.ForTesting(0.01f, deterministic: true, seedSalt: 29));

        Assert.True(frameA.Active);
        Assert.True(frameB.Active);
        Assert.NotEqual(frameA.Point0, frameB.Point0);
    }

    [Fact]
    public void FeatureSetDropsLightningPolicyWithoutParentChannelFlag()
    {
        using var _ = new CleanLightningEnvironment();
        var settings = new GameSettings
        {
            VolumetricNebula = true,
            VolumetricLightningChannels = false,
            VolumetricLightningDeterministic = true,
            VolumetricLightningGoldenDisable = true,
            VolumetricLightningReplayTime = 0.25f,
            VolumetricLightningReplaySeed = 99
        };

        var features = RenderFeatureSet.FromSettings(settings);

        Assert.False(features.VolumetricLightningChannels);
        Assert.False(features.VolumetricLightningDeterministic);
        Assert.False(features.VolumetricLightningGoldenDisable);
        Assert.Equal(-1f, features.VolumetricLightningReplayTime);
        Assert.Equal(0, features.VolumetricLightningReplaySeed);
    }

    [Fact]
    public void EnvironmentReplayTimeCreatesDeterministicReplayPolicy()
    {
        using var _ = new CleanLightningEnvironment();
        Environment.SetEnvironmentVariable("SIRIUS_VOLUMETRIC_NEBULA", "1");
        Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING", "1");
        Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_REPLAY_TIME", "0.25");
        Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_REPLAY_SEED", "77");

        var features = RenderFeatureSet.FromSettings(new GameSettings());
        var policy = VolumetricLightningPolicy.FromFeatures(features, liveTimeSeconds: 9f);

        Assert.True(features.VolumetricNebula);
        Assert.True(features.VolumetricLightningChannels);
        Assert.False(features.VolumetricLightningDeterministic);
        Assert.Equal(0.25f, features.VolumetricLightningReplayTime);
        Assert.Equal(77, features.VolumetricLightningReplaySeed);
        Assert.True(policy.Deterministic);
        Assert.Equal(0.25f, policy.TimeSeconds);
        Assert.Equal("replay+seed", policy.HudMode);
        Assert.Contains("replay t=0.250s seed=77", policy.DebugSummary);
    }

    private static RenderFeatureSet MakeFeatures() => new(
        RenderFeatureBits.VolumetricNebula | RenderFeatureBits.VolumetricLightningChannels,
        2,
        RenderDebugView.VolumetricLightning,
        null);

    private sealed class CleanLightningEnvironment : IDisposable
    {
        private readonly string? volumetricNebula = Environment.GetEnvironmentVariable("SIRIUS_VOLUMETRIC_NEBULA");
        private readonly string? volFog = Environment.GetEnvironmentVariable("SIRIUS_VOLFOG");
        private readonly string? lightning = Environment.GetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING");
        private readonly string? lightningLong = Environment.GetEnvironmentVariable("SIRIUS_VOLUMETRIC_LIGHTNING_CHANNELS");
        private readonly string? lightningShort = Environment.GetEnvironmentVariable("SIRIUS_VOLLIGHTNING");
        private readonly string? deterministic = Environment.GetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_DETERMINISTIC");
        private readonly string? deterministicShort = Environment.GetEnvironmentVariable("SIRIUS_VOLLIGHTNING_DETERMINISTIC");
        private readonly string? replay = Environment.GetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_REPLAY");
        private readonly string? goldenDisable = Environment.GetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_GOLDEN_DISABLE");
        private readonly string? goldenDisableShort = Environment.GetEnvironmentVariable("SIRIUS_VOLLIGHTNING_GOLDEN_DISABLE");
        private readonly string? disable = Environment.GetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_DISABLE");
        private readonly string? replayTime = Environment.GetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_REPLAY_TIME");
        private readonly string? replayTimeShort = Environment.GetEnvironmentVariable("SIRIUS_VOLLIGHTNING_REPLAY_TIME");
        private readonly string? replaySeed = Environment.GetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_REPLAY_SEED");
        private readonly string? replaySeedShort = Environment.GetEnvironmentVariable("SIRIUS_VOLLIGHTNING_REPLAY_SEED");

        public CleanLightningEnvironment()
        {
            Environment.SetEnvironmentVariable("SIRIUS_VOLUMETRIC_NEBULA", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLUMETRIC_LIGHTNING_CHANNELS", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLLIGHTNING", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_DETERMINISTIC", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLLIGHTNING_DETERMINISTIC", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_REPLAY", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_GOLDEN_DISABLE", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLLIGHTNING_GOLDEN_DISABLE", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_DISABLE", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_REPLAY_TIME", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLLIGHTNING_REPLAY_TIME", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_REPLAY_SEED", null);
            Environment.SetEnvironmentVariable("SIRIUS_VOLLIGHTNING_REPLAY_SEED", null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("SIRIUS_VOLUMETRIC_NEBULA", volumetricNebula);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG", volFog);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING", lightning);
            Environment.SetEnvironmentVariable("SIRIUS_VOLUMETRIC_LIGHTNING_CHANNELS", lightningLong);
            Environment.SetEnvironmentVariable("SIRIUS_VOLLIGHTNING", lightningShort);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_DETERMINISTIC", deterministic);
            Environment.SetEnvironmentVariable("SIRIUS_VOLLIGHTNING_DETERMINISTIC", deterministicShort);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_REPLAY", replay);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_GOLDEN_DISABLE", goldenDisable);
            Environment.SetEnvironmentVariable("SIRIUS_VOLLIGHTNING_GOLDEN_DISABLE", goldenDisableShort);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_DISABLE", disable);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_REPLAY_TIME", replayTime);
            Environment.SetEnvironmentVariable("SIRIUS_VOLLIGHTNING_REPLAY_TIME", replayTimeShort);
            Environment.SetEnvironmentVariable("SIRIUS_VOLFOG_LIGHTNING_REPLAY_SEED", replaySeed);
            Environment.SetEnvironmentVariable("SIRIUS_VOLLIGHTNING_REPLAY_SEED", replaySeedShort);
        }
    }

    private static NebulaVolumeProfile MakeProfile(bool hasLightning) => new(
        "zone_crow_test",
        "crow.ini",
        "crow",
        ShapeKind.Sphere,
        Vector3.Zero,
        Matrix4x4.Identity,
        new Vector3(10000f),
        0.2f,
        new Vector2(1000f, 8000f),
        new Color4(0.2f, 0.3f, 0.6f, 1f),
        new Color4(0.4f, 0.5f, 0.8f, 1f),
        new Color4(0.02f, 0.02f, 0.03f, 1f),
        0.00008f,
        0.55f,
        1f / 6000f,
        0.62f,
        8f,
        0.42f,
        0.82f,
        -0.20f,
        0.80f,
        0.55f,
        0.90f,
        0.24f,
        0.42f,
        true,
        hasLightning,
        0);
}
