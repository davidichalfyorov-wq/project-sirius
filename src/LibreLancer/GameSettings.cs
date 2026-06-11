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
                Shadows = Shadows
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
