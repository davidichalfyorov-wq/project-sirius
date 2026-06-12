// Compute smoke test (phase 5 / F5): fills a 3D texture with an XYZ
// gradient plus diagonal bands. Proves the whole compute path - bundle
// loading, pipeline creation, UAV descriptors, dispatch and the
// compute->graphics barrier - before any volumetric work builds on it.

[[vk::image_format("rgba16f")]]
RWTexture3D<float4> Result : register(u4, TEXTURE_SPACE);

cbuffer SmokeParams : register(b3, UNIFORM_SPACE)
{
    float4 GridSize; // xyz: texture dimensions, w: time
};

[numthreads(4, 4, 4)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= (uint3)GridSize.xyz))
        return;
    float3 uvw = (id + 0.5) / GridSize.xyz;
    // Bands make slice stepping obvious in the visualiser.
    float bands = 0.5 + 0.5 * sin((uvw.x + uvw.y + uvw.z) * 20.0 + GridSize.w);
    Result[id] = float4(uvw * bands, 1.0);
}
