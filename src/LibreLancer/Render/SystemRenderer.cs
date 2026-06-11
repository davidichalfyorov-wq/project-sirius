// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Data.GameData;
using LibreLancer.Data.GameData.World;
using LibreLancer.Fx;
using LibreLancer.Graphics;
using LibreLancer.Graphics.Backends.OpenGL;
using LibreLancer.Render.Materials;
using LibreLancer.Resources;
using LibreLancer.Thn;
using LibreLancer.World;

namespace LibreLancer.Render
{
    // Responsible for rendering the GameWorld.
    public class SystemRenderer : IDisposable
    {
        public ICamera Camera
        {
            get { return camera; }
            set { camera = value; }
        }

        private ICamera camera;

        public Color4 NullColor = Color4.Black;
        public Color4? BackgroundOverride;

        public GameWorld World { get; set; } = null!;
        public List<AsteroidFieldRenderer>? AsteroidFields { get; private set; }
        public List<NebulaRenderer> Nebulae { get; private set; }

        private StarSystem starSystem = null!;
        public StarSystem StarSystem => starSystem;

        // Editor Options
        public bool DrawNebulae = true;
        public bool DrawStarsphere = true;

        // Global Renderer Options
        public bool ExtraLights = false; // See comments in Draw() before enabling

        public RigidModel[] StarSphereModels;
        public Matrix4x4[] StarSphereWorlds = null!;
        public Lighting[] StarSphereLightings = null!;
        private readonly RigidModel?[] starSphereLayerModels = new RigidModel?[3];
        private StarsphereCubemapRenderer? cubemapStarspheres;
        private bool useSystemCubemapStarspheres;
        public LineRenderer DebugRenderer;
        public Action? OpaqueHook;
        public Action? PhysicsHook;
        public PolylineRender Polyline;
        public SystemLighting SystemLighting = new();
        public ParticleEffectPool FxPool;
        public BeamsBuffer Beams;
        public QuadBuffer QuadBuffer;
        public DfmDrawMode DfmMode = DfmDrawMode.Normal;
        public RenderContext RenderContext => rstate;
        private RenderContext rstate;
        private Game game;
        private Texture2D dot;

        public int ZoneVersion = 0;
        public IRendererSettings Settings;
        private Billboards billboards;
        private ResourceManager resman;

        public Game Game
        {
            get { return game; }
        }

        public Billboards Billboards
        {
            get { return billboards; }
        }

        public ResourceManager ResourceManager
        {
            get { return resman; }
        }

        public SystemRenderer(ICamera camera, GameResourceManager resources, Game game)
        {
            this.game = game;
            Settings = game.GetService<IRendererSettings>()!;
            billboards = game.GetService<Billboards>()!;
            Commands = game.GetService<CommandBuffer>()!;
            this.camera = camera;
            AsteroidFields = [];
            Nebulae = [];
            StarSphereModels = [];
            FxPool = new ParticleEffectPool(resources.GLWindow.RenderContext, Commands);
            rstate = resources.GLWindow.RenderContext;
            resman = resources;
            Polyline = new PolylineRender(rstate, Commands);
            QuadBuffer = new QuadBuffer(rstate);
            dot = (Texture2D) resources.FindTexture(ResourceManager.WhiteTextureName)!;
            DebugRenderer = new LineRenderer(rstate);
            Beams = new BeamsBuffer(resources, rstate);
        }

        public void LoadZones(IList<AsteroidField>? asteroids, IList<Nebula>? nebulae)
        {
            if (AsteroidFields != null)
            {
                foreach (var f in AsteroidFields) f.Dispose();
            }

            AsteroidFields = [];
            Nebulae = [];

            if (asteroids != null)
            {
                foreach (var field in asteroids)
                    AsteroidFields.Add(new AsteroidFieldRenderer(field, this));
            }

            if (nebulae != null)
            {
                foreach (var n in nebulae)
                    Nebulae.Add(new NebulaRenderer(n, Game, this));
            }
        }

        public void LoadStarspheres(StarSystem system)
        {
            starSystem = system;

            cubemapStarspheres ??= new StarsphereCubemapRenderer(rstate);
            cubemapStarspheres.Clear();
            Array.Clear(starSphereLayerModels, 0, starSphereLayerModels.Length);
            StarSphereWorlds = null!;
            StarSphereLightings = null!;

            useSystemCubemapStarspheres = game.GetService<GameSettings>()?.UseCubemapStarspheres ?? true;
            List<RigidModel> starSphereRenderData = [];

            AddLayer(StarsphereLayer.Basic, system.StarsBasic, system.StarsBasicCubemap);
            AddLayer(StarsphereLayer.Complex, system.StarsComplex, system.StarsComplexCubemap);
            AddLayer(StarsphereLayer.Nebula, system.StarsNebula, system.StarsNebulaCubemap);

            // Per-system IBL probe (roadmap 5.3): convolve the most
            // colourful available starsphere cubemap, ambient fallback
            // otherwise. CPU build, runs once per system load.
            SystemLighting.Ibl?.Dispose();
            SystemLighting.Ibl = null;
            if (Settings.SelectedIbl)
            {
                var loadTimer = System.Diagnostics.Stopwatch.StartNew();
                SystemLighting.Ibl = EnvironmentProbe.Build(rstate, resman,
                    system.StarsNebulaCubemap ?? system.StarsComplexCubemap ?? system.StarsBasicCubemap,
                    system.AmbientColor);
                FLLog.Info("IBL", $"Environment probe for {system.Nickname} built in {loadTimer.ElapsedMilliseconds} ms");
            }

            StarSphereModels = starSphereRenderData.ToArray();
            return;

            void AddLayer(StarsphereLayer layer, ResolvedModel? mdl, string? cubemapPath)
            {
                if (useSystemCubemapStarspheres)
                {
                    cubemapStarspheres.LoadLayer(layer, cubemapPath, resman);
                    if (cubemapStarspheres.HasLayer(layer))
                    {
                        return;
                    }
                }

                if (mdl?.LoadFile(resman)?.Drawable is not IRigidModelFile loaded)
                {
                    return;
                }

                var rigidModel = loaded.CreateRigidModel(true, resman);
                starSphereLayerModels[(int)layer] = rigidModel;
                starSphereRenderData.Add(rigidModel);
            }
        }

        /// <summary>
        /// Disables system cubemap starspheres for temporary starsphere overrides such as THN cutscenes.
        /// </summary>
        public void DisableCubemapStarspheres()
        {
            useSystemCubemapStarspheres = false;
            Array.Clear(starSphereLayerModels, 0, starSphereLayerModels.Length);
        }

        public void LoadLights(StarSystem system)
        {
            SystemLighting = new SystemLighting
            {
                Ambient = new Color4(system.AmbientColor, 1)
            };

            foreach (var lt in system.LightSources)
            {
                SystemLighting.Lights.Add(new DynamicLight() { Light = lt.Light });
            }
        }

        public void LoadSystem(StarSystem system)
        {
            LoadLights(system);
            LoadStarspheres(system);
            LoadZones(system.AsteroidFields, system.Nebulae);
        }

        public void Update(double elapsed)
        {
            foreach (var model in StarSphereModels)
            {
                model.Update(RenderClock.Get(game.TotalTime));
            }

            foreach (var field in AsteroidFields!)
            {
                field.Update(camera);
            }

            foreach (var nebula in Nebulae)
            {
                nebula.Update(elapsed);
            }

            for (var i = tempFx.Count - 1; i >= 0; i--)
            {
                tempFx[i].Render.Update(elapsed, tempFx[i].Position, Matrix4x4.CreateTranslation(tempFx[i].Position));

                if (tempFx[i].Render.Finished)
                {
                    tempFx.RemoveAt(i);
                }
            }
        }

        private Vector3[] debugPoints = [];

        public void UseDebugPoints(List<Vector3> list)
        {
            this.debugPoints = list.ToArray();
            list.Clear();
        }

        public NebulaRenderer? ObjectInNebula(Vector3 position)
        {
            for (var i = 0; i < Nebulae.Count; i++)
            {
                var n = Nebulae[i];

                if (n.Nebula.Zone?.ContainsPoint(position) ?? false)
                {
                    return n;
                }
            }

            return null;
        }

        private NebulaRenderer? CheckNebulae()
        {
            if (!DrawNebulae)
            {
                return null;
            }

            for (var i = 0; i < Nebulae.Count; i++)
            {
                var n = Nebulae[i];

                if (n.Nebula.Zone!.ContainsPoint(camera.Position))
                {
                    return n;
                }
            }

            return null;
        }

        private MultisampleTarget? msaa;
        private HdrFramePipeline? hdrPipeline;
        private ShadowMapRenderer? shadowMaps;
        private int _mwidth = -1, _mheight = -1;
        public CommandBuffer Commands;
        private int _twidth = -1, _theight = -1;
        private int _dwidth = -1, _dheight = -1;

        public List<ObjectRenderer> objects = new(250);

        public void AddObject(ObjectRenderer render)
        {
            objects.Add(render);
        }

        private record TemporaryFx(ParticleEffectRenderer Render, Vector3 Position);

        private List<TemporaryFx> tempFx = [];

        public void SpawnTempFx(ParticleEffect? fx, Vector3 position)
        {
            var ren = new ParticleEffectRenderer(fx)
            {
                SParam = 0,
                Active = true
            };

            tempFx.Add(new TemporaryFx(ren, position));
        }

        public bool ZOverride = false; // Stop Thn Camera from changing Z

        public unsafe void Draw(int renderWidth, int renderHeight)
        {
            if (renderWidth == 0 || renderHeight == 0)
                // Don't render on Width/Height = 0
            {
                return;
            }

            // Unified frame pipeline: the scene goes into an HDR target and
            // reaches the output through one tonemap pass; MSAA (below)
            // resolves into that target instead of the screen. The pass is
            // mandatory: the phase 2 linear workflow decodes textures at the
            // sample site, and this pass owns the matching display encode —
            // hdr=false maps grading/bloom/rays/AA off and runs it as the
            // bare pass-through encode.
            hdrPipeline ??= new HdrFramePipeline(rstate);
            hdrPipeline.Tonemapper = Settings.SelectedTonemapper;
            hdrPipeline.Exposure = Settings.SelectedExposure;
            hdrPipeline.BloomEnabled = Settings.SelectedBloom;
            hdrPipeline.BloomThreshold = Settings.SelectedBloomThreshold;
            hdrPipeline.BloomIntensity = Settings.SelectedBloomIntensity;
            hdrPipeline.BloomRadius = Settings.SelectedBloomRadius;
            hdrPipeline.BloomMips = Settings.SelectedBloomMips;
            hdrPipeline.GodRaysEnabled = Settings.SelectedGodRays;
            hdrPipeline.GodRaysIntensity = Settings.SelectedGodRaysIntensity;
            hdrPipeline.GodRaysSamples = Settings.SelectedGodRaysSamples;
            hdrPipeline.PostAa = Settings.SelectedPostAa;
            hdrPipeline.Begin(renderWidth, renderHeight);
            rstate.BeginPassTimer("scene");

            RenderTarget? restoreTarget = rstate.RenderTarget;

            if (Settings.SelectedMSAA > 0)
            {
                if (_mwidth != renderWidth || _mheight != renderHeight)
                {
                    _mwidth = renderWidth;
                    _mheight = renderHeight;
                    msaa?.Dispose();
                    msaa = new MultisampleTarget(rstate, renderWidth, renderHeight, Settings.SelectedMSAA);
                }

                rstate.PushViewport(new Rectangle(0, 0, renderWidth, renderHeight));
                rstate.PushScissor(new Rectangle(0, 0, renderWidth, renderHeight), false);
                rstate.RenderTarget = msaa;
            }

            rstate.PreferredFilterLevel = Settings.SelectedFiltering;
            rstate.AnisotropyLevel = Settings.SelectedAnisotropy;
            var nr = CheckNebulae(); // are we in a nebula?

            // Cascaded sun shadows (roadmap 5.4): render the atlas before
            // the scene camera takes over; receivers sample it via the
            // ShadowData block SetLights publishes.
            RenderMaterial.ActiveShadows = null;
            // Space scenes only: a SunRenderer marks a real system scene.
            // THN sets (bar/city backdrops) light their own way and the
            // cascade fit around their cameras self-shadows everything.
            if (Settings.SelectedShadows && HasSunRenderer() &&
                FindDirectionalLight(out var sunDirection))
            {
                shadowMaps ??= new ShadowMapRenderer(rstate, resman);
                shadowMaps.Draw(objects, SystemLighting.Lights, sunDirection, camera, resman);
                RenderMaterial.ActiveShadows = shadowMaps;
                rstate.Textures[8] = shadowMaps.AtlasTexture;
                rstate.Samplers[8] = SamplerState.PointClamp;
                rstate.Textures[9] = shadowMaps.LocalAtlasTexture;
                rstate.Samplers[9] = SamplerState.PointClamp;
            }

            rstate.SetCamera(camera);
            Commands.Camera = camera;
            var transitioned = false;

            if (nr != null)
            {
                transitioned = nr.FogTransitioned() && DrawNebulae;
            }

            rstate.DepthEnabled = true;
            Commands.BonesMax = Commands.BonesOffset = 0;
            Commands.BonesBuffer.BeginStreaming();
            QuadBuffer.BeginUpload();

            foreach (var obj in tempFx)
            {
                obj.Render.PrepareRender(camera, nr, this, false);
            }

            for (var i = 0; i < World.Objects.Count; i++)
            {
                World.Objects[i].PrepareRender(camera, nr, this);
            }

            foreach (var n in Nebulae)
                n.UploadPuffs();
            QuadBuffer.EndUpload();
            Commands.BonesBuffer.EndStreaming(Commands.BonesMax);

            if (transitioned)
            {
                // Fully in fog. Skip Starsphere
                rstate.ClearColor = nr!.Nebula.FogColor;
                rstate.ClearAll();
            }
            else
            {
                rstate.ClearColor =
                    BackgroundOverride ??
                    starSystem?.BackgroundColor ??
                    NullColor;
                rstate.ClearAll();
            }

            DebugRenderer.StartFrame(rstate);
            Commands.StartFrame(rstate);
            FxPool.StartFrame(camera);
            Polyline.StartFrame();
            rstate.DepthEnabled = true;
            // Optimisation for dictionary lookups
            LightEquipRenderer.FrameStart();
            // Clear depth buffer for game objects
            billboards.Begin(camera, Commands);
            SystemLighting.NumberOfTilesX = -1;
            // Simple depth pre-pass
            rstate.ColorWrite = false;
            rstate.DepthFunction = DepthFunction.Less;
            foreach (var obj in objects) obj.DepthPrepass(camera, rstate);
            rstate.DepthFunction = DepthFunction.LessEqual;
            rstate.ColorWrite = true;
            // Actual Drawing

            Beams.Begin(Commands, resman, camera);

            foreach (var obj in objects)
            {
                obj.Draw(camera, Commands, SystemLighting, nr!);
            }

            Beams.End();
            for (var i = 0; i < AsteroidFields!.Count; i++)
                AsteroidFields[i].Draw(resman, SystemLighting, Commands, nr!);

            if (DrawNebulae)
            {
                if (nr == null)
                {
                    foreach (var nebula in Nebulae)
                    {
                        nebula.Draw(Commands);
                    }
                }
                else
                {
                    nr.Draw(Commands);
                }
            }

            billboards.End();
            FxPool.EndFrame();
            Polyline.EndFrame();
            // Opaque Pass
            rstate.DepthEnabled = true;
            Commands.DrawOpaque(rstate);

            if ((!transitioned || !DrawNebulae) && DrawStarsphere)
            {
                // Starsphere
                rstate.DepthRange = new Vector2(1, 1);

                if (camera is ThnCamera thn && !ZOverride)
                {
                    thn.DefaultZ();
                    rstate.SetCamera(thn);
                }

                DrawStarsphereLayers();

                if (camera is ThnCamera thn2 && !ZOverride)
                {
                    thn2.CameraZ();
                    rstate.SetCamera(thn2);
                }

                if (nr != null && DrawNebulae)
                {
                    nr.RenderFogTransition(rstate);
                }

                rstate.DepthRange = new Vector2(0, 1);
            }

            OpaqueHook?.Invoke();
            // Transparent Pass
            rstate.DepthWrite = false;
            Commands.DrawTransparent(rstate);
            rstate.DepthWrite = true;
            rstate.DepthEnabled = true;
            PhysicsHook?.Invoke();

            foreach (var point in debugPoints)
            {
                var lX = point + new Vector3(5, 0, 0);
                var lmX = point + new Vector3(-5, 0, 0);
                var lY = point + new Vector3(0, -5, 0);
                var lmY = point + new Vector3(0, 5, 0);
                var lZ = point + new Vector3(0, 0, 5);
                var lmZ = point + new Vector3(0, 0, -5);
                DebugRenderer.DrawLine(lX, lmX, Color4.Red);
                DebugRenderer.DrawLine(lY, lmY, Color4.Red);
                DebugRenderer.DrawLine(lZ, lmZ, Color4.Red);
            }

            debugPoints = [];
            DebugRenderer.Render();

            if (Settings.SelectedMSAA > 0)
            {
                rstate.PopViewport();
                rstate.PopScissor();

                if (restoreTarget == null)
                {
                    msaa?.BlitToScreen(new Point(rstate.CurrentViewport.X, rstate.CurrentViewport.Y));
                }
                else
                {
                    msaa?.BlitToRenderTarget((restoreTarget as RenderTarget2D)!);
                }

                rstate.RenderTarget = restoreTarget;
            }

            rstate.EndPassTimer();
            hdrPipeline.GodRaysSun = ComputeGodRaysSun();
            hdrPipeline.End();

            if (debugBrdfLut)
            {
                // Reference look: red->yellow gradient falling off along Y.
                var list = rstate.Renderer2D.CreateDrawList();
                list.DrawImageStretched(IblResources.GetBrdfLut(rstate),
                    new Rectangle(8, renderHeight - 136, 128, 128), Color4.White);
                list.Render();
            }

            rstate.DepthEnabled = true;
            objects.Clear();
        }

        private bool HasSunRenderer()
        {
            foreach (var obj in objects)
            {
                if (obj is SunRenderer)
                {
                    return true;
                }
            }
            return false;
        }

        private bool FindDirectionalLight(out Vector3 direction)
        {
            foreach (var light in SystemLighting.Lights)
            {
                if (light.Light.Kind == LightKind.Directional && light.Active)
                {
                    direction = Vector3.Normalize(light.Light.Direction);
                    return true;
                }
            }
            direction = Vector3.UnitY;
            return false;
        }

        private static readonly bool debugBrdfLut =
            Environment.GetEnvironmentVariable("SIRIUS_DEBUG_BRDF") == "1";

        /// <summary>
        /// Projects the dominant visible sun for the god rays pass:
        /// (uv.x, uv.y, uv radius, 1), or W &lt;= 0 when no sun applies
        /// (behind the camera / far off screen) - roadmap 4.6.
        /// </summary>
        private Vector4 ComputeGodRaysSun()
        {
            var best = new Vector4(0, 0, 0, -1);
            var bestDistance = float.MaxValue;
            foreach (var obj in objects)
            {
                if (obj is not SunRenderer sun)
                    continue;
                var clip = Vector4.Transform(new Vector4(sun.WorldPosition, 1), camera.ViewProjection);
                if (clip.W <= 0)
                    continue; // behind the camera
                var uv = new Vector2(clip.X, clip.Y) / clip.W * 0.5f + new Vector2(0.5f);
                if (uv.X < -0.5f || uv.X > 1.5f || uv.Y < -0.5f || uv.Y > 1.5f)
                    continue; // too far off screen to contribute
                var centerDistance = (uv - new Vector2(0.5f)).Length();
                if (centerDistance >= bestDistance)
                    continue;
                bestDistance = centerDistance;
                // Radius in v units (the mask shader works in aspect-corrected uv).
                var radius = sun.Sun.Radius * camera.Projection.M22 / clip.W * 0.5f;
                best = new Vector4(uv.X, uv.Y, MathF.Max(radius, 1e-4f), 1);
            }
            return best;
        }


        private void DrawStarsphereLayers()
        {
            if (useSystemCubemapStarspheres && cubemapStarspheres is { AllLoaded: true })
            {
                cubemapStarspheres.DrawComposite(camera, game.TotalTime, rstate.CurrentViewport);
                return;
            }

            if (useSystemCubemapStarspheres && cubemapStarspheres is { AnyLoaded: true })
            {
                for (var i = 0; i < 3; i++)
                {
                    var layer = (StarsphereLayer)i;
                    if (cubemapStarspheres.HasLayer(layer))
                    {
                        cubemapStarspheres.DrawLayer(camera, game.TotalTime, rstate.CurrentViewport, layer);
                    }
                    else if (starSphereLayerModels[i] != null)
                    {
                        DrawRigidStarsphere(starSphereLayerModels[i]!, i);
                    }
                }

                return;
            }

            for (var i = 0; i < StarSphereModels.Length; i++)
            {
                DrawRigidStarsphere(StarSphereModels[i], i);
            }
        }

        private void DrawRigidStarsphere(RigidModel mdl, int index)
        {
            Matrix4x4 ssworld = Matrix4x4.CreateTranslation(camera.Position);

            if (StarSphereWorlds != null && index < StarSphereWorlds.Length)
            {
                ssworld = StarSphereWorlds[index] * ssworld;
            }

            var lighting = Lighting.Empty;

            if (StarSphereLightings != null && index < StarSphereLightings.Length)
            {
                lighting = StarSphereLightings[index];
            }

            // We frustum cull to save on fill rate for low end devices (pi)
            foreach (var part in mdl.AllParts)
            {
                if (!part.Active || part.Mesh == null)
                {
                    continue;
                }

                var p = part;
                var w = p.LocalTransform.Matrix() * ssworld;
                var bsphere = new BoundingSphere(Vector3.Transform(p.Mesh!.Center, w), p.Mesh.Radius);

                if (camera.FrustumCheck(bsphere))
                {
                    p.Mesh.DrawImmediate(0, resman, rstate, w, ref lighting, mdl.MaterialAnims,
                        BasicMaterial.ForceAlpha);
                }
            }
        }

        public void Dispose()
        {
            msaa?.Dispose();
            hdrPipeline?.Dispose();
            cubemapStarspheres?.Dispose();

            Polyline.Dispose();
            FxPool.Dispose();
            DebugRenderer.Dispose();
            QuadBuffer.Dispose();
            Beams.Dispose();
        }
    }
}
