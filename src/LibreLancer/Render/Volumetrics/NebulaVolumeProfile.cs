using System;
using System.Numerics;
using LibreLancer.Data.GameData.World;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Runtime-only volumetric description derived from existing Freelancer nebula/fog zones.
/// It deliberately does not add mandatory INI/MAT fields; all values have safe defaults.
/// PR-5.2 will upload this CPU profile data into froxel resources.
/// </summary>
public readonly record struct NebulaVolumeProfile(
    string Nickname,
    string SourceFile,
    string Archetype,
    ShapeKind Shape,
    Vector3 Position,
    Matrix4x4 Rotation,
    Vector3 Size,
    float EdgeFraction,
    Vector2 FogRange,
    Color4 FogColor,
    Color4 Albedo,
    Color4 Ambient,
    float CoreExtinction,
    float Coverage,
    float BaseNoiseScale,
    float DetailErosion,
    float DriftSpeed,
    float DomainWarp,
    float PhaseGForward,
    float PhaseGBackward,
    float PhaseBlend,
    float PowderFactor,
    float GodRayStrength,
    float DustMoteDensity,
    float DisplacementStrength,
    bool HasInteriorClouds,
    bool HasLightning,
    int ExclusionCount)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Nickname) && CoreExtinction > 0f && Coverage > 0f;

    public float BoundsRadius => Shape switch
    {
        ShapeKind.Box => MathF.Max(Size.X, MathF.Max(Size.Y, Size.Z)) * 0.5f,
        ShapeKind.Cylinder or ShapeKind.Ring => MathF.Max(Size.X, Size.Y * 0.5f),
        _ => MathF.Max(Size.X, MathF.Max(Size.Y, Size.Z))
    };

    public float EdgeExtinction => CoreExtinction * (EdgeFraction <= 0f ? 0.65f : 0.45f);

    public float DetailScale => BaseNoiseScale;

    public float WarpAmplitude => DomainWarp;

    public float PhaseGBack => PhaseGBackward;

    /// <summary>x: base noise 1/m, y: coverage, z: detail erosion, w: drift m/s.</summary>
    public Vector4 NoiseProfile => new(BaseNoiseScale, Coverage, DetailErosion, DriftSpeed);

    /// <summary>x/y: dual-HG g terms, z: blend weight, w: powder.</summary>
    public Vector4 PhaseProfile => new(PhaseGForward, PhaseGBackward, PhaseBlend, PowderFactor);

    /// <summary>x: god rays, y: dust motes, z: ship displacement, w: domain warp.</summary>
    public Vector4 EffectProfile => new(GodRayStrength, DustMoteDensity, DisplacementStrength, DomainWarp);

    public string DebugSummary => FormattableString.Invariant(
        $"{Nickname}: archetype={Archetype}, shape={Shape}, ext={CoreExtinction:0.000000}, coverage={Coverage:0.00}, fog={FogRange.X:0}/{FogRange.Y:0}");
}
