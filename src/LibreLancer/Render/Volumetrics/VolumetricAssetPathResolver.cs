using System;
using System.Collections.Generic;

namespace LibreLancer.Render.Volumetrics;

public static class VolumetricAssetPathResolver
{
    public const string DefaultDataPath = "DATA/";

    public static string[] BuildVfsCandidates(string? requestedPath, string? dataPath)
    {
        var requested = NormalizeRelativePath(requestedPath);
        if (string.IsNullOrWhiteSpace(requested))
        {
            return [];
        }

        var candidates = new List<string>(2) { requested };
        var dataPrefix = NormalizeDataPath(dataPath);
        if (!string.IsNullOrWhiteSpace(dataPrefix) &&
            !requested.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(dataPrefix + requested);
        }

        return Deduplicate(candidates);
    }

    public static string NormalizeDataPath(string? dataPath)
    {
        var path = NormalizeRelativePath(dataPath);
        if (string.IsNullOrWhiteSpace(path))
        {
            path = DefaultDataPath;
        }
        return path.EndsWith("/", StringComparison.Ordinal) ? path : path + "/";
    }

    private static string NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        var normalized = path.Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }
        normalized = normalized.TrimStart('/');
        return normalized;
    }

    private static string[] Deduplicate(List<string> candidates)
    {
        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrWhiteSpace(candidates[i]))
            {
                candidates.RemoveAt(i);
            }
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = candidates.Count - 1; j > i; j--)
            {
                if (string.Equals(candidates[i], candidates[j], StringComparison.OrdinalIgnoreCase))
                {
                    candidates.RemoveAt(j);
                }
            }
        }

        return candidates.ToArray();
    }
}
