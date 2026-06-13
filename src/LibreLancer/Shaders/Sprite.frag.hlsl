#include "includes/ColorSpace.hlsl"
#include "includes/VolumetricMaterialFog.hlsl"

Texture2D<float4> Texture : register(t0, TEXTURE_SPACE);
SamplerState Sampler : register(s0, TEXTURE_SPACE);

struct PSInput
{
    float2 texCoord: TEXCOORD0;
    float4 color: TEXCOORD1;
    float3 worldPosition : TEXCOORD2;
    float4 viewPosition : TEXCOORD3;
};

float4 main(PSInput input) : SV_Target0
{
    // ALE particle tints are display-referred like everything authored.
    float4 tex = SampleColorTexture(Texture, Sampler, input.texCoord);
    float alpha = tex.a * input.color.a;
    // Some legacy particle atlases keep 1/255 alpha in transparent padding.
    // Blending that padding shows up as rotating dark cards in dense smoke.
    if (alpha <= (1.5 / 255.0))
        discard;

    float4 color = float4(tex.rgb * SrgbToLinear(input.color.rgb), alpha);
    float viewDist = length(input.viewPosition.xyz);
    return ApplyVolumetricFogAdditive(input.worldPosition, viewDist, color);
}
