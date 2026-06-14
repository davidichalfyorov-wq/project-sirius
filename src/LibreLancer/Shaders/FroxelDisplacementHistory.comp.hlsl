// Phase 5 / PR-5.15: persistent near-field wake history.
// Blends current ship push/swirl/wake with previous history so fog closes
// softly behind a ship over a few seconds.

Texture3D<float4> CurrentDisplacement : register(t0, TEXTURE_SPACE);
SamplerState CurrentSampler : register(s0, TEXTURE_SPACE);
Texture3D<float4> HistoryRead : register(t1, TEXTURE_SPACE);
SamplerState HistorySampler : register(s1, TEXTURE_SPACE);

[[vk::image_format("rgba16f")]]
RWTexture3D<float4> HistoryWrite : register(u6, TEXTURE_SPACE);

cbuffer DisplacementHistoryParams : register(b3, UNIFORM_SPACE)
{
    float4 GridSize;       // xyz dimensions
    float4 HistoryParams;  // x dt, y wake half-life, z push half-life, w swirl half-life
    float4 HistoryParams2; // x wake strength, y enabled
};

float Decay(float dt, float halfLife)
{
    return halfLife <= 0.0 ? 0.0 : exp2(-max(dt, 0.0) / halfLife);
}

[numthreads(4, 4, 4)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= (uint3)GridSize.xyz))
        return;

    float3 uvw = (id + 0.5) / GridSize.xyz;
    float4 current = CurrentDisplacement.SampleLevel(CurrentSampler, uvw, 0);
    float4 history = HistoryRead.SampleLevel(HistorySampler, uvw, 0);

    if (HistoryParams2.y <= 0.5)
    {
        HistoryWrite[id] = current;
        return;
    }

    float dt = max(HistoryParams.x, 1.0 / 240.0);
    float wakeDecay = Decay(dt, max(HistoryParams.y, 0.05));
    float pushDecay = Decay(dt, max(HistoryParams.z, 0.05));
    float swirlDecay = Decay(dt, max(HistoryParams.w, 0.05));

    float push = max(current.r, history.r * pushDecay);
    float swirl = max(current.g, history.g * swirlDecay);
    float wake = max(current.b, history.b * wakeDecay) * max(HistoryParams2.x, 0.0);
    float occupancy = max(push, max(swirl, wake));
    HistoryWrite[id] = saturate(float4(push, swirl, wake, occupancy));
}
