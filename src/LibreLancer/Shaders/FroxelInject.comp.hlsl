// Phase 5 / PR-5.4: profile-driven density injection prototype.
// This fills the froxel density volume for debug and later lighting passes.
// It does not composite into the frame by itself.

[[vk::image_format("rgba16f")]]
RWTexture3D<float4> Density : register(u0, TEXTURE_SPACE);

Texture2D<float4> JitterNoise : register(t3, TEXTURE_SPACE);
SamplerState JitterSampler : register(s3, TEXTURE_SPACE);
Texture3D<float4> DisplacementVolume : register(t4, TEXTURE_SPACE);
SamplerState DisplacementSampler : register(s4, TEXTURE_SPACE);
Texture3D<float4> WakeVectorField : register(t5, TEXTURE_SPACE);
SamplerState WakeVectorSampler : register(s5, TEXTURE_SPACE);
Texture3D<float4> ImportedDensityVolume : register(t6, TEXTURE_SPACE);
SamplerState ImportedDensitySampler : register(s6, TEXTURE_SPACE);

cbuffer FroxelInjectParams : register(b3, UNIFORM_SPACE)
{
    float4 GridSize;            // xyz: grid dimensions, w: 1 for near cascade
    float4 ZonePositionRadius;  // xyz: profile center, w: conservative radius
    float4 ZoneSizeEdge;        // xyz: authored zone size/radius, w: edge fraction
    float4 DensityParams;       // x: core extinction, y: edge extinction, z: coverage, w: erosion
    float4 NoiseParams;         // x: base noise 1/m, y: domain warp, z: drift m/s, w: total time
    float4 EffectParams;        // x: god rays, y: dust, z: ship displacement, w: profile warp
    float4 ColorParams;         // rgb: authored fog color, w: lightning profile flag
    float4 JitterParams;        // x: enabled, y: tile size, z: frame index, w: reserved
    float4 NearDetailParams;    // x: enabled, y: base freq mul, z: detail freq mul, w: erosion boost
    float4 NearDustParams;      // x: dust density, y: dust strength, z: contrast, w: warp boost
    float4 DisplacementParams;  // x: enabled, y: strength, z: near-only, w: unused
    float4 WakeVectorParams;    // x: enabled, y: domain warp, z: softening, w: unused
    float4 ImportedDensityParams; // x: enabled, y: blend, z: density multiplier, w: unused
};

float Hash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float ValueNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    float n000 = Hash13(i + float3(0, 0, 0));
    float n100 = Hash13(i + float3(1, 0, 0));
    float n010 = Hash13(i + float3(0, 1, 0));
    float n110 = Hash13(i + float3(1, 1, 0));
    float n001 = Hash13(i + float3(0, 0, 1));
    float n101 = Hash13(i + float3(1, 0, 1));
    float n011 = Hash13(i + float3(0, 1, 1));
    float n111 = Hash13(i + float3(1, 1, 1));

    float nx00 = lerp(n000, n100, f.x);
    float nx10 = lerp(n010, n110, f.x);
    float nx01 = lerp(n001, n101, f.x);
    float nx11 = lerp(n011, n111, f.x);
    float nxy0 = lerp(nx00, nx10, f.y);
    float nxy1 = lerp(nx01, nx11, f.y);
    return lerp(nxy0, nxy1, f.z);
}

float Fbm(float3 p)
{
    float sum = 0.0;
    float amp = 0.5;
    [unroll]
    for (int i = 0; i < 5; i++)
    {
        sum += ValueNoise(p) * amp;
        p = p * 2.03 + float3(13.1, 7.7, 5.3);
        amp *= 0.5;
    }
    return saturate(sum * 1.25);
}

float CellularDistance(float3 p)
{
    float3 cell = floor(p);
    float3 f = frac(p);
    float d = 8.0;

    [unroll]
    for (int z = -1; z <= 1; z++)
    {
        [unroll]
        for (int y = -1; y <= 1; y++)
        {
            [unroll]
            for (int x = -1; x <= 1; x++)
            {
                float3 o = float3(x, y, z);
                float3 h = float3(
                    Hash13(cell + o + 11.0),
                    Hash13(cell + o + 47.0),
                    Hash13(cell + o + 83.0));
                d = min(d, length(o + h - f));
            }
        }
    }
    return saturate(d);
}

float3 DomainWarp(float3 p, float strength)
{
    float3 q = float3(
        Fbm(p + float3(0.0, 17.0, 31.0)),
        Fbm(p + float3(43.0, 0.0, 19.0)),
        Fbm(p + float3(11.0, 29.0, 0.0))) - 0.5;
    return p + q * strength;
}

[numthreads(4, 4, 4)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= (uint3)GridSize.xyz))
        return;

    float3 uvw = (float3(id) + 0.5) / GridSize.xyz;
    float3 centered = uvw * 2.0 - 1.0;

    float radius01 = length(centered);
    float edge = max(ZoneSizeEdge.w, 0.05);
    float zoneFalloff = saturate((1.0 - radius01) / edge);
    zoneFalloff = zoneFalloff * zoneFalloff * (3.0 - 2.0 * zoneFalloff);

    float3 worldLike = ZonePositionRadius.xyz + centered * ZonePositionRadius.w;
    float3 drift = float3(0.17, 0.05, -0.11) * (NoiseParams.z * NoiseParams.w * NoiseParams.x);
    float3 p = worldLike * NoiseParams.x + drift;
    if (JitterParams.x > 0.5)
    {
        float tile = max(JitterParams.y, 1.0);
        float2 jitterUv = (float2(id.xy) + float2(JitterParams.z * 0.37, JitterParams.z * 0.61)) / tile;
        float4 jitter = JitterNoise.SampleLevel(JitterSampler, frac(jitterUv), 0);
        p += (jitter.xyz - 0.5) * 0.045;
    }
    float nearDetail = (GridSize.w > 0.5 && NearDetailParams.x > 0.5) ? 1.0 : 0.0;
    float baseMul = lerp(1.0, max(NearDetailParams.y, 0.1), nearDetail);
    float detailMul = lerp(1.0, max(NearDetailParams.z, 0.1), nearDetail);
    float erosionMul = lerp(1.0, max(NearDetailParams.w, 0.1), nearDetail);
    float warpMul = lerp(1.0, max(NearDustParams.w, 0.1), nearDetail);
    float curlSoftening = 0.0;
    if (WakeVectorParams.x > 0.5 && GridSize.w > 0.5)
    {
        float4 wakeVector = WakeVectorField.SampleLevel(WakeVectorSampler, uvw, 0);
        p += wakeVector.xyz * WakeVectorParams.y;
        curlSoftening = saturate(wakeVector.a * WakeVectorParams.z);
    }
    float warpStrength = (NoiseParams.y + EffectParams.w) * (GridSize.w > 0.5 ? 1.35 : 1.0) * warpMul;
    p = DomainWarp(p, warpStrength);

    float baseShape = Fbm(p * 0.75 * baseMul);
    float banks = smoothstep(1.0 - DensityParams.z, 1.0, baseShape);
    float detail = Fbm(p * 3.15 * detailMul + 19.0);
    float cells = 1.0 - CellularDistance(p * (2.15 * detailMul) + 5.0);
    float erosion = saturate(DensityParams.w * erosionMul);

    float carved = saturate(banks - detail * erosion * 0.42 - cells * erosion * 0.18);
    float normalizedDensity = smoothstep(0.02, 0.72, carved) * zoneFalloff;
    float nearDustNoise = ValueNoise(p * (9.0 * detailMul) + float3(31.0, 17.0, 7.0));
    float dustGate = smoothstep(1.0 - saturate(NearDustParams.x), 1.0, nearDustNoise);
    float dust = dustGate * saturate(NearDustParams.y) * zoneFalloff * nearDetail;
    normalizedDensity = saturate(normalizedDensity * lerp(1.0, max(NearDustParams.z, 0.1), nearDetail) + dust);
    if (ImportedDensityParams.x > 0.5)
    {
        float importedDensity = ImportedDensityVolume.SampleLevel(ImportedDensitySampler, uvw, 0).r;
        importedDensity = saturate(importedDensity * max(ImportedDensityParams.z, 0.0)) * zoneFalloff;
        float importedBlend = saturate(ImportedDensityParams.y);
        normalizedDensity = lerp(normalizedDensity, importedDensity, importedBlend);
        carved = max(carved, importedDensity);
    }
    if (DisplacementParams.x > 0.5 && GridSize.w > 0.5)
    {
        float4 disp = DisplacementVolume.SampleLevel(DisplacementSampler, uvw, 0);
        float push = saturate(disp.r * max(DisplacementParams.y, 0.0));
        float wake = saturate(disp.b) * 0.10;
        normalizedDensity = saturate(normalizedDensity * (1.0 - push * 0.72) +
            normalizedDensity * wake);
        carved = max(carved, push);
    }
    normalizedDensity = saturate(normalizedDensity * (1.0 - curlSoftening));
    float extinction = lerp(DensityParams.y, DensityParams.x, zoneFalloff);
    float physicalExtinction = extinction * normalizedDensity;

    Density[id] = float4(normalizedDensity, physicalExtinction, zoneFalloff, max(carved, dust));
}
