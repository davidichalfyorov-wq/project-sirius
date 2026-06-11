// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System.Collections.Generic;
using System.Data;
using System.Numerics;
using LibreLancer.Data.GameData.World;

namespace LibreLancer.World.Components
{
    public class DockCameraInfo
    {
        public GameObject Parent;
        public Hardpoint DockHardpoint;

        public DockCameraInfo(GameObject parent, Hardpoint dockHp)
        {
            Parent = parent;
            DockHardpoint = dockHp;
        }
    }

    public class UndockInfo
    {
        public Hardpoint? Start;
        public Hardpoint? End;
    }

	public class DockInfoComponent : GameComponent
	{
		public required DockAction Action;
        public required DockSphere[] Spheres;

        private string? tlHP;
		public DockInfoComponent(GameObject parent) : base(parent)
		{
		}

        public DockCameraInfo? GetDockCamera(int index)
        {
            var hpname = Spheres[index].Hardpoint.Replace("DockMount", "DockCam");
            var hp = Parent.GetHardpoint(hpname);
            return hp == null ? null : new DockCameraInfo(Parent, hp);
        }

        public UndockInfo GetUndockInfo(int index)
        {
            var hpname = Spheres[index].Hardpoint.Replace("DockMount", "DockPoint");
            var start = Parent.GetHardpoint(Spheres[index].Hardpoint);
            var end = Parent.GetHardpoint(hpname + "02");

            return new UndockInfo() { Start = start, End = end };
        }

        public float GetTriggerRadius(int index = 0)
        {
            return Spheres[index].Radius;
        }

		public IEnumerable<Hardpoint> GetDockHardpoints(Vector3 position, int index = 0)
		{
			if (Spheres.Length == 0 || index < 0 || index >= Spheres.Length)
            {
                yield break;
            }

			if (Action.Kind != DockKinds.Tradelane)
			{
				var hpname = Spheres[index].Hardpoint.Replace("DockMount", "DockPoint");
                foreach (var name in new[] { hpname + "02", hpname + "01", Spheres[index].Hardpoint })
                {
                    var hp = Parent.GetHardpoint(name);
                    if (hp != null)
                    {
                        yield return hp;
                    }
                }
			}
			else if (Action.Kind == DockKinds.Tradelane)
			{
				var heading = position - Parent.PhysicsComponent!.Body.Position;
                var fwd = Vector3.Transform(-Vector3.UnitZ, Parent.PhysicsComponent.Body.Orientation);
				var dot = Vector3.Dot(heading, fwd);
                var names = dot > 0
                    ? new[] { "HpLeftLane", "HpRightLane" }
                    : new[] { "HpRightLane", "HpLeftLane" };
                foreach (var name in names)
                {
                    tlHP = name;
                    var hp = Parent.GetHardpoint(name);
                    if (hp != null)
                    {
                        yield return hp;
                    }
                }
			}
		}
        public override void Update(double time, GameWorld world)
		{
		}
	}
}
