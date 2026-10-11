using System;
using System.Collections.Generic;

namespace BlockRealm
{
    public enum ToolKind : byte { None = 0, Pickaxe = 1, Axe = 2, Shovel = 3, Sword = 4, Shears = 5 }

    public enum RenderKind : byte
    {
        None = 0,     // air
        Cube = 1,     // opaque or non-opaque full cube
        Cutout = 2,   // full cube with alpha-tested texture (leaves, glass)
        Liquid = 3,   // water and lava
        Cross = 4,    // two crossed quads (plants, torch, fire)
    }

    /// <summary>Block ids. One byte each, so a section is 4096 bytes.</summary>
    public static class B
    {
        public const byte Air = 0, Stone = 1, Grass = 2, Dirt = 3, Cobble = 4, Planks = 5, Bedrock = 6,
            Water = 7, Lava = 8, Sand = 9, Gravel = 10, CoalOre = 11, IronOre = 12, GoldOre = 13,
            DiamondOre = 14, LogOak = 15, LeavesOak = 16, LogSpruce = 17, LeavesSpruce = 18,
            LogBirch = 19, LeavesBirch = 20, Glass = 21, Sandstone = 22, Snow = 23, Ice = 24,
            Cactus = 25, Clay = 26, Bricks = 27, CraftingTable = 28, Furnace = 29, FurnaceLit = 30,
            Chest = 31, Torch = 32, TallGrass = 33, FlowerRed = 34, FlowerYellow = 35, DeadBush = 36,
            Obsidian = 37, Wool = 38, ScorchedStone = 39, Fire = 40, SnowGrass = 41;
        public const int Count = 42;
    }

    public sealed class BlockInfo
    {
        public byte Id;
        public string Name;
        public RenderKind Render = RenderKind.Cube;
        public bool Solid = true;          // blocks movement
        public bool Opaque = true;         // hides neighbouring faces and blocks sky light
        public bool Liquid;
        public bool Replaceable;           // can be built over (air, water, plants)
        public bool Gravity;               // falls when unsupported (sand, gravel)
        public bool Flammable;             // dragon fire and lava can burn it away
        public byte Emission;              // block light 0..15
        public byte Absorb;                // extra light lost passing through (0 = clear, 15 = opaque)
        public float Hardness = 1f;        // < 0 means unbreakable
        public ToolKind Tool = ToolKind.None;
        public int MinTier;                // tier needed for the block to drop anything
        public int DropItem = -1;          // -1 = drops itself, 0 = nothing
        public int DropCount = 1;
        public string TileTop, TileSide, TileBottom;
        public int LayerTop, LayerSide, LayerBottom;   // texture array layers, set by Blocks.Init

        public int LayerFor(Face face)
        {
            if (face == Face.Up) return LayerTop;
            if (face == Face.Down) return LayerBottom;
            return LayerSide;
        }
    }

    public static class Blocks
    {
        public static readonly BlockInfo[] All = new BlockInfo[256];
        public static readonly List<string> TileNames = new List<string>();
        private static readonly Dictionary<string, int> TileIndex = new Dictionary<string, int>();

        // Per-id lookup tables for hot paths (meshing, light, collision).
        public static readonly bool[] SolidTable = new bool[256];
        public static readonly bool[] OpaqueTable = new bool[256];
        public static readonly byte[] AbsorbTable = new byte[256];
        public static readonly byte[] EmissionTable = new byte[256];

        static Blocks()
        {
            Define(B.Air, "Air", RenderKind.None, solid: false, opaque: false, replaceable: true, hardness: 0, drop: 0);
            Define(B.Stone, "Stone", hardness: 1.5f, tool: ToolKind.Pickaxe, minTier: 1, drop: B.Cobble, tile: "stone");
            Define(B.Grass, "Grass", hardness: 0.6f, tool: ToolKind.Shovel, drop: B.Dirt, top: "grass_top", side: "grass_side", bottom: "dirt");
            Define(B.Dirt, "Dirt", hardness: 0.5f, tool: ToolKind.Shovel, tile: "dirt");
            Define(B.Cobble, "Cobblestone", hardness: 2f, tool: ToolKind.Pickaxe, minTier: 1, tile: "cobble");
            Define(B.Planks, "Oak Planks", hardness: 2f, tool: ToolKind.Axe, flammable: true, tile: "planks");
            Define(B.Bedrock, "Bedrock", hardness: -1, tile: "bedrock");
            Define(B.Water, "Water", RenderKind.Liquid, solid: false, opaque: false, liquid: true, replaceable: true, absorb: 2, hardness: 100, drop: 0, tile: "water");
            Define(B.Lava, "Lava", RenderKind.Liquid, solid: false, opaque: false, liquid: true, replaceable: true, absorb: 0, emission: 15, hardness: 100, drop: 0, tile: "lava");
            Define(B.Sand, "Sand", hardness: 0.5f, tool: ToolKind.Shovel, gravity: true, tile: "sand");
            Define(B.Gravel, "Gravel", hardness: 0.6f, tool: ToolKind.Shovel, gravity: true, tile: "gravel");
            Define(B.CoalOre, "Coal Ore", hardness: 3f, tool: ToolKind.Pickaxe, minTier: 1, drop: ItemIds.Coal, tile: "coal_ore");
            Define(B.IronOre, "Iron Ore", hardness: 3f, tool: ToolKind.Pickaxe, minTier: 2, tile: "iron_ore");
            Define(B.GoldOre, "Gold Ore", hardness: 3f, tool: ToolKind.Pickaxe, minTier: 3, tile: "gold_ore");
            Define(B.DiamondOre, "Diamond Ore", hardness: 3f, tool: ToolKind.Pickaxe, minTier: 3, drop: ItemIds.Diamond, tile: "diamond_ore");
            Define(B.LogOak, "Oak Log", hardness: 2f, tool: ToolKind.Axe, flammable: true, top: "log_oak_top", side: "log_oak", bottom: "log_oak_top");
            Define(B.LeavesOak, "Oak Leaves", RenderKind.Cutout, opaque: false, absorb: 1, hardness: 0.2f, tool: ToolKind.Shears, flammable: true, drop: 0, tile: "leaves_oak");
            Define(B.LogSpruce, "Spruce Log", hardness: 2f, tool: ToolKind.Axe, flammable: true, top: "log_spruce_top", side: "log_spruce", bottom: "log_spruce_top");
            Define(B.LeavesSpruce, "Spruce Leaves", RenderKind.Cutout, opaque: false, absorb: 1, hardness: 0.2f, tool: ToolKind.Shears, flammable: true, drop: 0, tile: "leaves_spruce");
            Define(B.LogBirch, "Birch Log", hardness: 2f, tool: ToolKind.Axe, flammable: true, top: "log_birch_top", side: "log_birch", bottom: "log_birch_top");
            Define(B.LeavesBirch, "Birch Leaves", RenderKind.Cutout, opaque: false, absorb: 1, hardness: 0.2f, tool: ToolKind.Shears, flammable: true, drop: 0, tile: "leaves_birch");
            Define(B.Glass, "Glass", RenderKind.Cutout, opaque: false, hardness: 0.3f, drop: 0, tile: "glass");
            Define(B.Sandstone, "Sandstone", hardness: 0.8f, tool: ToolKind.Pickaxe, minTier: 1, top: "sandstone_top", side: "sandstone", bottom: "sandstone_top");
            Define(B.Snow, "Snow", hardness: 0.2f, tool: ToolKind.Shovel, tile: "snow");
            Define(B.Ice, "Ice", opaque: false, hardness: 0.5f, tool: ToolKind.Pickaxe, drop: 0, tile: "ice");
            Define(B.Cactus, "Cactus", opaque: false, hardness: 0.4f, top: "cactus_top", side: "cactus_side", bottom: "cactus_top");
            Define(B.Clay, "Clay", hardness: 0.6f, tool: ToolKind.Shovel, tile: "clay");
            Define(B.Bricks, "Bricks", hardness: 2f, tool: ToolKind.Pickaxe, minTier: 1, tile: "bricks");
            Define(B.CraftingTable, "Crafting Table", hardness: 2.5f, tool: ToolKind.Axe, flammable: true, top: "crafting_top", side: "crafting_side", bottom: "planks");
            Define(B.Furnace, "Furnace", hardness: 3.5f, tool: ToolKind.Pickaxe, minTier: 1, top: "furnace_top", side: "furnace_side", bottom: "furnace_top");
            Define(B.FurnaceLit, "Lit Furnace", hardness: 3.5f, tool: ToolKind.Pickaxe, minTier: 1, emission: 13, drop: B.Furnace, top: "furnace_top", side: "furnace_side_lit", bottom: "furnace_top");
            Define(B.Chest, "Chest", hardness: 2.5f, tool: ToolKind.Axe, flammable: true, top: "chest_top", side: "chest_side", bottom: "chest_top");
            Define(B.Torch, "Torch", RenderKind.Cross, solid: false, opaque: false, replaceable: false, emission: 14, hardness: 0, tile: "torch");
            Define(B.TallGrass, "Tall Grass", RenderKind.Cross, solid: false, opaque: false, replaceable: true, hardness: 0, flammable: true, drop: 0, tile: "tall_grass");
            Define(B.FlowerRed, "Poppy", RenderKind.Cross, solid: false, opaque: false, replaceable: true, hardness: 0, flammable: true, tile: "flower_red");
            Define(B.FlowerYellow, "Dandelion", RenderKind.Cross, solid: false, opaque: false, replaceable: true, hardness: 0, flammable: true, tile: "flower_yellow");
            Define(B.DeadBush, "Dead Bush", RenderKind.Cross, solid: false, opaque: false, replaceable: true, hardness: 0, flammable: true, drop: ItemIds.Stick, tile: "dead_bush");
            Define(B.Obsidian, "Obsidian", hardness: 50f, tool: ToolKind.Pickaxe, minTier: 4, tile: "obsidian");
            Define(B.Wool, "Wool", hardness: 0.8f, tool: ToolKind.Shears, flammable: true, tile: "wool");
            Define(B.ScorchedStone, "Scorched Stone", hardness: 3f, tool: ToolKind.Pickaxe, minTier: 1, tile: "scorched_stone");
            Define(B.Fire, "Fire", RenderKind.Cross, solid: false, opaque: false, replaceable: true, emission: 13, hardness: 0, drop: 0, tile: "fire");
            Define(B.SnowGrass, "Snowy Grass", hardness: 0.6f, tool: ToolKind.Shovel, drop: B.Dirt, top: "snow", side: "grass_snow_side", bottom: "dirt");

            for (int i = 0; i < 256; i++)
            {
                BlockInfo info = All[i];
                if (info == null) continue;
                SolidTable[i] = info.Solid;
                OpaqueTable[i] = info.Opaque;
                AbsorbTable[i] = info.Opaque ? (byte)15 : info.Absorb;
                EmissionTable[i] = info.Emission;
            }
        }

        private static int Tile(string name)
        {
            if (name == null) return 0;
            int idx;
            if (TileIndex.TryGetValue(name, out idx)) return idx;
            idx = TileNames.Count;
            TileNames.Add(name);
            TileIndex[name] = idx;
            return idx;
        }

        private static void Define(byte id, string name, RenderKind render = RenderKind.Cube, bool solid = true,
            bool opaque = true, bool liquid = false, bool replaceable = false, bool gravity = false, bool flammable = false,
            int emission = 0, int absorb = 0, float hardness = 1f, ToolKind tool = ToolKind.None, int minTier = 0,
            int drop = -1, int dropCount = 1, string tile = null, string top = null, string side = null, string bottom = null)
        {
            BlockInfo b = new BlockInfo();
            b.Id = id; b.Name = name; b.Render = render; b.Solid = solid; b.Opaque = opaque; b.Liquid = liquid;
            b.Replaceable = replaceable; b.Gravity = gravity; b.Flammable = flammable;
            b.Emission = (byte)emission; b.Absorb = (byte)absorb; b.Hardness = hardness; b.Tool = tool;
            b.MinTier = minTier; b.DropItem = drop; b.DropCount = dropCount;
            b.TileTop = top ?? tile; b.TileSide = side ?? tile; b.TileBottom = bottom ?? tile;
            if (render != RenderKind.None)
            {
                b.LayerTop = Tile(b.TileTop);
                b.LayerSide = Tile(b.TileSide);
                b.LayerBottom = Tile(b.TileBottom);
            }
            All[id] = b;
        }

        public static BlockInfo Get(byte id) { return All[id] ?? All[0]; }
        public static bool IsSolid(byte id) { return SolidTable[id]; }
        public static bool IsOpaque(byte id) { return OpaqueTable[id]; }
        public static bool IsLiquid(byte id) { BlockInfo b = All[id]; return b != null && b.Liquid; }
        public static bool IsDefined(int id) { return id >= 0 && id < 256 && All[id] != null; }

        /// <summary>Time in seconds to break a block with the given tool, or -1 if it cannot be broken.</summary>
        public static float BreakTime(byte blockId, ToolKind toolKind, int toolTier)
        {
            BlockInfo b = Get(blockId);
            if (b.Hardness < 0) return -1f;
            if (b.Hardness == 0f) return 0f;
            bool right = b.Tool == ToolKind.None || (toolKind == b.Tool && toolTier >= 0);
            bool canHarvest = b.MinTier == 0 || (toolKind == b.Tool && toolTier >= b.MinTier);
            float speed = 1f;
            if (right && toolKind == b.Tool && b.Tool != ToolKind.None) speed = 1f + toolTier * 2f;
            if (b.Tool == ToolKind.Shears && toolKind == ToolKind.Shears) speed = 5f;
            float time = b.Hardness * (canHarvest ? 1.5f : 5f) / speed;
            return time;
        }

        /// <summary>Whether breaking with this tool yields the block's drop (stone needs a pickaxe, and so on).</summary>
        public static bool Harvests(byte blockId, ToolKind toolKind, int toolTier)
        {
            BlockInfo b = Get(blockId);
            return b.MinTier == 0 || (toolKind == b.Tool && toolTier >= b.MinTier);
        }
    }
}
