using System;
using System.IO;
using LibreLancer.Fx;
using LibreLancer.Utf.Ale;
using Xunit;

namespace LibreLancer.Tests;

public class IntroVolcanoFxTests
{
    [Fact]
    public void IntroVolcanoAleEffectsResolveByNameWhenIniCrcMismatches()
    {
        var alePath = FindRepoFile(
            "Discovery Freelancer 4.86.0",
            "DATA",
            "FX",
            "MISC",
            "intro_volcanoplanet.ale");

        if (alePath == null)
            return;

        using var stream = File.OpenRead(alePath);
        var library = new ParticleLibrary(null!, new AleFile(alePath, stream));

        Assert.NotNull(library.FindEffect(0xDEADBEEFu, "Intro_volcanoplanet_gf_volcanicglow"));
        Assert.NotNull(library.FindEffect(0xDEADBEEFu, "Intro_volcanoplanet_planetstorm"));
        Assert.NotNull(library.FindEffect(0xDEADBEEFu, "Intro_volcanoplanet_sun"));

        Assert.NotNull(library.FindEffect(unchecked((uint)-334022224), "Intro_volcanoplanet_gf_volcanicglow"));
        Assert.NotNull(library.FindEffect(unchecked((uint)-8353882), "Intro_volcanoplanet_planetstorm"));
        Assert.NotNull(library.FindEffect(unchecked((uint)-175242615), "Intro_volcanoplanet_sun"));
    }

    private static string? FindRepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = dir.FullName;
            foreach (var part in parts)
                path = Path.Combine(path, part);

            if (File.Exists(path))
                return path;
        }

        return null;
    }
}
