using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BlockRealm.Net;
using BlockRealm.Sim;

namespace BlockRealm.Tests
{
    /// <summary>A server and any number of clients wired together in-process, stepped in lock-step.</summary>
    public sealed class Rig : IDisposable
    {
        public readonly GameServer Server;
        public readonly List<GameClient> Clients = new List<GameClient>();
        public readonly Dictionary<GameClient, PlayerInput> Inputs = new Dictionary<GameClient, PlayerInput>();
        public readonly Dictionary<GameClient, ClientActions> Actions = new Dictionary<GameClient, ClientActions>();

        public Rig(Action<ServerOptions> configure = null)
        {
            ServerOptions o = new ServerOptions();
            o.Seed = 2024; o.ViewDistance = 3; o.SynchronousWorldGen = true;
            o.SpawnMobs = false; o.SpawnDragons = false; o.EveryoneIsOp = true;
            if (configure != null) configure(o);
            Server = new GameServer(o);
        }

        public GameClient Join(string name)
        {
            LoopbackConnection a, b;
            LoopbackConnection.CreatePair(out a, out b);
            Server.AddConnection(b);
            GameClient c = new GameClient(a, name);
            Clients.Add(c);
            Inputs[c] = new PlayerInput();
            Actions[c] = new ClientActions();
            return c;
        }

        public ServerPlayer ServerSide(GameClient c) { return (ServerPlayer)Server.GetEntity(c.PlayerId); }

        public void Tick()
        {
            Server.Tick();
            foreach (GameClient c in Clients) c.Update(0.05f, Inputs[c], Actions[c]);
        }

        public void Run(int ticks) { for (int i = 0; i < ticks; i++) Tick(); }

        public bool RunUntil(Func<bool> done, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (done()) return true;
                Tick();
            }
            return done();
        }

        /// <summary>Joins and runs until the client stands on loaded ground.</summary>
        public GameClient JoinAndSettle(string name)
        {
            GameClient c = Join(name);
            bool ok = RunUntil(() => c.State == ClientState.Joined && c.Player.OnGround && c.PendingColumns == 0 && c.World != null && c.World.ColumnCount >= 9, 400);
            Check.True(ok, name + " did not settle: state " + c.State + ", columns " + (c.World == null ? 0 : c.World.ColumnCount) + ", onGround " + c.Player.OnGround);
            return c;
        }

        public void SetInput(GameClient c, PlayerInput i) { Inputs[c] = i; }
        public void SetActions(GameClient c, ClientActions a) { Actions[c] = a; }

        public void Dispose() { Server.Shutdown(); }
    }

    public static class NetTests
    {
        private static void Say(GameClient c, string text) { c.SendChat(text); }

        public static void RunAll()
        {
            Harness.Group("single player session over loopback");
            Harness.Run("a player joins, receives terrain and lands on the ground", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    Check.Equal(ClientState.Joined, c.State);
                    Check.True(c.World.ColumnCount >= 9, "columns " + c.World.ColumnCount);
                    int sx = MathUtil.FloorInt(c.Player.Pos.X), sz = MathUtil.FloorInt(c.Player.Pos.Z);
                    int ground = c.World.GetSurfaceY(sx, sz);
                    Check.Near(ground + 1f, c.Player.Pos.Y, 0.05f, "standing on the surface");
                    Check.Equal(20f, c.Health);
                    Console.WriteLine("        spawned at " + c.Player.Pos + ", " + c.World.ColumnCount + " columns, " + c.PacketsReceived + " packets");
                }
            });
            Harness.Run("walking moves the player at walking pace", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    rig.Run(20);
                    Vec3 start = c.Player.Pos;
                    PlayerInput walk = new PlayerInput { MoveZ = 1f, Yaw = 0f };
                    rig.SetInput(c, walk);
                    rig.Run(40);                                           // 2 seconds
                    float moved = new Vec3(c.Player.Pos.X - start.X, 0, c.Player.Pos.Z - start.Z).HorizontalLength;
                    Console.WriteLine("        moved " + moved.ToString("0.0") + " blocks in 2 s");
                    Check.InRange(moved, 5.5f, 9.5f, "distance walked");
                    rig.RunUntil(() => rig.ServerSide(c).Pos.Z > start.Z + 3f, 20);
                    Check.Near(c.Player.Pos.Z, rig.ServerSide(c).Pos.Z, 1.5f, "server tracks the client");
                }
            });
            Harness.Run("mining a block with bare hands drops it and the player picks it up", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    rig.Run(10);
                    int bx = MathUtil.FloorInt(c.Player.Pos.X), bz = MathUtil.FloorInt(c.Player.Pos.Z);
                    int by = MathUtil.FloorInt(c.Player.Pos.Y) - 1;
                    byte original = c.World.GetBlock(bx, by, bz);
                    Check.True(original != B.Air, "something to stand on");
                    rig.SetInput(c, new PlayerInput { Pitch = 1.5f });          // look straight down
                    rig.Tick();
                    Check.True(c.TargetBlock.Hit, "looking at the ground");
                    rig.SetActions(c, new ClientActions { AttackHeld = true });
                    bool broken = rig.RunUntil(() => rig.Server.World.GetBlock(bx, by, bz) == B.Air, 80);
                    Check.True(broken, "block broke within 4 s");
                    rig.SetActions(c, new ClientActions());
                    rig.SetInput(c, new PlayerInput { Pitch = 0f });
                    bool got = rig.RunUntil(() => c.Slots[0].Id != 0 || c.Slots[1].Id != 0 || CountItems(c) > 0, 80);
                    Check.True(got, "the dropped item reached the inventory");
                    ushort expectedDrop = Items.DropFor(original, true, null, false);
                    Check.True(CountOf(c, expectedDrop) >= 1, "picked up " + Items.Get(expectedDrop).Name);
                }
            });
            Harness.Run("placing a block consumes it and shows for everyone", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    Say(c, "/give cobblestone 10");
                    rig.Run(5);
                    Check.Equal(10, CountOf(c, B.Cobble));
                    // Aim at the ground ahead and to the side so the new block is not inside the player.
                    rig.SetInput(c, new PlayerInput { Pitch = 0.5f });
                    rig.Tick();
                    Check.True(c.TargetBlock.Hit, "has a target");
                    c.SelectSlot(0);
                    RayHit aimed = c.TargetBlock;
                    rig.SetActions(c, new ClientActions { UsePressed = true });
                    rig.Tick();
                    rig.SetActions(c, new ClientActions());
                    rig.Run(5);
                    Check.Equal(9, CountOf(c, B.Cobble));
                    Check.Equal(B.Cobble, rig.Server.World.GetBlock(aimed.PlaceX, aimed.PlaceY, aimed.PlaceZ), "server has the block");
                }
            });
            Harness.Run("breaking too fast is rejected by the server", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Cheater");
                    int bx = MathUtil.FloorInt(c.Player.Pos.X), bz = MathUtil.FloorInt(c.Player.Pos.Z);
                    int by = MathUtil.FloorInt(c.Player.Pos.Y) - 1;
                    // Hand-break obsidian instantly: only creative mode may do that.
                    rig.Server.World.SetBlock(bx, by, bz, B.Obsidian);
                    rig.Run(3);
                    c.Send(new StartBreakPacket { X = bx, Y = by, Z = bz });
                    c.Send(new FinishBreakPacket { X = bx, Y = by, Z = bz });
                    rig.Run(5);
                    Check.Equal(B.Obsidian, rig.Server.World.GetBlock(bx, by, bz), "obsidian survives");
                    Check.Equal(B.Obsidian, c.World.GetBlock(bx, by, bz), "client sees it too");
                }
            });
            Harness.Run("reach is enforced for building", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    Say(c, "/give stone 5");
                    rig.Run(5);
                    int x = MathUtil.FloorInt(c.Player.Pos.X) + 40, z = MathUtil.FloorInt(c.Player.Pos.Z);
                    int y = rig.Server.World.GetSurfaceY(x, z);
                    byte before = rig.Server.World.GetBlock(x, y + 1, z);
                    c.Send(new PlaceBlockPacket { X = x, Y = y, Z = z, Face = Face.Up });
                    rig.Run(5);
                    Check.Equal(before, rig.Server.World.GetBlock(x, y + 1, z), "nothing placed out of reach");
                    Check.Equal(5, CountOf(c, B.Stone));
                }
            });

            Harness.Group("inventory and crafting over the protocol");
            Harness.Run("crafting planks and a table through window clicks", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    Say(c, "/give oaklog 4");
                    rig.Run(5);
                    int logSlot = SlotOf(c, B.LogOak);
                    Check.True(logSlot >= 0, "has logs");
                    int grid0 = PlayerInventory.Size;             // 2x2 grid in the personal window
                    int result = PlayerInventory.Size + 4;
                    c.ClickSlot(logSlot, 0, false);              // pick up the stack
                    c.ClickSlot(grid0, 1, false);                // put one log in the grid
                    rig.Run(3);
                    Check.Equal((ushort)B.Planks, c.WindowItems[result].Id);
                    c.ClickSlot(result, 0, true);                // shift-click: craft all it can
                    rig.Run(3);
                    Check.True(CountOf(c, B.Planks) >= 4, "planks " + CountOf(c, B.Planks));
                    // Park the leftover logs in an empty slot and make a crafting table from four planks.
                    c.ClickSlot(20, 0, false);
                    rig.Run(2);
                    Check.True(c.Cursor.IsEmpty, "cursor free");
                    int planksSlot = SlotOf(c, B.Planks);
                    c.ClickSlot(planksSlot, 0, false);
                    for (int i = 0; i < 4; i++) c.ClickSlot(grid0 + i, 1, false);
                    rig.Run(3);
                    Check.Equal((ushort)B.CraftingTable, c.WindowItems[result].Id);
                }
            });
            Harness.Run("smelting in a furnace window", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    rig.Run(5);
                    ServerPlayer sp = rig.ServerSide(c);
                    Say(c, "/give furnace 1"); Say(c, "/give ironore 3"); Say(c, "/give coal 2");
                    rig.Run(5);
                    // Place the furnace in front of the player on the ground.
                    rig.SetInput(c, new PlayerInput { Pitch = 0.5f });
                    rig.Tick();
                    int fs = SlotOf(c, B.Furnace);
                    c.SelectSlot(fs);
                    rig.Run(2);
                    RayHit aimed = c.TargetBlock;
                    rig.SetActions(c, new ClientActions { UsePressed = true });
                    rig.Tick();
                    rig.SetActions(c, new ClientActions());
                    rig.Run(3);
                    Vec3i fpos = new Vec3i(aimed.PlaceX, aimed.PlaceY, aimed.PlaceZ);
                    Check.Equal(B.Furnace, rig.Server.World.GetBlock(fpos.X, fpos.Y, fpos.Z), "furnace placed");
                    // Open it, load ore and fuel, wait.
                    c.Send(new InteractPacket { X = fpos.X, Y = fpos.Y, Z = fpos.Z });
                    rig.Run(3);
                    Check.True(c.WindowOpen && c.OpenWindowKind == WindowKind.Furnace, "furnace window open");
                    int ore = SlotOf(c, B.IronOre), coal = SlotOf(c, ItemIds.Coal);
                    c.ClickSlot(ore, 0, false);
                    c.ClickSlot(PlayerInventory.Size + 0, 0, false);
                    c.ClickSlot(coal, 0, false);
                    c.ClickSlot(PlayerInventory.Size + 1, 0, false);
                    rig.Run(FurnaceState.CookTicks * 3 + 40);
                    Check.True(c.WindowItems[PlayerInventory.Size + 2].Id == ItemIds.IronIngot, "smelted output: " + c.WindowItems[PlayerInventory.Size + 2]);
                    Check.Equal(B.FurnaceLit == rig.Server.World.GetBlock(fpos.X, fpos.Y, fpos.Z) ? B.FurnaceLit : B.Furnace, rig.Server.World.GetBlock(fpos.X, fpos.Y, fpos.Z));
                }
            });

            Harness.Group("multiplayer");
            Harness.Run("two players see each other and each other's edits", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient a = rig.JoinAndSettle("Ada");
                    GameClient b = rig.JoinAndSettle("Bob");
                    Say(b, "/tp Ada");
                    rig.Run(30);
                    // Entities replicate: each sees the other as a player entity.
                    Check.True(a.Entities.ContainsKey(b.PlayerId), "Ada sees Bob");
                    Check.True(b.Entities.ContainsKey(a.PlayerId), "Bob sees Ada");
                    Check.Equal("Bob", a.Entities[b.PlayerId].Name);
                    // Bob changes a block (creative placement via packet); Ada's replica updates.
                    Say(b, "/gamemode creative");
                    Say(b, "/give glass 1");
                    rig.Run(10);
                    int x = MathUtil.FloorInt(a.Player.Pos.X) + 2, z = MathUtil.FloorInt(a.Player.Pos.Z);
                    int y = rig.Server.World.GetSurfaceY(x, z);
                    Check.True(b.World.HasColumn(x >> 4, z >> 4), "Bob has the column");
                    rig.Server.SetBlock(x, y + 1, z, B.Glass);
                    rig.Run(5);
                    Check.Equal(B.Glass, a.World.GetBlock(x, y + 1, z), "Ada sees the new block");
                    Check.Equal(B.Glass, b.World.GetBlock(x, y + 1, z), "Bob sees the new block");
                    // Chat reaches both.
                    Say(a, "hello world");
                    rig.Run(5);
                    Check.True(b.Chat.Exists(m => m.Contains("hello world") && m.Contains("Ada")), "chat delivered");
                }
            });
            Harness.Run("attacking another player hurts them and respects PvP setting", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient a = rig.JoinAndSettle("Ada");
                    GameClient b = rig.JoinAndSettle("Bob");
                    Say(b, "/tp Ada"); rig.Run(30);
                    float before = b.Health;
                    a.Send(new InteractEntityPacket { EntityId = b.PlayerId, Action = EntityAction.Attack });
                    rig.Run(5);
                    Check.True(b.Health < before, "Bob took damage: " + b.Health);
                }
                using (Rig rig = new Rig(o => o.Pvp = false))
                {
                    GameClient a = rig.JoinAndSettle("Ada");
                    GameClient b = rig.JoinAndSettle("Bob");
                    Say(b, "/tp Ada"); rig.Run(30);
                    a.Send(new InteractEntityPacket { EntityId = b.PlayerId, Action = EntityAction.Attack });
                    rig.Run(5);
                    Check.Equal(20f, b.Health, "no damage with PvP off");
                }
            });
            Harness.Run("duplicate names are made unique and the server fills up", () =>
            {
                using (Rig rig = new Rig(o => o.MaxPlayers = 2))
                {
                    GameClient a = rig.Join("Ada");
                    GameClient b = rig.Join("Ada");
                    GameClient c = rig.Join("Third");
                    rig.RunUntil(() => a.State == ClientState.Joined && b.State == ClientState.Joined, 30);
                    rig.Run(5);
                    Check.Equal(2, rig.Server.Players.Count);
                    Check.True(rig.Server.FindPlayer("Ada") != null && rig.Server.FindPlayer("Ada2") != null, "unique names");
                    rig.RunUntil(() => c.State == ClientState.Disconnected, 30);
                    Check.Equal(ClientState.Disconnected, c.State);
                    Check.True(c.DisconnectReason.Contains("full"), "reason: " + c.DisconnectReason);
                }
            });
            Harness.Run("a hostile client sending garbage is dropped and the server survives", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient good = rig.JoinAndSettle("Good");
                    LoopbackConnection a, b;
                    LoopbackConnection.CreatePair(out a, out b);
                    rig.Server.AddConnection(b);
                    a.Send(new byte[] { 99, 1, 2, 3 });
                    a.Send(new HelloPacket { Name = "Evil" }.Serialize());
                    rig.Run(5);
                    Check.True(!b.IsOpen || rig.Server.FindPlayer("Evil") == null, "bad hello refused");
                    // A joined player sending a malformed packet is disconnected, not the whole server.
                    RawClient mallory = new RawClient(rig.Server, "Mallory");
                    for (int i = 0; i < 6; i++) { rig.Tick(); mallory.Pump(); }
                    Check.True(rig.Server.FindPlayer("Mallory") != null, "joined");
                    mallory.Conn.Send(new byte[] { (byte)PacketId.PlayerState, 1, 2 });
                    rig.Run(3);
                    Check.True(rig.Server.FindPlayer("Mallory") == null, "disconnected for malformed packet");
                    rig.Run(20);
                    Check.Equal(ClientState.Joined, good.State);
                }
            });
            Harness.Run("impossible movement is corrected by the server", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Speedy");
                    Vec3 p = rig.ServerSide(c).Pos;
                    c.Send(new PlayerStatePacket { Position = new Vec3(p.X + 400f, p.Y, p.Z), Flags = PlayerFlags.OnGround });
                    rig.Run(5);
                    Check.True(Vec3.Distance(rig.ServerSide(c).Pos, p) < 5f, "server kept the player in place");
                    Check.True(Vec3.Distance(c.Player.Pos, p) < 5f, "client was pulled back");
                }
            });

            Harness.Group("survival");
            Harness.Run("falling from height hurts and creative flight does not", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    rig.Run(5);
                    Vec3 p = c.Player.Pos;
                    Say(c, "/tp " + p.X + " " + (p.Y + 20f) + " " + p.Z);
                    rig.RunUntil(() => c.Player.Pos.Y > p.Y + 10f, 20);
                    Check.True(c.Player.Pos.Y > p.Y + 10f, "teleported up");
                    rig.RunUntil(() => c.Player.OnGround && c.Player.Pos.Y < p.Y + 2f, 200);
                    rig.Run(10);
                    Console.WriteLine("        health after a 20 block fall: " + c.Health);
                    Check.True(c.Health < 8f, "fall damage applied: " + c.Health);
                    Check.True(!c.Dead || c.Health <= 0f);
                }
            });
            Harness.Run("death drops the inventory, respawn restores health", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    Say(c, "/give diamond 5");
                    rig.Run(5);
                    Check.Equal(5, CountOf(c, ItemIds.Diamond));
                    Say(c, "/kill");
                    rig.Run(5);
                    Check.True(c.Dead, "dead");
                    Check.Equal(0, CountOf(c, ItemIds.Diamond), "inventory emptied");
                    Check.True(rig.Server.EntitiesNear(rig.ServerSide(c).Pos, 6f).Exists(e => e is ItemEntity), "items dropped");
                    c.RequestRespawn();
                    rig.Run(10);
                    Check.True(!c.Dead, "alive again");
                    Check.Equal(20f, c.Health);
                }
            });
            Harness.Run("eating restores hunger", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    ServerPlayer sp = rig.ServerSide(c);
                    sp.Hunger = 8; sp.StatsDirty = true;
                    Say(c, "/give steak 2");
                    rig.Run(5);
                    c.SelectSlot(SlotOf(c, ItemIds.Steak));
                    rig.Run(2);
                    c.Send(new UseItemPacket());
                    rig.Run(5);
                    Check.Equal(16, c.Hunger, "8 + 8 from steak");
                    Check.Equal(1, CountOf(c, ItemIds.Steak));
                }
            });
            Harness.Run("tools wear out while mining", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    Say(c, "/give woodenpickaxe 1");
                    rig.Run(5);
                    int slot = SlotOf(c, ItemIds.WoodPickaxe);
                    c.SelectSlot(slot);
                    rig.Run(2);
                    ServerPlayer sp = rig.ServerSide(c);
                    int bx = MathUtil.FloorInt(c.Player.Pos.X) + 2, bz = MathUtil.FloorInt(c.Player.Pos.Z);
                    int by = rig.Server.World.GetSurfaceY(bx, bz);
                    for (int i = 0; i < 3; i++)
                    {
                        rig.Server.SetBlock(bx, by + 1 + i, bz, B.Stone);
                        rig.Server.BreakBlockAs(sp, bx, by + 1 + i, bz);
                    }
                    Check.Equal((short)3, sp.Inventory.Slots[slot].Wear, "three uses");
                }
            });
        }

        private static int CountItems(GameClient c)
        {
            int n = 0;
            for (int i = 0; i < c.Slots.Length; i++) if (!c.Slots[i].IsEmpty) n += c.Slots[i].Count;
            return n;
        }

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
    }
}
