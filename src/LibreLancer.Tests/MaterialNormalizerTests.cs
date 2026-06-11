using System.Numerics;
using LibreLancer.Render.Materials;
using Xunit;

namespace LibreLancer.Tests;

public class MaterialNormalizerTests
{
    [Fact]
    public void MetallicDefaultsToDielectricWithoutMapOrFactor()
    {
        Assert.Equal(0.0f, MaterialNormalizer.Metallic(null, hasMetallicMap: false));
    }

    [Fact]
    public void MetallicMapDrivesTheChannelWhenPresent()
    {
        Assert.Equal(1.0f, MaterialNormalizer.Metallic(null, hasMetallicMap: true));
    }

    [Fact]
    public void ExplicitFactorsAlwaysWin()
    {
        Assert.Equal(0.25f, MaterialNormalizer.Metallic(0.25f, hasMetallicMap: true));
        Assert.Equal(0.1f, MaterialNormalizer.Roughness(0.1f, hasRoughnessMap: true));
    }

    [Fact]
    public void RoughnessDefaultsToPaintedHull()
    {
        Assert.Equal(0.65f, MaterialNormalizer.Roughness(null, hasRoughnessMap: false));
    }

    // Shader-mirror of the local light attenuation (roadmap 5.2 bug audit):
    // the distance must be measured BEFORE normalization, otherwise it
    // collapses to 1 and lights never fall off.
    private static float Attenuation(Vector3 lightPos, Vector3 surface, Vector3 curve)
    {
        var lightVector = lightPos - surface;
        var distance = lightVector.Length();
        return 1.0f / (curve.X + curve.Y * distance + curve.Z * distance * distance);
    }

    [Fact]
    public void PointLightAttenuationFallsOffWithDistance()
    {
        var curve = new Vector3(0, 0, 1); // pure quadratic
        var near = Attenuation(new Vector3(0, 0, 10), Vector3.Zero, curve);
        var far = Attenuation(new Vector3(0, 0, 100), Vector3.Zero, curve);
        Assert.Equal(1.0f / 100.0f, near, 6);
        Assert.Equal(1.0f / 10000.0f, far, 8);
        Assert.True(near > far * 50);
    }
}
