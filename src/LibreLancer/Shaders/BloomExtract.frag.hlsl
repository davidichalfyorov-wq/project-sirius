Texture2D<float4> SceneTexture : register(t0, TEXTURE_SPACE);
SamplerState SceneSampler : register(s0, TEXTURE_SPACE);

cbuffer BloomExtractParameters : register(b3, UNIFORM_SPACE)
{
    float4 Params; // x: threshold, y: soft knee, zw: source texel size
};

struct Input
{
    float2 texCoord : TEXCOORD0;
};

float Luminance(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

float3 Contribution(float3 c)
{
    float luminance = Luminance(c);
    float gate = smoothstep(Params.x - Params.y, Params.x + Params.y, luminance);
    return c * gate;
}

// Half-res bright-pass. The 4 taps with luma weights (Karis average)
// stop single hot pixels from strobing the whole chain - the roadmap's
// "no persistent flicker in particle-heavy scenes" gate.
float4 main(Input input) : SV_Target0
{
    float2 texel = Params.zw;
    float3 a = Contribution(SceneTexture.Sample(SceneSampler, input.texCoord + texel * float2(-0.5, -0.5)).rgb);
    float3 b = Contribution(SceneTexture.Sample(SceneSampler, input.texCoord + texel * float2(0.5, -0.5)).rgb);
    float3 c = Contribution(SceneTexture.Sample(SceneSampler, input.texCoord + texel * float2(-0.5, 0.5)).rgb);
    float3 d = Contribution(SceneTexture.Sample(SceneSampler, input.texCoord + texel * float2(0.5, 0.5)).rgb);
    float wa = 1.0 / (1.0 + Luminance(a));
    float wb = 1.0 / (1.0 + Luminance(b));
    float wc = 1.0 / (1.0 + Luminance(c));
    float wd = 1.0 / (1.0 + Luminance(d));
    float3 result = (a * wa + b * wb + c * wc + d * wd) / (wa + wb + wc + wd);
    return float4(result, 1.0);
}
