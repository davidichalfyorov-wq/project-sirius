using System.IO;
using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricBlueNoiseAssetTests
{
    [Fact]
    public void ParsesManifestWithRelativeRawPath()
    {
        var text = """
                   data = stbn.raw
                   width = 64
                   height = 64
                   format = rgba8
                   frames = 16
                   stbn = true
                   source = test-stbn
                   """;

        Assert.True(VolumetricBlueNoiseAsset.TryParseManifest(text, "/tmp/noise", out var desc, out var error), error);
        Assert.EndsWith("/tmp/noise/stbn.raw", desc.DataPath.Replace('\\', '/'));
        Assert.Equal(64, desc.Width);
        Assert.Equal(64, desc.Height);
        Assert.Equal(VolumetricBlueNoiseAssetFormat.Rgba8, desc.Format);
        Assert.Equal(16, desc.Frames);
        Assert.True(desc.IsStbn);
        Assert.Equal("test-stbn", desc.SourceName);
    }

    [Theory]
    [InlineData("/tmp/stbn.raw")]
    [InlineData("C:/temp/stbn.raw")]
    [InlineData("../stbn.raw")]
    [InlineData("noise/../stbn.raw")]
    [InlineData("noise\\stbn.raw")]
    public void RejectsUnsafeRawDataPaths(string dataPath)
    {
        var text = $"""
                    data = {dataPath}
                    width = 64
                    height = 64
                    format = rgba8
                    """;

        Assert.False(VolumetricBlueNoiseAsset.TryParseManifest(text, "/tmp/noise", out _, out var error));
        Assert.Contains("data path", error);
    }

    [Fact]
    public void RgbaPayloadConvertsToEnginePackedBgraUint()
    {
        using var dir = new TempDir();
        var raw = Path.Combine(dir.Path, "noise.raw");
        File.WriteAllBytes(raw, new byte[] { 10, 20, 30, 40, 1, 2, 3, 4 });
        var manifest = Path.Combine(dir.Path, "noise.manifest");
        File.WriteAllText(manifest, """
            data = noise.raw
            width = 2
            height = 1
            format = rgba8
            source = unit-test
            """);

        Assert.True(VolumetricBlueNoiseAsset.TryLoad(manifest, out var asset, out var error), error);
        var packed = asset!.ToBgraPacked();
        Assert.Equal(0x281E140Au, packed[0]);
        Assert.Equal(0x04030201u, packed[1]);
    }

    [Fact]
    public void R8PayloadExpandsToOpaqueGrayscale()
    {
        using var dir = new TempDir();
        var raw = Path.Combine(dir.Path, "noise.r8");
        File.WriteAllBytes(raw, new byte[] { 0, 127, 255, 64 });
        var manifest = Path.Combine(dir.Path, "noise.manifest");
        File.WriteAllText(manifest, """
            data = noise.r8
            width = 2
            height = 2
            format = r8
            """);

        Assert.True(VolumetricBlueNoiseAsset.TryLoad(manifest, out var asset, out var error), error);
        var packed = asset!.ToBgraPacked();
        Assert.Equal(0xFF000000u, packed[0]);
        Assert.Equal(0xFF7F7F7Fu, packed[1]);
        Assert.Equal(0xFFFFFFFFu, packed[2]);
        Assert.Equal(0xFF404040u, packed[3]);
    }

    private sealed class TempDir : System.IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ll_stbn_" + System.Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
