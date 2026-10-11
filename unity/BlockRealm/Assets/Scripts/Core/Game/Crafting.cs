using System;
using System.Collections.Generic;

namespace BlockRealm.Sim
{
    public sealed class ShapedRecipe
    {
        public int Width, Height;
        public ushort[] Cells;               // row-major; 0 = must be empty
        public ItemStack Result;
    }

    public sealed class ShapelessRecipe
    {
        public ushort[] Ingredients;
        public ItemStack Result;
    }

    public sealed class SmeltRecipe
    {
        public ushort Input;
        public ItemStack Result;
    }

    public static class Recipes
    {
        /// <summary>Ingredient tag that matches any log.</summary>
        public const ushort AnyLog = 0xF001;

        public static readonly List<ShapedRecipe> Shaped = new List<ShapedRecipe>();
        public static readonly List<ShapelessRecipe> Shapeless = new List<ShapelessRecipe>();
        public static readonly List<SmeltRecipe> Smelting = new List<SmeltRecipe>();

        static Recipes()
        {
            const ushort P = B.Planks, S = ItemIds.Stick, C = B.Cobble, I = ItemIds.IronIngot, D = ItemIds.Diamond;

            AddShapeless(new[] { AnyLog }, B.Planks, 4);
            AddShaped(ItemIds.Stick, 4, "P", "P", P);
            AddShaped(B.CraftingTable, 1, "PP", "PP", P);
            AddShaped(B.Chest, 1, "PPP", "P.P", "PPP", P);
            AddShaped(B.Furnace, 1, "CCC", "C.C", "CCC", C);
            AddShaped(B.Torch, 4, "K", "S", ItemIds.Coal, S);
            AddShaped(B.Sandstone, 1, "SS", "SS", B.Sand);

            Tools(ItemIds.WoodPickaxe, ItemIds.WoodAxe, ItemIds.WoodShovel, ItemIds.WoodSword, P);
            Tools(ItemIds.StonePickaxe, ItemIds.StoneAxe, ItemIds.StoneShovel, ItemIds.StoneSword, C);
            Tools(ItemIds.IronPickaxe, ItemIds.IronAxe, ItemIds.IronShovel, ItemIds.IronSword, I);
            Tools(ItemIds.DiamondPickaxe, ItemIds.DiamondAxe, ItemIds.DiamondShovel, ItemIds.DiamondSword, D);
            AddShaped(ItemIds.Shears, 1, ".X", "X.", I);
            AddShaped(ItemIds.Bow, 1, ".SF", "S.F", ".SF", S, ItemIds.Feather);
            AddShaped(ItemIds.Arrow, 4, "K", "S", "F", ItemIds.Coal, S, ItemIds.Feather);
            AddShaped(ItemIds.Saddle, 1, "LLL", "LDL", "I.I", ItemIds.Leather, ItemIds.DragonScale, I);
            AddShaped(B.Bricks, 1, "CC", "CC", B.Clay);
            Armour(ItemIds.LeatherHelmet, ItemIds.LeatherChestplate, ItemIds.LeatherLeggings, ItemIds.LeatherBoots, ItemIds.Leather);
            Armour(ItemIds.IronHelmet, ItemIds.IronChestplate, ItemIds.IronLeggings, ItemIds.IronBoots, I);
            Armour(ItemIds.DiamondHelmet, ItemIds.DiamondChestplate, ItemIds.DiamondLeggings, ItemIds.DiamondBoots, D);
            AddShaped(ItemIds.WoodHoe, 1, "XX", ".S", ".S", P, S);
            AddShaped(ItemIds.StoneHoe, 1, "XX", ".S", ".S", C, S);
            AddShaped(ItemIds.IronHoe, 1, "XX", ".S", ".S", I, S);
            AddShaped(ItemIds.DiamondHoe, 1, "XX", ".S", ".S", D, S);
            AddShaped(ItemIds.Bucket, 1, "X.X", ".X.", I);
            AddShaped(ItemIds.Bread, 1, "WWW", ItemIds.Wheat);
            AddShaped(B.Bed, 1, "WWW", "PPP", B.Wool, P);

            Smelt(B.IronOre, ItemIds.IronIngot);
            Smelt(B.GoldOre, ItemIds.GoldIngot);
            Smelt(B.Sand, B.Glass);
            Smelt(B.Cobble, B.Stone);
            Smelt(B.Clay, B.Bricks);
            Smelt(B.LogOak, ItemIds.Charcoal);
            Smelt(B.LogSpruce, ItemIds.Charcoal);
            Smelt(B.LogBirch, ItemIds.Charcoal);
            Smelt(ItemIds.RawPork, ItemIds.CookedPork);
            Smelt(ItemIds.RawBeef, ItemIds.Steak);
            Smelt(ItemIds.RawChicken, ItemIds.CookedChicken);
            Smelt(ItemIds.RawMutton, ItemIds.CookedMutton);
        }

        private static void Armour(ushort helmet, ushort chest, ushort legs, ushort boots, ushort material)
        {
            AddShaped(helmet, 1, "XXX", "X.X", material);
            AddShaped(chest, 1, "X.X", "XXX", "XXX", material);
            AddShaped(legs, 1, "XXX", "X.X", "X.X", material);
            AddShaped(boots, 1, "X.X", "X.X", material);
        }

        private static void Tools(ushort pick, ushort axe, ushort shovel, ushort sword, ushort material)
        {
            const ushort S = ItemIds.Stick;
            AddShaped(pick, 1, "XXX", ".S.", ".S.", material, S);
            AddShaped(axe, 1, "XX", "XS", ".S", material, S);
            AddShaped(shovel, 1, "X", "S", "S", material, S);
            AddShaped(sword, 1, "X", "X", "S", material, S);
        }

        private static void AddShapeless(ushort[] ingredients, ushort result, int count)
        {
            ShapelessRecipe r = new ShapelessRecipe();
            r.Ingredients = ingredients; r.Result = new ItemStack(result, count);
            Shapeless.Add(r);
        }

        /// <summary>
        /// Rows use letters mapped in order of appearance to the trailing ingredient arguments:
        /// for AddShaped(result, n, rows..., ing0, ing1, ...), letters are assigned from a fixed table
        /// (X, S, P, C, K, F, L, D, I) according to the ingredient list given.
        /// </summary>
        private static void AddShaped(ushort result, int count, params object[] spec)
        {
            List<string> rows = new List<string>();
            List<ushort> ingredients = new List<ushort>();
            for (int i = 0; i < spec.Length; i++)
            {
                string s = spec[i] as string;
                if (s != null) rows.Add(s);
                else ingredients.Add(Convert.ToUInt16(spec[i]));
            }
            // The distinct letters in the pattern, in order of first appearance, map to the ingredients in order.
            List<char> letters = new List<char>();
            foreach (string row in rows)
                foreach (char ch in row)
                    if (ch != '.' && !letters.Contains(ch)) letters.Add(ch);
            if (letters.Count != ingredients.Count)
                throw new InvalidOperationException("recipe for " + result + " has " + letters.Count + " letters but " + ingredients.Count + " ingredients");

            ShapedRecipe r = new ShapedRecipe();
            r.Height = rows.Count; r.Width = rows[0].Length;
            r.Cells = new ushort[r.Width * r.Height];
            for (int y = 0; y < r.Height; y++)
                for (int x = 0; x < r.Width; x++)
                {
                    char ch = rows[y][x];
                    r.Cells[y * r.Width + x] = ch == '.' ? (ushort)0 : ingredients[letters.IndexOf(ch)];
                }
            r.Result = new ItemStack(result, count);
            Shaped.Add(r);
        }

        private static void Smelt(ushort input, ushort result)
        {
            SmeltRecipe r = new SmeltRecipe();
            r.Input = input; r.Result = new ItemStack(result, 1);
            Smelting.Add(r);
        }

        public static bool Matches(ushort need, ushort have)
        {
            if (need == AnyLog) return have == B.LogOak || have == B.LogSpruce || have == B.LogBirch;
            return need == have;
        }

        /// <summary>Finds the recipe produced by a crafting grid (row-major, width x height), or an empty stack.</summary>
        public static ItemStack Match(ItemStack[] grid, int width, int height)
        {
            int minX = width, maxX = -1, minY = height, maxY = -1, count = 0;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (grid[y * width + x].IsEmpty) continue;
                    count++;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            if (count == 0) return ItemStack.Empty;
            int bw = maxX - minX + 1, bh = maxY - minY + 1;

            for (int ri = 0; ri < Shaped.Count; ri++)
            {
                ShapedRecipe r = Shaped[ri];
                if (r.Width != bw || r.Height != bh) continue;
                bool ok = true;
                for (int y = 0; y < bh && ok; y++)
                    for (int x = 0; x < bw && ok; x++)
                    {
                        ItemStack have = grid[(minY + y) * width + (minX + x)];
                        ushort need = r.Cells[y * bw + x];
                        if (need == 0) { if (!have.IsEmpty) ok = false; }
                        else if (have.IsEmpty || !Matches(need, have.Id)) ok = false;
                    }
                if (ok) return r.Result;
            }

            List<ushort> items = new List<ushort>();
            for (int i = 0; i < grid.Length; i++) if (!grid[i].IsEmpty) items.Add(grid[i].Id);
            for (int ri = 0; ri < Shapeless.Count; ri++)
            {
                ShapelessRecipe r = Shapeless[ri];
                if (r.Ingredients.Length != items.Count) continue;
                bool[] used = new bool[items.Count];
                bool ok = true;
                for (int i = 0; i < r.Ingredients.Length && ok; i++)
                {
                    bool found = false;
                    for (int j = 0; j < items.Count; j++)
                    {
                        if (used[j] || !Matches(r.Ingredients[i], items[j])) continue;
                        used[j] = true; found = true; break;
                    }
                    if (!found) ok = false;
                }
                if (ok) return r.Result;
            }
            return ItemStack.Empty;
        }

        /// <summary>True if the item can be smelted (shift-click uses it to choose the input or the fuel slot).</summary>
        public static bool TrySmeltCheck(ushort input)
        {
            for (int i = 0; i < Smelting.Count; i++) if (Smelting[i].Input == input) return true;
            return false;
        }

        public static bool TrySmelt(ushort input, out ItemStack result)
        {
            for (int i = 0; i < Smelting.Count; i++)
            {
                if (Smelting[i].Input == input) { result = Smelting[i].Result; return true; }
            }
            result = ItemStack.Empty;
            return false;
        }
    }
}
