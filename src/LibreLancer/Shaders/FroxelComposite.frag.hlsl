// Phase 5 / PR-5.6: first opt-in volumetric composite prototype.
// The shader applies participating-media composition to the HDR scene:
// C_out = C_scene * transmittance + in_scattering.

struct Input
{
    float2 texCoord : TEXCOORD0;
};

Texture2D<float4> SceneColor : register(t0, TEXTURE_SPACE);
SamplerState SceneSampler : register(s0, TEXTURE_SPACE);
Texture3D<float4> IntegratedVolume : register(t1, TEXTURE_SPACE);
SamplerState VolumeSampler : register(s1, TEXTURE_SPACE);
Texture2D<float> SceneDepth : register(t2, TEXTURE_SPACE);
SamplerState DepthSampler : register(s2, TEXTURE_SPACE);
Texture3D<float4> NearIntegratedVolume : register(t3, TEXTURE_SPACE);
SamplerState NearVolumeSampler : register(s3, TEXTURE_SPACE);

cbuffer FroxelCompositeParams : register(b3, UNIFORM_SPACE)
{
    float4 CompositeParams; // x: intensity, y: representative slice, z: scatter gain, w: composite/copy
    float4 GridParams;      // xyz: main grid dimensions, w: future depth mode/pixel divisor
    float4 DepthParams;     // x: enabled, y: near meters, z: 1/(far-near), w: far meters
    float4 NearParams;      // x: enabled, y: near far, z: fade start, w: inverse fade range
    float4x4 InverseProjection;
};

float FroxelTextureSliceFromNormalized(float normalized, float depth)
{
    return saturate((saturate(normalized) * max(depth - 1.0, 0.0) + 0.5) / max(depth, 1.0));
}

float FroxelSliceFromDepth(float2 uv, float fallbackSlice)
{
    if (DepthParams.x < 0.5)
    {
        return FroxelTextureSliceFromNormalized(fallbackSlice, GridParams.z);
    }

    float deviceDepth = SceneDepth.SampleLevel(DepthSampler, uv, 0);
    if (deviceDepth >= 0.999999)
    {
        return FroxelTextureSliceFromNormalized(fallbackSlice, GridParams.z);
    }

    float2 ndc = uv * 2.0 - 1.0;
    float4 view = mul(float4(ndc, deviceDepth, 1.0), InverseProjection);
    view.xyz /= max(abs(view.w), 1e-6);
    float viewDistance = length(view.xyz);
    float normalized = saturate((viewDistance - DepthParams.y) * DepthParams.z);
    return FroxelTextureSliceFromNormalized(sqrt(normalized), GridParams.z);
}

float ViewDistanceFromDepth(float2 uv)
{
    float deviceDepth = SceneDepth.SampleLevel(DepthSampler, uv, 0);
    if (DepthParams.x < 0.5 || deviceDepth >= 0.999999)
    {
        return DepthParams.w;
    }

    float2 ndc = uv * 2.0 - 1.0;
    float4 view = mul(float4(ndc, deviceDepth, 1.0), InverseProjection);
    view.xyz /= max(abs(view.w), 1e-6);
    return length(view.xyz);
}

float4 BlendNearCascade(float2 uv, float viewDistance, float4 mainFog)
{
    if (NearParams.x < 0.5)
    {
        return mainFog;
    }

    float nearSlice = saturate(viewDistance / max(NearParams.y, 1e-3));
    float4 nearFog = NearIntegratedVolume.SampleLevel(NearVolumeSampler, float3(uv, nearSlice), 0);
    float fadeToMain = saturate((viewDistance - NearParams.z) * NearParams.w);
    return lerp(nearFog, mainFog, fadeToMain);
}

float4 main(Input input) : SV_Target0
{
    float4 scene = SceneColor.SampleLevel(SceneSampler, input.texCoord, 0);
    if (CompositeParams.w < 0.5)
    {
        return scene;
    }

    float slice = FroxelSliceFromDepth(input.texCoord, saturate(CompositeParams.y));
    float4 integrated = IntegratedVolume.SampleLevel(VolumeSampler, float3(input.texCoord, slice), 0);
    float viewDistance = ViewDistanceFromDepth(input.texCoord);
    integrated = BlendNearCascade(input.texCoord, viewDistance, integrated);
    float intensity = saturate(CompositeParams.x);
    float transmittance = lerp(1.0, saturate(integrated.a), intensity);
    float3 scattering = max(integrated.rgb, 0.0.xxx) * max(CompositeParams.z, 0.0) * intensity;

    return float4(scene.rgb * transmittance + scattering, scene.a);
}
