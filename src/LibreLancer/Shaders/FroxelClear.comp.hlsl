// Phase 5 / PR-5.3: identity clear for volumetric nebula froxel grids.
// This pass writes transparent participating media data and does not affect
// the scene until a later opt-in composite pass consumes the volume.

[[vk::image_format("rgba16f")]]
RWTexture3D<float4> Density : register(u0, TEXTURE_SPACE);
[[vk::image_format("rgba16f")]]
RWTexture3D<float4> Lighting : register(u1, TEXTURE_SPACE);
[[vk::image_format("rgba16f")]]
RWTexture3D<float4> Integrated : register(u2, TEXTURE_SPACE);
[[vk::image_format("rgba16f")]]
RWTexture3D<float4> History : register(u3, TEXTURE_SPACE);
[[vk::image_format("rgba16f")]]
RWTexture3D<float4> HistoryConfidence : register(u4, TEXTURE_SPACE);

cbuffer FroxelClearParams : register(b3, UNIFORM_SPACE)
{
    float4 GridSize;    // xyz: grid dimensions, w: 1 for near cascade
    float4 ClearParams; // x: quality, y: near metres, z: far metres, w: reserved
};

[numthreads(4, 4, 4)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= (uint3)GridSize.xyz))
        return;

    Density[id] = float4(0.0, 0.0, 0.0, 0.0);
    Lighting[id] = float4(0.0, 0.0, 0.0, 0.0);
    Integrated[id] = float4(0.0, 0.0, 0.0, 1.0);
    History[id] = float4(0.0, 0.0, 0.0, 1.0);
    HistoryConfidence[id] = float4(0.0, 0.0, 0.0, 0.0);
}
