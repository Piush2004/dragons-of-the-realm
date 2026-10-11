using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace BlockRealm
{
    /// <summary>A 16x16x16 block of the world: block ids plus packed light (sky in the high nibble, block in the low).</summary>
    public sealed class Section
    {
        public const int Volume = 4096;
        public readonly byte[] Blocks = new byte[Volume];
        public readonly byte[] Light = new byte[Volume];

        public static int Index(int lx, int ly, int lz) { return (ly << 8) | (lz << 4) | lx; }
    }

    /// <summary>A 16x16 footprint of the world, 384 blocks tall, stored as lazily allocated sections.</summary>
    public sealed class Column
    {
        public readonly int X, Z;                                   // column coordinates
        public readonly Section[] Sections = new Section[WorldConst.SectionsPerColumn];
        /// <summary>For each (lz*16+lx): y of the first cell above the highest light-blocking block (0 if none).</summary>
        public readonly short[] SkyTop = new short[WorldConst.ColumnArea];
        public bool LightReady;
        public int TopSection = -1;                                 // highest allocated section, -1 when empty
        public bool Modified;                                       // changed since generation (for saving)

        public Column(int x, int z) { X = x; Z = z; }

        public Section GetSection(int sy) { return (sy >= 0 && sy < Sections.Length) ? Sections[sy] : null; }

        public Section EnsureSection(int sy)
        {
            // Sections are kept contiguous from 0 to TopSection so light queries below the top are always answerable.
            for (int i = 0; i <= sy; i++)
            {
                if (Sections[i] == null)
                {
                    Section created = new Section();
                    // A section added above a lit column sits above every block, so it is in full daylight.
                    if (LightReady) { byte[] l = created.Light; for (int k = 0; k < l.Length; k++) l[k] = 0xF0; }
                    Sections[i] = created;
                }
            }
            if (sy > TopSection) TopSection = sy;
            return Sections[sy];
        }

        public byte GetBlock(int lx, int y, int lz)
        {
            if (y < 0) return B.Bedrock;
            if (y >= WorldConst.Height) return B.Air;
            Section s = Sections[y >> 4];
            return s == null ? B.Air : s.Blocks[Section.Index(lx, y & 15, lz)];
        }

        public void SetBlock(int lx, int y, int lz, byte id)
        {
            if (y < 0 || y >= WorldConst.Height) return;
            int sy = y >> 4;
            Section s = Sections[sy] ?? EnsureSection(sy);
            s.Blocks[Section.Index(lx, y & 15, lz)] = id;
        }

        public byte GetSky(int lx, int y, int lz)
        {
            if (y >= WorldConst.Height) return 15;
            if (y < 0) return 0;
            Section s = Sections[y >> 4];
            if (s == null) return 15;                               // above the top section: open sky
            return (byte)(s.Light[Section.Index(lx, y & 15, lz)] >> 4);
        }

        public byte GetBlockLight(int lx, int y, int lz)
        {
            if (y < 0 || y >= WorldConst.Height) return 0;
            Section s = Sections[y >> 4];
            return s == null ? (byte)0 : (byte)(s.Light[Section.Index(lx, y & 15, lz)] & 15);
        }

        public void SetSky(int lx, int y, int lz, int v)
        {
            Section s = Sections[y >> 4];
            if (s == null) return;
            int i = Section.Index(lx, y & 15, lz);
            s.Light[i] = (byte)((s.Light[i] & 15) | (v << 4));
        }

        public void SetBlockLight(int lx, int y, int lz, int v)
        {
            Section s = Sections[y >> 4];
            if (s == null) return;
            int i = Section.Index(lx, y & 15, lz);
            s.Light[i] = (byte)((s.Light[i] & 0xF0) | v);
        }

        public bool IsSectionAllAir(int sy)
        {
            Section s = Sections[sy];
            if (s == null) return true;
            byte[] b = s.Blocks;
            for (int i = 0; i < b.Length; i++) if (b[i] != 0) return false;
            return true;
        }
    }

    /// <summary>One 18x18x18 copy of a section and its border, so meshing can run on another thread without locks.</summary>
    public sealed class PaddedSection
    {
        public const int N = 18;
        public readonly byte[] Blocks = new byte[N * N * N];
        public readonly byte[] Light = new byte[N * N * N];      // sky<<4 | block
        public int ColumnX, SectionY, ColumnZ;
        public bool AllAir;

        public static int Index(int x, int y, int z) { return (y * N + z) * N + x; }   // x,y,z in 0..17 (1..16 = inside)
    }

    /// <summary>The set of loaded columns plus helpers to read and edit blocks across column borders.</summary>
    public sealed class World
    {
        private readonly Dictionary<ColumnPos, Column> _columns = new Dictionary<ColumnPos, Column>();
        public readonly long Seed;

        /// <summary>Raised when a section's blocks or light changed and its mesh is stale.</summary>
        public Action<int, int, int> SectionChanged;
        /// <summary>Raised for every block edit made through SetBlock (x, y, z, old, new).</summary>
        public Action<int, int, int, byte, byte> BlockChanged;

        public World(long seed) { Seed = seed; }

        public int ColumnCount { get { return _columns.Count; } }
        public IEnumerable<Column> Columns { get { return _columns.Values; } }

        public Column GetColumn(int cx, int cz)
        {
            Column c;
            return _columns.TryGetValue(new ColumnPos(cx, cz), out c) ? c : null;
        }

        public bool HasColumn(int cx, int cz) { return _columns.ContainsKey(new ColumnPos(cx, cz)); }

        public void AddColumn(Column c) { _columns[new ColumnPos(c.X, c.Z)] = c; }

        public bool RemoveColumn(int cx, int cz) { return _columns.Remove(new ColumnPos(cx, cz)); }

        public byte GetBlock(int x, int y, int z)
        {
            if (y < 0) return B.Bedrock;
            if (y >= WorldConst.Height) return B.Air;
            Column c = GetColumn(x >> 4, z >> 4);
            return c == null ? B.Air : c.GetBlock(x & 15, y, z & 15);
        }

        /// <summary>Like GetBlock but reports whether the column is loaded, so physics can treat unloaded ground as solid.</summary>
        public bool TryGetBlock(int x, int y, int z, out byte id)
        {
            if (y < 0) { id = B.Bedrock; return true; }
            if (y >= WorldConst.Height) { id = B.Air; return true; }
            Column c = GetColumn(x >> 4, z >> 4);
            if (c == null) { id = B.Air; return false; }
            id = c.GetBlock(x & 15, y, z & 15);
            return true;
        }

        /// <summary>Raw write used by generation and network decoding: no light or events.</summary>
        public void SetBlockRaw(int x, int y, int z, byte id)
        {
            Column c = GetColumn(x >> 4, z >> 4);
            if (c != null) c.SetBlock(x & 15, y, z & 15, id);
        }

        /// <summary>Edits a block, updates light and marks affected sections stale. Returns false if the column is not loaded.</summary>
        public bool SetBlock(int x, int y, int z, byte id)
        {
            if (y < 0 || y >= WorldConst.Height) return false;
            Column c = GetColumn(x >> 4, z >> 4);
            if (c == null) return false;
            int lx = x & 15, lz = z & 15;
            byte old = c.GetBlock(lx, y, lz);
            if (old == id) return true;
            c.SetBlock(lx, y, lz, id);
            c.Modified = true;
            if (c.LightReady) LightEngine.OnBlockChanged(this, x, y, z, old, id);
            MarkDirty(x, y, z);
            if (BlockChanged != null) BlockChanged(x, y, z, old, id);
            return true;
        }

        /// <summary>Marks the section containing the block, and any neighbouring section the block touches, as stale.</summary>
        public void MarkDirty(int x, int y, int z)
        {
            if (SectionChanged == null) return;
            int cx = x >> 4, cz = z >> 4, sy = y >> 4;
            int lx = x & 15, ly = y & 15, lz = z & 15;
            SectionChanged(cx, sy, cz);
            if (lx == 0) SectionChanged(cx - 1, sy, cz);
            if (lx == 15) SectionChanged(cx + 1, sy, cz);
            if (lz == 0) SectionChanged(cx, sy, cz - 1);
            if (lz == 15) SectionChanged(cx, sy, cz + 1);
            if (ly == 0 && sy > 0) SectionChanged(cx, sy - 1, cz);
            if (ly == 15 && sy < WorldConst.SectionsPerColumn - 1) SectionChanged(cx, sy + 1, cz);
        }

        public byte GetSkyLight(int x, int y, int z)
        {
            if (y >= WorldConst.Height) return 15;
            if (y < 0) return 0;
            Column c = GetColumn(x >> 4, z >> 4);
            return c == null ? (byte)15 : c.GetSky(x & 15, y, z & 15);
        }

        public byte GetBlockLightAt(int x, int y, int z)
        {
            if (y < 0 || y >= WorldConst.Height) return 0;
            Column c = GetColumn(x >> 4, z >> 4);
            return c == null ? (byte)0 : c.GetBlockLight(x & 15, y, z & 15);
        }

        /// <summary>Y of the highest solid or liquid block in the column of (x, z), or -1 if the column is not loaded or empty.</summary>
        public int GetSurfaceY(int x, int z)
        {
            Column c = GetColumn(x >> 4, z >> 4);
            if (c == null) return -1;
            int lx = x & 15, lz = z & 15;
            int y = Math.Min(WorldConst.Height - 1, c.TopSection < 0 ? 0 : (c.TopSection + 1) * 16 - 1);
            for (; y >= 0; y--)
            {
                byte id = c.GetBlock(lx, y, lz);
                if (id != B.Air && (Blocks.SolidTable[id] || Blocks.IsLiquid(id))) return y;
            }
            return -1;
        }

        /// <summary>Copies a section and its one-block border into a padded buffer for off-thread meshing.</summary>
        public void CopyPadded(int cx, int sy, int cz, PaddedSection dst)
        {
            dst.ColumnX = cx; dst.SectionY = sy; dst.ColumnZ = cz;
            Array.Clear(dst.Blocks, 0, dst.Blocks.Length);
            Array.Clear(dst.Light, 0, dst.Light.Length);
            int baseX = cx * 16, baseY = sy * 16, baseZ = cz * 16;
            bool anyBlock = false;
            for (int pz = 0; pz < PaddedSection.N; pz++)
            {
                int wz = baseZ + pz - 1;
                for (int px = 0; px < PaddedSection.N; px++)
                {
                    int wx = baseX + px - 1;
                    Column col = GetColumn(wx >> 4, wz >> 4);
                    int lx = wx & 15, lz = wz & 15;
                    for (int py = 0; py < PaddedSection.N; py++)
                    {
                        int wy = baseY + py - 1;
                        int di = PaddedSection.Index(px, py, pz);
                        if (wy < 0) { dst.Blocks[di] = B.Bedrock; continue; }
                        if (wy >= WorldConst.Height) { dst.Light[di] = (byte)(15 << 4); continue; }
                        if (col == null) { dst.Light[di] = (byte)(15 << 4); continue; }
                        Section s = col.Sections[wy >> 4];
                        if (s == null) { dst.Light[di] = (byte)(15 << 4); continue; }
                        int si = Section.Index(lx, wy & 15, lz);
                        byte b = s.Blocks[si];
                        dst.Blocks[di] = b;
                        dst.Light[di] = s.Light[si];
                        if (b != 0 && px >= 1 && px <= 16 && py >= 1 && py <= 16 && pz >= 1 && pz <= 16) anyBlock = true;
                    }
                }
            }
            dst.AllAir = !anyBlock;
        }
    }

    /// <summary>Compact column encoding for the network and save files: per-section run-length coding, deflated.</summary>
    public static class ColumnCodec
    {
        public static byte[] Encode(Column c)
        {
            using (MemoryStream raw = new MemoryStream(8192))
            {
                BinaryWriter w = new BinaryWriter(raw);
                w.Write((byte)(c.TopSection + 1));
                for (int sy = 0; sy <= c.TopSection; sy++)
                {
                    Section s = c.Sections[sy];
                    byte[] b = s == null ? new byte[Section.Volume] : s.Blocks;
                    int i = 0;
                    while (i < b.Length)
                    {
                        byte v = b[i];
                        int run = 1;
                        while (i + run < b.Length && b[i + run] == v && run < 65535) run++;
                        if (run >= 3 || v >= 0xFE)
                        {
                            w.Write((byte)0xFE); w.Write(v); w.Write((ushort)run);
                        }
                        else
                        {
                            for (int k = 0; k < run; k++) w.Write(v);
                        }
                        i += run;
                    }
                }
                w.Flush();
                using (MemoryStream packed = new MemoryStream())
                {
                    using (DeflateStream d = new DeflateStream(packed, CompressionLevel.Fastest, true))
                    {
                        d.Write(raw.GetBuffer(), 0, (int)raw.Length);
                    }
                    return packed.ToArray();
                }
            }
        }

        public static Column Decode(int cx, int cz, byte[] data)
        {
            Column c = new Column(cx, cz);
            using (MemoryStream packed = new MemoryStream(data))
            using (DeflateStream d = new DeflateStream(packed, CompressionMode.Decompress))
            using (MemoryStream raw = new MemoryStream())
            {
                d.CopyTo(raw);
                byte[] buf = raw.ToArray();
                int pos = 0;
                int sections = buf[pos++];
                for (int sy = 0; sy < sections; sy++)
                {
                    Section s = c.EnsureSection(sy);
                    int i = 0;
                    while (i < Section.Volume)
                    {
                        byte v = buf[pos++];
                        if (v == 0xFE)
                        {
                            byte value = buf[pos++];
                            int run = buf[pos] | (buf[pos + 1] << 8); pos += 2;
                            for (int k = 0; k < run; k++) s.Blocks[i++] = value;
                        }
                        else s.Blocks[i++] = v;
                    }
                }
            }
            return c;
        }
    }
}
