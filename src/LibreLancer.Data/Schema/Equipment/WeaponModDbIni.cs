using System.Collections.Generic;
using LibreLancer.Data.Ini;
using LibreLancer.Data.IO;

namespace LibreLancer.Data.Schema.Equipment;

[ParsedIni]
public partial class WeaponModDbIni
{
    [Section("weapontype")]
    public List<WeaponType> WeaponTypes = [];

    public void AddFile(string filename, FileSystem vfs, IniStringPool? stringPool = null) =>
        ParseIni(filename, vfs, stringPool);
}

[ParsedSection]
public partial class WeaponType
{
    [Entry("nickname", Required = true)]
    public string Nickname = null!;

    public List<ShieldModifier> ShieldMods = [];

    [EntryHandler("shield_mod", MinComponents = 2, Multiline = true)]
    private void HandleShieldMod(Entry e) =>
        ShieldMods.Add(new ShieldModifier(e[0].ToString(), e[1].ToSingle()));
}

public readonly record struct ShieldModifier(string ShieldType, float Multiplier);
