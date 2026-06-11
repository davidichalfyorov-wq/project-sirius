Texture2D<float4> MaskTexture : register(t0, TEXTURE_SPACE);
SamplerState MaskSampler : register(s0, TEXTURE_SPACE);

cbuffer GodRaysBlurParameters : register(b3, UNIFORM_SPACE)
{
    float4 SunPosition; // xy: sun uv
    float4 RayParams;   // x: density, y: decay, z: weight, w: sample count
};

struct Input
{
    float2 texCoord : TEXCOORD0;
};

// Screen-space radial blur from the sun position (roadmap 4.6).
float4 main(Input input) : SV_Target0
{
    int sampleCount = (int)RayParams.w;
    float2 uv = input.texCoord;
    float2 delta = (uv - SunPosition.xy) * (RayParams.x / RayParams.w);
    float3 result = (float3)0.0;
    float weight = RayParams.z;
    [loop]
    for (int i = 0; i < sampleCount; i++)
    {
        uv -= delta;
        result += MaskTexture.Sample(MaskSampler, uv).rgb * weight;
        weight *= RayParams.y;
    }
    return float4(result / RayParams.w, 1.0);
}
