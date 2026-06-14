using System;
using System.Numerics;
using LibreLancer.Graphics;
using LibreLancer.Shaders;

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

    public bool DrawDebugView(RenderContext rstate, global::LibreLancer.Render.RenderDebugView debugView,
        int renderWidth, int renderHeight)
    {
        if (debugView is not (global::LibreLancer.Render.RenderDebugView.AtmosphereLuts or
                              global::LibreLancer.Render.RenderDebugView.AtmosphereAerial) ||
            !Allocated ||
            AllShaders.FroxelDebugSlice == null)
        {
            return false;
        }

        var source = debugView == global::LibreLancer.Render.RenderDebugView.AtmosphereAerial
            ? AerialPerspective
            : Transmittance;
        var paired = debugView == global::LibreLancer.Render.RenderDebugView.AtmosphereAerial
            ? AerialPerspective
            : MultiScattering;
        if (source == null || paired == null)
        {
            return false;
        }

        var shader = AllShaders.FroxelDebugSlice.Get(0);
        rstate.Textures[0] = source;
        rstate.Samplers[0] = SamplerState.LinearClamp;
        rstate.Textures[1] = paired;
        rstate.Samplers[1] = SamplerState.LinearClamp;
        var sliceParams = new AtmosphereDebugSliceParams
        {
            SliceParams = new Vector4(
                debugView == global::LibreLancer.Render.RenderDebugView.AtmosphereAerial ? 0.55f : 0f,
                1f,
                debugView == global::LibreLancer.Render.RenderDebugView.AtmosphereAerial ? 13f : 12f,
                1f),
            GridParams = new Vector4(source.Width, source.Height, Math.Max(1, source.Depth), generation)
        };
        shader.SetUniformBlock(3, ref sliceParams);

        var oldCull = rstate.Cull;
        var oldDepth = rstate.DepthEnabled;
        var oldBlend = rstate.BlendMode;
        var width = Math.Max(96, Math.Min(512, Math.Max(renderWidth - 32, 1) / 3));
        var height = Math.Max(72, Math.Min(288, width * 9 / 16));
        try
        {
            rstate.Cull = false;
            rstate.DepthEnabled = false;
            rstate.BlendMode = BlendMode.Opaque;
            rstate.PushViewport(16, 16, width, height);
            rstate.Shader = shader;
            rstate.DrawNoVertexBuffer(PrimitiveTypes.TriangleList, 1);
            rstate.PopViewport();
        }
        finally
        {
            rstate.Cull = oldCull;
            rstate.DepthEnabled = oldDepth;
            rstate.BlendMode = oldBlend;
        }
        return true;
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

    private struct AtmosphereDebugSliceParams
    {
        public Vector4 SliceParams;
        public Vector4 GridParams;
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
