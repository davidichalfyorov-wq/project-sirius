#include "includes/ColorSpace.hlsl"

Texture2D<float4> Texture : register(t0, TEXTURE_SPACE);
SamplerState Sampler : register(s0, TEXTURE_SPACE);

struct PSInput
{
    float2 texCoord: TEXCOORD0;
    float4 color: TEXCOORD1;
};

float4 main(PSInput input) : SV_Target0
{
    // ALE particle tints are display-referred like everything authored.
    float4 tex = SampleColorTexture(Texture, Sampler, input.texCoord);
    return tex * float4(SrgbToLinear(input.color.rgb), input.color.a);
}
