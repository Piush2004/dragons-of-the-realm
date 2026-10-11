using System;
using System.Collections.Generic;
using BlockRealm.Net;
using BlockRealm.Sim;

namespace BlockRealm.Tests
{
    /// <summary>A hand-driven client with no game logic of its own: it sends exactly what a cheater would.</summary>
    public sealed class RawClient
    {
        public readonly LoopbackConnection Conn;
        public readonly List<Packet> Received = new List<Packet>();
        public int PlayerId;

        public RawClient(GameServer server, string name)
        {
            LoopbackConnection a, b;
            LoopbackConnection.CreatePair(out a, out b);
            server.AddConnection(b);
            Conn = a;
            Send(new HelloPacket { Name = name });
        }

        public void Send(Packet p) { Conn.Send(p.Serialize()); }

        public void Pump()
        {
            byte[] data;
            while (Conn.TryReceive(out data))
            {
                Packet p = Packet.Deserialize(data);
                Received.Add(p);
                WelcomePacket w = p as WelcomePacket;
                if (w != null) PlayerId = w.PlayerId;
            }
        }

        public int Count<T>() where T : Packet
        {
            int n = 0;
            foreach (Packet p in Received) if (p is T) n++;
            return n;
        }
    }

    public static class AbuseTests
    {
        private static void Run(GameServer s, RawClient[] clients, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                s.Tick();
                foreach (RawClient c in clients) c.Pump();
            }
        }

        private static GameServer NewServer(Action<ServerOptions> cfg = null)
        {
            ServerOptions o = new ServerOptions { Seed = 2024, ViewDistance = 3, SynchronousWorldGen = true, SpawnMobs = false, SpawnDragons = false };
            if (cfg != null) cfg(o);
            return new GameServer(o);
        }

        public static void RunAll()
        {
            Harness.Group("abuse resistance");
            Harness.Run("only the host and listed operators may use commands", () =>
            {
                GameServer s = NewServer(o => { o.HostIsOp = true; });
                try
                {
                    s.Options.Operators.Add("Helper");
                    RawClient host = new RawClient(s, "Host"), guest = new RawClient(s, "Guest"), helper = new RawClient(s, "Helper");
                    RawClient[] all = { host, guest, helper };
                    Run(s, all, 5);
                    Check.True(s.FindPlayer("Host").Op && !s.FindPlayer("Guest").Op && s.FindPlayer("Helper").Op, "op flags");
                    guest.Send(new ChatPacket { Text = "/give diamond 20" });
                    guest.Send(new ChatPacket { Text = "/gamemode creative" });
                    helper.Send(new ChatPacket { Text = "/give diamond 3" });
                    Run(s, all, 5);
                    Check.Equal(0, s.FindPlayer("Guest").Inventory.Count(ItemIds.Diamond), "guest got nothing");
                    Check.Equal(GameMode.Survival, s.FindPlayer("Guest").Mode);
                    Check.Equal(3, s.FindPlayer("Helper").Inventory.Count(ItemIds.Diamond), "listed operator may");
                }
                finally { s.Shutdown(); }
            });
            Harness.Run("survival players cannot claim flight or bank up a free teleport", () =>
            {
                GameServer s = NewServer(o => o.EveryoneIsOp = false);
                try
                {
                    RawClient r = new RawClient(s, "Cheat");
                    Run(s, new[] { r }, 30);
                    ServerPlayer p = s.FindPlayer("Cheat");
                    Vec3 start = p.Pos;
                    // Claiming Flying in survival changes nothing about what is allowed.
                    r.Send(new PlayerStatePacket { Position = new Vec3(start.X + 6f, start.Y, start.Z), Flags = PlayerFlags.Flying });
                    Run(s, new[] { r }, 2);
                    Check.Equal(0, (int)(p.ReportedFlags & PlayerFlags.Flying), "flying flag ignored");
                    Check.True(Vec3.Distance(p.Pos, start) < 1f, "6 blocks in one tick is not walking");
                    // Staying silent for a while does not allow a long jump afterwards.
                    Run(s, new[] { r }, 100);
                    r.Send(new PlayerStatePacket { Position = new Vec3(start.X + 40f, start.Y, start.Z), Flags = PlayerFlags.OnGround });
                    Run(s, new[] { r }, 2);
                    Check.True(Vec3.Distance(p.Pos, start) < 1f, "40 blocks after silence rejected");
                    Check.True(r.Count<PlayerTeleportPacket>() >= 1, "told to go back");
                    // An honest sequence of small steps is accepted (on a cleared strip, so no hillside is in the way).
                    int sx = MathUtil.FloorInt(start.X), sy = MathUtil.FloorInt(start.Y), sz = MathUtil.FloorInt(start.Z);
                    for (int i = 0; i <= 4; i++)
                        for (int y = sy; y <= sy + 3; y++) s.World.SetBlockRaw(sx + i, y, sz, B.Air);
                    for (int i = 0; i <= 4; i++) s.World.SetBlockRaw(sx + i, sy - 1, sz, B.Stone);
                    for (int i = 1; i <= 10; i++)
                    {
                        r.Send(new PlayerStatePacket { Position = new Vec3(start.X + i * 0.2f, start.Y, start.Z), Flags = PlayerFlags.OnGround });
                        Run(s, new[] { r }, 1);
                    }
                    Check.True(p.Pos.X > start.X + 1.5f, "normal walking accepted: " + (p.Pos.X - start.X));
                }
                finally { s.Shutdown(); }
            });
            Harness.Run("walking through solid ground is refused", () =>
            {
                GameServer s = NewServer(o => o.EveryoneIsOp = false);
                try
                {
                    RawClient r = new RawClient(s, "Ghost");
                    Run(s, new[] { r }, 30);
                    ServerPlayer p = s.FindPlayer("Ghost");
                    Vec3 start = p.Pos;
                    int gx = MathUtil.FloorInt(start.X), gz = MathUtil.FloorInt(start.Z);
                    int surface = s.World.GetSurfaceY(gx, gz);
                    // Ten blocks of stone below the surface is solid.
                    r.Send(new PlayerStatePacket { Position = new Vec3(start.X, surface - 1.0f, start.Z), Flags = PlayerFlags.None });
                    Run(s, new[] { r }, 2);
                    Check.True(p.Pos.Y > surface - 0.5f, "stayed above the ground: " + p.Pos.Y + " surface " + surface);
                }
                finally { s.Shutdown(); }
            });
            Harness.Run("instant mass-mining is rate limited", () =>
            {
                GameServer s = NewServer(o => o.EveryoneIsOp = false);
                try
                {
                    RawClient r = new RawClient(s, "Miner");
                    Run(s, new[] { r }, 30);
                    ServerPlayer p = s.FindPlayer("Miner");
                    int px = MathUtil.FloorInt(p.Pos.X), pz = MathUtil.FloorInt(p.Pos.Z), py = MathUtil.FloorInt(p.Pos.Y);
                    // A field of tall grass (breaks instantly), each plant standing on solid ground.
                    List<Vec3i> cells = new List<Vec3i>();
                    for (int i = 0; i < 30; i++)
                    {
                        int cx = px - 3 + (i % 6), cz = pz + 2 + i / 6;
                        int top = s.World.GetSurfaceY(cx, cz);
                        s.World.SetBlockRaw(cx, top, cz, B.Stone);
                        s.World.SetBlockRaw(cx, top + 1, cz, B.TallGrass);
                        cells.Add(new Vec3i(cx, top + 1, cz));
                    }
                    foreach (Vec3i c in cells) r.Send(new FinishBreakPacket { X = c.X, Y = c.Y, Z = c.Z });
                    Run(s, new[] { r }, 1);
                    int broken = 0;
                    foreach (Vec3i c in cells) if (s.World.GetBlock(c.X, c.Y, c.Z) == B.Air) broken++;
                    Console.WriteLine("        broke " + broken + " of 30 in one tick");
                    Check.True(broken <= 10 && broken >= 1, "burst limited to the token bucket: " + broken);
                }
                finally { s.Shutdown(); }
            });
            Harness.Run("chat floods are limited", () =>
            {
                GameServer s = NewServer(o => o.EveryoneIsOp = false);
                try
                {
                    RawClient spammer = new RawClient(s, "Spam"), listener = new RawClient(s, "Ear");
                    RawClient[] all = { spammer, listener };
                    Run(s, all, 10);
                    int before = listener.Count<ChatMessagePacket>();
                    for (int i = 0; i < 50; i++) spammer.Send(new ChatPacket { Text = "buy now " + i });
                    Run(s, all, 3);
                    int got = listener.Count<ChatMessagePacket>() - before;
                    Console.WriteLine("        delivered " + got + " of 50 flooded messages");
                    Check.True(got <= 6 && got >= 1, "flood cut to the burst allowance: " + got);
                }
                finally { s.Shutdown(); }
            });
            Harness.Run("a flood of half-open connections is capped", () =>
            {
                GameServer s = NewServer(o => o.MaxPendingConnections = 8);
                try
                {
                    List<LoopbackConnection> peers = new List<LoopbackConnection>();
                    for (int i = 0; i < 50; i++)
                    {
                        LoopbackConnection a, b;
                        LoopbackConnection.CreatePair(out a, out b);
                        s.AddConnection(b);
                        peers.Add(b);
                    }
                    int open = 0;
                    foreach (LoopbackConnection c in peers) if (c.IsOpen) open++;
                    Check.Equal(8, open, "only the cap stays open");
                }
                finally { s.Shutdown(); }
            });
            Harness.Run("attacks and interactions out of reach are ignored", () =>
            {
                GameServer s = NewServer(o => o.EveryoneIsOp = true);
                try
                {
                    RawClient r = new RawClient(s, "Far");
                    Run(s, new[] { r }, 30);
                    ServerPlayer p = s.FindPlayer("Far");
                    MobEntity pig = new MobEntity(EntityType.Pig);
                    pig.Pos = p.Pos + new Vec3(12f, 0f, 0f);
                    s.AddEntity(pig);
                    r.Send(new InteractEntityPacket { EntityId = pig.Id, Action = EntityAction.Attack });
                    Run(s, new[] { r }, 2);
                    Check.Equal(pig.MaxHealth, pig.Health, "pig untouched at 12 blocks");
                    pig.Pos = p.Pos + new Vec3(2f, 0f, 0f);
                    r.Send(new InteractEntityPacket { EntityId = pig.Id, Action = EntityAction.Attack });
                    Run(s, new[] { r }, 2);
                    Check.True(pig.Health < pig.MaxHealth, "pig hit at 2 blocks");
                }
                finally { s.Shutdown(); }
            });
        }
    }
}
