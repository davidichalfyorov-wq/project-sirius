// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using LibreLancer.Data.GameData;
using LibreLancer.Graphics;
using LibreLancer.Graphics.Vertices;
using LibreLancer.Resources;
using LibreLancer.Utf.Cmp;
using LibreLancer.Utf.Mat;

namespace LibreLancer.Render
{
    public abstract class RenderMaterial
    {
        private static int _key = 0;

        public int Key { get; private set; }

        protected RenderMaterial(ResourceManager? library)
        {
            Library = library;
            Key = Interlocked.Increment(ref _key);
        }

        public static bool VertexLighting = false;
        public MaterialAnim? MaterialAnim;
        public WorldMatrixHandle World = new();
        public ResourceManager? Library;
        public bool Fade = false;
        public float FadeNear = 0;
        public float FadeFar = 0;
        public float OpacityMultiplier = 1;
        public StorageBuffer? Bones;
        public int BufferOffset;
        public abstract void Use(RenderContext rstate, IVertexType vertextype, ref Lighting lights, int userData);
        public abstract bool IsTransparent { get; }

        public virtual bool DisableCull
        {
            get { return false; }
        }

        // Translucent volumes (atmosphere shells) must not write depth or
        // they occlude everything behind the shell when seen from inside.
        public virtual bool DisableDepthWrite
        {
            get { return false; }
        }

        public bool DoubleSided = false;
        private Texture?[] textures = new Texture?[8];
        private bool[] loaded = new bool[8];

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct PackedLight
        {
            public Vector3 Position;
            public float Type;
            public Color3f Diffuse;
            public float Range;
            public Color3f Ambient;
            public float CastsShadow;
            public Vector3 Attentuation;
            private float _padding1;
            public Vector3 Direction;
            private float _padding2;
            public float Spotlight;
            public float Falloff;
            public float Theta;
            public float Phi;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct ShaderLighting
        {
            public Vector2 FogRange;
            public float UseLighting;
            public float FogMode;
            public Color3f AmbientColor;
            public float LightCount;
            public Color3f FogColor;
            public float _padding;
            public PackedLight Light0;
            public PackedLight Light1;
            public PackedLight Light2;
            public PackedLight Light3;
            public PackedLight Light4;
            public PackedLight Light5;
            public PackedLight Light6;
            public PackedLight Light7;
            public PackedLight Light8;
            public Vector4 VolFogParams;
            public Vector4 VolFogParams2;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct VolumetricFogBlock
        {
            public Vector4 VolFogParams;
            public Vector4 VolFogParams2;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WorldBuffer
        {
            public Matrix4x4 WorldMatrix;
            public Matrix4x4 NormalMatrix;
        }

        protected unsafe void SetWorld(Shader shader, Matrix4x4 world, Matrix4x4 normal)
        {
            shader.UniformBlockTag(0) = ulong.MaxValue;
            var b = new WorldBuffer() { WorldMatrix = world, NormalMatrix = normal };
            shader.SetUniformBlock(0, ref b);
        }

        protected unsafe void SetWorld(Shader shader)
        {
            if (World.Source == (Matrix4x4*) 0)
            {
                SetWorld(shader, Matrix4x4.Identity, Matrix4x4.Identity);
            }
            else if (shader.HasUniformBlock(0) &&
                     World.ID == ulong.MaxValue || shader.UniformBlockTag(0) != World.ID)
            {
                shader.SetUniformBlock(0, ref Unsafe.AsRef<WorldBuffer>(World.Source), true);
                shader.UniformBlockTag(0) = World.ID;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct ShadowDataBuffer
        {
            public Matrix4x4 Matrix0;
            public Matrix4x4 Matrix1;
            public Matrix4x4 Matrix2;
            public Vector4 Splits;
            public Vector4 Params;
        }

        /// <summary>Cascade source for this frame; set by SystemRenderer.</summary>
        public static ShadowMapRenderer? ActiveShadows;

        // The system light the cascade atlas was built around this frame
        // (FL suns are point lights; the shader needs to know which slot
        // samples the atlas). Null when no shadow pass ran.
        public static DynamicLight? ShadowLight;

        // True while the frame has both an active sun shadow pass and a
        // built TLAS; materials then pick RT_SHADOWS permutations (phase 4).
        public static bool RtShadowsActive;

        // True when this frame's volumetric nebula path owns fogging.
        // RenderHelpers uses this to stop legacy Linear fog being submitted
        // on top of the fullscreen volume composite.
        public static bool VolumetricFogActive;

        // Set only around the transparent pass: opaque geometry is already
        // covered by the fullscreen depth composite.
        public static bool VolumetricFogMaterialActive;

        public static Texture3D? VolumetricFogIntegrated;
        public static Vector4 VolumetricFogSettings;
        public static Texture3D? AtmosphereTransmittance;
        public static Texture3D? AtmosphereMultiScattering;
        public static Texture3D? AtmosphereSkyView;
        public static Vector4 AtmosphereLutSettings;
        public static Texture3D? AtmosphereAerial;
        public static Vector4 AtmosphereAerialSettings;
        public static Texture3D? AtmosphereCloudShell;
        public static Vector4 AtmosphereCloudShellSettings;

        // Ray-traced ambient occlusion (phase 4): modulates ambient/IBL
        // terms in the PBR shader while a TLAS is being built this frame.
        public static bool RtaoActive;

        // Ray-traced mirror reflections (phase 4): glass/smooth surfaces
        // trace one ray against the TLAS instead of trusting the cubemap.
        public static bool RtReflectionsActive;

        // SIRIUS_DEBUG_VIEW channel views (roadmap 9.4), baked as a
        // vulkan-only PBR permutation - GL silently keeps the lit path.
        // 1 albedo, 2 normals, 3 roughness, 4 metallic, 5 shadow, 6 ibl.
        public static readonly int DebugViewMode =
            Environment.GetEnvironmentVariable("SIRIUS_DEBUG_VIEW")?.ToLowerInvariant() switch
            {
                "albedo" or "1" => 1,
                "normals" or "2" => 2,
                "roughness" or "3" => 3,
                "metallic" or "4" => 4,
                "shadow" or "5" => 5,
                "ibl" or "6" => 6,
                _ => 0
            };

        // Ф2.0.2: IBL probe intensity multiplier (anti-blackness of ships),
        // applied post-sample in linear (PBR.frag.hlsl) so the sRGB-8bit probe
        // store does not clamp the boost. 1 = bitwise-neutral. SystemRenderer
        // pushes this each frame from SIRIUS_IBL_INTENSITY env or GameSettings
        // (Ф2.0.4); the static default keeps non-system paths neutral.
        public static float IblIntensity = 1f;

        // G-buffer MRT (graphics phase 0.1). Env-gated until the settings UI
        // lands (checklist 0.1.8); default off => renderer is byte-identical.
        // GBufferActive binds RT1 (world-normal + roughness) in the opaque
        // pass; GBufferShow blits RT1 over the final frame for visual QA.
        public static readonly bool GBufferActive =
            Environment.GetEnvironmentVariable("SIRIUS_GBUFFER") == "1";
        public static readonly bool GBufferShow =
            Environment.GetEnvironmentVariable("SIRIUS_GBUFFER_SHOW") == "1";

        // True only while the opaque MRT pass is recording (set by
        // SystemRenderer). PBR materials emit the 2-output GBUFFER shader
        // ONLY then; transparent/other passes keep the single-target shader so
        // the output count always matches the bound attachment count.
        public static bool GBufferPassActive;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private unsafe struct LocalShadowBuffer
        {
            public Matrix4x4 Matrix0;
            public Matrix4x4 Matrix1;
            public Matrix4x4 Matrix2;
            public Matrix4x4 Matrix3;
            public Vector4 Pos0;
            public Vector4 Pos1;
            public Vector4 Pos2;
            public Vector4 Pos3;
            public Vector4 Params;
        }

        private static void SetShadowData(Shader shader)
        {
            if (shader.HasUniformBlock(6))
            {
                var data = new ShadowDataBuffer();
                if (ActiveShadows != null)
                {
                    data.Matrix0 = ActiveShadows.LightViewProjection[0];
                    data.Matrix1 = ActiveShadows.LightViewProjection[1];
                    data.Matrix2 = ActiveShadows.LightViewProjection[2];
                    data.Splits = ActiveShadows.CascadeSplits;
                    // y: 1/cascade tiles, z: linear depth bias (~9m of the
                    // 30km light range, against acne on big curved hulls).
                    data.Params = new Vector4(1f, 1f / ShadowMapRenderer.Cascades, 0.0003f, 0f);
                }
                shader.SetUniformBlock(6, ref data);
            }
            if (shader.HasUniformBlock(7))
            {
                var local = new LocalShadowBuffer();
                if (ActiveShadows is { LocalCount: > 0 } shadows)
                {
                    local.Matrix0 = shadows.LocalViewProjection[0];
                    local.Matrix1 = shadows.LocalViewProjection[1];
                    local.Matrix2 = shadows.LocalViewProjection[2];
                    local.Matrix3 = shadows.LocalViewProjection[3];
                    local.Pos0 = WithEnabled(shadows.LocalPositions[0], 0 < shadows.LocalCount);
                    local.Pos1 = WithEnabled(shadows.LocalPositions[1], 1 < shadows.LocalCount);
                    local.Pos2 = WithEnabled(shadows.LocalPositions[2], 2 < shadows.LocalCount);
                    local.Pos3 = WithEnabled(shadows.LocalPositions[3], 3 < shadows.LocalCount);
                    local.Params = new Vector4(shadows.LocalCount, 0f, 0.002f, 0f);
                }
                shader.SetUniformBlock(7, ref local);
            }

            static Vector4 WithEnabled(Vector4 position, bool enabled) =>
                enabled ? position with { W = MathF.Max(position.W, 1e-6f) } : position with { W = 0f };
        }

        public static void SetVolumetricFogSource(Texture3D? integrated, Vector4 settings)
        {
            VolumetricFogIntegrated = integrated;
            VolumetricFogSettings = settings;
        }

        public static void SetAtmosphereAerialSource(Texture3D? aerial, Vector4 settings)
        {
            AtmosphereAerial = aerial;
            AtmosphereAerialSettings = settings;
        }

        public static void SetAtmosphereCloudShellSource(Texture3D? cloudShell, Vector4 settings)
        {
            AtmosphereCloudShell = cloudShell;
            AtmosphereCloudShellSettings = settings;
        }

        public static void SetAtmosphereLutSource(Texture3D? transmittance, Texture3D? multiScattering,
            Texture3D? skyView = null)
        {
            AtmosphereTransmittance = transmittance;
            AtmosphereMultiScattering = multiScattering;
            AtmosphereSkyView = skyView;
            AtmosphereLutSettings = new Vector4(
                transmittance != null && multiScattering != null ? 1f : 0f,
                0f,
                0f,
                0f);
        }

        private static VolumetricFogBlock BuildVolumetricFogBlock(bool extinctionOnly)
        {
            var active = VolumetricFogActive &&
                         VolumetricFogMaterialActive &&
                         VolumetricFogIntegrated != null;
            var aerialActive = AtmosphereAerial != null && AtmosphereAerialSettings.X > 0f;
            return new VolumetricFogBlock
            {
                VolFogParams = new Vector4(active ? 1f : 0f,
                    VolumetricFogSettings.X,
                    VolumetricFogSettings.Y,
                    VolumetricFogSettings.Z),
                VolFogParams2 = new Vector4(VolumetricFogSettings.W,
                    extinctionOnly ? 1f : 0f,
                    aerialActive ? 1f : 0f,
                    AtmosphereAerialSettings.Y)
            };
        }

        internal static void SetVolumetricFog(Shader shader, RenderContext rstate, bool extinctionOnly)
        {
            BindVolumetricFogTexture(rstate);
            if (shader.HasUniformBlock(2))
            {
                var data = BuildVolumetricFogBlock(extinctionOnly);
                shader.SetUniformBlock(2, ref data);
            }
        }

        private static void BindVolumetricFogTexture(RenderContext rstate)
        {
            if (AtmosphereAerial != null)
            {
                rstate.Textures[14] = AtmosphereAerial;
                rstate.Samplers[14] = SamplerState.LinearClamp;
            }
            if (VolumetricFogIntegrated == null)
            {
                return;
            }
            rstate.Textures[15] = VolumetricFogIntegrated;
            rstate.Samplers[15] = SamplerState.LinearClamp;
        }

        protected static void BindAtmosphereLuts(RenderContext rstate)
        {
            if (AtmosphereTransmittance != null)
            {
                rstate.Textures[11] = AtmosphereTransmittance;
                rstate.Samplers[11] = SamplerState.LinearClamp;
            }
            if (AtmosphereMultiScattering != null)
            {
                rstate.Textures[12] = AtmosphereMultiScattering;
                rstate.Samplers[12] = SamplerState.LinearClamp;
            }
            var aux = AtmosphereCloudShell ?? AtmosphereSkyView;
            if (aux != null)
            {
                rstate.Textures[13] = aux;
                rstate.Samplers[13] = SamplerState.LinearClamp;
            }
        }

        internal static bool IsAdditiveBlend(ushort blendMode)
        {
            var (_, dst) = BlendMode.Deconstruct(blendMode);
            return dst == BlendOp.One;
        }

        public static unsafe void SetLights(Shader shader, RenderContext rstate, ref Lighting lighting, long frameNumber)
        {
            SetShadowData(shader);
            BindVolumetricFogTexture(rstate);
            var data = new ShaderLighting();
            var fogBlock = BuildVolumetricFogBlock(true);
            data.VolFogParams = fogBlock.VolFogParams;
            data.VolFogParams2 = fogBlock.VolFogParams2;
            if (!lighting.Enabled)
            {
                shader.SetUniformBlock<ShaderLighting>(2, ref data, false, sizeof(ShaderLighting));
                return;
            }

            // INI light/fog colours are display-referred: decode once here
            // so the shader math runs linear (docs/LINEAR_AUDIT.md).
            data.UseLighting = 1;
            data.FogMode = (float) lighting.FogMode;
            data.FogRange = lighting.FogRange;
            data.FogColor = ColorSpace.SrgbToLinear(lighting.FogColor);
            data.AmbientColor = ColorSpace.SrgbToLinear(lighting.Ambient);

            var lt = 0;
            var lights = new Span<PackedLight>(&data.Light0, 9);

            for (var i = 0; i < lighting.Lights.SourceLighting.Lights.Count; i++)
            {
                if (!lighting.Lights.SourceEnabled[i])
                {
                    continue;
                }

                var src = lighting.Lights.SourceLighting.Lights[i].Light;
                lights[lt].Position = src.Position;
                lights[lt].Attentuation = src.Attenuation;
                lights[lt].Direction = src.Direction;
                lights[lt].Diffuse = ColorSpace.SrgbToLinear(src.Color);
                lights[lt].Ambient = ColorSpace.SrgbToLinear(src.Ambient);
                lights[lt].CastsShadow =
                    ReferenceEquals(lighting.Lights.SourceLighting.Lights[i], ShadowLight) ? 1 : 0;
                lights[lt].Range = src.Range;

                if (src.Kind == LightKind.Spotlight)
                {
                    lights[lt].Spotlight = 1;
                    lights[lt].Theta = MathF.Cos(MathHelper.DegreesToRadians(src.Theta) * 0.5f);
                    lights[lt].Phi = MathF.Cos(MathHelper.DegreesToRadians(src.Phi) * 0.5f);
                    lights[lt].Falloff = src.Falloff;
                }

                if (src.Kind == LightKind.Point || src.Kind == LightKind.Spotlight)
                {
                    lights[lt].Type = 1;
                }
                else if (src.Kind == LightKind.PointAttenCurve)
                {
                    lights[lt].Type = 2;
                }

                lt++;

                if (lt >= lights.Length)
                {
                    break;
                }
            }

            data.LightCount = lt;
            shader.SetUniformBlock<ShaderLighting>(2, ref data, false, sizeof(ShaderLighting));
        }

        protected Texture? GetTexture(int cacheIndex, string? tex)
        {
            if (tex == null || Library is null)
            {
                return Library?.FindTexture(ResourceManager.NullTextureName);
            }

            textures[cacheIndex] ??= Library.FindTexture(tex);
            var texture = textures[cacheIndex];

            if (texture == null)
            {
                return texture;
            }

            if (texture.IsDisposed)
            {
                textures[cacheIndex] = Library.FindTexture(tex);
            }

            return textures[cacheIndex];
        }

        protected void SetTextureCoordinates(Shader shader, SamplerFlags t0, SamplerFlags t1 = 0, SamplerFlags t2 = 0,
            SamplerFlags t3 = 0)
        {
            Vector4i flags = new(
                (t0 & SamplerFlags.SecondUV) == SamplerFlags.SecondUV ? 1 : 0,
                (t1 & SamplerFlags.SecondUV) == SamplerFlags.SecondUV ? 1 : 0,
                (t2 & SamplerFlags.SecondUV) == SamplerFlags.SecondUV ? 1 : 0,
                (t3 & SamplerFlags.SecondUV) == SamplerFlags.SecondUV ? 1 : 0
            );

            if (shader.HasUniformBlock(5))
            {
                shader.SetUniformBlock(5, ref flags);
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Flags2
        {
            public Vector4i A;
            public Vector4i B;
        }

        protected void SetTextureCoordinates(Shader shader, SamplerFlags t0, SamplerFlags t1, SamplerFlags t2,
            SamplerFlags t3, SamplerFlags t4, SamplerFlags t5 = 0)
        {
            var f2 = new Flags2
            {
                A = new(
                    (t0 & SamplerFlags.SecondUV) == SamplerFlags.SecondUV ? 1 : 0,
                    (t1 & SamplerFlags.SecondUV) == SamplerFlags.SecondUV ? 1 : 0,
                    (t2 & SamplerFlags.SecondUV) == SamplerFlags.SecondUV ? 1 : 0,
                    (t3 & SamplerFlags.SecondUV) == SamplerFlags.SecondUV ? 1 : 0
                ),
                B = new(
                    (t4 & SamplerFlags.SecondUV) == SamplerFlags.SecondUV ? 1 : 0,
                    (t5 & SamplerFlags.SecondUV) == SamplerFlags.SecondUV ? 1 : 0,
                    0, 0
                )
            };

            if (shader.HasUniformBlock(5))
            {
                shader.SetUniformBlock(5, ref f2);
            }
        }

        protected void BindTexture(RenderContext rstate, int cacheidx, string? tex, int unit, SamplerFlags flags,
            string? nullName = null)
        {
            if (tex == null)
            {
                tex = nullName ?? ResourceManager.NullTextureName;
            }

            if (textures[cacheidx] == null || !loaded[cacheidx])
            {
                textures[cacheidx] = Library?.FindTexture(tex);
            }

            if (textures[cacheidx] == null)
            {
                textures[cacheidx] = Library?.FindTexture(ResourceManager.NullTextureName);
                loaded[cacheidx] = false;
            }
            else
            {
                loaded[cacheidx] = true;
            }

            var texture = textures[cacheidx];

            if (texture?.IsDisposed ?? true)
            {
                texture = textures[cacheidx] = (Texture2D?)Library?.FindTexture(tex);
            }

            if (texture == null)
            {
                texture = Library?.FindTexture(ResourceManager.NullTextureName);
            }

            var wrapS = WrapMode.Repeat;
            var wrapT = WrapMode.Repeat;
            if ((flags & SamplerFlags.ClampToEdgeU) == SamplerFlags.ClampToEdgeU)
            {
                wrapS = WrapMode.ClampToEdge;
            }
            if ((flags & SamplerFlags.ClampToEdgeV) == SamplerFlags.ClampToEdgeV)
            {
                wrapT = WrapMode.ClampToEdge;
            }
            if ((flags & SamplerFlags.MirrorRepeatU) == SamplerFlags.MirrorRepeatU)
            {
                wrapS = WrapMode.MirroredRepeat;
            }
            if ((flags & SamplerFlags.MirrorRepeatV) == SamplerFlags.MirrorRepeatV)
            {
                wrapT = WrapMode.MirroredRepeat;
            }
            rstate.Textures[unit] = texture;
            rstate.Samplers[unit] = new(rstate.PreferredFilterLevel, wrapS, wrapT);
        }
    }
}
