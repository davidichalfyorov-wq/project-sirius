// Atmosphere 2.0: physically-inspired LUT-driven scattering shell.
// The FL art shell (Scale x planet sphere) is kept as geometry, but the
// visible Vulkan path now samples the W1 transmittance / multi-scattering
// LUTs, so the old 8-step inline march is only a no-compute fallback. The
// legacy 1D ramp texture still tints the result so each planet keeps its
// hand-authored palette.

#include "includes/ColorSpace.hlsl"

#define LL_ENABLE_MATERIAL_FOG 1
#include "includes/Lighting.hlsl"

#include "includes/Camera.hlsl"

Texture2D<float4> DtTexture : register(t0, TEXTURE_SPACE);
SamplerState DtSampler : register(s0, TEXTURE_SPACE);

Texture2D<float4> AtmoTransmittance : register(t11, TEXTURE_SPACE);
SamplerState AtmoTransmittanceSampler : register(s11, TEXTURE_SPACE);
Texture2D<float4> AtmoMultiScattering : register(t12, TEXTURE_SPACE);
SamplerState AtmoMultiScatteringSampler : register(s12, TEXTURE_SPACE);


struct Input
{
    float2 texCoord: TEXCOORD0;
    float3 N : TEXCOORD1;
    float3 V : TEXCOORD2;
    float3 worldPosition: TEXCOORD3;
    float4 viewPosition: TEXCOORD4;
    float3 normal: TEXCOORD5;
#ifdef VERTEX_LIGHTING
    float3 diffuseTermFront: TEXCOORD6;
    float3 diffuseTermBack: TEXCOORD7;
    float3 ambientTermFront: TEXCOORD8;
    float3 ambientTermBack: TEXCOORD9;
#endif
    bool frontFacing : SV_IsFrontFace;
};

cbuffer AtmosphereParameters : register(b3, UNIFORM_SPACE)
{
    float4 Dc;
    float4 Ac;
    float Oc;
    float Fade;
    float ShellScale;
    float _atmoPad0;
    float4 PlanetCenter; // xyz = world center of the planet/shell
    float4 LutParams;    // x > 0 when transmittance/MS LUTs are bound
};

static const float ATMO_PI = 3.14159265;

// Returns (near, far) ray-sphere intersections, (1e9, -1e9) on miss.
float2 raySphere(float3 ro, float3 rd, float3 c, float r)
{
    float3 oc = ro - c;
    float b = dot(oc, rd);
    float disc = b * b - (dot(oc, oc) - r * r);
    if (disc < 0.0)
    {
        return float2(1e9, -1e9);
    }
    float s = sqrt(disc);
    return float2(-b - s, -b + s);
}

// Henyey-Greenstein phase for the forward Mie lobe (sun glow).
float miePhase(float cosTheta, float g)
{
    float g2 = g * g;
    float denom = pow(abs(1.0 + g2 - 2.0 * g * cosTheta), 1.5);
    return (3.0 * (1.0 - g2) / (8.0 * ATMO_PI)) * (1.0 + cosTheta * cosTheta) / ((2.0 + g2) * denom);
}

float3 selectSunDirection(float3 center)
{
    float3 sunDir = float3(0.0, 1.0, 0.0);
    for (int li = 0; li < MAX_LIGHTS; li++)
    {
        if (li >= int(LightCount)) break;
        if (Lights[li].Type == 0)
        {
            sunDir = -normalize(Lights[li].Direction);
            if (Lights[li].CastsShadow > 0.0) break;
        }
        else if (Lights[li].CastsShadow > 0.0)
        {
            sunDir = normalize(Lights[li].Position - center);
            break;
        }
    }
    return sunDir;
}

float4 main(Input input) : SV_Target0
{
    float3 ro = CameraPosition;
    float3 rd = normalize(input.worldPosition - ro);
    float3 c = PlanetCenter.xyz;

    // The fragment sits exactly on the shell, so the shell radius (and
    // from it the planet radius) comes for free - no loader plumbing.
    float shellRadius = length(input.worldPosition - c);
    float planetRadius = shellRadius / max(ShellScale, 1.0001);
    float thickness = shellRadius - planetRadius;
    // Game planets are ~1000x smaller than Earth, so the limb-chord to
    // nadir ratio is geometrically weak; a compressed scale height keeps
    // the rim thin and hugging the surface.
    float scaleHeight = max(thickness * 0.12, 1.0);

    float2 tShell = raySphere(ro, rd, c, shellRadius);
    float t0 = max(tShell.x, 0.0); // camera inside -> start at the eye
    float t1 = tShell.y;
    float2 tPlanet = raySphere(ro, rd, c, planetRadius);
    if (tPlanet.x > 0.0)
    {
        t1 = min(t1, tPlanet.x); // ground occludes the far half
    }
    if (t1 <= t0)
    {
        discard;
    }

    float3 sunDir = selectSunDirection(c);

    // Beer-Lambert single scattering. TAU_ZENITH is the optical depth of
    // a vertical ray (Earth-like haze: a nadir view stays transparent,
    // the limb chord saturates into a bright rim).
    const float TAU_ZENITH = 0.12;
    float opticalScale = scaleHeight / TAU_ZENITH;
    float fadeScale = Fade > 0.0 ? Fade : 1.0;

    if (LutParams.x <= 0.5)
    {
        const int STEPS = 8;
        float dt = (t1 - t0) / float(STEPS);
        float tau = 0.0;
        float inscatter = 0.0;
        float heightAcc = 0.0;
        float weightAcc = 0.0;
        for (int i = 0; i < STEPS; i++)
        {
            float3 p = ro + rd * (t0 + (float(i) + 0.5) * dt);
            float h = length(p - c) - planetRadius;
            float density = exp(-max(h, 0.0) / scaleHeight);
            float ndl = dot(normalize(p - c), sunDir);
            float day = smoothstep(-0.12, 0.3, ndl);
            float segTau = density * dt / opticalScale * fadeScale;
            float transToHere = exp(-(tau + segTau * 0.5));
            inscatter += density * day * (dt / opticalScale) * fadeScale * transToHere;
            tau += segTau;
            heightAcc += h * density;
            weightAcc += density;
        }
        float avgHeight = weightAcc > 0.0 ? heightAcc / weightAcc : thickness;

        float cosTheta = dot(rd, sunDir);
        float rayleighPhase = 0.0596831 * (1.0 + cosTheta * cosTheta); // 3/(16pi)
        float mie = miePhase(cosTheta, 0.76);

        float hNorm = saturate(avgHeight / max(thickness, 1.0));
        float4 ramp = SampleColorTexture(DtTexture, DtSampler, float2(saturate(1.0 - hNorm), 0.0));
        float3 tint = Dc.rgb * ramp.rgb;

        const float SUN_INTENSITY = 24.0;
        float3 rayleighTerm = tint * rayleighPhase;
        float3 mieTerm = float3(1.0, 0.95, 0.88) * mie * 0.25;
        float3 color = (rayleighTerm + mieTerm) * inscatter * SUN_INTENSITY;
        color += Ac.rgb * tint * 0.015 * (1.0 - exp(-tau));

        float alpha = saturate(1.0 - exp(-tau)) * Oc;
        color = color / (1.0.xxx + color);
        color *= saturate(alpha);
        return float4(color, alpha);
    }

    float pathLength = max(t1 - t0, 0.0);
    float midT = t0 + pathLength * 0.5;
    float3 midPoint = ro + rd * midT;
    float3 up = normalize(midPoint - c);
    float midHeight = length(midPoint - c) - planetRadius;
    float hNorm = saturate(midHeight / max(thickness, 1.0));
    float viewZenith = dot(rd, up);
    float horizon = sqrt(saturate(1.0 - viewZenith * viewZenith));
    float cameraHeight = length(ro - c) - planetRadius;
    float entryFade = 1.0 - smoothstep(thickness, thickness * 1.75, cameraHeight);
    float rimGate = max(smoothstep(0.22, 0.95, horizon), entryFade * 0.04);

    float2 lutUv = float2(saturate(viewZenith * 0.5 + 0.5), hNorm);
    float3 trans = AtmoTransmittance.SampleLevel(AtmoTransmittanceSampler, lutUv, 0).rgb;
    float3 multi = AtmoMultiScattering.SampleLevel(AtmoMultiScatteringSampler, lutUv, 0).rgb;

    float densityMid = exp(-max(midHeight, 0.0) / scaleHeight);
    float limbBoost = lerp(0.18, 0.42, horizon * horizon);
    float tauAnalytic = densityMid * pathLength / opticalScale * limbBoost * fadeScale;
    float tauLut = -log(max(dot(trans, float3(0.2126, 0.7152, 0.0722)), 0.001));
    float tau = min(max(tauAnalytic, tauLut * lerp(0.12, 0.42, horizon)), 4.0);

    float cosTheta = dot(rd, sunDir);
    float rayleighPhase = 0.0596831 * (1.0 + cosTheta * cosTheta); // 3/(16pi)
    float mie = miePhase(cosTheta, 0.76);

    float4 ramp = SampleColorTexture(DtTexture, DtSampler, float2(saturate(1.0 - hNorm), 0.0));
    float3 tint = Dc.rgb * ramp.rgb;

    const float SUN_INTENSITY = 24.0;
    float ndl = dot(up, sunDir);
    float day = smoothstep(-0.12, 0.3, ndl);
    float opacity = saturate(1.0 - exp(-tau));
    float inscatter = opacity * day * (0.01 + rimGate * 0.58);
    float3 spectralLoss = max(1.0 - trans, 0.0);
    float spectralMax = max(max(spectralLoss.r, spectralLoss.g), max(spectralLoss.b, 0.001));
    float3 spectralTint = lerp(1.0.xxx, spectralLoss / spectralMax, 0.35);
    float3 rayleighTerm = tint * spectralTint * rayleighPhase * (0.9 + horizon * 0.45);
    float3 multiTerm = multi * tint * (0.025 + horizon * 0.075);
    float3 mieTerm = float3(1.0, 0.95, 0.88) * mie * (0.12 + horizon * 0.08);
    float3 color = (rayleighTerm + multiTerm + mieTerm) * inscatter * SUN_INTENSITY;
    // Keep a faint authored lift through the terminator; the LUT terms
    // provide the horizon colour while Ac preserves Discovery's night rim.
    color += Ac.rgb * tint * (0.006 + horizon * 0.015) * opacity;
    color = min(color, 6.0.xxx);

    float alpha = opacity * Oc * (0.004 + rimGate * 0.22);
    color = color / (1.0.xxx + color);
    color *= saturate(alpha);
    return float4(color, alpha);
}
