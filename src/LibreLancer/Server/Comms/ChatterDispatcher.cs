using System;
using System.Numerics;
using LibreLancer.Data;
using LibreLancer.Data.GameData;
using LibreLancer.Net.Protocol;
using LibreLancer.World;

namespace LibreLancer.Server.Comms;

/// <summary>
/// Routes assembled GCS phrases to players. Keeps the radio believable:
/// one phrase at a time globally, per-ship cooldowns, and nothing plays if
/// no player is close enough to plausibly receive the transmission.
/// </summary>
public sealed class ChatterDispatcher(ServerWorld world)
{
    private const double GlobalCooldownSeconds = 5.0;
    private const float HearingRange = 12_000f;

    private double lastPhraseTime = -100;

    public Random Random { get; } = new();

    /// <summary>Estimated seconds the radio stays busy after the last phrase.</summary>
    public bool RadioBusy => world.Server.TotalTime - lastPhraseTime < GlobalCooldownSeconds;

    /// <summary>
    /// Station/gate traffic control reply (dock granted etc.) using the stock
    /// legal ATC voice. The dock object is the radio source, but audibility is
    /// anchored to the requesting ship — if the player heard the request, they
    /// hear the tower answer it, just like the vanilla dock exchange.
    /// </summary>
    public bool TrySendAtc(GameObject station, ChatterEvent evt, GameObject requester)
    {
        var voice = world.Server.GameData.Items.Voices.Get("atc_leg_m01")
                    ?? world.Server.GameData.Items.Voices.Get("atc_leg_f01");
        if (voice == null)
        {
            return false;
        }
        if (!GcsVoiceLines.TryBuild(evt, voice, null, default, true, Random, out var segments))
        {
            return false;
        }
        return Send(station, voice, segments, requester.WorldTransform.Position, ignoreGlobalCooldown: true);
    }

    public bool TrySend(GameObject speaker, Voice voice, string[] segments)
        => Send(speaker, voice, segments, speaker.WorldTransform.Position, ignoreGlobalCooldown: false);

    /// <param name="anchorPosition">Position used for the hearing-range check.</param>
    /// <param name="ignoreGlobalCooldown">
    /// ATC replies are part of an exchange already on the air, so they skip
    /// the global cooldown that spaces out unrelated phrases.
    /// </param>
    private bool Send(GameObject speaker, Voice voice, string[] segments, Vector3 anchorPosition, bool ignoreGlobalCooldown)
    {
        if (segments.Length == 0 || (RadioBusy && !ignoreGlobalCooldown))
        {
            return false;
        }

        var position = anchorPosition;
        var lines = new NetDlgLine[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            lines[i] = new NetDlgLine
            {
                Voice = voice.Nickname,
                Hash = FLHash.CreateID(segments[i]),
                Source = speaker.NetID,
                TargetIsPlayer = false
            };
        }

        var sent = false;
        foreach (var (player, ship) in world.Players)
        {
            if (ship == null || Vector3.Distance(ship.WorldTransform.Position, position) > HearingRange)
            {
                continue;
            }
            player.RpcClient.Chatter(lines);
            sent = true;
        }

        if (sent)
        {
            lastPhraseTime = world.Server.TotalTime;
            FLLog.Info("Chatter", $"{speaker.Nickname ?? speaker.Name?.ToString()} [{voice.Nickname}]: {string.Join(" -> ", segments)}");
        }
        return sent;
    }
}
