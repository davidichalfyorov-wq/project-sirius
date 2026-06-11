#include "includes/ColorSpace.hlsl"

Texture2D<float4> Texture : register(t0, TEXTURE_SPACE);
SamplerState Sampler : register(s0, TEXTURE_SPACE);

struct PSInput
{
    float2 texCoord: TEXCOORD0;
};

cbuffer MaterialParameters: register(b2, UNIFORM_SPACE)
{
    float4 Dc;
};

float4 main(PSInput input) : SV_Target0
{
    float4 tex = SampleColorTexture(Texture, Sampler, input.texCoord);
    return tex * float4(SrgbToLinear(Dc.rgb), Dc.a);
}
