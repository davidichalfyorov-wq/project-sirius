using System;
using System.Linq;
using LibreLancer.Data;
using LibreLancer.Data.GameData;
using LibreLancer.Data.Schema;
using LibreLancer.Data.Schema.Missions;
using LibreLancer.Server.Comms;
using Xunit;

namespace LibreLancer.Tests;

/// <summary>
/// Verifies that GCS chatter sequences are assembled with the original
/// Freelancer grammar: callsign first (faction, designation, two numbers),
/// then the situational clip; permutations only from clips the voice has;
/// silence (false) whenever a required clip is missing.
/// </summary>
public class GcsChatterTests
{
    private static Voice MakeVoice(params string[] lines)
    {
        var v = new Voice { Nickname = "test_voice", Gender = FLGender.male };
        foreach (var line in lines)
        {
            v.Lines[line] = new VoiceLineInfo(0);
        }
        return v;
    }

    private static Voice FullVoice() => MakeVoice(
        "gcs_refer_faction_li_n_short",
        "gcs_refer_formationdesig_01",
        "gcs_refer_formationdesig_02",
        "gcs_misc_number_1", "gcs_misc_number_2", "gcs_misc_number_3",
        "gcs_misc_number_4", "gcs_misc_number_5", "gcs_misc_number_6",
        "gcs_misc_number_7", "gcs_misc_number_8", "gcs_misc_number_9",
        "gcs_misc_number_10", "gcs_misc_number_11", "gcs_misc_number_12",
        "gcs_misc_number_13", "gcs_misc_number_14", "gcs_misc_number_15",
        "gcs_dockrequest_request_01+", "gcs_dockrequest_request_02+",
        "gcs_dockrequest_granted_01-",
        "gcs_combat_announce_enemysighted_01-",
        "gcs_combat_order_engage_01-",
        "gcs_combat_taking_damage_01-",
        "gcs_combat_inflicting_damage_01-",
        "gcs_combat_scream_01-",
        "gcs_combat_fleereason_damaged_01-",
        "gcs_combat_announce_allclear_01-",
        "gcs_dhail_mbid_trader_leg",
        "gcs_dhail_mbid_trader_ill");

    private static Faction MakeFaction(string msgIdPrefix) => new()
    {
        Nickname = "li_n_grp",
        Properties = new FactionProps
        {
            MsgIdPrefix = msgIdPrefix,
            FormationDesig = new ValueRange<int>(197808, 197809)
        }
    };

    [Fact]
    public void DockRequestFollowsVanillaCallsignGrammar()
    {
        var voice = FullVoice();
        var faction = MakeFaction("gcs_refer_faction_li_n");
        var callsign = new GcsVoiceLines.Callsign(1, 4, 13);

        Assert.True(GcsVoiceLines.TryBuild(
            ChatterEvent.DockRequest, voice, faction, callsign, true, new Random(1), out var segments));

        Assert.Equal(5, segments.Length);
        Assert.Equal("gcs_refer_faction_li_n_short", segments[0]);
        Assert.Equal("gcs_refer_formationdesig_01", segments[1]);
        Assert.Equal("gcs_misc_number_4", segments[2]);
        Assert.Equal("gcs_misc_number_13", segments[3]);
        Assert.StartsWith("gcs_dockrequest_request_", segments[4]);
        Assert.EndsWith("+", segments[4]); // dock requests keep the rising intonation form
    }

    [Fact]
    public void CombatPhrasesAreSingleTerminalClips()
    {
        var voice = FullVoice();
        var faction = MakeFaction("gcs_refer_faction_li_n");
        var callsign = GcsVoiceLines.Callsign.Generate(faction, new Random(7));

        foreach (var evt in new[]
                 {
                     ChatterEvent.EnemySighted, ChatterEvent.OrderEngage,
                     ChatterEvent.TakingDamage, ChatterEvent.InflictingDamage,
                     ChatterEvent.Death, ChatterEvent.Fleeing, ChatterEvent.AllClear,
                     ChatterEvent.DockGranted
                 })
        {
            Assert.True(GcsVoiceLines.TryBuild(evt, voice, faction, callsign, true, new Random(2), out var segments));
            var segment = Assert.Single(segments);
            Assert.EndsWith("-", segment); // phrase-final falling intonation
        }
    }

    [Fact]
    public void EverySegmentComesFromTheSpeakingVoice()
    {
        var voice = FullVoice();
        var faction = MakeFaction("gcs_refer_faction_li_n");
        var random = new Random(42);

        foreach (ChatterEvent evt in Enum.GetValues<ChatterEvent>())
        {
            var callsign = GcsVoiceLines.Callsign.Generate(faction, random);
            if (!GcsVoiceLines.TryBuild(evt, voice, faction, callsign, true, random, out var segments))
            {
                continue;
            }
            Assert.All(segments, s => Assert.True(voice.Lines.ContainsKey(s), $"{evt}: '{s}' not in voice"));
        }
    }

    [Fact]
    public void MissingClipMeansSilenceNotPartialPhrase()
    {
        // Voice without number clips cannot speak a dock request at all.
        var crippled = MakeVoice(
            "gcs_refer_faction_li_n_short",
            "gcs_refer_formationdesig_01",
            "gcs_dockrequest_request_01+");
        var faction = MakeFaction("gcs_refer_faction_li_n");

        Assert.False(GcsVoiceLines.TryBuild(
            ChatterEvent.DockRequest, crippled, faction, new GcsVoiceLines.Callsign(1, 2, 3), true,
            new Random(3), out var segments));
        Assert.Empty(segments);
    }

    [Fact]
    public void FactionlessShipsDoNotBroadcastCallsigns()
    {
        Assert.False(GcsVoiceLines.TryBuild(
            ChatterEvent.DockRequest, FullVoice(), null, new GcsVoiceLines.Callsign(1, 2, 3), true,
            new Random(4), out _));
    }

    [Fact]
    public void CallsignGenerationStaysInClipRange()
    {
        var faction = MakeFaction("gcs_refer_faction_li_n");
        var random = new Random(11);
        for (var i = 0; i < 200; i++)
        {
            var callsign = GcsVoiceLines.Callsign.Generate(faction, random);
            Assert.InRange(callsign.DesigClip, 1, 29);
            Assert.InRange(callsign.Number1, 1, 15);
            Assert.InRange(callsign.Number2, 1, 15);
        }
    }

    [Fact]
    public void MindYourBusinessPicksLegalityVariant()
    {
        var voice = FullVoice();
        Assert.True(GcsVoiceLines.TryBuild(
            ChatterEvent.MindYourBusiness, voice, null, default, true, new Random(5), out var legal));
        Assert.Equal("gcs_dhail_mbid_trader_leg", legal.Single());

        Assert.True(GcsVoiceLines.TryBuild(
            ChatterEvent.MindYourBusiness, voice, null, default, false, new Random(6), out var outlaw));
        Assert.Equal("gcs_dhail_mbid_trader_ill", outlaw.Single());
    }
}
