using System;
using LibreLancer.Graphics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// Resource owner for the Phase 5/B1 atmosphere LUT path. The textures are
/// identity-filled until compute generation lands, so enabling atmosphere_luts
/// gives RenderDoc/HUD-visible resources without changing the current
/// Atmosphere 2.0 visual path.
/// </summary>
public sealed class VolumetricAtmosphereFrameResources : IDisposable
{
    private const ushort HalfZero = 0x0000;
    private const ushort HalfOne = 0x3C00;

    private bool disposed;
    private int generation;
    private VolumetricAtmosphereLutBudget allocatedBudget;

    public Texture3D? Transmittance { get; private set; }
    public Texture3D? MultiScattering { get; private set; }
    public Texture3D? AerialPerspective { get; private set; }

    public bool Allocated => Transmittance is { IsDisposed: false } &&
                             MultiScattering is { IsDisposed: false } &&
                             AerialPerspective is { IsDisposed: false };

    public static VolumetricAtmosphereResourceDebug LastDebug { get; private set; } =
        VolumetricAtmosphereResourceDebug.Disabled("not initialized");

    public static void NoteDisabled(string reason)
    {
        LastDebug = VolumetricAtmosphereResourceDebug.Disabled(reason);
        global::LibreLancer.Render.RenderMaterial.SetAtmosphereLutSource(null, null);
        global::LibreLancer.Render.RenderMaterial.SetAtmosphereAerialSource(null, System.Numerics.Vector4.Zero);
    }

    public void Ensure(RenderContext rstate, global::LibreLancer.Render.RenderFeatureSet features,
        int renderWidth, int renderHeight)
    {
        ThrowIfDisposed();

        var budget = VolumetricAtmosphereLutBudget.Create(
            features.AtmosphereLuts,
            rstate.HasFeature(GraphicsFeature.Compute),
            features.VolumetricQuality,
            renderWidth,
            renderHeight,
            cloudShellRequested: features.VolumetricQuality >= 2);
        if (!budget.Enabled)
        {
            DisposeTextures();
            NoteDisabled(budget.Reason);
            return;
        }

        var needsAllocate = !Allocated || !Matches(allocatedBudget, budget);
        if (needsAllocate)
        {
            rstate.BeginPassTimer("vol_atmosphere_allocate");
            try
            {
                Allocate(rstate, budget);
            }
            finally
            {
                rstate.EndPassTimer();
            }
        }

        global::LibreLancer.Render.RenderMaterial.SetAtmosphereLutSource(Transmittance, MultiScattering);
        global::LibreLancer.Render.RenderMaterial.SetAtmosphereAerialSource(
            AerialPerspective,
            new System.Numerics.Vector4(1f, Math.Max(1f, budget.AerialDepth * 1000f), 0f, 0f));
        LastDebug = new VolumetricAtmosphereResourceDebug(
            true,
            budget.DebugSummary,
            $"{budget.TransmittanceWidth}x{budget.TransmittanceHeight}x1 / " +
            $"{budget.MultiScatteringSize}x{budget.MultiScatteringSize}x1 / " +
            $"{budget.AerialWidth}x{budget.AerialHeight}x{budget.AerialDepth}",
            budget.EstimatedBytes,
            generation,
            true);
    }

    private void Allocate(RenderContext rstate, VolumetricAtmosphereLutBudget budget)
    {
        DisposeTextures();
        Transmittance = new Texture3D(rstate, budget.TransmittanceWidth, budget.TransmittanceHeight, 1,
            SurfaceFormat.HdrBlendable, storage: true);
        MultiScattering = new Texture3D(rstate, budget.MultiScatteringSize, budget.MultiScatteringSize, 1,
            SurfaceFormat.HdrBlendable, storage: true);
        AerialPerspective = new Texture3D(rstate, budget.AerialWidth, budget.AerialHeight, budget.AerialDepth,
            SurfaceFormat.HdrBlendable, storage: true);
        FillTransmittanceIdentity(Transmittance);
        FillZero(MultiScattering);
        FillAerialIdentity(AerialPerspective);
        allocatedBudget = budget;
        generation++;
    }

    private static bool Matches(VolumetricAtmosphereLutBudget a, VolumetricAtmosphereLutBudget b) =>
        a.Quality == b.Quality &&
        a.TransmittanceWidth == b.TransmittanceWidth &&
        a.TransmittanceHeight == b.TransmittanceHeight &&
        a.MultiScatteringSize == b.MultiScatteringSize &&
        a.AerialWidth == b.AerialWidth &&
        a.AerialHeight == b.AerialHeight &&
        a.AerialDepth == b.AerialDepth &&
        a.CloudShell == b.CloudShell;

    private static void FillTransmittanceIdentity(Texture3D texture)
    {
        var data = new ushort[checked(texture.Width * texture.Height * texture.Depth * 4)];
        Array.Fill(data, HalfOne);
        texture.SetData(data);
    }

    private static void FillAerialIdentity(Texture3D texture)
    {
        var data = new ushort[checked(texture.Width * texture.Height * texture.Depth * 4)];
        for (var i = 3; i < data.Length; i += 4)
        {
            data[i] = HalfOne;
        }
        texture.SetData(data);
    }

    private static void FillZero(Texture3D texture)
    {
        var data = new ushort[checked(texture.Width * texture.Height * texture.Depth * 4)];
        texture.SetData(data);
    }

    private void DisposeTextures()
    {
        Transmittance?.Dispose();
        MultiScattering?.Dispose();
        AerialPerspective?.Dispose();
        Transmittance = null;
        MultiScattering = null;
        AerialPerspective = null;
        allocatedBudget = default;
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(VolumetricAtmosphereFrameResources));
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        DisposeTextures();
        NoteDisabled("disposed");
    }
}

public readonly record struct VolumetricAtmosphereResourceDebug(
    bool Allocated,
    string Summary,
    string Dimensions,
    long EstimatedBytes,
    int Generation,
    bool Bound)
{
    public static VolumetricAtmosphereResourceDebug Disabled(string reason) =>
        new(false, reason, "not allocated", 0, 0, false);
}
