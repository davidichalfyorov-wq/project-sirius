// Froxel media injection (track V, pass 1 of 3): evaluates the analytic
// nebula volumes eroded by baked Perlin-Worley noise at every froxel
// centre and writes participating-media properties. Displacement (V10)
// will modulate the near cascade here.

#include "includes/VolumeCommon.hlsl"

Texture3D<float4> NoiseBase : register(t0, TEXTURE_SPACE);   // R: Perlin-Worley shape, GBA: Worley fbm
SamplerState NoiseBaseSampler : register(s0, TEXTURE_SPACE);
Texture3D<float4> NoiseDetail : register(t2, TEXTURE_SPACE); // RGB: high-frequency Worley
SamplerState NoiseDetailSampler : register(s2, TEXTURE_SPACE);
Texture3D<float4> DisplacementField : register(t3, TEXTURE_SPACE); // R push, G swirl
SamplerState DisplacementSampler : register(s3, TEXTURE_SPACE);

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
    float4 DetailParams;   // x detail frequency multiplier, y erosion multiplier
    float4 DisplacementOriginExtent; // xyz min corner, w extent
    float4 DisplacementParams; // x active, y push scale, z swirl distance
};

float NoiseDensity(VolumeZone z, float3 worldPos, float falloff)
{
    float4 stages = NoiseDensityStagesEx(z, worldPos, falloff, Counts.w,
        NoiseBase, NoiseBaseSampler, NoiseDetail, NoiseDetailSampler,
        DetailParams.x, DetailParams.y);
    return stages.w <= 0 ? falloff : stages.z;
}

float3 CurlCheap(float3 worldPos)
{
    float3 p = worldPos * 0.021;
    float3 v = float3(
        sin(p.y * 7.1 + p.z * 3.3) - cos(p.z * 5.2 - p.x),
        sin(p.z * 6.7 + p.x * 2.9) - cos(p.x * 4.8 - p.y),
        sin(p.x * 6.3 + p.y * 3.7) - cos(p.y * 5.4 - p.z));
    return v * rsqrt(max(dot(v, v), 1e-4));
}

float3 ApplyDisplacementWarp(float3 worldPos, out float push)
{
    push = 0.0;
    if (DisplacementParams.x <= 0.5 || DisplacementOriginExtent.w <= 0.0)
        return worldPos;

    float3 uvw = (worldPos - DisplacementOriginExtent.xyz) / DisplacementOriginExtent.w;
    if (any(uvw < 0.0) || any(uvw > 1.0))
        return worldPos;

    float2 disp = DisplacementField.SampleLevel(DisplacementSampler, uvw, 0).rg;
    push = saturate(disp.r * DisplacementParams.y);
    float swirl = saturate(disp.g) * DisplacementParams.z;
    return worldPos + CurlCheap(worldPos) * swirl;
}

bool BestDensityZone(float3 worldPos, out VolumeZone bestZone,
    out float bestFalloff, out uint bestIndex)
{
    bool found = false;
    float bestScore = -1.0;
    bestFalloff = 0;
    bestIndex = 0;
    uint zoneCount = min((uint)Counts.x, VOLZONE_MAX);
    for (uint i = 0; i < zoneCount; i++)
    {
        VolumeZone z = Zones[i];
        if (z.Params.z > 0.5)
            continue;
        float f = ZoneFalloff(z, worldPos);
        float score = f * z.ColorDensity.w;
        if (score > bestScore)
        {
            found = f > 0;
            bestScore = score;
            bestFalloff = f;
            bestZone = z;
            bestIndex = i;
        }
    }
    return found;
}

float3 ZoneDebugColour(uint index)
{
    if (index == 0) return float3(0.004, 0, 0);
    if (index == 1) return float3(0, 0.004, 0);
    if (index == 2) return float3(0, 0, 0.004);
    if (index == 3) return float3(0.004, 0.004, 0);
    if (index == 4) return float3(0, 0.004, 0.004);
    return float3(0.004, 0, 0.004);
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

    // Counts.z < 0: debug probes.
    // -1 raw base-noise; -2 density heatmap; -4 camera-zone stride;
    // -5 scale; -6 shape; -7 shaped; -8 final density; -9 max zone index.
    uint debugZ = grid.z / 3;
    bool debugSlice = id.z >= debugZ && id.z <= debugZ + 2;
    if (Counts.z < -8.5)
    {
        if (!debugSlice)
        {
            Media[id] = 0;
            return;
        }
        VolumeZone bz;
        float bf;
        uint bi;
        if (!BestDensityZone(worldPos, bz, bf, bi))
        {
            Media[id] = 0;
            return;
        }
        Media[id] = float4(ZoneDebugColour(bi), 0.0006);
        return;
    }
    if (Counts.z < -7.5)
    {
        if (!debugSlice)
        {
            Media[id] = 0;
            return;
        }
        VolumeZone bz;
        float bf;
        uint bi;
        if (!BestDensityZone(worldPos, bz, bf, bi))
        {
            Media[id] = 0;
            return;
        }
        float4 stages = NoiseDensityStages(bz, worldPos, bf, Counts.w,
            NoiseBase, NoiseBaseSampler, NoiseDetail, NoiseDetailSampler);
        Media[id] = float4(stages.z.xxx * 0.006, 0.0006);
        return;
    }
    if (Counts.z < -6.5)
    {
        if (!debugSlice)
        {
            Media[id] = 0;
            return;
        }
        VolumeZone bz;
        float bf;
        uint bi;
        if (!BestDensityZone(worldPos, bz, bf, bi))
        {
            Media[id] = 0;
            return;
        }
        float4 stages = NoiseDensityStages(bz, worldPos, bf, Counts.w,
            NoiseBase, NoiseBaseSampler, NoiseDetail, NoiseDetailSampler);
        Media[id] = float4(stages.y.xxx * 0.006, 0.0006);
        return;
    }
    if (Counts.z < -5.5)
    {
        if (!debugSlice)
        {
            Media[id] = 0;
            return;
        }
        VolumeZone bz;
        float bf;
        uint bi;
        if (!BestDensityZone(worldPos, bz, bf, bi))
        {
            Media[id] = 0;
            return;
        }
        float4 stages = NoiseDensityStages(bz, worldPos, bf, Counts.w,
            NoiseBase, NoiseBaseSampler, NoiseDetail, NoiseDetailSampler);
        Media[id] = float4(stages.x.xxx * 0.006, 0.0006);
        return;
    }
    if (Counts.z < -4.5)
    {
        VolumeZone bz;
        float bf;
        uint bi;
        if (!BestDensityZone(worldPos, bz, bf, bi))
        {
            Media[id] = 0;
            return;
        }
        bool ok = bz.NoiseProfile.x > 1e-6 && bz.NoiseProfile.y > 0.05
            && bz.NoiseProfile.y < 1.0;
        Media[id] = float4(ok ? float3(0, 0.004, 0) : float3(0.004, 0, 0), 0.0006);
        return;
    }
    if (Counts.z < -3.5)
    {
        // Stride probe: green = NoiseProfile of the zone that contains
        // the camera reads sane, red = garbage (stride/layout broken).
        float3 probeCol = float3(0.004, 0, 0);
        uint pc = min((uint)Counts.x, VOLZONE_MAX);
        for (uint pi = 0; pi < pc; pi++)
        {
            VolumeZone pz = Zones[pi];
            if (pz.Params.z > 0.5)
                continue;
            if (ZoneFalloff(pz, CameraPosNear.xyz) <= 0)
                continue;
            bool ok = pz.NoiseProfile.x > 1e-6 && pz.NoiseProfile.y > 0.05
                && pz.NoiseProfile.y < 1.0;
            probeCol = ok ? float3(0, 0.004, 0) : float3(0.004, 0, 0);
            break;
        }
        Media[id] = float4(probeCol, 0.0006);
        return;
    }
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
        Media[id] = float4(densitySum.xxx * 0.00002, 0.00002);
        return;
    }
    if (Counts.z < 0)
    {
        if (!debugSlice)
        {
            Media[id] = 0;
            return;
        }
        float4 probe = NoiseBase.SampleLevel(NoiseBaseSampler,
            worldPos * 0.0010, 0);
        float density = max(probe.r, max(probe.g, probe.b));
        Media[id] = float4(probe.rgb * 0.012, 0.0001 + density * 0.003);
        return;
    }

    float push;
    float3 densityPos = ApplyDisplacementWarp(worldPos, push);
    float displacementCut = 1.0 - push;

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
            float density = z.ColorDensity.w * NoiseDensity(z, densityPos, f);
            extinction += density;
            colour += z.ColorDensity.rgb * density;
        }
    }
    extinction *= cut * displacementCut * Counts.z;
    colour *= cut * displacementCut * Counts.z;

    Media[id] = float4(colour, extinction);
}
