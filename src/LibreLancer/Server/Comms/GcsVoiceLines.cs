using System;
using System.Collections.Generic;
using LibreLancer.Data.GameData;
using LibreLancer.Data.Schema.Missions;

namespace LibreLancer.Server.Comms;

public enum ChatterEvent
{
    /// <summary>Trader hails a station/gate: "[faction] [desig] [n]-[n], requesting dock".</summary>
    DockRequest,
    /// <summary>ATC reply after a dock request.</summary>
    DockGranted,
    /// <summary>Patrol spots a hostile contact.</summary>
    EnemySighted,
    /// <summary>Wing leader orders the attack.</summary>
    OrderEngage,
    /// <summary>Ship is being hit.</summary>
    TakingDamage,
    /// <summary>Ship lands hits on its target.</summary>
    InflictingDamage,
    /// <summary>Ship is destroyed (death scream).</summary>
    Death,
    /// <summary>Ship breaks off and runs because of damage.</summary>
    Fleeing,
    /// <summary>Combat has ended, no hostiles left.</summary>
    AllClear,
    /// <summary>Civilian brush-off when the player crowds a ship.</summary>
    MindYourBusiness
}

/// <summary>
/// Assembles original-Freelancer GCS voice line sequences from the segment
/// clips shipped in voices_space_male/female.ini. Phrases follow the stock
/// grammar encoded in the data itself:
///   callsign  = gcs_refer_faction_*_short + gcs_refer_formationdesig_NN
///               + gcs_misc_number_N + gcs_misc_number_N
///   continuation segments end in '+', phrase-final segments end in '-'.
/// Every returned segment is guaranteed to exist in the speaking voice, so
/// the audio sequence always matches what the data can actually say.
/// </summary>
public static class GcsVoiceLines
{
    // formation_desig IDS values map onto gcs_refer_formationdesig_NN clips;
    // 197808 is the first designation ("Alpha") in the stock resource table.
    private const int FormationDesigFirstIds = 197808;
    private const int FormationDesigClipCount = 29;
    private const int CallsignNumberMax = 15;

    public readonly record struct Callsign(int DesigClip, int Number1, int Number2)
    {
        public static Callsign Generate(Faction? faction, Random random)
        {
            var desig = 1;
            var range = faction?.Properties?.FormationDesig ?? default;
            if (range.Max >= range.Min && range.Max > 0)
            {
                var ids = random.Next(range.Min, range.Max + 1);
                desig = Math.Clamp(ids - FormationDesigFirstIds + 1, 1, FormationDesigClipCount);
            }
            else
            {
                desig = random.Next(1, FormationDesigClipCount + 1);
            }
            return new Callsign(desig, random.Next(1, CallsignNumberMax + 1), random.Next(1, CallsignNumberMax + 1));
        }
    }

    /// <summary>
    /// Builds the segment sequence for an event. Returns false when the voice
    /// cannot speak the phrase (missing clips) — callers simply stay silent,
    /// they never play a wrong or partial sequence.
    /// </summary>
    public static bool TryBuild(
        ChatterEvent evt,
        Voice voice,
        Faction? faction,
        Callsign callsign,
        bool legalFaction,
        Random random,
        out string[] segments)
    {
        segments = [];
        var parts = new List<string>(6);
        switch (evt)
        {
            case ChatterEvent.DockRequest:
                // Stock dock hail: full callsign, then the rising "requesting
                // permission to dock" clip (request_NN ends in '+' by design).
                if (!AppendCallsign(parts, voice, faction, callsign))
                    return false;
                if (!AppendVariant(parts, voice, "gcs_dockrequest_request_", random))
                    return false;
                break;
            case ChatterEvent.DockGranted:
                if (!AppendVariant(parts, voice, "gcs_dockrequest_granted_", random))
                    return false;
                break;
            case ChatterEvent.EnemySighted:
                if (!AppendVariant(parts, voice, "gcs_combat_announce_enemysighted_", random))
                    return false;
                break;
            case ChatterEvent.OrderEngage:
                if (!AppendVariant(parts, voice, "gcs_combat_order_engage_", random))
                    return false;
                break;
            case ChatterEvent.TakingDamage:
                if (!AppendVariant(parts, voice, "gcs_combat_taking_damage_", random))
                    return false;
                break;
            case ChatterEvent.InflictingDamage:
                if (!AppendVariant(parts, voice, "gcs_combat_inflicting_damage_", random))
                    return false;
                break;
            case ChatterEvent.Death:
                if (!AppendVariant(parts, voice, "gcs_combat_scream_", random))
                    return false;
                break;
            case ChatterEvent.Fleeing:
                if (!AppendVariant(parts, voice, "gcs_combat_fleereason_damaged_", random))
                    return false;
                break;
            case ChatterEvent.AllClear:
                if (!AppendVariant(parts, voice, "gcs_combat_announce_allclear_", random))
                    return false;
                break;
            case ChatterEvent.MindYourBusiness:
                // Single fixed clip selected by trader/patrol role + legality;
                // traders use the singular "trader" form here.
                var mbid = legalFaction ? "gcs_dhail_mbid_trader_leg" : "gcs_dhail_mbid_trader_ill";
                if (!voice.Lines.ContainsKey(mbid))
                    return false;
                parts.Add(mbid);
                break;
            default:
                return false;
        }

        segments = parts.ToArray();
        return true;
    }

    private static bool AppendCallsign(List<string> parts, Voice voice, Faction? faction, Callsign callsign)
    {
        var prefix = faction?.Properties?.MsgIdPrefix;
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return false;
        }
        var factionClip = prefix + "_short";
        var desigClip = $"gcs_refer_formationdesig_{callsign.DesigClip:00}";
        // Mid-phrase numbers use the suffix-less (continuing intonation) form.
        var n1 = $"gcs_misc_number_{callsign.Number1}";
        var n2 = $"gcs_misc_number_{callsign.Number2}";
        if (!voice.Lines.ContainsKey(factionClip) || !voice.Lines.ContainsKey(desigClip) ||
            !voice.Lines.ContainsKey(n1) || !voice.Lines.ContainsKey(n2))
        {
            return false;
        }
        parts.Add(factionClip);
        parts.Add(desigClip);
        parts.Add(n1);
        parts.Add(n2);
        return true;
    }

    /// <summary>
    /// Picks a random numbered permutation ("prefix01-", "prefix02+", ...) of
    /// a clip family from what this voice actually contains.
    /// </summary>
    private static bool AppendVariant(List<string> parts, Voice voice, string prefix, Random random)
    {
        List<string>? found = null;
        foreach (var key in voice.Lines.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                (found ??= []).Add(key);
            }
        }
        if (found == null)
        {
            return false;
        }
        parts.Add(found[random.Next(found.Count)]);
        return true;
    }
}
