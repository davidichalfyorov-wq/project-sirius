#ifndef LL_GBUFFER_ENCODE_INCLUDED
#define LL_GBUFFER_ENCODE_INCLUDED

// Project Sirius G-buffer encode/decode helpers (graphics phase 0.1.4).
// Encoding contract grounded in NRD (RayTracingDenoiser, master b5ef794 /
// v4.17.3) + Streamline/DLSS headers (research wf_8dec9093-b30, 2026-06-14;
// see memory gbuffer-encoding-spec).
//
//   RT0 = scene colour                         (RGBA16F, linear HDR)
//   RT1 = normal (xyz) + roughness (w)         (RGBA16F). Stored *0.5+0.5 for
//         debug-friendly Renderer2D display; downstream (NRD/DLSS) want the
//         signed [-1,1] form and decode via *2-1 in their transcode shader.
//         Roughness is LINEAR, clamped to >= LL_MIN_ROUGHNESS (never 0).
//   RT2 = linear view-space Z                  (R32F). RAW, keep the engine's
//         native NEGATIVE sign (RH CreatePerspectiveFieldOfView, near=3,
//         far=1e7 - viewZ of in-front geometry is negative). NRD supports both
//         signs; the only hard rule is finiteness - sky/no-hit must write a
//         FINITE sentinel (LL_VIEWZ_SKY), never INF/NAN.
//   RT3 = screen motion vector (xy)            (RG16F now; widen to RGBA16F +
//         .z = viewZprev-viewZ for NRD 2.5D at Ф2.4). Stored as prev-cur in
//         normalized-UV units (NRD: pixelUvPrev = pixelUv + mv). MUST be
//         non-jittered (Ф0.3) and exactly 0 for static pixels.

static const float LL_MIN_ROUGHNESS = 1.5 / 512.0; // never 0
static const float LL_VIEWZ_SKY     = -1.0e6;      // finite far sentinel

// ---- RT1: normal + roughness ----------------------------------------------
// Debug-friendly storage (unsigned). NRD/DLSS transcode does *2-1 to recover
// the signed normal; both want LINEAR roughness.
float4 EncodeNormalRoughnessDisplay(float3 n, float roughness)
{
    return float4(normalize(n) * 0.5 + 0.5, clamp(roughness, LL_MIN_ROUGHNESS, 1.0));
}

// ---- RT3: screen-space motion vector --------------------------------------
// curClip / prevClip: clip-space positions of THIS world point under the
// current and previous main-camera ViewProjection (un-jittered). Returns the
// NRD/DLSS reprojection vector (prev - cur) in normalized-UV (0;1) units.
// The engine's clip is GL-convention (NDC Y up); UV is Y down, hence the
// float2(0.5, -0.5) scale (NDC delta -> UV delta with Y flip).
float2 EncodeScreenMotion(float4 curClip, float4 prevClip)
{
    float2 curNDC  = curClip.xy  / curClip.w;
    float2 prevNDC = prevClip.xy / prevClip.w;
    return (prevNDC - curNDC) * float2(0.5, -0.5);
}

// ---- Octahedral normal packing (DEFERRED) ---------------------------------
// Cigolle et al. 2014 "A Survey of Efficient Representations for Independent
// Unit Vectors" (improved/signed octahedron). DEFERRED for step 0.1.4: on the
// RGBA16F RT1 octahedral buys nothing (fp16 ~10-11 bits/channel >> oct's
// ~8-bit precision) and only adds ALU + non-linear interpolation hazard. It
// becomes worthwhile ONLY if RT1 moves to a compact UNORM format
// (R10G10B10A2_UNORM, NRD's oct path + 2-bit materialID) for bandwidth - a
// Ф2.4/Ф4.2 decision. Helpers kept ready below.
float2 OctWrap(float2 v)
{
    return (1.0 - abs(v.yx)) * (v.xy >= 0.0 ? 1.0 : -1.0);
}
// Unit normal [-1,1] -> octahedral [0,1] (two channels).
float2 EncodeOctNormal(float3 n)
{
    n /= (abs(n.x) + abs(n.y) + abs(n.z));
    n.xy = n.z >= 0.0 ? n.xy : OctWrap(n.xy);
    return n.xy * 0.5 + 0.5;
}
// Octahedral [0,1] -> unit normal [-1,1].
float3 DecodeOctNormal(float2 f)
{
    f = f * 2.0 - 1.0;
    float3 n = float3(f.xy, 1.0 - abs(f.x) - abs(f.y));
    float t = saturate(-n.z);
    n.xy += n.xy >= 0.0 ? -t : t;
    return normalize(n);
}

#endif
