using System;
using System.Collections.Generic;
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
                c.SendChat("/time set night");
                c.SendChat("/summon skeleton");
                rig.Run(3);
                ServerPlayer sp = rig.ServerSide(c);
                int shown = 0;
                for (int i = 0; i < 200 && shown < 12; i++)
                {
                    rig.Tick();
                    foreach (KeyValuePair<int, Entity> kv in rig.Server.Entities)
                    {
                        ArrowEntity a = kv.Value as ArrowEntity;
                        if (a == null) continue;
                        Aabb box = sp.Box;
                        Console.WriteLine("t" + i + " arrow " + a.Pos + " vel " + a.Vel + " player box " + box.Min + ".." + box.Max + " hp " + sp.Health);
                        shown++;
                    }
                }
            }
        }
    }
}
