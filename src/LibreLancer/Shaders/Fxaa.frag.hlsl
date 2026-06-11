Texture2D<float4> InputTexture : register(t0, TEXTURE_SPACE);
SamplerState InputSampler : register(s0, TEXTURE_SPACE);

cbuffer FxaaParameters : register(b3, UNIFORM_SPACE)
{
    float4 RcpFrame; // xy: 1 / output size
};

struct Input
{
    float2 texCoord : TEXCOORD0;
};

// FXAA 3.11 quality (Timothy Lottes), compact PC preset. Runs on the
// tonemapped LDR image; UI is drawn after this pass and stays sharp.
#define FXAA_EDGE_THRESHOLD 0.166
#define FXAA_EDGE_THRESHOLD_MIN 0.0833
#define FXAA_SUBPIX 0.75

float FxaaLuma(float3 rgb)
{
    return dot(rgb, float3(0.299, 0.587, 0.114));
}

float4 main(Input input) : SV_Target0
{
    float2 posM = input.texCoord;
    float4 rgbaM = InputTexture.Sample(InputSampler, posM);
    float lumaM = FxaaLuma(rgbaM.rgb);
    float lumaS = FxaaLuma(InputTexture.Sample(InputSampler, posM + float2(0, RcpFrame.y)).rgb);
    float lumaE = FxaaLuma(InputTexture.Sample(InputSampler, posM + float2(RcpFrame.x, 0)).rgb);
    float lumaN = FxaaLuma(InputTexture.Sample(InputSampler, posM - float2(0, RcpFrame.y)).rgb);
    float lumaW = FxaaLuma(InputTexture.Sample(InputSampler, posM - float2(RcpFrame.x, 0)).rgb);

    float maxSM = max(lumaS, lumaM);
    float minSM = min(lumaS, lumaM);
    float maxESM = max(lumaE, maxSM);
    float minESM = min(lumaE, minSM);
    float maxWN = max(lumaN, lumaW);
    float minWN = min(lumaN, lumaW);
    float rangeMax = max(maxWN, maxESM);
    float rangeMin = min(minWN, minESM);
    float range = rangeMax - rangeMin;
    if (range < max(FXAA_EDGE_THRESHOLD_MIN, rangeMax * FXAA_EDGE_THRESHOLD))
    {
        return rgbaM;
    }

    float lumaNW = FxaaLuma(InputTexture.Sample(InputSampler, posM - RcpFrame.xy).rgb);
    float lumaSE = FxaaLuma(InputTexture.Sample(InputSampler, posM + RcpFrame.xy).rgb);
    float lumaNE = FxaaLuma(InputTexture.Sample(InputSampler, posM + float2(RcpFrame.x, -RcpFrame.y)).rgb);
    float lumaSW = FxaaLuma(InputTexture.Sample(InputSampler, posM + float2(-RcpFrame.x, RcpFrame.y)).rgb);

    float lumaNS = lumaN + lumaS;
    float lumaWE = lumaW + lumaE;
    float subpixRcpRange = 1.0 / range;
    float subpixNSWE = lumaNS + lumaWE;
    float edgeHorz1 = -2.0 * lumaM + lumaNS;
    float edgeVert1 = -2.0 * lumaM + lumaWE;
    float lumaNESE = lumaNE + lumaSE;
    float lumaNWNE = lumaNW + lumaNE;
    float edgeHorz2 = -2.0 * lumaE + lumaNESE;
    float edgeVert2 = -2.0 * lumaN + lumaNWNE;
    float lumaNWSW = lumaNW + lumaSW;
    float lumaSWSE = lumaSW + lumaSE;
    float edgeHorz4 = abs(edgeHorz1) * 2.0 + abs(edgeHorz2);
    float edgeVert4 = abs(edgeVert1) * 2.0 + abs(edgeVert2);
    float edgeHorz3 = -2.0 * lumaW + lumaNWSW;
    float edgeVert3 = -2.0 * lumaS + lumaSWSE;
    float edgeHorz = abs(edgeHorz3) + edgeHorz4;
    float edgeVert = abs(edgeVert3) + edgeVert4;

    bool horzSpan = edgeHorz >= edgeVert;
    float lengthSign = horzSpan ? RcpFrame.y : RcpFrame.x;
    float lumaN2 = horzSpan ? lumaN : lumaW;
    float lumaS2 = horzSpan ? lumaS : lumaE;
    float gradientN = lumaN2 - lumaM;
    float gradientS = lumaS2 - lumaM;
    bool pairN = abs(gradientN) >= abs(gradientS);
    float gradient = max(abs(gradientN), abs(gradientS));
    if (pairN)
    {
        lengthSign = -lengthSign;
    }
    float subpixA = subpixNSWE * 2.0 + lumaNWSW + lumaNESE;
    float subpixB = subpixA * (1.0 / 12.0) - lumaM;
    float subpixC = saturate(abs(subpixB) * subpixRcpRange);
    float subpixD = (-2.0 * subpixC) + 3.0;
    float subpixE = subpixC * subpixC;
    float subpixF = subpixD * subpixE;
    float subpixG = subpixF * subpixF;
    float subpixH = subpixG * FXAA_SUBPIX;

    float lumaNN = lumaN2 + lumaM;
    float lumaSS = lumaS2 + lumaM;
    float lumaMN = pairN ? lumaNN : lumaSS;
    float2 posB = posM;
    float2 offNP = horzSpan ? float2(RcpFrame.x, 0.0) : float2(0.0, RcpFrame.y);
    if (horzSpan)
    {
        posB.y += lengthSign * 0.5;
    }
    else
    {
        posB.x += lengthSign * 0.5;
    }
    float gradientScaled = gradient * (1.0 / 4.0);
    float2 posN = posB - offNP;
    float2 posP = posB + offNP;
    float lumaEndN = FxaaLuma(InputTexture.Sample(InputSampler, posN).rgb) - lumaMN * 0.5;
    float lumaEndP = FxaaLuma(InputTexture.Sample(InputSampler, posP).rgb) - lumaMN * 0.5;
    bool doneN = abs(lumaEndN) >= gradientScaled;
    bool doneP = abs(lumaEndP) >= gradientScaled;

    // Edge walk: 8 steps with growing stride covers ~24 texels each way.
    const float steps[8] = { 1.0, 1.0, 1.0, 1.5, 2.0, 2.0, 4.0, 8.0 };
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        if (doneN && doneP)
        {
            break;
        }
        if (!doneN)
        {
            posN -= offNP * steps[i];
            lumaEndN = FxaaLuma(InputTexture.Sample(InputSampler, posN).rgb) - lumaMN * 0.5;
            doneN = abs(lumaEndN) >= gradientScaled;
        }
        if (!doneP)
        {
            posP += offNP * steps[i];
            lumaEndP = FxaaLuma(InputTexture.Sample(InputSampler, posP).rgb) - lumaMN * 0.5;
            doneP = abs(lumaEndP) >= gradientScaled;
        }
    }

    float dstN = horzSpan ? (posM.x - posN.x) : (posM.y - posN.y);
    float dstP = horzSpan ? (posP.x - posM.x) : (posP.y - posM.y);
    bool directionN = dstN < dstP;
    float dst = min(dstN, dstP);
    float spanLength = dstP + dstN;
    bool goodSpanN = (lumaEndN < 0.0) != ((lumaM - lumaMN * 0.5) < 0.0);
    bool goodSpanP = (lumaEndP < 0.0) != ((lumaM - lumaMN * 0.5) < 0.0);
    bool goodSpan = directionN ? goodSpanN : goodSpanP;
    float pixelOffset = (dst * (-1.0 / spanLength)) + 0.5;
    float pixelOffsetGood = goodSpan ? pixelOffset : 0.0;
    float pixelOffsetSubpix = max(pixelOffsetGood, subpixH);

    float2 finalPos = posM;
    if (horzSpan)
    {
        finalPos.y += pixelOffsetSubpix * lengthSign;
    }
    else
    {
        finalPos.x += pixelOffsetSubpix * lengthSign;
    }
    return float4(InputTexture.Sample(InputSampler, finalPos).rgb, rgbaM.a);
}
