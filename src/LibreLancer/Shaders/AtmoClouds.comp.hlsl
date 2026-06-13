// Atmosphere cloud shell (Phase 5 / W4): half-res 2.5D march through a
// thin spherical layer inside the atmosphere shell. The expensive density
// work only runs for rays intersecting [cloudInner, cloudOuter].

Texture3D<float4> NoiseBase : register(t0, TEXTURE_SPACE);
SamplerState NoiseBaseSampler : register(s0, TEXTURE_SPACE);
Texture3D<float4> NoiseDetail : register(t1, TEXTURE_SPACE);
SamplerState NoiseDetailSampler : register(s1, TEXTURE_SPACE);
Texture2D<float4> History : register(t6, TEXTURE_SPACE);
SamplerState HistorySampler : register(s6, TEXTURE_SPACE);

[[vk::image_format("rgba16f")]]
RWTexture2D<float4> Result : register(u4, TEXTURE_SPACE); // rgb non-premultiplied, a opacity

cbuffer AtmoCloudParams : register(b3, UNIFORM_SPACE)
{
    float4x4 InvViewProj;
    float4x4 PrevViewProj;
    float4 CameraPosPlanetRadius; // xyz camera, w planet radius
    float4 PlanetCenterShell;     // xyz planet center, w atmosphere shell radius
    float4 TargetSizeLayer;       // xy output size, z frame, w step count
    float4 SunDirBlend;           // xyz to-sun, w history blend
    float4 SunColorTime;          // rgb sun colour/intensity, w drift time
    float4 CloudShape;            // x inner radius, y outer radius, z density scale, w coverage threshold
};

static const float ATMO_CLOUD_PI = 3.14159265;

float2 RaySphere(float3 ro, float3 rd, float3 c, float r)
{
    float3 oc = ro - c;
    float b = dot(oc, rd);
    float disc = b * b - (dot(oc, oc) - r * r);
    if (disc < 0.0)
    {
        return float2(1e9, -1e9);
    }
    float s = sqrt(disc);
    return float2(-b - s, -b + s);
}

float Hg(float cosTheta, float g)
{
    float g2 = g * g;
    float denom = max(pow(abs(1.0 + g2 - 2.0 * g * cosTheta), 1.5), 1e-3);
    return (1.0 - g2) / (4.0 * ATMO_CLOUD_PI * denom);
}

float InterleavedGradientNoise(uint2 pixel, uint frame)
{
    float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
    return frac(magic.z * frac(dot(float2(pixel) + frame * 17.0, magic.xy)));
}

float CloudDensity(float3 worldPos)
{
    float3 local = worldPos - PlanetCenterShell.xyz;
    float r = length(local);
    float layer = max(CloudShape.y - CloudShape.x, 1.0);
    float h = saturate((r - CloudShape.x) / layer);
    float heightBell = smoothstep(0.02, 0.34, h) * (1.0 - smoothstep(0.74, 1.0, h));
    if (heightBell <= 1e-4)
    {
        return 0.0;
    }

    float3 n = local / max(r, 1e-3);
    float t = SunColorTime.w;
    float3 drift = float3(t * 0.00026, t * 0.00007, -t * 0.00019);
    float3 uvw = frac(n * 0.34 + 0.5 + drift + h * float3(0.11, 0.17, 0.07));
    float4 base = NoiseBase.SampleLevel(NoiseBaseSampler, uvw, 0);

    float macroBreakup = saturate(base.g * 0.45 + base.b * 0.35 + base.a * 0.20);
    float shapeSignal = saturate(base.r * 1.60 + macroBreakup * 0.45);
    float coverageThreshold = abs(CloudShape.w);
    float coverage;
    if (CloudShape.w < 0.0)
    {
        coverage = 1.0;
    }
    else
    {
        coverage = saturate((shapeSignal - coverageThreshold) / max(1.0 - coverageThreshold, 0.05));
        coverage = smoothstep(0.02, 0.60, coverage);
        coverage = max(coverage, 0.20 + macroBreakup * 0.12);
    }

    float3 detailUv = frac(uvw * 4.2 + float3(-t * 0.0011, t * 0.0006, t * 0.0009));
    float3 detail = NoiseDetail.SampleLevel(NoiseDetailSampler, detailUv, 0).rgb;
    float erosion = dot(detail, float3(0.50, 0.32, 0.18));

    float density = coverage * heightBell;
    density *= lerp(0.55, 1.25, macroBreakup);
    density = saturate(density - erosion * 0.28 * density);
    return density;
}

float SunOpticalDepth(float3 worldPos)
{
    float layer = max(CloudShape.y - CloudShape.x, 1.0);
    float2 tOuter = RaySphere(worldPos, SunDirBlend.xyz, PlanetCenterShell.xyz, CloudShape.y);
    if (tOuter.y <= 0.0)
    {
        return 0.0;
    }

    float tau = 0.0;
    float prev = 0.0;
    float taps[2] = { layer * 0.55, layer * 1.45 };
    [unroll]
    for (uint i = 0; i < 2; i++)
    {
        float dist = min(taps[i], max(tOuter.y, 0.0));
        if (dist <= prev)
        {
            continue;
        }
        float3 p = worldPos + SunDirBlend.xyz * dist;
        tau += CloudDensity(p) * CloudShape.z * (dist - prev);
        prev = dist;
    }
    return tau;
}

[numthreads(8, 8, 1)]
void main(uint2 id : SV_DispatchThreadID)
{
    uint2 target = (uint2)TargetSizeLayer.xy;
    if (any(id >= target))
    {
        return;
    }

    float2 uv = (id + 0.5) / TargetSizeLayer.xy;
    float2 ndc = uv * 2.0 - 1.0;
    float4 nearPoint = mul(float4(ndc, 0.0, 1.0), InvViewProj);
    nearPoint /= nearPoint.w;
    float4 farPoint = mul(float4(ndc, 1.0, 1.0), InvViewProj);
    farPoint /= farPoint.w;

    float3 ro = CameraPosPlanetRadius.xyz;
    float3 rd = normalize(farPoint.xyz - nearPoint.xyz);
    float3 center = PlanetCenterShell.xyz;
    float outer = CloudShape.y;
    float inner = CloudShape.x;

    float2 tOuter = RaySphere(ro, rd, center, outer);
    if (tOuter.y <= tOuter.x)
    {
        Result[id] = 0.0.xxxx;
        return;
    }

    float t0 = max(tOuter.x, 0.0);
    float t1 = tOuter.y;
    float cameraRadius = length(ro - center);
    float2 tInner = RaySphere(ro, rd, center, inner);
    if (tInner.y > tInner.x)
    {
        if (cameraRadius < inner)
        {
            t0 = max(t0, tInner.y);
        }
        else if (tInner.x > t0)
        {
            t1 = min(t1, tInner.x);
        }
    }
    if (t1 <= t0)
    {
        Result[id] = 0.0.xxxx;
        return;
    }

    uint steps = max(1u, (uint)TargetSizeLayer.w);
    float dt = (t1 - t0) / steps;
    float jitter = InterleavedGradientNoise(id, (uint)TargetSizeLayer.z);
    float cosTheta = dot(rd, SunDirBlend.xyz);
    float phase = Hg(cosTheta, 0.62) * 0.78 + Hg(cosTheta, -0.22) * 0.22;

    float3 colour = 0.0.xxx;
    float transmittance = 1.0;
    float weightedT = 0.0;
    float weightSum = 0.0;
    [loop]
    for (uint s = 0; s < steps; s++)
    {
        float t = t0 + (s + jitter) * dt;
        float3 pos = ro + rd * t;
        float density = CloudDensity(pos);
        if (density <= 1e-5)
        {
            continue;
        }

        float sigma = density * CloudShape.z;
        float segOpacity = 1.0 - exp(-sigma * dt);
        float sunTau = SunOpticalDepth(pos);
        float sunVis = exp(-sunTau);
        float powder = 1.0 - exp(-density * 3.0);
        float3 cloudAlbedo = float3(0.74, 0.78, 0.84);
        float3 light = cloudAlbedo * (0.035.xxx +
            SunColorTime.rgb * (sunVis * phase * 2.8 + powder * 0.11));

        float weight = transmittance * segOpacity;
        colour += light * weight;
        weightedT += t * weight;
        weightSum += weight;
        transmittance *= 1.0 - segOpacity;
        if (transmittance < 0.02)
        {
            break;
        }
    }

    float alpha = saturate(1.0 - transmittance);
    float3 outColour = colour / (1.0.xxx + colour);
    float4 current = float4(outColour, alpha);

    if (SunDirBlend.w > 0.0 && weightSum > 1e-5)
    {
        float3 anchor = ro + rd * (weightedT / weightSum);
        float4 prevClip = mul(float4(anchor, 1.0), PrevViewProj);
        if (prevClip.w > 0.0)
        {
            float2 prevUv = (prevClip.xy / prevClip.w) * 0.5 + 0.5;
            if (all(prevUv >= 0.0) && all(prevUv <= 1.0))
            {
                float4 history = History.SampleLevel(HistorySampler, prevUv, 0);
                history.rgb = min(history.rgb, current.rgb * 3.0 + 0.02);
                history.a = min(history.a, current.a * 2.0 + 0.02);
                current = lerp(current, history, SunDirBlend.w);
            }
        }
    }

    Result[id] = current;
}
