using System;
using System.Collections.Generic;

namespace BlockRealm
{
    public enum Biome : byte { Ocean = 0, River, Beach, Plains, Forest, Desert, Tundra, Taiga, Mountains }

    public struct TerrainSample
    {
        public int Height;       // y of the topmost terrain block
        public Biome Biome;
        public float Temperature, Humidity;
    }

    /// <summary>A dragon's nest: a levelled platform on a high peak.</summary>
    public struct Lair
    {
        public int X, Y, Z;      // Y is the platform surface
        public int Radius;
        public int DragonType;   // 0 Cinderwing, 1 Brazenhide, 2 Elderscale
        public int Id;           // stable identifier derived from the position
    }

    /// <summary>
    /// Deterministic terrain from a seed: continents, hills, mountains, rivers, biomes, caves, ores, trees, plants and
    /// dragon lairs. Everything is a pure function of (seed, x, z), so neighbouring columns agree about trees and
    /// structures that cross column borders. Safe to call from several threads.
    /// </summary>
    public sealed class WorldGenerator
    {
        public readonly long Seed;
        private readonly Noise _continent, _erosion, _peaks, _detail, _temp, _humid, _river, _ridge, _caveA, _caveB, _caveC, _surface;

        private const int LairRegion = 640;
        public const int LairRadius = 13;

        public WorldGenerator(long seed)
        {
            Seed = seed;
            _continent = new Noise(seed * 31 + 1);
            _erosion = new Noise(seed * 31 + 2);
            _peaks = new Noise(seed * 31 + 3);
            _detail = new Noise(seed * 31 + 4);
            _temp = new Noise(seed * 31 + 5);
            _humid = new Noise(seed * 31 + 6);
            _river = new Noise(seed * 31 + 7);
            _ridge = new Noise(seed * 31 + 8);
            _caveA = new Noise(seed * 31 + 9);
            _caveB = new Noise(seed * 31 + 10);
            _caveC = new Noise(seed * 31 + 11);
            _surface = new Noise(seed * 31 + 12);
        }

        // ------------------------------------------------------------------ terrain shape

        public TerrainSample Sample(int x, int z)
        {
            float fx = x, fz = z;
            float cont = _continent.Fbm2(fx * 0.0007f, fz * 0.0007f, 4);
            float ero = _erosion.Fbm2(fx * 0.0021f + 31f, fz * 0.0021f + 17f, 3);
            float pk = _peaks.Fbm2(fx * 0.0042f - 77f, fz * 0.0042f + 53f, 4);
            float det = _detail.Fbm2(fx * 0.016f, fz * 0.016f, 4);

            float h = WorldConst.SeaLevel + 3f + (cont + 0.12f) * 55f;
            float hillAmp = 3f + 14f * MathUtil.Saturate(ero * 0.5f + 0.5f);
            h += det * hillAmp;

            float mask = MathUtil.SmoothStep(0.04f, 0.38f, cont) * MathUtil.SmoothStep(-0.14f, 0.26f, pk);
            if (mask > 0f)
            {
                float r = 1f - Math.Abs(_ridge.Perlin2(fx * 0.009f + 300f, fz * 0.009f + 300f));
                h += mask * (60f + 150f * r * r + 60f * Math.Max(0f, pk));
            }

            float rv = Math.Abs(_river.Fbm2(fx * 0.0028f + 9f, fz * 0.0028f - 9f, 2));
            float rmask = MathUtil.SmoothStep(0.045f, 0.0f, rv) * MathUtil.SmoothStep(-0.1f, 0.1f, cont) * (1f - mask);
            if (rmask > 0f) h = MathUtil.Lerp(h, WorldConst.SeaLevel - 4f, rmask);

            int height = (int)h;
            int cap = WorldConst.Height - 24;
            if (height > cap) height = cap;
            if (height < 6) height = 6;

            float t = _temp.Fbm2(fx * 0.0011f + 50f, fz * 0.0011f + 50f, 3);
            float hu = _humid.Fbm2(fx * 0.0013f - 70f, fz * 0.0013f - 70f, 3);
            t -= Math.Max(0f, h - (WorldConst.SeaLevel + 30f)) * 0.006f;

            TerrainSample s;
            s.Height = height; s.Temperature = t; s.Humidity = hu;
            int sea = WorldConst.SeaLevel;
            if (height < sea - 1) s.Biome = rmask > 0.35f ? Biome.River : Biome.Ocean;
            else if (height <= sea + 2) s.Biome = Biome.Beach;
            else if (height > sea + 85) s.Biome = Biome.Mountains;
            else if (t > 0.18f && hu < 0.05f) s.Biome = Biome.Desert;
            else if (t < -0.2f) s.Biome = hu > -0.05f ? Biome.Taiga : Biome.Tundra;
            else s.Biome = hu > 0.02f ? Biome.Forest : Biome.Plains;
            return s;
        }

        public int HeightAt(int x, int z) { return Sample(x, z).Height; }

        /// <summary>A reasonable place to start: dry, lowland, not too close to water.</summary>
        public Vec3i FindSpawn()
        {
            for (int ring = 0; ring < 400; ring++)
            {
                int r = ring * 12;
                int steps = ring == 0 ? 1 : Math.Max(8, ring * 8);
                for (int i = 0; i < steps; i++)
                {
                    // Deterministic spiral using integer-ish stepping (no trig): walk the square ring.
                    int side = (i * 4) / steps;
                    int along = (i * 4) % steps;
                    float f = steps > 0 ? (float)along / steps * 2f - 1f : 0f;
                    int x = 0, z = 0;
                    if (side == 0) { x = r; z = (int)(f * r); }
                    else if (side == 1) { x = (int)(-f * r); z = r; }
                    else if (side == 2) { x = -r; z = (int)(-f * r); }
                    else { x = (int)(f * r); z = -r; }
                    TerrainSample s = Sample(x, z);
                    if (s.Height >= WorldConst.SeaLevel + 4 && s.Height < WorldConst.SeaLevel + 24 &&
                        (s.Biome == Biome.Plains || s.Biome == Biome.Forest))
                    {
                        return new Vec3i(x, s.Height + 1, z);
                    }
                }
            }
            return new Vec3i(0, WorldConst.SeaLevel + 20, 0);
        }

        // ------------------------------------------------------------------ lairs

        public List<Lair> LairsNear(int minX, int minZ, int maxX, int maxZ)
        {
            List<Lair> list = new List<Lair>();
            int r0x = MathUtil.FloorDiv(minX, LairRegion), r1x = MathUtil.FloorDiv(maxX, LairRegion);
            int r0z = MathUtil.FloorDiv(minZ, LairRegion), r1z = MathUtil.FloorDiv(maxZ, LairRegion);
            for (int rz = r0z; rz <= r1z; rz++)
            {
                for (int rx = r0x; rx <= r1x; rx++)
                {
                    Lair lair;
                    if (LairInRegion(rx, rz, out lair)) list.Add(lair);
                }
            }
            return list;
        }

        private bool LairInRegion(int rx, int rz, out Lair lair)
        {
            lair = new Lair();
            if (Rng.Hash01(Seed ^ 0x51A1, rx, rz) > 0.6f) return false;
            int px = rx * LairRegion + 120 + (int)(Rng.Hash01(Seed ^ 0x77, rx, rz) * (LairRegion - 240));
            int pz = rz * LairRegion + 120 + (int)(Rng.Hash01(Seed ^ 0x78, rx, rz) * (LairRegion - 240));
            if (Math.Abs(px) < 160 && Math.Abs(pz) < 160) return false;
            // Search the neighbourhood for the highest ground so the nest sits on a real peak.
            int bestX = px, bestZ = pz, bestH = -1;
            for (int dz = -48; dz <= 48; dz += 12)
            {
                for (int dx = -48; dx <= 48; dx += 12)
                {
                    int h = HeightAt(px + dx, pz + dz);
                    if (h > bestH) { bestH = h; bestX = px + dx; bestZ = pz + dz; }
                }
            }
            if (bestH < WorldConst.SeaLevel + 60) return false;
            lair.X = bestX; lair.Z = bestZ; lair.Y = bestH;
            lair.Radius = LairRadius;
            float roll = Rng.Hash01(Seed ^ 0x99, rx, rz);
            int altitude = bestH - WorldConst.SeaLevel;
            lair.DragonType = altitude > 150 ? 2 : (roll < 0.5f ? 0 : 1);
            lair.Id = (int)(Rng.Hash(Seed, bestX, bestZ) & 0x7FFFFFFF);
            return true;
        }

        // ------------------------------------------------------------------ column generation

        public Column GenerateColumn(int cx, int cz)
        {
            Column col = new Column(cx, cz);
            int baseX = cx * 16, baseZ = cz * 16;
            int sea = WorldConst.SeaLevel;
            int[] heights = new int[256];
            Biome[] biomes = new Biome[256];
            int maxH = 0;

            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    TerrainSample s = Sample(baseX + lx, baseZ + lz);
                    heights[lz * 16 + lx] = s.Height;
                    biomes[lz * 16 + lx] = s.Biome;
                    if (s.Height > maxH) maxH = s.Height;
                }
            }
            int topY = Math.Max(maxH, sea) + 1;
            col.EnsureSection(Math.Min(WorldConst.SectionsPerColumn - 1, (topY + 40) >> 4));

            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int wx = baseX + lx, wz = baseZ + lz;
                    int h = heights[lz * 16 + lx];
                    Biome biome = biomes[lz * 16 + lx];
                    FillColumn(col, lx, lz, wx, wz, h, biome);
                }
            }

            CarveCaves(col, heights);
            PlaceOres(col, heights);
            PlaceLairs(col, heights);
            PlaceTrees(col);
            PlacePlants(col, heights, biomes);
            PlaceVillages(col);
            col.Modified = false;
            return col;
        }

        private void FillColumn(Column col, int lx, int lz, int wx, int wz, int h, Biome biome)
        {
            int sea = WorldConst.SeaLevel;
            float sn = _surface.Perlin2(wx * 0.08f, wz * 0.08f);
            byte top, filler;
            int fillerDepth = 3 + (int)(Rng.Hash01(Seed, wx, wz) * 2f);
            switch (biome)
            {
                case Biome.Desert: top = B.Sand; filler = B.Sand; fillerDepth = 4; break;
                case Biome.Beach: top = B.Sand; filler = B.Sand; fillerDepth = 3; break;
                case Biome.Ocean:
                case Biome.River:
                    if (sn > 0.35f) { top = B.Gravel; filler = B.Gravel; }
                    else if (sn < -0.4f) { top = B.Clay; filler = B.Clay; }
                    else { top = B.Sand; filler = B.Sand; }
                    break;
                case Biome.Tundra:
                case Biome.Taiga: top = B.SnowGrass; filler = B.Dirt; break;
                case Biome.Mountains:
                    if (h > sea + 125) { top = B.Snow; filler = B.Stone; fillerDepth = 2; }
                    else if (sn > 0.25f) { top = B.Gravel; filler = B.Stone; fillerDepth = 2; }
                    else { top = B.Stone; filler = B.Stone; fillerDepth = 1; }
                    break;
                default: top = B.Grass; filler = B.Dirt; break;
            }

            col.SetBlock(lx, 0, lz, B.Bedrock);
            for (int y = 1; y <= h; y++)
            {
                byte id;
                if (y <= 3 && Rng.Hash01(Seed + y, wx, wz) < 0.6f - y * 0.15f) id = B.Bedrock;
                else if (y == h) id = top;
                else if (y > h - fillerDepth) id = filler;
                else if ((biome == Biome.Desert || biome == Biome.Beach) && y > h - fillerDepth - 4) id = B.Sandstone;
                else id = B.Stone;
                col.SetBlock(lx, y, lz, id);
            }

            // Water fills the basin up to sea level; cold shallows freeze.
            if (h < sea)
            {
                for (int y = h + 1; y <= sea; y++) col.SetBlock(lx, y, lz, B.Water);
                if (_temp.Fbm2(wx * 0.0011f + 50f, wz * 0.0011f + 50f, 3) < -0.25f) col.SetBlock(lx, sea, lz, B.Ice);
            }
        }

        private void CarveCaves(Column col, int[] heights)
        {
            int baseX = col.X * 16, baseZ = col.Z * 16;
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int wx = baseX + lx, wz = baseZ + lz;
                    int h = heights[lz * 16 + lx];
                    int ceiling = h - 4;
                    // Don't open caves right under the sea floor.
                    if (h < WorldConst.SeaLevel) ceiling = h - 8;
                    for (int y = 5; y <= ceiling; y++)
                    {
                        float fx = wx, fy = y, fz = wz;
                        bool carve = false;
                        float cheese = _caveA.Perlin3(fx * 0.017f, fy * 0.03f, fz * 0.017f);
                        // Rarer and smaller near the surface, bigger caverns deep down.
                        float threshold = 0.62f - Math.Min(1f, (ceiling - y) / 70f) * 0.10f;
                        if (cheese > threshold) carve = true;
                        if (!carve)
                        {
                            float a = _caveB.Perlin3(fx * 0.028f, fy * 0.034f, fz * 0.028f);
                            if (a > -0.055f && a < 0.055f)
                            {
                                float b = _caveC.Perlin3(fx * 0.028f + 55f, fy * 0.034f, fz * 0.028f - 31f);
                                if (b > -0.055f && b < 0.055f) carve = true;
                            }
                        }
                        if (!carve) continue;
                        byte existing = col.GetBlock(lx, y, lz);
                        if (existing == B.Bedrock || existing == B.Water || existing == B.Ice) continue;
                        col.SetBlock(lx, y, lz, y <= 11 ? B.Lava : B.Air);
                    }
                }
            }
        }

        private void PlaceOres(Column col, int[] heights)
        {
            PlaceVeins(col, B.CoalOre, 5, 200, 20, 8);
            PlaceVeins(col, B.IronOre, 5, 110, 14, 5);
            PlaceVeins(col, B.GoldOre, 5, 42, 3, 5);
            PlaceVeins(col, B.DiamondOre, 5, 24, 2, 3);
            PlaceVeins(col, B.Gravel, 5, 120, 5, 14);
            PlaceVeins(col, B.Dirt, 5, 140, 5, 14);
        }

        private void PlaceVeins(Column col, byte ore, int minY, int maxY, int attempts, int size)
        {
            Rng rng = new Rng((long)(Rng.Hash(Seed, col.X * 7919L + ore, col.Z * 104729L + ore * 31L) & 0x7FFFFFFFFFFFUL));
            for (int a = 0; a < attempts; a++)
            {
                int x = rng.NextInt(16), z = rng.NextInt(16), y = rng.Range(minY, maxY);
                int n = rng.Range(Math.Max(2, size / 2), size);
                for (int i = 0; i < n; i++)
                {
                    if (x >= 0 && x < 16 && z >= 0 && z < 16 && y >= 1 && y < WorldConst.Height)
                    {
                        if (col.GetBlock(x, y, z) == B.Stone) col.SetBlock(x, y, z, ore);
                    }
                    x += rng.Range(-1, 1); y += rng.Range(-1, 1); z += rng.Range(-1, 1);
                }
            }
        }

        private void PlaceLairs(Column col, int[] heights)
        {
            int baseX = col.X * 16, baseZ = col.Z * 16;
            List<Lair> lairs = LairsNear(baseX - LairRadius - 4, baseZ - LairRadius - 4, baseX + 16 + LairRadius + 4, baseZ + 16 + LairRadius + 4);
            for (int li = 0; li < lairs.Count; li++)
            {
                Lair lair = lairs[li];
                int r = lair.Radius;
                for (int lz = 0; lz < 16; lz++)
                {
                    for (int lx = 0; lx < 16; lx++)
                    {
                        int dx = baseX + lx - lair.X, dz = baseZ + lz - lair.Z;
                        int d2 = dx * dx + dz * dz;
                        if (d2 > (r + 1) * (r + 1)) continue;
                        // Carve everything above the platform, fill everything below it down to the terrain.
                        for (int y = lair.Y + 1; y <= lair.Y + 28 && y < WorldConst.Height; y++) col.SetBlock(lx, y, lz, B.Air);
                        int floor = Math.Max(1, lair.Y - 40);
                        for (int y = lair.Y; y >= floor; y--)
                        {
                            byte cur = col.GetBlock(lx, y, lz);
                            if (y == lair.Y || cur == B.Air || cur == B.Water || cur == B.Lava || cur == B.Snow || cur == B.Gravel)
                                col.SetBlock(lx, y, lz, (d2 > (r - 2) * (r - 2)) && d2 <= r * r ? B.Obsidian : B.ScorchedStone);
                            else if (y < lair.Y - 3 && cur != B.Air) break;
                        }
                        // Broken obsidian rim, pillars, and a central chest.
                        bool rim = d2 >= (r - 1) * (r - 1) && d2 <= r * r;
                        if (rim && Rng.Hash01(Seed, baseX + lx, baseZ + lz) < 0.55f)
                        {
                            col.SetBlock(lx, lair.Y + 1, lz, B.Obsidian);
                            if (Rng.Hash01(Seed + 1, baseX + lx, baseZ + lz) < 0.35f) col.SetBlock(lx, lair.Y + 2, lz, B.Obsidian);
                        }
                    }
                }
                // Four pillars at the compass points and the loot chest.
                int[] px = { r - 3, -(r - 3), 0, 0 }, pz = { 0, 0, r - 3, -(r - 3) };
                for (int p = 0; p < 4; p++)
                {
                    int wx = lair.X + px[p], wz = lair.Z + pz[p];
                    if (wx < baseX || wx >= baseX + 16 || wz < baseZ || wz >= baseZ + 16) continue;
                    for (int y = lair.Y + 1; y <= lair.Y + 7; y++) col.SetBlock(wx - baseX, y, wz - baseZ, y == lair.Y + 7 ? B.Lava : B.Obsidian);
                }
                if (lair.X >= baseX && lair.X < baseX + 16 && lair.Z >= baseZ && lair.Z < baseZ + 16)
                {
                    col.SetBlock(lair.X - baseX, lair.Y + 1, lair.Z - baseZ, B.Chest);
                }
            }
        }

        // ------------------------------------------------------------------ villages

        /// <summary>One small abandoned house. Pure function of the seed, so every column agrees where it stands.</summary>
        public struct VillageHouse
        {
            public int X, Z;         // centre of the 7x7 footprint
            public int Y;            // floor level (the ground height at the centre)
            public int Facing;       // which wall holds the doorway: 0 +X, 1 -X, 2 +Z, 3 -Z
            public bool Chest;       // holds a loot chest
            public bool Desert;      // sandstone instead of planks
        }

        private const int VillageRegion = 384;
        public const int HouseHalf = 3;

        public List<VillageHouse> HousesNear(int minX, int minZ, int maxX, int maxZ)
        {
            List<VillageHouse> list = new List<VillageHouse>();
            int r0x = MathUtil.FloorDiv(minX - 40, VillageRegion), r1x = MathUtil.FloorDiv(maxX + 40, VillageRegion);
            int r0z = MathUtil.FloorDiv(minZ - 40, VillageRegion), r1z = MathUtil.FloorDiv(maxZ + 40, VillageRegion);
            for (int rz = r0z; rz <= r1z; rz++)
            {
                for (int rx = r0x; rx <= r1x; rx++)
                {
                    if (Rng.Hash01(Seed ^ 0x7111, rx, rz) > 0.45f) continue;
                    int cx = rx * VillageRegion + 60 + (int)(Rng.Hash01(Seed ^ 0x7112, rx, rz) * (VillageRegion - 120));
                    int cz = rz * VillageRegion + 60 + (int)(Rng.Hash01(Seed ^ 0x7113, rx, rz) * (VillageRegion - 120));
                    TerrainSample centre = Sample(cx, cz);
                    Biome b = centre.Biome;
                    if (b != Biome.Plains && b != Biome.Forest && b != Biome.Desert && b != Biome.Taiga && b != Biome.Tundra) continue;
                    if (centre.Height < WorldConst.SeaLevel + 2) continue;
                    for (int sz = -1; sz <= 1; sz++)
                    {
                        for (int sx = -1; sx <= 1; sx++)
                        {
                            long k = (long)(sz + 1) * 3 + (sx + 1);
                            if (Rng.Hash01(Seed ^ (0x7120 + k), rx, rz) > 0.65f && !(sx == 0 && sz == 0)) continue;
                            int hx = cx + sx * 12, hz = cz + sz * 12;
                            if (hx < minX - 4 || hx > maxX + 4 || hz < minZ - 4 || hz > maxZ + 4) continue;
                            TerrainSample s = Sample(hx, hz);
                            if (s.Height < WorldConst.SeaLevel + 2 || Math.Abs(s.Height - centre.Height) > 4) continue;
                            VillageHouse h = new VillageHouse();
                            h.X = hx; h.Z = hz; h.Y = s.Height;
                            h.Facing = (int)(Rng.Hash01(Seed ^ (0x7140 + k), rx, rz) * 4f) & 3;
                            h.Chest = Rng.Hash01(Seed ^ (0x7160 + k), rx, rz) < 0.55f;
                            h.Desert = b == Biome.Desert;
                            list.Add(h);
                        }
                    }
                }
            }
            return list;
        }

        private static bool NearHouse(List<VillageHouse> houses, int x, int z, int margin)
        {
            for (int i = 0; i < houses.Count; i++)
                if (Math.Abs(houses[i].X - x) <= HouseHalf + margin && Math.Abs(houses[i].Z - z) <= HouseHalf + margin) return true;
            return false;
        }

        private void PlaceVillages(Column col)
        {
            int baseX = col.X * 16, baseZ = col.Z * 16;
            List<VillageHouse> houses = HousesNear(baseX - 4, baseZ - 4, baseX + 20, baseZ + 20);
            for (int i = 0; i < houses.Count; i++)
            {
                VillageHouse h = houses[i];
                byte wall = h.Desert ? B.Sandstone : B.Planks;
                byte post = h.Desert ? B.Sandstone : B.LogOak;
                int fx = h.Facing == 0 ? HouseHalf : (h.Facing == 1 ? -HouseHalf : 0);
                int fz = h.Facing == 2 ? HouseHalf : (h.Facing == 3 ? -HouseHalf : 0);
                for (int dz = -HouseHalf; dz <= HouseHalf; dz++)
                {
                    for (int dx = -HouseHalf; dx <= HouseHalf; dx++)
                    {
                        int wx = h.X + dx, wz = h.Z + dz;
                        int lx = wx - baseX, lz = wz - baseZ;
                        if (lx < 0 || lx > 15 || lz < 0 || lz > 15) continue;
                        bool edgeX = Math.Abs(dx) == HouseHalf, edgeZ = Math.Abs(dz) == HouseHalf;
                        bool edge = edgeX || edgeZ, corner = edgeX && edgeZ;

                        // Level the site: clear above the floor, then build the foundation down to solid ground.
                        for (int y = h.Y + 1; y <= h.Y + 8 && y < WorldConst.Height; y++) col.SetBlock(lx, y, lz, B.Air);
                        for (int y = h.Y - 1; y > 0 && y >= h.Y - 12; y--)
                        {
                            byte cur = col.GetBlock(lx, y, lz);
                            if (cur != B.Air && !B.IsFluid(cur) && cur != B.TallGrass && cur != B.Snow) break;
                            col.SetBlock(lx, y, lz, B.Cobble);
                        }
                        col.SetBlock(lx, h.Y, lz, h.Desert ? B.Sandstone : B.Planks);

                        if (edge)
                        {
                            bool door = dx == fx && dz == fz;
                            bool window = !corner && !door && (dx == 0 || dz == 0);
                            for (int y = h.Y + 1; y <= h.Y + 3; y++)
                            {
                                byte id = corner ? post : wall;
                                if (door && y <= h.Y + 2) id = B.Air;
                                else if (window && y == h.Y + 2) id = B.Glass;
                                col.SetBlock(lx, y, lz, id);
                            }
                        }
                        col.SetBlock(lx, h.Y + 4, lz, wall);
                    }
                }

                // Furnishing: a bed in one corner, a torch in another, and sometimes a chest on a marker block.
                SetLocal(col, baseX, baseZ, h.X - 2, h.Y + 1, h.Z - 2, B.Bed);
                SetLocal(col, baseX, baseZ, h.X - 2, h.Y + 1, h.Z + 2, B.Torch);
                if (h.Chest)
                {
                    SetLocal(col, baseX, baseZ, h.X + 2, h.Y, h.Z + 2, B.Bedrock);
                    SetLocal(col, baseX, baseZ, h.X + 2, h.Y + 1, h.Z + 2, B.Chest);
                }
            }
        }

        private static void SetLocal(Column col, int baseX, int baseZ, int wx, int y, int wz, byte id)
        {
            int lx = wx - baseX, lz = wz - baseZ;
            if (lx < 0 || lx > 15 || lz < 0 || lz > 15) return;
            col.SetBlock(lx, y, lz, id);
        }

        /// <summary>True if a loot chest of a village house stands at this position.</summary>
        public bool IsVillageChest(int x, int y, int z)
        {
            List<VillageHouse> houses = HousesNear(x - 4, z - 4, x + 4, z + 4);
            for (int i = 0; i < houses.Count; i++)
                if (houses[i].Chest && houses[i].X + 2 == x && houses[i].Z + 2 == z && houses[i].Y + 1 == y) return true;
            return false;
        }

        // ------------------------------------------------------------------ trees and plants

        private enum TreeKind { None, Oak, Birch, Spruce, Cactus }

        private TreeKind TreeFor(TerrainSample s, float roll)
        {
            if (s.Height <= WorldConst.SeaLevel + 1) return TreeKind.None;
            switch (s.Biome)
            {
                case Biome.Forest: return roll < 0.55f ? (roll < 0.14f ? TreeKind.Birch : TreeKind.Oak) : TreeKind.None;
                case Biome.Plains: return roll < 0.025f ? TreeKind.Oak : TreeKind.None;
                case Biome.Taiga: return roll < 0.5f ? TreeKind.Spruce : TreeKind.None;
                case Biome.Tundra: return roll < 0.03f ? TreeKind.Spruce : TreeKind.None;
                case Biome.Mountains: return (roll < 0.12f && s.Height < WorldConst.SeaLevel + 115) ? TreeKind.Spruce : TreeKind.None;
                case Biome.Desert: return roll < 0.04f ? TreeKind.Cactus : TreeKind.None;
                default: return TreeKind.None;
            }
        }

        private void PlaceTrees(Column col)
        {
            const int cell = 4;
            int baseX = col.X * 16, baseZ = col.Z * 16;
            int gx0 = MathUtil.FloorDiv(baseX - 4, cell), gx1 = MathUtil.FloorDiv(baseX + 19, cell);
            int gz0 = MathUtil.FloorDiv(baseZ - 4, cell), gz1 = MathUtil.FloorDiv(baseZ + 19, cell);
            List<VillageHouse> houses = HousesNear(baseX - 12, baseZ - 12, baseX + 28, baseZ + 28);
            for (int gz = gz0; gz <= gz1; gz++)
            {
                for (int gx = gx0; gx <= gx1; gx++)
                {
                    ulong hash = Rng.Hash(Seed ^ 0x7EE, gx, gz);
                    int tx = gx * cell + (int)(hash % (ulong)cell);
                    int tz = gz * cell + (int)((hash >> 8) % (ulong)cell);
                    float roll = ((hash >> 20) & 0xFFFF) / 65536f;
                    // Cheap rejection before sampling the terrain.
                    if (roll > 0.6f) continue;
                    TerrainSample s = Sample(tx, tz);
                    TreeKind kind = TreeFor(s, roll);
                    if (kind == TreeKind.None) continue;
                    if (houses.Count > 0 && NearHouse(houses, tx, tz, 5)) continue;     // keep canopies off the houses
                    PlaceTree(col, tx, tz, s.Height, kind, new Rng((long)(hash & 0x7FFFFFFFFFFFUL)));
                }
            }
        }

        private static void SetIfReplaceable(Column col, int wx, int y, int wz, byte id, bool overwriteLeaves = false)
        {
            int lx = wx - col.X * 16, lz = wz - col.Z * 16;
            if (lx < 0 || lx > 15 || lz < 0 || lz > 15 || y < 1 || y >= WorldConst.Height) return;
            byte cur = col.GetBlock(lx, y, lz);
            BlockInfo info = Blocks.Get(cur);
            if (cur == B.Air || (info.Replaceable && !info.Liquid) || (overwriteLeaves && info.Render == RenderKind.Cutout && cur != B.Glass))
                col.SetBlock(lx, y, lz, id);
        }

        private void PlaceTree(Column col, int tx, int tz, int ground, TreeKind kind, Rng rng)
        {
            // Skip trunks that fall outside this column AND have no chance of reaching in (canopy radius <= 3).
            int dxMin = col.X * 16 - 4, dxMax = col.X * 16 + 19, dzMin = col.Z * 16 - 4, dzMax = col.Z * 16 + 19;
            if (tx < dxMin || tx > dxMax || tz < dzMin || tz > dzMax) return;
            int y0 = ground + 1;
            switch (kind)
            {
                case TreeKind.Cactus:
                {
                    int h = rng.Range(1, 3);
                    for (int i = 0; i < h; i++) SetIfReplaceable(col, tx, y0 + i, tz, B.Cactus);
                    break;
                }
                case TreeKind.Oak:
                case TreeKind.Birch:
                {
                    bool birch = kind == TreeKind.Birch;
                    int h = birch ? rng.Range(5, 7) : rng.Range(4, 6);
                    byte log = birch ? B.LogBirch : B.LogOak, leaf = birch ? B.LeavesBirch : B.LeavesOak;
                    for (int i = 0; i < h; i++) SetIfReplaceable(col, tx, y0 + i, tz, log, true);
                    int topY = y0 + h - 1;
                    for (int dy = -2; dy <= 1; dy++)
                    {
                        int radius = dy >= 0 ? 1 : 2;
                        for (int dz = -radius; dz <= radius; dz++)
                        {
                            for (int dx = -radius; dx <= radius; dx++)
                            {
                                if (radius == 2 && Math.Abs(dx) == 2 && Math.Abs(dz) == 2 && rng.Chance(0.5f)) continue;
                                if (dx == 0 && dz == 0 && dy < 1) continue;   // trunk
                                SetIfReplaceable(col, tx + dx, topY + dy, tz + dz, leaf);
                            }
                        }
                    }
                    break;
                }
                case TreeKind.Spruce:
                {
                    int h = rng.Range(6, 9);
                    for (int i = 0; i < h; i++) SetIfReplaceable(col, tx, y0 + i, tz, B.LogSpruce, true);
                    int topY = y0 + h;
                    SetIfReplaceable(col, tx, topY, tz, B.LeavesSpruce);
                    int radius = 0;
                    for (int dy = 0; dy < h - 2; dy++)
                    {
                        int y = topY - 1 - dy;
                        radius = 1 + (dy % 3 == 2 ? 1 : 0) + dy / 3;
                        if (radius > 3) radius = 3;
                        for (int dz = -radius; dz <= radius; dz++)
                        {
                            for (int dx = -radius; dx <= radius; dx++)
                            {
                                if (Math.Abs(dx) + Math.Abs(dz) > radius + 1) continue;
                                if (dx == 0 && dz == 0) continue;
                                SetIfReplaceable(col, tx + dx, y, tz + dz, B.LeavesSpruce);
                            }
                        }
                    }
                    break;
                }
            }
        }

        private void PlacePlants(Column col, int[] heights, Biome[] biomes)
        {
            int baseX = col.X * 16, baseZ = col.Z * 16;
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int h = heights[lz * 16 + lx];
                    if (h <= WorldConst.SeaLevel || h + 1 >= WorldConst.Height) continue;
                    byte ground = col.GetBlock(lx, h, lz);
                    if (col.GetBlock(lx, h + 1, lz) != B.Air) continue;
                    float r = Rng.Hash01(Seed ^ 0x9A1, baseX + lx, baseZ + lz);
                    Biome biome = biomes[lz * 16 + lx];
                    if (ground == B.Grass)
                    {
                        if (r < 0.10f) col.SetBlock(lx, h + 1, lz, B.TallGrass);
                        else if (r < 0.115f) col.SetBlock(lx, h + 1, lz, B.FlowerRed);
                        else if (r < 0.13f) col.SetBlock(lx, h + 1, lz, B.FlowerYellow);
                    }
                    else if (ground == B.Sand && biome == Biome.Desert && r < 0.012f)
                    {
                        col.SetBlock(lx, h + 1, lz, B.DeadBush);
                    }
                }
            }
        }
    }
}
