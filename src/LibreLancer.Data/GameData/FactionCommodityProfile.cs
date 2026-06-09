using System;
using System.Collections.Generic;
using LibreLancer.Data.GameData.Items;

namespace LibreLancer.Data.GameData;

public class FactionCommodityProfile
{
    public required Faction Faction;
    public Dictionary<string, FactionCommodityRule> Commodities = new(StringComparer.OrdinalIgnoreCase);

    public bool Allows(string nickname) => Commodities.ContainsKey(nickname);
}

public readonly record struct FactionCommodityRule(ResolvedGood Good, int Min, int Max);
