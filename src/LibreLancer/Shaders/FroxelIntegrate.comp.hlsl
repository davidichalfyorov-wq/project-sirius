// Froxel integration (track V, pass 3 of 3): front-to-back accumulation
// along Z. Integrated[x,y,s] holds the in-scattered light reaching the
// camera from slices 0..s (rgb) and the transmittance through them (a) -
// the composite samples this directly by scene depth.

#include "includes/VolumeCommon.hlsl"

Texture3D<float4> Scatter : register(t0, TEXTURE_SPACE);
SamplerState ScatterSampler : register(s0, TEXTURE_SPACE);

[[vk::image_format("rgba16f")]]
RWTexture3D<float4> Integrated : register(u4, TEXTURE_SPACE);

cbuffer IntegrateParams : register(b3, UNIFORM_SPACE)
{
    float4 GridNear;  // xyz grid dimensions, w near
    float4 FarPad;    // x far, yzw unused
};

[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint3 grid = (uint3)GridNear.xyz;
    if (any(id.xy >= grid.xy))
        return;

    float3 light = 0;
    float transmittance = 1.0;
    float prevDist = SliceToDistance(0, GridNear.z, GridNear.w, FarPad.x);
    for (uint s = 0; s < grid.z; s++)
    {
        float4 scatter = Scatter[uint3(id.xy, s)];
        float dist = SliceToDistance(s + 1, GridNear.z, GridNear.w, FarPad.x);
        float dt = dist - prevDist;
        prevDist = dist;

        float segTrans = exp(-scatter.a * dt);
        // Energy-conserving slice integral (Frostbite): the in-scatter
        // emitted across the slice, attenuated by the media inside it.
        float3 sliceLight = scatter.a > 1e-5
            ? scatter.rgb * (1.0 - segTrans) / scatter.a
            : scatter.rgb * dt;
        light += transmittance * sliceLight;
        transmittance *= segTrans;

        Integrated[uint3(id.xy, s)] = float4(light, transmittance);
    }
}
