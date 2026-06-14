// Phase 5 / PR-5.16: velocity-aligned curl vector field for persistent wake.
// Converts wake occupancy into a subtle vector field for near-density domain
// warp. This is a cheap polish pass, not a fluid solver.

Texture3D<float4> WakeHistory : register(t0, TEXTURE_SPACE);
SamplerState WakeSampler : register(s0, TEXTURE_SPACE);

[[vk::image_format("rgba16f")]]
RWTexture3D<float4> WakeVectors : register(u7, TEXTURE_SPACE);

cbuffer WakeCurlParams : register(b3, UNIFORM_SPACE)
{
    float4 GridSize;    // xyz dimensions, w time
    float4 CurlParams;  // x enabled, y curl strength, z axis wake strength, w noise frequency
    float4 CurlParams2; // xyz velocity axis, w vector damping
};

float Hash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float SampleWake(float3 uvw)
{
    float4 h = WakeHistory.SampleLevel(WakeSampler, saturate(uvw), 0);
    return saturate(max(h.b, max(h.g * 0.65, h.a * 0.35)));
}

[numthreads(4, 4, 4)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= (uint3)GridSize.xyz))
        return;

    float3 dims = max(GridSize.xyz, 1.0);
    float3 uvw = (float3(id) + 0.5) / dims;
    float4 history = WakeHistory.SampleLevel(WakeSampler, uvw, 0);

    if (CurlParams.x <= 0.5)
    {
        WakeVectors[id] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    float3 texel = 1.0 / dims;
    float dx = SampleWake(uvw + float3(texel.x, 0, 0)) - SampleWake(uvw - float3(texel.x, 0, 0));
    float dy = SampleWake(uvw + float3(0, texel.y, 0)) - SampleWake(uvw - float3(0, texel.y, 0));
    float dz = SampleWake(uvw + float3(0, 0, texel.z)) - SampleWake(uvw - float3(0, 0, texel.z));
    float3 gradient = float3(dx, dy, dz);

    float3 axis = CurlParams2.xyz;
    axis = dot(axis, axis) > 1e-4 ? normalize(axis) : float3(0.0, 0.0, 1.0);
    float3 tangent = cross(axis, gradient);
    tangent = dot(tangent, tangent) > 1e-6 ? normalize(tangent) : float3(0.0, 0.0, 0.0);

    float occupancy = saturate(history.a);
    float wake = saturate(history.b);
    float swirl = saturate(history.g);
    float noise = Hash13(uvw * CurlParams.w + GridSize.w * 0.037);
    float signedNoise = noise * 2.0 - 1.0;

    float3 axisPull = -axis * (wake * CurlParams.z);
    float3 curl = tangent * (swirl + wake * 0.5) * CurlParams.y * (0.85 + 0.30 * signedNoise);
    float3 vector = (axisPull + curl) * saturate(CurlParams2.w);
    vector = clamp(vector, -0.45, 0.45);

    WakeVectors[id] = float4(vector, occupancy);
}
