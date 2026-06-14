using System;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// B1 atmosphere LUT allocation contract. The current AtmosphereMaterial stays
/// active until GPU LUT generation lands; this budget fixes the dimensions,
/// memory estimate and fallback reasons for the future Hillaire-style path.
/// </summary>
public readonly record struct VolumetricAtmosphereLutBudget(
    bool Enabled,
    int Quality,
    int TransmittanceWidth,
    int TransmittanceHeight,
    int MultiScatteringSize,
    int SkyViewWidth,
    int SkyViewHeight,
    int AerialWidth,
    int AerialHeight,
    int AerialDepth,
    bool CloudShell,
    long EstimatedBytes,
    string Reason)
{
    private const int Rgba16fBytes = 8;

    public static VolumetricAtmosphereLutBudget Create(
        bool requested,
        bool computeSupported,
        int quality,
        int renderWidth,
        int renderHeight,
        bool cloudShellRequested)
    {
        quality = Math.Clamp(quality, 0, 3);
        if (!requested)
        {
            return Disabled(quality, "off");
        }
        if (!computeSupported)
        {
            return Disabled(quality, "backend has no compute feature");
        }

        var transmittance = quality switch
        {
            0 => (128, 32),
            1 => (192, 48),
            3 => (384, 96),
            _ => (256, 64)
        };
        var multiSize = quality >= 3 ? 64 : 32;
        var sky = quality switch
        {
            0 => (96, 54),
            1 => (128, 72),
            3 => (256, 144),
            _ => (192, 108)
        };
        var divisor = quality switch
        {
            0 => 16,
            1 => 12,
            3 => 6,
            _ => 8
        };
        var aerialDepth = quality switch
        {
            0 => 16,
            1 => 24,
            3 => 48,
            _ => 32
        };
        var aerialWidth = Math.Max(1, (renderWidth + divisor - 1) / divisor);
        var aerialHeight = Math.Max(1, (renderHeight + divisor - 1) / divisor);
        var cloudShell = cloudShellRequested && quality >= 2;
        var bytes =
            (long)transmittance.Item1 * transmittance.Item2 * Rgba16fBytes +
            (long)multiSize * multiSize * Rgba16fBytes +
            (long)sky.Item1 * sky.Item2 * Rgba16fBytes +
            (long)aerialWidth * aerialHeight * aerialDepth * Rgba16fBytes;
        if (cloudShell)
        {
            bytes += 64L * 64L * 32L * Rgba16fBytes;
        }

        return new VolumetricAtmosphereLutBudget(
            true,
            quality,
            transmittance.Item1,
            transmittance.Item2,
            multiSize,
            sky.Item1,
            sky.Item2,
            aerialWidth,
            aerialHeight,
            aerialDepth,
            cloudShell,
            bytes,
            "ready for allocation");
    }

    public string DebugSummary => Enabled
        ? FormattableString.Invariant(
            $"q{Quality} trans={TransmittanceWidth}x{TransmittanceHeight} sky={SkyViewWidth}x{SkyViewHeight} aerial={AerialWidth}x{AerialHeight}x{AerialDepth} mem={EstimatedBytes / (1024.0 * 1024.0):0.0}MB")
        : Reason;

    public long TransmittanceBytes => Enabled
        ? (long)TransmittanceWidth * TransmittanceHeight * Rgba16fBytes
        : 0;

    public long MultiScatteringBytes => Enabled
        ? (long)MultiScatteringSize * MultiScatteringSize * Rgba16fBytes
        : 0;

    public long SkyViewBytes => Enabled
        ? (long)SkyViewWidth * SkyViewHeight * Rgba16fBytes
        : 0;

    public long AerialPerspectiveBytes => Enabled
        ? (long)AerialWidth * AerialHeight * AerialDepth * Rgba16fBytes
        : 0;

    public long CloudShellBytes => Enabled && CloudShell
        ? 64L * 64L * 32L * Rgba16fBytes
        : 0;

    private static VolumetricAtmosphereLutBudget Disabled(int quality, string reason) =>
        new(false, quality, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, reason);
}
