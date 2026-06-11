Texture2D<float4> SceneTexture : register(t0, TEXTURE_SPACE);
SamplerState SceneSampler : register(s0, TEXTURE_SPACE);

cbuffer GodRaysMaskParameters : register(b3, UNIFORM_SPACE)
{
    float4 SunPosition; // xy: sun uv, z: sun uv radius, w: viewport aspect
    float4 MaskParams;  // x: luminance cutoff
};

struct Input
{
    float2 texCoord : TEXCOORD0;
};

// Bright scene pixels around the sun disk. Occlusion is implicit: a ship
// or station covering the sun leaves no bright pixels to smear.
float4 main(Input input) : SV_Target0
{
    float2 toSun = input.texCoord - SunPosition.xy;
    toSun.x *= SunPosition.w;
    float dist = length(toSun);
    // Soft disk: full inside the sun radius, fading out by 2.5x radius
    // so the glow sprite still contributes.
    float disk = 1.0 - smoothstep(SunPosition.z, SunPosition.z * 2.5, dist);
    float3 scene = SceneTexture.Sample(SceneSampler, input.texCoord).rgb;
    float luminance = dot(scene, float3(0.2126, 0.7152, 0.0722));
    float gate = saturate(luminance - MaskParams.x);
    return float4(scene * gate * disk, 1.0);
}
