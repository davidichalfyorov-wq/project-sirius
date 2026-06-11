using System;
using System.Collections.Generic;
using System.Linq;
using LibreLancer.Data;
using LibreLancer.Data.GameData;
using LibreLancer.Data.Schema;
using LibreLancer.Data.Schema.Missions;
using LibreLancer.Data.Schema.Voices;
using DVoice = LibreLancer.Data.GameData.Voice;

namespace LibreLancer.World;

public static class EncounterHandler
{
    public static EncounterInfo CreateEncounter(
        EncounterIni encounter,
        int level,
        Faction faction,
        GameItemDb db)
    {
        var ei = new EncounterInfo();
        var r = new Random();

        if (encounter.Formations.Count == 0)
        {
            return ei;
        }

        var formation = encounter.Formations[0];
        ei.Behavior = formation.Behavior;

        foreach (var s in formation.Ships)
        {
            var min = Math.Max(0, s.Min);
            var max = Math.Max(min, s.Max);
            var count = r.Next(min, max + 1);
            if (count <= 0)
            {
                continue;
            }

            var possible = ResolvePossibleShips(s, level, faction, db);
            if (possible.Count == 0)
            {
                FLLog.Warning("Encounter", $"{faction.Nickname} has no ships for encounter archetype {s.Archetype} at d{level}");
                continue;
            }

            for (int i = 0; i < count; i++)
            {
                var arch = possible[r.Next(possible.Count)];
                var v = faction.NpcVoices.Count > 0
                    ? faction.NpcVoices[r.Next(faction.NpcVoices.Count)]
                    : null;
                ei.Ships.Add(new(RandomName(faction, v, r), v, arch));
            }
        }

        return ei;
    }

    private static List<ShipArch> ResolvePossibleShips(
        EncounterShipDefinition s,
        int level,
        Faction faction,
        GameItemDb db)
    {
        if (s.Kind == EncounterShipKind.NPCArch)
        {
            var arch = db.NpcShips.Get(s.Archetype);
            return arch == null ? [] : [arch];
        }

        var cls = db.Ini.ShipClasses.Classes.FirstOrDefault(x =>
            x.Nickname.Equals(s.Archetype, StringComparison.OrdinalIgnoreCase));
        if (cls == null)
        {
            FLLog.Warning("Encounter", $"{s.Archetype} not in shipclasses.ini");
            return [];
        }

        var possible = new List<ShipArch>();
        foreach (var m in cls.Members)
        {
            if (faction.ShipsByClass.TryGetValue(m, out var ls))
            {
                foreach (var x in ls)
                {
                    if (x.NpcClass.Contains($"d{level}", StringComparer.OrdinalIgnoreCase))
                    {
                        possible.Add(x);
                    }
                }
            }
        }

        if (possible.Count == 0)
        {
            foreach (var m in cls.Members)
            {
                if (faction.ShipsByClass.TryGetValue(m, out var ls))
                {
                    possible.AddRange(ls);
                }
            }
        }

        if (possible.Count == 0 && faction.NpcShips.Count > 0)
        {
            possible.AddRange(faction.NpcShips);
        }

        return possible;
    }

    private static ObjectName RandomName(Faction faction, DVoice? voice, Random r)
    {
        if (faction.Properties == null)
        {
            return new ObjectName(faction.IdsName > 0 ? faction.IdsName : 0);
        }

        ValueRange<int>? firstName = null;
        var gender = voice?.Gender ?? FLGender.unset;
        if (faction.Properties.FirstNameFemale != null && gender == FLGender.female)
        {
            firstName = faction.Properties.FirstNameFemale;
        }
        else if (faction.Properties.FirstNameMale != null)
        {
            firstName = faction.Properties.FirstNameMale;
        }
        else if (faction.Properties.FirstNameFemale != null)
        {
            firstName = faction.Properties.FirstNameFemale;
        }

        var ids = new List<int>(2);
        if (firstName != null)
        {
            ids.Add(r.Next(firstName.Value));
        }
        if (faction.Properties.LastName.Max > 0 || faction.Properties.LastName.Min > 0)
        {
            ids.Add(r.Next(faction.Properties.LastName));
        }

        return ids.Count == 0 ? new ObjectName(faction.IdsName > 0 ? faction.IdsName : 0) : new ObjectName(ids.Where(x => x != 0).ToArray());
    }
}
