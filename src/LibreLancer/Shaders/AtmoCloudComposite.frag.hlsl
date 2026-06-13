// Fullscreen composite for W4 atmosphere cloud shell. The compute pass
// stores non-premultiplied colour plus opacity in a half-res 2D target.

struct Input
{
    float2 texCoord : TEXCOORD0;
};

Texture2D<float4> Clouds : register(t0, TEXTURE_SPACE);
SamplerState CloudsSampler : register(s0, TEXTURE_SPACE);

cbuffer AtmoCloudCompositeParams : register(b3, UNIFORM_SPACE)
{
    float4 DebugMode; // x > 0 shows the raw cloud buffer as an opaque overlay
};

float4 main(Input input) : SV_Target0
{
    float4 cloud = Clouds.SampleLevel(CloudsSampler, input.texCoord, 0);
    cloud.rgb = saturate(cloud.rgb);
    cloud.a = saturate(cloud.a);
    if (DebugMode.x > 0.5)
    {
        return float4(cloud.rgb + cloud.a.xxx * 0.2, max(cloud.a, 0.85));
    }
    return cloud;
}
