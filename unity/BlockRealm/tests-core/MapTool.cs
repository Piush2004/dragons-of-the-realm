using System;
using System.IO;

namespace BlockRealm.Tests
{
    /// <summary>Writes a top-down biome and height map as a PPM image so the terrain can be inspected visually.</summary>
    public static class MapTool
    {
        public static void Write(string path, long seed, int centerX, int centerZ, int size, int step)
        {
            WorldGenerator g = new WorldGenerator(seed);
            int n = size / step;
            using (FileStream fs = new FileStream(path, FileMode.Create))
            {
                byte[] header = System.Text.Encoding.ASCII.GetBytes("P6\n" + n + " " + n + "\n255\n");
                fs.Write(header, 0, header.Length);
                byte[] row = new byte[n * 3];
                for (int j = 0; j < n; j++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        int x = centerX - size / 2 + i * step, z = centerZ - size / 2 + j * step;
                        TerrainSample s = g.Sample(x, z);
                        int r, gg, b;
                        float shade = 0.65f + 0.35f * Math.Min(1f, Math.Max(0f, (s.Height - 60) / 220f));
                        switch (s.Biome)
                        {
                            case Biome.Ocean: { float d = Math.Min(1f, (WorldConst.SeaLevel - s.Height) / 60f); r = (int)(30 - 20 * d); gg = (int)(90 - 40 * d); b = (int)(190 - 60 * d); break; }
                            case Biome.River: r = 70; gg = 140; b = 220; break;
                            case Biome.Beach: r = 225; gg = 210; b = 150; break;
                            case Biome.Plains: r = 130; gg = 190; b = 80; break;
                            case Biome.Forest: r = 40; gg = 130; b = 50; break;
                            case Biome.Desert: r = 235; gg = 200; b = 120; break;
                            case Biome.Tundra: r = 225; gg = 235; b = 240; break;
                            case Biome.Taiga: r = 70; gg = 120; b = 100; break;
                            default: r = 140; gg = 140; b = 145; if (s.Height > WorldConst.SeaLevel + 125) { r = 250; gg = 250; b = 255; } break;
                        }
                        if (s.Biome != Biome.Ocean && s.Biome != Biome.River) { r = (int)(r * shade); gg = (int)(gg * shade); b = (int)(b * shade); }
                        row[i * 3] = (byte)Math.Min(255, r); row[i * 3 + 1] = (byte)Math.Min(255, gg); row[i * 3 + 2] = (byte)Math.Min(255, b);
                    }
                    fs.Write(row, 0, row.Length);
                }
            }
        }
    }
}
