using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Data.Ini;

namespace LibreLancer.Data.Schema.Effects;

[ParsedSection]
public partial class Explosion
{
    [Entry("nickname", Required = true)] public string Nickname = null!;
    public Vector2 Lifetime;
    [Entry("process")] public string? Process;
    [Entry("num_child_pieces")] public int NumChildPieces;
    [Entry("debris_impulse")] public float DebrisImpulse;
    [Entry("strength")] public float Strength;
    [Entry("radius")] public float Radius;
    [Entry("hull_damage")] public float HullDamage;
    [Entry("impulse")] public float Impulse;
    [Entry("innards_debris_start_time")] public float InnardsDebrisStartTime;
    [Entry("innards_debris_num")] public int InnardsDebrisNum;
    [Entry("innards_debris_radius")] public float InnardsDebrisRadius;
    [Entry("innards_debris_object", Multiline = true)]
    public List<string> InnardsDebrisObjects = [];

    public string? Effect;
    public float EffectSParam; // guess

    public List<(string Name, float Weight)> DebrisTypes = [];


    [EntryHandler("lifetime", MinComponents = 1)]
    private void HandleLifetime(Entry e)
    {
        var min = e[0].ToSingle();
        var max = e.Count > 1 ? e[1].ToSingle() : min;
        Lifetime = new Vector2(min, max);
    }

    [EntryHandler("effect", MinComponents = 1)]
    private void HandleEffect(Entry e)
    {
        Effect = e[0].ToString();
        EffectSParam = e.Count > 1 ? e[1].ToSingle() : 0.0f;
    }

    [EntryHandler("debris_type", MinComponents = 2, Multiline = true)]
    private void HandleDebrisType(Entry e)
    {
        DebrisTypes.Add((e[0].ToString(), e[1].ToSingle()));
    }
}
