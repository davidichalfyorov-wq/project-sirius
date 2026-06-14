using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using LibreLancer;
using LibreLancer.Data;
using LibreLancer.Fx;
using LibreLancer.Graphics;
using LibreLancer.Graphics.Primitives;
using LibreLancer.Render;
using LibreLancer.Resources;
using LibreLancer.Utf.Cmp;
using LibreLancer.Utf.Mat;
using LibreLancer.Utf.Vms;
using LibreLancer.ContentEdit;
using LibreLancer.ContentEdit.Model;
using SimpleMesh;
using Material = LibreLancer.Utf.Mat.Material;

// Headless, GL-free resource manager that resolves ONLY VMeshData (from a cmp's
// embedded VMeshLibrary). Everything else is a stub — enough for geometry-only
// glTF export via ModelExporter (IncludeTextures=false, sur=null).
class MeshOnlyResources : ResourceManager
{
    private readonly Dictionary<uint, VMeshData> meshes;

    public MeshOnlyResources(CmpFile cmp) : base(null)
    {
        meshes = cmp.VMeshLibrary?.Meshes ?? new Dictionary<uint, VMeshData>();
    }

    public override VMeshData? FindMeshData(uint vMeshLibId)
        => meshes.TryGetValue(vMeshLibId, out var m) ? m : null;

    public override Dictionary<string, Texture?> TextureDictionary => throw new InvalidOperationException();
    public override Dictionary<uint, Material?> MaterialDictionary => throw new InvalidOperationException();
    public override Dictionary<string, TexFrameAnimation?> AnimationDictionary => throw new InvalidOperationException();
    public override VertexResource AllocateVertices(FVFVertex format, byte[] vertices, ushort[] indices) => throw new InvalidOperationException();
    public override OpenCylinder GetOpenCylinder(int slices) => throw new InvalidOperationException();
    public override ParticleLibrary? GetParticleLibrary(string? filename) => null;
    public override QuadSphere GetQuadSphere(int slices) => throw new InvalidOperationException();
    public override Material? FindMaterial(uint materialId) => null;
    public override VMeshResource? FindMesh(uint vMeshLibId) => null;
    public override Texture? FindTexture(string name) => null;
    public override ImageResource? FindImage(string name) => null;
    public override bool TryGetShape(string name, out TextureShape? shape) { shape = null; return false; }
    public override bool TryGetFrameAnimation(string name, [MaybeNullWhen(false)] out TexFrameAnimation anim) { anim = default; return false; }
    public override ModelResource? GetDrawable(string? filename, MeshLoadMode loadMode = MeshLoadMode.GPU) => null;
    public override void LoadResourceFile(string? filename, MeshLoadMode loadMode = MeshLoadMode.GPU) { }
}

class ExportCmp
{
    static int Main(string[] argv)
    {
        if (argv.Length < 2)
        {
            Console.Error.WriteLine("usage: export_cmp <input.cmp> <output.glb>");
            return 2;
        }
        string inp = argv[0], outp = argv[1];
        if (!File.Exists(inp))
        {
            Console.Error.WriteLine($"not found: {inp}");
            return 2;
        }

        CmpFile cmp;
        using (var fs = File.OpenRead(inp))
            cmp = new CmpFile(inp, fs);

        var res = new MeshOnlyResources(cmp);

        var settings = new ModelExporterSettings
        {
            IncludeLods       = false,   // LOD0 only = highest detail
            IncludeHulls      = false,
            IncludeHardpoints = false,
            IncludeTextures   = false,
            IncludeWireframes = false,
            IncludeAnimations = false,
        };

        var exported = ModelExporter.Export(cmp, null, settings, res);
        foreach (var m in exported.Messages)
            Console.WriteLine($"{m.Kind}: {m.Message}");
        if (exported.IsError)
        {
            Console.Error.WriteLine("export failed");
            return 1;
        }

        using (var os = File.Create(outp))
            exported.Data.SaveTo(os, ModelSaveFormat.GLB);

        Console.WriteLine($"OK -> {outp}");
        return 0;
    }
}
