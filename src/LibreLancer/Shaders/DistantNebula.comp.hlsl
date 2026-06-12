// Distant nebula layer (track V6): half-res 2D raymarch covering the
// range BEYOND the froxel grid (14-80km) so zones read as volumetric
// cloud banks from outside. Same media model as the froxel inject (base
// noise, zone falloffs, exclusions), lit by the MsScatter sun with the
// accumulated march tau as self-shadow. Temporal smoothing reprojects by
// the absorption-weighted distance (bitsquid clouds approach).

#include "includes/VolumeCommon.hlsl"

Texture3D<float4> NoiseBase : register(t0, TEXTURE_SPACE);
SamplerState NoiseBaseSampler : register(s0, TEXTURE_SPACE);
Texture3D<float4> NoiseDetail : register(t2, TEXTURE_SPACE);
SamplerState NoiseDetailSampler : register(s2, TEXTURE_SPACE);
Texture2D<float4> History : register(t6, TEXTURE_SPACE);
SamplerState HistorySampler : register(s6, TEXTURE_SPACE);
Texture2D<float> SceneDepth : register(t8, TEXTURE_SPACE); // LAST frame's depth copy
SamplerState SceneDepthSampler : register(s8, TEXTURE_SPACE);

StructuredBuffer<VolumeZone> Zones : register(t9, TEXTURE_SPACE);

[[vk::image_format("rgba16f")]]
RWTexture2D<float4> Result : register(u4, TEXTURE_SPACE); // rgb light, a transmittance

cbuffer DistantParams : register(b3, UNIFORM_SPACE)
{
    float4x4 InvViewProj;
    float4x4 PrevViewProj;
    float4 CameraPosStart;   // xyz camera, w march start (froxel far)
    float4 TargetSizeEnd;    // xy half-res target size, z march end, w frame
    float4 SunDirAmbient;    // xyz to-sun, w ambient
    float4 SunColorIntensity; // rgb colour, w intensity
    float4 ZoneCountsBlend;  // x zone count, y drift time, z history blend, w depth valid
};

float SampleDensity(float3 worldPos, uint zoneCount)
{
    float density = 0;
    float cut = 1.0;
    for (uint i = 0; i < zoneCount; i++)
    {
        VolumeZone z = Zones[i];
        float f = ZoneFalloff(z, worldPos);
        if (z.Params.z > 0.5)
        {
            // Smoothed exclusion: the analytic cone edge reads as hard
            // facets at 1.5km march steps.
            cut *= 1.0 - smoothstep(0.0, 1.0, f);
        }
        else if (f > 0)
        {
            density += z.ColorDensity.w * NoiseDensityShared(z, worldPos,
                f, ZoneCountsBlend.y, NoiseBase, NoiseBaseSampler,
                NoiseDetail, NoiseDetailSampler);
        }
    }
    return density * cut;
}

float3 SampleColour(float3 worldPos, uint zoneCount)
{
    // Dominant zone colour at the point (cheap: first contributing zone).
    for (uint i = 0; i < zoneCount; i++)
    {
        VolumeZone z = Zones[i];
        if (z.Params.z > 0.5)
        {
            continue;
        }
        if (ZoneFalloff(z, worldPos) > 0)
        {
            return z.ColorDensity.rgb;
        }
    }
    return float3(0.5, 0.5, 0.5);
}

// Optical depth toward the sun: two local noisy taps for edge detail,
// then the analytic cloud-scale tail (ZonesAnalyticSunTau) - a purely
// local march lit the rim from every direction on a 70km bank.
float DistantSunTau(float3 worldPos, uint zoneCount)
{
    static const float taps[2] = { 800.0, 2400.0 };
    float tau = 0;
    float prev = 0;
    [unroll]
    for (uint i = 0; i < 2; i++)
    {
        float3 pos = worldPos + SunDirAmbient.xyz * taps[i];
        tau += SampleDensity(pos, zoneCount) * (taps[i] - prev);
        prev = taps[i];
    }
    return tau + ZonesAnalyticSunTau(Zones, zoneCount, worldPos,
        SunDirAmbient.xyz, 2400.0);
}

[numthreads(8, 8, 1)]
void main(uint2 id : SV_DispatchThreadID)
{
    uint2 target = (uint2)TargetSizeEnd.xy;
    if (any(id >= target))
        return;

    float2 uv = (id + 0.5) / TargetSizeEnd.xy;
    float2 ndc = uv * 2.0 - 1.0;
    float4 nearPoint = mul(float4(ndc, 0.0, 1.0), InvViewProj);
    nearPoint /= nearPoint.w;
    float4 farPoint = mul(float4(ndc, 1.0, 1.0), InvViewProj);
    farPoint /= farPoint.w;
    float3 dir = normalize(farPoint.xyz - nearPoint.xyz);
    float3 origin = CameraPosStart.xyz;

    // Union of zone bounding-sphere intervals clipped to the layer range.
    // Used to SKIP steps, never to shift march phase: every pixel walks
    // the same world-space step lattice (per-pixel start offsets put
    // neighbouring rays on different phases and painted hard seams along
    // the spheres' screen boundaries).
    float intervalStart = TargetSizeEnd.z;
    float marchEnd = CameraPosStart.w;
    uint zoneCount = min((uint)ZoneCountsBlend.x, VOLZONE_MAX);
    for (uint i = 0; i < zoneCount; i++)
    {
        VolumeZone z = Zones[i];
        if (z.Params.z > 0.5)
        {
            continue;
        }
        float2 t = RaySphere(origin, dir, z.BoundsSphere.xyz, z.BoundsSphere.w);
        if (t.y > t.x)
        {
            intervalStart = min(intervalStart, max(t.x, CameraPosStart.w));
            marchEnd = max(marchEnd, min(t.y, TargetSizeEnd.z));
        }
    }
    float marchStart = CameraPosStart.w; // fixed lattice origin (froxel far)
    // Clip the march by LAST frame's scene depth: geometry inside the
    // layer (stations 20-60km out) must occlude the cloud behind it. A
    // one-frame lag is invisible at these distances.
    if (ZoneCountsBlend.w > 0.5)
    {
        float depth = SceneDepth.SampleLevel(SceneDepthSampler, uv, 0);
        if (depth < 0.9999999)
        {
            float4 scenePoint = mul(float4(ndc, depth, 1.0), InvViewProj);
            scenePoint /= scenePoint.w;
            marchEnd = min(marchEnd, length(scenePoint.xyz - origin));
        }
    }

    if (marchEnd <= intervalStart)
    {
        Result[id] = float4(0, 0, 0, 1);
        return;
    }

    const uint Steps = 48;
    float stepLen = (TargetSizeEnd.z - CameraPosStart.w) / Steps;
    float jitter = InterleavedGradientNoise(id, (uint)TargetSizeEnd.w);

    float3 light = 0;
    float transmittance = 1.0;
    float weightedDist = 0;
    float weightSum = 0;
    float cosTheta = dot(dir, SunDirAmbient.xyz);

    [loop]
    for (uint s = 0; s < Steps; s++)
    {
        float t = marchStart + (s + jitter) * stepLen;
        if (t < intervalStart || t > marchEnd)
        {
            continue; // outside every zone's bounds: free skip
        }
        float3 pos = origin + dir * t;
        float density = SampleDensity(pos, zoneCount);
        if (density <= 1e-7)
        {
            continue;
        }
        float segTrans = exp(-density * stepLen);
        float sunTau = DistantSunTau(pos, zoneCount);
        float3 colour = SampleColour(pos, zoneCount);
        float3 scatterLight = colour * (
            SunColorIntensity.rgb * SunColorIntensity.w * MsScatter(sunTau, cosTheta)
            + SunDirAmbient.w.xxx);
        float weight = transmittance * (1.0 - segTrans);
        light += scatterLight * weight;
        weightedDist += t * weight;
        weightSum += weight;
        transmittance *= segTrans;
        if (transmittance < 0.005)
        {
            break;
        }
    }

    float4 current = float4(light, transmittance);

    // Temporal reprojection by absorption-weighted distance (bitsquid).
    if (ZoneCountsBlend.z > 0 && weightSum > 1e-5)
    {
        float3 anchor = origin + dir * (weightedDist / weightSum);
        float4 prevClip = mul(float4(anchor, 1.0), PrevViewProj);
        if (prevClip.w > 0)
        {
            float2 prevUv = (prevClip.xy / prevClip.w) * 0.5 + 0.5;
            if (all(prevUv >= 0) && all(prevUv <= 1))
            {
                float4 history = History.SampleLevel(HistorySampler, prevUv, 0);
                history = min(history, current * 4.0 + 0.001);
                current = lerp(current, history, ZoneCountsBlend.z);
            }
        }
    }

    Result[id] = current;
}
