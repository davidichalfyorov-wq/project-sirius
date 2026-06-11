Texture2D<float4> SceneTexture : register(t0, TEXTURE_SPACE);
SamplerState SceneSampler : register(s0, TEXTURE_SPACE);

Texture2D<float4> BloomTexture : register(t1, TEXTURE_SPACE);
SamplerState BloomSampler : register(s1, TEXTURE_SPACE);

Texture2D<float4> RaysTexture : register(t2, TEXTURE_SPACE);
SamplerState RaysSampler : register(s2, TEXTURE_SPACE);

cbuffer TonemapParameters : register(b3, UNIFORM_SPACE)
{
    float4 Params; // x: exposure, y: mode (0 = off, 1 = ACES, 2 = filmic shoulder), z: bloom intensity, w: god rays intensity
};

struct Input
{
    float2 texCoord : TEXCOORD0;
};

// ACES filmic fit (graphics roadmap 4.4); input is linear scene color.
float3 ACESFitted(float3 x)
{
    float3 a = x * (x + 0.0245786) - 0.000090537;
    float3 b = x * (0.983729 * x + 0.4329510) + 0.238081;
    return saturate(a / b);
}

float3 LinearToSrgb(float3 c)
{
    float3 lo = c * 12.92;
    float3 hi = 1.055 * pow(max(c, 0.0), (float3)(1.0 / 2.4)) - 0.055;
    return lerp(lo, hi, step((float3)0.0031308, c));
}

// Identity below the knee, exponential shoulder above, C1-continuous at
// the join and asymptotic to 1.0. Dim/mid content (the classic Freelancer
// range) passes through untouched; only HDR highlights roll off.
float3 FilmicShoulder(float3 x)
{
    const float knee = 0.6;
    const float span = 0.4; // 1.0 - knee
    float3 over = max(x - knee, 0.0);
    float3 shoulder = knee + (1.0 - exp(-over / span)) * span;
    return lerp(min(x, knee), shoulder, step((float3)knee, x));
}

float4 main(Input input) : SV_Target0
{
    // The scene buffer is LINEAR HDR (phase 2 cleanup); this pass owns
    // the one and only display encode (docs/LINEAR_AUDIT.md).
    float4 scene = SceneTexture.Sample(SceneSampler, input.texCoord);
    // Composite bloom and god rays before grading (roadmap 4.5/4.6:
    // hdr += effect * intensity). Intensity 0 = bit-exact scene.
    scene.rgb += BloomTexture.Sample(BloomSampler, input.texCoord).rgb * Params.z;
    scene.rgb += RaysTexture.Sample(RaysSampler, input.texCoord).rgb * Params.w;
    float3 graded = scene.rgb * Params.x;
    if (Params.y > 1.5)
    {
        graded = FilmicShoulder(graded);
    }
    else if (Params.y > 0.5)
    {
        graded = ACESFitted(graded);
    }
    return float4(LinearToSrgb(saturate(graded)), scene.a);
}
