using System;
using System.Numerics;

namespace LibreLancer.Graphics;

/// <summary>Opaque handle to a bottom-level acceleration structure.</summary>
public readonly record struct BlasHandle(int Id)
{
    public static readonly BlasHandle Invalid = new(-1);
    public bool IsValid => Id >= 0;
}

/// <summary>Submesh index range for BLAS geometry (one geometry per range).</summary>
public readonly record struct BlasRange(int FirstIndex, int TriangleCount, int BaseVertex);

/// <summary>One TLAS instance: a BLAS placed in the world. Id lands in
/// InstanceCustomIndex (readable in shaders for per-instance data).</summary>
public readonly record struct TlasInstance(BlasHandle Blas, Matrix4x4 Transform, uint Id);

/// <summary>
/// Ray tracing services (graphics roadmap phase 4, ray-query tier).
/// Null on backends/devices without VK_KHR_ray_query support.
/// </summary>
public interface IRayTracing
{
    /// <summary>Builds a BLAS from CPU triangle data (positions are the
    /// first 12 bytes of each vertex). Returns Invalid when the build
    /// can't be queued.</summary>
    BlasHandle CreateBlas(ReadOnlySpan<byte> vertexData, int vertexStride, int vertexCount,
        ReadOnlySpan<ushort> indices, ReadOnlySpan<BlasRange> ranges);

    /// <summary>Queues the BLAS for destruction (deferred until the GPU
    /// is done with in-flight frames).</summary>
    void DestroyBlas(BlasHandle handle);

    /// <summary>Rebuilds the scene TLAS from this frame's instances and
    /// binds it for ray-query shaders. Call once per frame, before the
    /// scene pass consumes it.</summary>
    void BuildSceneTlas(ReadOnlySpan<TlasInstance> instances);

    /// <summary>Number of live BLAS entries (Dev HUD).</summary>
    int BlasCount { get; }

    /// <summary>Instances submitted to the last TLAS build (Dev HUD).</summary>
    int LastInstanceCount { get; }
}
