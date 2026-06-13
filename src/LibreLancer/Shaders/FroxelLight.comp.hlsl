// Phase 5 / PR-5.5: debug lighting injection for froxel nebula volumes.
// Reads density and writes sun/ambient lighting. No visible composite happens
// in this pass; legacy nebula rendering remains the default output.

Texture3D<float4> Density : register(t0, TEXTURE_SPACE);
SamplerState DensitySampler : register(s0, TEXTURE_SPACE);

[[vk::image_format("rgba16f")]]
RWTexture3D<float4> Lighting : register(u1, TEXTURE_SPACE);

cbuffer FroxelLightParams : register(b3, UNIFORM_SPACE)
{
    float4 GridSize;            // xyz: grid dimensions, w: 1 for near cascade
    float4 SunDirectionPhase;   // xyz: direction to dominant sun, w: forward HG g
    float4 PhaseParams;         // x: backward HG g, y: forward blend, z: powder, w: god-ray scale
    float4 LightColorIntensity; // rgb: albedo/sun tint, w: intensity multiplier
    float4 AmbientDensity;      // rgb: ambient, w: authored coverage
    float4 TimeParams;          // x: time, y: near metres, z: far metres, w: lightning flag
};

static const float PI4 = 12.566370614359172;

float HgPhase(float cosTheta, float g)
{
    g = clamp(g, -0.95, 0.95);
    float gg = g * g;
    float d = max(1.0 + gg - 2.0 * g * cosTheta, 1e-4);
    return (1.0 - gg) / (PI4 * d * sqrt(d));
}

float DualHg(float cosTheta, float forwardG, float backwardG, float blend)
{
    return lerp(HgPhase(cosTheta, backwardG), HgPhase(cosTheta, forwardG), saturate(blend));
}

float Hash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

[numthreads(4, 4, 4)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= (uint3)GridSize.xyz))
        return;

    float4 d = Density.Load(int4(id, 0));
    float density01 = saturate(d.x);
    float sigmaT = max(d.y, 0.0);
    float zoneFalloff = saturate(d.z);
    float carved = saturate(d.w);

    float3 uvw = (float3(id) + 0.5) / GridSize.xyz;
    float3 sunDir = normalize(SunDirectionPhase.xyz);
    float3 viewDir = normalize(float3(uvw.xy * 2.0 - 1.0, 1.0));
    float cosTheta = dot(viewDir, sunDir);

    float phase = DualHg(cosTheta, SunDirectionPhase.w, PhaseParams.x, PhaseParams.y);
    float powder = 1.0 - exp(-density01 * max(PhaseParams.z, 0.0) * 3.0);

    float lightDistance = GridSize.w > 0.5 ? 180.0 : 2200.0;
    float selfShadow = exp(-sigmaT * lightDistance * lerp(0.25, 1.0, density01));
    float bankContrast = lerp(0.55, 1.0, carved);
    float sunEnergy = LightColorIntensity.w * (1.0 + PhaseParams.w * 0.75);

    float3 direct = LightColorIntensity.rgb * sunEnergy * sigmaT * phase *
        (0.55 + 0.45 * powder) * selfShadow * bankContrast;
    float3 ambient = AmbientDensity.rgb * sigmaT * lerp(0.08, 0.22, AmbientDensity.w) * zoneFalloff;
    if (TimeParams.w > 0.5)
    {
        float sparkle = pow(Hash13(float3(id) + floor(TimeParams.x * 3.0)), 24.0) * density01;
        direct += LightColorIntensity.rgb * sparkle * 0.45;
    }

    Lighting[id] = float4(max(direct + ambient, 0.0), selfShadow);
}
