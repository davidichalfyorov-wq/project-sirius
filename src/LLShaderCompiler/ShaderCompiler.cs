using System.Text;

namespace LLShaderCompiler;

public static class ShaderCompiler
{
    public static async Task Compile(string inputFile, string outputFile, string dumpFolder, bool verbose)
    {
        var shader = await ShaderInfo.FromFile(inputFile);

        if(verbose)
            Console.WriteLine(inputFile);

        if (!string.IsNullOrWhiteSpace(dumpFolder))
        {
            Directory.CreateDirectory(dumpFolder);
        }

        var permutations = FeatureHelper.AllPermutations(shader.Features.Select(x => x.Mask)).ToArray();
        var allShaders = new BundledShader[permutations.Length];

        if (shader.IsCompute)
        {
            // Compute bundle (roadmap phase 5): SPIR-V only, single stage.
            // Rides in the vertex slot with EntryPoint "CSMain" as the
            // pipeline-kind marker (mirror of mesh "MSMain"); readers on
            // other backends must never load these bundles.
            await Parallel.ForEachAsync(permutations, async (permutation, _) =>
            {
                List<string> csDefines = new List<string>();
                foreach (var f in shader.Features)
                {
                    if ((permutation & f.Mask) == f.Mask)
                    {
                        csDefines.Add(f.Feature);
                    }
                }
                // vulkanfeature permutations (ray query in compute) need
                // SM 6.5 + the vulkan1.2 target environment.
                var csRayQuery = (permutation & shader.VulkanOnlyMask) != 0;
                var csCode = await DXC.CompileSPIRV(shader.ComputeSource!, ShaderStage.Compute, csDefines, csRayQuery);
                GPUProgram csReflected;
                try
                {
                    csReflected = SpvcReflection.ReflectComputeProgram(csCode);
                }
                catch (Exception)
                {
                    // SPIRV-Cross may not parse ray-query opcodes: counts are
                    // unused on Vulkan (VKSpirv re-reflects), zero them.
                    csReflected = new GPUProgram(
                        new ReflectedShader("main", csCode),
                        new ReflectedShader("none", Array.Empty<byte>()));
                }
                csReflected.Vertex.EntryPoint = "CSMain";

                if (!string.IsNullOrWhiteSpace(dumpFolder))
                {
                    var cident = Path.GetFileNameWithoutExtension(inputFile) +
                        (csDefines.Count > 0 ? "." + string.Join(".", csDefines) : ".default");
                    await File.WriteAllBytesAsync(Path.Combine(dumpFolder, $"{cident}.comp.spv"), csCode);
                }

                var cIdx = Array.IndexOf(permutations, permutation);
                allShaders[cIdx] = new BundledShader(permutation, csReflected, null, null!, null);
            });

            if (verbose)
                Console.WriteLine($"Packing compute bundle to {outputFile}");
            using var csBundle = File.Create(outputFile);
            ShaderBundleWriter.Write(csBundle, new ShaderBundle() { Shaders = allShaders });
            return;
        }

        if (shader.IsMesh)
        {
            // Mesh-pipeline bundle (roadmap 7.5): SPIR-V only. The mesh
            // stage rides in the vertex slot of the existing container with
            // EntryPoint "MSMain" as the pipeline-kind marker - readers on
            // other backends must never load these bundles.
            await Parallel.ForEachAsync(permutations, async (permutation, _) =>
            {
                List<string> meshDefines = new List<string>();
                foreach (var f in shader.Features)
                {
                    if ((permutation & f.Mask) == f.Mask)
                    {
                        meshDefines.Add(f.Feature);
                    }
                }
                var meshCode = await DXC.CompileSPIRV(shader.MeshSource!, ShaderStage.Mesh, meshDefines);
                var fragCode = await DXC.CompileSPIRV(shader.FragmentSource, ShaderStage.Fragment, meshDefines);
                GPUProgram meshReflected;
                try
                {
                    meshReflected = SpvcReflection.ReflectProgram(meshCode, fragCode);
                }
                catch (Exception)
                {
                    // SPIRV-Cross may not parse EXT_mesh_shader opcodes:
                    // fall back to fragment-only reflection and zero counts
                    // on the mesh stage (resource use surfaces in C2 tests).
                    var fragOnly = SpvcReflection.ReflectProgram(fragCode, fragCode);
                    meshReflected = new GPUProgram(
                        new ReflectedShader("main", meshCode),
                        fragOnly.Fragment);
                }
                meshReflected.Vertex.EntryPoint = "MSMain";

                if (!string.IsNullOrWhiteSpace(dumpFolder))
                {
                    var mident = Path.GetFileNameWithoutExtension(inputFile) +
                        (meshDefines.Count > 0 ? "." + string.Join(".", meshDefines) : ".default");
                    await File.WriteAllBytesAsync(Path.Combine(dumpFolder, $"{mident}.mesh.spv"), meshCode);
                    await File.WriteAllBytesAsync(Path.Combine(dumpFolder, $"{mident}.frag.spv"), fragCode);
                }

                var mIdx = Array.IndexOf(permutations, permutation);
                allShaders[mIdx] = new BundledShader(permutation, meshReflected, null, null!, null);
            });

            if (verbose)
                Console.WriteLine($"Packing mesh bundle to {outputFile}");
            using var meshBundle = File.Create(outputFile);
            ShaderBundleWriter.Write(meshBundle, new ShaderBundle() { Shaders = allShaders });
            return;
        }

        await Parallel.ForEachAsync(permutations, async (permutation, _) =>
        {
            List<string> defines = new List<string>();
            foreach (var f in shader.Features)
            {
                if ((permutation & f.Mask) == f.Mask)
                {
                    defines.Add(f.Feature);
                }
            }

            if (verbose)
                Console.WriteLine($"Compiling {(defines.Count > 0 ? string.Join(", ", defines) : "default")}.");

            // Vulkan-only permutations (ray query etc.): SPIR-V is the only
            // real payload; GL/DXIL/MSL alias the base permutation after the
            // loop. Compiled with SM 6.5 + vulkan1.2 target environment.
            var vulkanOnly = (permutation & shader.VulkanOnlyMask) != 0;

            var variant = await CompileVariantSPIRV(shader, defines, vulkanOnly);
            GPUProgram reflected;
            if (vulkanOnly)
            {
                try
                {
                    reflected = SpvcReflection.ReflectProgram(variant.Vertex, variant.Fragment);
                }
                catch (Exception)
                {
                    // SPIRV-Cross can't parse the ray-query opcodes: take the
                    // resource counts from the base permutation (vulkan-only
                    // features are fragment-side additions; counts match) and
                    // substitute our SPIR-V code.
                    var baseDefines = new List<string>();
                    foreach (var f in shader.Features)
                    {
                        var baseMask = permutation & ~shader.VulkanOnlyMask;
                        if ((baseMask & f.Mask) == f.Mask)
                        {
                            baseDefines.Add(f.Feature);
                        }
                    }
                    var baseVariant = await CompileVariantSPIRV(shader, baseDefines, false);
                    var baseReflected = SpvcReflection.ReflectProgram(baseVariant.Vertex, baseVariant.Fragment);
                    reflected = new GPUProgram(
                        baseReflected.Vertex.CloneWithCode(variant.Vertex),
                        baseReflected.Fragment.CloneWithCode(variant.Fragment));
                }

                if (!string.IsNullOrWhiteSpace(dumpFolder))
                {
                    var vident = Path.GetFileNameWithoutExtension(inputFile) + "." + string.Join(".", defines);
                    await File.WriteAllBytesAsync(Path.Combine(dumpFolder, $"{vident}.vert.spv"), reflected.Vertex.Code);
                    await File.WriteAllBytesAsync(Path.Combine(dumpFolder, $"{vident}.frag.spv"), reflected.Fragment.Code);
                }

                var vIdx = Array.IndexOf(permutations, permutation);
                allShaders[vIdx] = new BundledShader(permutation, reflected, null, null!, null);
                return;
            }

            reflected = SpvcReflection.ReflectProgram(variant.Vertex, variant.Fragment);
            var glCompiled = shader.NoLegacy
                ? null
                : GLTranslator.TranslateProgram(shader.FriendlyName, variant.Vertex, variant.Fragment);

            var dxilCompiled = await DXILTranslator.TranslateProgram(reflected);
            
            var mslCompiled = MSLTranslator.TranslateProgram(reflected);

            if (!string.IsNullOrWhiteSpace(dumpFolder))
            {
                var ident = defines.Count > 0
                    ? string.Join(".", defines)
                    : "default";
                ident = Path.GetFileNameWithoutExtension(inputFile) + "." + ident;

                await File.WriteAllBytesAsync(Path.Combine(dumpFolder, $"{ident}.vert.spv"), reflected.Vertex.Code);
                await File.WriteAllBytesAsync(Path.Combine(dumpFolder, $"{ident}.frag.spv"), reflected.Fragment.Code);
                if (dxilCompiled != null)
                {
                    await File.WriteAllBytesAsync(Path.Combine(dumpFolder, $"{ident}.vert.dxil"),
                        dxilCompiled.Vertex.Code);
                    await File.WriteAllBytesAsync(Path.Combine(dumpFolder, $"{ident}.frag.dxil"),
                        dxilCompiled.Fragment.Code);
                }
                //Don't include terminating null in output
                await File.WriteAllTextAsync(Path.Combine(dumpFolder, $"{ident}.vert.msl"),
                    Encoding.UTF8.GetString(mslCompiled.Vertex.Code, 0, mslCompiled.Vertex.Code.Length - 1));
                await File.WriteAllTextAsync(Path.Combine(dumpFolder, $"{ident}.frag.msl"),
                    Encoding.UTF8.GetString(mslCompiled.Fragment.Code, 0, mslCompiled.Fragment.Code.Length - 1));
                if (!shader.NoLegacy)
                {
                    await File.WriteAllTextAsync(Path.Combine(dumpFolder, $"{ident}.vert.glsl"),
                        glCompiled!.VertexSource);
                    await File.WriteAllTextAsync(Path.Combine(dumpFolder, $"{ident}.frag.glsl"),
                        glCompiled!.FragmentSource);
                }
            }

            var idx = Array.IndexOf(permutations, permutation);
            allShaders[idx] = new BundledShader(permutation, reflected, dxilCompiled, mslCompiled, glCompiled);
        });

        // Alias the missing payloads of vulkan-only permutations to their
        // base permutation: ShaderBundle eagerly constructs every variant on
        // every backend, and GL input-table parsing reads the variant's own
        // GL blob - aliasing keeps both working with zero format changes.
        if (shader.VulkanOnlyMask != 0)
        {
            for (var i = 0; i < permutations.Length; i++)
            {
                if ((permutations[i] & shader.VulkanOnlyMask) == 0)
                {
                    continue;
                }
                var baseIdx = Array.IndexOf(permutations, permutations[i] & ~shader.VulkanOnlyMask);
                if (baseIdx < 0)
                {
                    throw new ShaderCompilerException(ShaderError.DescriptionInvalidError, shader.FriendlyName, 0, 0,
                        "vulkanfeature permutation has no base permutation");
                }
                var baseShader = allShaders[baseIdx];
                allShaders[i] = allShaders[i] with
                {
                    DXIL = baseShader.DXIL,
                    MSL = baseShader.MSL,
                    GL = baseShader.GL
                };
            }
        }

        if(verbose)
            Console.WriteLine($"Packing output to {outputFile}");
        using var bundle = File.Create(outputFile);
        ShaderBundleWriter.Write(bundle, new ShaderBundle() { Shaders = allShaders });
    }

    static async Task<(byte[] Vertex, byte[] Fragment)> CompileVariantSPIRV(ShaderInfo shader, List<string> defines,
        bool vulkanOnly = false)
    {
        var vertex = await DXC.CompileSPIRV(shader.VertexSource, ShaderStage.Vertex, defines, vulkanOnly);
        var fragment = await DXC.CompileSPIRV(shader.FragmentSource, ShaderStage.Fragment, defines, vulkanOnly);
        return (vertex, fragment);
    }
}
