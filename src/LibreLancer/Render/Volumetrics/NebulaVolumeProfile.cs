using System;
using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Data.GameData.World;
using LibreLancer.Graphics;
using GameNebula = LibreLancer.Data.GameData.World.Nebula;
using ShapeKind = LibreLancer.Data.Schema.Universe.ShapeKind;

namespace LibreLancer.Render;

/// <summary>
/// Runtime-only Phase 5 profile derived from existing Freelancer/Discovery
/// nebula data. No INI/MAT format changes are required.
/// </summary>
public sealed class NebulaVolumeProfile
{
    public string Nickname { get; init; } = "";
    public string SourceFile { get; init; } = "";
    public bool Enabled { get; init; }
    public Zone? Zone { get; init; }
    public ShapeKind Shape { get; init; }
    public Vector3 Position { get; init; }
    public Vector3 Size { get; init; }
    public Matrix4x4 Rotation { get; init; } = Matrix4x4.Identity;
    public float EdgeFraction { get; init; }
    public Color4 FogColor { get; init; } = Color4.White;
    public Color4 Albedo { get; init; } = Color4.White;
    public Color4 Ambient { get; init; } = Color4.Black;
    public Vector2 LegacyFogRange { get; init; }
    public float Coverage { get; init; }
    public float CoreExtinction { get; init; }
    public float EdgeExtinction { get; init; }
    public float DetailScale { get; init; }
    public float WarpAmplitude { get; init; }
    public float PhaseGForward { get; init; } = 0.78f;
    public float PhaseGBack { get; init; } = -0.18f;
    public float PhaseBlend { get; init; } = 0.82f;
    public float PowderFactor { get; init; } = 0.45f;
    public float GodRayStrength { get; init; }
    public float DustMoteDensity { get; init; }
    public bool HasInteriorClouds { get; init; }
    public bool HasExteriorPuffs { get; init; }
    public bool HasLightning { get; init; }
    public Color4 LightningColor { get; init; } = Color4.White;
    public IReadOnlyList<NebulaVolumeExclusion> Exclusions { get; init; } = Array.Empty<NebulaVolumeExclusion>();
    public bool IsValid => Enabled && Zone != null;

    public float TransitionAt(Vector3 worldPosition)
    {
        if (Zone == null || !Zone.ContainsPoint(worldPosition)) return 0f;
        if (EdgeFraction <= 0.000001f) return 1f;
        var sd = 1f - MathHelper.Clamp(Zone.ScaledDistance(worldPosition), 0f, 1f);
        return MathHelper.Clamp(sd / EdgeFraction, 0f, 1f);
    }

    public string DebugSummary => FormattableString.Invariant(
        $"{Nickname}: shape={Shape}, ext={CoreExtinction:0.000000}, coverage={Coverage:0.00}, near/far={LegacyFogRange.X:0}/{LegacyFogRange.Y:0}");

    public static NebulaVolumeProfile FromNebula(GameNebula nebula)
    {
        var zone = nebula.Zone;
        if (zone == null)
        {
            return new NebulaVolumeProfile
            {
                Nickname = "<no-zone>",
                SourceFile = nebula.SourceFile,
                Enabled = false,
                FogColor = nebula.FogColor,
                LegacyFogRange = nebula.FogRange
            };
        }

        var fogFar = MathF.Max(nebula.FogRange.Y, 1f);
        var fogNear = MathF.Max(nebula.FogRange.X, 0f);
        var lightning = nebula.DynamicLightning || nebula.BackgroundLightning || nebula.CloudLightning;
        var authoredShapeBoost = (nebula.HasInteriorClouds ? 0.20f : 0f) + (nebula.HasExteriorBits ? 0.08f : 0f);
        var coverage = Math.Clamp((nebula.FogEnabled ? 0.48f : 0.18f) + authoredShapeBoost, 0.05f, 0.95f);
        var coreExtinction = Math.Clamp(3.0f / fogFar, 0.000015f, 0.025f);
        var fogColor = AlphaOne(nebula.FogColor);
        var ambient = AlphaOne(nebula.AmbientColor ?? Dim(nebula.FogColor, 0.35f));

        return new NebulaVolumeProfile
        {
            Nickname = zone.Nickname,
            SourceFile = nebula.SourceFile,
            Enabled = nebula.FogEnabled || nebula.HasInteriorClouds || nebula.HasExteriorBits,
            Zone = zone,
            Shape = zone.Shape,
            Position = zone.Position,
            Size = zone.Size,
            Rotation = zone.RotationMatrix,
            EdgeFraction = zone.EdgeFraction,
            FogColor = fogColor,
            Albedo = EstimateAlbedo(fogColor),
            Ambient = ambient,
            LegacyFogRange = new Vector2(fogNear, fogFar),
            Coverage = coverage,
            CoreExtinction = coreExtinction,
            EdgeExtinction = coreExtinction * (zone.EdgeFraction <= 0f ? 0.65f : 0.45f),
            DetailScale = EstimateDetailScale(nebula),
            WarpAmplitude = lightning ? 0.18f : 0.12f,
            PowderFactor = lightning ? 0.55f : 0.42f,
            GodRayStrength = Math.Clamp(nebula.SunBurnthroughIntensity > 0 ? nebula.SunBurnthroughIntensity : 0.35f, 0f, 3f),
            DustMoteDensity = EstimateDustMoteDensity(nebula),
            HasInteriorClouds = nebula.HasInteriorClouds,
            HasExteriorPuffs = nebula.HasExteriorBits,
            HasLightning = lightning,
            LightningColor = AlphaOne(PickLightningColor(nebula)),
            Exclusions = MapExclusions(nebula.ExclusionZones)
        };
    }

    private static IReadOnlyList<NebulaVolumeExclusion> MapExclusions(List<NebulaExclusionZone>? zones)
    {
        if (zones == null || zones.Count == 0) return Array.Empty<NebulaVolumeExclusion>();
        var result = new NebulaVolumeExclusion[zones.Count];
        for (var i = 0; i < zones.Count; i++)
        {
            var ex = zones[i];
            result[i] = new NebulaVolumeExclusion(ex.Zone?.Nickname ?? $"exclusion_{i}", ex.Zone, ex.FogFar, ex.ShellTint, ex.ShellMaxAlpha, ex.ShellScalar);
        }
        return result;
    }

    private static Color4 PickLightningColor(GameNebula nebula) =>
        nebula.DynamicLightning ? nebula.DynamicLightningColor :
        nebula.CloudLightning ? nebula.CloudLightningColor :
        nebula.BackgroundLightning ? nebula.BackgroundLightningColor : Color4.White;

    private static float EstimateDetailScale(GameNebula nebula) =>
        nebula.InteriorCloudRadius > 0 ? 1f / Math.Clamp(nebula.InteriorCloudRadius, 64, 4096) :
        nebula.ExteriorBitRadius > 0 ? 1f / Math.Clamp(nebula.ExteriorBitRadius, 128f, 8192f) : 1f / 2048f;

    private static float EstimateDustMoteDensity(GameNebula nebula)
    {
        if (!nebula.HasInteriorClouds || nebula.InteriorCloudMaxDistance <= 0) return 0.02f;
        var localDensity = nebula.InteriorCloudCount / MathF.Max(nebula.InteriorCloudMaxDistance, 1f);
        return Math.Clamp(localDensity * 8f, 0.02f, 0.45f);
    }

    private static Color4 EstimateAlbedo(Color4 fog)
    {
        var max = MathF.Max(MathF.Max(fog.R, fog.G), fog.B);
        if (max < 0.001f) return new Color4(0.55f, 0.55f, 0.55f, 1f);
        var scale = MathF.Min(1f / max, 2.0f);
        return new Color4(Math.Clamp(fog.R * scale, 0.15f, 1f), Math.Clamp(fog.G * scale, 0.15f, 1f), Math.Clamp(fog.B * scale, 0.15f, 1f), 1f);
    }

    private static Color4 Dim(Color4 color, float factor) => new(color.R * factor, color.G * factor, color.B * factor, 1f);
    private static Color4 AlphaOne(Color4 color) => new(color.R, color.G, color.B, 1f);
}

public readonly record struct NebulaVolumeExclusion(string Nickname, Zone? Zone, float FogFar, Color3f ShellTint, float ShellMaxAlpha, float ShellScalar);

public readonly record struct VolumetricNebulaFrameDebug(bool Requested, bool Active, bool LegacyFallback, string Reason, string ProfileNickname, int Quality, bool NearCascade, bool ShipDisplacement, bool AtmosphereLuts, string DebugView)
{
    public static VolumetricNebulaFrameDebug Evaluate(GameSettings settings, RenderContext rstate)
    {
        var debugView = settings.Phase5DebugView;
        if (!settings.VolumetricNebula)
            return new VolumetricNebulaFrameDebug(false, false, false, "disabled", "", settings.VolumetricQuality, false, false, settings.AtmosphereLuts, debugView);
        if (!rstate.HasFeature(GraphicsFeature.Compute))
            return new VolumetricNebulaFrameDebug(true, false, true, "backend has no compute feature", "", settings.VolumetricQuality, false, false, settings.AtmosphereLuts, debugView);
        return new VolumetricNebulaFrameDebug(true, false, true, "froxel resources are scheduled for PR-5.2", "", settings.VolumetricQuality, settings.Phase5NearCascadeEnabled, settings.Phase5ShipDisplacementEnabled, settings.AtmosphereLuts, debugView);
    }
}
