struct Input
{
    float2 texCoord1: TEXCOORD0;
    float2 texCoord2: TEXCOORD1;
    float3 worldPosition: TEXCOORD2;
    float3 normal: TEXCOORD3;
    float4 color: TEXCOORD4;
    float4 viewPosition: TEXCOORD5;
};

cbuffer ShadowCasterParameters : register(b3, UNIFORM_SPACE)
{
    float4 Params; // x: 1 / light far plane
};

// Linear light-space depth packed into RGB (24-bit fixed point): the
// engine's depth buffers aren't sampleable, so the cascade atlas is a
// plain colour target (roadmap 5.4 adapted).
float4 main(Input input) : SV_Target0
{
    float depth = saturate(-input.viewPosition.z * Params.x);
    float3 packed = frac(depth * float3(1.0, 255.0, 65025.0));
    packed -= packed.yzz * float3(1.0 / 255.0, 1.0 / 255.0, 0.0);
    return float4(packed, 1.0);
}
