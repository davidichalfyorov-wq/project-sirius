struct Output
{
    float4 position: SV_Position;
    float2 texCoord: TEXCOORD0;
};

Output main(int vert: SV_VertexID)
{
    float2 vertices[3] = { float2(-1, -1), float2(3, -1), float2(-1, 3) };
    Output output;
    output.position = float4(vertices[vert], 0, 1);
    output.texCoord = (vertices[vert] + 1.0) * 0.5;
    return output;
}
