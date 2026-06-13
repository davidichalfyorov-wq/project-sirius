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
        // Phase 5 track V: froxel volumetric nebulae (Vulkan/compute only).
        // Default on after V12; Validate() disables it on backends without compute.
        [Entry("volumetric_nebulae")]
        public bool VolumetricNebulae = true;
        // 0 low, 1 medium, 2 high, 3 ultra. High matches the P5 baseline.
        [Entry("volumetric_quality")]
        public int VolumetricQuality = 2;

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
        bool IRendererSettings.SelectedVolumetricNebulae => VolumetricNebulae;
        int IRendererSettings.SelectedVolumetricQuality => VolumetricQuality;
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
            writer.WriteLine($"volumetric_nebulae = {(VolumetricNebulae ? "true" : "false")}");
            writer.WriteLine($"volumetric_quality = {VolumetricQuality}");
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
                VolumetricNebulae = VolumetricNebulae,
                VolumetricQuality = VolumetricQuality
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
            if (VolumetricNebulae && !RenderContext.HasFeature(GraphicsFeature.Compute))
            {
                FLLog.Info("Config", "volumetric_nebulae requires compute support, disabling.");
                VolumetricNebulae = false;
            }
            VolumetricQuality = System.Math.Clamp(VolumetricQuality, 0, 3);
            if (!PostAA.Equals("off", System.StringComparison.OrdinalIgnoreCase) &&
                !PostAA.Equals("fxaa", System.StringComparison.OrdinalIgnoreCase) &&
                !PostAA.Equals("smaa", System.StringComparison.OrdinalIgnoreCase))
            {
                FLLog.Info("Config", $"Unknown post_aa '{PostAA}', using 'off'.");
                PostAA = "off";
            }
        }
    }
}
