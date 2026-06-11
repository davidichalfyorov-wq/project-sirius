using LibreLancer.Data.Ini;

namespace LibreLancer.Data.Schema.Universe;

[ParsedSection]
public partial class SystemBackground
{
    [Entry("basic_stars")]
    public string? BasicStarsPath;
    [Entry("complex_stars")]
    public string? ComplexStarsPath;
    [Entry("nebulae")]
    public string? NebulaePath;

    [Entry("basic_stars_cubemap")]
    public string? BasicStarsCubemapPath;
    [Entry("complex_stars_cubemap")]
    public string? ComplexStarsCubemapPath;
    [Entry("nebulae_cubemap")]
    public string? NebulaeCubemapPath;
}
