using System;
using LibreLancer.Graphics;

namespace LibreLancer.Render.Volumetrics;

/// <summary>
/// PR-5.2 froxel resource owner. This allocates and identity-clears the textures
/// that later density, lighting, integration and temporal reprojection passes will
/// consume. It deliberately does not bind textures into materials yet, preserving
/// the legacy nebula path until a real composite pass exists.
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
    private bool nearAllocated;
    private bool displacementAllocated;
    private bool disposed;

    public Texture3D? Density { get; private set; }
    public Texture3D? Lighting { get; private set; }
    public Texture3D? Integrated { get; private set; }
    public Texture3D? History { get; private set; }

    public Texture3D? NearDensity { get; private set; }
    public Texture3D? NearLighting { get; private set; }
    public Texture3D? NearIntegrated { get; private set; }
    public Texture3D? NearHistory { get; private set; }
    public Texture3D? DisplacementField { get; private set; }

    public VolumetricNebulaQualityProfile QualityProfile => qualityProfile;
    public FroxelGridDesc MainDesc => mainDesc;
    public FroxelGridDesc NearDesc => nearDesc;
    public bool Allocated => Density != null && Lighting != null && Integrated != null && History != null;
    public bool NearAllocated => nearAllocated && NearDensity != null && NearLighting != null && NearIntegrated != null && NearHistory != null;
    public bool DisplacementAllocated => displacementAllocated && DisplacementField != null;
    public long EstimatedBytes => Allocated ? EstimateBytes() : 0;
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
            DisposeTextures();
            LastDebug = VolumetricNebulaResourceDebug.Waiting("waiting for active nebula profile");
            return;
        }

        var desiredProfile = VolumetricNebulaQualityProfile.Create(renderWidth, renderHeight, features, active);
        var desired = desiredProfile.MainGrid;
        var desiredNear = desiredProfile.NearGrid;
        var wantsNear = features.VolumetricNearCascade;
        var wantsDisplacement = features.VolumetricShipDisplacement;
        var needsAllocate = !Allocated ||
                            desired != mainDesc ||
                            desiredNear != nearDesc ||
                            allocatedQuality != desired.Quality ||
                            wantsNear != nearAllocated ||
                            wantsDisplacement != displacementAllocated;

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
                nearAllocated = wantsNear;
                displacementAllocated = wantsDisplacement;

                Density = CreateVolume(rstate, "density", desired);
                Lighting = CreateVolume(rstate, "lighting", desired);
                Integrated = CreateVolume(rstate, "integrated", desired);
                History = CreateVolume(rstate, "history", desired);

                if (nearAllocated)
                {
                    NearDensity = CreateVolume(rstate, "near_density", desiredNear);
                    NearLighting = CreateVolume(rstate, "near_lighting", desiredNear);
                    NearIntegrated = CreateVolume(rstate, "near_integrated", desiredNear);
                    NearHistory = CreateVolume(rstate, "near_history", desiredNear);
                }

                if (displacementAllocated)
                {
                    DisplacementField = CreateVolume(rstate, "displacement", desiredNear);
                }

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
            activeProfile = active.Nickname;
        }

        rstate.BeginPassTimer("vol_nebula_clear");
        rstate.EndPassTimer();

        LastDebug = new VolumetricNebulaResourceDebug(
            true,
            mainDesc.Dimensions,
            nearAllocated ? nearDesc.Dimensions : "disabled",
            qualityProfile.Name,
            mainDesc.Quality,
            activeProfile,
            EstimatedBytes,
            needsAllocate ? "allocated + identity uploaded" : "identity clear marker",
            mainDesc.DebugName,
            VolumetricNebulaPassDeclaration.CanonicalOrder.Count,
            nearAllocated,
            displacementAllocated);
    }

    private static Texture3D CreateVolume(RenderContext rstate, string role, FroxelGridDesc desc) =>
        new(rstate, desc.Width, desc.Height, desc.Depth, SurfaceFormat.HdrBlendable, storage: true);

    private void ClearIdentityUploads()
    {
        if (!Allocated)
        {
            return;
        }

        ClearZero(Density!, mainDesc);
        ClearZero(Lighting!, mainDesc);
        ClearIdentity(Integrated!, mainDesc);
        ClearIdentity(History!, mainDesc);

        if (NearAllocated)
        {
            ClearZero(NearDensity!, nearDesc);
            ClearZero(NearLighting!, nearDesc);
            ClearIdentity(NearIntegrated!, nearDesc);
            ClearIdentity(NearHistory!, nearDesc);
        }

        if (DisplacementAllocated)
        {
            ClearZero(DisplacementField!, nearDesc);
        }
    }

    private static void ClearZero(Texture3D texture, FroxelGridDesc desc)
    {
        var zeros = new ushort[checked(desc.VoxelCount * 4)];
        texture.SetData(zeros);
    }

    private static void ClearIdentity(Texture3D texture, FroxelGridDesc desc)
    {
        var identity = new ushort[checked(desc.VoxelCount * 4)];
        for (var i = 0; i < identity.Length; i += 4)
        {
            identity[i + 0] = HalfZero;
            identity[i + 1] = HalfZero;
            identity[i + 2] = HalfZero;
            identity[i + 3] = HalfOne;
        }
        texture.SetData(identity);
    }

    private long EstimateBytes()
    {
        var bytes = mainDesc.BytesPerVolume() * 4;
        if (NearAllocated)
        {
            bytes += nearDesc.BytesPerVolume() * 4;
        }
        if (DisplacementAllocated)
        {
            bytes += nearDesc.BytesPerVolume();
        }
        return bytes;
    }

    private void DisposeTextures()
    {
        Density?.Dispose();
        Lighting?.Dispose();
        Integrated?.Dispose();
        History?.Dispose();
        NearDensity?.Dispose();
        NearLighting?.Dispose();
        NearIntegrated?.Dispose();
        NearHistory?.Dispose();
        DisplacementField?.Dispose();
        Density = null;
        Lighting = null;
        Integrated = null;
        History = null;
        NearDensity = null;
        NearLighting = null;
        NearIntegrated = null;
        NearHistory = null;
        DisplacementField = null;
        qualityProfile = default;
        activeProfile = string.Empty;
        allocatedQuality = -1;
        mainDesc = default;
        nearDesc = default;
        nearAllocated = false;
        displacementAllocated = false;
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
    int PassSlotCount,
    bool NearAllocated,
    bool DisplacementAllocated)
{
    public static VolumetricNebulaResourceDebug Disabled(string reason) =>
        new(false, "not allocated", "not allocated", "off", -1, "", 0, reason, "vol_nebula.none", 0, false, false);

    public static VolumetricNebulaResourceDebug Waiting(string reason) =>
        new(false, "waiting", "waiting", "waiting", -1, "", 0, reason, "vol_nebula.waiting", VolumetricNebulaPassDeclaration.CanonicalOrder.Count, false, false);
}
