// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LibreLancer.Data.Ini;

namespace LibreLancer.Data.Schema.Fuses;

public abstract class FuseAction
{
    public float AtT;

    [EntryHandler("at_t", Multiline = true)]
    protected void HandleAtT(Entry e)
    {
        if (e.Count > 0)
        {
            AtT = e[0].ToSingle();
        }
    }
}

[ParsedSection]
public partial class FuseDestroyRoot : FuseAction
{
}

[ParsedSection]
public partial class FuseStartEffect : FuseAction //[start_effect]
{
    [Entry("effect")]
    public string? Effect;
    [Entry("particles")]
    public string? Particles;
    [Entry("hardpoint", Multiline = true)]
    public List<string> Hardpoints = [];
    [Entry("attached")]
    public bool Attached;
    [Entry("pos_offset")]
    public Vector3 PosOffset;
    [Entry("ori_offset")]
    public Vector3 OriOffset;
}
[ParsedSection]
public partial class FuseDestroyHpAttachment : FuseAction //[destroy_hp_attachment]
{
    [Entry("hardpoint")]
    public string? hardpoint;
    [Entry("fate")]
    public string? Fate;
}
public enum FusePartFate
{
    NONE,
    disappear,
    debris
}
[ParsedSection]
public partial class FuseDestroyGroup : FuseAction //[destroy_group]
{
    [Entry("group_name")]
    public string? GroupName;
    [Entry("fate")]
    public FusePartFate Fate;
    public bool? Separable;
    public float[] LODRanges = [];
    public string? DmgHp;
    public string? DmgObj;

    [EntryHandler("separable", Multiline = true)]
    private void HandleSeparable(Entry e) => Separable = e.Count == 0 || e[0].ToBoolean();

    [EntryHandler("LODranges", Multiline = true)]
    private void HandleLodRanges(Entry e) => LODRanges = e.Select(x => x.ToSingle()).ToArray();

    [EntryHandler("dmg_hp", Multiline = true)]
    private void HandleDmgHp(Entry e) => DmgHp = string.Join(", ", e.Select(x => x.ToString()));

    [EntryHandler("dmg_obj", Multiline = true)]
    private void HandleDmgObj(Entry e) => DmgObj = string.Join(", ", e.Select(x => x.ToString()));
}

[ParsedSection]
public partial class FuseMakeInvincible : FuseAction //[make_invincible]
{
}
[ParsedSection]
public partial class FuseStartCamParticles : FuseAction //[start_cam_particles]
{
    [Entry("effect")]
    public string? Effect;
    [Entry("pos_offset")]
    public Vector3 PosOffset;
    [Entry("ori_offset")]
    public Vector3 OriOffset;
}
[ParsedSection]
public partial class FuseIgniteFuse : FuseAction //[ignite_fuse]
{
    [Entry("fuse")]
    public string? Fuse;
    [Entry("fuse_t")]
    public float FuseT;
}
[ParsedSection]
public partial class FuseImpulse : FuseAction //[impulse]
{
    [Entry("hardpoint")]
    public string? Hardpoint;
    [Entry("pos_offset")]
    public Vector3 PosOffset;
    [Entry("radius")]
    public float Radius;
    [Entry("damage")]
    public float Damage;
    [Entry("force")]
    public float Force;
}

[ParsedSection]
public partial class FuseDumpCargo : FuseAction //[dump_cargo]
{
    [Entry("origin_hardpoint")]
    public string? OriginHardpoint;
}

[ParsedSection]
public partial class FuseDamageRoot : FuseAction //[damage_root]
{
    [Entry("damage_type")]
    public string? DamageType;
    [Entry("hitpoints")]
    public float Hitpoints;
}

[ParsedSection]
public partial class FuseDamageGroup : FuseAction //[damage_group]
{
    [Entry("group_name")]
    public string? GroupName;
    [Entry("damage_type")]
    public string? DamageType;
    [Entry("hitpoints")]
    public float Hitpoints;
}

[ParsedSection]
public partial class FuseTumble : FuseAction //[tumble]
{
    [Entry("ang_drag_scale")]
    public float AngDragScale;
    [Entry("turn_throttle_x")]
    public Vector2 TurnThrottleX;
    [Entry("turn_throttle_y")]
    public Vector2 TurnThrottleY;
    [Entry("turn_throttle_z")]
    public Vector2 TurnThrottleZ;
    [Entry("throttle")]
    public Vector2 Throttle;
}
