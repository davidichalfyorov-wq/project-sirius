using System;
using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Graphics;

namespace LibreLancer.Render;

/// <summary>
/// Scene-side ray tracing bookkeeping (graphics roadmap phase 4): a BLAS
/// cache keyed by mesh LOD0 resources with lazy budgeted builds, and a
/// per-frame TLAS instance list collected from the renderer's object list
/// (same filter as the shadow caster pass: rigid models only).
/// </summary>
public class RayTracedScene
{
    private const int MaxBlasBuildsPerFrame = 8;
    private const int MaxInstances = 4096;
    private const long EvictAfterFrames = 600;

    private sealed class CacheEntry
    {
        public BlasHandle Handle;
        public long LastSeenFrame;
    }

    private readonly IRayTracing rayTracing;
    private readonly Dictionary<VMeshResource, CacheEntry> cache = new();
    private readonly List<TlasInstance> instances = new();
    private long frame;
    private int buildsThisFrame;
    private bool overflowLogged;
    private int skipNoResource, skipNoIndices, skipNoSource, skipOther;

    public RayTracedScene(IRayTracing rayTracing)
    {
        this.rayTracing = rayTracing;
    }

    public int InstanceCount => instances.Count;

    public void BeginFrame()
    {
        frame++;
        buildsThisFrame = 0;
        instances.Clear();
    }

    /// <summary>Adds one rigid model part's mesh at LOD0; builds its BLAS
    /// on first sight (budgeted per frame).</summary>
    public void AddPart(RigidModelPart part, in Matrix4x4 world)
    {
        if (instances.Count >= MaxInstances)
        {
            if (!overflowLogged)
            {
                overflowLogged = true;
                FLLog.Warning("RayTracing", $"TLAS instance cap {MaxInstances} reached; extra geometry skipped");
            }
            return;
        }
        var level = part.Mesh?.Levels is { Length: > 0 } levels ? levels[0] : null;
        var resource = level?.Resource;
        if (resource == null || resource.IsDisposed || resource.Indices == null ||
            resource.SourceVertices == null || level!.Drawcalls == null)
        {
            if (resource == null) skipNoResource++;
            else if (resource.Indices == null) skipNoIndices++;
            else if (resource.SourceVertices == null) skipNoSource++;
            else skipOther++;
            return;
        }

        if (!cache.TryGetValue(resource, out var entry))
        {
            if (buildsThisFrame >= MaxBlasBuildsPerFrame)
            {
                return; // next frame; shadows fade in over a few frames
            }
            buildsThisFrame++;
            Span<BlasRange> ranges = stackalloc BlasRange[level.Drawcalls.Length];
            for (var i = 0; i < level.Drawcalls.Length; i++)
            {
                var dc = level.Drawcalls[i];
                ranges[i] = new BlasRange(dc.StartIndex, dc.PrimitiveCount, dc.BaseVertex);
            }
            var handle = rayTracing.CreateBlas(resource.SourceVertices, resource.SourceStride,
                resource.SourceVertexCount, resource.Indices, ranges);
            if (!handle.IsValid)
            {
                return;
            }
            entry = new CacheEntry { Handle = handle };
            cache.Add(resource, entry);
        }

        entry.LastSeenFrame = frame;
        var localWorld = part.LocalTransform.Matrix() * world;
        instances.Add(new TlasInstance(entry.Handle, localWorld, (uint)instances.Count));
    }

    /// <summary>Builds the TLAS from this frame's instances and evicts
    /// long-unseen or disposed BLAS entries.</summary>
    public void EndFrame()
    {
        rayTracing.BuildSceneTlas(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(instances));
        if (frame % 300 == 1)
        {
            FLLog.Info("RayTracing", $"frame {frame}: {instances.Count} instances, {cache.Count} BLAS cached; " +
                $"skips res={skipNoResource} idx={skipNoIndices} src={skipNoSource} other={skipOther}");
        }
        skipNoResource = skipNoIndices = skipNoSource = skipOther = 0;

        List<VMeshResource>? evict = null;
        foreach (var (resource, entry) in cache)
        {
            if (resource.IsDisposed || frame - entry.LastSeenFrame > EvictAfterFrames)
            {
                (evict ??= new List<VMeshResource>()).Add(resource);
            }
        }
        if (evict != null)
        {
            foreach (var resource in evict)
            {
                rayTracing.DestroyBlas(cache[resource].Handle);
                cache.Remove(resource);
            }
        }
    }

    public void Clear()
    {
        foreach (var entry in cache.Values)
        {
            rayTracing.DestroyBlas(entry.Handle);
        }
        cache.Clear();
        instances.Clear();
    }
}
