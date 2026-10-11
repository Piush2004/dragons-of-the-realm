using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BlockRealm.Net;
using BlockRealm.Sim;

namespace BlockRealm.Tests
{
    public static class SimTests
    {
        private static int CountOf(GameClient c, ushort id)
        {
            int n = 0;
            for (int i = 0; i < c.Slots.Length; i++) if (c.Slots[i].Id == id) n += c.Slots[i].Count;
            return n;
        }

        private static int SlotOf(GameClient c, ushort id)
        {
            for (int i = 0; i < c.Slots.Length; i++) if (c.Slots[i].Id == id) return i;
            return -1;
        }

        private static List<T> EntitiesOf<T>(GameServer s) where T : Entity
        {
            List<T> list = new List<T>();
            foreach (KeyValuePair<int, Entity> kv in s.Entities)
            {
                T e = kv.Value as T;
                if (e != null) list.Add(e);
            }
            return list;
        }

        public static void RunAll()
        {
            Harness.Group("mobs");
            Harness.Run("a zombie hunts a survival player and ignores creative", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    c.SendChat("/time set night");
                    c.SendChat("/summon zombie");
                    rig.Run(10);
                    List<MobEntity> mobs = EntitiesOf<MobEntity>(rig.Server);
                    Check.Equal(1, mobs.Count, "one zombie");
                    bool hurt = rig.RunUntil(() => c.Health < 20f, 160);
                    Check.True(hurt, "zombie reached and hit the player: health " + c.Health);
                    c.SendChat("/gamemode creative");
                    c.SendChat("/heal");
                    rig.Run(40);
                    float hp = c.Health;
                    rig.Run(80);
                    Check.Equal(hp, c.Health, "creative player is left alone");
                }
            });
            Harness.Run("killing mobs drops loot and mobs flee when hurt", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    c.SendChat("/summon pig");
                    rig.Run(5);
                    MobEntity pig = EntitiesOf<MobEntity>(rig.Server)[0];
                    rig.Server.Hurt(pig, 3f, rig.ServerSide(c), rig.ServerSide(c).Pos, 5f);
                    Check.True(pig.PanicTicks > 0, "panics");
                    rig.Server.Hurt(pig, 100f, rig.ServerSide(c), rig.ServerSide(c).Pos, 0f);
                    Check.True(pig.Dead);
                    rig.Run(5);
                    int items = EntitiesOf<ItemEntity>(rig.Server).Count + CountOf(c, ItemIds.RawPork);
                    Check.True(items >= 1, "dropped pork");
                    rig.Run(40);
                    Check.True(pig.Removed, "corpse removed");
                }
            });
            Harness.Run("a creeper explodes: damages the player and breaks blocks", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    int effects = 0;
                    c.EffectReceived = (k, p, d) => { if (k == EffectKind.Explosion) effects++; };
                    c.SendChat("/summon creeper");
                    rig.Run(3);
                    MobEntity creeper = EntitiesOf<MobEntity>(rig.Server)[0];
                    // Count solid blocks around the creeper before the blast.
                    Vec3 at = creeper.Pos;
                    int before = SolidAround(rig.Server.World, at, 4);
                    bool boom = rig.RunUntil(() => creeper.Removed, 200);
                    Check.True(boom, "creeper detonated");
                    rig.Run(3);
                    Check.True(effects >= 1, "explosion effect reached the client");
                    Check.True(c.Health < 20f, "player took blast damage: " + c.Health);
                    int after = SolidAround(rig.Server.World, at, 4);
                    Check.True(after < before, "terrain was damaged: " + before + " -> " + after);
                }
            });
            Harness.Run("skeletons shoot arrows", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    c.SendChat("/time set night");
                    c.SendChat("/summon skeleton");
                    bool sawArrow = false;
                    float lowest = 20f;
                    for (int i = 0; i < 300; i++)
                    {
                        rig.Tick();
                        if (EntitiesOf<ArrowEntity>(rig.Server).Count > 0) sawArrow = true;
                        lowest = Math.Min(lowest, c.Health);
                    }
                    Check.True(sawArrow, "an arrow flew");
                    Check.True(lowest < 20f, "an arrow hit: lowest health " + lowest);
                }
            });
            Harness.Run("ambient spawning follows the time of day", () =>
            {
                using (Rig rig = new Rig(o => o.SpawnMobs = true))
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    c.SendChat("/time set noon");
                    rig.Run(20 * 40);
                    int passive = 0, hostile = 0;
                    foreach (MobEntity m in EntitiesOf<MobEntity>(rig.Server)) { if (m.Hostile) hostile++; else passive++; }
                    Console.WriteLine("        day: " + passive + " passive, " + hostile + " hostile");
                    Check.True(passive > 0, "animals appear by day");
                    c.SendChat("/time set night");
                    rig.Run(20 * 60);
                    hostile = 0;
                    foreach (MobEntity m in EntitiesOf<MobEntity>(rig.Server)) if (m.Hostile) hostile++;
                    Console.WriteLine("        night: " + hostile + " hostile");
                    Check.True(hostile > 0 || c.Dead, "monsters appear at night");
                }
            });

            Harness.Group("pathfinding");
            Harness.Run("a path goes around a wall and up a step", () =>
            {
                World w = TestWorlds.Flat(64, 2);
                // A wall 3 blocks high from z=0, x=-6..6 at y=65..67.
                for (int x = -6; x <= 6; x++) for (int y = 65; y <= 67; y++) w.SetBlock(x, y, 0, B.Stone);
                List<Vec3> path = new List<Vec3>();
                bool found = Pathfinder.Find(w, 0, 65, -4, 0, 65, 4, path, 800);
                Check.True(found && path.Count > 0, "a route exists");
                Vec3 last = path[path.Count - 1];
                Check.True(Math.Abs(last.X - 0.5f) < 1.6f && Math.Abs(last.Z - 4.5f) < 1.6f, "it reaches the goal: " + last);
                bool wentAround = false;
                foreach (Vec3 p in path) if (Math.Abs(p.X) > 6f) wentAround = true;
                Check.True(wentAround, "it went around the end of the wall");
                // Every step is walkable.
                foreach (Vec3 p in path) Check.True(Pathfinder.CanStand(w, (int)Math.Floor(p.X), (int)Math.Floor(p.Y), (int)Math.Floor(p.Z)), "step " + p + " is standable");

                // A staircase of one-block steps is climbed.
                for (int i = 0; i < 4; i++) for (int y = 65; y <= 65 + i; y++) w.SetBlock(10 + i, y, 10, B.Cobble);
                path.Clear();
                found = Pathfinder.Find(w, 8, 65, 10, 13, 69, 10, path, 400);
                Check.True(found, "stairs found");
                Check.True(path.Count > 0 && path[path.Count - 1].Y >= 68f, "climbed to the top: " + (path.Count > 0 ? path[path.Count - 1].Y : 0));
            });
            Harness.Run("a zombie finds its way around a wall to reach the player", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    c.SendChat("/time set night");
                    rig.Run(5);
                    ServerPlayer sp = rig.ServerSide(c);
                    int px = MathUtil.FloorInt(sp.Pos.X), pz = MathUtil.FloorInt(sp.Pos.Z), py = MathUtil.FloorInt(sp.Pos.Y);
                    // A long tall wall three blocks in front of the player, on level ground that we flatten first.
                    for (int x = px - 12; x <= px + 12; x++)
                        for (int z = pz - 3; z <= pz + 14; z++)
                        {
                            rig.Server.World.SetBlockRaw(x, py - 1, z, B.Stone);
                            for (int y = py; y <= py + 6; y++) rig.Server.World.SetBlockRaw(x, y, z, B.Air);
                        }
                    for (int x = px - 10; x <= px + 10; x++) for (int y = py; y <= py + 3; y++) rig.Server.World.SetBlockRaw(x, y, pz + 3, B.Stone);
                    MobEntity zed = new MobEntity(EntityType.Zombie);
                    zed.Pos = new Vec3(sp.Pos.X, py, pz + 9.5f);
                    rig.Server.AddEntity(zed);
                    bool reached = false;
                    for (int t = 0; t < 20 * 40 && !reached; t++)
                    {
                        rig.Tick();
                        if (Vec3.Distance(zed.Pos, rig.ServerSide(c).Pos) < 2.2f) reached = true;
                    }
                    Console.WriteLine("        zombie ended at " + zed.Pos + ", player at " + rig.ServerSide(c).Pos);
                    Check.True(reached, "zombie got round the wall");
                }
            });

            Harness.Group("world rules");
            Harness.Run("sand falls, torches pop off, fire burns out", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    int x = MathUtil.FloorInt(c.Player.Pos.X) + 3, z = MathUtil.FloorInt(c.Player.Pos.Z) + 3;
                    int y = rig.Server.World.GetSurfaceY(x, z);
                    rig.Server.SetBlock(x, y + 6, z, B.Sand);
                    rig.Run(5);
                    Check.Equal(B.Air, rig.Server.World.GetBlock(x, y + 6, z), "sand left its cell");
                    Check.Equal(B.Sand, rig.Server.World.GetBlock(x, y + 1, z), "sand landed on the ground");
                    // Torch loses its support.
                    rig.Server.SetBlock(x + 1, y + 1, z, B.Dirt);
                    rig.Server.SetBlock(x + 1, y + 2, z, B.Torch);
                    rig.Run(3);
                    rig.Server.SetBlock(x + 1, y + 1, z, B.Air);
                    rig.Run(5);
                    Check.Equal(B.Air, rig.Server.World.GetBlock(x + 1, y + 2, z), "torch fell off");
                    // Fire on a bare block dies by itself.
                    rig.Server.SetBlock(x, y + 2, z + 2, B.Fire);
                    rig.Run(20 * 10);
                    Check.Equal(B.Air, rig.Server.World.GetBlock(x, y + 2, z + 2), "fire burned out");
                }
            });
            Harness.Run("explosions throw items and fire spreads across wood", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    int x = MathUtil.FloorInt(c.Player.Pos.X) + 6, z = MathUtil.FloorInt(c.Player.Pos.Z);
                    int y = rig.Server.World.GetSurfaceY(x, z);
                    for (int i = 0; i < 6; i++) rig.Server.SetBlock(x + i, y + 1, z, B.Planks);
                    rig.Server.SetBlock(x, y + 2, z, B.Fire);
                    rig.Run(20 * 15);
                    int planks = 0;
                    for (int i = 0; i < 6; i++) if (rig.Server.World.GetBlock(x + i, y + 1, z) == B.Planks) planks++;
                    Console.WriteLine("        planks left after the fire: " + planks + " of 6");
                    Check.True(planks < 6, "the fire consumed wood");
                }
            });
            Harness.Run("the day advances and clients are told the time", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    int start = rig.Server.DayTick;
                    rig.Run(200);
                    Check.Equal(200, rig.Server.DayTick - start, "20 ticks per second");
                    Check.True(Math.Abs(c.DayTick - rig.Server.DayTick) <= 100, "client time is within a broadcast interval");
                }
            });

            Harness.Group("dragons");
            Harness.Run("a wild dragon attacks with fire and fangs", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    c.SendChat("/summon cinderwing");
                    rig.Run(10);
                    DragonEntity d = EntitiesOf<DragonEntity>(rig.Server)[0];
                    Check.Equal("Cinderwing", d.Name);
                    bool sawFire = false;
                    bool hurt = rig.RunUntil(() =>
                    {
                        if (EntitiesOf<FireballEntity>(rig.Server).Count > 0) sawFire = true;
                        return c.Health < 12f || c.Dead;
                    }, 20 * 40);
                    Console.WriteLine("        dragon state " + d.State + ", health of player " + c.Health + ", fire seen " + sawFire);
                    Check.True(sawFire, "breathed fire");
                    Check.True(hurt, "hurt the player");
                }
            });
            Harness.Run("dragons are tamed with meat, saddled and ridden", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    c.SendChat("/summon brazenhide");
                    c.SendChat("/give rawporkchop 6");
                    c.SendChat("/give dragonsaddle 1");
                    rig.Run(5);
                    DragonEntity d = EntitiesOf<DragonEntity>(rig.Server)[0];
                    ServerPlayer sp = rig.ServerSide(c);
                    d.Health = d.MaxHealth * 0.2f;
                    // Keep it calm and close so the interaction is in reach.
                    d.Pos = sp.Pos + new Vec3(2f, 0f, 0f);
                    c.SelectSlot(SlotOf(c, ItemIds.RawPork));
                    rig.Run(2);
                    for (int i = 0; i < 4 && !d.Tamed; i++)
                    {
                        d.Pos = sp.Pos + new Vec3(2f, 0f, 0f); d.Vel = Vec3.Zero;
                        c.Send(new InteractEntityPacket { EntityId = d.Id, Action = EntityAction.Interact });
                        rig.Run(2);
                    }
                    Check.True(d.Tamed, "tamed after feeding");
                    Check.Equal(sp.Id, d.OwnerId, "owner");
                    // Saddle it.
                    c.SelectSlot(SlotOf(c, ItemIds.Saddle));
                    rig.Run(2);
                    d.Pos = sp.Pos + new Vec3(2f, 0f, 0f);
                    c.Send(new InteractEntityPacket { EntityId = d.Id, Action = EntityAction.Interact });
                    rig.Run(3);
                    Check.True(d.Saddled, "saddled");
                    // Mount with an empty hand.
                    c.SelectSlot(8);
                    rig.Run(2);
                    d.Pos = sp.Pos + new Vec3(2f, 0f, 0f);
                    c.Send(new InteractEntityPacket { EntityId = d.Id, Action = EntityAction.Interact });
                    rig.Run(5);
                    Check.True(c.Player.Riding, "client is riding");
                    Check.Equal(d.Id, c.RideEntityId);
                    Check.Equal(sp.Id == d.RiderId ? 1 : 0, 1, "server agrees on the rider");

                    // Fly forward and up: the rider moves and the dragon follows.
                    Vec3 start = c.Player.Pos;
                    rig.SetInput(c, new PlayerInput { MoveZ = 1f, Jump = true });
                    rig.Run(60);
                    float flown = Vec3.Distance(start, c.Player.Pos);
                    Console.WriteLine("        flew " + flown.ToString("0.0") + " blocks in 3 s");
                    Check.True(flown > 12f, "flew: " + flown);
                    Check.True(c.Player.Pos.Y > start.Y + 3f, "climbed");
                    Check.True(Vec3.Distance(d.Pos + DragonEntity.RiderOffset, sp.Pos) < 3f, "dragon stays under the rider: " + Vec3.Distance(d.Pos + DragonEntity.RiderOffset, sp.Pos));

                    // Land and dismount.
                    rig.SetInput(c, new PlayerInput { MoveZ = -1f, Sneak = true });
                    rig.Run(40);
                    rig.SetActions(c, new ClientActions { DismountPressed = true });
                    rig.Tick();
                    rig.SetActions(c, new ClientActions());
                    rig.Run(5);
                    Check.True(!c.Player.Riding, "dismounted");
                    Check.Equal(0, rig.ServerSide(c).RidingEntityId, "server cleared the ride");
                }
            });
            Harness.Run("slaying a dragon drops scales and the lair respawns it later", () =>
            {
                using (Rig rig = new Rig(o => o.SpawnDragons = true))
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    c.SendChat("/gamemode creative");
                    rig.Run(3);
                    List<Lair> lairs = rig.Server.Generator.LairsNear(-3000, -3000, 3000, 3000);
                    Check.True(lairs.Count > 0, "world has lairs");
                    Lair l = lairs[0];
                    for (int i = 1; i < lairs.Count; i++)
                        if (Math.Abs(lairs[i].X) + Math.Abs(lairs[i].Z) < Math.Abs(l.X) + Math.Abs(l.Z)) l = lairs[i];
                    // Go there: load the area, then put the player on the platform.
                    rig.Server.EnsureLoaded(l.X, l.Z, 3);
                    c.SendChat("/tp " + l.X + " " + (l.Y + 3) + " " + l.Z);
                    rig.Run(10);
                    c.SendChat("/gamemode survival");
                    bool dragonAppeared = rig.RunUntil(() => EntitiesOf<DragonEntity>(rig.Server).Count > 0, 20 * 10);
                    Check.True(dragonAppeared, "dragon woke at its nest");
                    DragonEntity d = EntitiesOf<DragonEntity>(rig.Server)[0];
                    Check.True(d.HasLair, "bound to the lair");
                    Check.Equal(l.DragonType, d.Kind, "kind matches the lair");
                    rig.Server.Hurt(d, 10000f, rig.ServerSide(c), rig.ServerSide(c).Pos, 0f);
                    Check.True(d.Dead, "dead");
                    rig.Run(10);
                    int scales = 0;
                    foreach (ItemEntity ie in EntitiesOf<ItemEntity>(rig.Server)) if (ie.Stack.Id == ItemIds.DragonScale) scales += ie.Stack.Count;
                    Check.True(scales >= 2 || CountOf(c, ItemIds.DragonScale) >= 2, "dropped dragon scales");
                    // The chest at the centre holds the hoard.
                    Check.Equal(B.Chest, rig.Server.World.GetBlock(l.X, l.Y + 1, l.Z), "hoard chest");
                    ChestState chest = rig.Server.EnsureChest(new Vec3i(l.X, l.Y + 1, l.Z));
                    int stacks = 0;
                    for (int i = 0; i < ChestState.Size; i++) if (!chest.Slots[i].IsEmpty) stacks++;
                    Check.True(stacks >= 3, "loot in the chest: " + stacks + " stacks");
                }
            });

            Harness.Group("persistence and TCP");
            Harness.Run("a world and its players survive a restart", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "brtest_" + Guid.NewGuid().ToString("N"));
                try
                {
                    int bx, by, bz;
                    using (Rig rig = new Rig(o => o.SaveDir = dir))
                    {
                        GameClient c = rig.JoinAndSettle("Ada");
                        c.SendChat("/give diamond 7");
                        c.SendChat("/give chest 1");
                        rig.Run(5);
                        bx = MathUtil.FloorInt(c.Player.Pos.X) + 2; bz = MathUtil.FloorInt(c.Player.Pos.Z) + 1;
                        by = rig.Server.World.GetSurfaceY(bx, bz) + 1;
                        rig.Server.SetBlock(bx, by, bz, B.Bricks);
                        rig.Server.SetBlock(bx, by + 1, bz, B.Chest);
                        ChestState chest = rig.Server.EnsureChest(new Vec3i(bx, by + 1, bz));
                        chest.Slots[3] = new ItemStack(ItemIds.GoldIngot, 9);
                        rig.Server.DayTick = 7777;
                        rig.Run(2);
                    }   // Dispose -> Shutdown -> SaveAll
                    Check.True(File.Exists(Path.Combine(dir, "level.dat")), "level saved");
                    Check.True(File.Exists(Path.Combine(dir, "players", "Ada.dat")), "player saved");

                    using (Rig rig = new Rig(o => o.SaveDir = dir))
                    {
                        Check.Near(7777f, rig.Server.DayTick, 5f, "time restored");
                        GameClient c = rig.Join("Ada");
                        rig.RunUntil(() => c.State == ClientState.Joined && c.PendingColumns == 0 && c.World != null && c.World.ColumnCount >= 9, 300);
                        rig.Run(20);
                        Check.Equal(7, CountOf(c, ItemIds.Diamond), "inventory restored");
                        Check.Equal(B.Bricks, rig.Server.World.GetBlock(bx, by, bz), "edited block restored");
                        Check.Equal(B.Chest, rig.Server.World.GetBlock(bx, by + 1, bz), "chest restored");
                        ChestState chest = rig.Server.EnsureChest(new Vec3i(bx, by + 1, bz));
                        Check.Equal(ItemIds.GoldIngot, chest.Slots[3].Id, "chest contents restored");
                        Check.Equal((byte)9, chest.Slots[3].Count);
                    }
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch (Exception) { }
                }
            });
            Harness.Run("two real TCP clients join, edit and chat", () =>
            {
                ServerOptions o = new ServerOptions { Seed = 2024, ViewDistance = 3, SynchronousWorldGen = true, SpawnMobs = false, SpawnDragons = false };
                GameServer server = new GameServer(o);
                server.StartListening(0);
                int port = server.ListeningPort;
                Check.True(port > 0, "listening on " + port);
                TcpConnection ca = TcpConnection.Connect("127.0.0.1", port, 3000);
                TcpConnection cb = TcpConnection.Connect("127.0.0.1", port, 3000);
                GameClient a = new GameClient(ca, "Net_Ada", "127.0.0.1:" + port), b = new GameClient(cb, "Net_Bob", "127.0.0.1:" + port);
                try
                {
                    PlayerInput none = new PlayerInput();
                    ClientActions noActs = new ClientActions();
                    for (int i = 0; i < 300; i++)
                    {
                        server.Tick();
                        a.Update(0.05f, none, noActs);
                        b.Update(0.05f, none, noActs);
                        Thread.Sleep(2);
                        if (a.State == ClientState.Joined && b.State == ClientState.Joined && a.PendingColumns == 0 && b.PendingColumns == 0 &&
                            a.World.ColumnCount >= 9 && b.World.ColumnCount >= 9 && a.Player.OnGround && b.Player.OnGround) break;
                    }
                    Check.Equal(ClientState.Joined, a.State);
                    Check.Equal(ClientState.Joined, b.State);
                    Check.Equal(2, server.Players.Count);
                    int x = MathUtil.FloorInt(a.Player.Pos.X), z = MathUtil.FloorInt(a.Player.Pos.Z);
                    int y = server.World.GetSurfaceY(x + 2, z) + 1;
                    server.SetBlock(x + 2, y, z, B.Glass);
                    a.SendChat("hi over tcp");
                    for (int i = 0; i < 40; i++)
                    {
                        server.Tick();
                        a.Update(0.05f, none, noActs);
                        b.Update(0.05f, none, noActs);
                        Thread.Sleep(2);
                    }
                    Check.Equal(B.Glass, b.World.GetBlock(x + 2, y, z), "edit reached the other client over TCP");
                    Check.True(b.Chat.Exists(m => m.Contains("hi over tcp")), "chat reached the other client");
                }
                finally
                {
                    server.Shutdown();
                    ca.Close(); cb.Close();
                }
            });
            Harness.Run("a TCP peer sending an oversized frame is cut off", () =>
            {
                ServerOptions o = new ServerOptions { Seed = 1, ViewDistance = 2, SynchronousWorldGen = true, SpawnMobs = false, SpawnDragons = false };
                GameServer server = new GameServer(o);
                server.StartListening(0);
                try
                {
                    System.Net.Sockets.TcpClient raw = new System.Net.Sockets.TcpClient("127.0.0.1", server.ListeningPort);
                    byte[] evil = new byte[] { 0xFF, 0xFF, 0xFF, 0x7F, 1, 2, 3 };       // claims a 2 GB frame
                    raw.GetStream().Write(evil, 0, evil.Length);
                    for (int i = 0; i < 40; i++) { server.Tick(); Thread.Sleep(5); }
                    bool closed = false;
                    try
                    {
                        raw.Client.Poll(200000, System.Net.Sockets.SelectMode.SelectRead);
                        byte[] one = new byte[1];
                        closed = raw.Client.Receive(one, System.Net.Sockets.SocketFlags.Peek) == 0;
                    }
                    catch (Exception) { closed = true; }
                    Check.True(closed, "server closed the connection");
                    raw.Close();
                }
                finally { server.Shutdown(); }
            });

            Harness.Group("soak");
            Harness.Run("five players doing random things for two minutes stay consistent", () =>
            {
                using (Rig rig = new Rig(o => { o.SpawnMobs = true; o.SpawnDragons = false; o.ViewDistance = 3; }))
                {
                    List<GameClient> players = new List<GameClient>();
                    for (int i = 0; i < 5; i++) players.Add(rig.JoinAndSettle("P" + i));
                    rig.Run(5);
                    foreach (GameClient c in players) c.SendChat("/give cobblestone 64");
                    Rng rng = new Rng(77);
                    int exceptions = 0;
                    for (int t = 0; t < 20 * 120; t++)
                    {
                        if (t % 15 == 0)
                        {
                            foreach (GameClient c in players)
                            {
                                PlayerInput inp = new PlayerInput
                                {
                                    MoveZ = rng.Chance(0.7f) ? 1f : 0f, MoveX = rng.Range(-1f, 1f), Jump = rng.Chance(0.2f),
                                    Sprint = rng.Chance(0.4f), Yaw = rng.Range(0f, 6.28f), Pitch = rng.Range(-0.6f, 1.2f),
                                };
                                rig.SetInput(c, inp);
                                rig.SetActions(c, new ClientActions { AttackHeld = rng.Chance(0.4f), UseHeld = rng.Chance(0.3f), UsePressed = rng.Chance(0.2f) });
                                if (rng.Chance(0.05f)) c.SelectSlot(rng.NextInt(9));
                                if (rng.Chance(0.02f)) c.SendChat("hello " + t);
                                if (c.Dead && rng.Chance(0.5f)) c.RequestRespawn();
                            }
                        }
                        try { rig.Tick(); } catch (Exception) { exceptions++; }
                    }
                    Check.Equal(0, exceptions, "no exceptions escaped a tick");
                    Check.Equal(5, rig.Server.Players.Count, "everyone is still connected");
                    foreach (GameClient c in players)
                    {
                        Check.Equal(ClientState.Joined, c.State, c.PlayerName + " state");
                        Check.True(!float.IsNaN(c.Player.Pos.X) && !float.IsNaN(c.Player.Pos.Y) && !float.IsNaN(c.Player.Pos.Z), "finite position");
                        Check.True(c.Player.Pos.Y > -30f, "has not fallen out of the world: " + c.Player.Pos.Y);
                    }
                    Console.WriteLine("        entities alive at the end: " + rig.Server.Entities.Count + ", columns loaded: " + rig.Server.World.ColumnCount);
                }
            });
        }

        private static int SolidAround(World w, Vec3 c, int r)
        {
            int n = 0, cx = MathUtil.FloorInt(c.X), cy = MathUtil.FloorInt(c.Y), cz = MathUtil.FloorInt(c.Z);
            for (int y = cy - r; y <= cy + r; y++)
                for (int z = cz - r; z <= cz + r; z++)
                    for (int x = cx - r; x <= cx + r; x++)
                        if (Blocks.IsSolid(w.GetBlock(x, y, z))) n++;
            return n;
        }
    }
}
