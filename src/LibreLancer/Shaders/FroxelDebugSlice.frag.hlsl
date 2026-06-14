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
        color = density.rgb * gain;
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
    else
    {
        float3 axis = float3(input.texCoord, z);
        color = lerp(axis * 0.35, float3(0.1, 0.85, 1.0), grid);
    }

    float stripe = step(frac((input.texCoord.x + GridParams.w * 0.037) * 12.0), 0.08);
    color += stripe * float3(0.08, 0.08, 0.02);
    return float4(saturate(color), 1.0);
}
