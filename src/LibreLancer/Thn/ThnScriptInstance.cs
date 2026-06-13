// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Client.Components;
using LibreLancer.Render;
using LibreLancer.Resources;
using LibreLancer.Sounds;
using LibreLancer.Thn.Events;
using LibreLancer.Utf.Dfm;
using LibreLancer.World;
using LibreLancer.World.Components;

namespace LibreLancer.Thn
{
    public abstract class ThnEventProcessor
    {
        public abstract bool Run(double delta);
    }

    public class ThnScriptInstance
    {
        private Queue<ThnEvent> events = new();
        private List<ThnEventProcessor> processors = [];

        public double CurrentTime = 0;
        public double Duration;

        public bool Running => CurrentTime < Duration;

        public Cutscene Cutscene;

        public Dictionary<string, ThnSceneObject> Objects = [];
        public Dictionary<string, ThnSoundInstance> Sounds = new();

        private readonly ThnScript thn;

        public ThnScriptInstance(Cutscene cs, ThnScript script)
        {
            this.thn = script;
            Duration = script.Duration;
            Cutscene = cs;

            foreach (var ev in script.Events)
            {
                events.Enqueue(ev);
            }
        }

        public void AddProcessor(ThnEventProcessor ev)
        {
            processors.Add(ev);
        }

        private bool CheckObject(ThnEntity e, object? sub, EntityTypes type, string templateName)
        {
            return sub != null && type == e.Type && e.Template.Equals(templateName, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsPlayerEnginePsys(ThnEntity entity)
        {
            if (Cutscene.PlayerEngine == null || entity.Type != EntityTypes.PSys)
            {
                return false;
            }

            if (string.Equals(entity.Template, "PlayerShipEngines", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Discovery room THNs often name player engine PSys objects after concrete ships
            // (e.g. Ship_l_fighter_1_engine) instead of using the vanilla template. Keep this
            // matcher ship-scoped so ambient fire, smoke, steam and intro planet effects do not
            // accidentally get attached to the player's engine component.
            var name = entity.Name ?? string.Empty;
            var template = entity.Template ?? string.Empty;
            var source = name + " " + template;
            var looksLikeEngine = source.Contains("engine", StringComparison.OrdinalIgnoreCase) ||
                                  source.Contains("exhaust", StringComparison.OrdinalIgnoreCase) ||
                                  source.Contains("_fire", StringComparison.OrdinalIgnoreCase) ||
                                  source.Contains("fire_", StringComparison.OrdinalIgnoreCase);
            var tiedToShip = source.Contains("ship", StringComparison.OrdinalIgnoreCase) ||
                             source.StartsWith("Ship_", StringComparison.OrdinalIgnoreCase);
            return looksLikeEngine && tiedToShip;
        }

        public void ConstructEntities(Dictionary<string, ThnSceneObject> objects, bool spawnObjects)
        {
            this.Objects = objects;
            List<ThnSceneObject> monitors = [];

            foreach (var kv in thn.Entities)
            {
                if (Objects.ContainsKey(kv.Key)) continue;
                if ((kv.Value.ObjectFlags & ThnObjectFlags.Reference) == ThnObjectFlags.Reference) continue;
                var obj = new ThnSceneObject
                {
                    Name = kv.Key,
                    Translate = kv.Value.Position ?? Vector3.Zero,
                    Rotate = kv.Value.Rotation
                };

                // PlayerShip object
                if (spawnObjects && CheckObject(kv.Value, Cutscene.PlayerShip, EntityTypes.Compound, "playership"))
                {
                    obj.Object = Cutscene.PlayerShip;
                    obj.Object!.RenderComponent!.LitDynamic = (kv.Value.ObjectFlags & ThnObjectFlags.LitDynamic) ==
                                                              ThnObjectFlags.LitDynamic;
                    obj.Object.RenderComponent.LitAmbient = (kv.Value.ObjectFlags & ThnObjectFlags.LitAmbient) ==
                                                            ThnObjectFlags.LitAmbient;
                    obj.Object.RenderComponent.NoFog = kv.Value.NoFog;
                    ((ModelRenderer) obj.Object.RenderComponent).LightGroup = kv.Value.LightGroup;
                    obj.Entity = kv.Value;
                    Vector3 transform = kv.Value.Position ?? Vector3.Zero;
                    obj.Object.SetLocalTransform(new Transform3D(transform, obj.Rotate));
                    obj.HpMount = Cutscene.PlayerShip!.GetHardpoint("HpMount");
                    Cutscene.World.AddObject(obj.Object);
                    Objects.Add(kv.Key, obj);
                    continue;
                }

                if (spawnObjects && IsPlayerEnginePsys(kv.Value))
                {
                    obj.Entity = kv.Value;
                    obj.Engine = Cutscene.PlayerEngine;
                    Objects.Add(kv.Key, obj);
                    continue;
                }

                var template = kv.Value.Template;
                if (Cutscene.Substitutions != null &&
                    Cutscene.Substitutions.TryGetValue(kv.Value.Template, out var replacement))
                    template = replacement;
                var resman = Cutscene.ResourceManager;
                var gameData = Cutscene.GameData;

                if (spawnObjects && kv.Value.Type == EntityTypes.Compound)
                {
                    bool getHpMount = false;
                    // Fetch model
                    IDrawable drawable = null!;
                    float[]? lodranges = null;

                    if (!string.IsNullOrEmpty(template))
                    {
                        switch (kv.Value.MeshCategory!.ToLowerInvariant())
                        {
                            case "solar":
                                ModelResource? mr;
                                (mr, lodranges) = gameData.GetSolar(template);
                                drawable = mr!.Drawable;
                                break;
                            case "ship":
                            case "spaceship":
                                getHpMount = true;
                                var sh = gameData.Items.Ships.Get(template)!;
                                drawable = sh.ModelFile!.LoadFile(resman)!.Drawable;
                                break;
                            case "prop":
                                drawable = gameData.GetProp(template)!;
                                break;
                            case "room":
                                drawable = gameData.GetRoom(template)!;
                                break;
                            case "equipment cart":
                                drawable = gameData.GetCart(template)!;
                                break;
                            case "equipment":
                                var eq = gameData.Items.Equipment.Get(template);
                                drawable = eq?.ModelFile!.LoadFile(resman)!.Drawable!;
                                break;
                            case "asteroid":
                                var ast = gameData.Items.Asteroids.Get(template);
                                drawable = ast?.ModelFile!.LoadFile(resman)!.Drawable!;
                                break;
                            default:
                                FLLog.Warning("Thn", $"Unhandled mesh category '{kv.Value.MeshCategory}' for THN entity '{kv.Value.Name}'");
                                drawable = null;
                                break;
                        }
                    }
                    else
                    {
                        FLLog.Warning("Thn", $"object '{kv.Value.Name}' has empty template, category " +
                                             $"'{kv.Value.MeshCategory}'");
                    }

                    if (kv.Value.UserFlag != 0)
                    {
                        // This is a starsphere
                        Cutscene.AddStarsphere(drawable, obj);
                    }
                    else
                    {
                        obj.Object = new GameObject(new ModelResource(drawable, default), Cutscene.ResourceManager,
                            true, false)
                        {
                            Name = new ObjectName(kv.Value.Name)
                        };

                        if (getHpMount)
                            obj.HpMount = obj.Object.GetHardpoint("HpMount");

                        if (obj.Object.RenderComponent is ModelRenderer r)
                        {
                            r.LightGroup = kv.Value.LightGroup;
                            r.LitDynamic = (kv.Value.ObjectFlags & ThnObjectFlags.LitDynamic) ==
                                           ThnObjectFlags.LitDynamic;
                            r.LitAmbient = (kv.Value.ObjectFlags & ThnObjectFlags.LitAmbient) ==
                                           ThnObjectFlags.LitAmbient;
                            // HIDDEN just seems to be an editor flag?
                            // r.Hidden = (kv.Value.ObjectFlags & ThnObjectFlags.Hidden) == ThnObjectFlags.Hidden;
                            r.NoFog = kv.Value.NoFog;
                            r.LODRanges = lodranges;
                        }
                    }
                }
                else if (kv.Value.Type == EntityTypes.PSys)
                {
                    var fx = gameData.ResolveEffect(kv.Value.Template);

                    if (fx?.AlePath != null)
                    {
                        var effect = fx.GetEffect(resman);
                        if (effect != null)
                        {
                            obj.Object = new GameObject
                            {
                                RenderComponent = new ParticleEffectRenderer(effect) { Active = false }
                            };
                        }
                        else if (spawnObjects)
                        {
                            FLLog.Warning("Thn",
                                $"PSYS '{kv.Value.Name}' resolved '{kv.Value.Template}' but no particle effect was found");
                        }
                    }
                    else if (spawnObjects)
                    {
                        FLLog.Warning("Thn",
                            $"PSYS '{kv.Value.Name}' references missing effect '{kv.Value.Template}'");
                    }
                }
                else if (kv.Value.Type == EntityTypes.Scene)
                {
                    if (kv.Value.DisplayText != null)
                        Cutscene.SetDisplayText(kv.Value.DisplayText);

                    var amb = kv.Value.Ambient!.Value;
                    if (amb is { X: 0, Y: 0, Z: 0 }) continue;
                    Cutscene.SetAmbient(amb);
                }
                else if (kv.Value.Type == EntityTypes.Light)
                {
                    var lt = new DynamicLight
                    {
                        LightGroup = kv.Value.LightGroup,
                        Active = kv.Value.LightProps!.On,
                        Light = kv.Value.LightProps.Render
                    };
                    obj.Light = lt;
                    obj.LightDir = lt.Light.Direction;
                    lt.Light.Direction = Vector3.Transform(lt.Light.Direction, obj.Rotate);
                    if (Cutscene.Renderer != null)
                        Cutscene.Renderer.SystemLighting.Lights.Add(lt);
                }
                else if (kv.Value.Type == EntityTypes.Camera)
                {
                    obj.Camera = new ThnCameraProps();
                    obj.Camera.FovH = kv.Value.FovH ?? obj.Camera.FovH;
                    obj.Camera.AspectRatio = kv.Value.HVAspect ?? obj.Camera.AspectRatio;
                    if (kv.Value.NearPlane != null) obj.Camera.Znear = kv.Value.NearPlane.Value;
                    if (kv.Value.FarPlane != null) obj.Camera.Zfar = kv.Value.FarPlane.Value;
                }
                else if (kv.Value.Type == EntityTypes.Marker)
                {
                    if (kv.Value.MainObject && Cutscene.MainObject != null)
                    {
                        obj.Object = Cutscene.MainObject;
                        obj.PosFromObject = true;
                    }
                    else
                    {
                        obj.Object = new GameObject
                        {
                            Name = new ObjectName("Marker"),
                            Nickname = ""
                        };
                    }
                }
                else if (kv.Value.Type == EntityTypes.Deformable)
                {
                    obj.Actor = kv.Value.Actor;
                    // TODO: Hacky with fidget/placement scripts
                    if (string.IsNullOrEmpty(kv.Value.Actor) || !objects.ContainsKey(kv.Value.Actor))
                    {
                        obj.Object = new GameObject();
                        var costume = gameData.Items.Costumes.Get(template)!;

                        var skel = new DfmSkeletonManager(
                            costume.Body.LoadModel(resman)!, costume.Head?.LoadModel(resman),
                            costume.LeftHand?.LoadModel(resman), costume.RightHand?.LoadModel(resman))
                        {
                            FloorHeight = kv.Value.FloorHeight
                        };
                        obj.Object.RenderComponent = new CharacterRenderer(skel);
                        var anmComponent = new AnimationComponent(obj.Object, gameData.GetCharacterAnimations());
                        obj.Object.AnimationComponent = anmComponent;
                        obj.Object.AddComponent(anmComponent);
                    }
                    else
                    {
                        if (Objects.TryGetValue(obj.Actor, out var act))
                        {
                            act.Translate = obj.Translate;
                            act.Rotate = obj.Rotate;
                        }
                    }
                }
                else if (kv.Value.Type == EntityTypes.Sound)
                {
                    obj.Sound = new ThnSound(kv.Value.Template,
                        kv.Value.Speaker, Cutscene.SoundManager, kv.Value.AudioProps ?? new(), obj)
                    {
                        Spatial = (kv.Value.ObjectFlags & ThnObjectFlags.SoundSpatial) == ThnObjectFlags.SoundSpatial
                    };
                }
                else if (kv.Value.Type == EntityTypes.Monitor)
                {
                    monitors.Add(obj);
                }

                if (obj.Object != null)
                {
                    if (!obj.PosFromObject)
                    {
                        Vector3 transform = kv.Value.Position ?? Vector3.Zero;
                        obj.Object.SetLocalTransform(new Transform3D(transform, kv.Value.Rotation));
                        Cutscene.World.AddObject(obj.Object);
                    }
                }

                obj.Entity = kv.Value;
                Objects[kv.Key] = obj;
            }

            // Verify? This seems to work
            monitors.Sort((x, y) => string.Compare(x.Entity.Priority, y.Entity.Priority, StringComparison.Ordinal));
            for (int i = 0; i < monitors.Count; i++)
                monitors[i].MonitorIndex = i;
        }

        private Queue<ThnEvent> delaySoundEvents = new();

        public void Update(double delta)
        {
            if (CurrentTime > Duration) return;
            CurrentTime += delta;
            // Don't run sound on T=0 exactly to avoid desync
            while (delaySoundEvents.Count > 0 && CurrentTime > 0)
                delaySoundEvents.Dequeue().Run(this);

            while (events.Count > 0 && events.Peek().Time <= CurrentTime)
            {
                var ev = events.Dequeue();

                if (delta <= 0 && (ev is StartSoundEvent || ev is StartAudioPropAnimEvent))
                {
                    delaySoundEvents.Enqueue(ev);
                }
                else
                {
                    ev.Run(this);
                }
            }

            for (int i = 0; i < processors.Count; i++)
            {
                if (!processors[i].Run(delta))
                {
                    processors.RemoveAt(i);
                    i--;
                }
            }

            if (CurrentTime > Duration)
                Shutdown();
        }

        public void Shutdown()
        {
            Cutscene.OnScriptFinished(thn);
            Cleanup();
        }

        public void Cleanup()
        {
            foreach (var v in Sounds.Values)
            {
                if (v.Instance != null)
                {
                    v.Instance.Stop();
                    v.Instance = null;
                }
            }

            Sounds = new Dictionary<string, ThnSoundInstance>();
        }
    }
}
