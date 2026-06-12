// SMAA 1x pass 2/3: blending weight calculation (graphics roadmap 4.7).
// Inputs: edges (t0), AreaTex LUT (t1), SearchTex LUT (t2). Output: RGBA
// blending weights.

Texture2D EdgesTex : register(t0, TEXTURE_SPACE);
Texture2D AreaTex : register(t1, TEXTURE_SPACE);
Texture2D SearchTex : register(t2, TEXTURE_SPACE);

#include "includes/SmaaPort.hlsl"

struct Input
{
    float2 texCoord : TEXCOORD0;
};

float4 main(Input input) : SV_Target0
{
    float2 pixcoord;
    float4 offset[3];
    SMAABlendingWeightCalculationVS(input.texCoord, pixcoord, offset);
    return SMAABlendingWeightCalculationPS(input.texCoord, pixcoord, offset,
        EdgesTex, AreaTex, SearchTex, float4(0.0, 0.0, 0.0, 0.0));
}
