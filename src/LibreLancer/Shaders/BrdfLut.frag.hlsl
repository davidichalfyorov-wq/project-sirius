struct Input
{
    float2 texCoord : TEXCOORD0;
};

// Split-sum BRDF integration LUT (Karis, roadmap 5.3): x = NdotV,
// y = roughness; output (scale, bias) for the specular IBL term.
static const uint SAMPLE_COUNT = 1024u;
static const float PI = 3.14159265359;

float RadicalInverse_VdC(uint bits)
{
    bits = (bits << 16u) | (bits >> 16u);
    bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
    bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
    bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
    bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
    return float(bits) * 2.3283064365386963e-10;
}

float2 Hammersley(uint i, uint count)
{
    return float2(float(i) / float(count), RadicalInverse_VdC(i));
}

float3 ImportanceSampleGGX(float2 xi, float3 n, float roughness)
{
    float a = roughness * roughness;
    float phi = 2.0 * PI * xi.x;
    float cosTheta = sqrt((1.0 - xi.y) / (1.0 + (a * a - 1.0) * xi.y));
    float sinTheta = sqrt(1.0 - cosTheta * cosTheta);
    float3 h = float3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);
    float3 up = abs(n.z) < 0.999 ? float3(0, 0, 1) : float3(1, 0, 0);
    float3 tangent = normalize(cross(up, n));
    float3 bitangent = cross(n, tangent);
    return normalize(tangent * h.x + bitangent * h.y + n * h.z);
}

float GeometrySchlickGGX(float NdotV, float roughness)
{
    // IBL k remap (Karis): k = a^2 / 2
    float a = roughness;
    float k = (a * a) / 2.0;
    return NdotV / (NdotV * (1.0 - k) + k);
}

float GeometrySmith(float NdotV, float NdotL, float roughness)
{
    return GeometrySchlickGGX(NdotV, roughness) * GeometrySchlickGGX(NdotL, roughness);
}

float4 main(Input input) : SV_Target0
{
    float NdotV = max(input.texCoord.x, 1e-3);
    float roughness = input.texCoord.y;
    float3 v = float3(sqrt(1.0 - NdotV * NdotV), 0.0, NdotV);
    float3 n = float3(0, 0, 1);

    float scale = 0.0;
    float bias = 0.0;
    [loop]
    for (uint i = 0u; i < SAMPLE_COUNT; i++)
    {
        float2 xi = Hammersley(i, SAMPLE_COUNT);
        float3 h = ImportanceSampleGGX(xi, n, roughness);
        float3 l = normalize(2.0 * dot(v, h) * h - v);
        float NdotL = max(l.z, 0.0);
        if (NdotL > 0.0)
        {
            float NdotH = max(h.z, 0.0);
            float VdotH = max(dot(v, h), 0.0);
            float g = GeometrySmith(NdotV, NdotL, roughness);
            float gVis = (g * VdotH) / (NdotH * NdotV);
            float fc = pow(1.0 - VdotH, 5.0);
            scale += (1.0 - fc) * gVis;
            bias += fc * gVis;
        }
    }
    return float4(scale / SAMPLE_COUNT, bias / SAMPLE_COUNT, 0.0, 1.0);
}
