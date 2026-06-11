#define MAX_LIGHTS 9

struct Light
{
    //1x float4
    float3 Position;
    float Type;
    //2x float4
    float3 Diffuse;
    float Range;
    //3x float4
    float3 Ambient;
    float _pad1;
    //4x float4
    float3 Attenuation;
    float _pad2;
    //5x float4
    float3 Direction;
    float _pad3;
    //6x float4
    float Spotlight;
    float Falloff;
    float Theta;
    float Phi;
};

cbuffer Lighting : register(b2, UNIFORM_SPACE)
{
    //[0]
    float2 FogRange;
    float UseLighting;
    float FogMode; //1x float4
    //[1]
    float3 AmbientColor;
    float LightCount; //2x float4
    //[2]
    float3 FogColor;
    float _Padding; //3x float4
    //[3]
    Light Lights[MAX_LIGHTS];
}

#ifdef PIXEL_SHADOWS
// Cascaded shadow maps (roadmap 5.4): linear light depth RGB-packed
// into a colour atlas of 3 side-by-side cascade tiles (t8/s8).
// ShadowParams.x > 0 enables sampling; y = 1/atlas tiles; z = bias.
cbuffer ShadowData : register(b6, UNIFORM_SPACE)
{
    float4x4 ShadowMatrix0;
    float4x4 ShadowMatrix1;
    float4x4 ShadowMatrix2;
    float4 ShadowSplits; // xyz: cascade far view-distances
    float4 ShadowParams; // x: enabled, y: 1/cascades, z: depth bias
};

Texture2D<float4> ShadowAtlas : register(t8, TEXTURE_SPACE);
SamplerState ShadowSampler : register(s8, TEXTURE_SPACE);

// Local spotlight shadows (roadmap 5.5): 2x2 tile atlas, lights matched
// by position (w of LocalShadowPos > 0 enables the slot).
cbuffer LocalShadowData : register(b7, UNIFORM_SPACE)
{
    float4x4 LocalShadowMatrix[4];
    float4 LocalShadowPos[4]; // xyz: light position, w: enabled
    float4 LocalShadowParams; // x: count, z: depth bias
};

Texture2D<float4> LocalShadowAtlas : register(t9, TEXTURE_SPACE);
SamplerState LocalShadowSampler : register(s9, TEXTURE_SPACE);

float UnpackShadowDepth(float3 packed)
{
    return dot(packed, float3(1.0, 1.0 / 255.0, 1.0 / 65025.0));
}

float SampleLocalShadow(float3 worldPosition, float3 lightPosition)
{
    if (LocalShadowParams.x <= 0)
        return 1.0;
    [unroll]
    for (int slot = 0; slot < 4; slot++)
    {
        if (LocalShadowPos[slot].w <= 0)
            continue;
        float3 d = LocalShadowPos[slot].xyz - lightPosition;
        if (dot(d, d) > 4.0)
            continue;
        float4 lightClip = mul(float4(worldPosition, 1.0), LocalShadowMatrix[slot]);
        if (lightClip.w <= 0)
            return 1.0;
        float2 uv = lightClip.xy / lightClip.w * 0.5 + 0.5;
        if (uv.x <= 0.0 || uv.x >= 1.0 || uv.y <= 0.0 || uv.y >= 1.0)
            return 1.0;
        float reference = saturate(lightClip.w * LocalShadowPos[slot].w) - LocalShadowParams.z;
        float2 atlasUv = (uv + float2(slot & 1, slot >> 1)) * 0.5;
        float3 packed = LocalShadowAtlas.Sample(LocalShadowSampler, atlasUv).rgb;
        return UnpackShadowDepth(packed) >= reference ? 1.0 : 0.0;
    }
    return 1.0;
}

// 1 = lit, 0 = fully shadowed. viewDistance picks the cascade.
float SampleShadow(float3 worldPosition, float viewDistance)
{
    if (ShadowParams.x <= 0)
        return 1.0;
    int cascade = viewDistance < ShadowSplits.x ? 0 : viewDistance < ShadowSplits.y ? 1 : 2;
    if (viewDistance >= ShadowSplits.z)
        return 1.0;
    float4x4 mat = cascade == 0 ? ShadowMatrix0 : cascade == 1 ? ShadowMatrix1 : ShadowMatrix2;
    float4 lightClip = mul(float4(worldPosition, 1.0), mat); // row-vector convention
    float2 uv = lightClip.xy * 0.5 + 0.5;
    if (uv.x <= 0.0 || uv.x >= 1.0 || uv.y <= 0.0 || uv.y >= 1.0)
        return 1.0;
    // Offscreen targets keep GL orientation on both backends; the ortho
    // depth in [0, far] was written linearly by the caster pass.
    float reference = lightClip.z - ShadowParams.z;
    float tiles = ShadowParams.y;
    float2 atlasUv = float2((uv.x + cascade) * tiles, uv.y);
    float2 texel = float2(tiles / 1024.0, 1.0 / 1024.0);
    float lit = 0.0;
    [unroll]
    for (int oy = 0; oy < 2; oy++)
    {
        [unroll]
        for (int ox = 0; ox < 2; ox++)
        {
            float3 packed = ShadowAtlas.Sample(ShadowSampler,
                atlasUv + float2(ox - 0.5, oy - 0.5) * texel).rgb;
            lit += UnpackShadowDepth(packed) >= reference ? 1.0 : 0.0;
        }
    }
    return lit * 0.25;
}

#else
float SampleShadow(float3 worldPosition, float viewDistance) { return 1.0; }
float SampleLocalShadow(float3 worldPosition, float3 lightPosition) { return 1.0; }
#endif

float quadratic(float x, float3 params)
{
    return x * x * params.x + x * params.y + params.z;
}

struct VertexLightTerms
{
    float3 diffuseTermFront;
    float3 diffuseTermBack;
    float3 ambientTermFront;
    float3 ambientTermBack;
};

VertexLightTerms CalculateVertexLighting(float3 position, float3 normal)
{
    float3 diffuseFront = float3(0, 0, 0);
    float3 diffuseBack = float3(0, 0, 0);
    float3 ambientFront = float3(0, 0, 0);
    float3 ambientBack = float3(0, 0, 0);

    float3 n = normalize(normal);
    float3 nBack = -n;
    for (int i = 0; i < MAX_LIGHTS; i++)
    {
        if (i >= int(LightCount))
            break;
        float3 surfaceToLight;
        float attenuationFront;
        float attenuationBack;

        if (Lights[i].Type == 0)
        {
            surfaceToLight = normalize(-Lights[i].Direction);
            attenuationFront = attenuationBack = 1.0;
        }
        else
        {
            surfaceToLight = normalize(Lights[i].Position - position);
            float distanceToLight = length(Lights[i].Position - position);
            float3 curve = Lights[i].Attenuation;
            float atten = Lights[i].Type == 1.0
                ? 1.0 / (curve.x + curve.y * distanceToLight + curve.z * (distanceToLight * distanceToLight))
                : quadratic(distanceToLight / max(Lights[i].Range,1.), curve);
            if (Lights[i].Spotlight > 0)
            {
                float rho = dot(surfaceToLight, -Lights[i].Direction);
                float theta = Lights[i].Theta;
                float phi = Lights[i].Phi;
                float falloff = Lights[i].Falloff;
                float spotAtten = rho - phi;
                spotAtten = spotAtten / (theta - phi);
                spotAtten = pow(spotAtten, falloff);

                bool insideThetaAndPhi = rho <= theta;
                bool insidePhi = rho > phi;
                spotAtten = insidePhi ? spotAtten : 0.0;
                spotAtten = insideThetaAndPhi ? spotAtten : 1.0;
                spotAtten = clamp(spotAtten, 0.0, 1.0);

                atten *= spotAtten;
            }
            attenuationFront = attenuationBack = atten;
        }

        float diffuseCoefficient = max(dot(n, surfaceToLight), 0.0);
        float3 diffuse = diffuseCoefficient * Lights[i].Diffuse;
        diffuseFront += attenuationFront * diffuse;
        ambientFront += attenuationFront * Lights[i].Ambient;

        float diffuseBackCoeff = max(dot(nBack, surfaceToLight), 0.0);
        diffuseBack += diffuseBackCoeff * Lights[i].Diffuse;
        ambientBack += attenuationBack * Lights[i].Ambient;
    }
    VertexLightTerms result;
    result.diffuseTermFront = clamp(diffuseFront, 0.0, 1.0);
    result.diffuseTermBack = clamp(diffuseBack, 0.0, 1.0);
    result.ambientTermFront = clamp(ambientFront, 0.0, 1.0);
    result.ambientTermBack = clamp(ambientBack, 0.0, 1.0);
    return result;
}

#define FOGMODE_LINEAR 3
#define FOGMODE_EXP 1
#define FOGMODE_EXP2 2

float3 ApplyFog(float4 viewPosition, float3 objectColor)
{
    float fogFactor;
    float dist = length(viewPosition);
    if(FogMode == FOGMODE_EXP)
    {
        //FogRange - x: density
        fogFactor = 1.0 / exp(dist * FogRange.x);
    }
    else if (FogMode == FOGMODE_EXP2)
    {
        //FogRange - x: density
        fogFactor = 1.0 / exp((dist * FogRange.x) * (dist * FogRange.x));
    }
    else
    {
        //FogRange - x: near, y: far
        fogFactor = (FogRange.y - dist) / (FogRange.y - FogRange.x);
    }
    fogFactor = clamp(fogFactor, 0.0, 1.0);
    return lerp(FogColor.rgb, objectColor, fogFactor);
}

float4 ApplyVertexLighting(
    float4 ac,float4 ec, float4 dc, float4 tex,
    float4 viewPosition, float3 vertexDiffuseTerm, float3 vertexAmbientTerm)
{
    if (UseLighting <= 0)
        return dc * tex;
    float3 color = ac.rgb * (AmbientColor + vertexAmbientTerm) + vertexDiffuseTerm;
    color = clamp(color, 0.0f, 1.0f);
    float3 objectColor = (ec.rgb * tex.rgb) + (tex.rgb * dc.rgb * color);
    if (FogMode > 0)
    {
        objectColor = ApplyFog(viewPosition, objectColor);
    }
    return float4(objectColor, tex.a);
}

float4 ApplyPixelLighting(
    float4 ac, float4 ec, float4 dc, float4 tex,
    float3 position, float4 viewPosition, float3 normal,
    bool frontFacing
)
{
    if (UseLighting <= 0)
        return dc * tex;
    float3 ambientTerm = float3(0.0, 0.0, 0.0);
    float3 diffuseTerm = float3(0.0, 0.0, 0.0);
    float3 n = frontFacing ? normalize(normal) : normalize(-normal);
    for (int i = 0; i < MAX_LIGHTS; i++)
    {
        if (i >= int(LightCount))
            break;
        float3 surfaceToLight;
        float attenuation;

        if (Lights[i].Type == 0)
        {
            surfaceToLight = normalize(-Lights[i].Direction);
            attenuation = SampleShadow(position, length(viewPosition.xyz));
        }
        else
        {
            surfaceToLight = normalize(Lights[i].Position - position);
            float distanceToLight = length(Lights[i].Position - position);
            float3 curve = Lights[i].Attenuation;
            attenuation = Lights[i].Type == 1.0
                ? 1.0 / (curve.x + curve.y * distanceToLight + curve.z * (distanceToLight * distanceToLight))
                : quadratic(distanceToLight / max(Lights[i].Range,1.), curve);
            if (Lights[i].Spotlight > 0)
            {
                attenuation *= SampleLocalShadow(position, Lights[i].Position);
                float rho = dot(surfaceToLight, -Lights[i].Direction);
                float spotlightFactor = pow(clamp((rho - Lights[i].Phi) / (Lights[i].Theta - Lights[i].Phi),0.,1.), Lights[i].Falloff);
                float NdotL = max(dot(n, Lights[i].Direction), 0.0);
                if (NdotL > 0.0)
                {
                    attenuation *= spotlightFactor;
                }
                else
                {
                    attenuation = 0.0;
                }
            }
        }
        float diffuseCoefficient = max(dot(n, surfaceToLight), 0.0);
        float3 diffuse = diffuseCoefficient  * Lights[i].Diffuse;
        diffuseTerm += attenuation * diffuse;
        ambientTerm += attenuation * Lights[i].Ambient;
    }
    float3 color = clamp(diffuseTerm + ac.rgb * (AmbientColor + ambientTerm), 0.0f, 1.0f);
    float3 objectColor = (ec.rgb * tex.rgb) + (tex.rgb * dc.rgb * color);
    if (FogMode > 0)
    {
        objectColor = ApplyFog(viewPosition, objectColor);
    }
    return float4(objectColor, tex.a);
}
