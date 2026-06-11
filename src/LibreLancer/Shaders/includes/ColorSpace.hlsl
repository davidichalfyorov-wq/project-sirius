// Phase 2 linear workflow (docs/LINEAR_AUDIT.md). Colour textures store
// sRGB bytes (no hardware sRGB formats on either backend); decode at the
// sample site, light in linear, and let the tonemap pass encode the one
// and only display conversion.

float3 SrgbToLinear(float3 c)
{
    float3 lo = c / 12.92;
    float3 hi = pow(max((c + 0.055) / 1.055, 0.0), (float3)2.4);
    return lerp(lo, hi, step((float3)0.04045, c));
}

float4 SampleColorTexture(Texture2D<float4> map, SamplerState samp, float2 uv)
{
    float4 sampled = map.Sample(samp, uv);
    return float4(SrgbToLinear(sampled.rgb), sampled.a);
}
