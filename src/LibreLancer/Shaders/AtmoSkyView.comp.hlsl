// Phase 5 / W2 sky-view LUT. This prepares the low-dimensional sky term
// used by W3; W2 mostly proves the compute resource, mapping and debug path.

Texture2D<float4> Transmittance : register(t0, TEXTURE_SPACE);
SamplerState TransmittanceSampler : register(s0, TEXTURE_SPACE);
Texture2D<float4> MultiScattering : register(t1, TEXTURE_SPACE);
SamplerState MultiScatteringSampler : register(s1, TEXTURE_SPACE);

[[vk::image_format("rgba16f")]]
RWTexture2D<float4> Result : register(u4, TEXTURE_SPACE);

cbuffer AtmoSkyParams : register(b3, UNIFORM_SPACE)
{
    float4 SizeMode;          // xy target size, z shell scale, w fade
    float4 SunDirIntensity;   // xyz to sun, w intensity
    float4 RayleighMie;       // rgb rayleigh, a mie
    float4 AbsorptionAlbedo;  // rgb absorption, a ground bounce
};

[numthreads(8, 8, 1)]
void main(uint2 id : SV_DispatchThreadID)
{
    uint2 size = (uint2)SizeMode.xy;
    if (any(id >= size))
        return;

    float2 uv = (id + 0.5) / SizeMode.xy;
    float horizon = 1.0 - abs(uv.y * 2.0 - 1.0);
    float mu = uv.x * 2.0 - 1.0;
    float3 trans = Transmittance.SampleLevel(TransmittanceSampler, float2(uv.x, saturate(uv.y)), 0).rgb;
    float3 multi = MultiScattering.SampleLevel(MultiScatteringSampler, uv, 0).rgb;

    float rayleighPhase = 0.0596831 * (1.0 + mu * mu);
    float3 sky = (1.0 - trans) * RayleighMie.rgb * rayleighPhase * SunDirIntensity.w;
    sky += multi * (0.15 + horizon * 0.5);
    sky += AbsorptionAlbedo.a.xxx * horizon * float3(0.9, 0.75, 0.55);

    Result[id] = float4(saturate(sky), 1.0);
}
