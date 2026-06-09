// MIT License - Copyright (c) Malte Rupprecht, Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibreLancer.Data.Dll;
using LibreLancer.Data.Ini;
using LibreLancer.Data.IO;

namespace LibreLancer.Data.Schema;

public class FreelancerIni
{
    public bool IsLibrelancer { get; private set; }
    public List<ResourceDll>? Resources { get; private set; } = [];
    public List<string> StartupMovies { get; private set; }

    public string DataPath { get; private set; } = null!;
    public List<string> SolarPaths { get; private set; }
    public string? UniversePath { get; private set; }
    public string? HudPath { get; private set; }
    public string? XInterfacePath { get; private set; }
    public string? DataVersion { get; private set; }

    public List<string> EquipmentPaths { get; private set; }
    public List<string> LoadoutPaths { get; private set; }
    public List<string> ShiparchPaths { get; private set; }
    public List<string> GoodsPaths { get; private set; }
    public List<string> MarketsPaths { get; private set; }
    public List<string> CommoditiesPerFactionPaths { get; private set; }
    public List<string> WeaponModDbPaths { get; private set; }
    public List<string> SoundPaths { get; private set; }
    public List<string> GraphPaths { get; private set; }
    public List<string> EffectPaths { get; private set; }

    public List<string> ExplosionPaths { get; private set; }
    public List<string> AsteroidPaths { get; private set; }
    public List<string> RichFontPaths { get; private set; }
    public List<string> FontPaths { get; private set;  }
    public List<string> PetalDbPaths { get; private set; }
    public List<string> FusePaths { get; private set;  }
    public List<string> NewCharDBPaths { get; private set;  }

    public List<string> VoicePaths { get; private set; }

    public string? StarsPath { get; private set; }
    public string? BodypartsPath { get; private set; }
    public string? CostumesPath { get; private set; }
    public string? EffectShapesPath { get; private set; }
    //Extended. Not in vanilla
    public string? DacomPath { get; private set; } = @"EXE\dacom.ini";

    public string NewPlayerPath { get; private set; } = @"EXE\newplayer.fl";

    public string MpNewCharacterPath { get; private set; } = @"EXE\mpnewcharacter.fl";

    public List<string>? MBasesPaths { get; private set; } = [];

    public string MousePath { get; private set; }
    public string CamerasPath { get; private set; }
    public string ConstantsPath { get; private set; }

    public string? NavmapPath { get; private set; }

    public List<string> NoNavmapSystems { get; private set; }

    private static readonly string[] NoNavmaps =
    [
        "St02c",
        "St03b",
        "St03",
        "St02"
    ];
    public List<string?> HiddenFactions { get; private set;  }

    private static readonly string[] NoShowFactions =
    [
        "fc_uk_grp",
        "fc_ouk_grp",
        "fc_q_grp",
        "fc_f_grp",
        "fc_or_grp",
        "fc_n_grp",
        "fc_rn_grp",
        "fc_kn_grp",
        "fc_ln_grp"
    ];

    private readonly HashSet<string> resourceDllKeys = new(StringComparer.OrdinalIgnoreCase);

    private bool AddResourceDll(FileSystem vfs, string path, bool logMissing = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('/', '\\');
        if (!vfs.FileExists(normalized))
        {
            if (logMissing)
            {
                FLLog.Warning("Dll", $"{path} not found");
            }
            return false;
        }

        var backing = vfs.GetBackingFileName(normalized);
        var key = backing ?? normalized;
        if (!resourceDllKeys.Add(key))
        {
            return false;
        }

        try
        {
            using var stream = vfs.Open(normalized);
            Resources ??= [];
            Resources.Add(ResourceDll.FromStream(stream, backing ?? normalized));
            return true;
        }
        catch (Exception ex)
        {
            FLLog.Error("Dll", $"Failed to load resource dll '{path}': {ex.Message}");
            return false;
        }
    }

    private bool AddResourceDllAny(FileSystem vfs, string dll, bool logMissing = false)
    {
        var text = dll.Replace('/', '\\');
        if (AddResourceDll(vfs, text, false))
        {
            return true;
        }

        if (!text.StartsWith("EXE\\", StringComparison.OrdinalIgnoreCase) &&
            AddResourceDll(vfs, "EXE\\" + text, false))
        {
            return true;
        }

        if (text.StartsWith("EXE\\", StringComparison.OrdinalIgnoreCase) &&
            AddResourceDll(vfs, text[4..], false))
        {
            return true;
        }

        if (logMissing)
        {
            FLLog.Warning("Dll", $"{dll} not found");
        }

        return false;
    }

    private void LoadResourceSection(FileSystem vfs, Section section)
    {
        // Freelancer.exe always prepends resources.dll before the [Resources] dll list.
        // For librelancer.ini we accept either EXE-relative DLLs or root-relative DLLs so
        // Linux installs can keep the original Discovery resource DLLs without Wine.
        if (IsLibrelancer)
        {
            AddResourceDllAny(vfs, "resources.dll", logMissing: false);
        }
        else
        {
            AddResourceDll(vfs, @"EXE\resources.dll", logMissing: true);
        }

        foreach (Entry e in section)
        {
            if (e.Name.ToLowerInvariant() != "dll")
            {
                continue;
            }

            var dll = e[0].ToString();
            if (IsLibrelancer)
            {
                AddResourceDllAny(vfs, dll, logMissing: true);
            }
            else
            {
                AddResourceDll(vfs, @"EXE\" + dll, logMissing: true);
            }
        }
    }

    private void EnsureLibrelancerResourceDlls(FileSystem vfs)
    {
        if (!IsLibrelancer)
        {
            return;
        }

        if (vfs.FileExists(@"EXE\freelancer.ini"))
        {
            foreach (Section section in IniFile.ParseFile(@"EXE\freelancer.ini", vfs))
            {
                if (!section.Name.Equals("resources", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddResourceDll(vfs, @"EXE\resources.dll", logMissing: true);
                foreach (Entry e in section)
                {
                    if (e.Name.Equals("dll", StringComparison.OrdinalIgnoreCase))
                    {
                        AddResourceDll(vfs, @"EXE\" + e[0], logMissing: true);
                    }
                }

                break;
            }
        }

        // Vanilla Freelancer encodes global IDS as (dllIndex << 16) + localId. If the
        // original EXE/freelancer.ini is unavailable we still need a stable, vanilla-compatible
        // order before adding Discovery's DLLs, otherwise UI, map, goods and infocard ids resolve
        // against the wrong DLL index.
        foreach (var candidate in new[]
                 {
                     @"EXE\resources.dll",
                     @"EXE\offerbriberesources.dll",
                     @"EXE\misctext.dll",
                     @"EXE\nameresources.dll",
                     @"EXE\equipresources.dll",
                     @"EXE\goodsresources.dll",
                     @"EXE\infocard.dll",
                     @"EXE\misctextinfo2.dll",
                     @"EXE\Discovery.dll",
                     @"EXE\DsyAddition.dll",
                     @"EXE\DsyAdditional.dll",
                     @"resources.dll",
                     @"offerbriberesources.dll",
                     @"misctext.dll",
                     @"nameresources.dll",
                     @"equipresources.dll",
                     @"goodsresources.dll",
                     @"infocard.dll",
                     @"misctextinfo2.dll",
                     @"Discovery.dll",
                     @"DsyAddition.dll",
                     @"DsyAdditional.dll"
                 })
        {
            AddResourceDll(vfs, candidate, false);
        }

        if (Resources is not { Count: > 0 })
        {
            FLLog.Warning("Dll", "No resource DLLs were loaded. UI strings and infocards will be missing.");
        }
    }

    private static string FindIni(FileSystem vfs) => vfs.FileExists("librelancer.ini") ? "librelancer.ini" : @"EXE\freelancer.ini";

    private static string EndInSep(string path)
    {
        if (path[path.Length - 1] == '/' || path[path.Length - 1] == '\\')
            return path;
        return path + Path.DirectorySeparatorChar;
    }

    public FreelancerIni(FileSystem vfs) : this(FindIni(vfs), vfs) { }

    public FreelancerIni(string path, FileSystem vfs)
    {
        IsLibrelancer = path.EndsWith("librelancer.ini", StringComparison.OrdinalIgnoreCase);
        if (IsLibrelancer)
        {
            DacomPath = null;
        }

        EquipmentPaths = [];
        LoadoutPaths = [];
        ShiparchPaths = [];
        SoundPaths = [];
        GraphPaths = [];
        EffectPaths = [];
        ExplosionPaths = [];
        AsteroidPaths = [];
        RichFontPaths = [];
        FontPaths = [];
        PetalDbPaths = [];
        StartupMovies = [];
        GoodsPaths = [];
        MarketsPaths = [];
        CommoditiesPerFactionPaths = [];
        WeaponModDbPaths = [];
        FusePaths = [];
        NewCharDBPaths = [];
        VoicePaths = [];
        SolarPaths = [];

        bool extNoNavmaps = false;
        bool extHideFac = false;
        NoNavmapSystems = [..NoNavmaps];
        HiddenFactions = [..NoShowFactions];

        foreach (Section s in IniFile.ParseFile(path, vfs)) {
            switch (s.Name.ToLowerInvariant ()) {
                case "freelancer":
                    foreach (Entry e in s) {
                        if (e.Name.ToLowerInvariant () == "data path") {
                            if (e.Count != 1)
                                throw new Exception ("Invalid number of values in " + s.Name + " Entry " + e.Name + ": " + e.Count);
                            if (DataPath != null)
                                throw new Exception ("Duplicate " + e.Name + " Entry in " + s.Name);
                            if (IsLibrelancer)
                                DataPath = EndInSep(e[0].ToString());
                            else
                                DataPath = "EXE\\" + EndInSep(e[0].ToString());
                        }
                        if (e.Name.ToLowerInvariant() == "dacom path")
                        {
                            DacomPath = e[0].ToString();
                        }
                    }
                    break;
                case "resources":
                    Resources = [];
                    LoadResourceSection(vfs, s);
                    break;
                case "startup":
                    foreach (Entry e in s) {
                        if (e.Name.ToLowerInvariant () != "movie_file")
                            continue;
                        StartupMovies.Add (e [0].ToString());
                    }
                    break;
                case "extended":
                    foreach(Entry e in s) {
                        switch(e.Name.ToLowerInvariant())
                        {
                            case "xinterface":
                                if (Directory.Exists(e[0].ToString()))
                                    XInterfacePath = e[0].ToString();
                                else
                                    XInterfacePath = DataPath + e[0];
                                if (!XInterfacePath!.EndsWith("\\",StringComparison.InvariantCulture) &&
                                    !XInterfacePath.EndsWith("/",StringComparison.InvariantCulture))
                                    XInterfacePath += "/";
                                break;
                            case "dataversion":
                                DataVersion = e[0].ToString();
                                break;
                            case "nonavmap":
                                if (!extNoNavmaps) { NoNavmapSystems = []; extNoNavmaps = true; }
                                NoNavmapSystems.Add(e[0].ToString());
                                break;
                            case "hidefaction":
                                if (!extHideFac) { HiddenFactions = [];  extHideFac = true; };
                                HiddenFactions.Add(e[0].ToString());
                                break;
                        }
                    }
                    break;
                case "data":
                    foreach (Entry e in s) {
                        switch (e.Name.ToLowerInvariant ()) {
                            case "solar":
                                SolarPaths.Add(DataPath + e[0]);
                                break;
                            case "universe":
                                UniversePath = DataPath + e [0];
                                break;
                            case "equipment":
                                EquipmentPaths.Add(DataPath + e [0]);
                                break;
                            case "loadouts":
                                LoadoutPaths.Add(DataPath + e [0]);
                                break;
                            case "stars":
                                StarsPath = DataPath + e [0];
                                break;
                            case "bodyparts":
                                BodypartsPath = DataPath + e [0];
                                break;
                            case "costumes":
                                CostumesPath = DataPath + e [0];
                                break;
                            case "sounds":
                                SoundPaths.Add(DataPath + e[0]);
                                break;
                            case "ships":
                                ShiparchPaths.Add (DataPath + e [0]);
                                break;
                            case "rich_fonts":
                                RichFontPaths.Add(DataPath + e[0]);
                                break;
                            case "fonts":
                                FontPaths.Add(DataPath + e[0]);
                                break;
                            case "igraph":
                                GraphPaths.Add(DataPath + e[0]);
                                break;
                            case "effect_shapes":
                                EffectShapesPath = DataPath + e[0];
                                break;
                            case "effects":
                                EffectPaths.Add(DataPath + e[0]);
                                break;
                            case "explosions":
                                ExplosionPaths.Add(DataPath + e[0]);
                                break;
                            case "asteroids":
                                AsteroidPaths.Add (DataPath + e [0]);
                                break;
                            case "petaldb":
                                PetalDbPaths.Add(DataPath + e[0]);
                                break;
                            case "hud":
                                HudPath = DataPath + e[0];
                                break;
                            case "goods":
                                GoodsPaths.Add(DataPath + e[0]);
                                break;
                            case "markets":
                                MarketsPaths.Add(DataPath + e[0]);
                                break;
                            case "commodities_per_faction":
                            case "commodity_per_faction":
                                CommoditiesPerFactionPaths.Add(DataPath + e[0]);
                                break;
                            case "weaponmoddb":
                                WeaponModDbPaths.Add(DataPath + e[0]);
                                break;
                            case "fuses":
                                FusePaths.Add(DataPath + e[0]);
                                break;
                            case "newchardb":
                                NewCharDBPaths.Add(DataPath + e[0]);
                                break;
                            case "voices":
                                VoicePaths.Add(DataPath + e[0]);
                                break;
                            //extended
                            case "newplayer":
                                NewPlayerPath = DataPath + e[0];
                                break;
                            case "mpnewcharacter":
                                MpNewCharacterPath = DataPath + e[0];
                                break;
                            case "mbases":
                                if (MBasesPaths == null) MBasesPaths = [];
                                MBasesPaths.Add(DataPath + e[0]);
                                break;
                            case "mouse":
                                MousePath = DataPath + e[0];
                                break;
                            case "cameras":
                                CamerasPath = DataPath + e[0];
                                break;
                            case "constants":
                                ConstantsPath = DataPath + e[0];
                                break;
                            case "navmap":
                                NavmapPath = DataPath + e[0];
                                break;
                        }
                    }
                    break;
            }
        }

        if (string.IsNullOrEmpty(MousePath)) MousePath = DataPath + "mouse.ini";
        if (string.IsNullOrEmpty(CamerasPath)) CamerasPath = DataPath + "cameras.ini";
        if (string.IsNullOrEmpty(ConstantsPath)) ConstantsPath = DataPath + "constants.ini";

        EnsureLibrelancerResourceDlls(vfs);
    }
}
