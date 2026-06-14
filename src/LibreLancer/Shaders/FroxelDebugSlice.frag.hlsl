// Phase 5 / PR-5.3: debug slice viewer for the froxel resource skeleton.
// Drawn only for volumetric debug views, preserving default scene output.

struct Input
{
    float2 texCoord : TEXCOORD0;
};

Texture3D<float4> DensityVolume : register(t0, TEXTURE_SPACE);
SamplerState DensitySampler : register(s0, TEXTURE_SPACE);
Texture3D<float4> IntegratedVolume : register(t1, TEXTURE_SPACE);
SamplerState IntegratedSampler : register(s1, TEXTURE_SPACE);

cbuffer FroxelDebugSliceParams : register(b3, UNIFORM_SPACE)
{
    float4 SliceParams; // x: z slice 0..1, y: gain, z: mode, w: grid overlay
    float4 GridParams;  // xyz: dimensions, w: resource generation
};

float GridLine(float2 uv, float2 dims)
{
    float2 cell = frac(uv * dims);
    float2 gridMask = step(cell, 0.035) + step(0.965, cell);
    return saturate(max(gridMask.x, gridMask.y));
}

float4 main(Input input) : SV_Target0
{
    float z = saturate(SliceParams.x);
    float gain = max(SliceParams.y, 0.001);
    int mode = (int)(SliceParams.z + 0.5);
    float3 uvw = float3(input.texCoord, z);
    float4 density = DensityVolume.SampleLevel(DensitySampler, uvw, 0);
    float4 integrated = IntegratedVolume.SampleLevel(IntegratedSampler, uvw, 0);
    float grid = GridLine(input.texCoord, max(GridParams.xy / 8.0, float2(1.0, 1.0)));

    float3 color;
    if (mode == 1)
    {
        color = density.rrr * gain;
    }
    else if (mode == 2)
    {
        color = integrated.aaa;
    }
    else if (mode == 4)
    {
        color = saturate(density.rgb);
    }
    else if (mode == 5)
    {
        color = density.rgb * gain;
    }
    else if (mode == 6)
    {
        color = max(density.rgb * gain, (1.0 - density.aaa).rgb);
    }
    else if (mode == 7)
    {
        color = float3(density.r, density.g, density.b);
    }
    else if (mode == 8)
    {
        color = float3(pow(saturate(density.r), 0.55), saturate(density.w), saturate(density.g * 8000.0));
    }
    else if (mode == 9)
    {
        color = saturate(integrated.rgb * gain + (1.0 - integrated.a).xxx * 0.35);
    }
    else if (mode == 10)
    {
        color = saturate(density.rgb * 0.5 + 0.5) * saturate(0.25 + density.a);
    }
    else if (mode == 11)
    {
        color = float3(saturate(density.a * gain), saturate(density.r * 0.5), saturate(density.b * 0.5));
    }
    else if (mode == 12)
    {
        float trans = dot(saturate(density.rgb), float3(0.3333, 0.3333, 0.3334));
        float multi = dot(saturate(integrated.rgb), float3(0.3333, 0.3333, 0.3334));
        color = lerp(float3(0.02, 0.06, 0.10), float3(0.65, 0.92, 1.0), trans);
        color += multi.xxx * float3(0.25, 0.18, 0.08);
    }
    else if (mode == 13)
    {
        float aerialAlpha = saturate(density.a);
        float aerialLight = dot(saturate(density.rgb), float3(0.25, 0.45, 0.30));
        color = float3(aerialLight * 0.7, aerialLight, aerialLight * 1.25) + aerialAlpha.xxx * float3(0.05, 0.18, 0.34);
    }
    else if (mode == 14)
    {
        float coverage = saturate(density.a * gain);
        float light = dot(saturate(density.rgb * gain), float3(0.30, 0.42, 0.28));
        color = lerp(float3(0.015, 0.025, 0.045), float3(0.72, 0.82, 0.95), coverage);
        color += light.xxx * float3(0.16, 0.18, 0.20);
    }
    else
    {
        float3 axis = float3(input.texCoord, z);
        color = lerp(axis * 0.35, float3(0.1, 0.85, 1.0), grid);
    }

    float stripe = step(frac((input.texCoord.x + GridParams.w * 0.037) * 12.0), 0.08);
    color += stripe * float3(0.08, 0.08, 0.02);
    return float4(saturate(color), 1.0);
}
