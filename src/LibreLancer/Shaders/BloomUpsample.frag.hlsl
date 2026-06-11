Texture2D<float4> SourceTexture : register(t0, TEXTURE_SPACE);
SamplerState SourceSampler : register(s0, TEXTURE_SPACE);

cbuffer BloomUpsampleParameters : register(b3, UNIFORM_SPACE)
{
    float4 Params; // xy: source texel size, z: filter radius scale
};

struct Input
{
    float2 texCoord : TEXCOORD0;
};

// 9-tap tent upsample, blended additively onto the next-larger mip
// (pipeline sets BlendMode.Additive). Radius widens the tent for a
// softer falloff (bloom_radius setting).
float4 main(Input input) : SV_Target0
{
    float2 t = Params.xy * Params.z;
    float3 result = SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(-1, -1)).rgb;
    result += SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(0, -1)).rgb * 2.0;
    result += SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(1, -1)).rgb;
    result += SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(-1, 0)).rgb * 2.0;
    result += SourceTexture.Sample(SourceSampler, input.texCoord).rgb * 4.0;
    result += SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(1, 0)).rgb * 2.0;
    result += SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(-1, 1)).rgb;
    result += SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(0, 1)).rgb * 2.0;
    result += SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(1, 1)).rgb;
    return float4(result / 16.0, 1.0);
}
