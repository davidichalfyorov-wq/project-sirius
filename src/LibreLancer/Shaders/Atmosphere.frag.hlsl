// Atmosphere 2.0: physically-inspired single-scattering shell.
// The FL art shell (Scale x planet sphere) is kept as geometry, but the
// shading integrates Rayleigh+Mie along the view ray through an
// exponential-density atmosphere, so the glow hugs the surface instead
// of the legacy wide facing-ratio halo. With the camera inside the
// shell the same integral fills the screen with altitude-dependent haze
// (atmosphere entry). The legacy 1D ramp texture tints the result so
// each planet keeps its hand-authored palette.

#include "includes/ColorSpace.hlsl"

#include "includes/Lighting.hlsl"

#include "includes/Camera.hlsl"

Texture2D<float4> DtTexture : register(t0, TEXTURE_SPACE);
SamplerState DtSampler : register(s0, TEXTURE_SPACE);

Texture3D<float4> AtmoTransmittance : register(t11, TEXTURE_SPACE);
SamplerState AtmoTransmittanceSampler : register(s11, TEXTURE_SPACE);
Texture3D<float4> AtmoMultiScattering : register(t12, TEXTURE_SPACE);
SamplerState AtmoMultiScatteringSampler : register(s12, TEXTURE_SPACE);
Texture3D<float4> AtmoCloudShell : register(t13, TEXTURE_SPACE);
SamplerState AtmoCloudShellSampler : register(s13, TEXTURE_SPACE);


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
    float4 AtmoLutParams; // x LUT active, y cloud active, z cloud strength
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

    // Sun: prefer the shadow-casting light (FL suns are points with
    // CastsShadow set), fall back to the first directional.
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
            sunDir = normalize(Lights[li].Position - c);
            break;
        }
    }

    // Beer-Lambert single scattering. TAU_ZENITH is the optical depth of
    // a vertical ray (Earth-like haze: a nadir view stays transparent,
    // the limb chord saturates into a bright rim).
    const float TAU_ZENITH = 0.12;
    float opticalScale = scaleHeight / TAU_ZENITH;

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
        // Soft terminator: scattering dies on the night side.
        float day = smoothstep(-0.12, 0.3, ndl);
        float segTau = density * dt / opticalScale;
        // Light scattered here is attenuated by everything in front.
        float transToHere = exp(-(tau + segTau * 0.5));
        inscatter += density * day * (dt / opticalScale) * transToHere;
        tau += segTau;
        heightAcc += h * density;
        weightAcc += density;
    }
    float avgHeight = weightAcc > 0.0 ? heightAcc / weightAcc : thickness;

    float cosTheta = dot(rd, sunDir);
    float rayleighPhase = 0.0596831 * (1.0 + cosTheta * cosTheta); // 3/(16pi)
    float mie = miePhase(cosTheta, 0.76);

    // Legacy ramp, resampled by mean ray altitude: low passes read the
    // dense (bright) end, high passes the thin end.
    float hNorm = saturate(avgHeight / max(thickness, 1.0));
    float4 ramp = SampleColorTexture(DtTexture, DtSampler, float2(saturate(1.0 - hNorm), 0.0));
    float3 tint = Dc.rgb * ramp.rgb;

    const float SUN_INTENSITY = 24.0;
    float3 rayleighTerm = tint * rayleighPhase;
    float3 mieTerm = float3(1.0, 0.95, 0.88) * mie * 0.25;
    float3 color = (rayleighTerm + mieTerm) * inscatter * SUN_INTENSITY;
    // Faint ambient lift so the night limb is not pure black.
    color += Ac.rgb * tint * 0.015 * (1.0 - exp(-tau));

    float alpha = saturate(1.0 - exp(-tau)) * Oc;
    if (AtmoLutParams.x > 0.5)
    {
        float sunT = saturate(dot(normalize(input.worldPosition - c), sunDir) * 0.5 + 0.5);
        float3 lutUv = float3(sunT, hNorm, 0.5);
        float4 transLut = AtmoTransmittance.SampleLevel(AtmoTransmittanceSampler, lutUv, 0.0);
        float4 multiLut = AtmoMultiScattering.SampleLevel(AtmoMultiScatteringSampler, lutUv, 0.0);
        color = color * lerp(float3(1.0, 1.0, 1.0), transLut.rgb, 0.10);
        color += multiLut.rgb * inscatter * 0.018;
    }
    if (AtmoLutParams.y > 0.5)
    {
        float3 shellN = normalize(input.worldPosition - c);
        float3 cloudUv = float3(shellN.xy * 0.5 + 0.5, hNorm);
        float4 cloud = AtmoCloudShell.SampleLevel(AtmoCloudShellSampler, cloudUv, 0.0);
        float cloudStrength = saturate(AtmoLutParams.z);
        float cloudMask = cloud.a * cloudStrength;
        color += cloud.rgb * cloudMask * (0.35 + inscatter * 2.0);
        alpha = saturate(alpha + cloudMask * 0.08 * Oc);
    }
    return float4(color, alpha);
}
