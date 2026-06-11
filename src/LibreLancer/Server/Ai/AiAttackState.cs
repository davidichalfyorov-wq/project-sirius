// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System.Numerics;
using LibreLancer.Server.Components;
using LibreLancer.World;
using LibreLancer.World.Components;

namespace LibreLancer.Server.Ai
{
    public class AiAttackState : AiState
    {
        private GameObject target;
        public AiAttackState(GameObject target)
        {
            this.target = target;
        }

        public override string GetDebugInfo()
        {
            var label = string.IsNullOrWhiteSpace(target.Nickname) ? $"#{target.NetID}" : $"{target.Nickname} #{target.NetID}";
            return $"AiAttackState target={label}";
        }

        public override void OnStart(GameObject obj, GameWorld world, SNPCComponent ai)
        {

        }

        public override void Update(GameObject obj, GameWorld world, SNPCComponent ai, double time)
        {
            // The target can die, despawn (dock) or enter a tradelane with its
            // physics body removed while we are attacking it - querying its
            // pose then crashes the server thread. Stand down instead.
            if (!target.Flags.HasFlag(GameObjectFlags.Exists) || target.PhysicsComponent?.Body == null)
            {
                ai.SetState(null, world);
                return;
            }

            if (obj.TryGetComponent<WeaponControlComponent>(out var weapons))
            {
                weapons.AimPoint = ai.GetAimPosition(target, weapons, false); // Regular accuracy
                var fireInfo = ai.RunFireTimers((float)time);
                if (fireInfo.ShouldFireRegular || fireInfo.ShouldFireAutoTurrets)
                {
                    // Fire weapon groups based on fire info
                    ai.FireWeaponGroups(weapons, fireInfo, world);
                }
                if (ai.ShouldFireMissiles(time))
                    weapons.FireMissiles(world);
            }
        }
    }
}
