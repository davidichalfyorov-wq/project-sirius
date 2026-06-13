struct Input
{
    float2 texCoord : TEXCOORD0;
};

Texture2D<float4> LutTexture : register(t0, TEXTURE_SPACE);
SamplerState LutSampler : register(s0, TEXTURE_SPACE);

cbuffer AtmoLutDebugParams : register(b3, UNIFORM_SPACE)
{
    float4 GainMode; // x: gain, y: 0 transmittance / 1 multi-scattering
};

float4 main(Input input) : SV_Target0
{
    float3 rgb = LutTexture.SampleLevel(LutSampler, input.texCoord, 0).rgb * GainMode.x;

    // Thin grid lines make the contact sheet read as generated LUT data
    // without relying on text rendering in the capture path.
    float2 cell = abs(frac(input.texCoord * float2(16.0, 8.0)) - 0.5);
    float grid = 1.0 - smoothstep(0.485, 0.5, max(cell.x, cell.y));
    rgb = lerp(rgb, rgb + 0.08, grid * 0.35);

    return float4(saturate(rgb), 1.0);
}
