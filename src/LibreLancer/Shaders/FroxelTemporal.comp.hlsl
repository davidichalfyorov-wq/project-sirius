// Phase 5 / PR-5.8: deterministic temporal scaffold for integrated froxel fog.
// No motion-vector reprojection yet; this pass establishes history ping-pong,
// reset, jitter metadata and clamp for later STBN/reprojection work.

Texture3D<float4> CurrentIntegrated : register(t0, TEXTURE_SPACE);
SamplerState CurrentSampler : register(s0, TEXTURE_SPACE);
Texture3D<float4> HistoryRead : register(t1, TEXTURE_SPACE);
SamplerState HistorySampler : register(s1, TEXTURE_SPACE);
Texture2D<float> SceneDepth : register(t2, TEXTURE_SPACE);
SamplerState DepthSampler : register(s2, TEXTURE_SPACE);
Texture2D<float4> JitterNoise : register(t3, TEXTURE_SPACE);
SamplerState JitterSampler : register(s3, TEXTURE_SPACE);

[[vk::image_format("rgba16f")]]
RWTexture3D<float4> HistoryWrite : register(u4, TEXTURE_SPACE);
[[vk::image_format("rgba16f")]]
RWTexture3D<float4> HistoryConfidenceWrite : register(u5, TEXTURE_SPACE);

cbuffer FroxelTemporalParams : register(b3, UNIFORM_SPACE)
{
    float4 GridSize;       // xyz: grid dimensions, w: frame index
    float4 TemporalParams; // x: history weight, y: clamp sigma, z: reset, w: near cascade
    float4 JitterParams;   // xy: jitter in [-0.5,0.5], z: quality, w: blue-noise enabled
    float4 ReprojectionParams; // x: enabled, y: depth tolerance metres, z: near, w: far
    float4 RejectParams;       // x: reject strength, y: 1/(far-near), zw reserved
    float4 PreviousCameraPosition;
    float4 CurrentCameraPosition;
    float4x4 PreviousViewProjection;
    float4x4 CurrentInverseViewProjection;
};

float4 ClampHistory(float4 current, float4 previous, float sigma)
{
    float3 radius = max(abs(current.rgb) * sigma, 0.02.xxx);
    float3 lo = current.rgb - radius;
    float3 hi = current.rgb + radius;
    float aRadius = max(abs(current.a) * sigma, 0.02);
    return float4(clamp(previous.rgb, lo, hi), clamp(previous.a, current.a - aRadius, current.a + aRadius));
}

float DistanceToSlice(float distanceMeters)
{
    float t = saturate((distanceMeters - ReprojectionParams.z) * RejectParams.y);
    return sqrt(t);
}

float3 ReprojectHistoryUvw(float3 uvw, out float confidence, out float distanceMismatch)
{
    confidence = 1.0;
    distanceMismatch = 0.0;
    if (ReprojectionParams.x <= 0.5)
        return uvw;

    float sceneDepth = SceneDepth.SampleLevel(DepthSampler, uvw.xy, 0);
    if (sceneDepth >= 0.999999)
        return uvw;

    float2 ndc = uvw.xy * 2.0 - 1.0;
    float4 world = mul(float4(ndc, sceneDepth, 1.0), CurrentInverseViewProjection);
    world.xyz /= max(abs(world.w), 1e-6);

    float currentDistance = length(world.xyz - CurrentCameraPosition.xyz);
    float previousDistance = length(world.xyz - PreviousCameraPosition.xyz);
    float4 prevClip = mul(float4(world.xyz, 1.0), PreviousViewProjection);
    if (prevClip.w <= 1e-5)
    {
        confidence = 0.0;
        return uvw;
    }

    float2 prevUv = prevClip.xy / prevClip.w * 0.5 + 0.5;
    float prevSlice = DistanceToSlice(previousDistance);
    float2 inside = step(0.0.xx, prevUv) * step(prevUv, 1.0.xx);
    float insideTerm = inside.x * inside.y;

    distanceMismatch = abs(previousDistance - currentDistance);
    float depthReject = saturate(distanceMismatch / max(ReprojectionParams.y, 1.0));
    float sliceReject = saturate(abs(prevSlice - uvw.z) * max(RejectParams.x, 0.01));
    confidence = saturate(insideTerm * (1.0 - max(depthReject, sliceReject)));
    return confidence > 0.001 ? float3(prevUv, prevSlice) : uvw;
}

[numthreads(4, 4, 4)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= (uint3)GridSize.xyz))
        return;

    float3 uvw = (id + 0.5) / GridSize.xyz;
    uvw.xy = saturate(uvw.xy + JitterParams.xy / max(GridSize.xy, 1.0.xx));
    if (JitterParams.w > 0.5)
    {
        float2 jitterUv = (float2(id.xy) + float2(GridSize.w * 0.19, GridSize.w * 0.43)) / 64.0;
        float2 jitter = JitterNoise.SampleLevel(JitterSampler, frac(jitterUv), 0).xy - 0.5;
        uvw.xy = saturate(uvw.xy + jitter / max(GridSize.xy, 1.0.xx));
    }
    float4 current = CurrentIntegrated.SampleLevel(CurrentSampler, uvw, 0);
    float confidence;
    float distanceMismatch;
    float3 historyUvw = ReprojectHistoryUvw(uvw, confidence, distanceMismatch);
    float4 previous = HistoryRead.SampleLevel(HistorySampler, historyUvw, 0);

    if (TemporalParams.z > 0.5)
    {
        HistoryWrite[id] = current;
        HistoryConfidenceWrite[id] = float4(1.0, 1.0, 0.0, 1.0);
        return;
    }

    float4 clamped = ClampHistory(current, previous, max(TemporalParams.y, 0.1));
    float weight = saturate(TemporalParams.x) * confidence;
    HistoryWrite[id] = lerp(current, clamped, weight);
    HistoryConfidenceWrite[id] = float4(confidence, weight,
        saturate(distanceMismatch / max(ReprojectionParams.y, 1.0)), 1.0);
}
