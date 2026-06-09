// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.Linq;
using LibreLancer.Data.Ini;

namespace LibreLancer.Data.Schema.MBases;

[ParsedSection]
public partial class GfNpc
{
    [Entry("nickname", Required = true)]
    public string Nickname = null!;
    [Entry("base_appr")]
    public string? BaseAppr;
    [Entry("body")]
    public string? Body;
    [Entry("head")]
    public string? Head;
    [Entry("lefthand")]
    public string? LeftHand;
    [Entry("righthand")]
    public string? RightHand;
    [Entry("accessory", Multiline = true)]
    public List<string> Accessories = [];
    public string? Accessory => Accessories.Count > 0 ? Accessories[0] : null;
    [Entry("individual_name")]
    public int IndividualName;
    [Entry("affiliation")]
    public string? Affiliation;
    [Entry("voice")]
    public string? Voice;
    [Entry("room")]
    public string? Room;

    public List<NpcKnow> Know = [];
    public List<NpcRumor> Rumors = [];
    public List<NpcBribe> Bribes = [];
    public NpcMission? Mission;

    private string[]? activeKnowDb;
    private string[]? activeRumorKnowDb;
    private readonly List<NpcKnow> knowWaitingForDb = [];
    private readonly List<NpcRumor> rumorWaitingForDb = [];

    [EntryHandler("know", MinComponents = 4, Multiline = true)]
    private void HandleKnow(Entry e)
    {
        var know = new NpcKnow(e[0].ToInt32(), e[1].ToInt32(), e[2].ToInt32(), e[3].ToInt32());
        if (activeKnowDb != null)
        {
            know.Objects = activeKnowDb;
        }
        else
        {
            knowWaitingForDb.Add(know);
        }

        Know.Add(know);
    }

    [EntryHandler("rumor", MinComponents = 4, Multiline = true)]
    private void HandleRumor(Entry e)
    {
        AddRumor(new NpcRumor(e[0].ToString(), e[1].ToString(), e[2].ToInt32(), e[3].ToInt32(), false));
    }

    [EntryHandler("rumor_type2", MinComponents = 4, Multiline = true)]
    private void HandleRumorType2(Entry e)
    {
        AddRumor(new NpcRumor(e[0].ToString(), e[1].ToString(), e[2].ToInt32(), e[3].ToInt32(), true));
    }

    private void AddRumor(NpcRumor rumor)
    {
        if (activeRumorKnowDb != null)
        {
            rumor.Objects = activeRumorKnowDb;
        }
        else
        {
            rumorWaitingForDb.Add(rumor);
        }

        Rumors.Add(rumor);
    }

    [EntryHandler("bribe", MinComponents = 3, Multiline = true)]
    private void HandleBribe(Entry e) => Bribes.Add(
        new NpcBribe(e[0].ToString(), e[1].ToInt32(), e[2].ToInt32())
    );

    [EntryHandler("misn", MinComponents = 3)]
    private void HandleMisn(Entry e) => Mission = new NpcMission(e[0].ToString(), e[1].ToSingle(), e[2].ToSingle());

    [EntryHandler("rumorknowdb", Multiline = true)]
    private void RumorKnowDb(Entry knowdb)
    {
        activeRumorKnowDb = knowdb.Select(x => x.ToString()).ToArray();
        foreach (var rumor in rumorWaitingForDb)
        {
            rumor.Objects = activeRumorKnowDb;
        }
        rumorWaitingForDb.Clear();
    }

    [EntryHandler("knowdb", Multiline = true)]
    private void KnowDb(Entry knowdb)
    {
        activeKnowDb = knowdb.Select(x => x.ToString()).ToArray();
        foreach (var know in knowWaitingForDb)
        {
            know.Objects = activeKnowDb;
        }
        knowWaitingForDb.Clear();
    }
}

public class NpcMission(string kind, float min, float max)
{
    public string Kind = kind;
    public float Min = min;
    public float Max = max;
}

public class RepInfo
{
    public int RepRequired;
    public float RepThreshold => RepRequired switch
    {
        2 => 0.4f,
        3 => 0.6f,
        _ => 0.2f,
    };
}

public class NpcKnow : RepInfo
{
    public int Ids1;
    public int Ids2;
    public int Price;

    public string[] Objects = [];

    public NpcKnow(int ids1, int ids2, int price, int rep)
    {
        Ids1 = ids1;
        Ids2 = ids2;
        Price = price;
        RepRequired = rep;
    }
}

public record NpcBribe(string Faction, int Price, int Ids);

public class NpcRumor : RepInfo
{
    public string Start;
    public string End;
    public int Ids;

    public bool Type2;

    public string[]? Objects;

    public NpcRumor(string start, string end, int rep, int ids, bool type2)
    {
        Start = start;
        End = end;
        RepRequired = rep;
        Ids = ids;
        Type2 = type2;
    }
}
