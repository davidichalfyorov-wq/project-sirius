using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LibreLancer.Graphics.Backends.Vulkan;

/// <summary>
/// A vertex+fragment program created from the bundle's SPIR-V payload.
/// Owns the shader modules, the per-set descriptor layouts derived from
/// real SPIR-V bindings and the pipeline layout. Uniform block contents
/// live CPU-side here and are streamed into the frame's uniform ring by
/// the context when a draw is recorded.
/// </summary>
internal unsafe class VKShader : IShader
{
    // Descriptor set conventions baked into the shader compiler:
    public const int SetVertexTextures = 0;
    public const int SetVertexUniforms = 1;
    public const int SetFragmentTextures = 2;
    public const int SetFragmentUniforms = 3;
    public const int SetCount = 4;

    public ulong VertexModule;
    public ulong FragmentModule;

    /// <summary>True when the bundle's first stage is a mesh shader
    /// (EntryPoint marker "MSMain" written by the compiler) - the vertex
    /// module slot then holds a mesh module and pipelines are built
    /// without vertex input state.</summary>
    public readonly bool IsMeshPipeline;

    /// <summary>True when the bundle is a single compute stage (EntryPoint
    /// marker "CSMain"). The vertex module slot holds the compute module,
    /// there is no fragment module, and the shader binds at the compute
    /// pipeline bind point via DispatchCompute.</summary>
    public readonly bool IsComputePipeline;
    public readonly ulong[] SetLayouts = new ulong[SetCount];
    public ulong PipelineLayout;
    public readonly List<SpirvResource> Resources = new();

    /// <summary>
    /// Uniform blocks in (set, binding) ascending order - the order
    /// vkCmdBindDescriptorSets consumes pDynamicOffsets in. Uniform
    /// descriptors are UNIFORM_BUFFER_DYNAMIC: the sets bind the frame's
    /// ring buffer once and each draw only supplies fresh offsets.
    /// </summary>
    public readonly List<SpirvResource> UniformBlocks = new();

    /// <summary>Vertex input locations the SPIR-V module consumes.</summary>
    public System.Collections.Generic.IEnumerable<uint> VertexInputLocations
    {
        get
        {
            foreach (var resource in Resources)
            {
                if (resource.IsVertexInput)
                {
                    yield return resource.Binding;
                }
            }
        }
    }
    public readonly int Id;
    private static int nextId;

    private const int MaxBlocks = 32;
    private readonly byte[]?[] blockData = new byte[MaxBlocks][];
    private readonly int[] blockSizes = new int[MaxBlocks];
    private readonly ulong[] blockTags = new ulong[MaxBlocks];
    private readonly bool[] blockPresent = new bool[MaxBlocks];

    public VKShader(IntPtr device, ReadOnlySpan<byte> program)
    {
        Id = nextId++;
        var entryPoint = PeekEntryPoint(program);
        IsMeshPipeline = entryPoint == "MSMain";
        IsComputePipeline = entryPoint == "CSMain";
        var inputLocations = IsMeshPipeline || IsComputePipeline ? null : ReadInputTable(program);
        var blob = ShaderBytecodes.GetSPIRV(program);
        ReadStage(device, ref blob, isVertex: true, inputLocations);
        if (!IsComputePipeline)
        {
            // Compute bundles carry a zero-length fragment stub - a
            // VkShaderModule with codeSize 0 is invalid, skip it.
            ReadStage(device, ref blob, isVertex: false, null);
        }

        foreach (var resource in Resources)
        {
            if (!resource.IsVertexInput &&
                resource.Kind == SpirvResourceKind.UniformBuffer && resource.Binding < MaxBlocks)
            {
                blockPresent[resource.Binding] = true;
                UniformBlocks.Add(resource);
            }
        }
        UniformBlocks.Sort((a, b) => a.Set != b.Set
            ? a.Set.CompareTo(b.Set)
            : a.Binding.CompareTo(b.Binding));
        // maxDescriptorSetUniformBuffersDynamic is a PIPELINE LAYOUT limit
        // (sum over all sets): the spec floor is 8, NVIDIA exposes 15.
        // The shadow blocks pushed the lit shaders past 8; warn for
        // low-floor hardware, hard-stop at the NVIDIA limit.
        if (UniformBlocks.Count > 15)
        {
            throw new InvalidOperationException(
                $"Shader {Id} declares {UniformBlocks.Count} uniform blocks; dynamic descriptor limit is 15");
        }
        if (UniformBlocks.Count > 8)
        {
            FLLog.Debug("Vulkan", $"Shader {Id}: {UniformBlocks.Count} dynamic uniform blocks (spec floor is 8)");
        }

        CreateLayouts(device);
        if (IsMeshPipeline || IsComputePipeline)
        {
            var resourceList = string.Join("; ", Resources.ConvertAll(r =>
                $"{r.Kind} s{r.Set}b{r.Binding} {r.Name}"));
            FLLog.Info("Vulkan", $"{(IsComputePipeline ? "Compute" : "Mesh")} shader {Id}: {resourceList}");
        }
    }

    private static string PeekEntryPoint(ReadOnlySpan<byte> program)
    {
        var blob = ShaderBytecodes.GetSPIRV(program);
        var reader = new SpanReader(blob);
        for (var i = 0; i < 4; i++)
        {
            reader.ReadVarUInt32();
        }
        return reader.ReadUTF8();
    }

    /// <summary>
    /// The bundle's GL blob carries (location, attributeName) pairs that
    /// GLShader feeds to glBindAttribLocation - the authoritative
    /// name-to-VertexSlot contract, reused here to repatch SPIR-V input
    /// locations (DXC numbers them by declaration order).
    /// </summary>
    private static System.Collections.Generic.Dictionary<string, uint> ReadInputTable(
        ReadOnlySpan<byte> program)
    {
        var table = new System.Collections.Generic.Dictionary<string, uint>();
        var gl = ShaderBytecodes.GetGLSL(program);
        var reader = new SpanReader(gl.Slice(4)); // skip "\0GL\0"
        var count = reader.ReadVarUInt32();
        for (var i = 0; i < count; i++)
        {
            var location = reader.ReadVarUInt32();
            var name = reader.ReadUTF8();
            table[name] = location;
        }
        return table;
    }

    private void ReadStage(IntPtr device, ref ReadOnlySpan<byte> blob, bool isVertex,
        System.Collections.Generic.Dictionary<string, uint>? inputLocations)
    {
        var reader = new SpanReader(blob);
        for (var i = 0; i < 4; i++)
        {
            reader.ReadVarUInt32();
        }
        reader.ReadUTF8();
        var codeLength = (int)reader.ReadVarUInt32();
        var code = blob.Slice(reader.Offset, codeLength);

        var words = new uint[codeLength / 4];
        MemoryMarshal.Cast<byte, uint>(code).CopyTo(words);
        Resources.AddRange(VKSpirv.PatchAndReflect(words, inputLocations, collectInputs: isVertex));

        fixed (uint* pWords = words)
        {
            var createInfo = new VkShaderModuleCreateInfo
            {
                SType = VkStructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)codeLength,
                PCode = pWords
            };
            ulong module;
            Vk.Check(Vk.CreateShaderModule(device, &createInfo, null, &module), "vkCreateShaderModule");
            if (isVertex)
            {
                VertexModule = module;
            }
            else
            {
                FragmentModule = module;
            }
        }

        blob = blob.Slice(reader.Offset + codeLength);
    }

    internal static int DescriptorType(SpirvResourceKind kind) => kind switch
    {
        SpirvResourceKind.Sampler => 0,       // VK_DESCRIPTOR_TYPE_SAMPLER
        SpirvResourceKind.SampledImage => 2,  // VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE
        SpirvResourceKind.StorageImage => 3,  // VK_DESCRIPTOR_TYPE_STORAGE_IMAGE
        SpirvResourceKind.UniformBuffer => 8, // VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC
        SpirvResourceKind.AccelerationStructure => 1000150000, // ..._ACCELERATION_STRUCTURE_KHR
        _ => 7                                // VK_DESCRIPTOR_TYPE_STORAGE_BUFFER
    };

    private void CreateLayouts(IntPtr device)
    {
        // The 2N/2N+1 de-alias rule maps tN and uN registers to the SAME
        // binding (2N): authoring rule says t/u indices must not collide
        // within one shader. Catch violations at load instead of letting
        // the validation layer report a confusing duplicate-binding error.
        var seen = new Dictionary<(uint Set, uint Binding), string>();
        foreach (var resource in Resources)
        {
            if (resource.IsVertexInput)
            {
                continue;
            }
            var key = (resource.Set, resource.Binding);
            if (seen.TryGetValue(key, out var other))
            {
                throw new InvalidOperationException(
                    $"Shader {Id}: descriptor collision at set {resource.Set} binding {resource.Binding}: " +
                    $"'{other}' vs '{resource.Name}' (t- and u-register indices must not overlap)");
            }
            seen[key] = resource.Name;
        }

        for (var set = 0; set < SetCount; set++)
        {
            var bindings = new List<VkDescriptorSetLayoutBinding>();
            // Mesh/compute bundles bind the first-stage sets to their stage.
            var firstStage = IsComputePipeline ? 0x20u : IsMeshPipeline ? 0x80u : 1u;
            var stage = set is SetVertexTextures or SetVertexUniforms ? firstStage : 16u;
            foreach (var resource in Resources)
            {
                if (resource.Set != set || resource.IsVertexInput)
                {
                    continue;
                }
                bindings.Add(new VkDescriptorSetLayoutBinding
                {
                    Binding = resource.Binding,
                    DescriptorType = DescriptorType(resource.Kind),
                    DescriptorCount = 1,
                    StageFlags = stage
                });
            }

            var bindingArray = bindings.ToArray();
            fixed (VkDescriptorSetLayoutBinding* pBindings = bindingArray)
            {
                var layoutInfo = new VkDescriptorSetLayoutCreateInfo
                {
                    SType = VkStructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = (uint)bindingArray.Length,
                    PBindings = pBindings
                };
                ulong layout;
                Vk.Check(Vk.CreateDescriptorSetLayout(device, &layoutInfo, null, &layout),
                    "vkCreateDescriptorSetLayout");
                SetLayouts[set] = layout;
            }
        }

        fixed (ulong* pSetLayouts = SetLayouts)
        {
            var layoutInfo = new VkPipelineLayoutCreateInfo
            {
                SType = VkStructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = SetCount,
                PSetLayouts = pSetLayouts
            };
            ulong pipelineLayout;
            Vk.Check(Vk.CreatePipelineLayout(device, &layoutInfo, null, &pipelineLayout),
                "vkCreatePipelineLayout");
            PipelineLayout = pipelineLayout;
        }
    }

    public ReadOnlySpan<byte> BlockData(int index) =>
        blockData[index] is { } data ? data.AsSpan(0, blockSizes[index]) : default;

    public void SetUniformBlock<T>(int index, ref T data, bool forceUpdate = false, int forceSize = -1)
        where T : unmanaged
    {
        var size = forceSize > 0 ? forceSize : Unsafe.SizeOf<T>();
        var storage = blockData[index];
        if (storage == null || storage.Length < size)
        {
            blockData[index] = storage = new byte[size];
        }
        // forceSize may truncate the struct (lights array sized to the
        // actual light count): copy exactly `size` bytes.
        MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref data, 1))
            .Slice(0, size).CopyTo(storage);
        blockSizes[index] = size;
    }

    public bool HasUniformBlock(int index) => index < MaxBlocks && blockPresent[index];

    public ref ulong UniformBlockTag(int index) => ref blockTags[index];
}
