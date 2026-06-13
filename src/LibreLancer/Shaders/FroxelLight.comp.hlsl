// Froxel in-scatter (track V, pass 2 of 3 - V4 real lighting): sun
// radiance with dual-lobe HG phase and a short self-shadow march through
// the media volume (Beer), plus a flat nebula ambient. CSM sampling and
// dynamic-lightning point lights extend this pass next.

#include "includes/VolumeCommon.hlsl"

Texture3D<float4> Media : register(t0, TEXTURE_SPACE);
SamplerState MediaSampler : register(s0, TEXTURE_SPACE);
Texture3D<float4> NoiseBase : register(t1, TEXTURE_SPACE);
SamplerState NoiseBaseSampler : register(s1, TEXTURE_SPACE);
Texture3D<float4> History : register(t2, TEXTURE_SPACE); // previous frame's scatter
SamplerState HistorySampler : register(s2, TEXTURE_SPACE);
Texture3D<float4> NoiseDetail : register(t3, TEXTURE_SPACE);
SamplerState NoiseDetailSampler : register(s3, TEXTURE_SPACE);
Texture2D<float4> ShadowAtlas : register(t6, TEXTURE_SPACE); // CSM colour atlas (last frame's)
SamplerState ShadowAtlasSampler : register(s6, TEXTURE_SPACE);

// Same zone list the inject pass consumes (slot persists across the
// dispatch chain) - feeds the analytic cloud-scale sun occlusion.
StructuredBuffer<VolumeZone> Zones : register(t9, TEXTURE_SPACE);

[[vk::image_format("rgba16f")]]
RWTexture3D<float4> Scatter : register(u4, TEXTURE_SPACE); // rgb in-scatter/m, a extinction/m

cbuffer LightParams : register(b3, UNIFORM_SPACE)
{
    float4x4 InvViewProj;
    float4x4 ViewProj;
    float4x4 PrevViewProj;
    float4x4 ShadowMatrix0;
    float4x4 ShadowMatrix1;
    float4x4 ShadowMatrix2;
    float4 CameraPosNear;    // xyz camera, w near
    float4 GridSizeFar;      // xyz grid dimensions, w far
    float4 SunDirAmbient;    // xyz: direction TO the sun (normalized), w: ambient strength
    float4 SunColorIntensity; // rgb sun colour (linear), w intensity
    float4 Temporal;          // x: history blend (0 = off), y: zone count, z: drift time, w: unused
    float4 ShadowSplits;      // xyz: cascade far view-distances
    float4 ShadowParams;      // x: enabled, y: 1/cascades, z: depth bias
    float4 LightningPosRadius; // xyz: bolt position, w: range (0 = none)
    float4 LightningColor;     // rgb: bolt colour, w: intensity
};

float UnpackShadowDepth(float3 packed)
{
    return dot(packed, float3(1.0, 1.0 / 255.0, 1.0 / 65025.0));
}

// Station/ship shadows inside the fog (V4b): single-tap cascade lookup,
// same packing as the surface SampleShadow. Beyond the cascades the
// froxel self-shadow march is the only occluder (analytic planets: W).
float CsmShadowTerm(float3 worldPos, float viewDistance)
{
    if (ShadowParams.x <= 0 || viewDistance >= ShadowSplits.z)
    {
        return 1.0;
    }
    int cascade = viewDistance < ShadowSplits.x ? 0 : viewDistance < ShadowSplits.y ? 1 : 2;
    float4x4 mat = cascade == 0 ? ShadowMatrix0 : cascade == 1 ? ShadowMatrix1 : ShadowMatrix2;
    float4 lightClip = mul(float4(worldPos, 1.0), mat);
    float2 uv = lightClip.xy * 0.5 + 0.5;
    if (uv.x <= 0.0 || uv.x >= 1.0 || uv.y <= 0.0 || uv.y >= 1.0)
    {
        return 1.0;
    }
    float reference = lightClip.z - ShadowParams.z;
    float2 atlasUv = float2((uv.x + cascade) * ShadowParams.y, uv.y);
    float3 packed = ShadowAtlas.SampleLevel(ShadowAtlasSampler, atlasUv, 0).rgb;
    return UnpackShadowDepth(packed) >= reference ? 1.0 : 0.0;
}

// Optical depth toward the sun: sparse Beer march evaluated in WORLD
// space against the zones+noise directly. The first version sampled the
// froxel Media volume, which only exists inside the camera frustum -
// the frustum's edge projected a hard dark diamond across the screen.
static const float ShadowSteps[4] = { 60.0, 180.0, 450.0, 1100.0 };

float WorldDensity(float3 worldPos)
{
    uint zoneCount = (uint)Temporal.y;
    float density = 0;
    float cut = 1.0;
    for (uint i = 0; i < zoneCount; i++)
    {
        VolumeZone z = Zones[i];
        float f = ZoneFalloff(z, worldPos);
        if (z.Params.z > 0.5)
        {
            cut *= 1.0 - smoothstep(0.0, 1.0, f);
        }
        else if (f > 0)
        {
            density += z.ColorDensity.w * NoiseDensityShared(z, worldPos,
                f, Temporal.z, NoiseBase, NoiseBaseSampler,
                NoiseDetail, NoiseDetailSampler);
        }
    }
    return density * cut;
}

float SunOpticalDepth(float3 worldPos)
{
    float tau = 0;
    float prev = 0;
    [unroll]
    for (uint i = 0; i < 4; i++)
    {
        float t = ShadowSteps[i];
        tau += WorldDensity(worldPos + SunDirAmbient.xyz * t) * (t - prev);
        prev = t;
    }
    return tau;
}

[numthreads(4, 4, 4)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint3 grid = (uint3)GridSizeFar.xyz;
    if (any(id >= grid))
        return;
    float4 media = Media[id];
    if (media.a <= 1e-6)
    {
        Scatter[id] = float4(0, 0, 0, 0);
        return;
    }

    float3 worldPos = FroxelWorld(id, 0.5, InvViewProj,
        CameraPosNear.xyz, CameraPosNear.w, GridSizeFar.xyz, GridSizeFar.w);
    float3 viewDir = normalize(worldPos - CameraPosNear.xyz);
    float cosTheta = dot(viewDir, SunDirAmbient.xyz);

    // Intensity < -1.5: media probe - bypass lighting so inject/debug
    // outputs can be inspected through the same integration/composite path.
    if (SunColorIntensity.w < -1.5)
    {
        Scatter[id] = media;
        return;
    }

    // Intensity < 0: lighting probe - cosTheta / sun visibility / phase
    // written directly (extinction pinned) for composite inspection.
    if (SunColorIntensity.w < 0)
    {
        Scatter[id] = float4(saturate(cosTheta * 0.5 + 0.5) * 0.004,
            exp(-SunOpticalDepth(worldPos)) * 0.004,
            saturate(DualHgPhase(cosTheta) * 4.0) * 0.004, 0.0008);
        return;
    }

    // The analytic cloud-scale tail is intentionally damped here: at full
    // strength it kills every sun shaft inside a 30km bank (physically
    // true, visually dead). 0.35 keeps interiors moody but lets the local
    // noisy march carve light into the cloudscape.
    float tau = SunOpticalDepth(worldPos)
        + 0.35 * ZonesAnalyticSunTau(Zones, (uint)Temporal.y, worldPos,
            SunDirAmbient.xyz, 1100.0);
    float viewDistance = length(worldPos - CameraPosNear.xyz);
    float shadowTerm = CsmShadowTerm(worldPos, viewDistance);
    float3 sun = SunColorIntensity.rgb * SunColorIntensity.w
        * MsScatter(tau, cosTheta) * shadowTerm;
    float3 ambient = SunDirAmbient.w.xxx;

    // Dynamic lightning: isotropic point flash inside the medium (V4b).
    float3 lightning = 0;
    if (LightningPosRadius.w > 0)
    {
        float3 toBolt = LightningPosRadius.xyz - worldPos;
        float boltDistSq = dot(toBolt, toBolt);
        float atten = 1.0 / (1.0 + boltDistSq * 5.5e-6);
        float rangeFade = saturate(1.0 - sqrt(boltDistSq) / (LightningPosRadius.w * 3.0));
        lightning = LightningColor.rgb * (LightningColor.w * 0.0796) * atten * rangeFade;
    }

    float4 current = float4(media.rgb * (sun + ambient + lightning), media.a);

    // Temporal reprojection (V5): the jittered inject is noisy by design;
    // an exponential history over the REPROJECTED previous frame smooths
    // it. Brightness-clamped against the current sample (anti-ghosting);
    // golden captures run with blend = 0 for determinism.
    if (Temporal.x > 0)
    {
        float3 prevUvw = WorldToFroxelUvw(worldPos, PrevViewProj,
            CameraPosNear.xyz, CameraPosNear.w, GridSizeFar.xyz, GridSizeFar.w);
        if (all(prevUvw >= 0) && all(prevUvw <= 1))
        {
            float4 history = History.SampleLevel(HistorySampler, prevUvw, 0);
            float4 limit = current * 4.0 + 0.0005;
            history = min(history, limit);
            current = lerp(current, history, Temporal.x);
        }
    }

    Scatter[id] = current;
}
