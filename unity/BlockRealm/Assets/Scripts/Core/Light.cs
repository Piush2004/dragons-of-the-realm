using System;
using System.Collections.Generic;

namespace BlockRealm
{
    /// <summary>
    /// Minecraft-style flood-fill lighting. Sky light and block light (torches, lava) each run 0..15 and are stored
    /// per voxel. A cell loses 1 level per step (more through leaves or water), except that full sky light
    /// travels straight down through clear blocks without loss.
    /// </summary>
    public static class LightEngine
    {
        private struct Node
        {
            public int X, Y, Z;
            public byte Level;
            public Node(int x, int y, int z, int level) { X = x; Y = y; Z = z; Level = (byte)level; }
        }

        private static readonly int[] Dx = { 1, -1, 0, 0, 0, 0 };
        private static readonly int[] Dy = { 0, 0, 1, -1, 0, 0 };
        private static readonly int[] Dz = { 0, 0, 0, 0, 1, -1 };
        // index 3 is "down" in the arrays above
        private const int DownIndex = 3;

        [ThreadStatic] private static Queue<Node> _addSky, _addBlock, _remSky, _remBlock;
        [ThreadStatic] private static HashSet<long> _touched;

        private static void EnsureQueues()
        {
            if (_addSky == null)
            {
                _addSky = new Queue<Node>(); _addBlock = new Queue<Node>();
                _remSky = new Queue<Node>(); _remBlock = new Queue<Node>();
                _touched = new HashSet<long>();
            }
        }

        private static long SectionKey(int cx, int sy, int cz)
        {
            return ((long)(cx & 0xFFFFFF) << 40) | ((long)(cz & 0xFFFFFF) << 16) | (uint)sy;
        }

        private static void Touch(int x, int y, int z) { _touched.Add(SectionKey(x >> 4, y >> 4, z >> 4)); }

        private static int Cost(byte blockId)
        {
            int a = Blocks.AbsorbTable[blockId];
            return a < 1 ? 1 : a;
        }

        private static void FlushTouched(World w)
        {
            if (w.SectionChanged != null)
            {
                foreach (long key in _touched)
                {
                    int sy = (int)(key & 0xFFFF);
                    int cz = (int)((key >> 16) & 0xFFFFFF); if ((cz & 0x800000) != 0) cz -= 0x1000000;
                    int cx = (int)((key >> 40) & 0xFFFFFF); if ((cx & 0x800000) != 0) cx -= 0x1000000;
                    w.SectionChanged(cx, sy, cz);
                }
            }
            _touched.Clear();
        }

        // ---------------------------------------------------------------- column initialisation

        /// <summary>Computes sky and block light inside one column, ignoring its neighbours (see Stitch).</summary>
        public static void InitColumn(Column c)
        {
            EnsureQueues();
            _addBlock.Clear();
            // Sky heights and the straight-down sky light.
            int top = c.TopSection;
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int skyTop = 0;
                    for (int y = (top + 1) * 16 - 1; y >= 0; y--)
                    {
                        if (Blocks.AbsorbTable[c.GetBlock(lx, y, lz)] > 0) { skyTop = y + 1; break; }
                    }
                    c.SkyTop[lz * 16 + lx] = (short)skyTop;

                    int level = 15;
                    for (int y = (top + 1) * 16 - 1; y >= 0; y--)
                    {
                        byte b = c.GetBlock(lx, y, lz);
                        int absorb = Blocks.AbsorbTable[b];
                        if (!(level == 15 && absorb == 0)) level = Math.Max(0, level - Math.Max(1, absorb));
                        if (absorb >= 15) level = 0;
                        c.SetSky(lx, y, lz, level);
                        int emit = Blocks.EmissionTable[b];
                        c.SetBlockLight(lx, y, lz, emit);
                        if (emit > 1) _addBlock.Enqueue(new Node(c.X * 16 + lx, y, c.Z * 16 + lz, emit));
                    }
                }
            }

            // Spread sideways and into overhangs: run the same BFS as edits do, against a one-column world.
            // Seeds: every lit cell below the sky line, and open-air cells beside a taller neighbour column.
            World temp = new World(0);
            temp.AddColumn(c);
            _addSky.Clear();
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int skyTop = c.SkyTop[lz * 16 + lx];
                    int reach = skyTop;
                    if (lx > 0) reach = Math.Max(reach, (int)c.SkyTop[lz * 16 + lx - 1]);
                    if (lx < 15) reach = Math.Max(reach, (int)c.SkyTop[lz * 16 + lx + 1]);
                    if (lz > 0) reach = Math.Max(reach, (int)c.SkyTop[(lz - 1) * 16 + lx]);
                    if (lz < 15) reach = Math.Max(reach, (int)c.SkyTop[(lz + 1) * 16 + lx]);
                    int wx = c.X * 16 + lx, wz = c.Z * 16 + lz;
                    int limit = Math.Min(reach, (top + 1) * 16);
                    for (int y = 0; y < limit; y++)
                    {
                        int sky = c.GetSky(lx, y, lz);
                        if (sky > 1) _addSky.Enqueue(new Node(wx, y, wz, sky));
                    }
                }
            }
            PropagateAdd(temp, _addSky, true);
            PropagateAdd(temp, _addBlock, false);
            _touched.Clear();
            c.LightReady = true;
        }

        /// <summary>After a column joins the world, exchanges light with neighbours that are already lit.</summary>
        public static void Stitch(World w, Column c)
        {
            EnsureQueues();
            _addSky.Clear(); _addBlock.Clear();
            int maxY = (c.TopSection + 1) * 16;
            for (int dir = 0; dir < 4; dir++)
            {
                int ncx = c.X, ncz = c.Z;
                if (dir == 0) ncx++; else if (dir == 1) ncx--; else if (dir == 2) ncz++; else ncz--;
                Column n = w.GetColumn(ncx, ncz);
                if (n == null || !n.LightReady) continue;
                int top = Math.Max(maxY, (n.TopSection + 1) * 16);
                for (int i = 0; i < 16; i++)
                {
                    int ax, az, bx, bz;   // my edge cell, the neighbour's edge cell (column-local)
                    if (dir == 0) { ax = 15; az = i; bx = 0; bz = i; }
                    else if (dir == 1) { ax = 0; az = i; bx = 15; bz = i; }
                    else if (dir == 2) { ax = i; az = 15; bx = i; bz = 0; }
                    else { ax = i; az = 0; bx = i; bz = 15; }
                    for (int y = 0; y < top; y++)
                    {
                        int wax = c.X * 16 + ax, waz = c.Z * 16 + az;
                        int wbx = n.X * 16 + bx, wbz = n.Z * 16 + bz;
                        int sa = c.GetSky(ax, y, az), sb = n.GetSky(bx, y, bz);
                        if (sa > 1) _addSky.Enqueue(new Node(wax, y, waz, sa));
                        if (sb > 1) _addSky.Enqueue(new Node(wbx, y, wbz, sb));
                        int ba = c.GetBlockLight(ax, y, az), bb = n.GetBlockLight(bx, y, bz);
                        if (ba > 1) _addBlock.Enqueue(new Node(wax, y, waz, ba));
                        if (bb > 1) _addBlock.Enqueue(new Node(wbx, y, wbz, bb));
                    }
                }
            }
            PropagateAdd(w, _addSky, true);
            PropagateAdd(w, _addBlock, false);
            FlushTouched(w);
        }

        // ---------------------------------------------------------------- incremental edits

        public static void OnBlockChanged(World w, int x, int y, int z, byte oldId, byte newId)
        {
            EnsureQueues();
            _addSky.Clear(); _addBlock.Clear(); _remSky.Clear(); _remBlock.Clear();
            Column col = w.GetColumn(x >> 4, z >> 4);
            if (col == null) return;
            int lx = x & 15, lz = z & 15;

            // Keep the sky heightmap current.
            int idx = lz * 16 + lx;
            int skyTop = col.SkyTop[idx];
            if (Blocks.AbsorbTable[newId] > 0)
            {
                if (y + 1 > skyTop) col.SkyTop[idx] = (short)(y + 1);
            }
            else if (y + 1 == skyTop)
            {
                int ny = y - 1;
                while (ny >= 0 && Blocks.AbsorbTable[col.GetBlock(lx, ny, lz)] == 0) ny--;
                col.SkyTop[idx] = (short)(ny + 1);
            }

            // Make sure the light arrays reach this cell.
            if ((y >> 4) > col.TopSection) { col.EnsureSection(y >> 4); }

            // 1. Remove the old light at this cell and everything that depended on it.
            int oldSky = col.GetSky(lx, y, lz), oldBlk = col.GetBlockLight(lx, y, lz);
            if (oldSky > 0)
            {
                col.SetSky(lx, y, lz, 0);
                _remSky.Enqueue(new Node(x, y, z, oldSky));
            }
            if (oldBlk > 0)
            {
                col.SetBlockLight(lx, y, lz, 0);
                _remBlock.Enqueue(new Node(x, y, z, oldBlk));
            }
            PropagateRemove(w, _remSky, _addSky, true);
            PropagateRemove(w, _remBlock, _addBlock, false);

            // 2. Give the cell its new light.
            int emission = Blocks.EmissionTable[newId];
            if (emission > 0)
            {
                col.SetBlockLight(lx, y, lz, emission);
                _addBlock.Enqueue(new Node(x, y, z, emission));
            }
            if (Blocks.AbsorbTable[newId] < 15)
            {
                int cost = Cost(newId);
                int best = 0;
                int above = w.GetSkyLight(x, y + 1, z);
                if (above == 15 && Blocks.AbsorbTable[newId] == 0) best = 15;
                else if (above > 0) best = Math.Max(best, above - cost);
                for (int d = 0; d < 6; d++)
                {
                    if (d == 2) continue;
                    int nv = SkyAt(w, x + Dx[d], y + Dy[d], z + Dz[d]);
                    if (nv > 0) best = Math.Max(best, nv - cost);
                }
                if (best > 0)
                {
                    col.SetSky(lx, y, lz, best);
                    _addSky.Enqueue(new Node(x, y, z, best));
                }
                int bl = 0;
                for (int d = 0; d < 6; d++)
                {
                    int nv = BlockAt(w, x + Dx[d], y + Dy[d], z + Dz[d]);
                    if (nv > 0) bl = Math.Max(bl, nv - cost);
                }
                if (bl > col.GetBlockLight(lx, y, lz))
                {
                    col.SetBlockLight(lx, y, lz, bl);
                    _addBlock.Enqueue(new Node(x, y, z, bl));
                }
            }
            Touch(x, y, z);
            PropagateAdd(w, _addSky, true);
            PropagateAdd(w, _addBlock, false);
            FlushTouched(w);
        }

        // ---------------------------------------------------------------- BFS

        private static int SkyAt(World w, int x, int y, int z)
        {
            if (y >= WorldConst.Height) return 15;
            if (y < 0) return 0;
            Column c = w.GetColumn(x >> 4, z >> 4);
            return c == null ? 0 : c.GetSky(x & 15, y, z & 15);
        }

        private static int BlockAt(World w, int x, int y, int z)
        {
            if (y < 0 || y >= WorldConst.Height) return 0;
            Column c = w.GetColumn(x >> 4, z >> 4);
            return c == null ? 0 : c.GetBlockLight(x & 15, y, z & 15);
        }

        private static void PropagateAdd(World w, Queue<Node> queue, bool sky)
        {
            while (queue.Count > 0)
            {
                Node n = queue.Dequeue();
                // The entry may be stale: a removal pass can have darkened this cell after it was queued.
                Column own = w.GetColumn(n.X >> 4, n.Z >> 4);
                if (own == null || n.Y < 0 || n.Y >= WorldConst.Height || (n.Y >> 4) > own.TopSection) continue;
                int actual = sky ? own.GetSky(n.X & 15, n.Y, n.Z & 15) : own.GetBlockLight(n.X & 15, n.Y, n.Z & 15);
                if (actual < n.Level) continue;
                for (int d = 0; d < 6; d++)
                {
                    int nx = n.X + Dx[d], ny = n.Y + Dy[d], nz = n.Z + Dz[d];
                    if (ny < 0 || ny >= WorldConst.Height) continue;
                    Column c = w.GetColumn(nx >> 4, nz >> 4);
                    if (c == null) continue;
                    int lx = nx & 15, lz = nz & 15;
                    if ((ny >> 4) > c.TopSection) continue;
                    byte nb = c.GetBlock(lx, ny, lz);
                    int absorb = Blocks.AbsorbTable[nb];
                    if (absorb >= 15) continue;
                    int level;
                    if (sky && d == DownIndex && n.Level == 15 && absorb == 0) level = 15;
                    else level = n.Level - (absorb < 1 ? 1 : absorb);
                    if (level <= 0) continue;
                    int cur = sky ? c.GetSky(lx, ny, lz) : c.GetBlockLight(lx, ny, lz);
                    if (cur >= level) continue;
                    if (sky) c.SetSky(lx, ny, lz, level); else c.SetBlockLight(lx, ny, lz, level);
                    Touch(nx, ny, nz);
                    queue.Enqueue(new Node(nx, ny, nz, level));
                }
            }
        }

        private static void PropagateRemove(World w, Queue<Node> remove, Queue<Node> readd, bool sky)
        {
            while (remove.Count > 0)
            {
                Node n = remove.Dequeue();
                for (int d = 0; d < 6; d++)
                {
                    int nx = n.X + Dx[d], ny = n.Y + Dy[d], nz = n.Z + Dz[d];
                    if (ny < 0 || ny >= WorldConst.Height) continue;
                    Column c = w.GetColumn(nx >> 4, nz >> 4);
                    if (c == null) continue;
                    int lx = nx & 15, lz = nz & 15;
                    if ((ny >> 4) > c.TopSection) continue;
                    int nv = sky ? c.GetSky(lx, ny, lz) : c.GetBlockLight(lx, ny, lz);
                    if (nv == 0) continue;
                    bool streamed = sky && d == DownIndex && n.Level == 15 && nv == 15;
                    if (nv < n.Level || streamed)
                    {
                        if (sky) c.SetSky(lx, ny, lz, 0); else c.SetBlockLight(lx, ny, lz, 0);
                        Touch(nx, ny, nz);
                        remove.Enqueue(new Node(nx, ny, nz, nv));
                    }
                    else
                    {
                        readd.Enqueue(new Node(nx, ny, nz, nv));
                    }
                }
            }
        }
    }
}
