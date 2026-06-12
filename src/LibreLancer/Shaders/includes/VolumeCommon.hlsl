// Volumetric nebulae common (phase 5 track V): analytic zone volumes,
// froxel grid mapping and dither noise shared by the froxel compute
// passes and the composite.
//
// Zone shapes mirror LibreLancer's Zone.ScaledDistance: positions are
// transformed into the zone's unit-local space (the inverse transform
// bakes position, rotation and size), where ellipsoids/spheres test
// length(q) <= 1 and boxes test max(|q|) <= 1. Cylinder/ring zones are
// approximated by their bounding ellipsoid until V13 (no Discovery
// nebula uses them as the primary volume).

#define VOLZONE_MAX 16

struct VolumeZone
{
    // Inverse zone transform as matrix COLUMNS of the row-vector form:
    // local_i = dot(InvColI, float4(world, 1)).
    float4 InvCol0;
    float4 InvCol1;
    float4 InvCol2;
    float4 ColorDensity; // rgb: scatter colour (linear), w: extinction/m at core
    float4 Params;       // x: shape (0 ellipsoid, 1 box), y: edge fraction,
                         // z: 1 = exclusion cut, w: reserved
    float4 NoiseProfile; // x: base noise scale (1/m), y: coverage 0..1,
                         // z: detail erosion strength, w: drift speed (m/s)
    float4 BoundsSphere; // xyz: world centre, w: radius (march intervals)
};

// Ray/sphere interval for the distant march. Returns (tNear, tFar) or
// (1, -1) on miss.
float2 RaySphere(float3 origin, float3 dir, float3 centre, float radius)
{
    float3 oc = origin - centre;
    float b = dot(oc, dir);
    float c = dot(oc, oc) - radius * radius;
    float h = b * b - c;
    if (h < 0)
    {
        return float2(1, -1);
    }
    h = sqrt(h);
    return float2(-b - h, -b + h);
}

float RemapClamped(float value, float oldMin, float oldMax, float newMin, float newMax)
{
    return newMin + saturate((value - oldMin) / (oldMax - oldMin)) * (newMax - newMin);
}

float3 ZoneLocal(VolumeZone z, float3 worldPos)
{
    float4 hp = float4(worldPos, 1.0);
    return float3(dot(z.InvCol0, hp), dot(z.InvCol1, hp), dot(z.InvCol2, hp));
}

// 1 at the zone core, fading to 0 across the outer EdgeFraction band -
// the same normalized falloff Zone.ScaledDistance feeds the legacy fog.
float ZoneFalloff(VolumeZone z, float3 worldPos)
{
    float3 q = ZoneLocal(z, worldPos);
    float d = z.Params.x < 0.5
        ? length(q)
        : max(abs(q.x), max(abs(q.y), abs(q.z)));
    float edge = max(z.Params.y, 1e-3);
    return saturate((1.0 - d) / edge);
}

// Froxel slice <-> view distance: squared distribution packs resolution
// near the camera where detail reads, tail slices cover the far volume.
float SliceToDistance(float slice, float numSlices, float near, float far)
{
    float t = saturate(slice / numSlices);
    return near + (far - near) * t * t;
}

float DistanceToSlice(float dist, float numSlices, float near, float far)
{
    return sqrt(saturate((dist - near) / (far - near))) * numSlices;
}

// Interleaved gradient noise (Jimenez 2014) with a frame scroll: license-
// clean dither for sample jitter until the generated blue-noise volume
// lands (track V5).
float InterleavedGradientNoise(float2 pixel, uint frame)
{
    pixel += 5.588238 * (float)(frame & 63u);
    return frac(52.9829189 * frac(0.06711056 * pixel.x + 0.00583715 * pixel.y));
}

// Froxel cell centre -> world position (shared by inject/light passes).
float3 FroxelWorld(uint3 id, float jitterZ, float4x4 invViewProj,
    float3 cameraPos, float near, float3 gridSize, float far)
{
    float2 uv = (id.xy + 0.5) / gridSize.xy;
    float2 ndc = uv * 2.0 - 1.0;
    float4 nearPoint = mul(float4(ndc, 0.0, 1.0), invViewProj);
    nearPoint /= nearPoint.w;
    float4 farPoint = mul(float4(ndc, 1.0, 1.0), invViewProj);
    farPoint /= farPoint.w;
    float3 dir = normalize(farPoint.xyz - nearPoint.xyz);
    float dist = SliceToDistance(id.z + jitterZ, gridSize.z, near, far);
    return cameraPos + dir * dist;
}

// World position -> froxel UVW (for in-volume secondary marches).
// Returns w outside [0,1] when the point leaves the froxel range.
float3 WorldToFroxelUvw(float3 worldPos, float4x4 viewProj,
    float3 cameraPos, float near, float3 gridSize, float far)
{
    float4 clip = mul(float4(worldPos, 1.0), viewProj);
    float2 uv = (clip.xy / max(clip.w, 1e-4)) * 0.5 + 0.5;
    float dist = length(worldPos - cameraPos);
    float w = DistanceToSlice(dist, gridSize.z, near, far) / gridSize.z;
    return float3(uv, clip.w <= 0 ? -1.0 : w);
}

// Dual-lobe Henyey-Greenstein (cloud standard): strong forward silver
// lining + weak back lobe.
float HgPhase(float cosTheta, float g)
{
    float g2 = g * g;
    float denom = 1.0 + g2 - 2.0 * g * cosTheta;
    return (1.0 - g2) / (12.5663706 * denom * sqrt(max(denom, 1e-4)));
}

float DualHgPhase(float cosTheta)
{
    return lerp(HgPhase(cosTheta, -0.2), HgPhase(cosTheta, 0.6), 0.85);
}

// Multiple-scattering approximation (Wrenninge/Schneider octaves): each
// octave relaxes extinction and phase anisotropy, letting light bleed
// into the cloud body the way real multiple bounces do - without it the
// interior of a dense nebula reads as a black pit.
float MsScatter(float tau, float cosTheta)
{
    float result = 0;
    float weight = 1.0, extScale = 1.0, phaseScale = 1.0;
    [unroll]
    for (uint i = 0; i < 3; i++)
    {
        result += weight * exp(-tau * extScale)
            * lerp(HgPhase(cosTheta * phaseScale, -0.2), HgPhase(cosTheta * phaseScale, 0.6), 0.85);
        weight *= 0.42;
        extScale *= 0.28;
        phaseScale *= 0.65;
    }
    return result;
}

// Cloud-SCALE optical depth toward the sun, analytic: the remaining path
// through each zone volume times its coverage-weighted mean extinction.
// Short local marches (a few km) cannot see that a 70km bank shadows its
// own night side - without this the rim glows from EVERY direction and
// the lighting reads as broken. Local taps add noisy detail on top.
float ZonesAnalyticSunTau(StructuredBuffer<VolumeZone> zones, uint zoneCount,
    float3 worldPos, float3 toSun, float skipDist)
{
    float tau = 0;
    for (uint i = 0; i < zoneCount; i++)
    {
        VolumeZone z = zones[i];
        if (z.Params.z > 0.5)
        {
            continue;
        }
        float3 q = ZoneLocal(z, worldPos);
        float3 s = float3(dot(z.InvCol0.xyz, toSun),
                          dot(z.InvCol1.xyz, toSun),
                          dot(z.InvCol2.xyz, toSun));
        float sLen = length(s);
        if (sLen < 1e-6)
        {
            continue;
        }
        float3 sn = s / sLen;
        // Exit of the unit volume (boxes approximated by their sphere).
        float b = dot(q, sn);
        float c = dot(q, q) - 1.0;
        float h = b * b - c;
        if (h <= 0)
        {
            continue;
        }
        float tExit = -b + sqrt(h);
        if (tExit <= 0)
        {
            continue;
        }
        float remaining = max(tExit / sLen - skipDist, 0.0);
        float meanExt = z.ColorDensity.w * (z.NoiseProfile.y * 0.4 + 0.05);
        tau += meanExt * remaining;
    }
    return tau;
}

// Cloud-literature density shaping (Schneider): Perlin-Worley shape
// remapped by a Worley fbm, coverage cut, then high-frequency erosion.
// Shared by the froxel inject and the distant march.
float NoiseDensityShared(VolumeZone z, float3 worldPos, float falloff,
    float driftTime, Texture3D<float4> noiseBase, SamplerState baseSampler,
    Texture3D<float4> noiseDetail, SamplerState detailSampler)
{
    float scale = z.NoiseProfile.x;
    if (scale <= 0)
    {
        return falloff; // profile opts out of noise
    }
    float3 drift = float3(0.7, 0.06, 0.45) * (z.NoiseProfile.w * driftTime * scale);
    float3 baseUvw = worldPos * scale + drift;
    float4 nb = noiseBase.SampleLevel(baseSampler, baseUvw, 0);
    float worleyFbm = nb.g * 0.625 + nb.b * 0.25 + nb.a * 0.125;
    float shape = RemapClamped(nb.r, worleyFbm - 1.0, 1.0, 0.0, 1.0);

    // Coverage rises towards the zone core: the heart of the nebula stays
    // filled while edges break into wisps.
    float coverage = z.NoiseProfile.y * falloff;
    float shaped = RemapClamped(shape, 1.0 - coverage, 1.0, 0.0, 1.0);
    if (shaped <= 0)
    {
        return 0;
    }

    float3 detailUvw = worldPos * scale * 6.0 + drift * 1.7;
    float3 dn = noiseDetail.SampleLevel(detailSampler, detailUvw, 0).rgb;
    float detailFbm = dn.r * 0.625 + dn.g * 0.25 + dn.b * 0.125;
    return RemapClamped(shaped, detailFbm * z.NoiseProfile.z, 1.0, 0.0, 1.0);
}
