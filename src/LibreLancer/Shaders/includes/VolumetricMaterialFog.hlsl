#ifndef LL_VOLUMETRIC_MATERIAL_FOG_INCLUDED
#define LL_VOLUMETRIC_MATERIAL_FOG_INCLUDED

#include "Camera.hlsl"

#ifndef LL_VOLF_USE_LIGHTING_CBUFFER
cbuffer VolumetricMaterialFog : register(b2, UNIFORM_SPACE)
{
    float4 VolFogParams;  // x active, y near, z far, w gridZ
    float4 VolFogParams2; // x meanExtinction, y extinctionOnly, zw unused
};
#endif

Texture3D<float4> VolFogIntegrated : register(t15, TEXTURE_SPACE);
SamplerState VolFogSampler : register(s15, TEXTURE_SPACE);
Texture3D<float4> AtmoAerial : register(t14, TEXTURE_SPACE);
SamplerState AtmoAerialSampler : register(s14, TEXTURE_SPACE);

float VolFogDistanceToTextureSlice(float dist, float numSlices, float nearPlane, float farPlane)
{
    float normalized = sqrt(saturate((dist - nearPlane) / max(farPlane - nearPlane, 1e-4)));
    float depth = max(numSlices, 1.0);
    return saturate((normalized * max(depth - 1.0, 0.0) + 0.5) / depth);
}

float4 SampleVolumetricFog(float3 worldPos, float viewDist)
{
    if (VolFogParams.x <= 0.5 || viewDist <= VolFogParams.y)
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }

    float4 clip = mul(float4(worldPos, 1.0), ViewProjection);
    if (clip.w <= 0.0)
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }

    float2 uv = clip.xy / clip.w * 0.5 + 0.5;
    float froxelDist = min(viewDist, VolFogParams.z);
    float slice = VolFogDistanceToTextureSlice(froxelDist, VolFogParams.w, VolFogParams.y, VolFogParams.z);
    float4 fog = VolFogIntegrated.SampleLevel(VolFogSampler,
        float3(saturate(uv), slice), 0);

    if (viewDist > VolFogParams.z)
    {
        fog.a *= exp(-(viewDist - VolFogParams.z) * max(VolFogParams2.x, 0.0));
    }

    return fog;
}

float4 SampleAtmosphereAerial(float3 worldPos, float viewDist)
{
    if (VolFogParams2.z <= 0.5 || viewDist <= 0.0)
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }

    float4 clip = mul(float4(worldPos, 1.0), ViewProjection);
    if (clip.w <= 0.0)
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }

    float2 uv = clip.xy / clip.w * 0.5 + 0.5;
    float maxDistance = max(VolFogParams2.w, 1.0);
    float z = saturate(viewDist / maxDistance);
    return AtmoAerial.SampleLevel(AtmoAerialSampler, float3(saturate(uv), z), 0);
}

float3 ApplyVolumetricFog(float3 worldPos, float viewDist, float3 color)
{
    float4 aerial = SampleAtmosphereAerial(worldPos, viewDist);
    float4 fog = SampleVolumetricFog(worldPos, viewDist);
    color = aerial.rgb + color * aerial.a;
    if (VolFogParams2.y > 0.5)
    {
        return color * fog.a;
    }
    return fog.rgb + color * fog.a;
}

float4 ApplyVolumetricFog(float3 worldPos, float viewDist, float4 color)
{
    color.rgb = ApplyVolumetricFog(worldPos, viewDist, color.rgb);
    return color;
}

float4 ApplyVolumetricFogAdditive(float3 worldPos, float viewDist, float4 color)
{
    float4 aerial = SampleAtmosphereAerial(worldPos, viewDist);
    float4 fog = SampleVolumetricFog(worldPos, viewDist);
    color.rgb *= aerial.a * fog.a;
    return color;
}

#endif
