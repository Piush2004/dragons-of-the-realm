using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BlockRealm.Tests
{
    public static class WorldTests
    {
        public static void RunAll()
        {
            Harness.Group("math, noise and registry");
            Harness.Run("floor division and modulo handle negatives", () =>
            {
                Check.Equal(-1, MathUtil.FloorDiv(-1, 16));
                Check.Equal(-2, MathUtil.FloorDiv(-17, 16));
                Check.Equal(15, MathUtil.Mod(-1, 16));
                Check.Equal(-1, MathUtil.FloorInt(-0.5f));
                Check.Equal(0, MathUtil.FloorInt(0.5f));
            });
            Harness.Run("noise is deterministic and bounded", () =>
            {
                Noise a = new Noise(42), b = new Noise(42), c = new Noise(43);
                float min = 9, max = -9;
                bool differs = false;
                for (int i = 0; i < 2000; i++)
                {
                    float x = i * 0.37f, z = i * 0.91f;
                    float v = a.Perlin2(x, z);
                    Check.Equal(v, b.Perlin2(x, z), "same seed");
                    if (Math.Abs(v - c.Perlin2(x, z)) > 1e-4f) differs = true;
                    min = Math.Min(min, v); max = Math.Max(max, v);
                    float v3 = a.Perlin3(x, i * 0.13f, z);
                    Check.True(v3 >= -1.3f && v3 <= 1.3f, "perlin3 out of range " + v3);
                }
                Check.True(differs, "different seeds should differ");
                Check.True(min > -1.5f && max < 1.5f && max - min > 0.8f, "range " + min + ".." + max);
            });
            Harness.Run("every block is defined with textures and light tables", () =>
            {
                for (int id = 0; id < B.Count; id++)
                {
                    BlockInfo b = Blocks.All[id];
                    Check.True(b != null, "block " + id + " is not defined");
                    Check.Equal((byte)id, b.Id, "id of " + b.Name);
                    if (b.Render != RenderKind.None)
                        Check.True(b.TileTop != null && b.TileSide != null && b.TileBottom != null, b.Name + " has no tiles");
                }
                Check.True(Blocks.TileNames.Count > 20, "tile count " + Blocks.TileNames.Count);
                Check.Equal(15, (int)Blocks.AbsorbTable[B.Stone]);
                Check.Equal(14, (int)Blocks.EmissionTable[B.Torch]);
                Check.True(Blocks.IsLiquid(B.Water) && !Blocks.IsSolid(B.Water));
            });
            Harness.Run("break times follow tool and tier", () =>
            {
                float hand = Blocks.BreakTime(B.Stone, ToolKind.None, 0);
                float wood = Blocks.BreakTime(B.Stone, ToolKind.Pickaxe, 1);
                float iron = Blocks.BreakTime(B.Stone, ToolKind.Pickaxe, 3);
                Check.True(hand > wood && wood > iron, "stone times " + hand + ", " + wood + ", " + iron);
                Check.True(Blocks.BreakTime(B.Bedrock, ToolKind.Pickaxe, 4) < 0, "bedrock unbreakable");
                Check.True(!Blocks.Harvests(B.IronOre, ToolKind.Pickaxe, 1), "wood pick cannot harvest iron ore");
                Check.True(Blocks.Harvests(B.IronOre, ToolKind.Pickaxe, 2), "stone pick harvests iron ore");
                Check.Equal(B.Cobble, (byte)Items.DropFor(B.Stone, true, null, false), "stone drops cobble");
                Check.Equal((ushort)0, Items.DropFor(B.Stone, false, null, false), "unharvested drops nothing");
            });

            Harness.Group("storage");
            Harness.Run("columns store blocks and light", () =>
            {
                Column c = new Column(0, 0);
                c.SetBlock(3, 70, 5, B.Stone);
                Check.Equal(B.Stone, c.GetBlock(3, 70, 5));
                Check.Equal(B.Air, c.GetBlock(3, 71, 5));
                Check.Equal(B.Bedrock, c.GetBlock(0, -1, 0));
                Check.Equal(4, c.TopSection);
                Check.Equal(B.Air, c.GetBlock(0, 300, 0));
            });
            Harness.Run("column codec round-trips and compresses", () =>
            {
                WorldGenerator g = new WorldGenerator(7);
                Column c = g.GenerateColumn(12, -9);
                byte[] data = ColumnCodec.Encode(c);
                Column d = ColumnCodec.Decode(12, -9, data);
                Check.Equal(c.TopSection, d.TopSection, "top section");
                for (int sy = 0; sy <= c.TopSection; sy++)
                    for (int i = 0; i < Section.Volume; i++)
                        if (c.Sections[sy].Blocks[i] != d.Sections[sy].Blocks[i]) throw new CheckFailed("mismatch in section " + sy + " at " + i);
                Check.True(data.Length < 40000, "encoded size " + data.Length);
                Console.WriteLine("        encoded column: " + data.Length + " bytes for " + (c.TopSection + 1) + " sections");
            });
            Harness.Run("world edits across column borders", () =>
            {
                World w = TestWorlds.Flat(64, 1);
                Check.Equal(B.Grass, w.GetBlock(15, 64, 0));
                Check.Equal(B.Grass, w.GetBlock(16, 64, 0));
                List<string> changed = new List<string>();
                w.SectionChanged = (cx, sy, cz) => changed.Add(cx + "," + sy + "," + cz);
                Check.True(w.SetBlock(15, 70, 3, B.Stone));
                Check.Equal(B.Stone, w.GetBlock(15, 70, 3));
                Check.True(changed.Contains("0,4,0"), "own section marked");
                Check.True(changed.Contains("1,4,0"), "neighbour column marked for the border block");
                Check.True(!w.SetBlock(500, 70, 0, B.Stone), "unloaded column rejects edits");
                Check.Equal(64, w.GetSurfaceY(5, 5));
            });

            Harness.Group("lighting");
            Harness.Run("open sky is fully lit and a roof casts shade", () =>
            {
                World w = TestWorlds.Flat(64, 1);
                Check.Equal(15, (int)w.GetSkyLight(5, 70, 5));
                Check.Equal(15, (int)w.GetSkyLight(5, 65, 5));
                for (int x = -1; x <= 1; x++)
                    for (int z = -1; z <= 1; z++) w.SetBlock(x, 70, z, B.Stone);
                // Below the centre of a 3x3 roof the light has come in sideways: 15 at the edge, 14 then 13 inward.
                Check.Equal(13, (int)w.GetSkyLight(0, 69, 0));
                Check.Equal(14, (int)w.GetSkyLight(1, 69, 0));
                Check.Equal(15, (int)w.GetSkyLight(2, 69, 0));
                w.SetBlock(0, 70, 0, B.Air);
                Check.Equal(15, (int)w.GetSkyLight(0, 69, 0));
            });
            Harness.Run("torches light a radius and removal restores darkness", () =>
            {
                World w = TestWorlds.Flat(64, 1);
                // Build an enclosed room so only the torch lights it: stone shell 7x5x7 at y 65..69.
                for (int y = 64; y <= 70; y++)
                    for (int x = -3; x <= 3; x++)
                        for (int z = -3; z <= 3; z++)
                        {
                            bool shell = y == 64 || y == 70 || Math.Abs(x) == 3 || Math.Abs(z) == 3;
                            w.SetBlock(x, y, z, shell ? B.Stone : B.Air);
                        }
                Check.Equal(0, (int)w.GetSkyLight(0, 66, 0));
                Check.Equal(0, (int)w.GetBlockLightAt(0, 66, 0));
                w.SetBlock(0, 66, 0, B.Torch);
                Check.Equal(14, (int)w.GetBlockLightAt(0, 66, 0));
                Check.Equal(13, (int)w.GetBlockLightAt(1, 66, 0));
                Check.Equal(11, (int)w.GetBlockLightAt(2, 66, 1));
                w.SetBlock(0, 66, 0, B.Air);
                Check.Equal(0, (int)w.GetBlockLightAt(1, 66, 0));
                Check.Equal(0, (int)w.GetBlockLightAt(2, 66, 1));
            });
            Harness.Run("light crosses column borders when columns arrive later", () =>
            {
                // Two columns: the left has a roof over a torch near the edge; the right is added afterwards.
                World w = new World(3);
                Column a = new Column(0, 0);
                a.EnsureSection(5);
                for (int lz = 0; lz < 16; lz++) for (int lx = 0; lx < 16; lx++) { a.SetBlock(lx, 0, lz, B.Bedrock); for (int y = 1; y <= 64; y++) a.SetBlock(lx, y, lz, B.Stone); }
                a.SetBlock(14, 70, 8, B.Torch);
                LightEngine.InitColumn(a); w.AddColumn(a); LightEngine.Stitch(w, a);
                Column b = new Column(1, 0);
                b.EnsureSection(5);
                for (int lz = 0; lz < 16; lz++) for (int lx = 0; lx < 16; lx++) { b.SetBlock(lx, 0, lz, B.Bedrock); for (int y = 1; y <= 64; y++) b.SetBlock(lx, y, lz, B.Stone); }
                LightEngine.InitColumn(b); w.AddColumn(b); LightEngine.Stitch(w, b);
                Check.Equal(14, (int)w.GetBlockLightAt(14, 70, 8));
                Check.Equal(13, (int)w.GetBlockLightAt(15, 70, 8));
                Check.Equal(12, (int)w.GetBlockLightAt(16, 70, 8), "light reached the new column");
                Check.Equal(10, (int)w.GetBlockLightAt(18, 70, 8));
            });

            Harness.Group("terrain generation");
            Harness.Run("generation is deterministic per seed", () =>
            {
                WorldGenerator g1 = new WorldGenerator(1234), g2 = new WorldGenerator(1234), g3 = new WorldGenerator(99);
                Column a = g1.GenerateColumn(3, 4), b = g2.GenerateColumn(3, 4), c = g3.GenerateColumn(3, 4);
                bool same = true, diff = false;
                for (int sy = 0; sy <= Math.Max(a.TopSection, c.TopSection); sy++)
                {
                    Section sa = a.GetSection(sy), sb = b.GetSection(sy), sc = c.GetSection(sy);
                    for (int i = 0; i < Section.Volume; i++)
                    {
                        byte va = sa == null ? (byte)0 : sa.Blocks[i];
                        byte vb = sb == null ? (byte)0 : sb.Blocks[i];
                        byte vc = sc == null ? (byte)0 : sc.Blocks[i];
                        if (va != vb) same = false;
                        if (va != vc) diff = true;
                    }
                }
                Check.True(same, "same seed must give identical columns");
                Check.True(diff, "different seeds should differ");
            });
            Harness.Run("terrain is sane: bedrock, sea, heights, variety", () =>
            {
                WorldGenerator g = new WorldGenerator(2024);
                Dictionary<Biome, int> biomes = new Dictionary<Biome, int>();
                int minH = 9999, maxH = -1, land = 0, ocean = 0, total = 0;
                for (int z = -2400; z <= 2400; z += 64)
                {
                    for (int x = -2400; x <= 2400; x += 64)
                    {
                        TerrainSample s = g.Sample(x, z);
                        total++;
                        minH = Math.Min(minH, s.Height); maxH = Math.Max(maxH, s.Height);
                        int n; biomes.TryGetValue(s.Biome, out n); biomes[s.Biome] = n + 1;
                        if (s.Height > WorldConst.SeaLevel) land++; else ocean++;
                    }
                }
                Console.WriteLine("        heights " + minH + ".." + maxH + ", land " + land + " ocean " + ocean + " of " + total);
                foreach (KeyValuePair<Biome, int> kv in biomes) Console.WriteLine("        " + kv.Key + ": " + kv.Value);
                Check.True(minH >= 1 && maxH < WorldConst.Height, "height bounds");
                Check.True(land > total * 0.3f && ocean > total * 0.1f, "needs both land and sea");
                Check.True(biomes.Count >= 6, "biome variety " + biomes.Count);

                Column c = g.GenerateColumn(0, 0);
                for (int lz = 0; lz < 16; lz++)
                    for (int lx = 0; lx < 16; lx++)
                    {
                        Check.Equal(B.Bedrock, c.GetBlock(lx, 0, lz), "bedrock floor");
                        int h = g.HeightAt(lx, lz);
                        Check.True(c.GetBlock(lx, h, lz) != B.Air || h < 6, "surface block present at " + lx + "," + lz);
                        Check.Equal(B.Air, c.GetBlock(lx, WorldConst.Height - 1, lz), "sky is open");
                    }
            });
            Harness.Run("spawn point is on dry land", () =>
            {
                WorldGenerator g = new WorldGenerator(2024);
                Vec3i s = g.FindSpawn();
                TerrainSample t = g.Sample(s.X, s.Z);
                Console.WriteLine("        spawn " + s + " biome " + t.Biome);
                Check.True(t.Height >= WorldConst.SeaLevel + 3, "spawn above water");
                Check.Equal(t.Height + 1, s.Y);
            });
            Harness.Run("caves, ores and trees all occur", () =>
            {
                WorldGenerator g = new WorldGenerator(2024);
                Dictionary<byte, int> counts = new Dictionary<byte, int>();
                for (int cz = -6; cz <= 6; cz++)
                    for (int cx = -6; cx <= 6; cx++)
                    {
                        Column c = g.GenerateColumn(cx, cz);
                        for (int sy = 0; sy <= c.TopSection; sy++)
                        {
                            Section s = c.Sections[sy];
                            for (int i = 0; i < Section.Volume; i++)
                            {
                                byte b = s.Blocks[i];
                                int n; counts.TryGetValue(b, out n); counts[b] = n + 1;
                            }
                        }
                    }
                Check.True(counts.ContainsKey(B.CoalOre) && counts[B.CoalOre] > 100, "coal");
                Check.True(counts.ContainsKey(B.IronOre) && counts[B.IronOre] > 50, "iron");
                Check.True(counts.ContainsKey(B.DiamondOre) && counts[B.DiamondOre] > 2, "diamond");
                Check.True(counts.ContainsKey(B.Water) && counts[B.Water] > 1000, "water");
                Check.True(counts.ContainsKey(B.LogOak) || counts.ContainsKey(B.LogSpruce), "trees");
                Check.True(counts.ContainsKey(B.LeavesOak) || counts.ContainsKey(B.LeavesSpruce), "leaves");
                int air = counts.ContainsKey(B.Air) ? counts[B.Air] : 0;
                Check.True(counts.ContainsKey(B.Lava), "deep lava");
                Console.WriteLine("        stone " + counts[B.Stone] + ", coal " + counts[B.CoalOre] + ", iron " + counts[B.IronOre] +
                    ", gold " + (counts.ContainsKey(B.GoldOre) ? counts[B.GoldOre] : 0) + ", diamond " + counts[B.DiamondOre] + ", lava " + counts[B.Lava]);
            });
            Harness.Run("trees are complete across column borders", () =>
            {
                WorldGenerator g = new WorldGenerator(2024);
                int treesChecked = 0, orphans = 0;
                for (int cz = -24; cz < 24 && treesChecked < 400; cz += 3)
                    for (int cx = -24; cx < 24 && treesChecked < 400; cx += 3)
                    {
                        TerrainSample s = g.Sample(cx * 16 + 8, cz * 16 + 8);
                        if (s.Biome != Biome.Forest && s.Biome != Biome.Taiga) continue;
                        World w = new World(2024);
                        for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) w.AddColumn(g.GenerateColumn(cx + dx, cz + dz));
                        Column mid = w.GetColumn(cx, cz);
                        // Every leaf in the middle column must have a trunk within 3 blocks horizontally, searched in all nine columns.
                        for (int lz = 0; lz < 16; lz++)
                            for (int lx = 0; lx < 16; lx++)
                                for (int y = s.Height - 6; y < s.Height + 14; y++)
                                {
                                    byte b = mid.GetBlock(lx, y, lz);
                                    if (b != B.LeavesOak && b != B.LeavesSpruce && b != B.LeavesBirch) continue;
                                    treesChecked++;
                                    bool found = false;
                                    for (int ox = -3; ox <= 3 && !found; ox++)
                                        for (int oz = -3; oz <= 3 && !found; oz++)
                                            for (int oy = -9; oy <= 1 && !found; oy++)
                                            {
                                                byte t = w.GetBlock(cx * 16 + lx + ox, y + oy, cz * 16 + lz + oz);
                                                if (t == B.LogOak || t == B.LogSpruce || t == B.LogBirch) found = true;
                                            }
                                    if (!found) orphans++;
                                }
                    }
                Console.WriteLine("        leaves checked: " + treesChecked + ", orphans: " + orphans);
                Check.True(treesChecked > 100, "not enough trees sampled: " + treesChecked);
                Check.Equal(0, orphans, "leaves without a trunk nearby (tree cut at a border)");
            });
            Harness.Run("dragon lairs exist on peaks and are levelled", () =>
            {
                WorldGenerator g = new WorldGenerator(2024);
                List<Lair> lairs = g.LairsNear(-6000, -6000, 6000, 6000);
                Console.WriteLine("        lairs within 6 km: " + lairs.Count);
                Check.True(lairs.Count >= 3, "at least three lairs, got " + lairs.Count);
                Lair l = lairs[0];
                Column c = g.GenerateColumn(MathUtil.FloorDiv(l.X, 16), MathUtil.FloorDiv(l.Z, 16));
                int lx = MathUtil.Mod(l.X, 16), lz = MathUtil.Mod(l.Z, 16);
                Check.Equal(B.Chest, c.GetBlock(lx, l.Y + 1, lz), "chest at the centre");
                Check.True(c.GetBlock(lx, l.Y, lz) == B.ScorchedStone, "platform");
                Check.Equal(B.Air, c.GetBlock(lx, l.Y + 5, lz), "air above the platform");
            });
            Harness.Run("generation throughput", () =>
            {
                WorldGenerator g = new WorldGenerator(2024);
                Stopwatch sw = Stopwatch.StartNew();
                int n = 0;
                for (int cz = 0; cz < 6; cz++) for (int cx = 0; cx < 6; cx++) { Column c = g.GenerateColumn(cx * 7, cz * 7); LightEngine.InitColumn(c); n++; }
                sw.Stop();
                double per = sw.Elapsed.TotalMilliseconds / n;
                Console.WriteLine("        " + per.ToString("0.0") + " ms per column (generate + light), " + n + " columns");
                Check.True(per < 150, "too slow: " + per + " ms per column");
            });

            Harness.Group("meshing");
            Harness.Run("a lone block has six faces", () =>
            {
                World w = TestWorlds.Flat(60, 1);
                w.SetBlock(5, 70, 5, B.Stone);
                PaddedSection p = new PaddedSection();
                w.CopyPadded(0, 4, 0, p);
                MeshBuffers m = SectionMesher.Mesh(p);
                Check.Equal(24, m.VertexCount, "vertices");
                Check.Equal(36, m.Indices.Length, "indices");
                Check.Equal(36, m.OpaqueIndexCount);
            });
            Harness.Run("faces between two blocks are culled", () =>
            {
                World w = TestWorlds.Flat(60, 1);
                w.SetBlock(5, 70, 5, B.Stone); w.SetBlock(6, 70, 5, B.Stone);
                PaddedSection p = new PaddedSection();
                w.CopyPadded(0, 4, 0, p);
                MeshBuffers m = SectionMesher.Mesh(p);
                Check.Equal(40, m.VertexCount, "10 faces");
            });
            Harness.Run("triangle winding faces outward on every face", () =>
            {
                World w = TestWorlds.Flat(60, 1);
                w.SetBlock(8, 70, 8, B.Stone);
                PaddedSection p = new PaddedSection();
                w.CopyPadded(0, 4, 0, p);
                MeshBuffers m = SectionMesher.Mesh(p);
                for (int t = 0; t < m.Indices.Length; t += 3)
                {
                    MeshVertex a = m.Vertices[m.Indices[t]], b = m.Vertices[m.Indices[t + 1]], c = m.Vertices[m.Indices[t + 2]];
                    float ux = b.PX - a.PX, uy = b.PY - a.PY, uz = b.PZ - a.PZ;
                    float vx = c.PX - a.PX, vy = c.PY - a.PY, vz = c.PZ - a.PZ;
                    float cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
                    float dot = cx * a.NX + cy * a.NY + cz * a.NZ;
                    Check.True(dot > 0, "triangle " + (t / 3) + " faces inward");
                }
            });
            Harness.Run("plants are crossed quads, water has a lowered top", () =>
            {
                World w = TestWorlds.Flat(60, 1);
                w.SetBlock(4, 65, 4, B.TallGrass);
                w.SetBlock(9, 65, 9, B.Water);
                PaddedSection p = new PaddedSection();
                w.CopyPadded(0, 4, 0, p);
                MeshBuffers m = SectionMesher.Mesh(p);
                Check.Equal(12, m.CutoutIndexCount, "two quads = 12 indices");
                Check.True(m.WaterIndexCount > 0, "water faces");
                float maxY = 0;
                for (int i = 0; i < m.VertexCount; i++)
                    if (m.Vertices[i].Flags == 1) maxY = Math.Max(maxY, m.Vertices[i].PY);
                // Water at y=65 is local y=1 in section 4 (y 64..79), so its lowered top is 1.875.
                Check.Near(1.875f, maxY, 0.01f, "water top");
            });
            Harness.Run("ambient occlusion darkens inner corners", () =>
            {
                World w = TestWorlds.Flat(60, 1);
                // An L-shape: the floor corner touching a wall should be darker than a free corner.
                w.SetBlock(8, 65, 8, B.Stone); w.SetBlock(8, 66, 8, B.Stone); w.SetBlock(9, 65, 8, B.Stone);
                PaddedSection p = new PaddedSection();
                w.CopyPadded(0, 4, 0, p);
                MeshBuffers m = SectionMesher.Mesh(p);
                int minAo = 255, maxAo = 0;
                for (int i = 0; i < m.VertexCount; i++) { minAo = Math.Min(minAo, m.Vertices[i].Ao); maxAo = Math.Max(maxAo, m.Vertices[i].Ao); }
                Check.True(minAo < 255 && maxAo == 255, "ao range " + minAo + ".." + maxAo);
            });
            Harness.Run("meshing throughput on real terrain", () =>
            {
                World w = new World(5);
                WorldGenerator g = new WorldGenerator(5);
                for (int cz = -1; cz <= 1; cz++) for (int cx = -1; cx <= 1; cx++)
                    {
                        Column c = g.GenerateColumn(cx, cz); LightEngine.InitColumn(c); w.AddColumn(c); LightEngine.Stitch(w, c);
                    }
                PaddedSection p = new PaddedSection();
                Stopwatch sw = Stopwatch.StartNew();
                int sections = 0, tris = 0, verts = 0;
                Column mid = w.GetColumn(0, 0);
                for (int sy = 0; sy <= mid.TopSection; sy++)
                {
                    w.CopyPadded(0, sy, 0, p);
                    MeshBuffers m = SectionMesher.Mesh(p);
                    sections++; tris += m.TriangleCount; verts += m.VertexCount;
                }
                sw.Stop();
                Console.WriteLine("        " + sections + " sections, " + tris + " triangles, " + verts + " vertices in " + sw.ElapsedMilliseconds + " ms");
                Check.True(tris > 100, "terrain produces geometry");
                Check.True(sw.ElapsedMilliseconds < 800, "meshing too slow");
            });
        }
    }
}
