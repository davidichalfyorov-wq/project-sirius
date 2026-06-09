using System;
using System.Collections.Generic;

namespace LibreLancer.Data.GameData.Items;

public class MissileEquip : Equipment
{
    public required Data.Schema.Equipment.Munition Def;
    public required Data.Schema.Equipment.Motor? Motor;
    public required Data.Schema.Equipment.Explosion Explosion;
    public ResolvedFx? ExplodeFx;
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
