// Phase 5 / W2 aerial-perspective froxel. Output is premultiplied
// atmosphere in-scattering in rgb and transmittance in alpha.

Texture2D<float4> TransmittanceLut : register(t0, TEXTURE_SPACE);
SamplerState TransmittanceSampler : register(s0, TEXTURE_SPACE);
Texture2D<float4> MultiScatteringLut : register(t1, TEXTURE_SPACE);
SamplerState MultiScatteringSampler : register(s1, TEXTURE_SPACE);
Texture2D<float4> SkyViewLut : register(t2, TEXTURE_SPACE);
SamplerState SkyViewSampler : register(s2, TEXTURE_SPACE);

[[vk::image_format("rgba16f")]]
RWTexture3D<float4> Result : register(u4, TEXTURE_SPACE);

cbuffer AtmoAerialParams : register(b3, UNIFORM_SPACE)
{
    float4x4 InvViewProj;
    float4 CameraPosPlanetRadius; // xyz camera, w planet radius
    float4 PlanetCenterShell;     // xyz center, w shell radius
    float4 GridMaxDistance;       // xyz grid dims, w max view distance
    float4 SunDirIntensity;       // xyz to sun, w intensity
    float4 RayleighMie;           // rgb rayleigh scale, a mie scale
    float4 AbsorptionAlbedo;      // rgb absorption, a ground bounce
};

float2 RaySphere(float3 ro, float3 rd, float3 c, float r)
{
    float3 oc = ro - c;
    float b = dot(oc, rd);
    float disc = b * b - (dot(oc, oc) - r * r);
    if (disc < 0.0)
        return float2(1e9, -1e9);
    float s = sqrt(disc);
    return float2(-b - s, -b + s);
}

[numthreads(4, 4, 4)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint3 grid = (uint3)GridMaxDistance.xyz;
    if (any(id >= grid))
        return;

    float2 uv = (id.xy + 0.5) / GridMaxDistance.xy;
    float2 ndc = uv * 2.0 - 1.0;
    float4 nearPoint = mul(float4(ndc, 0.0, 1.0), InvViewProj);
    nearPoint /= nearPoint.w;
    float4 farPoint = mul(float4(ndc, 1.0, 1.0), InvViewProj);
    farPoint /= farPoint.w;

    float3 ro = CameraPosPlanetRadius.xyz;
    float3 rd = normalize(farPoint.xyz - nearPoint.xyz);
    float viewDist = GridMaxDistance.w * ((id.z + 1.0) / GridMaxDistance.z);
    float3 center = PlanetCenterShell.xyz;
    float planetRadius = CameraPosPlanetRadius.w;
    float shellRadius = PlanetCenterShell.w;
    float thickness = max(shellRadius - planetRadius, 1.0);
    float scaleHeight = max(thickness * 0.12, 1.0);
    float tauScale = 0.12 / scaleHeight;

    float2 shellHit = RaySphere(ro, rd, center, shellRadius);
    float startT = max(shellHit.x, 0.0);
    float endT = min(shellHit.y, viewDist);
    float2 planetHit = RaySphere(ro, rd, center, planetRadius);
    if (planetHit.x > 0.0)
    {
        endT = min(endT, planetHit.x);
    }

    if (endT <= startT)
    {
        Result[id] = float4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    const int STEPS = 4;
    float stepLen = (endT - startT) / STEPS;
    float transmittance = 1.0;
    float3 light = 0.0;
    float cosTheta = dot(rd, SunDirIntensity.xyz);
    float rayleighPhase = 0.0596831 * (1.0 + cosTheta * cosTheta);
    float mie = pow(saturate(cosTheta * 0.5 + 0.5), 8.0) * RayleighMie.a;

    [unroll]
    for (int i = 0; i < STEPS; i++)
    {
        float t = startT + (i + 0.5) * stepLen;
        float3 p = ro + rd * t;
        float height = max(length(p - center) - planetRadius, 0.0);
        float hNorm = saturate(height / thickness);
        float density = exp(-height / scaleHeight);
        float3 up = normalize(p - center);
        float day = 0.18 + 0.82 * smoothstep(-0.18, 0.35, dot(up, SunDirIntensity.xyz));
        float tau = density * stepLen * tauScale;
        float segTrans = exp(-tau);
        float2 lutUv = float2(cosTheta * 0.5 + 0.5, hNorm);
        float3 transLut = TransmittanceLut.SampleLevel(TransmittanceSampler, lutUv, 0).rgb;
        float3 multi = MultiScatteringLut.SampleLevel(MultiScatteringSampler, lutUv, 0).rgb;
        float3 sky = SkyViewLut.SampleLevel(SkyViewSampler,
            float2(cosTheta * 0.5 + 0.5, 1.0 - hNorm), 0).rgb;
        float3 scatterColour = (RayleighMie.rgb * rayleighPhase * transLut + multi + sky * 0.25);
        scatterColour += float3(1.0, 0.9, 0.78) * mie * 0.08;
        float weight = transmittance * (1.0 - segTrans);
        light += scatterColour * weight * day * SunDirIntensity.w * 64.0;
        transmittance *= segTrans;
    }

    Result[id] = float4(light, transmittance);
}
