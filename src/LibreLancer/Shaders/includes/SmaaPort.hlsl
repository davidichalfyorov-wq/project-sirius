// Port glue for the reference SMAA.hlsl (iryoku/smaa, see LICENSE-SMAA.txt
// next to the embedded textures). SMAA_CUSTOM_SL with engine conventions:
//  - One global linear-clamp sampler for every texture, exactly like the
//    reference GLSL path (point reads on pixel-aligned UVs are identical
//    under linear filtering).
//  - SMAA_RT_METRICS comes from the b3 per-pass parameter block.
//  - The per-pass *VS offset functions run at the top of the fragment
//    shaders instead of a custom vertex stage: the shared fullscreen
//    vertex shader (Tonemap.vert.hlsl) stays uniform-free.
// Define the pass textures BEFORE including this file; LinearSampler is
// declared here at s0.

#define SMAA_CUSTOM_SL 1

#ifndef SMAA_PRESET_LOW
#ifndef SMAA_PRESET_MEDIUM
#ifndef SMAA_PRESET_HIGH
#ifndef SMAA_PRESET_ULTRA
#define SMAA_PRESET_HIGH 1
#endif
#endif
#endif
#endif

cbuffer SmaaParameters : register(b3, UNIFORM_SPACE)
{
    float4 Metrics; // x: 1/width, y: 1/height, z: width, w: height
};

#define SMAA_RT_METRICS Metrics

SamplerState LinearSampler : register(s0, TEXTURE_SPACE);

#define SMAATexture2D(tex) Texture2D tex
#define SMAATexturePass2D(tex) tex
#define SMAASampleLevelZero(tex, coord) tex.SampleLevel(LinearSampler, coord, 0)
#define SMAASampleLevelZeroPoint(tex, coord) tex.SampleLevel(LinearSampler, coord, 0)
#define SMAASampleLevelZeroOffset(tex, coord, offset) tex.SampleLevel(LinearSampler, coord, 0, offset)
#define SMAASample(tex, coord) tex.Sample(LinearSampler, coord)
#define SMAASamplePoint(tex, coord) tex.Sample(LinearSampler, coord)
#define SMAASampleOffset(tex, coord, offset) tex.Sample(LinearSampler, coord, offset)
#define SMAA_FLATTEN [flatten]
#define SMAA_BRANCH [branch]

#include "SMAA.hlsl"
