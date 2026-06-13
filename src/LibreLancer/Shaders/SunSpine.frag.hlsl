#include "includes/ColorSpace.hlsl"
#include "includes/VolumetricMaterialFog.hlsl"

Texture2D<float4> Texture : register(t0, TEXTURE_SPACE);
SamplerState Sampler : register(s0, TEXTURE_SPACE);

struct Input
{
    float2 texCoord : TEXCOORD0;
    float4 innerColor : TEXCOORD1;
    float4 outerColor : TEXCOORD2;
    float3 worldPosition : TEXCOORD3;
    float4 viewPosition : TEXCOORD4;
    float4 position : SV_Position;
};

float4 main(Input input) : SV_Target0
{
    float4 texSample = SampleColorTexture(Texture, Sampler, input.texCoord);
    float dist = distance(float2(0.5, 0.5), input.texCoord) * 2.;
    float4 blendColor = lerp(input.innerColor, input.outerColor, dist);
    float4 color = texSample * float4(SrgbToLinear(blendColor.rgb), blendColor.a);
    return ApplyVolumetricFogAdditive(input.worldPosition, length(input.viewPosition.xyz), color);
}
