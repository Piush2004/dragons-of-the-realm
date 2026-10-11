using System;
using System.Collections.Generic;
using System.IO;
using BlockRealm.Net;
using BlockRealm.Sim;

namespace BlockRealm.Tests
{
    public static class GameTests
    {
        private static ItemStack S(ushort id, int n = 1) { return new ItemStack(id, n); }

        /// <summary>The bare inventory window used by the tests (no crafting grid).</summary>
        private sealed class InventoryProbe : Window
        {
            public InventoryProbe(PlayerInventory inv) : base(0, WindowKind.Inventory, inv) { }
            public override int ExtraCount { get { return 0; } }
            protected override ItemStack GetExtra(int i) { return ItemStack.Empty; }
            protected override void SetExtra(int i, ItemStack s) { }
        }

        public static void RunAll()
        {
            Harness.Group("protocol");
            Harness.Run("every packet round-trips", () =>
            {
                Packet[] all =
                {
                    new HelloPacket { Name = "Ada" },
                    new PlayerStatePacket { Position = new Vec3(1, 2, 3), Yaw = 0.5f, Pitch = -0.2f, Flags = PlayerFlags.OnGround | PlayerFlags.Sprinting },
                    new PlaceBlockPacket { X = -5, Y = 70, Z = 12, Face = Face.North },
                    new InventoryClickPacket { Window = 2, Slot = 40, Button = 1, Shift = true },
                    new ChatPacket { Text = "hello" },
                    new WelcomePacket { PlayerId = 7, Seed = 123456789012345L, Position = new Vec3(0, 130, 0), DayTick = 6000, ViewDistance = 8, ServerName = "Test" },
                    new ColumnDataPacket { Cx = -3, Cz = 4, Data = new byte[] { 1, 2, 3 } },
                    new BlockUpdatePacket { X = 1, Y = 2, Z = 3, Block = B.Stone },
                    new EntitySpawnPacket { EntityId = 9, Type = EntityType.Dragon, Variant = 2, Position = new Vec3(5, 6, 7), Name = "Elderscale" },
                    new HealthUpdatePacket { Health = 17.5f, Hunger = 12, Saturation = 3.5f },
                    new SetSlotPacket { Window = 0, Slot = 4, Stack = S(ItemIds.Diamond, 3) },
                };
                foreach (Packet p in all)
                {
                    Packet q = Packet.Deserialize(p.Serialize());
                    Check.Equal(p.Id, q.Id, "id");
                    Check.Equal(p.Serialize().Length, q.Serialize().Length, "re-serialised length of " + p.Id);
                }
                EntityStatesPacket es = new EntityStatesPacket();
                for (int i = 0; i < 50; i++) es.Entries.Add(new EntityStateEntry { EntityId = i, Position = new Vec3(i, 1, 2), Flags = EntityFlags.Burning, HealthPercent = 99 });
                EntityStatesPacket es2 = (EntityStatesPacket)Packet.Deserialize(es.Serialize());
                Check.Equal(50, es2.Entries.Count);
                Check.Equal((byte)99, es2.Entries[49].HealthPercent);
                WindowItemsPacket wi = new WindowItemsPacket { Window = 1, Items = new ItemStack[46] };
                wi.Items[3] = S(B.Stone, 12);
                WindowItemsPacket wi2 = (WindowItemsPacket)Packet.Deserialize(wi.Serialize());
                Check.Equal(46, wi2.Items.Length);
                Check.Equal((byte)12, wi2.Items[3].Count);
            });
            Harness.Run("malformed packets are rejected without crashing", () =>
            {
                Rng rng = new Rng(5);
                int rejected = 0;
                for (int i = 0; i < 3000; i++)
                {
                    byte[] junk = new byte[rng.Range(1, 80)];
                    for (int k = 0; k < junk.Length; k++) junk[k] = (byte)rng.NextInt(256);
                    Packet p;
                    if (!Packet.TryDeserialize(junk, out p)) rejected++;
                }
                Check.True(rejected > 1000, "most junk should be rejected: " + rejected);
                Packet dummy;
                // A tiny packet claiming a huge array or list must be refused before anything is allocated.
                Check.True(!Packet.TryDeserialize(new byte[] { (byte)PacketId.ColumnData, 0, 0, 0, 0, 0, 0, 0, 0, 0x00, 0x00, 0x40, 0x00 }, out dummy), "oversized byte array");
                Check.True(!Packet.TryDeserialize(new byte[] { (byte)PacketId.EntityStates, 0xFF, 0xFF }, out dummy), "entity list too long");
                Check.True(!Packet.TryDeserialize(new byte[] { (byte)PacketId.WindowItems, 1, 0xFF, 0x7F }, out dummy), "window list too long");
                Check.True(!Packet.TryDeserialize(new byte[] { 250 }, out dummy), "unknown id");
                byte[] nan = new PlayerStatePacket { Position = new Vec3(0, 0, 0) }.Serialize();
                nan[1] = 0x00; nan[2] = 0x00; nan[3] = 0xC0; nan[4] = 0x7F;   // x = NaN
                Check.True(!Packet.TryDeserialize(nan, out dummy), "NaN position");
            });
            Harness.Run("writer and reader agree on limits", () =>
            {
                PacketWriter w = new PacketWriter();
                Check.Throws(() => w.Str(new string('x', PacketLimits.MaxStringBytes + 1)), "long string");
                Check.Throws(() => w.Bytes(new byte[PacketLimits.MaxByteArray + 1]), "long array");
                PacketWriter ok = new PacketWriter();
                ok.Str(new string('x', PacketLimits.MaxStringBytes));
                ok.Bytes(new byte[PacketLimits.MaxByteArray]);
                PacketReader r = new PacketReader(ok.ToArray());
                Check.Equal(PacketLimits.MaxStringBytes, r.Str().Length);
                Check.Equal(PacketLimits.MaxByteArray, r.Bytes().Length);
            });

            Harness.Group("crafting and smelting");
            Harness.Run("planks, sticks, table and torches craft from the grid", () =>
            {
                ItemStack[] g = new ItemStack[4];
                g[0] = S(B.LogOak);
                ItemStack r = Recipes.Match(g, 2, 2);
                Check.Equal((ushort)B.Planks, r.Id); Check.Equal((byte)4, r.Count);
                g = new ItemStack[4]; g[0] = S(B.Planks); g[2] = S(B.Planks);
                r = Recipes.Match(g, 2, 2);
                Check.Equal(ItemIds.Stick, r.Id); Check.Equal((byte)4, r.Count);
                g = new ItemStack[4]; g[0] = S(B.Planks); g[1] = S(B.Planks); g[2] = S(B.Planks); g[3] = S(B.Planks);
                Check.Equal((ushort)B.CraftingTable, Recipes.Match(g, 2, 2).Id);
                g = new ItemStack[9]; g[1] = S(ItemIds.Coal); g[4] = S(ItemIds.Stick);
                r = Recipes.Match(g, 3, 3);
                Check.Equal((ushort)B.Torch, r.Id); Check.Equal((byte)4, r.Count);
            });
            Harness.Run("tools need the right shape and material", () =>
            {
                ItemStack[] g = new ItemStack[9];
                g[0] = S(B.Cobble); g[1] = S(B.Cobble); g[2] = S(B.Cobble); g[4] = S(ItemIds.Stick); g[7] = S(ItemIds.Stick);
                Check.Equal(ItemIds.StonePickaxe, Recipes.Match(g, 3, 3).Id);
                g[2] = ItemStack.Empty;
                Check.Equal(ItemIds.StoneHoe, Recipes.Match(g, 3, 3).Id);
                g[2] = S(B.Cobble); g[1] = ItemStack.Empty;
                Check.True(Recipes.Match(g, 3, 3).IsEmpty, "incomplete pickaxe");
                g = new ItemStack[9];
                g[3] = S(ItemIds.IronIngot); g[6] = S(ItemIds.IronIngot); g[4] = S(ItemIds.Stick); g[7] = S(ItemIds.Stick);
                Check.True(Recipes.Match(g, 3, 3).IsEmpty, "shape that is not a recipe");
                g = new ItemStack[9];
                g[1] = S(ItemIds.Diamond); g[4] = S(ItemIds.Diamond); g[7] = S(ItemIds.Stick);
                Check.Equal(ItemIds.DiamondSword, Recipes.Match(g, 3, 3).Id);
            });
            Harness.Run("crafting table window: logs to planks via clicks", () =>
            {
                PlayerInventory inv = new PlayerInventory();
                CraftingWindow w = new CraftingWindow(1, WindowKind.CraftingTable, inv, 3);
                inv.Slots[0] = S(B.LogOak, 3);
                Check.True(w.Click(0, 0, false));
                Check.Equal((byte)3, inv.Cursor.Count);
                int grid0 = PlayerInventory.Size;
                Check.True(w.Click(grid0, 1, false));             // right click places one log in the grid
                Check.Equal((byte)2, inv.Cursor.Count);
                int result = PlayerInventory.Size + 9;
                Check.Equal((ushort)B.Planks, w.Get(result).Id);
                // Put the remaining logs back so the cursor is free, then take the planks.
                Check.True(w.Click(0, 0, false));
                Check.True(inv.Cursor.IsEmpty);
                Check.True(w.Click(result, 0, false));
                Check.Equal((ushort)B.Planks, inv.Cursor.Id);
                Check.Equal((byte)4, inv.Cursor.Count);
                Check.True(w.Get(grid0).IsEmpty, "log consumed");
                Check.True(w.Get(result).IsEmpty, "result cleared");
            });
            Harness.Run("shift-clicking the result crafts everything it can", () =>
            {
                PlayerInventory inv = new PlayerInventory();
                CraftingWindow w = new CraftingWindow(0, WindowKind.Inventory, inv, 2);
                int g0 = PlayerInventory.Size;
                inv.Slots[10] = S(B.LogSpruce, 5);
                w.Click(10, 0, false);
                w.Click(g0, 0, false);
                Check.Equal((byte)5, w.Get(g0).Count);
                w.Click(PlayerInventory.Size + 4, 0, true);
                Check.Equal(20, inv.Count(B.Planks));
                Check.True(w.Get(g0).IsEmpty);
            });
            Harness.Run("closing a crafting window returns the grid", () =>
            {
                PlayerInventory inv = new PlayerInventory();
                CraftingWindow w = new CraftingWindow(1, WindowKind.CraftingTable, inv, 3);
                inv.Slots[0] = S(B.Cobble, 8);
                w.Click(0, 0, false);
                w.Click(PlayerInventory.Size + 2, 0, false);
                List<ItemStack> back = w.TakeLeftovers();
                Check.Equal(1, back.Count);
                Check.Equal((ushort)B.Cobble, back[0].Id);
                Check.Equal((byte)8, back[0].Count);
                Check.True(w.Get(PlayerInventory.Size + 2).IsEmpty);
            });
            Harness.Run("furnace smelts with fuel and reports the lit state", () =>
            {
                FurnaceState f = new FurnaceState();
                f.Input = S(B.IronOre, 2); f.Fuel = S(ItemIds.Coal, 1);
                bool changed = false;
                for (int t = 0; t < FurnaceState.CookTicks * 2 + 5; t++) changed |= f.Tick();
                Check.True(changed, "lit state toggled");
                Check.Equal(ItemIds.IronIngot, f.Output.Id);
                Check.Equal((byte)2, f.Output.Count);
                Check.True(f.Input.IsEmpty, "ore consumed");
                f = new FurnaceState();
                f.Input = S(ItemIds.RawPork, 1);
                for (int t = 0; t < 400; t++) f.Tick();
                Check.True(f.Output.IsEmpty, "no fuel, nothing cooks");
            });
            Harness.Run("furnace window: only fuel in the fuel slot, output is take-only", () =>
            {
                PlayerInventory inv = new PlayerInventory();
                FurnaceState f = new FurnaceState();
                FurnaceWindow w = new FurnaceWindow(2, inv, f);
                inv.Slots[0] = S(B.Stone, 5);
                inv.Slots[1] = S(ItemIds.Coal, 5);
                w.Click(0, 0, false);
                w.Click(PlayerInventory.Size + 1, 0, false);       // stone into the fuel slot: refused
                Check.True(f.Fuel.IsEmpty, "stone is not fuel");
                Check.Equal((byte)5, inv.Cursor.Count);
                w.Click(PlayerInventory.Size + 2, 0, false);       // into the output slot: refused
                Check.True(f.Output.IsEmpty);
                w.Click(0, 0, false);                               // put it back
                w.Click(1, 0, true);                                // shift-click coal: goes to the fuel slot
                Check.Equal(ItemIds.Coal, f.Fuel.Id);
                f.Output = S(ItemIds.IronIngot, 3);
                w.Click(PlayerInventory.Size + 2, 0, false);
                Check.Equal(ItemIds.IronIngot, inv.Cursor.Id);
            });
            Harness.Run("inventory clicks follow cursor rules", () =>
            {
                PlayerInventory inv = new PlayerInventory();
                InventoryProbe w = new InventoryProbe(inv);
                inv.Slots[0] = S(B.Stone, 10);
                inv.Slots[1] = S(B.Stone, 60);
                w.Click(0, 0, false);
                Check.Equal((byte)10, inv.Cursor.Count);
                w.Click(1, 0, false);                    // merge into 60: capped at 64
                Check.Equal((byte)64, inv.Slots[1].Count);
                Check.Equal((byte)6, inv.Cursor.Count);
                w.Click(2, 1, false);                    // right click drops one
                Check.Equal((byte)1, inv.Slots[2].Count);
                Check.Equal((byte)5, inv.Cursor.Count);
                w.Click(3, 0, false);                    // left click on an empty slot places all
                Check.Equal((byte)5, inv.Slots[3].Count);
                Check.True(inv.Cursor.IsEmpty);
                w.Click(1, 1, false);                    // right click on 64 takes half
                Check.Equal((byte)32, inv.Cursor.Count);
                Check.Equal((byte)32, inv.Slots[1].Count);
                int total = inv.Count(B.Stone) + inv.Cursor.Count;
                Check.Equal(10 + 60, total, "no items created or lost");
            });
            Harness.Run("add and remove respect stack limits", () =>
            {
                PlayerInventory inv = new PlayerInventory();
                for (int i = 0; i < 36; i++) Check.Equal(0, inv.Add(S(B.Dirt, 64)));
                Check.Equal(36 * 64, inv.Count(B.Dirt));
                Check.Equal(5, inv.Add(S(B.Dirt, 5)), "full inventory returns the leftover");
                Check.True(!inv.CanFit(S(B.Stone, 1)), "no room");
                Check.True(inv.Remove(B.Dirt, 100));
                Check.Equal(36 * 64 - 100, inv.Count(B.Dirt));
                Check.True(!inv.Remove(B.Stone, 1), "cannot remove what is absent");
                Check.Equal(0, inv.Add(S(B.Stone, 3)));
            });
            Harness.Run("tools wear and break", () =>
            {
                PlayerInventory inv = new PlayerInventory();
                inv.Slots[0] = S(ItemIds.WoodPickaxe, 1);
                int uses = 0;
                while (!inv.Held.IsEmpty && uses < 1000) { inv.WearHeld(); uses++; }
                Check.Equal(59, uses, "wooden pickaxe durability");
            });
        }
    }
}
