// Debug visualiser for 3D textures (phase 5 / F5): samples a single W
// slice across the viewport. Used by the compute smoke test and later by
// SIRIUS_DEBUG_VIEW volumetric slice views.

struct Input
{
    float2 texCoord : TEXCOORD0;
};

Texture3D<float4> Volume : register(t0, TEXTURE_SPACE);
SamplerState VolumeSampler : register(s0, TEXTURE_SPACE);

cbuffer VisParams : register(b3, UNIFORM_SPACE)
{
    float4 SliceParams; // x: slice depth 0..1, y: gain, zw: unused
};

float4 main(Input input) : SV_Target0
{
    float3 rgb = Volume.SampleLevel(VolumeSampler, float3(input.texCoord, SliceParams.x), 0).rgb;
    return float4(rgb * SliceParams.y, 1.0);
}
