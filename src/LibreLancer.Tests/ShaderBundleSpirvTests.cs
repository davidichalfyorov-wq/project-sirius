using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace LibreLancer.Tests;

/// <summary>
/// Validates the SPIR-V payload of every compiled shader bundle shipped in
/// the LibreLancer assembly. The GL backend only consumes the GLSL blob, so
/// SPIR-V corruption stays invisible until the Vulkan backend reads it -
/// this test reads the bundles exactly the way that backend will.
/// </summary>
public class ShaderBundleSpirvTests
{
    private const ulong BundleSignature = 0x524448534C4C0008; // \b\0LLSHDR
    private const uint ShaderSignature = 0x72646873; // "shdr"
    private const uint SpirvMagic = 0x07230203;

    public static IEnumerable<object[]> BundleNames() =>
        typeof(FreelancerGame).Assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            .Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(BundleNames))]
    public void EveryShaderCarriesValidSpirvForBothStages(string resourceName)
    {
        using var stream = typeof(FreelancerGame).Assembly.GetManifestResourceStream(resourceName)!;
        using var decomp = new ZstdSharp.DecompressionStream(stream);
        var reader = new BinaryReader(decomp);

        Assert.Equal(BundleSignature, reader.ReadUInt64());
        Assert.Equal(1u, reader.ReadUInt32());
        reader.ReadUInt32(); // feature mask
        var count = reader.ReadInt32();
        Assert.True(count > 0, $"{resourceName}: empty bundle");

        var entries = new (uint Features, int Offset, int Length)[count];
        var total = 0;
        for (var i = 0; i < count; i++)
        {
            entries[i] = (reader.ReadUInt32(), reader.ReadInt32(), reader.ReadInt32());
            total += entries[i].Length;
        }

        var data = reader.ReadBytes(total);
        var dataOffset = 20 + count * 12;
        foreach (var entry in entries)
        {
            var shader = data.AsSpan(entry.Offset - dataOffset, entry.Length);
            var spirv = ExtractSpirvBlob(resourceName, shader);
            VerifyStage(resourceName, ref spirv, "vertex");
            VerifyStage(resourceName, ref spirv, "fragment");
            Assert.True(spirv.IsEmpty, $"{resourceName}: trailing bytes after fragment stage");
        }
    }

    private static ReadOnlySpan<byte> ExtractSpirvBlob(string name, ReadOnlySpan<byte> shader)
    {
        // shdr header: magic, version, spirvLen, dxilLen, mslLen, glLen
        Assert.Equal(ShaderSignature, BitConverter.ToUInt32(shader.Slice(0, 4)));
        Assert.Equal(1u, BitConverter.ToUInt32(shader.Slice(4, 4)));
        var spirvLength = BitConverter.ToUInt32(shader.Slice(8, 4));
        Assert.True(spirvLength > 0, $"{name}: shader has no SPIR-V payload");
        return shader.Slice(24, (int)spirvLength);
    }

    private static void VerifyStage(string name, ref ReadOnlySpan<byte> blob, string stage)
    {
        for (var i = 0; i < 4; i++)
        {
            ReadVarUInt(ref blob); // samplers, storage textures/buffers, UBOs
        }

        var entryPointLength = (int)ReadVarUInt(ref blob);
        var entryPoint = Encoding.UTF8.GetString(blob.Slice(0, entryPointLength));
        blob = blob.Slice(entryPointLength);
        Assert.False(string.IsNullOrEmpty(entryPoint), $"{name}/{stage}: empty entry point");

        var codeLength = (int)ReadVarUInt(ref blob);
        Assert.True(codeLength > 20, $"{name}/{stage}: SPIR-V too small ({codeLength} bytes)");
        var magic = BitConverter.ToUInt32(blob.Slice(0, 4));
        Assert.True(magic == SpirvMagic,
            $"{name}/{stage}: bad SPIR-V magic 0x{magic:X8} (corrupt stage payload)");
        blob = blob.Slice(codeLength);
    }

    // Mirror of the engine's biased varuint (SpanReader.ReadVarUInt32):
    // multi-byte values carry a range offset, unlike plain LEB128.
    private static uint ReadVarUInt(ref ReadOnlySpan<byte> blob)
    {
        int b = blob[0];
        blob = blob.Slice(1);
        var a = (uint)(b & 0x7f);
        var extraCount = 0;
        for (var shift = 7; (b & 0x80) == 0x80 && shift <= 28; shift += 7)
        {
            b = blob[0];
            blob = blob.Slice(1);
            a |= (uint)(b & (shift == 28 ? 0xf : 0x7f)) << shift;
            extraCount++;
        }
        return a + extraCount switch { 1 => 128u, 2 => 16512u, 3 => 2113663u, _ => 0u };
    }
}
