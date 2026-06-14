// PR-5.17: segmented lightning channel injection into the froxel light volume.
// The pass lights participating media along a deterministic 8-point channel
// instead of treating nebula lightning as one spherical flash.

Texture3D<float4> Density : register(t0, TEXTURE_SPACE);
SamplerState DensitySampler : register(s0, TEXTURE_SPACE);

[[vk::image_format("rgba16f")]]
RWTexture3D<float4> Lighting : register(u1, TEXTURE_SPACE);

cbuffer FroxelLightningParams : register(b3, UNIFORM_SPACE)
{
    float4 GridSize; // xyz dimensions, w time
    float4 Point0;
    float4 Point1;
    float4 Point2;
    float4 Point3;
    float4 Point4;
    float4 Point5;
    float4 Point6;
    float4 Point7;
    float4 Params;  // x enabled, y point count, z intensity, w radius in normalized volume space
    float4 Color;   // rgb channel colour, a unused
    float4 Params2; // x near-grid, y afterglow duration, z debug color mode, w reserved
};

float3 PointAt(int index)
{
    if (index == 0) return Point0.xyz;
    if (index == 1) return Point1.xyz;
    if (index == 2) return Point2.xyz;
    if (index == 3) return Point3.xyz;
    if (index == 4) return Point4.xyz;
    if (index == 5) return Point5.xyz;
    if (index == 6) return Point6.xyz;
    return Point7.xyz;
}

float DistanceSqToSegment(float3 p, float3 a, float3 b)
{
    float3 ab = b - a;
    float denom = max(dot(ab, ab), 1e-5);
    float t = saturate(dot(p - a, ab) / denom);
    float3 q = a + ab * t;
    float3 d = p - q;
    return dot(d, d);
}

float ChannelMask(float3 p, int pointCount, float radius)
{
    float r2 = max(radius * radius, 1e-5);
    float accum = 0.0;
    [unroll]
    for (int i = 0; i < 7; i++)
    {
        if (i + 1 >= pointCount)
            break;
        float d2 = DistanceSqToSegment(p, PointAt(i), PointAt(i + 1));
        accum = max(accum, exp(-d2 / r2));
    }
    return saturate(accum);
}

[numthreads(4, 4, 4)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (any(id >= (uint3)GridSize.xyz))
        return;
    if (Params.x <= 0.5 || Params.y < 2.0 || Params.z <= 0.0)
        return;

    float3 dims = max(GridSize.xyz, 1.0);
    float3 uvw = (float3(id) + 0.5) / dims;
    float3 p = uvw * 2.0 - 1.0;

    float4 density = Density.SampleLevel(DensitySampler, uvw, 0);
    float medium = saturate(density.x * 0.85 + density.z * 0.15);
    float mask = ChannelMask(p, (int)Params.y, Params.w);

    float contribution = mask * Params.z * (0.18 + medium * 0.82);
    contribution *= Params2.x > 0.5 ? 0.82 : 1.0;

    float4 current = Lighting[id];
    current.rgb += Color.rgb * contribution;
    current.a = max(current.a, contribution);
    Lighting[id] = current;
}
