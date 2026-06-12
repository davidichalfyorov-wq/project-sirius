// Asteroid cube field, mesh-shader path (roadmap 7.5 / C2).
// One workgroup = one (meshlet, cube) pair: SV_GroupID.x walks the
// drawcall's meshlets, SV_GroupID.y walks the frame's opaque cubes.
// Vertex math mirrors Basic_FVF.vert exactly (parity gate vs the classic
// per-cube submit path).
//
// Every meshlet is CPU-padded to exactly 64 vertices / 124 triangles so
// SetMeshOutputCounts takes LITERALS: with counts sourced from a buffer
// read the whole emission silently disappears (DXC 1.9 mesh codegen +
// NVIDIA 580.x, found empirically in C2 bring-up). Padding triangles are
// zero-area and cost nothing past primitive setup.

#include "includes/Camera.hlsl"

// 48-byte packed vertex: a=[pos.xyz, normal.x] b=[normal.yz, uv] c=diffuse
struct PackedCubeVertex
{
    float4 a;
    float4 b;
    float4 c;
};

// Explicit row_major: -Zpr governs cbuffer matrices but NOT structured
// buffer elements - without this the world transforms arrive transposed.
struct CubeWorld
{
    row_major float4x4 World;
};
StructuredBuffer<CubeWorld> CubeWorlds : register(t10, TEXTURE_SPACE);
// SLOT-expanded vertices: element i belongs to meshlet slot i directly
// (the CPU pre-applies the meshlet->vertex indirection). A second level
// of data-dependent addressing (vertex id loaded from a buffer, then
// used as the next load's address) collapses the whole emission at full
// primitive count - same DXC/NV codegen disease as buffer-driven counts
// and dynamic tris[] writes; one header-relative level is fine.
ByteAddressBuffer CubeVerts : register(t11, TEXTURE_SPACE);
// Header: x=vertexOffset y=vertexCount z=triangleOffset w=triangleCount
// (counts are always the padded 64/124; kept for drawcall bookkeeping)
StructuredBuffer<uint4> MeshletHeaders : register(t12, TEXTURE_SPACE);
// Global vertex ids (uint stream). ByteAddressBuffer: dynamic component
// indexing into a uint4 (buf[i>>2][i&3]) miscompiles under DXC - every
// lane read component .x, collapsing the meshlet to every 4th vertex.
ByteAddressBuffer MeshletVerts : register(t13, TEXTURE_SPACE);
// Packed triangles (i0 | i1<<8 | i2<<16), one uint each
ByteAddressBuffer MeshletTris : register(t14, TEXTURE_SPACE);

cbuffer FieldParams : register(b0, UNIFORM_SPACE)
{
    uint MeshletBase;  // first meshlet of the drawcall being dispatched
    uint MeshletCount;
    uint CubeCount;
    uint _fieldPad;
};

struct CubeInterp
{
    float2 texCoord1: TEXCOORD0;
    float2 texCoord2: TEXCOORD1;
    float3 worldPosition: TEXCOORD2;
    float3 normal: TEXCOORD3;
    float4 color: TEXCOORD4;
    float4 viewPosition: TEXCOORD5;
    float4 position : SV_Position;
};

groupshared float4 sharedA[64];
groupshared float4 sharedB[64];
groupshared float4 sharedC[64];

[outputtopology("triangle")]
[numthreads(32, 1, 1)]
void main(
    uint3 groupId : SV_GroupID,
    uint3 groupThread : SV_GroupThreadID,
    out vertices CubeInterp verts[64],
    out indices uint3 tris[16])
{
    uint tid = groupThread.x;
    uint4 header = MeshletHeaders[MeshletBase + groupId.x];
    SetMeshOutputCounts(64, 16);

    float4x4 world = CubeWorlds[groupId.y].World;


    // Two-phase emission through groupshared: buffer loads feed shared
    // memory behind a barrier, vertex writes read ONLY shared values.
    // Writing vertex outputs straight from buffer loads at full primitive
    // count makes the whole emission vanish (same DXC/NV codegen disease
    // as buffer-driven SetMeshOutputCounts and dynamic tris[] indices).
    for (uint lv = tid; lv < 64; lv += 32)
    {
        int vertexByte = (int)((header.x + lv) * 48);
        sharedA[lv] = asfloat(CubeVerts.Load4(vertexByte));
        sharedB[lv] = asfloat(CubeVerts.Load4(vertexByte + 16));
        sharedC[lv] = asfloat(CubeVerts.Load4(vertexByte + 32));
    }
    GroupMemoryBarrierWithGroupSync();
    for (uint v = tid; v < 64; v += 32)
    {
        float4 a = sharedA[v];
        float4 bv = sharedB[v];
        float3 localPos = a.xyz;
        float3 localNormal = float3(a.w, bv.x, bv.y);
        float2 uv = float2(bv.z, bv.w);

        float4 worldPos = mul(float4(localPos, 1.0), world);
        verts[v].position = mul(worldPos, ViewProjection);
        verts[v].worldPosition = worldPos.xyz;
        verts[v].viewPosition = mul(worldPos, View);
        // Cube transforms are rigid (rotation+translation): the normal
        // matrix equals the world matrix, matching the classic path.
        verts[v].normal = mul(float4(localNormal, 0.0), world).xyz;
        verts[v].texCoord1 = uv;
        verts[v].texCoord2 = uv;
        verts[v].color = sharedC[v];
    }

    // 16 triangle writes with LITERAL indices (see header note: the
    // proven-stable emission plateau on this driver/compiler combo).
    if (tid == 0) { uint p0 = MeshletTris.Load((int)((header.z + 0) * 4)); tris[0] = uint3(p0 & 0xFF, (p0 >> 8) & 0xFF, (p0 >> 16) & 0xFF); }
    if (tid == 1) { uint p1 = MeshletTris.Load((int)((header.z + 1) * 4)); tris[1] = uint3(p1 & 0xFF, (p1 >> 8) & 0xFF, (p1 >> 16) & 0xFF); }
    if (tid == 2) { uint p2 = MeshletTris.Load((int)((header.z + 2) * 4)); tris[2] = uint3(p2 & 0xFF, (p2 >> 8) & 0xFF, (p2 >> 16) & 0xFF); }
    if (tid == 3) { uint p3 = MeshletTris.Load((int)((header.z + 3) * 4)); tris[3] = uint3(p3 & 0xFF, (p3 >> 8) & 0xFF, (p3 >> 16) & 0xFF); }
    if (tid == 4) { uint p4 = MeshletTris.Load((int)((header.z + 4) * 4)); tris[4] = uint3(p4 & 0xFF, (p4 >> 8) & 0xFF, (p4 >> 16) & 0xFF); }
    if (tid == 5) { uint p5 = MeshletTris.Load((int)((header.z + 5) * 4)); tris[5] = uint3(p5 & 0xFF, (p5 >> 8) & 0xFF, (p5 >> 16) & 0xFF); }
    if (tid == 6) { uint p6 = MeshletTris.Load((int)((header.z + 6) * 4)); tris[6] = uint3(p6 & 0xFF, (p6 >> 8) & 0xFF, (p6 >> 16) & 0xFF); }
    if (tid == 7) { uint p7 = MeshletTris.Load((int)((header.z + 7) * 4)); tris[7] = uint3(p7 & 0xFF, (p7 >> 8) & 0xFF, (p7 >> 16) & 0xFF); }
    if (tid == 8) { uint p8 = MeshletTris.Load((int)((header.z + 8) * 4)); tris[8] = uint3(p8 & 0xFF, (p8 >> 8) & 0xFF, (p8 >> 16) & 0xFF); }
    if (tid == 9) { uint p9 = MeshletTris.Load((int)((header.z + 9) * 4)); tris[9] = uint3(p9 & 0xFF, (p9 >> 8) & 0xFF, (p9 >> 16) & 0xFF); }
    if (tid == 10) { uint p10 = MeshletTris.Load((int)((header.z + 10) * 4)); tris[10] = uint3(p10 & 0xFF, (p10 >> 8) & 0xFF, (p10 >> 16) & 0xFF); }
    if (tid == 11) { uint p11 = MeshletTris.Load((int)((header.z + 11) * 4)); tris[11] = uint3(p11 & 0xFF, (p11 >> 8) & 0xFF, (p11 >> 16) & 0xFF); }
    if (tid == 12) { uint p12 = MeshletTris.Load((int)((header.z + 12) * 4)); tris[12] = uint3(p12 & 0xFF, (p12 >> 8) & 0xFF, (p12 >> 16) & 0xFF); }
    if (tid == 13) { uint p13 = MeshletTris.Load((int)((header.z + 13) * 4)); tris[13] = uint3(p13 & 0xFF, (p13 >> 8) & 0xFF, (p13 >> 16) & 0xFF); }
    if (tid == 14) { uint p14 = MeshletTris.Load((int)((header.z + 14) * 4)); tris[14] = uint3(p14 & 0xFF, (p14 >> 8) & 0xFF, (p14 >> 16) & 0xFF); }
    if (tid == 15) { uint p15 = MeshletTris.Load((int)((header.z + 15) * 4)); tris[15] = uint3(p15 & 0xFF, (p15 >> 8) & 0xFF, (p15 >> 16) & 0xFF); }


}
