// Dynamic near-fog displacement (V10): a small world-anchored field that
// fades over time and receives swept capsule splats from nearby ships.

Texture3D<float4> PreviousField : register(t0, TEXTURE_SPACE);
SamplerState PreviousFieldSampler : register(s0, TEXTURE_SPACE);

[[vk::image_format("rgba16f")]]
RWTexture3D<float4> DisplacementField : register(u4, TEXTURE_SPACE);

cbuffer FogDisplaceParams : register(b3, UNIFORM_SPACE)
{
    float4 OriginExtent;        // xyz current min corner, w extent
    float4 PrevOriginExtent;    // xyz previous min corner, w extent
    float4 GridDt;              // xyz grid dimensions, w dt
    float4 CountsDecay;         // x capsule count, y decay seconds
    float4 CapsuleStartRadius[8];
    float4 CapsuleEndStrength[8];
};

float CapsuleDistance(float3 p, float3 a, float3 b)
{
    float3 ab = b - a;
    float denom = max(dot(ab, ab), 1e-4);
    float t = saturate(dot(p - a, ab) / denom);
    return length(p - (a + ab * t));
}

[numthreads(4, 4, 4)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint3 grid = (uint3)GridDt.xyz;
    if (any(id >= grid))
        return;

    float3 uvw = (id + 0.5) / GridDt.xyz;
    float3 worldPos = OriginExtent.xyz + uvw * OriginExtent.w;

    float4 prev = 0;
    if (PrevOriginExtent.w > 0)
    {
        float3 prevUv = (worldPos - PrevOriginExtent.xyz) / PrevOriginExtent.w;
        if (all(prevUv >= 0.0) && all(prevUv <= 1.0))
            prev = PreviousField.SampleLevel(PreviousFieldSampler, prevUv, 0);
    }

    float decay = exp(-max(GridDt.w, 0.0) / max(CountsDecay.y, 0.01));
    float push = prev.r * decay;
    float swirl = prev.g * decay;

    uint capsuleCount = min((uint)CountsDecay.x, 8u);
    for (uint i = 0; i < capsuleCount; i++)
    {
        float4 sr = CapsuleStartRadius[i];
        float4 es = CapsuleEndStrength[i];
        float d = CapsuleDistance(worldPos, sr.xyz, es.xyz);
        float falloff = smoothstep(sr.w, 0.0, d);
        float strength = saturate(es.w);
        push += falloff * strength;
        swirl += falloff * strength * 0.75;
    }

    DisplacementField[id] = float4(saturate(push), saturate(swirl), 0.0, 1.0);
}
