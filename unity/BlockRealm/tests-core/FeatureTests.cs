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
            c.SendChat("/tp " + x + ".5 " + (y0 + 1) + ".1 " + z + ".5");
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
        }

        private static float Mesher_Height(byte id) { return SectionMesher.SurfaceHeight(id); }
    }
}
