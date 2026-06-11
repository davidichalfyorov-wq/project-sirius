#include "includes/ColorSpace.hlsl"

TextureCube<float4> BasicStars: register(t0, TEXTURE_SPACE);
SamplerState BasicSampler: register(s0, TEXTURE_SPACE);
TextureCube<float4> ComplexStars: register(t1, TEXTURE_SPACE);
SamplerState ComplexSampler: register(s1, TEXTURE_SPACE);
TextureCube<float4> Nebulae: register(t2, TEXTURE_SPACE);
SamplerState NebulaSampler: register(s2, TEXTURE_SPACE);

cbuffer Parameters: register(b3, UNIFORM_SPACE)
{
    float4x4 InverseViewProjection;
    float4 ViewportTime;
    float4 LayerMask;
    float4 BasicRotation;
    float4 ComplexRotation;
};

struct Input
{
    float4 position: SV_Position;
    float2 texCoord: TEXCOORD0;
};

float3 RotateY(float3 dir, float2 sc)
{
    return float3(
        dir.x * sc.y + dir.z * sc.x,
        dir.y,
        -dir.x * sc.x + dir.z * sc.y);
}

float3 RotateZ(float3 dir, float2 sc)
{
    return float3(
        dir.x * sc.y - dir.y * sc.x,
        dir.x * sc.x + dir.y * sc.y,
        dir.z);
}

float4 Over(float4 src, float4 dst)
{
    return src + dst * (1.0 - src.a);
}

float4 main(Input input) : SV_Target0
{
    // Reconstruct NDC from the fullscreen-triangle texCoord, never from
    // SV_Position: fragment coordinates change origin between backends
    // (GL bottom-up, Vulkan top-down), which mirrored the sky vertically
    // on one of them and made it track camera pitch. texCoord is tied to
    // the triangle's NDC corners, so it is backend-invariant and also
    // correct inside letterboxed (offset) viewports.
    float2 ndc = input.texCoord * 2.0 - 1.0;
    float4 nearPoint = mul(float4(ndc, 0.0, 1.0), InverseViewProjection);
    float4 farPoint = mul(float4(ndc, 1.0, 1.0), InverseViewProjection);
    nearPoint.xyz /= nearPoint.w;
    farPoint.xyz /= farPoint.w;
    float3 dir = normalize(farPoint.xyz - nearPoint.xyz);

    float4 color = float4(0, 0, 0, 0);
    if (LayerMask.x > 0.5)
    {
        color = Over(BasicStars.Sample(BasicSampler, RotateY(dir, BasicRotation.xy)), color);
    }
    if (LayerMask.y > 0.5)
    {
        color = Over(ComplexStars.Sample(ComplexSampler, RotateZ(dir, ComplexRotation.xy)), color);
    }
    if (LayerMask.z > 0.5)
    {
        color = Over(Nebulae.Sample(NebulaSampler, dir), color);
    }
    // Cubemaps are captured from display-referred art: composite as
    // authored, decode the result once into the linear scene.
    return float4(SrgbToLinear(color.rgb), color.a);
}
