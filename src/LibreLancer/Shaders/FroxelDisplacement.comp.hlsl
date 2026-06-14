// PR-5.14: near-field fog displacement scaffold.
// Writes subtle camera-relative push, swirl and wake terms for near froxels.

[[vk::image_format("rgba16f")]]
RWTexture3D<float4> DisplacementOut : register(u5, TEXTURE_SPACE);

cbuffer DisplacementParams : register(b3, UNIFORM_SPACE)
{
    float4 GridSize;             // xyz dimensions, w capsule count
    float4 GlobalParams;         // x nearFarMeters, y profile strength, z time, w unused
    float4 CapsuleA[16];         // xyz camera-relative nose, w radius
    float4 CapsuleB[16];         // xyz camera-relative tail, w strength
    float4 CapsuleVelocity[16];  // xyz velocity, w wake half-life
    float4 CapsuleSwirl[16];     // x swirl strength
};

float CapsuleDistance(float3 p, float3 a, float3 b, out float t)
{
    float3 ab = b - a;
    float inv = rcp(max(dot(ab, ab), 1e-4));
    t = saturate(dot(p - a, ab) * inv);
    float3 c = a + ab * t;
    return length(p - c);
}

float Hash31(float3 p)
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

    float3 uvw = (id + 0.5) / GridSize.xyz;
    float nearFar = max(GlobalParams.x, 1.0);
    float3 p;
    p.xy = (uvw.xy * 2.0 - 1.0) * nearFar * 0.72;
    p.z = (uvw.z * 2.0 - 1.0) * nearFar;

    float push = 0.0;
    float swirl = 0.0;
    float wake = 0.0;
    uint count = min((uint)GridSize.w, 16u);
    [loop]
    for (uint i = 0; i < count; i++)
    {
        float3 a = CapsuleA[i].xyz;
        float3 b = CapsuleB[i].xyz;
        float radius = max(CapsuleA[i].w, 1.0);
        float strength = max(CapsuleB[i].w, 0.0) * max(GlobalParams.y, 0.0);
        float t;
        float d = CapsuleDistance(p, a, b, t);

        float core = saturate(1.0 - d / max(radius * 1.25, 1.0));
        core = core * core * (3.0 - 2.0 * core);
        push = max(push, core * strength);

        float3 v = CapsuleVelocity[i].xyz;
        float speed = length(v);
        float3 dir = speed > 1.0 ? normalize(v) : normalize(b - a + float3(0.0, 0.0, 1e-3));
        float behind = saturate(dot((b - p), dir) / max(radius * 8.0, 1.0));
        float lateral = exp(-d * d / max(radius * radius * 5.0, 1.0));
        float w = behind * lateral * saturate(speed / 650.0) * strength;
        wake = max(wake, w);

        float n = Hash31(p * 0.015 + GlobalParams.z * 0.07 + i * 17.0);
        swirl += (n - 0.5) * CapsuleSwirl[i].x * w;
    }

    push = saturate(push);
    wake = saturate(wake);
    swirl = clamp(swirl * 0.5 + 0.5, 0.0, 1.0) * wake;
    DisplacementOut[id] = float4(push, swirl, wake, max(push, wake));
}
