// Volumetric fog composite (track V): fullscreen pass between the
// opaque/starsphere stage and the transparent pass. Reconstructs the view
// distance from the copied scene depth, samples the integrated froxel
// volume and blends scattered light over the scene with
// (One, SrcAlpha) - output alpha carries transmittance.

#include "includes/VolumeCommon.hlsl"

struct Input
{
    float2 texCoord : TEXCOORD0;
};

Texture3D<float4> Integrated : register(t0, TEXTURE_SPACE);
SamplerState IntegratedSampler : register(s0, TEXTURE_SPACE);
Texture2D<float> SceneDepth : register(t1, TEXTURE_SPACE);
SamplerState DepthSampler : register(s1, TEXTURE_SPACE);
Texture3D<float4> NearIntegrated : register(t2, TEXTURE_SPACE);
SamplerState NearIntegratedSampler : register(s2, TEXTURE_SPACE);
Texture2D<float4> Distant : register(t3, TEXTURE_SPACE); // half-res far layer (V6)
SamplerState DistantSampler : register(s3, TEXTURE_SPACE);

cbuffer CompositeParams : register(b3, UNIFORM_SPACE)
{
    float4x4 InvViewProj;
    float4 CameraPosNear; // xyz camera, w froxel near
    float4 GridSizeFar;   // xyz grid dimensions, w froxel far
    float4 NearGridSizeFar; // xyz near-cascade grid, w near far
    float4 CascadeBlend;  // x near-cascade near, y blend start, z near active
    float4 DebugMode;     // x: 0 normal, 1 zone view; y: distant layer on; z: min transmittance
};

float4 SampleIntegrated(
    Texture3D<float4> tex, SamplerState samp, float2 uv,
    float dist, float slices, float nearPlane, float farPlane)
{
    if (dist <= nearPlane)
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }
    float slice = DistanceToSlice(min(dist, farPlane), slices, nearPlane, farPlane);
    return tex.SampleLevel(samp, float3(uv, saturate(slice / slices)), 0);
}

float4 ComposeVolume(float4 front, float4 back)
{
    return float4(front.rgb + front.a * back.rgb, front.a * back.a);
}

float4 main(Input input) : SV_Target0
{
    float depth = SceneDepth.SampleLevel(DepthSampler, input.texCoord, 0);
    float2 ndc = input.texCoord * 2.0 - 1.0;
    float4 nearPoint = mul(float4(ndc, 0.0, 1.0), InvViewProj);
    nearPoint /= nearPoint.w;

    float viewDist;
    if (depth >= 0.9999999)
    {
        viewDist = GridSizeFar.w; // starsphere/sky: full froxel range
    }
    else
    {
        float4 scenePoint = mul(float4(ndc, depth, 1.0), InvViewProj);
        scenePoint /= scenePoint.w;
        viewDist = length(scenePoint.xyz - CameraPosNear.xyz);
    }

    float4 mainFog = SampleIntegrated(Integrated, IntegratedSampler,
        input.texCoord, viewDist, GridSizeFar.z, CameraPosNear.w, GridSizeFar.w);
    float4 fog = mainFog;

    if (CascadeBlend.z > 0.5)
    {
        float blendStart = CascadeBlend.y;
        float nearFar = NearGridSizeFar.w;
        float4 nearAtDist = SampleIntegrated(NearIntegrated, NearIntegratedSampler,
            input.texCoord, min(viewDist, nearFar), NearGridSizeFar.z,
            CascadeBlend.x, nearFar);
        float4 nearBase = SampleIntegrated(NearIntegrated, NearIntegratedSampler,
            input.texCoord, blendStart, NearGridSizeFar.z, CascadeBlend.x, nearFar);
        float4 composed = ComposeVolume(nearBase, mainFog);

        if (viewDist < blendStart)
        {
            fog = nearAtDist;
        }
        else if (viewDist < nearFar)
        {
            float t = saturate((viewDist - blendStart) / max(nearFar - blendStart, 1e-3));
            fog = lerp(nearAtDist, composed, t);
        }
        else
        {
            fog = composed;
        }
    }

    // Distant layer rides UNDER the froxel result: it only applies where
    // the scene depth reaches past the froxel range (sky/far geometry).
    if (DebugMode.y > 0.5 && viewDist >= GridSizeFar.w * 0.98)
    {
        float4 distant = Distant.SampleLevel(DistantSampler, input.texCoord, 0);
        fog.rgb += fog.a * distant.rgb;
        fog.a *= distant.a;
    }

    if (DebugMode.x > 1.5)
    {
        // Depth-copy debug (SIRIUS_DEBUG_VIEW=voldepth): linearised view
        // distance, red where the far-layer skip condition holds.
        float shade = saturate(viewDist / GridSizeFar.w);
        float farMask = viewDist >= GridSizeFar.w * 0.98 ? 1.0 : 0.0;
        return float4(shade * farMask + (1.0 - farMask), shade * farMask, shade * farMask, 0.0);
    }
    if (DebugMode.x > 0.5)
    {
        // Zone debug: density heat (1 - transmittance) as orange overlay,
        // scatter preview in the background. Opaque output for clarity.
        float density = 1.0 - fog.a;
        return float4(density, density * 0.5, fog.b * 0.25 + density * 0.1, 0.0);
    }

    return float4(fog.rgb, max(fog.a, DebugMode.z));
}
