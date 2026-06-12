struct Input
{
    float3 color : COLOR0;
};

float4 main(Input input) : SV_Target0
{
    return float4(input.color, 0.85);
}
