// Volumetric noise baker (track V3): generated once at startup into two
// tiling 3D textures - base 128^3 (R: Perlin-Worley shape, GBA: Worley
// fbm octaves) and detail 32^3 (RGB: high-frequency Worley) following the
// cloud-literature layout (Schneider). All hash-based, no input assets.

[[vk::image_format("rgba16f")]]
RWTexture3D<float4> Result : register(u4, TEXTURE_SPACE);

cbuffer NoiseGenParams : register(b3, UNIFORM_SPACE)
{
    float4 SizeMode; // xyz texture size, w: 0 = base, 1 = detail
};

// --- integer hash (PCG-ish), stable across compilers ---
uint3 Hash3(uint3 v)
{
    v = v * 1664525u + 1013904223u;
    v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
    v ^= v >> 16;
    v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
    return v;
}

float3 HashFloat3(uint3 v)
{
    return (float3)(Hash3(v) & 0x00FFFFFFu) / 16777215.0;
}

// --- tiling gradient (Perlin) noise, period P cells ---
float3 GradientDir(uint3 cell, uint period)
{
    float3 h = HashFloat3(cell % period) * 2.0 - 1.0;
    return normalize(h + 1e-4);
}

float PerlinTile(float3 p, float period)
{
    float3 pf = p * period;
    uint3 ip = (uint3)floor(pf);
    float3 fp = frac(pf);
    float3 u = fp * fp * fp * (fp * (fp * 6.0 - 15.0) + 10.0);
    uint per = (uint)period;

    float result = 0;
    [unroll]
    for (uint corner = 0; corner < 8; corner++)
    {
        uint3 offset = uint3(corner & 1, (corner >> 1) & 1, (corner >> 2) & 1);
        float3 grad = GradientDir(ip + offset, per);
        float3 delta = fp - (float3)offset;
        float dotVal = dot(grad, delta);
        float3 weight3 = lerp(1.0 - u, u, (float3)offset);
        result += dotVal * weight3.x * weight3.y * weight3.z;
    }
    return result * 0.5 + 0.5;
}

float PerlinFbm(float3 p, float period, uint octaves)
{
    float total = 0, amplitude = 0.5, frequency = 1;
    for (uint i = 0; i < octaves; i++)
    {
        // PerlinTile receives normalized p and multiplies by the lattice
        // period internally; fBm octave frequency is the octave period.
        total += amplitude * PerlinTile(p, period * frequency);
        amplitude *= 0.5;
        frequency *= 2.0;
    }
    return total;
}

// --- tiling Worley noise (inverted: 1 at cell cores) ---
float WorleyTile(float3 p, float cells)
{
    float3 pc = p * cells;
    int3 baseCell = (int3)floor(pc);
    float3 fp = frac(pc);
    float minDist = 1e9;
    [unroll]
    for (int x = -1; x <= 1; x++)
    [unroll]
    for (int y = -1; y <= 1; y++)
    [unroll]
    for (int z = -1; z <= 1; z++)
    {
        int3 neighbour = int3(x, y, z);
        uint3 wrapped = (uint3)((baseCell + neighbour + (int)cells) % (int)cells);
        float3 feature = HashFloat3(wrapped) + (float3)neighbour;
        float3 delta = feature - fp;
        minDist = min(minDist, dot(delta, delta));
    }
    return saturate(1.0 - sqrt(minDist));
}

float WorleyFbm(float3 p, float cells)
{
    return WorleyTile(p, cells) * 0.625 +
           WorleyTile(p, cells * 2.0) * 0.25 +
           WorleyTile(p, cells * 4.0) * 0.125;
}

float Remap(float value, float oldMin, float oldMax, float newMin, float newMax)
{
    return newMin + (saturate((value - oldMin) / (oldMax - oldMin))) * (newMax - newMin);
}

[numthreads(4, 4, 4)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint3 size = (uint3)SizeMode.xyz;
    if (any(id >= size))
        return;
    float3 uvw = (id + 0.5) / SizeMode.xyz;

    float4 result;
    if (SizeMode.w < 0.5)
    {
        // Base: R = Perlin-Worley shape, GBA = Worley fbm octaves.
        float perlin = PerlinFbm(uvw, 8.0, 4);
        float worley = WorleyFbm(uvw, 4.0);
        result.r = Remap(perlin, worley - 1.0, 1.0, 0.0, 1.0);
        result.g = WorleyFbm(uvw, 8.0);
        result.b = WorleyFbm(uvw, 16.0);
        result.a = WorleyFbm(uvw, 32.0);
    }
    else
    {
        // Detail: high-frequency Worley for edge erosion.
        result.r = WorleyFbm(uvw, 8.0);
        result.g = WorleyFbm(uvw, 16.0);
        result.b = WorleyFbm(uvw, 32.0);
        result.a = 1.0;
    }
    Result[id] = result;
}
