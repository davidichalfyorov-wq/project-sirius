// Froxel media injection (track V, pass 1 of 3): evaluates the analytic
// nebula volumes eroded by baked Perlin-Worley noise at every froxel
// centre and writes participating-media properties. Displacement (V10)
// will modulate the near cascade here.

#include "includes/VolumeCommon.hlsl"

Texture3D<float4> NoiseBase : register(t0, TEXTURE_SPACE);   // R: Perlin-Worley shape, GBA: Worley fbm
SamplerState NoiseBaseSampler : register(s0, TEXTURE_SPACE);
Texture3D<float4> NoiseDetail : register(t2, TEXTURE_SPACE); // RGB: high-frequency Worley
SamplerState NoiseDetailSampler : register(s2, TEXTURE_SPACE);

// t9: structured buffers keep their raw register as the binding (no 2N
// remap) - odd indices never collide with storage images at 2N.
StructuredBuffer<VolumeZone> Zones : register(t9, TEXTURE_SPACE);

[[vk::image_format("rgba16f")]]
RWTexture3D<float4> Media : register(u4, TEXTURE_SPACE); // rgb scatter colour * density, a extinction/m

cbuffer FroxelParams : register(b3, UNIFORM_SPACE)
{
    float4x4 InvViewProj;
    float4 CameraPosNear;  // xyz camera, w near plane of the froxel range
    float4 GridSizeFar;    // xyz grid dimensions, w far end of the range
    float4 Counts;         // x zone count, y frame index, z density boost, w drift time (s)
};

float NoiseDensity(VolumeZone z, float3 worldPos, float falloff)
{
    return NoiseDensityShared(z, worldPos, falloff, Counts.w,
        NoiseBase, NoiseBaseSampler, NoiseDetail, NoiseDetailSampler);
}

[numthreads(4, 4, 4)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint3 grid = (uint3)GridSizeFar.xyz;
    if (any(id >= grid))
        return;

    // Froxel centre -> world: unproject the cell's NDC through the same
    // inverse view-projection the composite uses, march the ray to the
    // slice distance. IGN jitter inside the cell fights slice banding.
    float2 uv = (id.xy + 0.5) / GridSizeFar.xy;
    float2 ndc = uv * 2.0 - 1.0;
    float4 nearPoint = mul(float4(ndc, 0.0, 1.0), InvViewProj);
    nearPoint /= nearPoint.w;
    float4 farPoint = mul(float4(ndc, 1.0, 1.0), InvViewProj);
    farPoint /= farPoint.w;
    float3 dir = normalize(farPoint.xyz - nearPoint.xyz);

    float jitter = InterleavedGradientNoise(id.xy + id.z * 7.0, (uint)Counts.y);
    float dist = SliceToDistance(id.z + jitter, GridSizeFar.z, CameraPosNear.w, GridSizeFar.w);
    float3 worldPos = CameraPosNear.xyz + dir * dist;

    // Counts.z < 0: debug probes (composite shows media directly).
    // -1: raw base-noise sample; -2: zone0 NoiseProfile as colour.
    if (Counts.z < -1.5)
    {
        // Production-path density heatmap: every zone evaluated exactly as
        // the real loop does, output boosted for visibility.
        float densitySum = 0;
        uint pCount = min((uint)Counts.x, VOLZONE_MAX);
        for (uint pi = 0; pi < pCount; pi++)
        {
            VolumeZone pz = Zones[pi];
            if (pz.Params.z > 0.5)
                continue;
            float pf = ZoneFalloff(pz, worldPos);
            if (pf > 0)
                densitySum += NoiseDensity(pz, worldPos, pf);
        }
        Media[id] = float4(densitySum * 0.004, densitySum * 0.002,
            densitySum * 0.0008, 0.0006);
        return;
    }
    if (Counts.z < 0)
    {
        float4 probe = NoiseBase.SampleLevel(NoiseBaseSampler,
            worldPos * 0.0002, 0);
        Media[id] = float4(probe.rgb * 0.002, 0.0008);
        return;
    }

    float3 colour = 0;
    float extinction = 0;
    float cut = 1.0;
    uint zoneCount = min((uint)Counts.x, VOLZONE_MAX);
    for (uint i = 0; i < zoneCount; i++)
    {
        VolumeZone z = Zones[i];
        float f = ZoneFalloff(z, worldPos);
        if (z.Params.z > 0.5)
        {
            cut *= 1.0 - f; // exclusion zones carve the media out
        }
        else if (f > 0)
        {
            float density = z.ColorDensity.w * NoiseDensity(z, worldPos, f);
            extinction += density;
            colour += z.ColorDensity.rgb * density;
        }
    }
    extinction *= cut * Counts.z;
    colour *= cut * Counts.z;

    Media[id] = float4(colour, extinction);
}
