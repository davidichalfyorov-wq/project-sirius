// SMAA 1x pass 3/3: neighborhood blending (graphics roadmap 4.7).
// Inputs: tonemapped LDR color (t0), blending weights (t1). Output: the
// anti-aliased frame. UI draws after this pass and stays sharp.

Texture2D ColorTex : register(t0, TEXTURE_SPACE);
Texture2D BlendTex : register(t1, TEXTURE_SPACE);

#include "includes/SmaaPort.hlsl"

struct Input
{
    float2 texCoord : TEXCOORD0;
};

float4 main(Input input) : SV_Target0
{
    float4 offset;
    SMAANeighborhoodBlendingVS(input.texCoord, offset);
    return SMAANeighborhoodBlendingPS(input.texCoord, offset, ColorTex, BlendTex);
}
