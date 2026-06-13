// Phase 5 / W1 atmosphere multiple-scattering LUT seed. This is a compact
// energy cache for W3's shader switch, not yet sampled by the visible path.

Texture2D<float4> Transmittance : register(t0, TEXTURE_SPACE);
SamplerState TransmittanceSampler : register(s0, TEXTURE_SPACE);

[[vk::image_format("rgba16f")]]
RWTexture2D<float4> Result : register(u4, TEXTURE_SPACE);

cbuffer AtmoLutParams : register(b3, UNIFORM_SPACE)
{
    float4 SizeMode;          // xy: target size, z: shell scale, w: fade
    float4 RayleighMie;       // rgb: rayleigh scale, a: mie scale
    float4 AbsorptionAlbedo;  // rgb: ozone/absorption, a: ground bounce
};

[numthreads(8, 8, 1)]
void main(uint2 id : SV_DispatchThreadID)
{
    uint2 size = (uint2)SizeMode.xy;
    if (any(id >= size))
        return;

    float2 uv = (id + 0.5) / SizeMode.xy;
    float mu = uv.x * 2.0 - 1.0;
    float height = saturate(uv.y);
    float horizon = sqrt(saturate(1.0 - mu * mu));
    float3 trans = Transmittance.SampleLevel(TransmittanceSampler, uv, 0).rgb;

    float3 skyLoss = 1.0 - trans;
    float phase = 0.22 + 0.55 * pow(saturate(mu * 0.5 + 0.5), 2.0);
    float groundBounce = AbsorptionAlbedo.a * SizeMode.w * (1.0 - height) * (0.35 + horizon);
    float3 warmBounce = float3(1.0, 0.78, 0.52) * groundBounce;
    float3 scatter = skyLoss * (phase + horizon * 0.22) + warmBounce;

    Result[id] = float4(scatter, 1.0);
}
