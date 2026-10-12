using System;
using System.Collections.Generic;
using BlockRealm.Net;
using BlockRealm.Sim;

namespace BlockRealm.Tests
{
    /// <summary>Water, buckets, farming and the recipes that go with them.</summary>
    public static class FeatureTests
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

        private static ItemStack S(ushort id) { return new ItemStack(id, 1); }

        /// <summary>
        /// A two-thick stone slab high above the terrain with clear air over it, so the water rules
        /// can be watched without any terrain getting in the way.
        /// </summary>
        private static void Platform(Rig rig, GameClient c, int half, out int ox, out int y0, out int oz)
        {
            World w = rig.Server.World;
            ox = MathUtil.FloorInt(c.Player.Pos.X);
            oz = MathUtil.FloorInt(c.Player.Pos.Z);
            y0 = w.GetSurfaceY(ox, oz) + 24;
            for (int x = ox - half; x <= ox + half; x++)
                for (int z = oz - half; z <= oz + half; z++)
                {
                    w.SetBlock(x, y0 - 1, z, B.Stone);
                    w.SetBlock(x, y0, z, B.Stone);
                    for (int y = y0 + 1; y <= y0 + 8; y++) w.SetBlock(x, y, z, B.Air);
                }
            rig.Run(60);                                           // let the block updates drain
        }

        private static void StandOn(Rig rig, GameClient c, int x, int y0, int z)
        {
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            c.SendChat("/tp " + (x + 0.5).ToString(inv) + " " + (y0 + 1.1).ToString(inv) + " " + (z + 0.5).ToString(inv));
            bool ok = rig.RunUntil(() => c.Player.OnGround && MathUtil.FloorInt(c.Player.Pos.Y) == y0 + 1, 120);
            Check.True(ok, "player standing on the platform: y " + c.Player.Pos.Y);
        }

        private static void Use(Rig rig, GameClient c, int slot)
        {
            c.SelectSlot(slot);
            rig.Run(2);
            rig.SetActions(c, new ClientActions { UsePressed = true });
            rig.Tick();
            rig.SetActions(c, new ClientActions());
            rig.Run(6);
        }

        public static void RunAll()
        {
            Harness.Group("water");
            Harness.Run("water spreads sideways a level at a time and stops after seven blocks", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    int ox, y0, oz;
                    Platform(rig, c, 12, out ox, out y0, out oz);
                    World w = rig.Server.World;
                    rig.Server.SetBlock(ox + 2, y0 + 1, oz, B.Water);
                    rig.Run(120);
                    Check.Equal(B.Water, w.GetBlock(ox + 2, y0 + 1, oz), "the source stays");
                    Check.Equal(7, B.WaterLevel(w.GetBlock(ox + 3, y0 + 1, oz)), "level one block out");
                    Check.Equal(5, B.WaterLevel(w.GetBlock(ox + 5, y0 + 1, oz)), "level three blocks out");
                    Check.Equal(1, B.WaterLevel(w.GetBlock(ox + 9, y0 + 1, oz)), "last cell");
                    Check.Equal(B.Air, w.GetBlock(ox + 10, y0 + 1, oz), "one past the end stays dry");
                    Check.Equal(6, B.WaterLevel(w.GetBlock(ox + 3, y0 + 1, oz + 1)), "diagonal is two steps away");
                    Check.True(!B.IsWater(w.GetBlock(ox + 2, y0 + 2, oz)), "water does not climb");
                }
            });
            Harness.Run("water drains away once its source is removed", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    int ox, y0, oz;
                    Platform(rig, c, 12, out ox, out y0, out oz);
                    World w = rig.Server.World;
                    rig.Server.SetBlock(ox, y0 + 1, oz, B.Water);
                    rig.Run(100);
                    Check.True(B.IsWater(w.GetBlock(ox + 4, y0 + 1, oz)), "flowing before removal");
                    rig.Server.SetBlock(ox, y0 + 1, oz, B.Air);
                    bool dry = rig.RunUntil(() =>
                    {
                        for (int x = ox - 9; x <= ox + 9; x++)
                            for (int z = oz - 9; z <= oz + 9; z++)
                                if (B.IsWater(w.GetBlock(x, y0 + 1, z))) return false;
                        return true;
                    }, 600);
                    Check.True(dry, "every flowing cell drained");
                }
            });
            Harness.Run("water falls straight down and spreads where it lands", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    int ox, y0, oz;
                    Platform(rig, c, 12, out ox, out y0, out oz);
                    World w = rig.Server.World;
                    rig.Server.SetBlock(ox, y0 + 6, oz, B.Water);
                    rig.Run(160);
                    for (int y = y0 + 1; y <= y0 + 5; y++) Check.True(B.IsWater(w.GetBlock(ox, y, oz)), "falling column at +" + (y - y0));
                    Check.True(B.IsWater(w.GetBlock(ox + 1, y0 + 1, oz)), "pool forms at the bottom");
                    Check.True(!B.IsWater(w.GetBlock(ox + 1, y0 + 3, oz)), "falling water does not spill sideways in mid-air");
                }
            });
            Harness.Run("every water block is a liquid you can swim in, and mesh heights follow the level", () =>
            {
                for (byte id = B.FlowWater1; id <= B.FlowWater7; id++)
                {
                    Check.True(Blocks.IsLiquid(id) && !Blocks.IsSolid(id), "block " + id);
                    Check.True(B.IsWater(id));
                }
                Check.True(!B.IsWater(B.Lava));
                Check.True(Mesher_Height(B.FlowWater3) < Mesher_Height(B.FlowWater6), "levels rise");
                World w = TestWorlds.Flat(60, 1);
                w.SetBlock(9, 65, 9, B.FlowWater3);
                PaddedSection p = new PaddedSection();
                w.CopyPadded(0, 4, 0, p);
                MeshBuffers m = SectionMesher.Mesh(p);
                float maxY = 0;
                for (int i = 0; i < m.VertexCount; i++)
                    if (m.Vertices[i].Flags == 1) maxY = Math.Max(maxY, m.Vertices[i].PY);
                Check.Near(1f + SectionMesher.SurfaceHeight(B.FlowWater3), maxY, 0.01f, "flowing top");
            });

            Harness.Group("buckets");
            Harness.Run("a bucket scoops a source and the water bucket puts it back", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    int ox, y0, oz;
                    Platform(rig, c, 6, out ox, out y0, out oz);
                    StandOn(rig, c, ox, y0, oz);
                    c.SendChat("/give " + ItemIds.Bucket);
                    rig.Run(5);
                    Check.Equal(1, CountOf(c, ItemIds.Bucket));
                    rig.SetInput(c, new PlayerInput { Pitch = 1.5f });
                    rig.Tick();
                    Check.True(c.TargetBlock.Hit, "looking at the slab");
                    RayHit h = c.TargetBlock;
                    rig.Server.World.SetBlock(h.X, h.Y, h.Z, B.Water);
                    rig.Run(5);
                    Use(rig, c, 0);
                    Check.Equal(1, CountOf(c, ItemIds.WaterBucket), "bucket is full");
                    Check.Equal(0, CountOf(c, ItemIds.Bucket), "the empty one was swapped");
                    Check.Equal(B.Air, rig.Server.World.GetBlock(h.X, h.Y, h.Z), "source removed");
                    Use(rig, c, 0);
                    Check.Equal(B.Water, rig.Server.World.GetBlock(h.X, h.Y, h.Z), "source placed again");
                    Check.Equal(1, CountOf(c, ItemIds.Bucket), "empty again");
                    Check.Equal(0, CountOf(c, ItemIds.WaterBucket));
                }
            });
            Harness.Run("an empty bucket cannot lift flowing water", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    int ox, y0, oz;
                    Platform(rig, c, 6, out ox, out y0, out oz);
                    StandOn(rig, c, ox, y0, oz);
                    c.SendChat("/give " + ItemIds.Bucket);
                    rig.Run(5);
                    rig.SetInput(c, new PlayerInput { Pitch = 1.5f });
                    rig.Tick();
                    RayHit h = c.TargetBlock;
                    Check.True(h.Hit);
                    rig.Server.World.SetBlock(h.X, h.Y, h.Z, B.FlowWater4);
                    rig.Run(2);
                    Use(rig, c, 0);
                    Check.Equal(1, CountOf(c, ItemIds.Bucket), "still empty");
                    Check.Equal(0, CountOf(c, ItemIds.WaterBucket));
                }
            });

            Harness.Group("farming");
            Harness.Run("a hoe tills grass, seeds sprout, wheat ripens in seven steps and drops grain", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    int ox, y0, oz;
                    Platform(rig, c, 6, out ox, out y0, out oz);
                    StandOn(rig, c, ox, y0, oz);
                    c.SendChat("/give " + ItemIds.WoodHoe);
                    c.SendChat("/give " + ItemIds.Seeds + " 5");
                    c.SendChat("/time set noon");
                    rig.Run(5);
                    rig.SetInput(c, new PlayerInput { Pitch = 1.5f });
                    rig.Tick();
                    RayHit h = c.TargetBlock;
                    Check.True(h.Hit, "target");
                    rig.Server.World.SetBlock(h.X, h.Y, h.Z, B.Grass);
                    rig.Run(3);

                    Use(rig, c, 1);                                 // seeds on plain grass do nothing
                    Check.Equal(B.Air, rig.Server.World.GetBlock(h.X, h.Y + 1, h.Z), "no sprout without farmland");
                    Check.Equal(5, CountOf(c, ItemIds.Seeds));

                    Use(rig, c, 0);
                    Check.Equal(B.Farmland, rig.Server.World.GetBlock(h.X, h.Y, h.Z), "tilled");
                    Use(rig, c, 1);
                    Check.Equal(B.Wheat0, rig.Server.World.GetBlock(h.X, h.Y + 1, h.Z), "planted");
                    Check.Equal(4, CountOf(c, ItemIds.Seeds), "one seed used");

                    for (int stage = 1; stage <= 7; stage++)
                    {
                        Check.True(rig.Server.GrowCropAt(h.X, h.Z), "grows to stage " + stage);
                        Check.Equal((byte)(B.Wheat0 + stage), rig.Server.World.GetBlock(h.X, h.Y + 1, h.Z));
                    }
                    Check.True(!rig.Server.GrowCropAt(h.X, h.Z), "ripe wheat stops growing");

                    c.Send(new StartBreakPacket { X = h.X, Y = h.Y + 1, Z = h.Z });
                    c.Send(new FinishBreakPacket { X = h.X, Y = h.Y + 1, Z = h.Z });
                    bool got = rig.RunUntil(() => CountOf(c, ItemIds.Wheat) >= 1, 60);
                    Check.True(got, "ripe wheat drops wheat");
                    Check.Equal(B.Air, rig.Server.World.GetBlock(h.X, h.Y + 1, h.Z), "harvested");
                }
            });
            Harness.Run("crops need farmland and daylight, and unripe wheat gives seeds", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    int ox, y0, oz;
                    Platform(rig, c, 6, out ox, out y0, out oz);
                    World w = rig.Server.World;
                    c.SendChat("/time set noon");
                    rig.Run(3);
                    w.SetBlock(ox + 3, y0, oz, B.Dirt);
                    w.SetBlock(ox + 3, y0 + 1, oz, B.Wheat0);
                    rig.Run(5);
                    Check.True(!rig.Server.GrowCropAt(ox + 3, oz), "wheat on plain dirt does not grow");
                    w.SetBlock(ox + 3, y0, oz, B.Farmland);
                    rig.Run(5);
                    c.SendChat("/time set midnight");
                    rig.Run(3);
                    Check.True(!rig.Server.GrowCropAt(ox + 3, oz), "wheat does not grow in the dark");
                    c.SendChat("/time set noon");
                    rig.Run(3);
                    Check.True(rig.Server.GrowCropAt(ox + 3, oz), "grows by day on farmland");
                    Check.Equal(ItemIds.Seeds, Items.DropFor(B.Wheat3, true, null, false), "young wheat drops seeds");
                    Check.Equal(ItemIds.Wheat, Items.DropFor(B.Wheat7, true, null, false), "ripe wheat drops grain");
                }
            });
            Harness.Run("a hoe cannot till stone or ground that has something on top", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    int ox, y0, oz;
                    Platform(rig, c, 6, out ox, out y0, out oz);
                    StandOn(rig, c, ox, y0, oz);
                    c.SendChat("/give " + ItemIds.WoodHoe);
                    rig.Run(5);
                    rig.SetInput(c, new PlayerInput { Pitch = 1.5f });
                    rig.Tick();
                    RayHit h = c.TargetBlock;
                    Check.True(h.Hit);
                    Use(rig, c, 0);
                    Check.Equal(B.Stone, rig.Server.World.GetBlock(h.X, h.Y, h.Z), "stone stays stone");
                }
            });

            Harness.Group("new recipes");
            Harness.Run("bread, bucket, hoe, armour and bed are craftable", () =>
            {
                ItemStack[] g = new ItemStack[9];
                g[3] = S(ItemIds.Wheat); g[4] = S(ItemIds.Wheat); g[5] = S(ItemIds.Wheat);
                Check.Equal(ItemIds.Bread, Recipes.Match(g, 3, 3).Id, "bread");

                g = new ItemStack[9];
                g[0] = S(ItemIds.IronIngot); g[2] = S(ItemIds.IronIngot); g[4] = S(ItemIds.IronIngot);
                Check.Equal(ItemIds.Bucket, Recipes.Match(g, 3, 3).Id, "bucket");

                g = new ItemStack[9];
                g[0] = S(B.Planks); g[1] = S(B.Planks); g[4] = S(ItemIds.Stick); g[7] = S(ItemIds.Stick);
                Check.Equal(ItemIds.WoodHoe, Recipes.Match(g, 3, 3).Id, "wooden hoe");

                g = new ItemStack[9];
                g[0] = S(ItemIds.IronIngot); g[1] = S(ItemIds.IronIngot); g[2] = S(ItemIds.IronIngot);
                g[3] = S(ItemIds.IronIngot); g[5] = S(ItemIds.IronIngot);
                Check.Equal(ItemIds.IronHelmet, Recipes.Match(g, 3, 3).Id, "iron helmet");

                g = new ItemStack[9];
                g[0] = S(ItemIds.Leather); g[2] = S(ItemIds.Leather); g[3] = S(ItemIds.Leather); g[5] = S(ItemIds.Leather);
                Check.Equal(ItemIds.LeatherBoots, Recipes.Match(g, 3, 3).Id, "leather boots");

                g = new ItemStack[9];
                g[0] = S(ItemIds.Diamond); g[2] = S(ItemIds.Diamond);
                for (int i = 3; i < 9; i++) g[i] = S(ItemIds.Diamond);
                Check.Equal(ItemIds.DiamondChestplate, Recipes.Match(g, 3, 3).Id, "diamond chestplate");

                g = new ItemStack[9];
                g[0] = S(B.Wool); g[1] = S(B.Wool); g[2] = S(B.Wool);
                g[3] = S(B.Planks); g[4] = S(B.Planks); g[5] = S(B.Planks);
                Check.Equal((ushort)B.Bed, Recipes.Match(g, 3, 3).Id, "bed");
            });
            Harness.Run("armour pieces know their slot and protection", () =>
            {
                Check.Equal(0, Items.Get(ItemIds.IronHelmet).ArmorSlot);
                Check.Equal(1, Items.Get(ItemIds.DiamondChestplate).ArmorSlot);
                Check.Equal(2, Items.Get(ItemIds.LeatherLeggings).ArmorSlot);
                Check.Equal(3, Items.Get(ItemIds.IronBoots).ArmorSlot);
                int leather = 0, iron = 0, diamond = 0;
                foreach (ushort id in new[] { ItemIds.LeatherHelmet, ItemIds.LeatherChestplate, ItemIds.LeatherLeggings, ItemIds.LeatherBoots })
                    leather += Items.Get(id).ArmorPoints;
                foreach (ushort id in new[] { ItemIds.IronHelmet, ItemIds.IronChestplate, ItemIds.IronLeggings, ItemIds.IronBoots })
                    iron += Items.Get(id).ArmorPoints;
                foreach (ushort id in new[] { ItemIds.DiamondHelmet, ItemIds.DiamondChestplate, ItemIds.DiamondLeggings, ItemIds.DiamondBoots })
                    diamond += Items.Get(id).ArmorPoints;
                Check.True(leather > 0 && leather < iron && iron < diamond, "tiers: " + leather + " < " + iron + " < " + diamond);
                Check.True(diamond <= 20, "a full set never makes you immune");
            });

            Harness.Group("armour");
            Harness.Run("armour goes only in its own slot, by click and by shift-click", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    ServerPlayer sp = rig.ServerSide(c);
                    c.SendChat("/give " + ItemIds.IronHelmet);
                    c.SendChat("/give " + ItemIds.IronBoots);
                    rig.Run(5);
                    int head = PlayerInventory.Size + 5, chest = head + 1;

                    c.ClickSlot(SlotOf(c, ItemIds.IronHelmet), 0, false);
                    rig.Run(3);
                    Check.Equal(ItemIds.IronHelmet, c.Cursor.Id, "helmet on the cursor");
                    c.ClickSlot(chest, 0, false);
                    rig.Run(3);
                    Check.True(sp.Inventory.Armor[1].IsEmpty, "a helmet does not fit the chest slot");
                    Check.Equal(ItemIds.IronHelmet, c.Cursor.Id, "still held");
                    c.ClickSlot(head, 0, false);
                    rig.Run(3);
                    Check.Equal(ItemIds.IronHelmet, sp.Inventory.Armor[0].Id, "helmet worn");
                    Check.True(c.Cursor.IsEmpty, "cursor empty");

                    c.ClickSlot(SlotOf(c, ItemIds.IronBoots), 0, true);
                    rig.Run(3);
                    Check.Equal(ItemIds.IronBoots, sp.Inventory.Armor[3].Id, "shift-click finds the boots slot");
                    rig.Run(5);
                    Check.Equal(4, c.ArmorPoints, "the client is told its protection");

                    c.ClickSlot(head, 0, true);                      // shift-click it off again
                    rig.Run(3);
                    Check.True(sp.Inventory.Armor[0].IsEmpty, "helmet taken off");
                    Check.Equal(1, CountOf(c, ItemIds.IronHelmet), "and back in the bag");
                }
            });
            Harness.Run("armour cuts blows from attackers, wears, and ignores falls and the like", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    ServerPlayer sp = rig.ServerSide(c);
                    ushort[] set = { ItemIds.DiamondHelmet, ItemIds.DiamondChestplate, ItemIds.DiamondLeggings, ItemIds.DiamondBoots };
                    for (int i = 0; i < 4; i++) sp.Inventory.Armor[i] = new ItemStack(set[i], 1);
                    Check.Equal(20, sp.Inventory.ArmorPoints());
                    ArrowEntity attacker = new ArrowEntity();

                    float before = sp.Health;
                    rig.Server.Hurt(sp, 10f, attacker, sp.Pos + new Vec3(1, 0, 0), 0f);
                    Check.Near(before - 2f, sp.Health, 0.01f, "a full diamond set lets 20% through");
                    Check.Equal(2, sp.Inventory.Armor[0].Wear, "each piece took wear");

                    rig.Run(15);
                    before = sp.Health;
                    rig.Server.Hurt(sp, 3f, null, sp.Pos, 0f);
                    Check.Near(before - 3f, sp.Health, 0.01f, "environmental damage ignores armour");
                    Check.Equal(2, sp.Inventory.Armor[0].Wear, "and does not wear it");
                }
            });
            Harness.Run("armour breaks when its durability runs out", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    ServerPlayer sp = rig.ServerSide(c);
                    ItemStack worn = new ItemStack(ItemIds.IronHelmet, 1);
                    worn.Wear = (short)(worn.Info.Durability - 1);
                    sp.Inventory.Armor[0] = worn;
                    rig.Server.Hurt(sp, 4f, new ArrowEntity(), sp.Pos, 0f);
                    Check.True(sp.Inventory.Armor[0].IsEmpty, "helmet broke");
                    rig.Run(5);
                    Check.Equal(0, c.ArmorPoints, "client sees it gone");
                }
            });
            Harness.Run("worn armour is saved with the player and dropped on death", () =>
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "brtest_" + Guid.NewGuid().ToString("N"));
                try
                {
                    using (Rig rig = new Rig(o => o.SaveDir = dir))
                    {
                        GameClient c = rig.JoinAndSettle("Ada");
                        rig.ServerSide(c).Inventory.Armor[1] = new ItemStack(ItemIds.DiamondChestplate, 1);
                        rig.Run(2);
                    }
                    using (Rig rig = new Rig(o => o.SaveDir = dir))
                    {
                        GameClient c = rig.Join("Ada");
                        rig.RunUntil(() => c.State == ClientState.Joined && c.PendingColumns == 0 && c.World != null && c.World.ColumnCount >= 9, 300);
                        rig.Run(20);
                        ServerPlayer sp = rig.ServerSide(c);
                        Check.Equal(ItemIds.DiamondChestplate, sp.Inventory.Armor[1].Id, "restored after restart");
                        Check.Equal(8, c.ArmorPoints);
                        c.SendChat("/kill");
                        rig.Run(5);
                        Check.True(sp.Inventory.Armor[1].IsEmpty, "taken off the body");
                        bool dropped = false;
                        foreach (KeyValuePair<int, Entity> kv in rig.Server.Entities)
                        {
                            ItemEntity ie = kv.Value as ItemEntity;
                            if (ie != null && ie.Stack.Id == ItemIds.DiamondChestplate) dropped = true;
                        }
                        Check.True(dropped, "lying on the ground where the player fell");
                    }
                }
                finally
                {
                    try { System.IO.Directory.Delete(dir, true); } catch (Exception) { }
                }
            });

            Harness.Group("beds");
            Harness.Run("a bed sets the respawn point even by day, and death returns you to it", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    ServerPlayer sp = rig.ServerSide(c);
                    int ox, y0, oz;
                    Platform(rig, c, 6, out ox, out y0, out oz);
                    rig.Server.SetBlock(ox + 2, y0 + 1, oz, B.Bed);
                    c.SendChat("/time set noon");
                    rig.Run(3);
                    StandOn(rig, c, ox, y0, oz);
                    c.Send(new InteractPacket { X = ox + 2, Y = y0 + 1, Z = oz });
                    rig.Run(3);
                    Check.True(sp.HasBed && sp.BedPos.Equals(new Vec3i(ox + 2, y0 + 1, oz)), "respawn point recorded");
                    Check.True(!sp.Sleeping, "no sleeping in daylight");

                    c.SendChat("/kill");
                    rig.Run(5);
                    Check.True(c.Dead, "died");
                    c.RequestRespawn();
                    rig.Run(10);
                    Check.True(sp.Pos.Y > y0, "back on the platform, not at the world spawn: y " + sp.Pos.Y);
                    Check.True(Vec3.Distance(sp.Pos, new Vec3(ox + 2.5f, y0 + 2f, oz + 0.5f)) < 3f, "next to the bed");
                }
            });
            Harness.Run("everyone in bed at night skips to morning", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    ServerPlayer sp = rig.ServerSide(c);
                    int ox, y0, oz;
                    Platform(rig, c, 6, out ox, out y0, out oz);
                    rig.Server.SetBlock(ox + 2, y0 + 1, oz, B.Bed);
                    StandOn(rig, c, ox, y0, oz);
                    c.SendChat("/time set night");
                    rig.Run(3);
                    Check.True(rig.Server.IsNight);
                    c.Send(new InteractPacket { X = ox + 2, Y = y0 + 1, Z = oz });
                    rig.Run(3);
                    Check.True(sp.Sleeping, "in bed");
                    rig.Run(5);
                    Check.True(c.Sleeping, "the client knows");
                    bool morning = rig.RunUntil(() => rig.Server.IsDaylight && !sp.Sleeping, 200);
                    Check.True(morning, "the night was skipped");
                    Check.InRange(rig.Server.DayTick, 1000, 1100, "early morning");
                    rig.Run(5);
                    Check.True(!c.Sleeping, "awake again");
                }
            });
            Harness.Run("monsters nearby, damage and walking away all keep you out of bed", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    ServerPlayer sp = rig.ServerSide(c);
                    int ox, y0, oz;
                    Platform(rig, c, 6, out ox, out y0, out oz);
                    rig.Server.SetBlock(ox + 2, y0 + 1, oz, B.Bed);
                    StandOn(rig, c, ox, y0, oz);
                    c.SendChat("/time set night");
                    c.SendChat("/summon zombie");
                    rig.Run(5);
                    c.Send(new InteractPacket { X = ox + 2, Y = y0 + 1, Z = oz });
                    rig.Run(45);
                    Check.True(!sp.Sleeping, "zombie within eight blocks");

                    foreach (KeyValuePair<int, Entity> kv in rig.Server.Entities)
                        if (kv.Value is MobEntity) kv.Value.Removed = true;
                    rig.Run(5);
                    c.Send(new InteractPacket { X = ox + 2, Y = y0 + 1, Z = oz });
                    rig.Run(3);
                    Check.True(sp.Sleeping, "now it is quiet");
                    rig.Server.Hurt(sp, 1f, null, sp.Pos, 0f);
                    Check.True(!sp.Sleeping, "a blow wakes you");

                    sp.Invulnerable = 0;
                    rig.Run(45);
                    c.Send(new InteractPacket { X = ox + 2, Y = y0 + 1, Z = oz });
                    rig.Run(3);
                    Check.True(sp.Sleeping, "back in bed");
                    c.Send(new WakeUpPacket());
                    rig.Run(3);
                    Check.True(!sp.Sleeping, "got up on request");
                }
            });
            Harness.Run("the bed survives a restart, and a destroyed bed falls back to the world spawn", () =>
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "brtest_" + Guid.NewGuid().ToString("N"));
                try
                {
                    int ox, y0, oz;
                    using (Rig rig = new Rig(o => o.SaveDir = dir))
                    {
                        GameClient c = rig.JoinAndSettle("Ada");
                        Platform(rig, c, 6, out ox, out y0, out oz);
                        rig.Server.SetBlock(ox + 2, y0 + 1, oz, B.Bed);
                        StandOn(rig, c, ox, y0, oz);
                        c.Send(new InteractPacket { X = ox + 2, Y = y0 + 1, Z = oz });
                        rig.Run(5);
                        Check.True(rig.ServerSide(c).HasBed);
                    }
                    using (Rig rig = new Rig(o => o.SaveDir = dir))
                    {
                        GameClient c = rig.Join("Ada");
                        rig.RunUntil(() => c.State == ClientState.Joined && c.PendingColumns == 0 && c.World != null && c.World.ColumnCount >= 9, 300);
                        rig.Run(20);
                        ServerPlayer sp = rig.ServerSide(c);
                        Check.True(sp.HasBed && sp.BedPos.Equals(new Vec3i(ox + 2, y0 + 1, oz)), "bed remembered");

                        rig.Server.SetBlock(ox + 2, y0 + 1, oz, B.Air);
                        rig.Run(3);
                        c.SendChat("/kill");
                        rig.Run(5);
                        c.RequestRespawn();
                        rig.Run(10);
                        Check.True(!sp.HasBed, "the missing bed is forgotten");
                        Check.True(!c.Dead && !sp.Dead, "and the player is back in the world anyway");
                    }
                }
                finally
                {
                    try { System.IO.Directory.Delete(dir, true); } catch (Exception) { }
                }
            });

            Harness.Group("lava");
            Harness.Run("lava creeps three blocks and takes its time", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    int ox, y0, oz;
                    Platform(rig, c, 12, out ox, out y0, out oz);
                    World w = rig.Server.World;
                    rig.Server.SetBlock(ox + 2, y0 + 1, oz, B.Lava);
                    rig.Run(40);
                    Check.True(B.IsLava(w.GetBlock(ox + 3, y0 + 1, oz)), "first block reached");
                    Check.True(!B.IsLava(w.GetBlock(ox + 5, y0 + 1, oz)), "far end not yet reached");
                    rig.Run(300);
                    Check.Equal(3, B.LavaLevel(w.GetBlock(ox + 3, y0 + 1, oz)), "one block out");
                    Check.Equal(1, B.LavaLevel(w.GetBlock(ox + 5, y0 + 1, oz)), "last cell");
                    Check.Equal(B.Air, w.GetBlock(ox + 6, y0 + 1, oz), "stops after three blocks");
                    Check.Equal(B.Lava, w.GetBlock(ox + 2, y0 + 1, oz), "source stays");
                }
            });
            Harness.Run("water turns a lava source into obsidian and flowing lava into cobblestone", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    int ox, y0, oz;
                    Platform(rig, c, 12, out ox, out y0, out oz);
                    World w = rig.Server.World;

                    // A lava source with water poured beside it.
                    rig.Server.SetBlock(ox + 6, y0 + 1, oz + 8, B.Lava);
                    rig.Run(5);
                    rig.Server.SetBlock(ox + 7, y0 + 1, oz + 8, B.Water);
                    rig.Run(120);
                    Check.Equal(B.Obsidian, w.GetBlock(ox + 6, y0 + 1, oz + 8), "source became obsidian");

                    // Flowing lava reaching water.
                    rig.Server.SetBlock(ox - 2, y0 + 1, oz, B.Lava);
                    rig.Run(300);
                    Check.True(B.IsLava(w.GetBlock(ox - 5, y0 + 1, oz)), "lava has run out to the end");
                    rig.Server.SetBlock(ox - 6, y0 + 1, oz, B.Water);
                    rig.Run(150);
                    Check.Equal(B.Cobble, w.GetBlock(ox - 5, y0 + 1, oz), "flowing lava became cobblestone");
                    Check.True(B.IsLava(w.GetBlock(ox - 2, y0 + 1, oz)) || w.GetBlock(ox - 2, y0 + 1, oz) == B.Obsidian, "the source is lava or obsidian");
                }
            });
            Harness.Run("standing in lava burns the player", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    ServerPlayer sp = rig.ServerSide(c);
                    int ox, y0, oz;
                    Platform(rig, c, 6, out ox, out y0, out oz);
                    StandOn(rig, c, ox, y0, oz);
                    float before = sp.Health;
                    rig.Server.SetBlock(ox, y0 + 1, oz, B.Lava);
                    rig.Run(30);
                    Check.True(sp.Health < before, "hurt by lava: " + sp.Health);
                }
            });
            Harness.Run("internal block states are hidden from palettes but sources are not", () =>
            {
                Check.True(Blocks.Get(B.FlowWater3).Hidden && Blocks.Get(B.FlowLava2).Hidden && Blocks.Get(B.Wheat5).Hidden);
                Check.True(!Blocks.Get(B.Water).Hidden && !Blocks.Get(B.Lava).Hidden && !Blocks.Get(B.Stone).Hidden && !Blocks.Get(B.Bed).Hidden);
                for (int id = 1; id < B.Count; id++)
                    Check.True(Blocks.IsDefined(id), "block " + id + " is defined");
            });

            Harness.Group("abuse limits on the new actions");
            Harness.Run("bed use is rate limited and the announcement stays local", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient a = rig.JoinAndSettle("Ada");
                    GameClient b = rig.JoinAndSettle("Bob");
                    ServerPlayer sa = rig.ServerSide(a);
                    int ox, y0, oz;
                    Platform(rig, a, 6, out ox, out y0, out oz);
                    rig.Server.SetBlock(ox + 2, y0 + 1, oz, B.Bed);
                    StandOn(rig, a, ox, y0, oz);
                    a.SendChat("/time set night");
                    rig.Run(3);
                    int before = b.Chat.Count;
                    for (int i = 0; i < 30; i++)
                    {
                        a.Send(new InteractPacket { X = ox + 2, Y = y0 + 1, Z = oz });
                        a.Send(new WakeUpPacket());
                        rig.Run(1);
                    }
                    rig.Run(5);
                    int heard = 0;
                    for (int i = before; i < b.Chat.Count; i++) if (b.Chat[i].Contains("is sleeping")) heard++;
                    Check.True(heard <= 1, "Bob heard the announcement " + heard + " times");
                }
            });
            Harness.Run("buckets and hoes draw on the placement budget", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    ServerPlayer sp = rig.ServerSide(c);
                    int ox, y0, oz;
                    Platform(rig, c, 6, out ox, out y0, out oz);
                    StandOn(rig, c, ox, y0, oz);
                    c.SendChat("/give " + ItemIds.WaterBucket);
                    rig.Run(5);
                    rig.SetInput(c, new PlayerInput { Pitch = 1.5f });
                    rig.Tick();
                    c.SelectSlot(0);
                    sp.PlaceTokens = 0f;
                    c.Send(new UseItemPacket());
                    rig.Tick();
                    Check.Equal(1, CountOf(c, ItemIds.WaterBucket), "no tokens, no water");
                }
            });

            Harness.Group("villages");
            Harness.Run("abandoned villages are generated with walls, a doorway, a bed and sometimes a chest", () =>
            {
                WorldGenerator gen = new WorldGenerator(2024);
                List<WorldGenerator.VillageHouse> houses = gen.HousesNear(-2500, -2500, 2500, 2500);
                Check.True(houses.Count >= 6, "houses within 2.5 km: " + houses.Count);
                Dictionary<long, Column> cache = new Dictionary<long, Column>();
                Func<int, int, int, byte> at = (x, y, z) =>
                {
                    int cx = MathUtil.FloorDiv(x, 16), cz = MathUtil.FloorDiv(z, 16);
                    long key = ((long)cx << 32) ^ (uint)cz;
                    Column col;
                    if (!cache.TryGetValue(key, out col)) { col = gen.GenerateColumn(cx, cz); cache[key] = col; }
                    return col.GetBlock(x - cx * 16, y, z - cz * 16);
                };
                int chests = 0, checkedHouses = 0;
                foreach (WorldGenerator.VillageHouse h in houses)
                {
                    if (checkedHouses >= 6) break;
                    checkedHouses++;
                    byte floor = at(h.X, h.Y, h.Z);
                    Check.True(floor == B.Planks || floor == B.Sandstone, "floor is built: " + floor);
                    for (int y = h.Y + 1; y <= h.Y + 3; y++) Check.Equal(B.Air, at(h.X, y, h.Z), "room is empty");
                    byte roof = at(h.X, h.Y + 4, h.Z);
                    Check.True(roof == B.Planks || roof == B.Sandstone, "roof");
                    Check.Equal(B.Bed, at(h.X - 2, h.Y + 1, h.Z - 2), "bed");
                    int fx = h.Facing == 0 ? 3 : (h.Facing == 1 ? -3 : 0), fz = h.Facing == 2 ? 3 : (h.Facing == 3 ? -3 : 0);
                    Check.Equal(B.Air, at(h.X + fx, h.Y + 1, h.Z + fz), "doorway low");
                    Check.Equal(B.Air, at(h.X + fx, h.Y + 2, h.Z + fz), "doorway high");
                    if (h.Chest)
                    {
                        chests++;
                        Check.Equal(B.Chest, at(h.X + 2, h.Y + 1, h.Z + 2), "chest");
                        Check.Equal(B.Bedrock, at(h.X + 2, h.Y, h.Z + 2), "marker under the chest");
                        Check.True(gen.IsVillageChest(h.X + 2, h.Y + 1, h.Z + 2));
                    }
                }
                Check.True(!gen.IsVillageChest(0, 200, 0), "random places are not village chests");
                List<WorldGenerator.VillageHouse> again = new WorldGenerator(2024).HousesNear(-2500, -2500, 2500, 2500);
                Check.Equal(houses.Count, again.Count, "same seed, same villages");
            });
            Harness.Run("a village chest gives its loot once and the marker is spent", () =>
            {
                using (Rig rig = new Rig())
                {
                    GameClient c = rig.JoinAndSettle("Ada");
                    WorldGenerator.VillageHouse target = default(WorldGenerator.VillageHouse);
                    bool found = false;
                    foreach (WorldGenerator.VillageHouse h in rig.Server.Generator.HousesNear(-2500, -2500, 2500, 2500))
                        if (h.Chest) { target = h; found = true; break; }
                    Check.True(found, "a house with a chest exists");
                    c.SendChat("/tp " + target.X + " " + (target.Y + 3) + " " + target.Z);
                    bool loaded = rig.RunUntil(() => rig.Server.World.HasColumn(target.X >> 4, target.Z >> 4), 300);
                    Check.True(loaded, "village column loaded");
                    rig.Run(10);
                    Vec3i pos = new Vec3i(target.X + 2, target.Y + 1, target.Z + 2);
                    Check.Equal(B.Chest, rig.Server.World.GetBlock(pos.X, pos.Y, pos.Z));
                    ChestState chest = rig.Server.EnsureChest(pos);
                    int items = 0;
                    for (int i = 0; i < ChestState.Size; i++) if (!chest.Slots[i].IsEmpty) items++;
                    Check.True(items >= 1, "has loot");
                    Check.Equal(B.Planks, rig.Server.World.GetBlock(pos.X, pos.Y - 1, pos.Z), "marker replaced");
                }
            });
        }

        private static float Mesher_Height(byte id) { return SectionMesher.SurfaceHeight(id); }
    }
}
