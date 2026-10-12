using System;
using System.Collections.Generic;

namespace BlockRealm.Sim
{
    /// <summary>
    /// Small grid pathfinder for walking creatures: A* over block cells with steps up (one block, by jumping) and down
    /// (up to three blocks). Bounded by a node budget so a bad request can never stall a tick.
    /// </summary>
    public static class Pathfinder
    {
        private struct Node
        {
            public int X, Y, Z;
            public float G, F;
            public int Parent;
        }

        private static readonly int[] Dx = { 1, -1, 0, 0 };
        private static readonly int[] Dz = { 0, 0, 1, -1 };

        /// <summary>True if a creature of the given height can stand with its feet in this cell.</summary>
        public static bool CanStand(World w, int x, int y, int z, int height = 2)
        {
            byte below;
            if (!w.TryGetBlock(x, y - 1, z, out below)) return false;
            if (!Blocks.IsSolid(below)) return false;
            for (int i = 0; i < height; i++)
            {
                byte id;
                if (!w.TryGetBlock(x, y + i, z, out id)) return false;
                if (Blocks.IsSolid(id)) return false;
                if (B.IsLava(id) || id == B.Fire || id == B.Cactus) return false;
            }
            return true;
        }

        /// <summary>
        /// Finds a walkable route from start to goal. Returns false (and an empty list) when none is found within the budget.
        /// The path contains cell centres to walk through, start excluded.
        /// </summary>
        public static bool Find(World w, int sx, int sy, int sz, int gx, int gy, int gz, List<Vec3> path, int maxNodes = 500, int height = 2)
        {
            path.Clear();
            if (sx == gx && sy == gy && sz == gz) return true;
            int maxDist = 40;
            if (Math.Abs(gx - sx) > maxDist || Math.Abs(gz - sz) > maxDist) return false;

            List<Node> nodes = new List<Node>(128);
            Dictionary<long, int> best = new Dictionary<long, int>();
            List<int> open = new List<int>();
            nodes.Add(new Node { X = sx, Y = sy, Z = sz, G = 0f, F = Heuristic(sx, sy, sz, gx, gy, gz), Parent = -1 });
            best[Key(sx, sy, sz)] = 0;
            open.Add(0);
            int expanded = 0, bestNode = 0;
            float bestH = nodes[0].F;

            while (open.Count > 0 && expanded < maxNodes)
            {
                // Pop the open node with the lowest F (the open list is short; a linear scan is fine).
                int oi = 0;
                for (int i = 1; i < open.Count; i++) if (nodes[open[i]].F < nodes[open[oi]].F) oi = i;
                int ni = open[oi];
                open[oi] = open[open.Count - 1];
                open.RemoveAt(open.Count - 1);
                Node n = nodes[ni];
                expanded++;

                float h = Heuristic(n.X, n.Y, n.Z, gx, gy, gz);
                if (h < bestH) { bestH = h; bestNode = ni; }
                // Reaching the goal cell, or standing right next to it when the goal itself is occupied (a creature or a wall).
                if ((n.X == gx && n.Z == gz && Math.Abs(n.Y - gy) <= 1) || (h <= 1.5f && Math.Abs(n.Y - gy) <= 1))
                {
                    Reconstruct(nodes, ni, path);
                    return true;
                }

                for (int d = 0; d < 4; d++)
                {
                    int nx = n.X + Dx[d], nz = n.Z + Dz[d];
                    // Same level, one step up, or a drop of up to three blocks.
                    int[] dys = { 0, 1, -1, -2, -3 };
                    for (int k = 0; k < dys.Length; k++)
                    {
                        int ny = n.Y + dys[k];
                        if (!CanStand(w, nx, ny, nz, height)) continue;
                        if (dys[k] == 1 && !HeadroomToJump(w, n.X, n.Y, n.Z, height)) continue;
                        if (dys[k] < 0)
                        {
                            // The air between the ledge and the landing must be clear.
                            bool clear = true;
                            for (int yy = ny; yy <= n.Y + height - 1 && clear; yy++)
                                if (Blocks.IsSolid(w.GetBlock(nx, yy, nz))) clear = false;
                            if (!clear) continue;
                        }
                        float step = 1f + (dys[k] == 1 ? 0.8f : 0f) + (dys[k] < 0 ? 0.3f * -dys[k] : 0f);
                        if (Blocks.IsLiquid(w.GetBlock(nx, ny, nz))) step += 1.5f;
                        float g = n.G + step;
                        long key = Key(nx, ny, nz);
                        int existing;
                        if (best.TryGetValue(key, out existing) && nodes[existing].G <= g) continue;
                        Node child = new Node { X = nx, Y = ny, Z = nz, G = g, F = g + Heuristic(nx, ny, nz, gx, gy, gz), Parent = ni };
                        nodes.Add(child);
                        best[key] = nodes.Count - 1;
                        open.Add(nodes.Count - 1);
                        break;       // the first valid vertical option for this direction is enough
                    }
                }
            }

            // No complete route: head for the closest cell found, so the creature at least makes progress.
            if (bestNode != 0)
            {
                Reconstruct(nodes, bestNode, path);
                return path.Count > 0;
            }
            return false;
        }

        private static bool HeadroomToJump(World w, int x, int y, int z, int height)
        {
            return !Blocks.IsSolid(w.GetBlock(x, y + height, z));
        }

        private static float Heuristic(int x, int y, int z, int gx, int gy, int gz)
        {
            float dx = Math.Abs(x - gx), dz = Math.Abs(z - gz), dy = Math.Abs(y - gy);
            return dx + dz + dy * 0.5f;
        }

        private static long Key(int x, int y, int z)
        {
            return ((long)(x & 0xFFFFF) << 40) | ((long)(z & 0xFFFFF) << 16) | (long)(y & 0xFFFF);
        }

        private static void Reconstruct(List<Node> nodes, int index, List<Vec3> path)
        {
            path.Clear();
            int i = index;
            while (i > 0)
            {
                Node n = nodes[i];
                path.Add(new Vec3(n.X + 0.5f, n.Y, n.Z + 0.5f));
                i = n.Parent;
            }
            path.Reverse();
        }
    }
}
