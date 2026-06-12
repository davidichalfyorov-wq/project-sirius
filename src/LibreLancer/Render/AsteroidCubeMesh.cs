using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LibreLancer.Data.GameData.World;
using LibreLancer.Graphics;
using LibreLancer.Graphics.Vertices;
using LibreLancer.Resources;
using LibreLancer.Utf.Cmp;
using LibreLancer.Utf.Vms;

namespace LibreLancer.Render;

public struct CubeDrawcall
{
    public uint MaterialCrc;
    public int StartIndex;
    public int Count;
    public int BaseVertex;
}

public class AsteroidCubeMesh : IDisposable
{
    public VertexBuffer VertexBuffer;
    public ElementBuffer ElementBuffer;
    public CubeDrawcall[] Drawcalls;
    public float Radius;

    /// <summary>CPU copies for the mesh-shader path (roadmap 7.5):
    /// vertex pulling reads these via storage buffers, so the GPU
    /// buffers above stay untouched. Null when the cube is empty.</summary>
    public VertexPositionNormalDiffuseTexture[]? SourceVertices;
    public ushort[]? SourceIndices;

    public AsteroidCubeMesh(VertexBuffer vertexBuffer, ElementBuffer elementBuffer, CubeDrawcall[] drawcalls,
        float radius)
    {
        VertexBuffer = vertexBuffer;
        ElementBuffer = elementBuffer;
        Drawcalls = drawcalls;
        Radius = radius;
    }

    public void Dispose()
    {
        VertexBuffer?.Dispose();
        ElementBuffer?.Dispose();
    }
}

/// <summary>One mesh-shader workgroup's slice of the cube mesh: up to 64
/// unique vertices / 124 triangles, cut along drawcall (material) bounds.
/// Offsets index the flat arrays in AsteroidMeshletSet.</summary>
public struct CubeMeshlet
{
    public uint VertexOffset;   // into MeshletVertices (global vertex ids)
    public uint VertexCount;
    public uint TriangleOffset; // into MeshletTriangles (3 local ids each)
    public uint TriangleCount;
    public uint DrawcallIndex;
}

/// <summary>CPU-side meshlet split of an AsteroidCubeMesh, built once at
/// field load. Layout mirrors what the mesh shader consumes.</summary>
public class AsteroidMeshletSet
{
    public CubeMeshlet[] Meshlets = [];
    public uint[] MeshletVertices = [];  // global vertex indices
    public byte[] MeshletTriangles = []; // local indices, 3 per triangle

    // 16 triangles, not the EXT limit: emissions with buffer-sourced
    // vertex data die silently past ~16-32 output primitives on
    // NVIDIA 580.x + DXC 1.9 (empirical bisect, C2 bring-up). 16 is the
    // proven-stable plateau; smaller meshlets just mean more workgroups.
    public const int MaxVertices = 64;
    public const int MaxTriangles = 16;

    public static AsteroidMeshletSet Build(ushort[] indices, CubeDrawcall[] drawcalls)
    {
        var set = new AsteroidMeshletSet();
        var meshlets = new List<CubeMeshlet>();
        var vertexList = new List<uint>();
        var triangleList = new List<byte>();

        for (var dc = 0; dc < drawcalls.Length; dc++)
        {
            var call = drawcalls[dc];
            var local = new Dictionary<uint, byte>();
            var meshletStartVertex = vertexList.Count;
            var meshletStartTri = triangleList.Count;

            void Flush()
            {
                if (local.Count == 0)
                {
                    return;
                }
                // Pad to EXACTLY MaxVertices/MaxTriangles: the mesh shader
                // then calls SetMeshOutputCounts with literals - DXC's mesh
                // codegen drops the whole emission when the counts come from
                // a buffer read (empirically, NVIDIA 580.x + DXC 1.9).
                // Padding vertices repeat vertex 0; padding triangles are
                // zero-area (0,0,0) and die at raster setup for free.
                var firstVertex = vertexList[meshletStartVertex];
                while (vertexList.Count - meshletStartVertex < MaxVertices)
                {
                    vertexList.Add(firstVertex);
                }
                while ((triangleList.Count - meshletStartTri) / 3 < MaxTriangles)
                {
                    triangleList.Add(0);
                    triangleList.Add(0);
                    triangleList.Add(0);
                }
                meshlets.Add(new CubeMeshlet
                {
                    VertexOffset = (uint)meshletStartVertex,
                    VertexCount = MaxVertices,
                    TriangleOffset = (uint)meshletStartTri,
                    TriangleCount = MaxTriangles,
                    DrawcallIndex = (uint)dc
                });
                local.Clear();
                meshletStartVertex = vertexList.Count;
                meshletStartTri = triangleList.Count;
            }

            byte LocalIndex(uint global)
            {
                if (!local.TryGetValue(global, out var id))
                {
                    id = (byte)local.Count;
                    local.Add(global, id);
                    vertexList.Add(global);
                }
                return id;
            }

            for (var tri = 0; tri < call.Count / 3; tri++)
            {
                var i0 = (uint)(indices[call.StartIndex + tri * 3] + call.BaseVertex);
                var i1 = (uint)(indices[call.StartIndex + tri * 3 + 1] + call.BaseVertex);
                var i2 = (uint)(indices[call.StartIndex + tri * 3 + 2] + call.BaseVertex);
                // Would this triangle overflow the meshlet? Flush first.
                var newVerts = (local.ContainsKey(i0) ? 0 : 1) +
                               (local.ContainsKey(i1) ? 0 : 1) +
                               (local.ContainsKey(i2) ? 0 : 1);
                if (local.Count + newVerts > MaxVertices ||
                    (triangleList.Count - meshletStartTri) / 3 + 1 > MaxTriangles)
                {
                    Flush();
                }
                triangleList.Add(LocalIndex(i0));
                triangleList.Add(LocalIndex(i1));
                triangleList.Add(LocalIndex(i2));
            }
            Flush();
        }

        set.Meshlets = meshlets.ToArray();
        set.MeshletVertices = vertexList.ToArray();
        set.MeshletTriangles = triangleList.ToArray();
        return set;
    }
}

public class AsteroidCubeMeshBuilder
{
    private List<VertexPositionNormalDiffuseTexture> verts = [];
    private List<ushort> indices = [];
    private List<int> hashes = [];
    private List<CubeDrawcall> cubeDrawCalls = [];
    private float radius = 0;

    public AsteroidCubeMesh CreateMesh(RenderContext context, AsteroidField field, ResourceManager resources)
    {
        verts = [];
        indices = [];
        hashes = [];
        cubeDrawCalls = [];
        radius = 0;
        // Gather a list of all materials
        List<uint> matCrcs = [];

        if (field.AllowMultipleMaterials)
        {
            foreach (var ast in field.Cube!)
            {
                var f = (ModelFile) ast.Archetype!.ModelFile!.LoadFile(resources, MeshLoadMode.CPU)!.Drawable;
                var l0 = f.Levels[0];
                var vms = resources.FindMeshData(l0!.MeshCrc)!;

                for (int i = l0.StartMesh; i < l0.StartMesh + l0.MeshCount; i++)
                {
                    var m = vms.Meshes[i].MaterialCrc;
                    if (!matCrcs.Contains(m))
                        matCrcs.Add(m);
                }
            }
        }
        else
        {
            var f = (ModelFile) field.Cube![0].Archetype!.ModelFile!.LoadFile(resources, MeshLoadMode.CPU)!.Drawable;
            var l0 = f.Levels[0];
            var vms = resources.FindMeshData(l0!.MeshCrc)!;
            matCrcs.Add(vms.Meshes[l0.StartMesh].MaterialCrc);
        }

        // Create the draw calls
        foreach (var mat in matCrcs)
        {
            var start = indices.Count;
            var newIndices = new List<int>();

            foreach (var ast in field.Cube)
            {
                AddAsteroidToBuffer(ast, mat, matCrcs.Count == 1, newIndices, resources, field.CubeSize);
            }

            var min = newIndices.Min();

            foreach (var i in newIndices)
            {
                indices.Add(checked((ushort) (i - min)));
            }

            var count = indices.Count - start;
            cubeDrawCalls.Add(new CubeDrawcall()
                { BaseVertex = min, MaterialCrc = mat, StartIndex = start, Count = count });
        }

        var cubeVbo = new VertexBuffer(context, typeof(VertexPositionNormalDiffuseTexture), verts.Count);
        var cubeIbo = new ElementBuffer(context, indices.Count);
        var vertArray = verts.ToArray();
        var indexArray = indices.ToArray();
        cubeIbo.SetData(indexArray);
        cubeVbo.SetData<VertexPositionNormalDiffuseTexture>(vertArray);
        cubeVbo.SetElementBuffer(cubeIbo);
        var dcs = cubeDrawCalls.ToArray();

        // Memory cleanup
        verts = null!;
        indices = null!;
        cubeDrawCalls = null!;

        return new AsteroidCubeMesh(cubeVbo, cubeIbo, dcs, radius)
        {
            // Kept for the mesh-shader path's vertex pulling (roadmap 7.5).
            SourceVertices = vertArray,
            SourceIndices = indexArray
        };
    }

    private VertexPositionNormalDiffuseTexture GetVertex(VMeshData vms, int index, ref Matrix4x4 world,
        ref Matrix4x4 normal)
    {
        VertexPositionNormalDiffuseTexture vert = new VertexPositionNormalDiffuseTexture
        {
            Position = vms.GetPosition(index),
            Normal = vms.VertexFormat.Normal ? vms.GetNormal(index) : Vector3.UnitY,
            Diffuse = vms.VertexFormat.Diffuse ? vms.GetDiffuse(index) : (VertexDiffuse) 0xFFFFFFFF,
            TextureCoordinate = vms.VertexFormat.TexCoords > 0 ? vms.GetTexCoord(index, 0) : Vector2.Zero
        };

        vert.Position = Vector3.Transform(vert.Position, world);
        vert.Normal = Vector3.TransformNormal(vert.Normal, normal);
        return vert;
    }

    private void AddAsteroidToBuffer(StaticAsteroid ast, uint matCrc, bool singleMat, List<int> newIndices,
        ResourceManager resources, float cubeSize)
    {
        var model = (ModelFile) ast.Archetype!.ModelFile!.LoadFile(resources, MeshLoadMode.CPU)!.Drawable;
        var l0 = model.Levels[0];
        var vms = resources.FindMeshData(l0!.MeshCrc);
        var transform = new Transform3D(ast.Position * cubeSize, ast.Rotation).Matrix();
        var norm = transform;
        Matrix4x4.Invert(norm, out norm);
        norm = Matrix4x4.Transpose(norm);

        for (int i = l0.StartMesh; i < l0.StartMesh + l0.MeshCount; i++)
        {
            var m = vms!.Meshes[i];
            if (m.MaterialCrc != matCrc && !singleMat) continue;
            var baseVertex = l0.StartVertex + m.StartVertex;
            var indexStart = m.TriangleStart;
            int indexCount = m.NumRefVertices;

            for (var j = indexStart; j < indexStart + indexCount; j++)
            {
                var idx = baseVertex + vms.Indices[j];
                var vtx = GetVertex(vms, idx, ref transform, ref norm);
                var hash = vtx.GetHashCode();
                var x = hashes.IndexOf(hash);

                if (x == -1 || verts[x] != vtx)
                {
                    x = verts.Count;
                    verts.Add(vtx);
                    hashes.Add(hash);
                    var d = vtx.Position.Length();
                    if (d > radius)
                        radius = d;
                }

                newIndices.Add(x);
            }
        }
    }
}
