// SMAA 1x pass 1/3: luma edge detection (graphics roadmap 4.7).
// Input: tonemapped LDR color (t0). Output: RG edges.

Texture2D ColorTex : register(t0, TEXTURE_SPACE);

#include "includes/SmaaPort.hlsl"

struct Input
{
    float2 texCoord : TEXCOORD0;
};

float4 main(Input input) : SV_Target0
{
    float4 offset[3];
    SMAAEdgeDetectionVS(input.texCoord, offset);
    float2 edges = SMAALumaEdgeDetectionPS(input.texCoord, offset, ColorTex);
    return float4(edges, 0.0, 1.0);
}
