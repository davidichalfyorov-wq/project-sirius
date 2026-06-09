using System.Collections.Generic;
using LibreLancer.Data.Ini;
using LibreLancer.Data.IO;

namespace LibreLancer.Data.Schema.Goods;

[ParsedIni]
public partial class CommoditiesPerFactionIni
{
    [Section("factiongood")]
    public List<FactionGood> FactionGoods = [];

    public void AddFile(string filename, FileSystem vfs, IniStringPool? stringPool = null) =>
        ParseIni(filename, vfs, stringPool);
}

[ParsedSection]
public partial class FactionGood
{
    [Entry("faction", Required = true)]
    public string Faction = null!;

    public List<FactionMarketGood> MarketGoods = [];

    [EntryHandler("marketgood", MinComponents = 3, Multiline = true)]
    private void HandleMarketGood(Entry e) => MarketGoods.Add(new FactionMarketGood(e));
}

public class FactionMarketGood
{
    public string Good = null!;
    public int Min;
    public int Max;

    public FactionMarketGood()
    {
    }

    public FactionMarketGood(Entry e)
    {
        Good = e[0].ToString();
        Min = e[1].ToInt32();
        Max = e[2].ToInt32();
    }
}
