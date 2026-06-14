// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System.Globalization;
using System.IO;
using System.Xml.Serialization;
using LibreLancer.Data.Ini;
using LibreLancer.Graphics;
using LibreLancer.Render;
using WattleScript.Interpreter;

namespace LibreLancer
{
    [WattleScriptUserData]
    [ParsedSection]
    public partial class GameSettings : IRendererSettings
    {
        [Entry("master_volume")]
        public float MasterVolume = 1.0f;
        [Entry("sfx_volume")]
        public float SfxVolume = 1.0f;
        [Entry("voice_volume")]
        public float VoiceVolume = 1.0f;
        [Entry("interface_volume")]
        public float InterfaceVolume = 1.0f;
        [Entry("music_volume")]
        public float MusicVolume = 1.0f;

        [Entry("fullscreen")]
        public bool FullScreen = true;

        [Entry("vsync")]
        public bool VSync = true;
        [Entry("anisotropy")]
        public int Anisotropy = 0;
        [Entry("msaa")]
        public int MSAA = 0;
        [Entry("lod_multiplier")]
        public float LodMultiplier = 1.3f;
        [Entry("debug")]
        public bool Debug = false;
        [Entry("use_cubemap_starspheres")]
        public bool UseCubemapStarspheres = true;

        // Advanced preset on by default (graphics roadmap phase 1); the
        // classic look is one INI/options change away (everything off).
        [Entry("hdr")]
        public bool Hdr = true;
        // String in the INI ("off"/"filmic"/"aces") so future curves slot in
        // without renumbering anybody's config. Filmic is the default: it is
        // an exact identity below the highlight knee, so the classic
        // Freelancer look survives in space scenes where the ACES toe
        // crushed dim stars and nebulae to black.
        [Entry("tonemapper")]
        public string Tonemapper = "filmic";
        [Entry("exposure")]
        public float Exposure = 1.0f;
        // Auto-exposure / eye adaptation (Slice 1). Opt-in; when on, the average
        // scene luminance drives exposure each frame instead of the fixed value.
        [Entry("auto_exposure")]
        public bool AutoExposure = false;
        // >= 0 freezes auto-exposure at this multiplier (deterministic captures).
        [Entry("auto_exposure_pin")]
        public float AutoExposurePin = -1.0f;
        // EV-style bias in stops applied on top of auto-exposure.
        [Entry("auto_exposure_compensation")]
        public float AutoExposureCompensation = 0.0f;
        [Entry("bloom")]
        public bool Bloom = true;
        // Linear-light threshold (phase 2): display 0.7 sits around 0.45
        // in linear luminance.
        [Entry("bloom_threshold")]
        public float BloomThreshold = 0.45f;
        [Entry("bloom_intensity")]
        public float BloomIntensity = 0.25f;
        [Entry("bloom_radius")]
        public float BloomRadius = 0.65f;
        [Entry("bloom_mips")]
        public int BloomMips = 6;
        [Entry("god_rays")]
        public bool GodRays = true;
        [Entry("god_rays_intensity")]
        public float GodRaysIntensity = 0.35f;
        [Entry("god_rays_samples")]
        public int GodRaysSamples = 48;
        // Post-AA and MSAA coexist only when picked explicitly (roadmap 4.7);
        // the UI offers them through one ANTI-ALIASING selector.
        [Entry("post_aa")]
        public string PostAA = "fxaa";
        [Entry("ibl")]
        public bool Ibl = true;
        [Entry("shadows")]
        public bool Shadows = true;
        // Phase 4 ray-traced sun shadows; requires ray query support.
        [Entry("rt_shadows")]
        public bool RtShadows = false;
        // Phase 4 ray-traced ambient occlusion (1 ray, ambient terms only).
        [Entry("rtao")]
        public bool Rtao = false;
        // Phase 4 ray-traced reflections (glass/smooth surfaces, 1 ray).
        [Entry("rt_reflections")]
        public bool RtReflections = false;
        // Roadmap 7.5: asteroid cube fields through the mesh-shader path.
        [Entry("mesh_asteroids")]
        public bool MeshAsteroids = false;
        // Roadmap 7.6: variable rate shading (2x2 on background passes).
        [Entry("vrs")]
        public bool Vrs = false;

        // PR-5.0 feature flags. Defaults are deliberately conservative:
        // no output pixels change until PR-5.2 wires actual froxel resources.
        [Entry("volumetric_nebula")]
        public bool VolumetricNebula = false;
        // Backward-compatible alias from earlier experiments.
        [Entry("volumetric_nebulae")]
        public bool VolumetricNebulae = false;
        [Entry("volumetric_near_cascade")]
        public bool VolumetricNearCascade = false;
        [Entry("volumetric_near_composite")]
        public bool VolumetricNearComposite = false;
        [Entry("volumetric_near_detail")]
        public bool VolumetricNearDetail = false;
        [Entry("volumetric_ship_displacement")]
        public bool VolumetricShipDisplacement = false;
        [Entry("volumetric_wake_history")]
        public bool VolumetricWakeHistory = false;
        [Entry("volumetric_wake_curl")]
        public bool VolumetricWakeCurl = false;
        [Entry("volumetric_composite")]
        public bool VolumetricComposite = false;
        [Entry("volumetric_god_rays")]
        public bool VolumetricGodRays = false;
        [Entry("volumetric_material_fog")]
        public bool VolumetricMaterialFog = false;
        [Entry("volumetric_lightning_channels")]
        public bool VolumetricLightningChannels = false;
        [Entry("volumetric_lightning_deterministic")]
        public bool VolumetricLightningDeterministic = false;
        [Entry("volumetric_lightning_golden_disable")]
        public bool VolumetricLightningGoldenDisable = false;
        [Entry("volumetric_lightning_replay_time")]
        public float VolumetricLightningReplayTime = -1f;
        [Entry("volumetric_lightning_replay_seed")]
        public int VolumetricLightningReplaySeed = 0;
        [Entry("volumetric_temporal")]
        public bool VolumetricTemporal = false;
        [Entry("volumetric_reprojection")]
        public bool VolumetricReprojection = false;
        [Entry("volumetric_blue_noise")]
        public bool VolumetricBlueNoise = false;
        [Entry("volumetric_openvdb_manifest")]
        public string VolumetricOpenVdbManifest = "";
        [Entry("volumetric_adaptive_quality")]
        public bool VolumetricAdaptiveQuality = true;
        [Entry("atmosphere_luts")]
        public bool AtmosphereLuts = false;
        [Entry("atmosphere_aerial")]
        public bool AtmosphereAerial = false;
        [Entry("atmosphere_cloud_shell")]
        public bool AtmosphereCloudShell = false;
        // 0 low, 1 medium, 2 high, 3 ultra. High matches the P5 baseline.
        [Entry("volumetric_quality")]
        public int VolumetricQuality = 2;
        [Entry("debug_view")]
        public string DebugView = "off";
        [Entry("dev_hud")]
        public bool DevHud = false;
        [Entry("pass_timings")]
        public bool PassTimings = false;
        [Entry("render_debug_markers")]
        public bool RenderDebugMarkers = true;
        [Entry("render_capture_startup")]
        public bool RenderCaptureStartup = false;
        [Entry("render_capture_next_frame")]
        public bool RenderCaptureNextFrame = false;
        [Entry("render_capture_path")]
        public string RenderCapturePath = "";

        private bool VolumetricNebulaRequested => VolumetricNebula || VolumetricNebulae;

        public bool Phase5NearCascadeEnabled => VolumetricNebulaRequested && VolumetricNearCascade;

        public bool Phase5NearCompositeEnabled =>
            Phase5NearCascadeEnabled && Phase5CompositeEnabled && VolumetricNearComposite;

        public bool Phase5NearDetailEnabled => Phase5NearCascadeEnabled && VolumetricNearDetail;

        public bool Phase5ShipDisplacementEnabled => VolumetricNebulaRequested && VolumetricShipDisplacement;

        public bool Phase5WakeHistoryEnabled => Phase5ShipDisplacementEnabled && VolumetricNearCascade && VolumetricWakeHistory;

        public bool Phase5WakeCurlEnabled => Phase5WakeHistoryEnabled && VolumetricWakeCurl;

        public bool Phase5CompositeEnabled => VolumetricNebulaRequested && VolumetricComposite;

        public bool Phase5GodRaysEnabled => VolumetricNebulaRequested && VolumetricGodRays;

        public bool Phase5MaterialFogEnabled => VolumetricNebulaRequested && VolumetricMaterialFog;

        public bool Phase5LightningChannelsEnabled => VolumetricNebulaRequested && VolumetricLightningChannels;

        public bool Phase5LightningDeterministicEnabled =>
            Phase5LightningChannelsEnabled && VolumetricLightningDeterministic;

        public bool Phase5LightningGoldenDisableEnabled =>
            Phase5LightningChannelsEnabled && VolumetricLightningGoldenDisable;

        public bool Phase5TemporalEnabled => VolumetricNebulaRequested && VolumetricTemporal;

        public bool Phase5ReprojectionEnabled => Phase5TemporalEnabled && VolumetricReprojection;

        public bool Phase5BlueNoiseEnabled => VolumetricNebulaRequested && VolumetricBlueNoise;

        public string? Phase5OpenVdbManifest =>
            VolumetricNebulaRequested && !string.IsNullOrWhiteSpace(VolumetricOpenVdbManifest)
                ? VolumetricOpenVdbManifest.Trim()
                : null;

        public bool Phase5AdaptiveQualityEnabled => VolumetricNebulaRequested && VolumetricAdaptiveQuality;

        public string Phase5DebugView => NormalizePhase5DebugView(DebugView);

        public static string NormalizePhase5DebugView(string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                null or "" or "0" or "off" or "none" => "off",
                "depth" => "depth",
                "albedo" or "normals" or "normal" or "roughness" or "metallic" or "shadow" or "shadows" or "ibl" => value!.Trim().ToLowerInvariant(),
                "vol_density" or "voldensity" or "density" or "volumetric_density" => "vol_density",
                "vol_transmittance" or "voltransmittance" or "transmittance" or "volumetric_transmittance" => "vol_transmittance",
                "vol_froxels" or "volfroxels" or "froxels" or "froxel" => "vol_froxels",
                "vol_near" or "volnear" or "near" or "near_froxels" or "volumetric_near" => "vol_near",
                "vol_zones" or "volzones" or "zones" or "volumetric_zones" or "volume_zones" => "vol_zones",
                "vol_displacement" or "voldisp" or "displacement" => "vol_displacement",
                "vol_displacement_history" or "voldisphistory" or "vol_wake_history" or "wake_history" => "vol_displacement_history",
                "vol_wake_vectors" or "wake_vectors" or "volwakevectors" or "vol_curl" or "wake_curl" => "vol_wake_vectors",
                "vol_god_rays" or "volgodrays" or "god_rays" or "sun_burnthrough" or "vol_burnthrough" => "vol_god_rays",
                "vol_lightning" or "vollightning" or "lightning_channels" or "vol_lightning_channels" => "vol_lightning",
                "vol_lightning_mask" or "vollightningmask" or "lightning_mask" or "vol_lightning_debug" => "vol_lightning_mask",
                "vol_history" or "volhistory" or "history" or "volumetric_history" => "vol_history",
                "vol_history_confidence" or "volconfidence" or "vol_confidence" or "history_confidence" => "vol_history_confidence",
                "vol_jitter" or "voljitter" or "jitter" or "vol_blue_noise" or "vol_stbn" => "vol_jitter",
                "vol_near_density" or "volneardensity" or "near_density" or "neardensity" => "vol_near_density",
                "atmosphere_luts" or "atmoluts" or "atmo_luts" => "atmosphere_luts",
                "atmosphere_aerial" or "atmoaerial" or "atmo_aerial" or "aerial_perspective" => "atmosphere_aerial",
                "atmosphere_cloud_shell" or "atmocloudshell" or "atmo_cloud_shell" or "cloud_shell" => "atmosphere_cloud_shell",
                "atmo_aerial" or "atmoaerial" or "aerial" => "atmo_aerial",
                _ => "off"
            };
        }

        float IRendererSettings.LodMultiplier => LodMultiplier;

        int IRendererSettings.SelectedAnisotropy => Anisotropy;
        TextureFiltering IRendererSettings.SelectedFiltering =>
            Anisotropy == 0 ? TextureFiltering.Trilinear : TextureFiltering.Anisotropic;
        int IRendererSettings.SelectedMSAA => MSAA;
        bool IRendererSettings.SelectedHdr => Hdr;
        TonemapMode IRendererSettings.SelectedTonemapper =>
            !Hdr ? TonemapMode.Off
                : Tonemapper.ToLowerInvariant() switch
                {
                    "aces" => TonemapMode.Aces,
                    "off" => TonemapMode.Off,
                    _ => TonemapMode.Filmic
                };
        float IRendererSettings.SelectedExposure => Exposure;
        bool IRendererSettings.SelectedAutoExposure => Hdr && AutoExposure;
        float IRendererSettings.SelectedAutoExposurePin => AutoExposurePin;
        float IRendererSettings.SelectedAutoExposureCompensation => AutoExposureCompensation;
        bool IRendererSettings.SelectedBloom => Hdr && Bloom;
        float IRendererSettings.SelectedBloomThreshold => BloomThreshold;
        float IRendererSettings.SelectedBloomIntensity => BloomIntensity;
        float IRendererSettings.SelectedBloomRadius => BloomRadius;
        int IRendererSettings.SelectedBloomMips => BloomMips;
        bool IRendererSettings.SelectedGodRays => Hdr && GodRays;
        float IRendererSettings.SelectedGodRaysIntensity => GodRaysIntensity;
        int IRendererSettings.SelectedGodRaysSamples => GodRaysSamples;
        bool IRendererSettings.SelectedIbl => Ibl;
        bool IRendererSettings.SelectedShadows => Shadows;
        bool IRendererSettings.SelectedRtShadows => Shadows && RtShadows;
        bool IRendererSettings.SelectedRtao => Rtao;
        bool IRendererSettings.SelectedRtReflections => RtReflections;
        bool IRendererSettings.SelectedMeshAsteroids => MeshAsteroids;
        bool IRendererSettings.SelectedVrs => Vrs;
        bool IRendererSettings.SelectedVolumetricNebula => VolumetricNebulaRequested;
        bool IRendererSettings.SelectedVolumetricNebulae => VolumetricNebulaRequested;
        bool IRendererSettings.SelectedVolumetricNearCascade => VolumetricNebulaRequested && VolumetricNearCascade;
        bool IRendererSettings.SelectedVolumetricNearComposite => Phase5NearCompositeEnabled;
        bool IRendererSettings.SelectedVolumetricNearDetail => Phase5NearDetailEnabled;
        bool IRendererSettings.SelectedVolumetricShipDisplacement => VolumetricNebulaRequested && VolumetricShipDisplacement;
        bool IRendererSettings.SelectedVolumetricWakeHistory => Phase5WakeHistoryEnabled;
        bool IRendererSettings.SelectedVolumetricWakeCurl => Phase5WakeCurlEnabled;
        bool IRendererSettings.SelectedVolumetricComposite => VolumetricNebulaRequested && VolumetricComposite;
        bool IRendererSettings.SelectedVolumetricGodRays => Phase5GodRaysEnabled;
        bool IRendererSettings.SelectedVolumetricMaterialFog => VolumetricNebulaRequested && VolumetricMaterialFog;
        bool IRendererSettings.SelectedVolumetricLightningChannels => VolumetricNebulaRequested && VolumetricLightningChannels;
        bool IRendererSettings.SelectedVolumetricLightningDeterministic => Phase5LightningDeterministicEnabled;
        bool IRendererSettings.SelectedVolumetricLightningGoldenDisable => Phase5LightningGoldenDisableEnabled;
        float IRendererSettings.SelectedVolumetricLightningReplayTime =>
            Phase5LightningChannelsEnabled ? VolumetricLightningReplayTime : -1f;
        int IRendererSettings.SelectedVolumetricLightningReplaySeed =>
            Phase5LightningChannelsEnabled ? VolumetricLightningReplaySeed : 0;
        bool IRendererSettings.SelectedVolumetricTemporal => VolumetricNebulaRequested && VolumetricTemporal;
        bool IRendererSettings.SelectedVolumetricReprojection => Phase5ReprojectionEnabled;
        bool IRendererSettings.SelectedVolumetricBlueNoise => Phase5BlueNoiseEnabled;
        string? IRendererSettings.SelectedVolumetricOpenVdbManifest => Phase5OpenVdbManifest;
        bool IRendererSettings.SelectedVolumetricAdaptiveQuality => Phase5AdaptiveQualityEnabled;
        bool IRendererSettings.SelectedAtmosphereLuts => AtmosphereLuts;
        bool IRendererSettings.SelectedAtmosphereAerialPerspective => AtmosphereLuts && AtmosphereAerial;
        bool IRendererSettings.SelectedAtmosphereCloudShell => AtmosphereLuts && AtmosphereCloudShell;
        int IRendererSettings.SelectedVolumetricQuality => VolumetricQuality;
        string IRendererSettings.SelectedDebugView => DebugView;
        bool IRendererSettings.SelectedRenderDebugMarkers => RenderDebugMarkers;
        bool IRendererSettings.SelectedRenderCaptureStartup => RenderCaptureStartup;
        bool IRendererSettings.SelectedRenderCaptureNextFrame => RenderCaptureNextFrame;
        string? IRendererSettings.SelectedRenderCapturePath => string.IsNullOrWhiteSpace(RenderCapturePath) ? null : RenderCapturePath;
        PostAaMode IRendererSettings.SelectedPostAa => !Hdr
            ? PostAaMode.Off
            : PostAA.ToLowerInvariant() switch
            {
                "fxaa" => PostAaMode.Fxaa,
                "smaa" => PostAaMode.Smaa,
                _ => PostAaMode.Off
            };

        public int[]? AnisotropyLevels() => RenderContext.GetAnisotropyLevels();
        public int MaxMSAA() => RenderContext.MaxSamples;
        public bool RayTracingSupported() => RenderContext.HasFeature(GraphicsFeature.RayQuery);
        public bool MeshShadersSupported() => RenderContext.HasFeature(GraphicsFeature.MeshShaders);
        public bool VrsSupported() => RenderContext.HasFeature(GraphicsFeature.VariableRateShading);

        [WattleScriptHidden]
        public void Write(TextWriter writer)
        {
            static string Fmt(float f) => f.ToString("F3", CultureInfo.InvariantCulture);
            writer.WriteLine("[Settings]");
            writer.WriteLine($"master_volume = {Fmt(MasterVolume)}");
            writer.WriteLine($"sfx_volume = {Fmt(SfxVolume)}");
            writer.WriteLine($"voice_volume = {Fmt(VoiceVolume)}");
            writer.WriteLine($"interface_volume = {Fmt(InterfaceVolume)}");
            writer.WriteLine($"music_volume = {Fmt(MusicVolume)}");

            writer.WriteLine($"fullscreen = {(FullScreen ? "true" : "false")}");

            writer.WriteLine($"vsync = {(VSync ? "true" : "false")}");
            writer.WriteLine($"anisotropy = {Anisotropy}");
            writer.WriteLine($"msaa = {MSAA}");
            writer.WriteLine($"lod_multiplier = {Fmt(LodMultiplier)}");
            writer.WriteLine($"debug = {(Debug ? "true" : "false")}");
            writer.WriteLine($"use_cubemap_starspheres = {(UseCubemapStarspheres ? "true" : "false")}");
            writer.WriteLine($"hdr = {(Hdr ? "true" : "false")}");
            writer.WriteLine($"tonemapper = {Tonemapper}");
            writer.WriteLine($"exposure = {Fmt(Exposure)}");
            writer.WriteLine($"auto_exposure = {(AutoExposure ? "true" : "false")}");
            writer.WriteLine($"auto_exposure_pin = {Fmt(AutoExposurePin)}");
            writer.WriteLine($"auto_exposure_compensation = {Fmt(AutoExposureCompensation)}");
            writer.WriteLine($"bloom = {(Bloom ? "true" : "false")}");
            writer.WriteLine($"bloom_threshold = {Fmt(BloomThreshold)}");
            writer.WriteLine($"bloom_intensity = {Fmt(BloomIntensity)}");
            writer.WriteLine($"bloom_radius = {Fmt(BloomRadius)}");
            writer.WriteLine($"bloom_mips = {BloomMips}");
            writer.WriteLine($"god_rays = {(GodRays ? "true" : "false")}");
            writer.WriteLine($"god_rays_intensity = {Fmt(GodRaysIntensity)}");
            writer.WriteLine($"god_rays_samples = {GodRaysSamples}");
            writer.WriteLine($"post_aa = {PostAA}");
            writer.WriteLine($"ibl = {(Ibl ? "true" : "false")}");
            writer.WriteLine($"shadows = {(Shadows ? "true" : "false")}");
            writer.WriteLine($"rt_shadows = {(RtShadows ? "true" : "false")}");
            writer.WriteLine($"rtao = {(Rtao ? "true" : "false")}");
            writer.WriteLine($"rt_reflections = {(RtReflections ? "true" : "false")}");
            writer.WriteLine($"mesh_asteroids = {(MeshAsteroids ? "true" : "false")}");
            writer.WriteLine($"vrs = {(Vrs ? "true" : "false")}");
            writer.WriteLine($"volumetric_nebula = {(VolumetricNebulaRequested ? "true" : "false")}");
            writer.WriteLine($"volumetric_near_cascade = {(VolumetricNearCascade ? "true" : "false")}");
            writer.WriteLine($"volumetric_near_composite = {(VolumetricNearComposite ? "true" : "false")}");
            writer.WriteLine($"volumetric_near_detail = {(VolumetricNearDetail ? "true" : "false")}");
            writer.WriteLine($"volumetric_ship_displacement = {(VolumetricShipDisplacement ? "true" : "false")}");
            writer.WriteLine($"volumetric_wake_history = {(VolumetricWakeHistory ? "true" : "false")}");
            writer.WriteLine($"volumetric_wake_curl = {(VolumetricWakeCurl ? "true" : "false")}");
            writer.WriteLine($"volumetric_composite = {(VolumetricComposite ? "true" : "false")}");
            writer.WriteLine($"volumetric_god_rays = {(VolumetricGodRays ? "true" : "false")}");
            writer.WriteLine($"volumetric_material_fog = {(VolumetricMaterialFog ? "true" : "false")}");
            writer.WriteLine($"volumetric_lightning_channels = {(VolumetricLightningChannels ? "true" : "false")}");
            writer.WriteLine($"volumetric_lightning_deterministic = {(VolumetricLightningDeterministic ? "true" : "false")}");
            writer.WriteLine($"volumetric_lightning_golden_disable = {(VolumetricLightningGoldenDisable ? "true" : "false")}");
            writer.WriteLine($"volumetric_lightning_replay_time = {Fmt(VolumetricLightningReplayTime)}");
            writer.WriteLine($"volumetric_lightning_replay_seed = {VolumetricLightningReplaySeed}");
            writer.WriteLine($"volumetric_temporal = {(VolumetricTemporal ? "true" : "false")}");
            writer.WriteLine($"volumetric_reprojection = {(VolumetricReprojection ? "true" : "false")}");
            writer.WriteLine($"volumetric_blue_noise = {(VolumetricBlueNoise ? "true" : "false")}");
            if (!string.IsNullOrWhiteSpace(VolumetricOpenVdbManifest))
            {
                writer.WriteLine($"volumetric_openvdb_manifest = {VolumetricOpenVdbManifest.Trim()}");
            }
            writer.WriteLine($"volumetric_adaptive_quality = {(VolumetricAdaptiveQuality ? "true" : "false")}");
            writer.WriteLine($"atmosphere_luts = {(AtmosphereLuts ? "true" : "false")}");
            writer.WriteLine($"atmosphere_aerial = {(AtmosphereAerial ? "true" : "false")}");
            writer.WriteLine($"atmosphere_cloud_shell = {(AtmosphereCloudShell ? "true" : "false")}");
            writer.WriteLine($"volumetric_quality = {VolumetricQuality}");
            writer.WriteLine($"debug_view = {DebugView}");
            writer.WriteLine($"dev_hud = {(DevHud ? "true" : "false")}");
            writer.WriteLine($"pass_timings = {(PassTimings ? "true" : "false")}");
            writer.WriteLine($"render_debug_markers = {(RenderDebugMarkers ? "true" : "false")}");
            writer.WriteLine($"render_capture_startup = {(RenderCaptureStartup ? "true" : "false")}");
            writer.WriteLine($"render_capture_next_frame = {(RenderCaptureNextFrame ? "true" : "false")}");
            if (!string.IsNullOrWhiteSpace(RenderCapturePath))
            {
                writer.WriteLine($"render_capture_path = {RenderCapturePath}");
            }
        }

        [WattleScriptHidden]
        public RenderContext RenderContext = null!;

        [WattleScriptHidden]
        public GameSettings MakeCopy()
        {
            var gs = new GameSettings
            {
                MasterVolume = MasterVolume,
                SfxVolume = SfxVolume,
                InterfaceVolume = InterfaceVolume,
                VoiceVolume = VoiceVolume,
                MusicVolume = MusicVolume,
                FullScreen = FullScreen,
                VSync = VSync,
                Anisotropy = Anisotropy,
                MSAA = MSAA,
                LodMultiplier = LodMultiplier,
                RenderContext = RenderContext,
                Debug = Debug,
                UseCubemapStarspheres = UseCubemapStarspheres,
                Hdr = Hdr,
                Tonemapper = Tonemapper,
                Exposure = Exposure,
                AutoExposure = AutoExposure,
                AutoExposurePin = AutoExposurePin,
                AutoExposureCompensation = AutoExposureCompensation,
                Bloom = Bloom,
                BloomThreshold = BloomThreshold,
                BloomIntensity = BloomIntensity,
                BloomRadius = BloomRadius,
                BloomMips = BloomMips,
                GodRays = GodRays,
                GodRaysIntensity = GodRaysIntensity,
                GodRaysSamples = GodRaysSamples,
                PostAA = PostAA,
                Ibl = Ibl,
                Shadows = Shadows,
                RtShadows = RtShadows,
                Rtao = Rtao,
                RtReflections = RtReflections,
                MeshAsteroids = MeshAsteroids,
                Vrs = Vrs,
                VolumetricNebula = VolumetricNebula,
                VolumetricNebulae = VolumetricNebulae,
                VolumetricNearCascade = VolumetricNearCascade,
                VolumetricNearComposite = VolumetricNearComposite,
                VolumetricNearDetail = VolumetricNearDetail,
                VolumetricShipDisplacement = VolumetricShipDisplacement,
                VolumetricWakeHistory = VolumetricWakeHistory,
                VolumetricWakeCurl = VolumetricWakeCurl,
                VolumetricComposite = VolumetricComposite,
                VolumetricGodRays = VolumetricGodRays,
                VolumetricMaterialFog = VolumetricMaterialFog,
                VolumetricLightningChannels = VolumetricLightningChannels,
                VolumetricLightningDeterministic = VolumetricLightningDeterministic,
                VolumetricLightningGoldenDisable = VolumetricLightningGoldenDisable,
                VolumetricLightningReplayTime = VolumetricLightningReplayTime,
                VolumetricLightningReplaySeed = VolumetricLightningReplaySeed,
                VolumetricTemporal = VolumetricTemporal,
                VolumetricReprojection = VolumetricReprojection,
                VolumetricBlueNoise = VolumetricBlueNoise,
                VolumetricOpenVdbManifest = VolumetricOpenVdbManifest,
                VolumetricAdaptiveQuality = VolumetricAdaptiveQuality,
                AtmosphereLuts = AtmosphereLuts,
                AtmosphereAerial = AtmosphereAerial,
                AtmosphereCloudShell = AtmosphereCloudShell,
                VolumetricQuality = VolumetricQuality,
                DebugView = DebugView,
                DevHud = DevHud,
                PassTimings = PassTimings,
                RenderDebugMarkers = RenderDebugMarkers,
                RenderCaptureStartup = RenderCaptureStartup,
                RenderCaptureNextFrame = RenderCaptureNextFrame,
                RenderCapturePath = RenderCapturePath
            };

            return gs;
        }

        public void Validate()
        {
            if (MSAA > RenderContext.MaxSamples)
            {
                FLLog.Info("Config", $"{MSAA}x MSAA not supported, disabling.");
                MSAA = 0;
            }
            if (Anisotropy > RenderContext.MaxAnisotropy)
            {
                FLLog.Info("Config", $"{Anisotropy}x anisotropy not supported, disabling.");
                Anisotropy = 0;
            }
            if (!Tonemapper.Equals("off", System.StringComparison.OrdinalIgnoreCase) &&
                !Tonemapper.Equals("filmic", System.StringComparison.OrdinalIgnoreCase) &&
                !Tonemapper.Equals("aces", System.StringComparison.OrdinalIgnoreCase))
            {
                FLLog.Info("Config", $"Unknown tonemapper '{Tonemapper}', using 'filmic'.");
                Tonemapper = "filmic";
            }
            Exposure = System.Math.Clamp(Exposure, 0.25f, 4.0f);
            if (float.IsNaN(AutoExposurePin) || float.IsInfinity(AutoExposurePin) || AutoExposurePin < 0f)
            {
                AutoExposurePin = -1f;
            }
            else
            {
                AutoExposurePin = System.Math.Clamp(AutoExposurePin, 0.05f, 8.0f);
            }
            AutoExposureCompensation = System.Math.Clamp(AutoExposureCompensation, -4.0f, 4.0f);
            BloomThreshold = System.Math.Clamp(BloomThreshold, 0.0f, 4.0f);
            BloomIntensity = System.Math.Clamp(BloomIntensity, 0.0f, 2.0f);
            BloomRadius = System.Math.Clamp(BloomRadius, 0.0f, 1.5f);
            BloomMips = System.Math.Clamp(BloomMips, 2, 7);
            GodRaysIntensity = System.Math.Clamp(GodRaysIntensity, 0.0f, 2.0f);
            GodRaysSamples = System.Math.Clamp(GodRaysSamples, 16, 96);
            if (RtShadows && !RenderContext.HasFeature(GraphicsFeature.RayQuery))
            {
                FLLog.Info("Config", "rt_shadows requires ray query support, disabling.");
                RtShadows = false;
            }
            if (Rtao && !RenderContext.HasFeature(GraphicsFeature.RayQuery))
            {
                FLLog.Info("Config", "rtao requires ray query support, disabling.");
                Rtao = false;
            }
            if (RtReflections && !RenderContext.HasFeature(GraphicsFeature.RayQuery))
            {
                FLLog.Info("Config", "rt_reflections requires ray query support, disabling.");
                RtReflections = false;
            }
            if (MeshAsteroids && !RenderContext.HasFeature(GraphicsFeature.MeshShaders))
            {
                FLLog.Info("Config", "mesh_asteroids requires mesh shader support, disabling.");
                MeshAsteroids = false;
            }
            if (Vrs && !RenderContext.HasFeature(GraphicsFeature.VariableRateShading))
            {
                FLLog.Info("Config", "vrs requires fragment shading rate support, disabling.");
                Vrs = false;
            }
            if (VolumetricNebulaRequested && !RenderContext.HasFeature(GraphicsFeature.Compute))
            {
                FLLog.Info("Config", "volumetric_nebula requires compute support, disabling.");
                VolumetricNebula = false;
                VolumetricNebulae = false;
                VolumetricNearCascade = false;
                VolumetricNearComposite = false;
                VolumetricNearDetail = false;
                VolumetricShipDisplacement = false;
                VolumetricWakeHistory = false;
                VolumetricWakeCurl = false;
                VolumetricComposite = false;
                VolumetricGodRays = false;
                VolumetricMaterialFog = false;
                VolumetricLightningChannels = false;
                VolumetricLightningDeterministic = false;
                VolumetricLightningGoldenDisable = false;
                VolumetricLightningReplayTime = -1f;
                VolumetricLightningReplaySeed = 0;
                VolumetricTemporal = false;
                VolumetricReprojection = false;
                VolumetricBlueNoise = false;
                VolumetricOpenVdbManifest = "";
            }
            if (!VolumetricNebulaRequested)
            {
                VolumetricNearCascade = false;
                VolumetricNearComposite = false;
                VolumetricNearDetail = false;
                VolumetricShipDisplacement = false;
                VolumetricWakeHistory = false;
                VolumetricWakeCurl = false;
                VolumetricComposite = false;
                VolumetricGodRays = false;
                VolumetricMaterialFog = false;
                VolumetricLightningChannels = false;
                VolumetricLightningDeterministic = false;
                VolumetricLightningGoldenDisable = false;
                VolumetricLightningReplayTime = -1f;
                VolumetricLightningReplaySeed = 0;
                VolumetricTemporal = false;
                VolumetricReprojection = false;
                VolumetricBlueNoise = false;
            }
            VolumetricOpenVdbManifest = NormalizeVolumetricOpenVdbManifest(VolumetricOpenVdbManifest);
            if (!VolumetricTemporal)
            {
                VolumetricReprojection = false;
            }
            if (VolumetricNearComposite && (!VolumetricNearCascade || !VolumetricComposite))
            {
                FLLog.Info("Config", "volumetric_near_composite requires volumetric_near_cascade and volumetric_composite, disabling.");
                VolumetricNearComposite = false;
            }
            if (VolumetricNearDetail && !VolumetricNearCascade)
            {
                FLLog.Info("Config", "volumetric_near_detail requires volumetric_near_cascade, disabling.");
                VolumetricNearDetail = false;
            }
            if (VolumetricWakeHistory && (!VolumetricShipDisplacement || !VolumetricNearCascade))
            {
                FLLog.Info("Config", "volumetric_wake_history requires volumetric_ship_displacement and volumetric_near_cascade, disabling.");
                VolumetricWakeHistory = false;
            }
            if (VolumetricWakeCurl && !VolumetricWakeHistory)
            {
                FLLog.Info("Config", "volumetric_wake_curl requires volumetric_wake_history, disabling.");
                VolumetricWakeCurl = false;
            }
            if (!VolumetricLightningChannels)
            {
                VolumetricLightningDeterministic = false;
                VolumetricLightningGoldenDisable = false;
                VolumetricLightningReplayTime = -1f;
                VolumetricLightningReplaySeed = 0;
            }
            if (float.IsNaN(VolumetricLightningReplayTime) || float.IsInfinity(VolumetricLightningReplayTime) ||
                VolumetricLightningReplayTime < 0f)
            {
                VolumetricLightningReplayTime = -1f;
            }
            if (AtmosphereLuts && !RenderContext.HasFeature(GraphicsFeature.Compute))
            {
                FLLog.Info("Config", "atmosphere_luts requires compute support, disabling.");
                AtmosphereLuts = false;
            }
            if (AtmosphereAerial && !AtmosphereLuts)
            {
                FLLog.Info("Config", "atmosphere_aerial requires atmosphere_luts, disabling.");
                AtmosphereAerial = false;
            }
            if (AtmosphereCloudShell && !AtmosphereLuts)
            {
                FLLog.Info("Config", "atmosphere_cloud_shell requires atmosphere_luts, disabling.");
                AtmosphereCloudShell = false;
            }
            VolumetricQuality = System.Math.Clamp(VolumetricQuality, 0, 3);
            if (string.IsNullOrWhiteSpace(DebugView))
            {
                DebugView = "off";
            }
            else
            {
                DebugView = NormalizePhase5DebugView(DebugView);
            }
            if (!PostAA.Equals("off", System.StringComparison.OrdinalIgnoreCase) &&
                !PostAA.Equals("fxaa", System.StringComparison.OrdinalIgnoreCase) &&
                !PostAA.Equals("smaa", System.StringComparison.OrdinalIgnoreCase))
            {
                FLLog.Info("Config", $"Unknown post_aa '{PostAA}', using 'off'.");
                PostAA = "off";
            }
        }

        private static string NormalizeVolumetricOpenVdbManifest(string? manifest)
        {
            if (string.IsNullOrWhiteSpace(manifest))
            {
                return "";
            }

            var normalized = manifest.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(normalized) ||
                normalized.Contains(":", System.StringComparison.Ordinal) ||
                normalized.StartsWith("../", System.StringComparison.Ordinal) ||
                normalized.Contains("/../", System.StringComparison.Ordinal) ||
                normalized.Equals("..", System.StringComparison.Ordinal))
            {
                FLLog.Info("Config", "volumetric_openvdb_manifest must be a safe relative VFS path, disabling.");
                return "";
            }
            if (!normalized.EndsWith(".siriusvol.manifest", System.StringComparison.OrdinalIgnoreCase))
            {
                FLLog.Info("Config", "volumetric_openvdb_manifest must point to a .siriusvol.manifest file, disabling.");
                return "";
            }
            return normalized;
        }
    }
}
