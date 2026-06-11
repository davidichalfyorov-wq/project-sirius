Texture2D<float4> SourceTexture : register(t0, TEXTURE_SPACE);
SamplerState SourceSampler : register(s0, TEXTURE_SPACE);

cbuffer BloomDownsampleParameters : register(b3, UNIFORM_SPACE)
{
    float4 Params; // xy: source texel size
};

struct Input
{
    float2 texCoord : TEXCOORD0;
};

// 13-tap downsample (Jimenez, "Next Generation Post Processing"):
// overlapping 2x2 blocks weighted 0.5/0.125 - cheap and stable.
float4 main(Input input) : SV_Target0
{
    float2 t = Params.xy;
    float3 a = SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(-2, -2)).rgb;
    float3 b = SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(0, -2)).rgb;
    float3 c = SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(2, -2)).rgb;
    float3 d = SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(-2, 0)).rgb;
    float3 e = SourceTexture.Sample(SourceSampler, input.texCoord).rgb;
    float3 f = SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(2, 0)).rgb;
    float3 g = SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(-2, 2)).rgb;
    float3 h = SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(0, 2)).rgb;
    float3 i = SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(2, 2)).rgb;
    float3 j = SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(-1, -1)).rgb;
    float3 k = SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(1, -1)).rgb;
    float3 l = SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(-1, 1)).rgb;
    float3 m = SourceTexture.Sample(SourceSampler, input.texCoord + t * float2(1, 1)).rgb;

    float3 result = e * 0.125;
    result += (a + c + g + i) * 0.03125;
    result += (b + d + f + h) * 0.0625;
    result += (j + k + l + m) * 0.125;
    return float4(result, 1.0);
}
