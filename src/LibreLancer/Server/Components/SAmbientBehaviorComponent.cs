using System;
using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Data.GameData.World;
using LibreLancer.Data.Schema.Missions;
using LibreLancer.Server.Ai;
using LibreLancer.Server.Comms;
using LibreLancer.World;
using LibreLancer.World.Components;

namespace LibreLancer.Server.Components;

/// <summary>
/// Server-side behaviour for ambient population traffic, approximating the
/// original Freelancer encounter behaviours:
///  - wander: cruise between points inside the spawn zone,
///  - patrol_path: fly a looping route around the zone at full throttle,
///  - trade: pick a dockable target (base, jump gate or tradelane ring), fly
///    there and dock. Docking despawns the ship the same way vanilla traffic
///    leaves the area; tradelane rings carry the ship across the system.
/// Combat stays with SNPCComponent's state-graph AI: while the NPC has a
/// selected hostile target or a foreign directive this component goes idle.
/// </summary>
public sealed class SAmbientBehaviorComponent : GameComponent
{
    private const double TradeRetrySeconds = 240.0;
    private const float TradeMinTargetDistance = 3000f;
    private const float TradeMaxTargetDistance = 120_000f;

    private readonly Random random;
    private readonly EncounterBehavior behavior;
    private readonly Vector3 center;
    private readonly float radius;

    /// <summary>What kind of traffic this ship is (patrols answer assist calls, traders do not).</summary>
    public EncounterBehavior Behavior => behavior;

    private const float FleeHealthFraction = 0.65f;
    private const double FleeDurationSeconds = 45.0;

    private Vector3[]? patrolRoute;
    private int patrolIndex;
    private bool patrolStarted;
    private double repathTimer;
    private double tradeTimer;
    private double fleeTimer;

    public SAmbientBehaviorComponent(GameObject parent, EncounterBehavior behavior, Vector3 center, float radius, int seed)
        : base(parent)
    {
        this.behavior = behavior;
        this.center = center;
        this.radius = MathF.Max(radius, 800f);
        random = new Random(seed);
        repathTimer = random.NextDouble() * 2.0;
    }

    public override void Update(double time, GameWorld world)
    {
        if (!Parent.Flags.HasFlag(GameObjectFlags.Exists))
        {
            return;
        }

        if (!Parent.TryGetComponent<AutopilotComponent>(out var autopilot))
        {
            return;
        }

        if (Parent.Formation != null && Parent.Formation.LeadShip != Parent)
        {
            // Formation followers steer via FormationBehavior; if the leader
            // is gone (killed or despawned at dock) break off and take over.
            if (Parent.Formation.LeadShip.Flags.HasFlag(GameObjectFlags.Exists))
            {
                return;
            }
            Parent.Formation.Remove(Parent);
            Parent.Formation = null;
            autopilot.Cancel();
        }

        Parent.TryGetComponent<SNPCComponent>(out var npc);
        if (npc != null && npc.MissionRuntime != null)
        {
            return;
        }

        // Mission/attack directives own the ship; our own dock orders are the
        // only directive kind this component manages itself.
        if (npc?.CurrentDirective != null && npc.CurrentDirective is not AiDockState)
        {
            return;
        }

        var threat = Parent.GetComponent<SelectedTargetComponent>()?.Selected;

        // Civilian traffic under fire breaks for safety instead of slugging
        // it out: cancel the current errand, run flat out and call it in.
        if (behavior == EncounterBehavior.trade && threat != null && fleeTimer <= 0 &&
            Parent.TryGetComponent<SHealthComponent>(out var health) &&
            health.MaxHealth > 0 && health.CurrentHealth / health.MaxHealth < FleeHealthFraction)
        {
            fleeTimer = FleeDurationSeconds;
            npc?.SetState(null, world);
            var away = Parent.WorldTransform.Position - threat.WorldTransform.Position;
            if (away.LengthSquared() < 1f)
            {
                away = Vector3.UnitX;
            }
            autopilot.GotoVec(Parent.WorldTransform.Position + Vector3.Normalize(away) * 25_000f, GotoKind.Goto, 1f, 500f);
            Parent.GetComponent<SChatterComponent>()?.Say(ChatterEvent.Fleeing);
            return;
        }

        if (fleeTimer > 0)
        {
            fleeTimer -= time;
            return;
        }

        // Don't fight the combat state graph for the controls.
        if (threat != null)
        {
            return;
        }

        switch (behavior)
        {
            case EncounterBehavior.trade when npc != null:
                UpdateTrade(time, world, npc, autopilot);
                break;
            case EncounterBehavior.patrol_path:
                UpdatePatrol(autopilot);
                break;
            default:
                UpdateWander(time, autopilot);
                break;
        }
    }

    private void UpdateWander(double time, AutopilotComponent autopilot)
    {
        repathTimer -= time;
        if (repathTimer > 0)
        {
            return;
        }
        if (autopilot.CurrentBehavior != AutopilotBehaviors.None)
        {
            return;
        }
        repathTimer = 25.0 + random.NextDouble() * 20.0;
        autopilot.GotoVec(RandomPoint(), GotoKind.Goto, 0.9f, 250f);
    }

    private void UpdatePatrol(AutopilotComponent autopilot)
    {
        if (autopilot.CurrentBehavior != AutopilotBehaviors.None)
        {
            return;
        }
        patrolRoute ??= BuildPatrolRoute();
        if (patrolStarted)
        {
            patrolIndex = (patrolIndex + 1) % patrolRoute.Length;
        }
        patrolStarted = true;
        autopilot.GotoVec(patrolRoute[patrolIndex], GotoKind.Goto, 1f, 300f);
    }

    private void UpdateTrade(double time, GameWorld world, SNPCComponent npc, AutopilotComponent autopilot)
    {
        tradeTimer -= time;
        if (npc.CurrentDirective is AiDockState && tradeTimer > 0)
        {
            return;
        }
        if (tradeTimer > 0 && autopilot.CurrentBehavior != AutopilotBehaviors.None)
        {
            return;
        }

        var target = PickDockTarget(world);
        if (target == null)
        {
            // Nothing to dock with in range; behave like local traffic.
            npc.SetState(null, world);
            UpdateWander(time, autopilot);
            return;
        }

        tradeTimer = TradeRetrySeconds;
        npc.SetState(new AiDockState(target, GotoKind.Goto), world);

        // The vanilla dock exchange: trader hails with a full callsign, then
        // traffic control answers a few seconds later.
        if (Parent.GetComponent<SChatterComponent>()?.Say(ChatterEvent.DockRequest) == true)
        {
            var server = world.Server;
            var requester = Parent;
            server?.DelayAction(
                () => server.Chatter.TrySendAtc(target, ChatterEvent.DockGranted, requester),
                3.0 + dispatcherJitter.NextDouble() * 2.0);
        }
    }

    private static readonly Random dispatcherJitter = new();

    private GameObject? PickDockTarget(GameWorld world)
    {
        var myPos = Parent.WorldTransform.Position;
        List<GameObject> candidates = [];
        foreach (var obj in world.Objects)
        {
            if (!obj.TryGetComponent<SDockableComponent>(out var dock))
            {
                continue;
            }
            if (dock.Action.Kind != DockKinds.Base &&
                dock.Action.Kind != DockKinds.Jump &&
                dock.Action.Kind != DockKinds.Tradelane)
            {
                continue;
            }
            var dist = Vector3.Distance(obj.WorldTransform.Position, myPos);
            if (dist < TradeMinTargetDistance || dist > TradeMaxTargetDistance)
            {
                continue;
            }
            candidates.Add(obj);
        }

        return candidates.Count == 0 ? null : candidates[random.Next(candidates.Count)];
    }

    private Vector3[] BuildPatrolRoute()
    {
        // A loose ring around the zone, flown at cruise speed where legs are
        // long enough — reads like the patrol loops vanilla flies on paths.
        var points = new Vector3[5];
        var startAngle = random.NextSingle() * MathF.Tau;
        var routeRadius = radius * (0.55f + random.NextSingle() * 0.25f);
        for (var i = 0; i < points.Length; i++)
        {
            var angle = startAngle + MathF.Tau * i / points.Length;
            var y = (random.NextSingle() - 0.5f) * radius * 0.12f;
            points[i] = center + new Vector3(MathF.Cos(angle) * routeRadius, y, MathF.Sin(angle) * routeRadius);
        }
        return points;
    }

    private Vector3 RandomPoint()
    {
        var dir = new Vector3(
            random.NextSingle() * 2f - 1f,
            (random.NextSingle() * 2f - 1f) * 0.18f,
            random.NextSingle() * 2f - 1f);
        if (dir.LengthSquared() < 0.001f)
        {
            dir = Vector3.UnitX;
        }
        dir = Vector3.Normalize(dir);
        var distance = radius * (0.20f + random.NextSingle() * 0.75f);
        return center + dir * distance;
    }
}
