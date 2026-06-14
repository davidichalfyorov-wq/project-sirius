using System;
using System.Collections.Generic;

namespace LibreLancer.Graphics.Backends.Vulkan;

internal enum SpirvResourceKind
{
    SampledImage,
    Sampler,
    UniformBuffer,
    StorageBuffer,
    AccelerationStructure,
    StorageImage
}

internal record struct SpirvResource(string Name, SpirvResourceKind Kind, uint Set, uint Binding,
    bool IsCube = false, bool IsVertexInput = false, bool Is3D = false);

/// <summary>
/// Minimal SPIR-V introspection + binding patcher.
///
/// DXC under the SDL_GPU convention gives a texture and its sampler the
/// SAME binding number (t0/s0 -> binding 0) - invalid for raw Vulkan where
/// one binding holds one descriptor type. This walks the module, reads the
/// real (set, binding, kind, name) of every resource and rewrites image
/// bindings to 2N and sampler bindings to 2N+1 in place.
/// </summary>
internal static class VKSpirv
{
    private const ushort OpName = 5;
    private const ushort OpTypeImage = 25;
    private const ushort OpTypeSampler = 26;
    private const ushort OpTypeSampledImage = 27;
    private const ushort OpTypeAccelerationStructureKHR = 5341;
    private const ushort OpTypePointer = 32;
    private const ushort OpVariable = 59;
    private const ushort OpDecorate = 71;
    private const uint DecorationBufferBlock = 3;
    private const uint DecorationLocation = 30;
    private const uint DecorationBinding = 33;
    private const uint DecorationDescriptorSet = 34;
    private const uint StorageClassInput = 1;
    private const uint StorageClassUniform = 2;
    private const uint StorageClassUniformConstant = 0;
    private const uint StorageClassStorageBuffer = 12;

    /// <param name="words">SPIR-V module, patched in place.</param>
    /// <param name="inputLocations">Vertex-input remap (attribute name from
    /// the bundle's GL table -> engine VertexSlot). DXC numbers input
    /// locations by declaration order; the engine binds buffers by slot, so
    /// the module's locations are rewritten to match - the exact mirror of
    /// what GLShader does with glBindAttribLocation.</param>
    public static List<SpirvResource> PatchAndReflect(uint[] words,
        Dictionary<string, uint>? inputLocations = null, bool collectInputs = false)
    {
        var names = new Dictionary<uint, string>();
        var pointerTargets = new Dictionary<uint, uint>(); // pointer type -> pointee type
        var typeKinds = new Dictionary<uint, SpirvResourceKind?>();
        var variables = new Dictionary<uint, (uint TypeId, uint StorageClass)>();
        var setDecorations = new Dictionary<uint, uint>();
        var cubeTypes = new HashSet<uint>();
        var threeDTypes = new HashSet<uint>();
        var bufferBlockTypes = new HashSet<uint>();
        var bindingOffsets = new Dictionary<uint, int>(); // variable id -> word index of binding VALUE
        var locationOffsets = new Dictionary<uint, int>(); // variable id -> word index of location VALUE

        var i = 5;
        while (i < words.Length)
        {
            var wordCount = (int)(words[i] >> 16);
            var op = (ushort)(words[i] & 0xFFFF);
            switch (op)
            {
                case OpName:
                {
                    var bytes = new byte[(wordCount - 2) * 4];
                    Buffer.BlockCopy(words, (i + 2) * 4, bytes, 0, bytes.Length);
                    var zero = Array.IndexOf(bytes, (byte)0);
                    names[words[i + 1]] = System.Text.Encoding.UTF8.GetString(bytes, 0, zero < 0 ? bytes.Length : zero);
                    break;
                }
                case OpTypeImage:
                    // Operand 7 (Sampled): 1 = sampled, 2 = storage (UAV).
                    typeKinds[words[i + 1]] = words[i + 7] == 2
                        ? SpirvResourceKind.StorageImage
                        : SpirvResourceKind.SampledImage;
                    if (words[i + 7] == 2 && words[i + 8] == 0)
                    {
                        // Format Unknown on a storage image needs the
                        // shaderStorageImageWriteWithoutFormat feature -
                        // authoring rule: annotate with [[vk::image_format]].
                        FLLog.Warning("Vulkan",
                            "Storage image declared without [[vk::image_format]] annotation (format=Unknown)");
                    }
                    if (words[i + 3] == 3) // Dim == Cube
                    {
                        cubeTypes.Add(words[i + 1]);
                    }
                    else if (words[i + 3] == 2) // Dim == 3D
                    {
                        threeDTypes.Add(words[i + 1]);
                    }
                    break;
                case OpTypeSampler:
                    typeKinds[words[i + 1]] = SpirvResourceKind.Sampler;
                    break;
                case OpTypeSampledImage:
                    typeKinds[words[i + 1]] = SpirvResourceKind.SampledImage;
                    if (cubeTypes.Contains(words[i + 2]))
                    {
                        cubeTypes.Add(words[i + 1]);
                    }
                    else if (threeDTypes.Contains(words[i + 2]))
                    {
                        threeDTypes.Add(words[i + 1]);
                    }
                    break;
                case OpTypeAccelerationStructureKHR:
                    typeKinds[words[i + 1]] = SpirvResourceKind.AccelerationStructure;
                    break;
                case OpTypePointer:
                    pointerTargets[words[i + 1]] = words[i + 3];
                    break;
                case OpVariable:
                    variables[words[i + 2]] = (words[i + 1], words[i + 3]);
                    break;
                case OpDecorate when words[i + 2] == DecorationBufferBlock:
                    bufferBlockTypes.Add(words[i + 1]);
                    break;
                case OpDecorate when words[i + 2] == DecorationDescriptorSet:
                    setDecorations[words[i + 1]] = words[i + 3];
                    break;
                case OpDecorate when words[i + 2] == DecorationBinding:
                    bindingOffsets[words[i + 1]] = i + 3;
                    break;
                case OpDecorate when words[i + 2] == DecorationLocation:
                    locationOffsets[words[i + 1]] = i + 3;
                    break;
            }
            i += wordCount;
        }

        var resources = new List<SpirvResource>();
        foreach (var (id, (_, storageClass)) in variables)
        {
            if (collectInputs && storageClass == StorageClassInput &&
                locationOffsets.TryGetValue(id, out var locIndex))
            {
                resources.Add(new SpirvResource(
                    names.GetValueOrDefault(id, $"id{id}"),
                    SpirvResourceKind.SampledImage, // kind unused for inputs
                    0, words[locIndex], false, IsVertexInput: true));
            }
        }

        if (inputLocations != null)
        {
            foreach (var (id, (_, storageClass)) in variables)
            {
                if (storageClass == StorageClassInput &&
                    locationOffsets.TryGetValue(id, out var locationIndex) &&
                    names.TryGetValue(id, out var name) &&
                    inputLocations.TryGetValue(name, out var slot))
                {
                    words[locationIndex] = slot;
                }
            }
        }

        foreach (var (id, (pointerType, storageClass)) in variables)
        {
            if (!bindingOffsets.TryGetValue(id, out var bindingIndex))
            {
                continue;
            }

            // DXC emits StructuredBuffer as Uniform-class + BufferBlock
            // (legacy SPIR-V style): a storage buffer, not a uniform block.
            var kind = storageClass switch
            {
                StorageClassUniform when IsBufferBlock(pointerType) => SpirvResourceKind.StorageBuffer,
                StorageClassUniform => SpirvResourceKind.UniformBuffer,
                StorageClassStorageBuffer => SpirvResourceKind.StorageBuffer,
                StorageClassUniformConstant when ResolveKind(pointerType) is { } resolved => resolved,
                _ => (SpirvResourceKind?)null
            };
            if (kind == null)
            {
                continue;
            }

            var binding = words[bindingIndex];
            if (kind is SpirvResourceKind.SampledImage or SpirvResourceKind.Sampler
                or SpirvResourceKind.AccelerationStructure or SpirvResourceKind.StorageImage)
            {
                // De-alias the shared SDL_GPU-style slot (AS and storage
                // images ride the image rule: tN/uN -> binding 2N).
                // Authoring rule: t- and u-register indices must not collide
                // within one shader (guarded at VKShader.CreateLayouts).
                binding = kind == SpirvResourceKind.Sampler ? binding * 2 + 1 : binding * 2;
                words[bindingIndex] = binding;
            }

            var isCube = pointerTargets.TryGetValue(pointerType, out var pointee) &&
                         cubeTypes.Contains(pointee);
            var is3D = pointerTargets.TryGetValue(pointerType, out var pointee3) &&
                       threeDTypes.Contains(pointee3);
            resources.Add(new SpirvResource(
                names.GetValueOrDefault(id, $"id{id}"),
                kind.Value,
                setDecorations.GetValueOrDefault(id, 0u),
                binding,
                isCube,
                Is3D: is3D));
        }

        return resources;

        SpirvResourceKind? ResolveKind(uint pointerType) =>
            pointerTargets.TryGetValue(pointerType, out var pointee) &&
            typeKinds.TryGetValue(pointee, out var kind)
                ? kind
                : null;

        bool IsBufferBlock(uint pointerType) =>
            pointerTargets.TryGetValue(pointerType, out var pointee) &&
            bufferBlockTypes.Contains(pointee);
    }
}
