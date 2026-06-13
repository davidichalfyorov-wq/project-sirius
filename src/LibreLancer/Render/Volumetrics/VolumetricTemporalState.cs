using System;
using System.Numerics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// CPU-side temporal state for froxel history. PR-5.8 keeps this conservative:
/// no camera projection jitter and no motion-vector reprojection yet, but the
/// renderer already has stable reset, jitter metadata, clamp and history slots.
/// </summary>
public sealed class VolumetricTemporalState
{
    private const float DefaultResetMeters = 900f;
    private const float DefaultHistoryWeight = 0.82f;
    private const float DefaultClampSigma = 1.75f;

    private bool hasHistory;
    private string lastProfile = string.Empty;
    private Vector3 previousPosition;
    private Matrix4x4 previousViewProjection = Matrix4x4.Identity;
    private int frameIndex;

    public VolumetricTemporalFrame BeginFrame(global::LibreLancer.ICamera camera, bool enabled, int quality,
        string profileNickname) =>
        BeginFrame(camera.Position, camera.ViewProjection, enabled, quality, profileNickname);

    public VolumetricTemporalFrame BeginFrame(Vector3 cameraPosition, Matrix4x4 viewProjection,
        bool enabled, int quality, string profileNickname)
    {
        var qualityClamped = Math.Clamp(quality, 0, 3);
        var resetMeters = ResetDistanceForQuality(qualityClamped);
        var moved = Vector3.Distance(cameraPosition, previousPosition);
        var reset = !enabled || !hasHistory || moved > resetMeters ||
                    !string.Equals(profileNickname, lastProfile, StringComparison.Ordinal);
        var jitter = enabled && IsLiveTemporal()
            ? JitterForFrame(frameIndex, qualityClamped)
            : Vector2.Zero;
        var previousCamera = previousPosition;
        var result = new VolumetricTemporalFrame(
            frameIndex,
            previousCamera,
            cameraPosition,
            previousViewProjection,
            viewProjection,
            jitter,
            HistoryWeightForQuality(qualityClamped),
            ClampSigmaForQuality(qualityClamped),
            DepthToleranceForQuality(qualityClamped),
            ReprojectionRejectStrengthForQuality(qualityClamped),
            reset);

        previousPosition = cameraPosition;
        previousViewProjection = viewProjection;
        lastProfile = profileNickname;
        hasHistory = enabled;
        frameIndex++;
        return result;
    }

    public void Reset()
    {
        hasHistory = false;
        lastProfile = string.Empty;
        previousViewProjection = Matrix4x4.Identity;
        previousPosition = Vector3.Zero;
        frameIndex = 0;
    }

    public static float ResetDistanceForQuality(int quality) => Math.Clamp(quality, 0, 3) switch
    {
        0 => 450f,
        1 => 650f,
        2 => DefaultResetMeters,
        _ => 1200f
    };

    public static float HistoryWeightForQuality(int quality) => Math.Clamp(quality, 0, 3) switch
    {
        0 => 0.70f,
        1 => 0.78f,
        2 => DefaultHistoryWeight,
        _ => 0.88f
    };

    public static float ClampSigmaForQuality(int quality) => Math.Clamp(quality, 0, 3) switch
    {
        0 => 1.25f,
        1 => 1.50f,
        2 => DefaultClampSigma,
        _ => 2.10f
    };

    public static float DepthToleranceForQuality(int quality) => Math.Clamp(quality, 0, 3) switch
    {
        0 => 80f,
        1 => 140f,
        2 => 220f,
        _ => 340f
    };

    public static float ReprojectionRejectStrengthForQuality(int quality) => Math.Clamp(quality, 0, 3) switch
    {
        0 => 1.25f,
        1 => 1.00f,
        2 => 0.80f,
        _ => 0.65f
    };

    public static bool IsLiveTemporal() =>
        Environment.GetEnvironmentVariable("SIRIUS_VOLFOG_LIVE") != "0" &&
        Environment.GetEnvironmentVariable("SIRIUS_GOLDEN") != "1";

    public static Vector2 JitterForFrame(int frame, int quality)
    {
        var x = frame & 3;
        var y = (frame >> 2) & 3;
        var h0 = Hash01((uint)(frame * 1664525 + quality * 1013904223));
        var h1 = Hash01((uint)(frame * 22695477 + quality * 1103515245 + 17));
        return new Vector2(
            ((x + h0) / 4f) - 0.5f,
            ((y + h1) / 4f) - 0.5f);
    }

    private static float Hash01(uint state)
    {
        state ^= state >> 16;
        state *= 0x7feb352dU;
        state ^= state >> 15;
        state *= 0x846ca68bU;
        state ^= state >> 16;
        return (state & 0xFFFFFF) / 16777215f;
    }
}

public readonly record struct VolumetricTemporalFrame(
    int FrameIndex,
    Vector3 PreviousCameraPosition,
    Vector3 CurrentCameraPosition,
    Matrix4x4 PreviousViewProjection,
    Matrix4x4 CurrentViewProjection,
    Vector2 Jitter,
    float HistoryWeight,
    float ClampSigma,
    float DepthToleranceMeters,
    float ReprojectionRejectStrength,
    bool Reset);
