// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using LibreLancer.Data;
using LibreLancer.Data.GameData;
using LibreLancer.Data.GameData.Items;
using LibreLancer.Data.GameData.World;
using DataSchemaEncounter = LibreLancer.Data.Schema.Universe.Encounter;
using LibreLancer.Missions;
using LibreLancer.Net;
using LibreLancer.Net.Protocol;
using LibreLancer.Physics;
using LibreLancer.Resources;
using LibreLancer.Data.Schema.Missions;
using LibreLancer.Server.Comms;
using LibreLancer.Server.Components;
using LibreLancer.World;
using LibreLancer.World.Components;

namespace LibreLancer.Server
{
    public class ServerWorld
    {
        public Dictionary<Player, GameObject> Players = new();
        private ConcurrentQueue<Action> actions = new();
        public GameWorld GameWorld;
        public GameServer Server;
        public StarSystem System;
        public NPCManager NPCs;
        public ChatterDispatcher Chatter = null!;
        private Random debrisRandom = new();
        private object _idLock = new();

        public NetIDGenerator IdGenerator = new();
        private UpdatePacker packer = new();
        private ConcurrentQueue<(Action, double)> delayedActions = new();
        private bool paused = false;
        private Dictionary<GameObject, List<string>> solarDestroyedHardpoints = new();

        public bool Paused => paused;

        public void Pause()
        {
            EnqueueAction(() => paused = true);
        }

        public void Resume()
        {
            EnqueueAction(() => paused = false);
        }

        public ServerWorld(StarSystem system, GameServer server)
        {
            Server = server;
            System = system;
            GameWorld = new GameWorld(null, server.Resources, () => server.TotalTime);
            GameWorld.Server = this;
            GameWorld.LoadSystem(system, server.Resources, null, true);
            GameWorld.Physics!.OnCollision += PhysicsOnCollision;
            NPCs = new NPCManager(this);
            Chatter = new ChatterDispatcher(this);
            SpawnInitialPopulationEncounters();
        }

        private readonly Random populationRandom = new();
        private Dictionary<string, EncounterIni?>? encounterCache;
        private int ambientNpcIndex = 0;

        private EncounterIni? GetEncounterIni(string nickname)
        {
            encounterCache ??= new Dictionary<string, EncounterIni?>(StringComparer.OrdinalIgnoreCase);
            if (encounterCache.TryGetValue(nickname, out var cached))
            {
                return cached;
            }

            var ep = System.EncounterParameters.FirstOrDefault(x =>
                x.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(ep.SourceFile))
            {
                encounterCache[nickname] = null;
                return null;
            }

            var path = ep.SourceFile;
            if (!Server.GameData.Items.VFS.FileExists(path))
            {
                var prefixed = Server.GameData.Items.Ini.Freelancer.DataPath + path;
                if (Server.GameData.Items.VFS.FileExists(prefixed))
                {
                    path = prefixed;
                }
            }

            if (!Server.GameData.Items.VFS.FileExists(path))
            {
                FLLog.Warning("Encounter", $"Encounter file '{ep.SourceFile}' for '{nickname}' not found");
                encounterCache[nickname] = null;
                return null;
            }

            try
            {
                cached = new EncounterIni(path, Server.GameData.Items.VFS);
            }
            catch (Exception e)
            {
                FLLog.Warning("Encounter", $"Unable to load encounter '{nickname}' from '{path}': {e.Message}");
                cached = null;
            }

            encounterCache[nickname] = cached;
            return cached;
        }

        private Faction? PickEncounterFaction(DataSchemaEncounter encounter)
        {
            if (encounter.FactionSpawns.Count == 0)
            {
                return System.LocalFaction;
            }

            var total = encounter.FactionSpawns.Sum(x => MathF.Max(0, x.Chance));
            var pick = total > 0 ? populationRandom.NextSingle() * total : 0;
            foreach (var spawn in encounter.FactionSpawns)
            {
                pick -= MathF.Max(0, spawn.Chance);
                if (pick <= 0 || total == 0)
                {
                    return Server.GameData.Items.Factions.Get(spawn.Faction) ?? System.LocalFaction;
                }
            }

            return Server.GameData.Items.Factions.Get(encounter.FactionSpawns[^1].Faction) ?? System.LocalFaction;
        }

        private CostumeEntry? PickSpaceCostume(Faction? faction)
        {
            var costumes = faction?.Properties?.SpaceCostume;
            if (costumes == null || costumes.Count == 0)
            {
                return null;
            }

            var costume = costumes[populationRandom.Next(costumes.Count)];
            return new CostumeEntry([costume.Head, costume.Body, costume.Extra], Server.GameData.Items);
        }

        private Vector3 RandomPointInZone(Zone zone)
        {
            var size = zone.Size;
            switch (zone.Shape)
            {
                case ShapeKind.Sphere:
                {
                    var dir = new Vector3(
                        populationRandom.NextSingle() * 2f - 1f,
                        populationRandom.NextSingle() * 2f - 1f,
                        populationRandom.NextSingle() * 2f - 1f);
                    if (dir.LengthSquared() < 0.001f) dir = Vector3.UnitX;
                    dir = Vector3.Normalize(dir);
                    var distance = size.X * MathF.Pow(populationRandom.NextSingle(), 1f / 3f) * 0.75f;
                    return zone.Position + dir * distance;
                }
                case ShapeKind.Box:
                {
                    var local = new Vector3(
                        (populationRandom.NextSingle() - 0.5f) * size.X,
                        (populationRandom.NextSingle() - 0.5f) * size.Y,
                        (populationRandom.NextSingle() - 0.5f) * size.Z);
                    return zone.Position + Vector3.TransformNormal(local, zone.RotationMatrix);
                }
                case ShapeKind.Ellipsoid:
                {
                    var local = new Vector3(
                        (populationRandom.NextSingle() * 2f - 1f) * size.X * 0.5f,
                        (populationRandom.NextSingle() * 2f - 1f) * size.Y * 0.5f,
                        (populationRandom.NextSingle() * 2f - 1f) * size.Z * 0.5f);
                    return zone.Position + Vector3.TransformNormal(local, zone.RotationMatrix);
                }
                default:
                {
                    var radius = MathF.Max(1000f, MathF.Max(size.X, size.Z));
                    var angle = populationRandom.NextSingle() * MathF.Tau;
                    var y = (populationRandom.NextSingle() - 0.5f) * MathF.Max(100f, size.Y);
                    return zone.Position + new Vector3(MathF.Cos(angle) * radius * 0.5f, y, MathF.Sin(angle) * radius * 0.5f);
                }
            }
        }

        private float ZoneWanderRadius(Zone zone)
        {
            var s = zone.Size;
            return MathF.Max(1000f, MathF.Max(s.X, MathF.Max(s.Y, s.Z)) * 0.65f);
        }

        private const int AmbientSystemBudget = 36;
        private readonly List<GameObject> ambientShips = new();
        private double ambientRepopTimer = 45.0;

        private void SpawnInitialPopulationEncounters()
        {
            // Golden captures need an empty, reproducible system: ambient
            // traffic is random per run and would diff every screenshot.
            if (SiriusAutoplay.GoldenDir != null)
            {
                return;
            }

            var totalSpawned = 0;

            foreach (var zone in System.Zones.Where(z => z.Encounters is { Length: > 0 } && z.Density > 0)
                         .OrderByDescending(z => z.Density))
            {
                if (totalSpawned >= AmbientSystemBudget)
                {
                    break;
                }

                totalSpawned += SpawnZoneEncounters(zone, AmbientSystemBudget - totalSpawned);
            }

            if (totalSpawned > 0)
            {
                FLLog.Info("Encounter", $"Spawned {totalSpawned} ambient NPC ships in {System.Nickname}");
            }
        }

        private int SpawnZoneEncounters(Zone zone, int budget)
        {
            var spawned = 0;
            var zoneBudget = Math.Clamp(zone.MaxBattleSize > 0 ? zone.MaxBattleSize : Math.Max(1, zone.Density / 3), 1, 4);
            foreach (var enc in zone.Encounters!)
            {
                if (spawned >= budget || zoneBudget <= 0)
                {
                    break;
                }

                var chance = enc.Chance <= 0 ? 1f : enc.Chance;
                if (chance > 1f) chance /= 100f;
                chance = Math.Clamp(chance, 0.05f, 1f);
                if (populationRandom.NextSingle() > chance)
                {
                    continue;
                }

                var faction = PickEncounterFaction(enc);
                if (faction == null)
                {
                    continue;
                }

                var ini = GetEncounterIni(enc.Archetype);
                if (ini == null)
                {
                    continue;
                }

                var info = EncounterHandler.CreateEncounter(ini, enc.Difficulty, faction, Server.GameData.Items);
                var group = new List<GameObject>();
                foreach (var entry in info.Ships)
                {
                    if (spawned >= budget || zoneBudget <= 0)
                    {
                        break;
                    }

                    var shipArch = entry.Ship;
                    if (string.IsNullOrWhiteSpace(shipArch.Loadout) ||
                        !Server.GameData.Items.TryGetLoadout(shipArch.Loadout!, out var loadout) ||
                        loadout.Archetype == null)
                    {
                        FLLog.Warning("Encounter", $"Skipping NPC ship '{shipArch.Nickname}' without loadout");
                        continue;
                    }

                    var pos = RandomPointInZone(zone);
                    var orient = Quaternion.CreateFromAxisAngle(Vector3.UnitY, populationRandom.NextSingle() * MathF.Tau);
                    var pilot = Server.GameData.Items.GetPilot(shipArch.Pilot ?? "pilot_solar_easy");
                    var nickname = $"ambient_{System.Nickname}_{++ambientNpcIndex}";
                    var obj = NPCs.DoSpawn(
                        entry.Name,
                        nickname,
                        faction,
                        shipArch.StateGraph ?? "FIGHTER",
                        PickSpaceCostume(faction),
                        loadout,
                        pilot,
                        pos,
                        orient,
                        null,
                        0,
                        null,
                        entry.Voice);
                    group.Add(obj);
                    ambientShips.Add(obj);
                    spawned++;
                    zoneBudget--;
                }

                if (group.Count == 0)
                {
                    continue;
                }

                var seed = ambientNpcIndex * 397 ^ System.Nickname.GetHashCode();
                if (info.Behavior != EncounterBehavior.trade && group.Count > 1)
                {
                    // Patrol/wander groups fly in formation behind a leader,
                    // like vanilla encounter wings. Traders travel solo so a
                    // leader docking away does not strand its followers.
                    var lead = group[0];
                    for (var i = 1; i < group.Count; i++)
                    {
                        FormationTools.EnterFormation(group[i], lead, Vector3.Zero);
                        group[i].AddComponent(new SAmbientBehaviorComponent(
                            group[i], info.Behavior, zone.Position, ZoneWanderRadius(zone), seed + i));
                    }
                    lead.AddComponent(new SAmbientBehaviorComponent(
                        lead, info.Behavior, zone.Position, ZoneWanderRadius(zone), seed));
                }
                else
                {
                    for (var i = 0; i < group.Count; i++)
                    {
                        group[i].AddComponent(new SAmbientBehaviorComponent(
                            group[i], info.Behavior, zone.Position, ZoneWanderRadius(zone), seed + i));
                    }
                }
            }

            return spawned;
        }

        /// <summary>
        /// Combat interconnection: when a ship calls contact, nearby idle
        /// patrol/wander traffic of the same faction turns to engage the
        /// hostile. Traders never join; mission NPCs are left alone.
        /// </summary>
        public void RequestAssistance(GameObject caller, GameObject hostile)
        {
            const float AssistRange = 7000f;
            if (!caller.TryGetComponent<SNPCComponent>(out var callerNpc) || callerNpc.Faction == null)
            {
                return;
            }

            var callerPos = caller.WorldTransform.Position;
            foreach (var ship in ambientShips)
            {
                if (ship == caller || !ship.Flags.HasFlag(GameObjectFlags.Exists))
                {
                    continue;
                }
                if (!ship.TryGetComponent<SAmbientBehaviorComponent>(out var ambient) ||
                    ambient.Behavior == EncounterBehavior.trade)
                {
                    continue;
                }
                if (!ship.TryGetComponent<SNPCComponent>(out var npc) ||
                    npc.Faction != callerNpc.Faction ||
                    npc.CurrentDirective != null ||
                    npc.MissionRuntime != null)
                {
                    continue;
                }
                if (ship.GetComponent<SelectedTargetComponent>()?.Selected != null)
                {
                    continue;
                }
                if (Vector3.Distance(ship.WorldTransform.Position, callerPos) > AssistRange)
                {
                    continue;
                }
                npc.Attack(hostile, GameWorld);
            }
        }

        private bool autoplayChatterTested;

        private void AmbientRepopulationTick(double delta)
        {
            if (SiriusAutoplay.GoldenDir != null)
            {
                return;
            }

            ambientRepopTimer -= delta;
            if (ambientRepopTimer > 0)
            {
                return;
            }
            ambientRepopTimer = 40.0 + populationRandom.NextDouble() * 20.0;

            // SIRIUS_AUTOPLAY diagnostics: force one dock-request exchange from
            // the NPC nearest to the player so headless runs always exercise
            // the full chatter path (assembly -> RPC -> client audio chain).
            if (SiriusAutoplay.Enabled && !autoplayChatterTested && Players.Count > 0)
            {
                var playerPos = Players.Values.First()?.WorldTransform.Position;
                var nearest = playerPos == null ? null : ambientShips
                    .Where(s => s.Flags.HasFlag(GameObjectFlags.Exists) && s.GetComponent<SChatterComponent>() != null)
                    .OrderBy(s => Vector3.Distance(s.WorldTransform.Position, playerPos.Value))
                    .FirstOrDefault();
                if (nearest != null)
                {
                    autoplayChatterTested = true;
                    var said = nearest.GetComponent<SChatterComponent>()!.Say(ChatterEvent.DockRequest, ignoreShipCooldown: true);
                    FLLog.Info("Autoplay", $"forced chatter test from {nearest.Nickname}: {(said ? "spoken" : "silent (no clips/hearing)")}");
                    if (said)
                    {
                        // Complete the exchange like UpdateTrade does: tower
                        // answers a few seconds later, anchored to the ship.
                        var station = GameWorld.Objects.FirstOrDefault(o => o.TryGetComponent<SDockableComponent>(out _));
                        if (station != null)
                        {
                            DelayAction(() => Chatter.TrySendAtc(station, ChatterEvent.DockGranted, nearest), 3.5);
                        }
                    }
                }
            }

            ambientShips.RemoveAll(x => !x.Flags.HasFlag(GameObjectFlags.Exists));
            if (ambientShips.Count >= AmbientSystemBudget)
            {
                return;
            }

            var zones = System.Zones.Where(z => z.Encounters is { Length: > 0 } && z.Density > 0).ToArray();
            if (zones.Length == 0)
            {
                return;
            }

            // Top the population back up gradually, a couple of ships per tick,
            // mimicking vanilla's continuous arrivals after traffic docks away.
            var zone = zones[populationRandom.Next(zones.Length)];
            SpawnZoneEncounters(zone, Math.Min(2, AmbientSystemBudget - ambientShips.Count));
        }

        private void PhysicsOnCollision(PhysicsObject? obja, PhysicsObject? objb)
        {
            if (obja == null || objb == null) // Asteroid collision
            {
                return;
            }

            if (obja.Tag is not GameObject g1 || objb.Tag is not GameObject g2)
            {
                return;
            }

            if (g1.Kind == GameObjectKind.Missile)
            {
                var msl = g1.GetComponent<SMissileComponent>();

                if (msl?.Target == g2)
                {
                    ExplodeMissile(g1);
                }
            }
            else if (g2.Kind == GameObjectKind.Missile)
            {
                var msl = g2.GetComponent<SMissileComponent>();

                if (msl?.Target == g1)
                {
                    ExplodeMissile(g2);
                }
            }
        }

        public void StartTractor(GameObject obj, GameObject target)
        {
            foreach (var p in Players)
            {
                p.Key.RpcClient.StartTractor(obj, target);
            }
        }

        public void OnCloak(GameObject obj)
        {
            foreach (var p in Players)
            {
                p.Key.RpcClient.Cloak(obj);
            }
        }

        public void OnUncloak(GameObject obj)
        {
            foreach (var p in Players)
            {
                p.Key.RpcClient.Uncloak(obj);
            }
        }


        public void PickupObject(GameObject obj, GameObject pickup)
        {
            if (!pickup.Flags.HasFlag(GameObjectFlags.Exists) ||
                !pickup.TryGetComponent<LootComponent>(out var loot))
            {
                return;
            }

            if (obj.TryGetComponent<AbstractCargoComponent>(out var cargo))
            {
                var newLoot = new List<BasicCargo>();
                int totalRemain = 0;
                int totalCount = 0;

                foreach (var c in loot.Cargo)
                {
                    var remaining = c.Count - cargo.TryAdd(c.Item, c.Count);
                    totalCount += c.Count;
                    totalRemain += remaining;

                    if (remaining > 0)
                    {
                        newLoot.Add(new BasicCargo(c.Item, remaining, c.Hardpoint));
                    }
                }

                if (totalRemain == totalCount)
                {
                    if (obj.TryGetComponent<SPlayerComponent>(out var player))
                    {
                        player.Player.RpcClient.TractorFailed();
                    }
                }
                else if (totalRemain == 0)
                {
                    RemoveSpawnedObject(pickup, false);

                    // Notify mission system that loot has been acquired after removal
                    if (obj.TryGetComponent<SPlayerComponent>(out var playerComponent))
                    {
                        actions.Enqueue(() =>
                            Server.LocalPlayer?.MissionRuntime?.LootAcquired(pickup.Nickname!, "Player"));
                    }
                }
                else
                {
                    loot.Cargo = newLoot;

                    foreach (var p in Players)
                    {
                        p.Key.RpcClient.UpdateLootObject(pickup,
                            loot.Cargo.Select(x => new NetBasicCargo(x.Item.CRC, x.Count)).ToArray());
                    }
                }
            }
        }

        public void EndTractor(GameObject obj, GameObject target)
        {
            foreach (var p in Players)
            {
                p.Key.RpcClient.EndTractor(obj, target);
            }
        }

        public void ExplodeMissile(GameObject obj)
        {
            if ((obj.Flags & GameObjectFlags.Exists) == 0)
            {
                return;
            }

            var missile = obj.GetComponent<SMissileComponent>();
            var pos = obj.LocalTransform.Position;
            obj.Unregister(GameWorld);
            GameWorld.RemoveObject(obj);
            updatingObjects.Remove(obj);
            IdGenerator.Free(obj.NetID);
            foreach (var p in Players)
            {
                p.Key.RpcClient.DestroyMissile(obj.NetID, true);
            }

            if (missile?.Missile.Explosion == null)
            {
                return;
            }

            foreach (var other in GameWorld.Physics!.SphereTest(pos, missile.Missile.Explosion.Radius))
            {
                if (other?.Tag is GameObject g && g.TryGetComponent<SHealthComponent>(out var health))
                {
                    var shield = g.GetFirstChildComponent<SShieldComponent>();
                    var shieldDamageModifier = shield is null
                        ? 1f
                        : missile.Missile.GetShieldDamageModifier(shield.Equip.Def.ShieldType);
                    health.DamageExplosion(missile.Missile.Explosion.HullDamage, missile.Missile.Explosion.EnergyDamage,
                            missile.Owner, pos, missile.Missile.Explosion.Radius, shieldDamageModifier);
                    health.OnProjectileHit(missile.Owner);
                }
            }
        }

        public void LaunchComplete(GameObject obj)
        {
            if (!string.IsNullOrWhiteSpace(obj.Nickname))
            {
                Server.LocalPlayer?.MissionRuntime?.LaunchComplete(obj.Nickname);
            }
        }

        public JumperNpc[] GatherJumpers()
        {
            var msn = Server.LocalPlayer?.MissionRuntime;

            if (msn == null)
            {
                return [];
            }

            var jumpers = new List<JumperNpc>();

            foreach (var npc in msn.Script.Ships.Values)
            {
                if (!npc.Jumper)
                {
                    continue;
                }

                var go = GameWorld.GetObject(npc.Nickname);

                if (go == null)
                {
                    continue;
                }

                jumpers.Add(JumperNpc.FromGameObject(go));
                RemoveSpawnedObject(go, false);
            }

            return jumpers.ToArray();
        }

        public bool TryScanCargo(GameObject obj, [MaybeNullWhen(false)] out NetLoadout ld)
        {
            if (obj.TryGetComponent<ShipComponent>(out var ship))
            {
                ld = new NetLoadout
                {
                    Items = [],
                    ArchetypeCrc = ship.Ship.CRC
                };
            }
            else
            {
                ld = null;
                return false;
            }

            int id = 1;

            foreach (var item in obj.GetComponents<EquipmentComponent>())
            {
                ld.Items.Add(item.GetDescription(id++));
            }

            foreach (var item in obj.GetChildComponents<EquipmentComponent>())
            {
                ld.Items.Add(item.GetDescription(id++));
            }

            if (obj.TryGetComponent<AbstractCargoComponent>(out var cargo))
            {
                ld.Items.AddRange(cargo.GetCargo(id));
            }

            return true;
        }

        private ObjectSpawnInfo BuildSpawnInfo(GameObject obj, GameObject self)
        {
            var info = new ObjectSpawnInfo
            {
                ID = new ObjNetId(obj.NetID),
                Nickname = obj.Nickname
            };

            if (obj.Name is not LootName)
            {
                info.Name = obj.Name;
            }

            var tr = obj.WorldTransform;
            info.Position = tr.Position;
            info.Orientation = tr.Orientation;

            if ((obj.Flags & GameObjectFlags.Hidden) == GameObjectFlags.Hidden)
            {
                info.Flags |= ObjectSpawnFlags.Hidden;
            }

            if (obj.TryGetComponent<SRepComponent>(out var rep))
            {
                var r = rep.GetRep(self);
                info.Affiliation = rep.Faction?.CRC ?? 0;

                if (r == RepAttitude.Friendly)
                {
                    info.Flags |= ObjectSpawnFlags.Friendly;
                }

                if (r == RepAttitude.Hostile)
                {
                    info.Flags |= ObjectSpawnFlags.Hostile;
                }
            }

            if (obj.TryGetComponent<SDockableComponent>(out var dock))
            {
                info.Dock = dock.Action;
            }

            info.DestroyedParts = obj.Model!.DestroyedParts.ToArray();
            // Fuse effects
            info.Effects = [];

            if (obj.TryGetComponent<SFuseRunnerComponent>(out var fuse)
                && fuse.Effects.Count > 0)
            {
                info.Effects = fuse.Effects.ToArray();
            }

            // Set comm data
            if (obj.TryGetComponent<SNPCComponent>(out var npc))
            {
                info.CommHead = npc.CommHead?.CRC ?? 0;
                info.CommBody = npc.CommBody?.CRC ?? 0;
                info.CommHelmet = npc.CommHelmet?.CRC ?? 0;
            }

            // Actual loadout
            info.Loadout = new NetLoadout();
            info.Loadout.Items = [];

            if (obj.TryGetComponent<SDebrisComponent>(out var debris))
            {
                info.Flags |= ObjectSpawnFlags.Debris;

                if (debris.Solar)
                {
                    info.Flags |= ObjectSpawnFlags.Solar;
                }

                info.Loadout.ArchetypeCrc = debris.Archetype;
                info.DebrisPart = debris.Part;
            }
            else if (obj.TryGetComponent<ShipComponent>(out var ship))
            {
                info.Loadout.ArchetypeCrc = ship.Ship.CRC;
            }
            else if (obj.Kind == GameObjectKind.Solar)
            {
                info.Flags |= ObjectSpawnFlags.Solar;
                info.Loadout.ArchetypeCrc = FLHash.CreateID(obj.ArchetypeName!);
            }
            else if (obj.Kind == GameObjectKind.Loot)
            {
                info.Flags |= ObjectSpawnFlags.Loot;
                info.Loadout.ArchetypeCrc = FLHash.CreateID(obj.ArchetypeName!);
            }
            else
            {
                // Shouldn't occur
                throw new InvalidOperationException("BuildSpawnInfo called on non-archetype object");
            }

            if (obj.TryGetComponent<LootComponent>(out var l))
            {
                foreach (var item in l.Cargo)
                {
                    info.Loadout.Items.Add(new NetShipCargo(0, item.Item.CRC, null, 255, item.Count));
                }
            }

            if (obj.TryGetComponent<SHealthComponent>(out var health))
            {
                info.Loadout.Health = health.CurrentHealth;
            }

            foreach (var item in obj.GetComponents<EquipmentComponent>())
            {
                info.Loadout.Items.Add(item.GetDescription());
            }

            foreach (var item in obj.GetChildComponents<EquipmentComponent>())
            {
                info.Loadout.Items.Add(item.GetDescription());
            }

            return info;
        }

        public int PlayerCount;

        public GameObject SpawnPlayer(Player player, Vector3 position, Quaternion orientation)
        {
            player.VisitSystem(System);
            Interlocked.Increment(ref PlayerCount);
            var obj = new GameObject(player.Character!.Ship!, Server.Resources, false, true);
            foreach (var item in player.Character.Items.Where(x => !string.IsNullOrEmpty(x.Hardpoint)))
                EquipmentObjectManager.InstantiateEquipment(obj, Server.Resources, null, EquipmentType.Server,
                    item.Hardpoint, item.Equipment!);
            obj.AddComponent(new SPlayerComponent(player, obj));
            obj.AddComponent(new WeaponControlComponent(obj));
            obj.AddComponent(new SHealthComponent(obj)
            {
                CurrentHealth = player.Character.Ship!.Hitpoints,
                MaxHealth = player.Character.Ship.Hitpoints
            });
            obj.AddComponent(new SFuseRunnerComponent(obj) { DamageFuses = player.Character.Ship.Fuses });
            obj.AddComponent(new ShipPhysicsComponent(obj, player.Character.Ship));
            obj.AddComponent(new SDestroyableComponent(obj, this));

            if (player == Server.LocalPlayer)
            {
                obj.Nickname = "Player"; // HACK: Set local player ID for mission script
            }
            obj.Name = new ObjectName(player.Name);
            obj.NetID = player.ID;
            obj.Flags |= GameObjectFlags.Player | GameObjectFlags.Important;
            GameWorld.AddObject(obj);
            obj.Register(GameWorld);
            FLLog.Debug("Server", $"Spawning player with rotation {orientation}");
            obj.SetLocalTransform(new Transform3D(position, orientation));
            int objSpawn = 0;
            var allComplexSpawns = new ObjectSpawnInfo[Players.Count + spawnedObjects.Count];

            foreach (var p in Players)
            {
                allComplexSpawns[objSpawn++] = BuildSpawnInfo(p.Value, obj);
                p.Key.RpcClient.SpawnObjects([BuildSpawnInfo(obj, p.Value)]);
            }

            Players[player] = obj;

            foreach (var spawned in spawnedObjects)
            {
                allComplexSpawns[objSpawn++] = BuildSpawnInfo(spawned, obj);
            }

            player.RpcClient.SpawnObjects(allComplexSpawns);
            // Already destroyed cargo pods. TODO: loot
            foreach (var so in solarDestroyedHardpoints)
            {
                foreach (var hp in so.Value)
                {
                    player.RpcClient.DestroyEquipment(so.Key, false, hp);
                }
            }
            foreach (var o in withAnimations)
                UpdateAnimations(o, player);
            updatingObjects.Add(obj);
            return obj;
        }

        public void SpawnJumpers(string target, JumperNpc[] jumpers)
        {
            foreach (var j in jumpers)
            {
                NPCs.SpawnJumper(j, Server.LocalPlayer?.MissionRuntime!, target);
            }
        }

        public void EffectSpawned(GameObject obj)
        {
            foreach (var p in Players)
            {
                p.Key.RpcClient.UpdateEffects(obj, obj.GetComponent<SFuseRunnerComponent>()!.Effects.ToArray());
            }
        }

        public void ProjectileHit(GameObject obj, GameObject? child, Vector3 hitPoint, GameObject owner, MunitionEquip munition)
        {
            if (obj.TryGetComponent<SHealthComponent>(out var health))
            {
                var shield = obj.GetFirstChildComponent<SShieldComponent>();
                var shieldDamageModifier = shield is null
                    ? 1f
                    : munition.GetShieldDamageModifier(shield.Equip.Def.ShieldType);
                health.Damage(munition.Def.HullDamage, munition.Def.EnergyDamage, owner, child, shieldDamageModifier);
                health.OnProjectileHit(owner);
            }
        }

        private static bool MissionDockAllowed(Player player, GameObject dock, DockKinds kind)
        {
            if (player.MPlayer == null)
            {
                return true;
            }

            var hash = dock.NicknameCRC;
            if (kind == DockKinds.Tradelane)
            {
                return player.MPlayer.CanTl != 0 ||
                       player.MPlayer.TlExceptions.Any(x => x.ItemA.Hash == hash || x.ItemB.Hash == hash);
            }

            return player.MPlayer.CanDock != 0 ||
                   player.MPlayer.DockExceptions.Any(x => x.Hash == hash);
        }

        public void RequestDock(Player player, ObjNetId id)
        {
            actions.Enqueue(() =>
            {
                var obj = Players[player];
                FLLog.Info("Server", $"{player.Name} requested dock at {id}");
                var dock = GetObject(id);

                if (dock == null)
                {
                    FLLog.Warning("Server", $"Dock object {id} does not exist.");
                }
                else
                {
                    var component = dock.GetComponent<SDockableComponent>();

                    if (component == null)
                    {
                        FLLog.Warning("Server", $"object {dock.Nickname} is not dockable.");
                    }
                    else if (player.MPlayer != null && player.MPlayer.LockedGates.Contains(unchecked((int)dock.NicknameCRC)))
                    {
                        FLLog.Warning("Server", $"{player.Name} attempted to dock at locked object {dock.Nickname}");
                    }
                    else if (!MissionDockAllowed(player, dock, component.Action.Kind))
                    {
                        FLLog.Warning("Server", $"{player.Name} is not allowed to dock at {dock.Nickname}");
                    }
                    else
                    {
                        component.StartDock(obj, 0);
                    }
                }
            });
        }

        public void DelayAction(Action action, double delay)
        {
            delayedActions.Enqueue((action, Server.TotalTime + delay));
        }

        public void EnqueueAction(Action a)
        {
            actions.Enqueue(a);
        }

        public void FireMissile(Transform3D transform, MissileEquip missile, float muzzleVelocity, GameObject owner,
            GameObject? target)
        {
            actions.Enqueue(() =>
            {
                var go = new GameObject(missile.ModelFile!.LoadFile(Server.Resources)!, Server.Resources, false, true);
                go.SetLocalTransform(transform);
                go.Kind = GameObjectKind.Missile;
                go.NetID = IdGenerator.Allocate();
                go.PhysicsComponent?.Mass = 1;
                go.AddComponent(new SMissileComponent(go, missile, target, owner,
                    owner.PhysicsComponent!.Body.LinearVelocity.Length() + muzzleVelocity));

                GameWorld.AddObject(go);
                go.Register(GameWorld);
                updatingObjects.Add(go);

                foreach (var p in Players)
                {
                    p.Key.RpcClient.SpawnMissile(go.NetID, p.Value != owner, missile.CRC, transform.Position,
                        transform.Orientation);
                }
            });
        }

        public void FireProjectiles(ProjectileFireCommand projectiles, Player owner)
        {
            actions.Enqueue(() =>
            {
                if (!Players.TryGetValue(owner, out var go))
                {
                    FLLog.Debug("Server", "Dead/unavailable player attempted fire");
                    return;
                }

                if (go.TryGetComponent<WeaponControlComponent>(out var wo))
                {
                    int tgtUnique = 0;

                    for (int i = 0; i < wo.NetOrderWeapons!.Length; i++)
                    {
                        if ((projectiles.Guns & (1UL << i)) == 0)
                        {
                            continue;
                        }

                        var target = projectiles.Target;

                        if ((projectiles.Unique & (1UL << i)) != 0)
                        {
                            target = projectiles.OtherTargets[tgtUnique++];
                        }

                        if (!wo.NetOrderWeapons[i].Fire(target, GameWorld))
                        {
                            FLLog.Debug("Server", $"Request failed firing {wo.NetOrderWeapons[i].Parent.Attachment}");
                        }
                    }
                }
            });
        }

        public GameObject? GetObject(ObjNetId id) => id.Value == 0 ? null : GameWorld.GetObject(id);

        public void FireMissiles(MissileFireCmd[] missiles, Player owner)
        {
            actions.Enqueue(() =>
            {
                var go = Players[owner];

                foreach (var m in missiles)
                {
                    var x = go.Children.FirstOrDefault(c =>
                        m.Hardpoint.Equals(c.Attachment?.Name, StringComparison.OrdinalIgnoreCase));

                    if (x?.TryGetComponent<MissileLauncherComponent>(out var ml) ?? false)
                    {
                        ml.Fire(Vector3.Zero, GameWorld, GetObject(m.Target));
                    }
                }
            });
        }

        public void ActivateLane(GameObject obj, bool left)
        {
            foreach (var p in Players)
            {
                p.Key.RpcClient.TradelaneActivate(obj.NicknameCRC, left);
            }
        }

        public void DeactivateLane(GameObject obj, bool left)
        {
            foreach (var p in Players)
            {
                p.Key.RpcClient.TradelaneDeactivate(obj.NicknameCRC, left);
            }
        }

        private void UpdateAnimations(GameObject obj, Player player)
        {
            player.RpcClient.UpdateAnimations(obj, obj.AnimationComponent!.Serialize().ToArray());
        }

        private List<GameObject> withAnimations = [];

        public void StartAnimation(GameObject obj)
        {
            if (!withAnimations.Contains(obj))
            {
                withAnimations.Add(obj);
            }

            foreach (var p in Players)
                UpdateAnimations(obj, p.Key);
        }

        private void RemoveObjectInternal(GameObject obj)
        {
            obj.Unregister(GameWorld);
            GameWorld.RemoveObject(obj);
            withAnimations.Remove(obj);
            updatingObjects.Remove(obj);
        }

        public void RemovePlayer(Player player, bool exploded)
        {
            actions.Enqueue(() =>
            {
                RemoveObjectInternal(Players[player]);
                Players.Remove(player);

                foreach (var p in Players)
                {
                    p.Key.Despawn(player.ID, exploded);
                }

                Interlocked.Decrement(ref PlayerCount);
            });
        }

        public void RemoveSpawnedObject(GameObject obj, bool exploded)
        {
            actions.Enqueue(() =>
            {
                RemoveObjectInternal(obj);
                spawnedObjects.Remove(obj);
                IdGenerator.Free(obj.NetID);
                foreach (var p in Players) p.Key.Despawn(obj.NetID, exploded);
            });
        }

        public void InputsUpdate(Player player, InputUpdatePacket input)
        {
            actions.Enqueue(() =>
            {
                if (Players.TryGetValue(player, out var p))
                {
                    var phys = p.GetComponent<SPlayerComponent>()!;
                    phys.QueueInput(input, GameWorld);
                }
            });
        }

        private List<GameObject> updatingObjects = [];
        private List<GameObject> spawnedObjects = [];

        public GameObject SpawnSolar(string nickname, Archetype arch, string loadout, Faction rep, Vector3 position,
            Quaternion orientation, int idsName = 0, string? dockWith = null)
        {
            var gameobj = new GameObject(arch, null, Server.Resources, false)
            {
                ArchetypeName = arch.Nickname,
                NetID = IdGenerator.Allocate()
            };

            if (idsName != 0)
            {
                gameobj.Name = new ObjectName(idsName);
            }

            gameobj.SetLocalTransform(new Transform3D(position, orientation));
            gameobj.Nickname = nickname;
            gameobj.AddComponent(new SSolarComponent(gameobj) { Faction = rep });

            ObjectLoadout? solarLoadout = null;
            if (!string.IsNullOrWhiteSpace(loadout))
            {
                Server.GameData.Items.TryGetLoadout(loadout, out solarLoadout);
            }
            solarLoadout ??= arch.Loadout;
            if (solarLoadout != null)
                gameobj.SetLoadout(solarLoadout, Server.Resources, null);

            if (!string.IsNullOrWhiteSpace(dockWith))
            {
                var act = new DockAction() { Kind = DockKinds.Base, Target = dockWith };
                gameobj.AddComponent(new SDockableComponent(gameobj, act, arch.DockSpheres.ToArray()));
            }

            if (arch.Hitpoints > 0)
            {
                gameobj.AddComponent(new SHealthComponent(gameobj)
                    { CurrentHealth = arch.Hitpoints, MaxHealth = arch.Hitpoints });
                gameobj.AddComponent(new SDestroyableComponent(gameobj, this));
            }

            GameWorld.AddObject(gameobj);
            gameobj.Register(GameWorld);
            spawnedObjects.Add(gameobj);
            updatingObjects.Add(gameobj);

            foreach (var p in Players)
            {
                p.Key.RpcClient.SpawnObjects([BuildSpawnInfo(gameobj, p.Value)]);
            }

            return gameobj;
        }

        public void SpawnLoot(
            LootCrateEquipment crate,
            Equipment good,
            int count,
            Transform3D transform,
            string? nickname = null,
            Vector3? initialImpulse = null)
        {
            actions.Enqueue(() =>
            {
                var model = crate.ModelFile!.LoadFile(Server.Resources)!;
                var go = new GameObject(model, Server.Resources, false)
                {
                    Kind = GameObjectKind.Loot,
                    PhysicsComponent =
                    {
                        Mass = crate.Mass
                    },
                    NetID = IdGenerator.Allocate(),
                    ArchetypeName = crate.Nickname,
                    Nickname = nickname ?? ""
                };
                go.SetLocalTransform(transform);
                GameWorld.AddObject(go);
                updatingObjects.Add(go);
                go.Register(GameWorld);
                go.PhysicsComponent.Body.SetDamping(0.5f, 0.2f);
                if (initialImpulse.HasValue)
                    go.PhysicsComponent.Body.Impulse(initialImpulse.Value);
                spawnedObjects.Add(go);
                go.AddComponent(new SHealthComponent(go)
                    { MaxHealth = crate.Hitpoints, CurrentHealth = crate.Hitpoints });
                go.AddComponent(new SDestroyableComponent(go, this));
                var lt = new LootComponent(go);
                lt.Cargo.Add(new BasicCargo(good, count));
                go.AddComponent(lt);

                // Spawn debris
                foreach (var p in Players)
                {
                    p.Key.RpcClient.SpawnObjects([BuildSpawnInfo(go, p.Value)]);
                }
            });
        }

        public void SpawnDebris(
            GameObjectKind kind,
            string archetype,
            string part,
            Transform3D transform,
            GameObject[] children,
            uint[] destroyedParts,
            float mass,
            Vector3 initialForce
        )
        {
            actions.Enqueue(() =>
            {
                ModelResource src;
                List<SeparablePart> sep;

                if (kind == GameObjectKind.Ship)
                {
                    var ship = Server.GameData.Items.Ships.Get(archetype);
                    sep = ship!.SeparableParts;
                    src = ship.ModelFile!.LoadFile(Server.Resources)!;
                }
                else
                {
                    var solar = Server.GameData.Items.Archetypes.Get(archetype);
                    sep = solar!.SeparableParts;
                    src = solar.ModelFile!.LoadFile(Server.Resources)!;
                }

                var collider = src.Collision;
                var mdl = ((IRigidModelFile) src.Drawable).CreateRigidModel(false, Server.Resources);
                var newmodel = mdl.Parts[part].CloneAsRoot(mdl);
                var id = IdGenerator.Allocate();
                var go = new GameObject(newmodel, collider, part, mass, false)
                {
                    Model =
                    {
                        SeparableParts = sep
                    }
                };

                foreach (var p in destroyedParts)
                {
                    go.DisableCmpPart(p, GameWorld, Server.Resources, out _);
                }

                go.NetID = id;
                go.SetLocalTransform(transform);
                var sepInfo = sep.FirstOrDefault(x => x.Part.Equals(part, StringComparison.OrdinalIgnoreCase));
                var lifetime = debrisRandom.Next(
                    sepInfo?.DebrisType?.Lifetime ?? new ValueRange<float>(30, 30));
                FLLog.Debug("Server", $"Spawn debris of {archetype}:{part} with lifetime {lifetime}");
                go.AddComponent(new SDebrisComponent(
                    go,
                    kind == GameObjectKind.Solar,
                    FLHash.CreateID(archetype),
                    CrcTool.FLModelCrc(part),
                    lifetime));

                // re-parent children
                foreach (var c in children)
                {
                    if (go.Model.TryGetHardpoint(c.Attachment!.Name, out var newHp))
                    {
                        c.Attachment = newHp;
                        c.Parent = go;
                        go.Children.Add(c);
                    }
                }

                GameWorld.AddObject(go);
                updatingObjects.Add(go);
                go.Register(GameWorld);
                go.PhysicsComponent!.Body.Impulse(initialForce);
                go.PhysicsComponent.Body.SetDamping(0.5f, 0.2f);
                spawnedObjects.Add(go);

                // Spawn debris
                foreach (var p in Players)
                {
                    p.Key.RpcClient.SpawnObjects([BuildSpawnInfo(go, p.Value)]);
                }
            });
        }

        public void OnNPCSpawn(GameObject obj)
        {
            obj.NetID = IdGenerator.Allocate();
            GameWorld.AddObject(obj);
            obj.Register(GameWorld);
            updatingObjects.Add(obj);
            spawnedObjects.Add(obj);

            foreach (var p in Players)
            {
                p.Key.RpcClient.SpawnObjects([BuildSpawnInfo(obj, p.Value)]);
            }
        }

        public void PartDisabled(GameObject obj, uint part)
        {
            foreach (Player p in Players.Keys)
                p.RpcClient.DestroyPart(obj, part);
        }

        public void EquipmentDestroyed(GameObject obj, Hardpoint hardpoint)
        {
            foreach (Player p in Players.Keys)
            {
                p.RpcClient.DestroyEquipment(obj, true, hardpoint.Name);
            }
            // Save destroyed cargo pods
            if (obj.SystemObject != null)
            {
                if (!solarDestroyedHardpoints.TryGetValue(obj, out var list))
                {
                    list = [];
                    solarDestroyedHardpoints[obj] = list;
                }
                list.Add(hardpoint.Name);
            }
            // Remove from SHealthComponent
            var health = obj.GetComponent<SHealthComponent>()!;
            health.EquipmentHealths.Remove(hardpoint);
        }

        public void LocalChatMessage(Player player, BinaryChatMessage message)
        {
            actions.Enqueue(() =>
            {
                var pObj = Players[player];
                player.RpcClient.ReceiveChatMessage(ChatCategory.Local, BinaryChatMessage.PlainText(player.Name),
                    message);

                foreach (var obj in GameWorld.SpatialLookup.GetNearbyObjects(pObj,
                             pObj.LocalTransform.Position, 15000))
                {
                    if (obj.TryGetComponent<SPlayerComponent>(out var other))
                    {
                        other.Player.RpcClient.ReceiveChatMessage(ChatCategory.Local,
                            BinaryChatMessage.PlainText(player.Name + ": "), message);
                    }
                }
            });
        }

        public uint CurrentTick { get; private set; }

        private double noPlayersTime;
        private double maxNoPlayers = 2.0;

        private bool goldenTeleported;
        private double goldenSpawnTime = -1;

        // SIRIUS_TELEPORT="x,y,z,yawDegrees" pins an arbitrary autoplay pose
        // for effect screenshots; the golden gate keeps its hardcoded pose.
        private static readonly Vector4? teleportOverride = ParseTeleportOverride();

        private static Vector4? ParseTeleportOverride()
        {
            var value = Environment.GetEnvironmentVariable("SIRIUS_TELEPORT");
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            var parts = value.Split(',');
            if (parts.Length != 4)
            {
                return null;
            }
            var inv = global::System.Globalization.CultureInfo.InvariantCulture;
            const global::System.Globalization.NumberStyles style =
                global::System.Globalization.NumberStyles.Float;
            return float.TryParse(parts[0], style, inv, out var x) &&
                   float.TryParse(parts[1], style, inv, out var y) &&
                   float.TryParse(parts[2], style, inv, out var z) &&
                   float.TryParse(parts[3], style, inv, out var yaw)
                ? new Vector4(x, y, z, yaw)
                : null;
        }

        public bool Update(double delta, double totalTime, uint currentTick)
        {
            // Avoid locks during Update
            CurrentTick = currentTick;

            // Golden captures: pin the player to a fixed director's pose on
            // the SERVER - it owns the authoritative transform, a client-side
            // teleport is overwritten on the next tick. Facing planet
            // Manhattan: a static subject (tradelane gate sections animate
            // with a per-run phase).
            if ((SiriusAutoplay.GoldenDir != null || (SiriusAutoplay.Enabled && teleportOverride != null))
                && Players.Count > 0)
            {
                // Key off the player's arrival in SPACE, not absolute server
                // time: load times differ per backend and an early teleport
                // gets overwritten by the launch sequence.
                if (goldenSpawnTime < 0)
                {
                    goldenSpawnTime = totalTime;
                }
                if (totalTime - goldenSpawnTime < 3.0)
                {
                    goto skipTeleport;
                }
                // Re-pin EVERY tick: idle physics drift (engine wash,
                // damping) shifts the hull a few pixels per run otherwise.
                var ship = Players.Values.First();
                var pose = teleportOverride is { } pin
                    ? new Transform3D(
                        new Vector3(pin.X, pin.Y, pin.Z),
                        Quaternion.CreateFromAxisAngle(Vector3.UnitY, pin.W * MathF.PI / 180f))
                    : new Transform3D(
                        new Vector3(-33000, 500, -28000),
                        Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI));
                ship.SetLocalTransform(pose);
                if (ship.PhysicsComponent?.Body != null)
                {
                    ship.PhysicsComponent.Body.SetTransform(pose);
                    ship.PhysicsComponent.Body.LinearVelocity = Vector3.Zero;
                    ship.PhysicsComponent.Body.AngularVelocity = Vector3.Zero;
                }
                if (!goldenTeleported)
                {
                    goldenTeleported = true;
                    FLLog.Info("Autoplay", "golden: server teleported player to director pose");
                }
            }
            skipTeleport: ;

            while (actions.Count > 0 && actions.TryDequeue(out var act))
            {
                act();
            }

            while (delayedActions.Count > 0 && delayedActions.TryPeek(out var delayedAct)
                                            && delayedAct.Item2 <= Server.TotalTime)
            {
                if (delayedActions.TryDequeue(out delayedAct))
                {
                    delayedAct.Item1();
                }
            }

            // pause
            if (paused)
            {
                return true;
            }

            AmbientRepopulationTick(delta);

            // Update
            NPCs.FrameStart();
            GameWorld.Update(delta);

            // projectiles
            if (GameWorld.Projectiles!.HasQueued)
            {
                var queue = GameWorld.Projectiles.GetSpawnQueue();

                foreach (var p in Players)
                {
                    p.Key.RpcClient.SpawnProjectiles(queue);
                }
            }

            // Network update tick
            SendWorldUpdates(currentTick);
            UpdateDebugInfo();

            // Despawn after 2 seconds of nothing
            if (PlayerCount == 0)
            {
                noPlayersTime += delta;
                return (noPlayersTime < maxNoPlayers);
            }
            else
            {
                noPlayersTime = 0;
                return true;
            }
        }

        private void UpdateDebugInfo()
        {
            if (Server is { LocalPlayer: not null, SendDebugInfo: true } &&
                Players.TryGetValue(Server.LocalPlayer, out var go) &&
                go.Flags.HasFlag(GameObjectFlags.Exists))
            {
                var pc = go.GetComponent<SPlayerComponent>();

                if (pc?.SelectedObject != null && pc.SelectedObject.TryGetComponent<SNPCComponent>(out var npc))
                {
                    Server.ReportDebugInfo(npc.GetDebugInfo());
                }
            }
        }

        private IEnumerable<GameObject> GetUpdatingObjects()
        {
            foreach (var obj in updatingObjects) yield return obj;

            foreach (var obj in GameWorld.Objects)
            {
                if (obj.SystemObject == null)
                {
                    continue;
                }
                if (obj.TryGetComponent<SSolarComponent>(out var solar))
                {
                    if (solar.SendSolarUpdate || solar.SendPartsUpdate)
                    {
                        yield return obj;
                    }
                }
            }
        }

        // This could do with some work
        private void SendWorldUpdates(uint tick)
        {
            // Update players
            foreach (var player in Players)
            {
                var tr = player.Value.WorldTransform;
                player.Key.Position = tr.Position;
                player.Key.Orientation = tr.Orientation;
            }

            // Fetch data
            var toUpdate = GetUpdatingObjects().ToArray();
            var allUpdates = new ObjectUpdate[toUpdate.Length];

            for (int i = 0; i < toUpdate.Length; i++)
            {
                // Get main object update fields
                var obj = toUpdate[i];
                var update = new ObjectUpdate
                {
                    ID = new ObjNetId(obj.NetID)
                };

                if (obj.SystemObject == null)
                {
                    // Don't send pos/orient of system objects, client doesn't read it.
                    var tr = obj.WorldTransform;
                    update.Position = new(tr.Position);
                    update.Orientation = tr.Orientation;
                }


                if (obj.PhysicsComponent != null)
                {
                    update.LinearVelocity = new(obj.PhysicsComponent.Body.LinearVelocity);
                    update.AngularVelocity = new(obj.PhysicsComponent.Body.AngularVelocity);
                }

                if (obj.TryGetComponent<SEngineComponent>(out var eng))
                {
                    update.ThrottleFloat = eng.Speed;
                    update.EngineKill = eng.EngineKill;
                }

                if (obj.TryGetComponent<ShipPhysicsComponent>(out var objPhysics))
                {
                    switch (objPhysics.EngineState)
                    {
                        case EngineStates.CruiseCharging:
                            update.CruiseThrust = CruiseThrustState.CruiseCharging;
                            break;
                        case EngineStates.Cruise:
                            update.CruiseThrust = CruiseThrustState.Cruising;
                            break;
                        case EngineStates.Standard when objPhysics.ThrustEnabled:
                            update.CruiseThrust = CruiseThrustState.Thrusting;
                            break;
                    }
                }

                if (obj.TryGetComponent<SHealthComponent>(out var health))
                {
                    update.Hull = (int) health.CurrentHealth;
                    var sh = obj.GetFirstChildComponent<SShieldComponent>();

                    if (sh != null)
                    {
                        update.Shield = (int) sh.Health;
                    }
                    if (health.EquipmentHealths.Count > 0)
                    {
                        update.DamagedParts = health.EquipmentHealths
                            .Select(x => new PartHealth(FLHash.CreateID(x.Key.Name), (byte)(x.Value * 255f)))
                            .ToArray();
                    }
                }

                if (obj.TryGetComponent<WeaponControlComponent>(out var weapons))
                {
                    update.Guns = weapons.GetRotations() ?? [];
                }

                allUpdates[i] = update;
            }

            // Send data to players
            var pk = packer.Begin(allUpdates, toUpdate);

            foreach (var player in Players)
            {
                var phealthcomponent = player.Value.GetComponent<SHealthComponent>();
                var phealth = phealthcomponent!.CurrentHealth;
                var pshieldComponent = player.Value.GetFirstChildComponent<SShieldComponent>();
                float pshield = 0;

                if (pshieldComponent != null)
                {
                    pshield = pshieldComponent.Health;
                }

                var selfPlayer = player.Value.GetComponent<SPlayerComponent>();
                var phys = player.Value.GetComponent<ShipPhysicsComponent>();
                var state = new PlayerAuthState
                {
                    Health = phealth,
                    Shield = pshield,
                    Position = player.Key.Position,
                    Orientation = player.Key.Orientation,
                    LinearVelocity = player.Value.PhysicsComponent!.Body.LinearVelocity,
                    AngularVelocity = MathHelper.ApplyEpsilon(player.Value.PhysicsComponent.Body.AngularVelocity),
                    CruiseAccelPct = phys!.CruiseAccelPct,
                    CruiseChargePct = phys.ChargePercent
                };

                if (player.Key.SinglePlayer)
                {
                    var lst = new ObjectUpdate[allUpdates.Length - 1];
                    int j = 0;

                    for (int i = 0; i < allUpdates.Length; i++)
                    {
                        if (toUpdate[i] == player.Value)
                        {
                            continue;
                        }

                        lst[j++] = allUpdates[i];
                    }

                    player.Key.SendSPUpdate(new SPUpdatePacket()
                    {
                        Tick = tick,
                        InputSequence = selfPlayer!.LatestReceived,
                        PlayerState = state,
                        Updates = lst
                    });
                }
                else
                {
#if DEBUG
                    int maxPacketSize = 500; // Min safe UDP packet size - 8
#else
                    int maxPacketSize = player.Key.Client.MaxSequencedSize;
#endif
                    player.Key.SendMPUpdate(pk.Pack(tick, state, selfPlayer!, player.Value, maxPacketSize));
                }
            }
        }

        public void Finish()
        {
            GameWorld.Dispose();
        }
    }
}
