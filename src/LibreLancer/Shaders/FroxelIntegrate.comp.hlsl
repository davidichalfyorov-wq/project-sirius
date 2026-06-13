// Phase 5 / PR-5.5: debug integration for froxel nebula volumes.
// Writes cumulative in-scattering/transmittance into Integrated and seeds
// History with the current frame.

Texture3D<float4> Density : register(t0, TEXTURE_SPACE);
SamplerState DensitySampler : register(s0, TEXTURE_SPACE);
Texture3D<float4> Lighting : register(t1, TEXTURE_SPACE);
SamplerState LightingSampler : register(s1, TEXTURE_SPACE);

[[vk::image_format("rgba16f")]]
RWTexture3D<float4> Integrated : register(u2, TEXTURE_SPACE);
[[vk::image_format("rgba16f")]]
RWTexture3D<float4> History : register(u3, TEXTURE_SPACE);

cbuffer FroxelIntegrateParams : register(b3, UNIFORM_SPACE)
{
    float4 GridSize;      // xyz: grid dimensions, w: 1 for near cascade
    float4 DepthParams;   // x: near metres, y: far metres, z: core ext, w: edge ext
    float4 HistoryParams; // x: history alpha (reserved), y: exposure/gain, z: god rays, w: powder
    float4 AlbedoParams;  // rgb: albedo, w: coverage
};

[numthreads(4, 4, 4)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= (uint3)GridSize.xyz))
        return;

    uint depth = (uint)GridSize.z;
    float stepMeters = max((DepthParams.y - DepthParams.x) / max(GridSize.z, 1.0), 1.0);
    if (GridSize.w > 0.5)
        stepMeters *= 0.55;

    float transmittance = 1.0;
    float3 scattering = 0.0;
    [loop]
    for (uint z = 0; z <= id.z && z < depth; z++)
    {
        float4 d = Density.Load(int4(id.xy, z, 0));
        float4 l = Lighting.Load(int4(id.xy, z, 0));
        float sigmaT = max(d.y, 0.0);
        float localT = exp(-sigmaT * stepMeters);
        float extinction = 1.0 - localT;
        float3 singleScatter = l.rgb * extinction * HistoryParams.y;
        scattering += transmittance * singleScatter;
        transmittance *= localT;
        if (transmittance < 0.01)
            break;
    }

    scattering = min(scattering, 8.0);
    float4 result = float4(scattering, saturate(transmittance));
    Integrated[id] = result;
    History[id] = result;
}
