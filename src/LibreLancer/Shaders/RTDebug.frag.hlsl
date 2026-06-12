// Ray tracing debug view (roadmap phase 4 / B5): camera ray cast against
// the scene TLAS. Mode 1 = hit distance, mode 2 = instance id colours.
// The base permutation never draws (magenta) - it exists so GL/DXIL/MSL
// translation stays legal; RT_VIEW is the vulkan-only ray query path.

struct Input
{
    float2 texCoord : TEXCOORD0;
};

cbuffer RTDebugParams : register(b3, UNIFORM_SPACE)
{
    float4x4 InverseViewProjection;
    float4 CameraPosMode; // xyz: camera position, w: mode
};

#ifdef RT_VIEW
RaytracingAccelerationStructure SceneTLAS : register(t10, TEXTURE_SPACE);
#endif

float4 main(Input input) : SV_Target0
{
#ifdef RT_VIEW
    float2 ndc = input.texCoord * 2.0 - 1.0;
    float4 farPoint = mul(float4(ndc, 1.0, 1.0), InverseViewProjection);
    farPoint /= farPoint.w;
    float3 direction = normalize(farPoint.xyz - CameraPosMode.xyz);

    RayQuery<RAY_FLAG_NONE> q;
    RayDesc ray;
    ray.Origin = CameraPosMode.xyz;
    ray.Direction = direction;
    ray.TMin = 1.0;
    ray.TMax = 300000.0;
    q.TraceRayInline(SceneTLAS, RAY_FLAG_NONE, 0xFF, ray);
    q.Proceed();

    if (q.CommittedStatus() == COMMITTED_TRIANGLE_HIT)
    {
        if (CameraPosMode.w > 2.5)
        {
            // Mode 3: 1-ray AO probe at the primary hit (matches the RTAO
            // shader's hemisphere logic, camera-driven for inspection).
            float t = q.CommittedRayT();
            float3 hitPos = CameraPosMode.xyz + direction * t;
            uint hashState = (uint(input.texCoord.x * 4096.0) * 1973u +
                uint(input.texCoord.y * 4096.0) * 9277u) | 1u;
            hashState ^= hashState >> 16; hashState *= 0x7feb352du;
            hashState ^= hashState >> 15; hashState *= 0x846ca68bu;
            hashState ^= hashState >> 16;
            float xi1 = float(hashState & 0xFFFFu) / 65535.0;
            float xi2 = float((hashState >> 16) & 0xFFFFu) / 65535.0;
            float3 up = -direction;
            float3 tangentA = normalize(abs(up.z) < 0.9
                ? cross(up, float3(0.0, 0.0, 1.0))
                : cross(up, float3(1.0, 0.0, 0.0)));
            float3 tangentB = cross(up, tangentA);
            float aoPhi = 6.2831853 * xi1;
            float cosTheta = sqrt(1.0 - xi2);
            float sinTheta = sqrt(xi2);
            float3 aoDir = tangentA * (cos(aoPhi) * sinTheta) +
                tangentB * (sin(aoPhi) * sinTheta) + up * cosTheta;
            RayQuery<RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> aoQ;
            RayDesc aoRay;
            aoRay.Origin = hitPos + up * 0.08;
            aoRay.Direction = aoDir;
            aoRay.TMin = 0.05;
            aoRay.TMax = 40.0;
            aoQ.TraceRayInline(SceneTLAS, RAY_FLAG_NONE, 0xFF, aoRay);
            while (aoQ.Proceed()) {}
            float ao = aoQ.CommittedStatus() == COMMITTED_TRIANGLE_HIT
                ? lerp(0.25, 1.0, saturate(aoQ.CommittedRayT() / 40.0))
                : 1.0;
            return float4(ao, ao, ao, 1.0);
        }
        if (CameraPosMode.w > 1.5)
        {
            uint id = q.CommittedInstanceID();
            float3 c = frac(float3(id * 0.61803, id * 0.24512, id * 0.4721) + 0.25);
            return float4(c, 1.0);
        }
        float t = q.CommittedRayT();
        float shade = saturate(1.0 - t / 60000.0);
        return float4(shade, shade * 0.8, 0.2, 1.0);
    }
    return float4(0.0, 0.0, 0.15, 1.0);
#else
    return float4(1.0, 0.0, 1.0, 1.0);
#endif
}
