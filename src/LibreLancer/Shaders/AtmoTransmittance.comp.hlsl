// Phase 5 / W1 atmosphere transmittance LUT bake. The mapping mirrors the
// Hillaire-style precomputed path at low cost: X is view zenith cosine,
// Y is altitude inside the atmosphere shell.

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

    float density = exp(-height * 6.0);
    float shellScale = max(SizeMode.z, 0.25);
    float fade = max(SizeMode.w, 0.15);
    float pathLength = 0.35 + horizon * horizon * 9.5 + saturate(-mu) * 3.0;
    pathLength *= lerp(0.75, 1.35, saturate((shellScale - 1.0) / 4.0)) * fade;
    float3 rayleigh = RayleighMie.rgb * 0.075;
    float3 absorption = AbsorptionAlbedo.rgb * smoothstep(0.18, 0.72, height) * 0.65;
    float mie = RayleighMie.a * (0.32 + horizon * 0.5);
    float3 tau = (rayleigh + absorption + mie.xxx) * density * pathLength;
    float3 transmittance = exp(-tau);

    Result[id] = float4(transmittance, 1.0);
}
