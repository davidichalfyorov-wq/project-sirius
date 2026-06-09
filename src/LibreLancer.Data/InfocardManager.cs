// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using LibreLancer.Data.Dll;

namespace LibreLancer.Data;

public class InfocardManager
{
    public List<ResourceDll> Dlls;
    private readonly Dictionary<int, string> extraStrings = [];
    private readonly Dictionary<int, string> extraInfocards = [];

    public InfocardManager (List<ResourceDll>? res)
    {
        Dlls = res ?? [];
    }

    protected virtual IEnumerable<KeyValuePair<int, string>> IterateStrings()
    {
        foreach (var str in extraStrings.OrderBy(x => x.Key))
        {
            yield return str;
        }

        for (int i = 0; i < Dlls.Count; i++) {
            foreach (var str in Dlls[i].Strings.OrderBy(x => x.Key)) {
                var id = i * 65536 + str.Key;
                if (!extraStrings.ContainsKey(id))
                {
                    yield return new KeyValuePair<int, string>(id, str.Value);
                }
            }
        }
    }
    protected virtual IEnumerable<KeyValuePair<int, string>> IterateXml()
    {
        foreach (var info in extraInfocards.OrderBy(x => x.Key))
        {
            yield return info;
        }

        for (int i = 0; i < Dlls.Count; i++) {
            foreach (var info in Dlls[i].Infocards.OrderBy(x => x.Key)) {
                var id = i * 65536 + info.Key;
                if (!extraInfocards.ContainsKey(id))
                {
                    yield return new KeyValuePair<int, string>(id, info.Value);
                }
            }
        }
    }

    public IEnumerable<int> StringIds => IterateStrings().Select(x => x.Key);
    public IEnumerable<int> InfocardIds => IterateXml().Select(x => x.Key);

    public IEnumerable<KeyValuePair<int, string>> AllStrings => IterateStrings();
    public IEnumerable<KeyValuePair<int, string>> AllXml => IterateXml();

    protected HashSet<int> MissingStrings = [];
    protected HashSet<int> MissingXml = [];

    public (int Strings, int Infocards) LoadJson(Stream stream, string? source = null)
    {
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var root = document.RootElement;
        var addedStrings = 0;
        var addedInfocards = 0;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{source ?? "strings json"} must contain a JSON object");
        }

        if (root.TryGetProperty("strings", out var strings))
        {
            addedStrings += LoadJsonMap(strings, extraStrings, source, "strings");
        }

        if (root.TryGetProperty("infocards", out var infocards))
        {
            addedInfocards += LoadJsonMap(infocards, extraInfocards, source, "infocards");
        }

        if (addedStrings == 0 && addedInfocards == 0)
        {
            // Backwards-compatible flat format: { "1271": "...", "501028": "..." }
            addedStrings += LoadJsonMap(root, extraStrings, source, "strings");
        }

        foreach (var id in extraStrings.Keys)
        {
            MissingStrings.Remove(id);
        }

        foreach (var id in extraInfocards.Keys)
        {
            MissingXml.Remove(id);
        }

        return (addedStrings, addedInfocards);
    }

    private static int LoadJsonMap(JsonElement map, Dictionary<int, string> destination, string? source, string mapName)
    {
        if (map.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{source ?? "strings json"}: '{mapName}' must be a JSON object");
        }

        var added = 0;
        foreach (var entry in map.EnumerateObject())
        {
            if (!TryParseResourceId(entry.Name, out var id))
            {
                FLLog.Warning("Strings", $"Ignoring invalid {mapName} id '{entry.Name}' in {source ?? "strings json"}");
                continue;
            }

            string? value = entry.Value.ValueKind switch
            {
                JsonValueKind.String => entry.Value.GetString(),
                JsonValueKind.Number => entry.Value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };

            if (value == null)
            {
                FLLog.Warning("Strings", $"Ignoring non-scalar {mapName} value '{entry.Name}' in {source ?? "strings json"}");
                continue;
            }

            destination[id] = value;
            added++;
        }

        return added;
    }

    private static bool TryParseResourceId(string text, out int id)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
        {
            return true;
        }

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id))
        {
            return true;
        }

        return false;
    }

    public bool HasStringResource(int id)
    {
        if (id <= 0) return false;
        if (extraStrings.ContainsKey(id)) return true;
        var (x, y) = (id >> 16, id & 0xFFFF);
        return (x < Dlls.Count && Dlls[x].Strings.ContainsKey(y)) ||
               Dlls.Any(dll => dll.Strings.ContainsKey(y));
    }

    public virtual string GetStringResource(int id)
    {
        if (id <= 0) return "";
        if (extraStrings.TryGetValue(id, out var extra))
        {
            return extra;
        }

        var (x, y) = (id >> 16, id & 0xFFFF);
        if (x < Dlls.Count && Dlls[x].Strings.TryGetValue(y, out var s))
        {
            return s;
        }

        // Discovery installs can run from librelancer.ini without the original [Resources]
        // DLL order. If the global id's dll slot is wrong, search by local RT_STRING id so
        // the UI remains usable instead of displaying blanks everywhere.
        foreach (var dll in Dlls)
        {
            if (dll.Strings.TryGetValue(y, out s))
            {
                return s;
            }
        }

        if (!MissingStrings.Contains(id))
        {
            FLLog.Warning("Strings", "Not Found: " + id);
            MissingStrings.Add(id);
        }
        return "";
    }

    public bool HasXmlResource(int id)
    {
        if (id <= 0) return false;
        if (extraInfocards.ContainsKey(id)) return true;
        var (x, y) = (id >> 16, id & 0xFFFF);
        return (x < Dlls.Count && Dlls[x].Infocards.ContainsKey(y)) ||
               Dlls.Any(dll => dll.Infocards.ContainsKey(y));
    }

    public virtual string? GetXmlResource(int id)
    {
        if (id <= 0)
        {
            return null;
        }

        if (extraInfocards.TryGetValue(id, out var extra))
        {
            return extra;
        }

        var (x, y) = (id >> 16, id & 0xFFFF);
        if (x < Dlls.Count && Dlls[x].Infocards.TryGetValue(y, out var s))
        {
            return s;
        }

        foreach (var dll in Dlls)
        {
            if (dll.Infocards.TryGetValue(y, out s))
            {
                return s;
            }
        }

        if (!MissingXml.Contains(id))
        {
            FLLog.Warning("Infocards", "Not Found: " + id);
            MissingXml.Add(id);
        }

        return null;
    }
}
