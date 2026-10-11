using System;
using BlockRealm.Net;
using BlockRealm.Sim;

namespace BlockRealm.Tests
{
    public static class DebugTests
    {
        public static void Run()
        {
            using (Rig rig = new Rig())
            {
                GameClient c = rig.JoinAndSettle("Ada");
                c.SendChat("/give cobblestone 10");
                rig.Run(5);
                Console.WriteLine("slots: " + c.Slots[0] + " selected " + c.Selected + " server held " + rig.ServerSide(c).Held);
                rig.SetInput(c, new PlayerInput { Pitch = 0.9f });
                rig.Tick();
                RayHit h = c.TargetBlock;
                Console.WriteLine("target hit " + h.Hit + " block " + h.X + "," + h.Y + "," + h.Z + " place " + h.PlaceX + "," + h.PlaceY + "," + h.PlaceZ + " face " + h.Face);
                Console.WriteLine("player " + c.Player.Pos + " box " + c.Player.Box.Min + " .. " + c.Player.Box.Max);
                rig.SetActions(c, new ClientActions { UsePressed = true });
                rig.Tick();
                rig.SetActions(c, new ClientActions());
                rig.Run(3);
                Console.WriteLine("after: " + c.Slots[0] + " block at place " + rig.Server.World.GetBlock(h.PlaceX, h.PlaceY, h.PlaceZ));

                GameClient b = rig.JoinAndSettle("Bob");
                b.SendChat("/tp Ada"); rig.Run(30);
                Console.WriteLine("Bob at " + rig.ServerSide(b).Pos + ", Ada at " + rig.ServerSide(c).Pos + ", Bob hp " + rig.ServerSide(b).Health + " mode " + rig.ServerSide(b).Mode + " hittable " + rig.ServerSide(b).Hittable);
                c.Send(new InteractEntityPacket { EntityId = b.PlayerId, Action = EntityAction.Attack });
                rig.Run(3);
                Console.WriteLine("Bob hp " + rig.ServerSide(b).Health + " client hp " + b.Health + " invuln " + rig.ServerSide(b).Invulnerable + " ada cooldown " + rig.ServerSide(c).AttackCooldown);
            }
        }
    }
}
