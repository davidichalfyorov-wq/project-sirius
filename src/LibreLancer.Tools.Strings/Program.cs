using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LibreLancer.Data;
using LibreLancer.Data.IO;
using LibreLancer.Data.Schema;

static int Usage()
{
    Console.Error.WriteLine("Usage: dotnet run --project src/LibreLancer.Tools.Strings -- <freelancer-root> [--output <DATA/strings.json>]");
    Console.Error.WriteLine("Example: dotnet run --project src/LibreLancer.Tools.Strings -- \"/games/Discovery Freelancer 4.86.0\" --output \"/games/Discovery Freelancer 4.86.0/DATA/strings.json\"");
    return 2;
}

if (args.Length < 1)
{
    return Usage();
}

var root = args[0];
var output = Path.Combine(root, "DATA", "strings.json");
for (var i = 1; i < args.Length; i++)
{
    if (args[i] == "--output" && i + 1 < args.Length)
    {
        output = args[++i];
    }
    else
    {
        return Usage();
    }
}

try
{
    var vfs = FileSystem.FromPath(root);
    FreelancerIni ini;
    if (vfs.FileExists(@"EXE\freelancer.ini"))
    {
        ini = new FreelancerIni(@"EXE\freelancer.ini", vfs);
    }
    else
    {
        ini = new FreelancerIni(vfs);
    }

    var manager = new InfocardManager(ini.Resources);
    var strings = manager.AllStrings.ToDictionary(x => x.Key.ToString(), x => x.Value);
    var infocards = manager.AllXml.ToDictionary(x => x.Key.ToString(), x => x.Value);
    var payload = new SortedDictionary<string, object?>
    {
        ["strings"] = strings,
        ["infocards"] = infocards
    };

    var outputDir = Path.GetDirectoryName(Path.GetFullPath(output));
    if (!string.IsNullOrEmpty(outputDir))
    {
        Directory.CreateDirectory(outputDir);
    }

    await File.WriteAllTextAsync(output, JsonSerializer.Serialize(payload, new JsonSerializerOptions
    {
        WriteIndented = true
    }));

    Console.WriteLine($"Wrote {strings.Count} strings and {infocards.Count} infocards to {output}");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine(e);
    return 1;
}
