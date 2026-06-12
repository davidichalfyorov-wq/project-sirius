#define PIXEL_SHADOWS 1
#include "includes/Lighting.hlsl"
#include "includes/Camera.hlsl"
#include "includes/Modulate.hlsl"
#include "includes/ColorSpace.hlsl"

Texture2D<float4> DtTexture : register(t0, TEXTURE_SPACE);
SamplerState DtSampler : register(s0, TEXTURE_SPACE);

Texture2D<float4> EtTexture : register(t1, TEXTURE_SPACE);
SamplerState EtSampler : register(s1, TEXTURE_SPACE);

Texture2D<float4> NtTexture : register(t2, TEXTURE_SPACE);
SamplerState NtSampler : register(s2, TEXTURE_SPACE);

Texture2D<float4> BtTexture : register(t4, TEXTURE_SPACE);
SamplerState BtSampler : register(s4, TEXTURE_SPACE);

#ifdef ENVMAP
TextureCube<float4> EnvTexture : register(t3, TEXTURE_SPACE);
SamplerState EnvSampler : register(s3, TEXTURE_SPACE);
#endif

struct Input
{
    float2 texCoord1: TEXCOORD0;
    float2 texCoord2: TEXCOORD1;
    float3 worldPosition: TEXCOORD2;
#ifdef NORMALMAP
    float3x3 tbn: TEXCOORD3;
#else
    float3 normal: TEXCOORD3;
#endif
    float4 color: TEXCOORD4;
    float4 viewPosition: TEXCOORD5;
#ifdef RTAO
    float4 screenPos: SV_Position;
#endif
#ifdef VERTEX_LIGHTING
    float3 diffuseTermFront: TEXCOORD6;
    float3 diffuseTermBack: TEXCOORD7;
    float3 ambientTermFront: TEXCOORD8;
    float3 ambientTermBack: TEXCOORD9;
#endif
#ifdef ENVMAP
    float3 viewSpaceReflection: TEXCOORD10;
#endif
    bool frontFacing: SV_IsFrontFace;
};

cbuffer MaterialParameters : register(b3, UNIFORM_SPACE)
{
    float4 Dc;
    float4 Ec;
    float2 FadeRange;
    float Oc;
    float Tex2Type; //>0 for BtSampler
    float DebugMode;
    float3 _debugPad;
};

cbuffer TexCoordSelectors : register(b5, UNIFORM_SPACE)
{
    int4 TexCoordSelectors;
};

float2 GetTexCoord(int index, Input input)
{
    return TexCoordSelectors[index] > 0 ? input.texCoord2 : input.texCoord1;
}
#ifdef NORMALMAP
float3 getNormal(Input input)
{
    float3 n = NtTexture.Sample(NtSampler, GetTexCoord(2, input)).xyz;
    n.xy = (n.xy * 2.0 - 1.0);
    n.z = sqrt(1.0 - dot(n.xy, n.xy));
    n = normalize(n);
    return normalize(mul(input.tbn, n));
}
#else
float3 getNormal(Input input)
{
    return normalize(input.normal);
}
#endif

float4 main(Input input) : SV_Target0
{
    float4 dtSampled = SampleColorTexture(DtTexture, DtSampler, GetTexCoord(0, input));
#ifdef ALPHATEST_ENABLED
    if (dtSampled.a < 0.5)
    {
        discard;
    }
#endif
    float4 ec = Ec;
#ifdef TEX2_ENABLED
    if (Tex2Type < 1)
    {
        ec += SampleColorTexture(EtTexture, EtSampler, GetTexCoord(1, input));
    }
#endif
    float4 ac = float4(1.0, 1.0, 1.0, 1.0);

#if defined(RTAO) && !defined(VERTEX_LIGHTING)
    AmbientOcclusionTerm = ComputeRtao(input.worldPosition,
        input.frontFacing ? getNormal(input) : -getNormal(input),
        input.screenPos.xy);
#endif
#ifdef VERTEX_LIGHTING
    float4 color = ApplyVertexLighting(ac, ec, Dc * input.color,
        dtSampled,
        input.viewPosition,
        input.frontFacing ? input.diffuseTermFront : input.diffuseTermBack,
        input.frontFacing ? input.ambientTermFront : input.ambientTermBack);
#else
    float4 color = ApplyPixelLighting(ac, ec, Dc * input.color,
        dtSampled,
        input.worldPosition, input.viewPosition,
        getNormal(input), input.frontFacing);
#endif

#ifdef TEX2_ENABLED
    if(Tex2Type >= 1)
    {
        float4 bt = SampleColorTexture(BtTexture, BtSampler, GetTexCoord(3, input));
        color.rgb = Mod2x(color.rgb, bt.rgb);
    }
#endif

#ifdef ENVMAP
    float4 env = EnvTexture.Sample(EnvSampler, input.viewSpaceReflection);
    float3 envrgb = 2 * SrgbToLinear(env.rgb) * color.rgb;
#ifdef RT_REFLECTIONS
    // Glass hulls mirror real scene geometry; miss keeps the cubemap.
    {
        float3 viewDir = normalize(input.worldPosition - CameraPosition);
        float3 reflNormal = input.frontFacing ? getNormal(input) : -getNormal(input);
        float3 reflDir = reflect(viewDir, reflNormal);
        float reflShade;
        if (ComputeRtReflection(input.worldPosition, reflDir, reflNormal, reflShade) > 0.5)
        {
            envrgb = lerp(envrgb, float3(reflShade, reflShade, reflShade) * color.rgb * 2.0, 0.65);
        }
    }
#endif
    color = float4(envrgb, color.a);
#endif
    float4 acolor = color * float4(1.0, 1.0, 1.0, Oc);

#ifdef DEBUG_VIEW
    // Channel views (roadmap 9.4, SIRIUS_DEBUG_VIEW). Channels with no
    // data in the fixed-function material model show magenta.
    if (DebugMode > 0.5)
    {
        int debugChannel = (int)(DebugMode + 0.5);
        if (debugChannel == 1) return float4((Dc * input.color * dtSampled).rgb, 1.0);
        if (debugChannel == 2) return float4(getNormal(input) * 0.5 + 0.5, 1.0);
        if (debugChannel == 5)
        {
            float4 shadowDebug = float4(0.5, 0.0, 0.5, 1.0);
            float3 debugNormal = input.frontFacing ? getNormal(input) : -getNormal(input);
            for (int debugLight = 0; debugLight < MAX_LIGHTS; debugLight++)
            {
                if (debugLight >= int(LightCount)) break;
                if (Lights[debugLight].Type == 0 || Lights[debugLight].CastsShadow > 0)
                {
                    float3 toLight = Lights[debugLight].Type == 0
                        ? normalize(-Lights[debugLight].Direction)
                        : normalize(Lights[debugLight].Position - input.worldPosition);
                    shadowDebug = SampleShadowDebug(input.worldPosition, debugNormal,
                        toLight, length(input.viewPosition.xyz));
                    break;
                }
            }
            return shadowDebug;
        }
        return float4(1.0, 0.0, 1.0, 1.0);
    }
#endif


#ifdef FADE_ENABLED
    float dist = length(input.viewPosition);
    //FadeRange - x: near, y: far
    float fadeFactor = (FadeRange.y - dist) / (FadeRange.y - FadeRange.x);
    fadeFactor = clamp(fadeFactor, 0.0, 1.0);
    return float4(acolor.rgb, acolor.a * fadeFactor);
#else
    return acolor;
#endif
}
