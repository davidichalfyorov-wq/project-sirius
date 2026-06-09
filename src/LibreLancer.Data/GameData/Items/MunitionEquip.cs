using System;
using System.Collections.Generic;

namespace LibreLancer.Data.GameData.Items;

public class MunitionEquip : Equipment
{
    public required Schema.Equipment.Munition Def;

    //Fx Stuff
    public Schema.Effects.BeamSpear? ConstEffect_Spear;
    public Schema.Effects.BeamBolt? ConstEffect_Bolt;
    public Dictionary<string, float> ShieldDamageModifiers = new(StringComparer.OrdinalIgnoreCase);

    public float GetShieldDamageModifier(string? shieldType)
    {
        if (string.IsNullOrWhiteSpace(shieldType))
        {
            return 1f;
        }

        return ShieldDamageModifiers.TryGetValue(shieldType, out var modifier) ? modifier : 1f;
    }
}
