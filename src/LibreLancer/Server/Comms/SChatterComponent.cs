using System;
using LibreLancer.Data.GameData;
using LibreLancer.World;

namespace LibreLancer.Server.Comms;

/// <summary>
/// Gives one NPC ship a radio personality: a voice, a persistent callsign
/// and per-ship phrase cooldowns. Game systems report events here; the
/// component assembles the GCS sequence and hands it to the dispatcher.
/// </summary>
public sealed class SChatterComponent : GameComponent
{
    private const double ShipCooldownSeconds = 18.0;

    private readonly ChatterDispatcher dispatcher;
    private readonly Voice voice;
    private readonly Faction? faction;
    private readonly bool legalFaction;
    private readonly GcsVoiceLines.Callsign callsign;
    private readonly Func<double> totalTime;
    private double lastSpoke = -100;

    public SChatterComponent(
        GameObject parent,
        ChatterDispatcher dispatcher,
        Voice voice,
        Faction? faction,
        Func<double> totalTime) : base(parent)
    {
        this.dispatcher = dispatcher;
        this.voice = voice;
        this.faction = faction;
        this.totalTime = totalTime;
        // Lawful factions get the "leg" brush-off clips, outlaws the "ill" ones.
        legalFaction = faction?.Properties?.MsgIdPrefix?.Contains("_ill", StringComparison.OrdinalIgnoreCase) != true;
        callsign = GcsVoiceLines.Callsign.Generate(faction, dispatcher.Random);
    }

    /// <summary>Speak if the phrase exists for this voice and cooldowns allow it.</summary>
    public bool Say(ChatterEvent evt, bool ignoreShipCooldown = false)
    {
        if (!Parent.Flags.HasFlag(GameObjectFlags.Exists))
        {
            return false;
        }
        if (!ignoreShipCooldown && totalTime() - lastSpoke < ShipCooldownSeconds)
        {
            return false;
        }
        if (!GcsVoiceLines.TryBuild(evt, voice, faction, callsign, legalFaction, dispatcher.Random, out var segments))
        {
            return false;
        }
        if (!dispatcher.TrySend(Parent, voice, segments))
        {
            return false;
        }
        lastSpoke = totalTime();
        return true;
    }
}
