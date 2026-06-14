using LibreLancer.Render.Volumetrics;
using Xunit;

namespace LibreLancer.Tests.Graphics;

public class VolumetricAssetPathResolverTests
{
    [Fact]
    public void AddsDataPathFallbackForShortVolumePath()
    {
        var candidates = VolumetricAssetPathResolver.BuildVfsCandidates(
            @"volumes\openvdb\li01_badlands.siriusvol.manifest",
            "DATA/");

        Assert.Equal([
            "volumes/openvdb/li01_badlands.siriusvol.manifest",
            "DATA/volumes/openvdb/li01_badlands.siriusvol.manifest"
        ], candidates);
    }

    [Fact]
    public void KeepsDataPrefixedPathSingle()
    {
        var candidates = VolumetricAssetPathResolver.BuildVfsCandidates(
            "DATA/volumes/openvdb/li01_badlands.siriusvol.manifest",
            "DATA/");

        Assert.Equal([
            "DATA/volumes/openvdb/li01_badlands.siriusvol.manifest"
        ], candidates);
    }

    [Fact]
    public void UsesDefaultDataPathWhenGameDataIsUnavailable()
    {
        var candidates = VolumetricAssetPathResolver.BuildVfsCandidates(
            "volumes/openvdb/li01_badlands.siriusvol.manifest",
            null);

        Assert.Equal([
            "volumes/openvdb/li01_badlands.siriusvol.manifest",
            "DATA/volumes/openvdb/li01_badlands.siriusvol.manifest"
        ], candidates);
    }
}
