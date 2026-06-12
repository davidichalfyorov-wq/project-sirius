using System.Runtime.Versioning;

namespace LLShaderCompiler;

public record struct ShaderFeature(string Feature, uint Mask);
public class ShaderInfo
{
    public string FriendlyName = null!;
    public string VertexSource = null!;
    public string FragmentSource = null!;

    /// <summary>Mesh-pipeline bundle (roadmap 7.5): a mesh shader replaces
    /// the vertex stage. SPIR-V-only by construction - GL/DXIL/MSL payloads
    /// stay null and the bundle must be loaded behind a feature check.</summary>
    public string? MeshSource;
    public bool IsMesh => MeshSource != null;

    /// <summary>Compute bundle (roadmap phase 5): a single compute stage,
    /// SPIR-V-only like mesh bundles. Rides in the vertex slot of the
    /// container with EntryPoint "CSMain"; the fragment slot is a stub.</summary>
    public string? ComputeSource;
    public bool IsCompute => ComputeSource != null;
    public List<ShaderFeature> Features = new();
    public List<string> Defines = new();
    public bool NoLegacy = false;

    /// <summary>
    /// Feature bits that only exist in the SPIR-V/Vulkan payload (ray
    /// query etc. - untranslatable to GLSL/DXIL/MSL). Permutations
    /// intersecting this mask alias the base permutation's other payloads.
    /// </summary>
    public uint VulkanOnlyMask;

    private ShaderInfo()
    {
    }

    public static async Task<ShaderInfo> FromFile(string file)
    {
        var lines = await File.ReadAllLinesAsync(file);
        var si = new ShaderInfo();
        si.FriendlyName = Path.GetFileName(file);
        int vertexSourceLine = 0;
        int fragmentSourceLine = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            int hash;
            if ((hash = line.IndexOf('#')) != -1)
            {
                line = line.Substring(0, hash);
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            line = line.Trim();

            var endCommand = line.IndexOfAny(new[] { ' ', '\t' });
            if (endCommand == -1)
            {
                throw new ShaderCompilerException(ShaderError.DescriptionSyntaxError, si.FriendlyName, i + 1, 0, $"Invalid shader line '{line}'");
            }

            var command = line.Substring(0, endCommand);
            var arg = line.Substring(endCommand + 1).Trim();

            switch (command.ToLowerInvariant())
            {
                case "vertex":
                    if (!string.IsNullOrWhiteSpace(si.VertexSource))
                    {
                        throw new ShaderCompilerException(ShaderError.DescriptionInvalidError, si.FriendlyName, i + 1, 0, $"Duplicate vertex source");
                    }
                    vertexSourceLine = i + 1;
                    si.VertexSource = arg;
                    break;
                case "meshshader":
                    if (!string.IsNullOrWhiteSpace(si.MeshSource))
                    {
                        throw new ShaderCompilerException(ShaderError.DescriptionInvalidError, si.FriendlyName, i + 1, 0, $"Duplicate mesh source");
                    }
                    si.MeshSource = arg;
                    break;
                case "computeshader":
                    if (!string.IsNullOrWhiteSpace(si.ComputeSource))
                    {
                        throw new ShaderCompilerException(ShaderError.DescriptionInvalidError, si.FriendlyName, i + 1, 0, $"Duplicate compute source");
                    }
                    si.ComputeSource = arg;
                    break;
                case "fragment":
                    if (!string.IsNullOrWhiteSpace(si.FragmentSource))
                    {
                        throw new ShaderCompilerException(ShaderError.DescriptionInvalidError, si.FriendlyName, i + 1, 0, $"Duplicate fragment source");
                    }
                    fragmentSourceLine = i + 1;
                    si.FragmentSource = arg;
                    break;
                case "feature":
                case "vulkanfeature":
                    var featureLen = arg.IndexOfAny(new[] { ' ', '\t' });
                    if (featureLen == -1)
                    {
                        throw new ShaderCompilerException(ShaderError.DescriptionSyntaxError, si.FriendlyName, i + 1, 0, "Feature must list bit");
                    }
                    var feature = arg.Substring(0, featureLen);
                    var bit = arg.Substring(featureLen + 1).Trim();
                    if (!int.TryParse(bit, out var bitInt) ||
                        bitInt < 1 || bitInt > 32)
                    {
                        throw new ShaderCompilerException(ShaderError.DescriptionSyntaxError, si.FriendlyName, i + 1, 0, "Feature bit must be integer between 1 and 32");
                    }
                    var mask = 1U << (bitInt - 1);
                    si.Features.Add(new ShaderFeature(feature, mask));
                    if (command.ToLowerInvariant() == "vulkanfeature")
                    {
                        si.VulkanOnlyMask |= mask;
                    }
                    break;
                case "define":
                    si.Defines.Add(arg);
                    break;
                case "nolegacy":
                    si.NoLegacy = true;
                    break;
                default:
                    throw new ShaderCompilerException(ShaderError.DescriptionSyntaxError, si.FriendlyName, i + 1, 0, $"Unknown shader line");
            }
        }

        var dir = Path.GetDirectoryName(file) ?? "";

        if (si.IsCompute)
        {
            if (!string.IsNullOrWhiteSpace(si.VertexSource) || si.IsMesh ||
                !string.IsNullOrWhiteSpace(si.FragmentSource))
            {
                throw new ShaderCompilerException(ShaderError.DescriptionInvalidError, si.FriendlyName, 0, 0, $"compute bundle cannot also declare vertex/mesh/fragment sources");
            }
            si.ComputeSource = Path.Combine(dir, si.ComputeSource!);
            if (!File.Exists(si.ComputeSource))
            {
                throw new ShaderCompilerException(ShaderError.FileNotFound, si.FriendlyName, 0, 0, $"can't find '{si.ComputeSource}'");
            }
            return si;
        }

        if (si.IsMesh)
        {
            if (!string.IsNullOrWhiteSpace(si.VertexSource))
            {
                throw new ShaderCompilerException(ShaderError.DescriptionInvalidError, si.FriendlyName, 0, 0, $"mesh bundle cannot also declare a vertex source");
            }
            si.MeshSource = Path.Combine(dir, si.MeshSource!);
            if (!File.Exists(si.MeshSource))
            {
                throw new ShaderCompilerException(ShaderError.FileNotFound, si.FriendlyName, 0, 0, $"can't find '{si.MeshSource}'");
            }
        }
        else if (string.IsNullOrWhiteSpace(si.VertexSource))
        {
            throw new ShaderCompilerException(ShaderError.DescriptionInvalidError, si.FriendlyName, 0, 0, $"missing vertex source");
        }

        if (!si.IsMesh)
        {
            si.VertexSource = Path.Combine(dir, si.VertexSource);
            if (!File.Exists(si.VertexSource))
            {
                throw new ShaderCompilerException(ShaderError.FileNotFound, si.FriendlyName, vertexSourceLine, 0, $"can't find '{si.VertexSource}'");
            }
        }

        if (string.IsNullOrWhiteSpace(si.FragmentSource))
        {
            throw new ShaderCompilerException(ShaderError.DescriptionInvalidError, si.FriendlyName, 0, 0, $"missing fragment source");
        }

        si.FragmentSource = Path.Combine(dir, si.FragmentSource);
        if (!File.Exists(si.FragmentSource))
        {
            throw new ShaderCompilerException(ShaderError.FileNotFound, si.FriendlyName, fragmentSourceLine, 0, $"can't find '{si.FragmentSource}'");
        }
        return si;
    }
}
