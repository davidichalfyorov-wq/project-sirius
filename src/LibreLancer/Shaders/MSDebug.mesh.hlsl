// Mesh shader toolchain smoke test (roadmap 7.5 / C1): one workgroup
// emits one full-screen-ish triangle. SIRIUS_MS_DEBUG=1 draws it as an
// overlay - if the triangle shows, the whole mesh pipeline path works.

struct MeshVertex
{
    float4 position : SV_Position;
    float3 color : COLOR0;
#ifdef BIG
    // Match the cube-field interpolant budget exactly (bisecting an
    // output-size-dependent emission failure).
    float2 texCoord1: TEXCOORD0;
    float2 texCoord2: TEXCOORD1;
    float3 worldPosition: TEXCOORD2;
    float3 normal: TEXCOORD3;
    float4 viewPosition: TEXCOORD5;
#endif
};

#ifdef BIG
// Stress twin of the cube-field emission: 64 verts / 124 literal-
// indexed triangles in one workgroup, drawn as a screen overlay.
[outputtopology("triangle")]
[numthreads(64, 1, 1)]
void main(
    uint3 groupThread : SV_GroupThreadID,
    out vertices MeshVertex verts[64],
    out indices uint3 tris[124])
{
    uint tid = groupThread.x;
    SetMeshOutputCounts(64, 124);
    // 8x8 vertex grid in NDC
    float2 cell = float2(tid % 8u, tid / 8u);
    verts[tid].position = float4(cell / 4.0 - 0.95, 0.1, 1.0);
    verts[tid].color = float3(cell / 8.0, 1.0);
    verts[tid].texCoord1 = cell / 8.0;
    verts[tid].texCoord2 = cell / 8.0;
    verts[tid].worldPosition = float3(cell, 0.0);
    verts[tid].normal = float3(0.0, 0.0, 1.0);
    verts[tid].viewPosition = float4(0.0, 0.0, 1.0, 1.0);
    if (tid == 0) { tris[0] = uint3(0, 1, 8); }
    if (tid == 1) { tris[1] = uint3(1, 9, 8); }
    if (tid == 2) { tris[2] = uint3(1, 2, 9); }
    if (tid == 3) { tris[3] = uint3(2, 10, 9); }
    if (tid == 4) { tris[4] = uint3(2, 3, 10); }
    if (tid == 5) { tris[5] = uint3(3, 11, 10); }
    if (tid == 6) { tris[6] = uint3(3, 4, 11); }
    if (tid == 7) { tris[7] = uint3(4, 12, 11); }
    if (tid == 8) { tris[8] = uint3(4, 5, 12); }
    if (tid == 9) { tris[9] = uint3(5, 13, 12); }
    if (tid == 10) { tris[10] = uint3(5, 6, 13); }
    if (tid == 11) { tris[11] = uint3(6, 14, 13); }
    if (tid == 12) { tris[12] = uint3(6, 7, 14); }
    if (tid == 13) { tris[13] = uint3(7, 15, 14); }
    if (tid == 14) { tris[14] = uint3(8, 9, 16); }
    if (tid == 15) { tris[15] = uint3(9, 17, 16); }
    if (tid == 16) { tris[16] = uint3(9, 10, 17); }
    if (tid == 17) { tris[17] = uint3(10, 18, 17); }
    if (tid == 18) { tris[18] = uint3(10, 11, 18); }
    if (tid == 19) { tris[19] = uint3(11, 19, 18); }
    if (tid == 20) { tris[20] = uint3(11, 12, 19); }
    if (tid == 21) { tris[21] = uint3(12, 20, 19); }
    if (tid == 22) { tris[22] = uint3(12, 13, 20); }
    if (tid == 23) { tris[23] = uint3(13, 21, 20); }
    if (tid == 24) { tris[24] = uint3(13, 14, 21); }
    if (tid == 25) { tris[25] = uint3(14, 22, 21); }
    if (tid == 26) { tris[26] = uint3(14, 15, 22); }
    if (tid == 27) { tris[27] = uint3(15, 23, 22); }
    if (tid == 28) { tris[28] = uint3(16, 17, 24); }
    if (tid == 29) { tris[29] = uint3(17, 25, 24); }
    if (tid == 30) { tris[30] = uint3(17, 18, 25); }
    if (tid == 31) { tris[31] = uint3(18, 26, 25); }
    if (tid == 32) { tris[32] = uint3(18, 19, 26); }
    if (tid == 33) { tris[33] = uint3(19, 27, 26); }
    if (tid == 34) { tris[34] = uint3(19, 20, 27); }
    if (tid == 35) { tris[35] = uint3(20, 28, 27); }
    if (tid == 36) { tris[36] = uint3(20, 21, 28); }
    if (tid == 37) { tris[37] = uint3(21, 29, 28); }
    if (tid == 38) { tris[38] = uint3(21, 22, 29); }
    if (tid == 39) { tris[39] = uint3(22, 30, 29); }
    if (tid == 40) { tris[40] = uint3(22, 23, 30); }
    if (tid == 41) { tris[41] = uint3(23, 31, 30); }
    if (tid == 42) { tris[42] = uint3(24, 25, 32); }
    if (tid == 43) { tris[43] = uint3(25, 33, 32); }
    if (tid == 44) { tris[44] = uint3(25, 26, 33); }
    if (tid == 45) { tris[45] = uint3(26, 34, 33); }
    if (tid == 46) { tris[46] = uint3(26, 27, 34); }
    if (tid == 47) { tris[47] = uint3(27, 35, 34); }
    if (tid == 48) { tris[48] = uint3(27, 28, 35); }
    if (tid == 49) { tris[49] = uint3(28, 36, 35); }
    if (tid == 50) { tris[50] = uint3(28, 29, 36); }
    if (tid == 51) { tris[51] = uint3(29, 37, 36); }
    if (tid == 52) { tris[52] = uint3(29, 30, 37); }
    if (tid == 53) { tris[53] = uint3(30, 38, 37); }
    if (tid == 54) { tris[54] = uint3(30, 31, 38); }
    if (tid == 55) { tris[55] = uint3(31, 39, 38); }
    if (tid == 56) { tris[56] = uint3(32, 33, 40); }
    if (tid == 57) { tris[57] = uint3(33, 41, 40); }
    if (tid == 58) { tris[58] = uint3(33, 34, 41); }
    if (tid == 59) { tris[59] = uint3(34, 42, 41); }
    if (tid == 60) { tris[60] = uint3(34, 35, 42); }
    if (tid == 61) { tris[61] = uint3(35, 43, 42); }
    if (tid == 62) { tris[62] = uint3(35, 36, 43); }
    if (tid == 63) { tris[63] = uint3(36, 44, 43); }
    if (tid == 0) { tris[64] = uint3(36, 37, 44); }
    if (tid == 1) { tris[65] = uint3(37, 45, 44); }
    if (tid == 2) { tris[66] = uint3(37, 38, 45); }
    if (tid == 3) { tris[67] = uint3(38, 46, 45); }
    if (tid == 4) { tris[68] = uint3(38, 39, 46); }
    if (tid == 5) { tris[69] = uint3(39, 47, 46); }
    if (tid == 6) { tris[70] = uint3(40, 41, 48); }
    if (tid == 7) { tris[71] = uint3(41, 49, 48); }
    if (tid == 8) { tris[72] = uint3(41, 42, 49); }
    if (tid == 9) { tris[73] = uint3(42, 50, 49); }
    if (tid == 10) { tris[74] = uint3(42, 43, 50); }
    if (tid == 11) { tris[75] = uint3(43, 51, 50); }
    if (tid == 12) { tris[76] = uint3(43, 44, 51); }
    if (tid == 13) { tris[77] = uint3(44, 52, 51); }
    if (tid == 14) { tris[78] = uint3(44, 45, 52); }
    if (tid == 15) { tris[79] = uint3(45, 53, 52); }
    if (tid == 16) { tris[80] = uint3(45, 46, 53); }
    if (tid == 17) { tris[81] = uint3(46, 54, 53); }
    if (tid == 18) { tris[82] = uint3(46, 47, 54); }
    if (tid == 19) { tris[83] = uint3(47, 55, 54); }
    if (tid == 20) { tris[84] = uint3(48, 49, 56); }
    if (tid == 21) { tris[85] = uint3(49, 57, 56); }
    if (tid == 22) { tris[86] = uint3(49, 50, 57); }
    if (tid == 23) { tris[87] = uint3(50, 58, 57); }
    if (tid == 24) { tris[88] = uint3(50, 51, 58); }
    if (tid == 25) { tris[89] = uint3(51, 59, 58); }
    if (tid == 26) { tris[90] = uint3(51, 52, 59); }
    if (tid == 27) { tris[91] = uint3(52, 60, 59); }
    if (tid == 28) { tris[92] = uint3(52, 53, 60); }
    if (tid == 29) { tris[93] = uint3(53, 61, 60); }
    if (tid == 30) { tris[94] = uint3(53, 54, 61); }
    if (tid == 31) { tris[95] = uint3(54, 62, 61); }
    if (tid == 32) { tris[96] = uint3(54, 55, 62); }
    if (tid == 33) { tris[97] = uint3(55, 63, 62); }
    if (tid == 34) { tris[98] = uint3(56, 57, 64); }
    if (tid == 35) { tris[99] = uint3(57, 65, 64); }
    if (tid == 36) { tris[100] = uint3(57, 58, 65); }
    if (tid == 37) { tris[101] = uint3(58, 66, 65); }
    if (tid == 38) { tris[102] = uint3(58, 59, 66); }
    if (tid == 39) { tris[103] = uint3(59, 67, 66); }
    if (tid == 40) { tris[104] = uint3(59, 60, 67); }
    if (tid == 41) { tris[105] = uint3(60, 68, 67); }
    if (tid == 42) { tris[106] = uint3(60, 61, 68); }
    if (tid == 43) { tris[107] = uint3(61, 69, 68); }
    if (tid == 44) { tris[108] = uint3(61, 62, 69); }
    if (tid == 45) { tris[109] = uint3(62, 70, 69); }
    if (tid == 46) { tris[110] = uint3(62, 63, 70); }
    if (tid == 47) { tris[111] = uint3(63, 71, 70); }
    if (tid == 48) { tris[112] = uint3(64, 65, 72); }
    if (tid == 49) { tris[113] = uint3(65, 73, 72); }
    if (tid == 50) { tris[114] = uint3(65, 66, 73); }
    if (tid == 51) { tris[115] = uint3(66, 74, 73); }
    if (tid == 52) { tris[116] = uint3(66, 67, 74); }
    if (tid == 53) { tris[117] = uint3(67, 75, 74); }
    if (tid == 54) { tris[118] = uint3(67, 68, 75); }
    if (tid == 55) { tris[119] = uint3(68, 76, 75); }
    if (tid == 56) { tris[120] = uint3(68, 69, 76); }
    if (tid == 57) { tris[121] = uint3(69, 77, 76); }
    if (tid == 58) { tris[122] = uint3(69, 70, 77); }
    if (tid == 59) { tris[123] = uint3(70, 78, 77); }
}
#else
[outputtopology("triangle")]
[numthreads(3, 1, 1)]
void main(
    uint threadId : SV_GroupThreadID,
    out vertices MeshVertex verts[3],
    out indices uint3 tris[1])
{
    SetMeshOutputCounts(3, 1);
    // Covers the lower-left half of the screen; distinct vertex colors
    // prove per-vertex interpolation survives the pipeline.
    float2 corner = float2((threadId << 1) & 2, threadId & 2);
    verts[threadId].position = float4(corner * 1.2 - 1.1, 0.0, 1.0);
    verts[threadId].color = float3(corner.x, corner.y, 1.0 - corner.x * 0.5);
    if (threadId == 0)
    {
        tris[0] = uint3(0, 1, 2);
    }
}
#endif
