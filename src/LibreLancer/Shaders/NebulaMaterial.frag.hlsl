#include "includes/ColorSpace.hlsl"

Texture2D<float4> Texture : register(t0, TEXTURE_SPACE);
SamplerState Sampler : register(s0, TEXTURE_SPACE);

struct PSInput
{
    float2 texCoord: TEXCOORD0;
    float4 color: TEXCOORD1;
};

cbuffer MaterialParameters : register(b3, UNIFORM_SPACE)
{
    float4 Dc;
};

float4 main(PSInput input) : SV_Target0
{
    // Texture, INI zone colour and vertex tint are all display-referred.
    float4 tex = SampleColorTexture(Texture, Sampler, input.texCoord);
    float4 tint = Dc * input.color;
    return tex * float4(SrgbToLinear(tint.rgb), tint.a);
}
