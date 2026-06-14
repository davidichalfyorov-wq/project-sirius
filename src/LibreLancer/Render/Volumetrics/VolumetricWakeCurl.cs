using System;
using System.Numerics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Low-cost vector-field profile for subtle wake turbulence. This is not fluid
/// simulation; it derives a tiny curl/pull field from persistent wake history.
/// </summary>
public readonly record struct VolumetricWakeCurlProfile(
    bool Enabled,
    float CurlStrength,
    float AxisWakeStrength,
    float NoiseFrequency,
    float VectorDamping,
    Vector3 Axis)
{
    public static VolumetricWakeCurlProfile Disabled => new(false, 0f, 0f, 0f, 0f, Vector3.UnitZ);

    public static VolumetricWakeCurlProfile ForQuality(int quality, bool enabled, Vector3 averageVelocity)
    {
        if (!enabled)
        {
            return Disabled;
        }
        var q = Math.Clamp(quality, 0, 3);
        var speed = averageVelocity.Length();
        var axis = speed > 1f ? Vector3.Normalize(averageVelocity) : Vector3.UnitZ;
        return q switch
        {
            0 => new VolumetricWakeCurlProfile(true, 0.08f, 0.025f, 2.0f, 0.72f, axis),
            1 => new VolumetricWakeCurlProfile(true, 0.11f, 0.035f, 2.4f, 0.76f, axis),
            3 => new VolumetricWakeCurlProfile(true, 0.19f, 0.055f, 3.8f, 0.86f, axis),
            _ => new VolumetricWakeCurlProfile(true, 0.15f, 0.045f, 3.1f, 0.82f, axis)
        };
    }

    public Vector4 ShaderParams => new(Enabled ? 1f : 0f, CurlStrength, AxisWakeStrength, NoiseFrequency);

    public Vector4 ShaderParams2 => new(Axis, VectorDamping);

    public string DebugSummary => Enabled
        ? FormattableString.Invariant(
            $"curl={CurlStrength:0.00} axis={Axis.X:0.00},{Axis.Y:0.00},{Axis.Z:0.00} damp={VectorDamping:0.00}")
        : "off";

    public static Vector3 BuildTangent(Vector3 axis, Vector3 gradient)
    {
        axis = axis.LengthSquared() < 1e-5f ? Vector3.UnitZ : Vector3.Normalize(axis);
        if (gradient.LengthSquared() < 1e-5f)
        {
            return Vector3.Zero;
        }
        gradient = Vector3.Normalize(gradient);
        var tangent = Vector3.Cross(axis, gradient);
        return tangent.LengthSquared() < 1e-5f ? Vector3.Zero : Vector3.Normalize(tangent);
    }
}
