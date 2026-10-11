using System;
using System.Collections.Generic;

namespace BlockRealm
{
    /// <summary>Item ids. 1..255 are the placeable blocks (same number as the block id); higher ids are plain items.</summary>
    public static class ItemIds
    {
        public const ushort None = 0;
        public const ushort Stick = 256, Coal = 257, IronIngot = 258, GoldIngot = 259, Diamond = 260;
        public const ushort WoodPickaxe = 261, StonePickaxe = 262, IronPickaxe = 263, DiamondPickaxe = 264;
        public const ushort WoodAxe = 265, StoneAxe = 266, IronAxe = 267, DiamondAxe = 268;
        public const ushort WoodShovel = 269, StoneShovel = 270, IronShovel = 271, DiamondShovel = 272;
        public const ushort WoodSword = 273, StoneSword = 274, IronSword = 275, DiamondSword = 276;
        public const ushort Shears = 277;
        public const ushort Apple = 280, RawPork = 281, CookedPork = 282, RawBeef = 283, Steak = 284,
            RawChicken = 285, CookedChicken = 286, RawMutton = 287, CookedMutton = 288, RottenFlesh = 289;
        public const ushort Leather = 290, Feather = 291, Bone = 292, Arrow = 293, Bow = 294,
            Gunpowder = 295, DragonScale = 296, Saddle = 297, Charcoal = 298;
        public const ushort LeatherHelmet = 300, LeatherChestplate = 301, LeatherLeggings = 302, LeatherBoots = 303,
            IronHelmet = 304, IronChestplate = 305, IronLeggings = 306, IronBoots = 307,
            DiamondHelmet = 308, DiamondChestplate = 309, DiamondLeggings = 310, DiamondBoots = 311;
        public const ushort Seeds = 312, Wheat = 313, Bread = 314, WoodHoe = 315, StoneHoe = 316, IronHoe = 317, DiamondHoe = 318,
            Bucket = 319, WaterBucket = 320;
        public const int MaxId = 330;
    }

    public sealed class ItemInfo
    {
        public ushort Id;
        public string Name;
        public int MaxStack = 64;
        public bool IsBlock;
        public ToolKind Tool = ToolKind.None;
        public int Tier;                 // 1 wood, 2 stone, 3 iron, 4 diamond
        public int Durability;           // 0 = does not wear
        public float Attack = 1f;        // damage per hit, in half hearts
        public int FoodHunger;           // hunger points restored (0 = not food)
        public float FoodSaturation;
        public int FuelTicks;            // furnace burn time in ticks (0 = not fuel)
        public string Icon;              // icon texture name (blocks use their side tile)
        public int ArmorSlot = -1;       // 0 head, 1 chest, 2 legs, 3 feet; -1 = not armour
        public int ArmorPoints;          // damage protection contributed
        public bool Usable;              // right-click without a target block does something (food, bucket, hoe, seeds, bow)
    }

    public struct ItemStack
    {
        public ushort Id;
        public byte Count;
        public short Wear;               // durability used so far

        public ItemStack(ushort id, int count) { Id = id; Count = (byte)count; Wear = 0; }
        public bool IsEmpty { get { return Id == 0 || Count == 0; } }
        public static readonly ItemStack Empty = new ItemStack();
        public ItemInfo Info { get { return Items.Get(Id); } }
        public int MaxStack { get { return Items.Get(Id).MaxStack; } }
        public override string ToString() { return IsEmpty ? "empty" : Items.Get(Id).Name + " x" + Count; }
    }

    public static class Items
    {
        private static readonly ItemInfo[] All = new ItemInfo[ItemIds.MaxId + 1];
        public static readonly List<string> IconNames = new List<string>();

        static Items()
        {
            for (int i = 1; i < 256; i++)
            {
                BlockInfo b = Blocks.All[i];
                if (b == null) continue;
                ItemInfo item = new ItemInfo();
                item.Id = (ushort)i; item.Name = b.Name; item.IsBlock = true; item.Icon = b.TileSide;
                if (b.Id == B.Planks || b.Id == B.LogOak || b.Id == B.LogSpruce || b.Id == B.LogBirch) item.FuelTicks = 300;
                if (b.Id == B.Wool) item.FuelTicks = 100;
                All[i] = item;
            }
            Plain(ItemIds.Stick, "Stick", "stick", fuel: 100);
            Plain(ItemIds.Coal, "Coal", "coal", fuel: 1600);
            Plain(ItemIds.Charcoal, "Charcoal", "charcoal", fuel: 1600);
            Plain(ItemIds.IronIngot, "Iron Ingot", "iron_ingot");
            Plain(ItemIds.GoldIngot, "Gold Ingot", "gold_ingot");
            Plain(ItemIds.Diamond, "Diamond", "diamond");

            Tool(ItemIds.WoodPickaxe, "Wooden Pickaxe", "wood_pickaxe", ToolKind.Pickaxe, 1, 59, 2f);
            Tool(ItemIds.StonePickaxe, "Stone Pickaxe", "stone_pickaxe", ToolKind.Pickaxe, 2, 131, 3f);
            Tool(ItemIds.IronPickaxe, "Iron Pickaxe", "iron_pickaxe", ToolKind.Pickaxe, 3, 250, 4f);
            Tool(ItemIds.DiamondPickaxe, "Diamond Pickaxe", "diamond_pickaxe", ToolKind.Pickaxe, 4, 1561, 5f);
            Tool(ItemIds.WoodAxe, "Wooden Axe", "wood_axe", ToolKind.Axe, 1, 59, 3f);
            Tool(ItemIds.StoneAxe, "Stone Axe", "stone_axe", ToolKind.Axe, 2, 131, 4f);
            Tool(ItemIds.IronAxe, "Iron Axe", "iron_axe", ToolKind.Axe, 3, 250, 5f);
            Tool(ItemIds.DiamondAxe, "Diamond Axe", "diamond_axe", ToolKind.Axe, 4, 1561, 6f);
            Tool(ItemIds.WoodShovel, "Wooden Shovel", "wood_shovel", ToolKind.Shovel, 1, 59, 2f);
            Tool(ItemIds.StoneShovel, "Stone Shovel", "stone_shovel", ToolKind.Shovel, 2, 131, 3f);
            Tool(ItemIds.IronShovel, "Iron Shovel", "iron_shovel", ToolKind.Shovel, 3, 250, 4f);
            Tool(ItemIds.DiamondShovel, "Diamond Shovel", "diamond_shovel", ToolKind.Shovel, 4, 1561, 5f);
            Tool(ItemIds.WoodSword, "Wooden Sword", "wood_sword", ToolKind.Sword, 1, 59, 5f);
            Tool(ItemIds.StoneSword, "Stone Sword", "stone_sword", ToolKind.Sword, 2, 131, 6f);
            Tool(ItemIds.IronSword, "Iron Sword", "iron_sword", ToolKind.Sword, 3, 250, 7f);
            Tool(ItemIds.DiamondSword, "Diamond Sword", "diamond_sword", ToolKind.Sword, 4, 1561, 8f);
            Tool(ItemIds.Shears, "Shears", "shears", ToolKind.Shears, 1, 238, 1f);

            Food(ItemIds.Apple, "Apple", "apple", 4, 2.4f);
            Food(ItemIds.RawPork, "Raw Porkchop", "raw_pork", 3, 1.8f);
            Food(ItemIds.CookedPork, "Cooked Porkchop", "cooked_pork", 8, 12.8f);
            Food(ItemIds.RawBeef, "Raw Beef", "raw_beef", 3, 1.8f);
            Food(ItemIds.Steak, "Steak", "steak", 8, 12.8f);
            Food(ItemIds.RawChicken, "Raw Chicken", "raw_chicken", 2, 1.2f);
            Food(ItemIds.CookedChicken, "Cooked Chicken", "cooked_chicken", 6, 7.2f);
            Food(ItemIds.RawMutton, "Raw Mutton", "raw_mutton", 2, 1.2f);
            Food(ItemIds.CookedMutton, "Cooked Mutton", "cooked_mutton", 6, 9.6f);
            Food(ItemIds.RottenFlesh, "Rotten Flesh", "rotten_flesh", 4, 0.8f);

            Armor(ItemIds.LeatherHelmet, "Leather Cap", "leather_helmet", 0, 1, 55);
            Armor(ItemIds.LeatherChestplate, "Leather Tunic", "leather_chestplate", 1, 3, 80);
            Armor(ItemIds.LeatherLeggings, "Leather Pants", "leather_leggings", 2, 2, 75);
            Armor(ItemIds.LeatherBoots, "Leather Boots", "leather_boots", 3, 1, 65);
            Armor(ItemIds.IronHelmet, "Iron Helmet", "iron_helmet", 0, 2, 165);
            Armor(ItemIds.IronChestplate, "Iron Chestplate", "iron_chestplate", 1, 6, 240);
            Armor(ItemIds.IronLeggings, "Iron Leggings", "iron_leggings", 2, 5, 225);
            Armor(ItemIds.IronBoots, "Iron Boots", "iron_boots", 3, 2, 195);
            Armor(ItemIds.DiamondHelmet, "Diamond Helmet", "diamond_helmet", 0, 3, 363);
            Armor(ItemIds.DiamondChestplate, "Diamond Chestplate", "diamond_chestplate", 1, 8, 528);
            Armor(ItemIds.DiamondLeggings, "Diamond Leggings", "diamond_leggings", 2, 6, 495);
            Armor(ItemIds.DiamondBoots, "Diamond Boots", "diamond_boots", 3, 3, 429);

            Plain(ItemIds.Seeds, "Seeds", "seeds").Usable = true;
            Plain(ItemIds.Wheat, "Wheat", "wheat");
            Food(ItemIds.Bread, "Bread", "bread", 5, 6.0f);
            Tool(ItemIds.WoodHoe, "Wooden Hoe", "wood_hoe", ToolKind.Hoe, 1, 59, 1f).Usable = true;
            Tool(ItemIds.StoneHoe, "Stone Hoe", "stone_hoe", ToolKind.Hoe, 2, 131, 1f).Usable = true;
            Tool(ItemIds.IronHoe, "Iron Hoe", "iron_hoe", ToolKind.Hoe, 3, 250, 1f).Usable = true;
            Tool(ItemIds.DiamondHoe, "Diamond Hoe", "diamond_hoe", ToolKind.Hoe, 4, 1561, 1f).Usable = true;
            Plain(ItemIds.Bucket, "Bucket", "bucket").Usable = true;
            Plain(ItemIds.WaterBucket, "Water Bucket", "water_bucket").Usable = true;
            All[ItemIds.Bucket].MaxStack = 16;
            All[ItemIds.WaterBucket].MaxStack = 1;

            Plain(ItemIds.Leather, "Leather", "leather");
            Plain(ItemIds.Feather, "Feather", "feather");
            Plain(ItemIds.Bone, "Bone", "bone");
            Plain(ItemIds.Arrow, "Arrow", "arrow");
            Tool(ItemIds.Bow, "Bow", "bow", ToolKind.None, 0, 384, 1f).Usable = true;
            Plain(ItemIds.Gunpowder, "Gunpowder", "gunpowder");
            Plain(ItemIds.DragonScale, "Dragon Scale", "dragon_scale");
            Plain(ItemIds.Saddle, "Dragon Saddle", "saddle").MaxStack = 1;
        }

        private static ItemInfo Plain(ushort id, string name, string icon, int fuel = 0)
        {
            ItemInfo i = new ItemInfo { Id = id, Name = name, Icon = icon, FuelTicks = fuel };
            All[id] = i;
            IconNames.Add(icon);
            return i;
        }

        private static ItemInfo Tool(ushort id, string name, string icon, ToolKind kind, int tier, int durability, float attack)
        {
            ItemInfo i = Plain(id, name, icon);
            i.MaxStack = 1; i.Tool = kind; i.Tier = tier; i.Durability = durability; i.Attack = attack;
            return i;
        }

        private static void Armor(ushort id, string name, string icon, int slot, int points, int durability)
        {
            ItemInfo i = Plain(id, name, icon);
            i.MaxStack = 1; i.ArmorSlot = slot; i.ArmorPoints = points; i.Durability = durability;
        }

        private static void Food(ushort id, string name, string icon, int hunger, float saturation)
        {
            ItemInfo i = Plain(id, name, icon);
            i.FoodHunger = hunger; i.FoodSaturation = saturation; i.Usable = true;
        }

        public static ItemInfo Get(ushort id)
        {
            if (id >= All.Length) return All[0] ?? Fallback;
            return All[id] ?? Fallback;
        }
        private static readonly ItemInfo Fallback = new ItemInfo { Id = 0, Name = "Nothing", MaxStack = 1 };

        public static bool IsDefined(ushort id) { return id < All.Length && All[id] != null; }
        public static bool IsFood(ushort id) { return Get(id).FoodHunger > 0; }

        /// <summary>The item a block turns into when broken (block id as item id, or a plain item).</summary>
        public static ushort DropFor(byte blockId, bool harvested, Rng rng, bool usedShears)
        {
            BlockInfo b = Blocks.Get(blockId);
            if (!harvested) return 0;
            bool leaves = blockId == B.LeavesOak || blockId == B.LeavesSpruce || blockId == B.LeavesBirch;
            if (leaves)
            {
                if (usedShears) return blockId;
                if (rng != null && rng.Chance(0.06f)) return rng.Chance(0.5f) ? ItemIds.Apple : ItemIds.Stick;
                return 0;
            }
            if (blockId == B.Wool) return blockId;
            if (B.IsWheat(blockId)) return blockId == B.Wheat7 ? ItemIds.Wheat : ItemIds.Seeds;
            if (blockId == B.TallGrass) return rng != null && rng.Chance(0.14f) ? ItemIds.Seeds : (ushort)0;
            if (blockId == B.Farmland) return B.Dirt;
            if (b.DropItem == 0) return 0;
            if (b.DropItem < 0) return blockId == B.FurnaceLit ? B.Furnace : blockId;
            return (ushort)b.DropItem;
        }
    }
}
