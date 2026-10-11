using System;
using System.Collections.Generic;
using BlockRealm.Net;

namespace BlockRealm.Sim
{
    public sealed partial class GameServer
    {
        private sealed class LairState
        {
            public Lair Lair;
            public int DragonId;
            public int RespawnTick;
        }

        private readonly Dictionary<int, LairState> _lairs = new Dictionary<int, LairState>();

        public void Send(ServerPlayer p, string text) { p.Send(new ChatMessagePacket { Text = text }); }

        /// <summary>Wakes a dragon in every nest a player comes close to, and puts it away again when they leave.</summary>
        private void TickLairs()
        {
            for (int i = 0; i < Players.Count; i++)
            {
                ServerPlayer p = Players[i];
                int px = MathUtil.FloorInt(p.Pos.X), pz = MathUtil.FloorInt(p.Pos.Z);
                List<Lair> near = Generator.LairsNear(px - 160, pz - 160, px + 160, pz + 160);
                for (int k = 0; k < near.Count; k++)
                {
                    Lair l = near[k];
                    float dx = l.X - p.Pos.X, dz = l.Z - p.Pos.Z;
                    if (dx * dx + dz * dz > 150f * 150f) continue;
                    if (!World.HasColumn(l.X >> 4, l.Z >> 4)) continue;
                    LairState st;
                    if (!_lairs.TryGetValue(l.Id, out st)) { st = new LairState { Lair = l, RespawnTick = 0 }; _lairs[l.Id] = st; }
                    DragonEntity existing = st.DragonId != 0 ? GetEntity(st.DragonId) as DragonEntity : null;
                    if (existing != null && !existing.Removed) continue;
                    if (TickCount < st.RespawnTick) continue;
                    // The nest must have its platform loaded (a chest stands at its centre).
                    if (World.GetBlock(l.X, l.Y, l.Z) != B.ScorchedStone) continue;
                    DragonEntity d = new DragonEntity(l.DragonType);
                    d.HomeLair = l; d.HasLair = true;
                    d.Pos = new Vec3(l.X + 0.5f, l.Y + 1f, l.Z + 0.5f);
                    AddEntity(d);
                    st.DragonId = d.Id;
                }
            }

            // Put wild dragons away when nobody is near, so far-off nests do not cost simulation time.
            if (TickCount % 100 == 0)
            {
                List<Entity> away = null;
                foreach (KeyValuePair<int, Entity> kv in Entities)
                {
                    DragonEntity d = kv.Value as DragonEntity;
                    if (d == null || d.Tamed || d.Summoned) continue;
                    bool near = false;
                    for (int i = 0; i < Players.Count; i++)
                        if (Vec3.DistanceSq(Players[i].Pos, d.Pos) < 260f * 260f) { near = true; break; }
                    if (!near) { if (away == null) away = new List<Entity>(); away.Add(d); }
                }
                if (away != null)
                    foreach (Entity e in away) RemoveEntity(e);
            }
        }

        public void LairDragonDied(DragonEntity d)
        {
            if (!d.HasLair) return;
            LairState st;
            if (_lairs.TryGetValue(d.HomeLair.Id, out st)) st.RespawnTick = TickCount + 20 * 60 * 15;
        }
    }
}
