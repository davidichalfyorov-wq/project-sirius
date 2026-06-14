// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using LibreLancer.Data.GameData;
using LibreLancer.Data.GameData.World;
using LibreLancer.Fx;
using LibreLancer.Graphics;
using LibreLancer.Graphics.Backends.OpenGL;
using LibreLancer.Render.Materials;
using LibreLancer.Render.Volumetrics;
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

        // Motion vectors (graphics phase 0.2): previous frame's main-camera VP,
        // shifted each frame just before the opaque/G-buffer pass.
        private Matrix4x4 prevMainViewProjection = Matrix4x4.Identity;
        private bool hasPrevMainViewProjection;
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
        private float volumetricSunTransmittance = 1f;
        internal float VolumetricSunTransmittance => volumetricSunTransmittance;
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

        // Last frame delta, forwarded to auto-exposure adaptation in Draw().
        private float lastFrameSeconds = 1f / 60f;

        public void Update(double elapsed)
        {
            lastFrameSeconds = (float)elapsed;
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
        private VolumetricNebulaFrameResources? volumetricNebulaResources;
        private VolumetricAtmosphereFrameResources? volumetricAtmosphereResources;
        private readonly VolumetricShipDisplacementState volumetricShipDisplacement = new();
        private string importedDensityCacheKey = string.Empty;
        private VolumetricImportedDensityFrame importedDensityFrame;
        private ShadowMapRenderer? shadowMaps;
        private RayTracedScene? rtScene;
        private bool rtShadowsWasActive;
        private int rtDebugCountdown;
        private static readonly bool MsDebugRequested =
            Environment.GetEnvironmentVariable("SIRIUS_MS_DEBUG") == "1";

        private static readonly int RtDebugMode =
            int.TryParse(Environment.GetEnvironmentVariable("SIRIUS_RT_DEBUG"), out var m) ? m : 0;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RtDebugUniforms
        {
            public Matrix4x4 InverseViewProjection;
            public Vector4 CameraPosMode;
        }

        /// <summary>Fullscreen TLAS ray-cast overlay (SIRIUS_RT_DEBUG=1|2):
        /// silhouettes must match the raster scene - the cheapest proof the
        /// instance transforms (and the transpose) are right.</summary>
        private void DrawRtDebug(ICamera camera)
        {
            if (!Matrix4x4.Invert(camera.ViewProjection, out var inverse))
            {
                return;
            }
            var shader = Shaders.AllShaders.RTDebug.Get(1); // RT_VIEW
            var uniforms = new RtDebugUniforms
            {
                InverseViewProjection = inverse,
                CameraPosMode = new Vector4(camera.Position, Math.Clamp(RtDebugMode, 1, 3))
            };
            var oldBlend = rstate.BlendMode;
            var oldCull = rstate.Cull;
            var oldDepth = rstate.DepthEnabled;
            rstate.BlendMode = BlendMode.Opaque;
            rstate.Cull = false;
            rstate.DepthEnabled = false;
            shader.SetUniformBlock(3, ref uniforms);
            rstate.Shader = shader;
            rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
            rstate.BlendMode = oldBlend;
            rstate.Cull = oldCull;
            rstate.DepthEnabled = oldDepth;
        }

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
            hdrPipeline.AutoExposureEnabled = Settings.SelectedAutoExposure;
            hdrPipeline.AutoExpPin = Settings.SelectedAutoExposurePin;
            hdrPipeline.AutoExpCompensation = Settings.SelectedAutoExposureCompensation;
            hdrPipeline.DeltaSeconds = lastFrameSeconds;
            hdrPipeline.BloomEnabled = Settings.SelectedBloom;
            hdrPipeline.BloomThreshold = Settings.SelectedBloomThreshold;
            hdrPipeline.BloomIntensity = Settings.SelectedBloomIntensity;
            hdrPipeline.BloomRadius = Settings.SelectedBloomRadius;
            hdrPipeline.BloomMips = Settings.SelectedBloomMips;
            hdrPipeline.GodRaysEnabled = Settings.SelectedGodRays;
            hdrPipeline.GodRaysIntensity = Settings.SelectedGodRaysIntensity;
            hdrPipeline.GodRaysSamples = Settings.SelectedGodRaysSamples;
            hdrPipeline.GodRaysDensity = 0.9f;
            hdrPipeline.GodRaysDecay = 0.95f;
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
            var renderFeatures = RenderFeatureSet.FromSettings(Settings);
            NebulaVolumeProfile activeProfile = default;
            var hasActiveProfile = nr != null && NebulaVolumeProfileMapper.TryCreate(nr.Nebula, out activeProfile);
            UpdateVolumetricNebulaFrame(renderWidth, renderHeight, renderFeatures,
                hasActiveProfile ? activeProfile : null);
            if (!renderFeatures.VolumetricReprojection)
            {
                ApplyVolumetricNebulaTemporal(renderFeatures, hasActiveProfile, activeProfile);
            }
            var useVolumetricCompositeThisFrame =
                ShouldUseVolumetricCompositeThisFrame(renderFeatures, hasActiveProfile);

            rstate.SetCamera(camera);
            Commands.Camera = camera;
            // Reset material fog before the frame's transparent pass. The
            // Phase 5 volumetric path binds it again only after integrated
            // froxel data and the opt-in composite/material flags are ready.
            RenderMaterial.VolumetricFogActive = false;
            RenderMaterial.VolumetricFogMaterialActive = false;
            RenderMaterial.SetVolumetricFogSource(null, Vector4.Zero);
            UpdateVolumetricAtmosphereResources(renderFeatures, renderWidth, renderHeight);
            UpdateVolumetricGodRays(renderFeatures, hasActiveProfile, activeProfile);
            var transitioned = false;

            if (nr != null)
            {
                // Volumetrics never "transition": the starsphere stays and
                // the gas itself swallows it physically (legacy hid the sky
                // behind a fog-coloured clear once deep enough).
                transitioned = !useVolumetricCompositeThisFrame && nr.FogTransitioned() && DrawNebulae;
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

            // Cascaded sun shadows (roadmap 5.4): the atlas pass runs AFTER
            // PrepareRender fills the object list - its gate and caster walk
            // both read it (an earlier placement saw an always-empty list and
            // silently disabled shadows on the first frame's state).
            // Receivers sample it via the ShadowData block SetLights publishes.
            RenderMaterial.ActiveShadows = null;
            RenderMaterial.ShadowLight = null;
            RenderMaterial.RtShadowsActive = false;
            RenderMaterial.RtaoActive = false;
            RenderMaterial.RtReflectionsActive = false;
            // Space scenes only: a loaded star system marks a real space
            // scene (THN sets light their own way and the cascade fit around
            // their cameras self-shadows everything). NOT HasSunRenderer():
            // the client only spawns solars inside its interest radius, so
            // the sun object usually doesn't exist - the light data comes
            // from the system INI and is always present.
            if (Settings.SelectedShadows && starSystem != null &&
                rstate.HasFeature(GraphicsFeature.SceneShadows) &&
                FindShadowLight(camera.Position, out var sunDirection, out var shadowLight))
            {
                shadowMaps ??= new ShadowMapRenderer(rstate, resman);
                // RT shadows replace the sun cascades entirely - skip their
                // GPU passes but keep the matrices/splits (the RT shader
                // reuses them for its early-outs and range cap).
                var rtSunShadows = Settings.SelectedRtShadows && rstate.RayTracing != null;
                shadowMaps.Draw(objects, SystemLighting.Lights, sunDirection, camera, resman,
                    skipSunCascades: rtSunShadows);
                RenderMaterial.ActiveShadows = shadowMaps;
                RenderMaterial.ShadowLight = shadowLight;
                rstate.Textures[8] = shadowMaps.AtlasTexture;
                rstate.Samplers[8] = SamplerState.PointClamp;
                rstate.Textures[9] = shadowMaps.LocalAtlasTexture;
                rstate.Samplers[9] = SamplerState.PointClamp;
                // The caster pass leaves its own camera bound.
                rstate.SetCamera(camera);
            }

            // Ray-traced scene structures (roadmap phase 4): BLAS cache +
            // per-frame TLAS. Walks World.Objects, NOT the visible list -
            // off-screen geometry must still cast shadows / occlude rays.
            if (rstate.RayTracing is { } rayTracing &&
                (RtDebugMode > 0 || Settings.SelectedRtShadows || Settings.SelectedRtao ||
                 Settings.SelectedRtReflections))
            {
                rtScene ??= new RayTracedScene(rayTracing);
                rstate.BeginPassTimer("rt_build");
                rtScene.BeginFrame();
                const float rtRangeSq = 25000f * 25000f;
                int dbgObjects = 0, dbgModels = 0, dbgParts = 0;
                var rtOrigin = camera.Position;
                for (var oi = 0; oi < World.Objects.Count; oi++)
                {
                    var worldObj = World.Objects[oi];
                    dbgObjects++;
                    if (worldObj.RenderComponent is not ModelRenderer { Model: { } rtModel } rtMr)
                    {
                        continue;
                    }
                    if (Vector3.DistanceSquared(rtMr.World.Translation, rtOrigin) > rtRangeSq)
                    {
                        continue;
                    }
                    dbgModels++;
                    foreach (var part in rtModel.AllParts)
                    {
                        if (part.Active && part.Mesh != null)
                        {
                            dbgParts++;
                            rtScene.AddPart(part, rtMr.World);
                        }
                    }
                }
                if (rtDebugCountdown-- <= 0)
                {
                    rtDebugCountdown = 300;
                    FLLog.Info("RayTracing", $"collector: {dbgObjects} objects, {dbgModels} models, {dbgParts} parts");
                }
                rtScene.EndFrame();
                // RT shadows replace the CSM sample only when this frame
                // actually has a sun shadow pass to inherit range/params from.
                RenderMaterial.RtShadowsActive = Settings.SelectedRtShadows &&
                    RenderMaterial.ActiveShadows != null;
                RenderMaterial.RtaoActive = Settings.SelectedRtao;
                RenderMaterial.RtReflectionsActive = Settings.SelectedRtReflections;
                if (RenderMaterial.RtShadowsActive != rtShadowsWasActive)
                {
                    rtShadowsWasActive = RenderMaterial.RtShadowsActive;
                    FLLog.Info("RayTracing",
                        $"rt shadows active={rtShadowsWasActive} (setting={Settings.SelectedRtShadows}, csm={RenderMaterial.ActiveShadows != null})");
                }
                rstate.EndPassTimer();
            }


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

            // Motion vectors (graphics phase 0.2): publish the previous frame's
            // main-camera VP, then re-bind the main camera so its tag bump
            // flushes PrevViewProjection into the cbuffer for the opaque/
            // G-buffer pass. First frame uses prev=current => zero motion.
            // Runs unconditionally (independent of the shadow-pass re-bind);
            // byte-neutral when the G-buffer is off (no non-GBUFFER shader
            // reads PrevViewProjection).
            rstate.SetPrevViewProjection(
                hasPrevMainViewProjection ? prevMainViewProjection : camera.ViewProjection);
            rstate.SetCamera(camera);
            prevMainViewProjection = camera.ViewProjection;
            hasPrevMainViewProjection = true;

            // G-buffer MRT (graphics phase 0.1): opaque geometry renders
            // IMMEDIATELY inside obj.Draw / AsteroidFields.Draw
            // (CommandBuffer.AddCommand is immediate-mode) - NOT in the later
            // no-op Commands.DrawOpaque - so RT1 and the GBUFFER shader flag
            // must wrap THIS loop, or the gbuffer never captures anything.
            // Gated to non-MSAA; default off => byte-identical. Non-PBR draws
            // (beams) leave RT1 cleared; transparent draws are deferred.
            var gbufferNormal = RenderMaterial.GBufferActive && Settings.SelectedMSAA <= 0
                ? hdrPipeline.CurrentGBufferNormalTarget
                : null;
            if (gbufferNormal != null)
            {
                // RT1 = normal+roughness (SV_Target1), RT2 = view-Z
                // (SV_Target2) - order must match the shader output indices.
                rstate.SetGBufferTargets(new RenderTarget2D[]
                {
                    gbufferNormal,
                    hdrPipeline.CurrentGBufferViewZTarget!
                });
                RenderMaterial.GBufferPassActive = true;
            }

            foreach (var obj in objects)
            {
                obj.Draw(camera, Commands, SystemLighting, nr!);
            }

            Beams.End();
            for (var i = 0; i < AsteroidFields!.Count; i++)
                AsteroidFields[i].Draw(resman, SystemLighting, Commands, nr!);

            if (gbufferNormal != null)
            {
                // Nebula / starsphere / transparents render single-attachment.
                RenderMaterial.GBufferPassActive = false;
                rstate.SetGBufferTargets(null);
            }

            // The volumetric path REPLACES the legacy billboard nebula
            // (exterior puffs, fill quad, interior puffs): drawing both
            // doubled the fog with hard-edged sprite blobs on top of the
            // real gas (the "ромбы и рваные углы" report).
            if (DrawNebulae && !useVolumetricCompositeThisFrame)
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
            // Mesh-shader cube fields (roadmap 7.5): immediate dispatches
            // run here, after the opaque pass settles viewport/state.
            for (var i = 0; i < AsteroidFields!.Count; i++)
            {
                AsteroidFields[i].DrawMeshPath(resman);
            }

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

                if (nr != null && DrawNebulae && !useVolumetricCompositeThisFrame)
                {
                    // Legacy fullscreen fog tint - the volumetric composite
                    // owns the in-cloud look now.
                    nr.RenderFogTransition(rstate);
                }

                rstate.DepthRange = new Vector2(0, 1);
            }

            OpaqueHook?.Invoke();

            // With non-MSAA HDR rendering, composite the opaque scene before
            // transparents. Transparent/additive materials then sample the
            // same integrated medium instead of being fogged later using the
            // opaque depth buffer behind them. The MSAA path still resolves
            // into the HDR scene after transparents, so it keeps the legacy
            // post-transparent composite until the renderer has a depth-safe
            // pre-transparent MSAA resolve.
            var compositeBeforeTransparent = Settings.SelectedMSAA <= 0;
            if (compositeBeforeTransparent)
            {
                ApplyVolumetricNebulaSceneComposite(renderFeatures, hasActiveProfile, activeProfile);
            }

            // Transparent Pass
            var materialFogBound = BindVolumetricMaterialFog(renderFeatures, hasActiveProfile, activeProfile);
            rstate.DepthWrite = false;
            try
            {
                Commands.DrawTransparent(rstate);
            }
            finally
            {
                if (materialFogBound)
                {
                    UnbindVolumetricMaterialFog();
                }
                rstate.DepthWrite = true;
                rstate.DepthEnabled = true;
            }
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
            if (!compositeBeforeTransparent)
            {
                ApplyVolumetricNebulaSceneComposite(renderFeatures, hasActiveProfile, activeProfile);
            }
            hdrPipeline.GodRaysSun = ComputeGodRaysSun();
            hdrPipeline.GodRaysSunTransmittance = volumetricSunTransmittance;
            hdrPipeline.End();
            // G-buffer MRT visual QA (graphics phase 0.1) is drawn inside
            // End() via DrawGBufferDebug (Renderer2D fullscreen) - the raw
            // RGBA16F->LDR vkCmdBlitImage path produced display garbage.
            DrawVolumetricNebulaDebugView(renderWidth, renderHeight, renderFeatures.DebugView);


            if (debugBrdfLut)
            {
                // Reference look: red->yellow gradient falling off along Y.
                var list = rstate.Renderer2D.CreateDrawList();
                list.DrawImageStretched(IblResources.GetBrdfLut(rstate),
                    new Rectangle(8, renderHeight - 136, 128, 128), Color4.White);
                list.Render();
            }

            rstate.DepthEnabled = true;
            // RT debug overlay covers the finished frame (UI still lands on
            // top - it renders later in the state's Draw).
            if (RtDebugMode > 0 && rstate.RayTracing != null && rtScene != null)
            {
                DrawRtDebug(camera);
            }
            // Mesh shader smoke test (roadmap 7.5 / C1): one mesh-emitted
            // triangle as an overlay proves the whole pipeline path.
            if (MsDebugRequested && Shaders.AllShaders.MSDebug != null)
            {
                rstate.Shader = Shaders.AllShaders.MSDebug.Get(
                    Environment.GetEnvironmentVariable("SIRIUS_MS_DEBUG_BIG") == "1" ? 1u : 0u);
                rstate.DepthEnabled = false;
                rstate.BlendMode = BlendMode.Normal;
                rstate.DrawMeshTasks(1, 1, 1);
                rstate.DepthEnabled = true;
            }
            objects.Clear();
        }

        private void UpdateVolumetricNebulaFrame(int renderWidth, int renderHeight, RenderFeatureSet features,
            NebulaVolumeProfile? profile)
        {
            if (!features.VolumetricNebula && volumetricNebulaResources == null)
            {
                return;
            }

            volumetricNebulaResources ??= new VolumetricNebulaFrameResources();
            if (profile is { IsValid: true } activeProfile)
            {
                TryBindImportedDensityVolume(features, activeProfile);
            }
            var displacementFrame = features.VolumetricShipDisplacement
                ? volumetricShipDisplacement.BuildFrame(World.Objects, camera.Position, (float)game.TotalTime)
                : VolumetricShipDisplacementFrame.Empty;
            volumetricNebulaResources.Ensure(rstate, renderWidth, renderHeight, features, profile,
                (float)game.TotalTime, ResolveVolumetricSunDirection(camera.Position), displacementFrame);
        }

        private void TryBindImportedDensityVolume(RenderFeatureSet features, NebulaVolumeProfile activeProfile)
        {
            if (volumetricNebulaResources == null)
            {
                return;
            }

            var manifestPath = features.VolumetricOpenVdbManifest;
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                importedDensityCacheKey = string.Empty;
                importedDensityFrame = default;
                volumetricNebulaResources.ClearImportedDensity();
                return;
            }

            var canonicalSystem = starSystem?.Nickname ?? "";
            var cacheKey = $"{manifestPath}|{canonicalSystem}|{activeProfile.Nickname}|{activeProfile.SourceFile}";
            if (!string.Equals(cacheKey, importedDensityCacheKey, StringComparison.Ordinal))
            {
                importedDensityFrame = LoadImportedDensityVolume(manifestPath, activeProfile, canonicalSystem);
                importedDensityCacheKey = cacheKey;
                if (!importedDensityFrame.Valid)
                {
                    FLLog.Warning("Volumetrics",
                        $"OpenVDB density manifest '{manifestPath}' not bound: {importedDensityFrame.Error}");
                }
            }

            if (importedDensityFrame.Valid)
            {
                volumetricNebulaResources.SetImportedDensity(importedDensityFrame, activeProfile, canonicalSystem);
            }
            else
            {
                volumetricNebulaResources.ClearImportedDensity(importedDensityFrame.Error);
            }
        }

        private VolumetricImportedDensityFrame LoadImportedDensityVolume(
            string manifestPath,
            NebulaVolumeProfile activeProfile,
            string canonicalSystem)
        {
            try
            {
                if (!TryOpenVolumetricAsset(manifestPath, out var manifestStream, out var triedManifestPaths))
                {
                    return VolumetricImportedDensityFrame.Invalid(
                        "OpenVDB density manifest not found in VFS: " +
                        string.Join(", ", triedManifestPaths));
                }

                string[] manifestLines;
                using (manifestStream)
                using (var reader = new StreamReader(manifestStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    manifestLines = reader.ReadToEnd()
                        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                }

                return VolumetricImportedDensityFrame.FromCacheManifest(
                    manifestLines,
                    activeProfile,
                    LoadArtifact,
                    canonicalSystem);
            }
            catch (Exception ex)
            {
                return VolumetricImportedDensityFrame.Invalid(ex.Message);
            }

            bool LoadArtifact(string relativePath, out byte[] artifact)
            {
                artifact = [];
                if (!TryOpenVolumetricAsset(relativePath, out var stream, out _))
                {
                    return false;
                }

                using (stream)
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    artifact = buffer.ToArray();
                }
                return artifact.Length > 0;
            }
        }

        private bool TryOpenVolumetricAsset(string relativePath, out Stream stream, out string[] triedPaths)
        {
            triedPaths = VolumetricAssetPathResolver.BuildVfsCandidates(
                relativePath,
                (game as FreelancerGame)?.GameData?.Items?.Ini?.Freelancer?.DataPath);
            foreach (var candidate in triedPaths)
            {
                if (!resman.ResourceExists(candidate))
                {
                    continue;
                }

                stream = resman.OpenResource(candidate);
                return true;
            }

            stream = Stream.Null;
            return false;
        }

        private void UpdateVolumetricAtmosphereResources(RenderFeatureSet features, int renderWidth, int renderHeight)
        {
            if (!features.AtmosphereLuts && volumetricAtmosphereResources == null)
            {
                VolumetricAtmosphereFrameResources.NoteDisabled("off");
                return;
            }

            volumetricAtmosphereResources ??= new VolumetricAtmosphereFrameResources();
            volumetricAtmosphereResources.Ensure(rstate, features, renderWidth, renderHeight);
        }

        private bool ShouldUseVolumetricCompositeThisFrame(RenderFeatureSet features, bool hasActiveProfile) =>
            features.VolumetricComposite &&
            hasActiveProfile &&
            volumetricNebulaResources is { GpuIntegratedThisFrame: true, Integrated: { IsDisposed: false } } &&
            LibreLancer.Shaders.AllShaders.FroxelComposite != null;

        private void ApplyVolumetricNebulaSceneComposite(RenderFeatureSet features, bool hasActiveProfile,
            NebulaVolumeProfile activeProfile)
        {
            if (features.VolumetricReprojection)
            {
                var temporalDepth = hdrPipeline.CopySceneDepthForVolumetrics();
                ApplyVolumetricNebulaTemporal(features, hasActiveProfile, activeProfile, temporalDepth);
            }
            ApplyVolumetricNebulaComposite(features, hasActiveProfile, activeProfile);
        }

        private void ApplyVolumetricNebulaTemporal(RenderFeatureSet features, bool hasActiveProfile,
            NebulaVolumeProfile activeProfile, Texture2D? sceneDepth = null)
        {
            if (!features.VolumetricTemporal || !hasActiveProfile || volumetricNebulaResources == null)
            {
                return;
            }
            volumetricNebulaResources.ApplyTemporal(rstate, features, activeProfile, camera, sceneDepth);
        }

        private void ApplyVolumetricNebulaComposite(RenderFeatureSet features, bool hasActiveProfile,
            NebulaVolumeProfile activeProfile)
        {
            if (!features.VolumetricComposite || !hasActiveProfile || volumetricNebulaResources == null || hdrPipeline == null)
            {
                return;
            }
            volumetricNebulaResources.CompositeIntoHdr(hdrPipeline, features, activeProfile, camera);
        }

        private bool BindVolumetricMaterialFog(RenderFeatureSet features, bool hasActiveProfile,
            NebulaVolumeProfile activeProfile)
        {
            if (!features.VolumetricMaterialFog || !hasActiveProfile || volumetricNebulaResources == null)
            {
                return false;
            }
            return volumetricNebulaResources.BindMaterialFog(features, activeProfile);
        }

        private void UpdateVolumetricGodRays(RenderFeatureSet features, bool hasActiveProfile,
            NebulaVolumeProfile activeProfile)
        {
            volumetricSunTransmittance = 1f;
            hdrPipeline!.GodRaysSunTransmittance = 1f;
            VolumetricNebulaFrameResources.NoteGodRays(false, "");
            if (!features.VolumetricGodRays || !hasActiveProfile)
            {
                return;
            }

            var sunDistance = activeProfile.FogRange.Y;
            if (TryGetDominantSun(camera.Position, out var sunPosition))
            {
                sunDistance = Vector3.Distance(camera.Position, sunPosition);
            }

            var effectiveQuality = VolumetricNebulaFrameResources.LastDebug is { Allocated: true, Quality: >= 0 } debug
                ? debug.Quality
                : features.VolumetricQuality;
            var godRays = VolumetricGodRayMath.ForProfile(activeProfile, sunDistance,
                effectiveQuality, Settings.SelectedGodRaysIntensity, enabled: true);
            volumetricSunTransmittance = godRays.SunTransmittance;
            hdrPipeline.GodRaysSunTransmittance = godRays.SunTransmittance;
            hdrPipeline.GodRaysIntensity = godRays.PostIntensity;
            hdrPipeline.GodRaysDensity = godRays.RayDensity;
            hdrPipeline.GodRaysDecay = godRays.RayDecay;
            VolumetricNebulaFrameResources.NoteGodRays(true, godRays.DebugSummary);
        }

        private static void UnbindVolumetricMaterialFog()
        {
            RenderMaterial.VolumetricFogMaterialActive = false;
            RenderMaterial.VolumetricFogActive = false;
            RenderMaterial.SetVolumetricFogSource(null, Vector4.Zero);
        }

        private void DrawVolumetricNebulaDebugView(int renderWidth, int renderHeight, RenderDebugView debugView)
        {
            if (debugView is RenderDebugView.AtmosphereLuts or
                RenderDebugView.AtmosphereSkyView or
                RenderDebugView.AtmosphereAerial or
                RenderDebugView.AtmosphereCloudShell)
            {
                if (volumetricAtmosphereResources == null)
                {
                    return;
                }
                rstate.BeginPassTimer("vol_atmosphere_debug_view");
                try
                {
                    volumetricAtmosphereResources.DrawDebugView(rstate, debugView, renderWidth, renderHeight);
                }
                finally
                {
                    rstate.EndPassTimer();
                }
                return;
            }

            if (volumetricNebulaResources == null)
            {
                return;
            }
            if (debugView is not (RenderDebugView.VolumetricDensity or
                                  RenderDebugView.VolumetricTransmittance or
                                  RenderDebugView.VolumetricFroxels or
                                  RenderDebugView.VolumetricZones or
                                  RenderDebugView.VolumetricDisplacement or
                                  RenderDebugView.VolumetricDisplacementHistory or
                                  RenderDebugView.VolumetricWakeVectors or
                                  RenderDebugView.VolumetricGodRays or
                                  RenderDebugView.VolumetricLightning or
                                  RenderDebugView.VolumetricLightningMask or
                                  RenderDebugView.VolumetricHistory or
                                  RenderDebugView.VolumetricHistoryConfidence or
                                  RenderDebugView.VolumetricJitter or
                                  RenderDebugView.VolumetricNear or
                                  RenderDebugView.VolumetricNearDensity or
                                  RenderDebugView.VolumetricOpenVdb))
            {
                return;
            }

            rstate.BeginPassTimer("vol_nebula_debug_view");
            try
            {
                volumetricNebulaResources.DrawDebugView(rstate, debugView, renderWidth, renderHeight);
            }
            finally
            {
                rstate.EndPassTimer();
            }
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

        private Vector3 ResolveVolumetricSunDirection(Vector3 nearPosition)
        {
            if (FindShadowLight(nearPosition, out var direction, out _))
            {
                var lenSq = direction.LengthSquared();
                if (lenSq > 1e-6f)
                {
                    return direction / MathF.Sqrt(lenSq);
                }
            }
            return Vector3.Normalize(new Vector3(0.35f, 0.18f, -0.92f));
        }

        // FL space systems author the sun as a huge-range point source, not
        // a directional light - at station scale its direction is constant,
        // so the dominant point light doubles as the cascade sun. A true
        // directional light (THN sets author those) still wins.
        // Nearest visible star - anchors the sun direction deterministically.
        private bool TryGetDominantSun(Vector3 nearPosition, out Vector3 sunPosition)
        {
            sunPosition = default;
            float best = float.MaxValue;
            bool found = false;
            foreach (var obj in objects)
            {
                if (obj is not SunRenderer sun)
                {
                    continue;
                }
                float d = Vector3.DistanceSquared(sun.WorldPosition, nearPosition);
                if (d < best)
                {
                    best = d;
                    sunPosition = sun.WorldPosition;
                    found = true;
                }
            }
            return found;
        }

        private bool FindShadowLight(Vector3 nearPosition, out Vector3 direction, out DynamicLight? shadowLight)
        {
            // Brightest active directional and widest point, both picked
            // order-INDEPENDENTLY. The previous version returned whichever
            // directional happened to be first in SystemLighting.Lights, but
            // that list is built lazily and its order is not stable - the
            // volumetric sun direction flipped between otherwise identical
            // captures (e.g. (0.13,0,-0.99) vs (1,0,0.03)), so the fog
            // lighting, god rays and CSM jumped run-to-run.
            DynamicLight? bestDir = null;
            float bestLum = float.NegativeInfinity;
            DynamicLight? bestPoint = null;
            float bestRange = float.NegativeInfinity;
            foreach (var light in SystemLighting.Lights)
            {
                if (!light.Active)
                {
                    continue;
                }
                if (light.Light.Kind == LightKind.Directional)
                {
                    float lum = light.Light.Color.R + light.Light.Color.G + light.Light.Color.B;
                    if (lum > bestLum)
                    {
                        bestLum = lum;
                        bestDir = light;
                    }
                }
                else if (light.Light.Range > bestRange)
                {
                    bestRange = light.Light.Range;
                    bestPoint = light;
                }
            }

            // Anchor on the visible star when one is loaded: it is stable and
            // is where the player actually sees the light come from, so the
            // god rays and the sun-lit side of the cloud line up with the disc.
            if (TryGetDominantSun(nearPosition, out var sunPos))
            {
                var toSun = sunPos - nearPosition;
                if (toSun.LengthSquared() > 1)
                {
                    direction = Vector3.Normalize(toSun);
                    shadowLight = bestDir ?? bestPoint;
                    return true;
                }
            }
            if (bestDir != null)
            {
                direction = Vector3.Normalize(bestDir.Light.Direction);
                shadowLight = bestDir;
                return true;
            }
            if (bestPoint != null)
            {
                var toScene = nearPosition - bestPoint.Light.Position;
                if (toScene.LengthSquared() > 1)
                {
                    direction = Vector3.Normalize(toScene);
                    shadowLight = bestPoint;
                    return true;
                }
            }
            direction = Vector3.UnitY;
            shadowLight = null;
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
            // VRS (roadmap 7.6): the starsphere is low-frequency content -
            // 2x2 coarse shading there is visually free. Ships, HUD and
            // text keep full rate (the rate resets right after).
            var vrs = Settings.SelectedVrs &&
                rstate.HasFeature(GraphicsFeature.VariableRateShading) &&
                Environment.GetEnvironmentVariable("SIRIUS_VRS_HOOK_OFF") != "1";
            if (vrs)
            {
                rstate.SetShadingRate(2);
            }
            try
            {
                DrawStarsphereLayersInner();
            }
            finally
            {
                if (vrs)
                {
                    rstate.SetShadingRate(1);
                }
            }
        }

        private void DrawStarsphereLayersInner()
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
            volumetricNebulaResources?.Dispose();
            volumetricAtmosphereResources?.Dispose();

            Polyline.Dispose();
            FxPool.Dispose();
            DebugRenderer.Dispose();
            QuadBuffer.Dispose();
            Beams.Dispose();
        }
    }
}
