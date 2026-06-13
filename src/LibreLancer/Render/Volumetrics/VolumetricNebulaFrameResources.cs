using System;
using LibreLancer.Graphics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// PR-5.2 froxel resource owner. This allocates and clears the textures that later
/// density, lighting, integration and temporal reprojection passes will consume.
/// It deliberately does not bind the textures into materials yet, preserving the
/// legacy nebula path until a real composite pass exists.
/// </summary>
public sealed class VolumetricNebulaFrameResources : IDisposable
{
    private const ushort HalfZero = 0x0000;
    private const ushort HalfOne = 0x3C00;

    private VolumetricNebulaQualityProfile qualityProfile;
    private FroxelGridDesc mainDesc;
    private FroxelGridDesc nearDesc;
    private int allocatedQuality = -1;
    private string activeProfile = string.Empty;
    private bool disposed;

    public Texture3D? Density { get; private set; }
    public Texture3D? Lighting { get; private set; }
    public Texture3D? Integrated { get; private set; }
    public Texture3D? History { get; private set; }

    public VolumetricNebulaQualityProfile QualityProfile => qualityProfile;
    public FroxelGridDesc MainDesc => mainDesc;
    public FroxelGridDesc NearDesc => nearDesc;
    public bool Allocated => Density != null && Lighting != null && Integrated != null && History != null;
    public long EstimatedBytes => Allocated ? mainDesc.BytesPerVolume() * 4 : 0;
    public string ActiveProfile => activeProfile;

    public static VolumetricNebulaResourceDebug LastDebug { get; private set; } =
        VolumetricNebulaResourceDebug.Disabled("not initialized");

    public void Ensure(
        RenderContext rstate,
        int renderWidth,
        int renderHeight,
        global::LibreLancer.Render.RenderFeatureSet features,
        NebulaVolumeProfile? profile)
    {
        ThrowIfDisposed();

        if (!features.VolumetricNebula)
        {
            DisposeTextures();
            LastDebug = VolumetricNebulaResourceDebug.Disabled("volumetric_nebula disabled");
            return;
        }

        if (!rstate.HasFeature(GraphicsFeature.Compute))
        {
            DisposeTextures();
            LastDebug = VolumetricNebulaResourceDebug.Disabled("backend has no compute feature");
            return;
        }

        if (profile is not { IsValid: true } active)
        {
            LastDebug = VolumetricNebulaResourceDebug.Waiting("waiting for active nebula profile");
            return;
        }

        var desiredProfile = VolumetricNebulaQualityProfile.Create(renderWidth, renderHeight, features, active);
        var desired = desiredProfile.MainGrid;
        var desiredNear = desiredProfile.NearGrid;
        var needsAllocate = !Allocated || desired != mainDesc || allocatedQuality != desired.Quality;

        if (needsAllocate)
        {
            rstate.BeginPassTimer("vol_nebula_allocate");
            try
            {
                DisposeTextures();
                qualityProfile = desiredProfile;
                mainDesc = desired;
                nearDesc = desiredNear;
                allocatedQuality = desired.Quality;
                activeProfile = active.Nickname;

                Density = CreateVolume(rstate, "density", desired);
                Lighting = CreateVolume(rstate, "lighting", desired);
                Integrated = CreateVolume(rstate, "integrated", desired);
                History = CreateVolume(rstate, "history", desired);

                // First allocation owns the only real upload in PR-5.2: zero density/light,
                // identity transmittance in A for integrated/history. Future compute clear
                // will replace this CPU upload without changing the resource contract.
                ClearIdentityUploads();
            }
            finally
            {
                rstate.EndPassTimer();
            }
        }
        else
        {
            qualityProfile = desiredProfile;
            nearDesc = desiredNear;
            activeProfile = active.Nickname;
        }

        rstate.BeginPassTimer("vol_nebula_clear");
        rstate.EndPassTimer();

        LastDebug = new VolumetricNebulaResourceDebug(
            true,
            mainDesc.Dimensions,
            nearDesc.Dimensions,
            qualityProfile.Name,
            mainDesc.Quality,
            activeProfile,
            EstimatedBytes,
            needsAllocate ? "allocated + identity uploaded" : "identity clear marker",
            mainDesc.DebugName,
            VolumetricNebulaPassDeclaration.CanonicalOrder.Count);
    }

    private static Texture3D CreateVolume(RenderContext rstate, string role, FroxelGridDesc desc) =>
        new(rstate, desc.Width, desc.Height, desc.Depth, SurfaceFormat.HdrBlendable, storage: true);

    private void ClearIdentityUploads()
    {
        if (!Allocated)
        {
            return;
        }

        var zeros = new ushort[checked(mainDesc.VoxelCount * 4)];
        Array.Fill(zeros, HalfZero);
        Density!.SetData(zeros);
        Lighting!.SetData(zeros);

        var identity = new ushort[checked(mainDesc.VoxelCount * 4)];
        for (var i = 0; i < identity.Length; i += 4)
        {
            identity[i + 0] = HalfZero;
            identity[i + 1] = HalfZero;
            identity[i + 2] = HalfZero;
            identity[i + 3] = HalfOne;
        }
        Integrated!.SetData(identity);
        History!.SetData(identity);
    }

    private void DisposeTextures()
    {
        Density?.Dispose();
        Lighting?.Dispose();
        Integrated?.Dispose();
        History?.Dispose();
        Density = null;
        Lighting = null;
        Integrated = null;
        History = null;
        qualityProfile = default;
        activeProfile = string.Empty;
        allocatedQuality = -1;
        mainDesc = default;
        nearDesc = default;
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(VolumetricNebulaFrameResources));
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
        LastDebug = VolumetricNebulaResourceDebug.Disabled("disposed");
    }
}

public readonly record struct VolumetricNebulaResourceDebug(
    bool Allocated,
    string Dimensions,
    string NearDimensions,
    string QualityName,
    int Quality,
    string ActiveProfile,
    long EstimatedBytes,
    string LastOperation,
    string DebugName,
    int PassSlotCount)
{
    public static VolumetricNebulaResourceDebug Disabled(string reason) =>
        new(false, "not allocated", "not allocated", "off", -1, "", 0, reason, "vol_nebula.none", 0);

    public static VolumetricNebulaResourceDebug Waiting(string reason) =>
        new(false, "waiting", "waiting", "waiting", -1, "", 0, reason, "vol_nebula.waiting", VolumetricNebulaPassDeclaration.CanonicalOrder.Count);
}
