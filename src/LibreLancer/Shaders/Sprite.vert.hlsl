#include "includes/Camera.hlsl"

struct VSInput
{
    [[vk::location(0)]] float3 position: POSITION;
    [[vk::location(1)]] float4 color: COLOR;
    [[vk::location(3)]] float2 uv: TEXCOORD0;
};

struct Output
{
    float2 texCoord : TEXCOORD0;
    float4 color : TEXCOORD1;
    float3 worldPosition : TEXCOORD2;
    float4 viewPosition : TEXCOORD3;
    float4 position : SV_Position;
};

Output main(VSInput input)
{
    Output output;
    float4 worldPos = float4(input.position, 1.0);
    output.texCoord = input.uv;
    output.color = input.color;
    output.worldPosition = worldPos.xyz;
    output.viewPosition = mul(worldPos, View);
    output.position = mul(worldPos, ViewProjection);
    return output;
}
