using LibreLancer.Data.GameData.Items;
using LibreLancer.Physics;
using LibreLancer.Render;
using LibreLancer.Resources;
using LibreLancer.World;

namespace LibreLancer.Client.Components;

public class CTradelaneComponent : GameComponent
{
    public TradelaneEquipment Def;

    private ParticleEffectRenderer leftLane = null!;
    private ParticleEffectRenderer rightLane = null!;

    public CTradelaneComponent(GameObject parent, TradelaneEquipment tl) : base(parent)
    {
        Def = tl;
    }

    public override void Register(GameWorld world)
    {
        if (GetGameData(world) == null)
        {
            return;
        }

        var resman = GetResourceManager(world)!;
        var gameData = GetGameData(world);
        var laneFx = Def.RingActive?.GetEffect(resman);

        if (laneFx == null && gameData != null)
        {
            // Some Discovery tradelane equipment omits tl_ring_active and
            // relies on the stock ring effect. Try the common Freelancer names
            // before giving up, otherwise every tradelane logs a warning and
            // appears inert.
            foreach (var fallback in new[]
            {
                "gf_tlr_active", "gf_tlr_active_loop",
                "li_tlr_active", "li_tlr_active_loop",
                "br_tlr_active", "br_tlr_active_loop",
                "ku_tlr_active", "ku_tlr_active_loop",
                "rh_tlr_active", "rh_tlr_active_loop",
                "tlr_active", "tlr_active_loop", "tradelane_ring_active"
            })
            {
                laneFx = gameData.Items.ResolveFx(fallback)?.GetEffect(resman);
                if (laneFx != null)
                {
                    break;
                }
            }
        }

        var leftHp = Parent?.GetHardpoint("HpLeftLane");
        var rightHp = Parent?.GetHardpoint("HpRightLane");

        if (laneFx is null)
        {
            FLLog.Debug("CTradelaneComponent", $"No tradelane ring effect for {Parent?.Nickname ?? "<unknown>"}; rendering lane without active ring FX");
            return;
        }

        if (leftHp is null || rightHp is null)
        {
            FLLog.Warning("CTradelaneComponent", $"Register called but lane hardpoints could not be resolved. leftHp: {leftHp}, rightHp: {rightHp}");
            return;
        }

        leftLane = new ParticleEffectRenderer(laneFx) {Attachment = leftHp, Active = false, SParam = 1 };
        rightLane = new ParticleEffectRenderer(laneFx) {Attachment = rightHp, Active = false, SParam = 1};
        Parent?.ExtraRenderers.Add(leftLane);
        Parent?.ExtraRenderers.Add(rightLane);
    }

    public void ActivateLeft()
    {
        if (leftLane != null) leftLane.Active = true;
    }

    public void ActivateRight()
    {
        if (rightLane != null) rightLane.Active = true;
    }

    public void DeactivateLeft()
    {
        if (leftLane != null) leftLane.Active = false;
    }

    public void DeactivateRight()
    {
        if (rightLane != null) rightLane.Active = false;
    }
}
