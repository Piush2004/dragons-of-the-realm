using System;

namespace BlockRealm
{
    public struct RayHit
    {
        public bool Hit;
        public int X, Y, Z;          // the block that was hit
        public Face Face;            // which face of it was hit
        public float Distance;
        public Vec3 Point;
        public int PlaceX, PlaceY, PlaceZ;   // the empty cell in front of the face (where a block would be placed)
    }

    /// <summary>Axis-aligned box sweeps against the block grid, and block raycasts.</summary>
    public static class VoxelPhysics
    {
        public const float Epsilon = 0.0005f;

        /// <summary>Unloaded terrain counts as solid so nothing falls through the world before it streams in.</summary>
        public static bool IsSolidCell(World w, int x, int y, int z)
        {
            byte id;
            if (!w.TryGetBlock(x, y, z, out id)) return true;
            return Blocks.SolidTable[id];
        }

        /// <summary>
        /// Moves a box by <paramref name="delta"/>, stopping at solid blocks. Axes are resolved Y, then X, then Z so that
        /// walking along walls slides and landing stops cleanly. Returns the actual movement.
        /// </summary>
        public static Vec3 Move(World w, ref Aabb box, Vec3 delta, out bool hitX, out bool hitY, out bool hitZ)
        {
            Vec3 want = delta;
            hitX = hitY = hitZ = false;

            if (delta.Y != 0f)
            {
                float d = ClipY(w, box, delta.Y);
                hitY = Math.Abs(d - delta.Y) > 1e-6f;
                box = box.Offset(new Vec3(0, d, 0));
                delta.Y = d;
            }
            if (delta.X != 0f)
            {
                float d = ClipX(w, box, delta.X);
                hitX = Math.Abs(d - delta.X) > 1e-6f;
                box = box.Offset(new Vec3(d, 0, 0));
                delta.X = d;
            }
            if (delta.Z != 0f)
            {
                float d = ClipZ(w, box, delta.Z);
                hitZ = Math.Abs(d - delta.Z) > 1e-6f;
                box = box.Offset(new Vec3(0, 0, d));
                delta.Z = d;
            }
            return delta;
        }

        private static float ClipY(World w, Aabb box, float dy)
        {
            int x0 = MathUtil.FloorInt(box.Min.X + Epsilon), x1 = MathUtil.FloorInt(box.Max.X - Epsilon);
            int z0 = MathUtil.FloorInt(box.Min.Z + Epsilon), z1 = MathUtil.FloorInt(box.Max.Z - Epsilon);
            int y0, y1;
            if (dy > 0) { y0 = MathUtil.FloorInt(box.Max.Y - Epsilon); y1 = MathUtil.FloorInt(box.Max.Y + dy); }
            else { y0 = MathUtil.FloorInt(box.Min.Y + dy); y1 = MathUtil.FloorInt(box.Min.Y + Epsilon); }
            for (int y = y0; y <= y1; y++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        if (!IsSolidCell(w, x, y, z)) continue;
                        if (dy > 0) { float gap = y - box.Max.Y; if (gap >= -Epsilon && gap < dy) dy = Math.Max(0f, gap); }
                        else { float gap = (y + 1) - box.Min.Y; if (gap <= Epsilon && gap > dy) dy = Math.Min(0f, gap); }
                    }
                }
            }
            return dy;
        }

        private static float ClipX(World w, Aabb box, float dx)
        {
            int y0 = MathUtil.FloorInt(box.Min.Y + Epsilon), y1 = MathUtil.FloorInt(box.Max.Y - Epsilon);
            int z0 = MathUtil.FloorInt(box.Min.Z + Epsilon), z1 = MathUtil.FloorInt(box.Max.Z - Epsilon);
            int x0, x1;
            if (dx > 0) { x0 = MathUtil.FloorInt(box.Max.X - Epsilon); x1 = MathUtil.FloorInt(box.Max.X + dx); }
            else { x0 = MathUtil.FloorInt(box.Min.X + dx); x1 = MathUtil.FloorInt(box.Min.X + Epsilon); }
            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int z = z0; z <= z1; z++)
                    {
                        if (!IsSolidCell(w, x, y, z)) continue;
                        if (dx > 0) { float gap = x - box.Max.X; if (gap >= -Epsilon && gap < dx) dx = Math.Max(0f, gap); }
                        else { float gap = (x + 1) - box.Min.X; if (gap <= Epsilon && gap > dx) dx = Math.Min(0f, gap); }
                    }
                }
            }
            return dx;
        }

        private static float ClipZ(World w, Aabb box, float dz)
        {
            int x0 = MathUtil.FloorInt(box.Min.X + Epsilon), x1 = MathUtil.FloorInt(box.Max.X - Epsilon);
            int y0 = MathUtil.FloorInt(box.Min.Y + Epsilon), y1 = MathUtil.FloorInt(box.Max.Y - Epsilon);
            int z0, z1;
            if (dz > 0) { z0 = MathUtil.FloorInt(box.Max.Z - Epsilon); z1 = MathUtil.FloorInt(box.Max.Z + dz); }
            else { z0 = MathUtil.FloorInt(box.Min.Z + dz); z1 = MathUtil.FloorInt(box.Min.Z + Epsilon); }
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        if (!IsSolidCell(w, x, y, z)) continue;
                        if (dz > 0) { float gap = z - box.Max.Z; if (gap >= -Epsilon && gap < dz) dz = Math.Max(0f, gap); }
                        else { float gap = (z + 1) - box.Min.Z; if (gap <= Epsilon && gap > dz) dz = Math.Min(0f, gap); }
                    }
                }
            }
            return dz;
        }

        public static bool BoxIntersectsSolid(World w, Aabb box)
        {
            int x0 = MathUtil.FloorInt(box.Min.X + Epsilon), x1 = MathUtil.FloorInt(box.Max.X - Epsilon);
            int y0 = MathUtil.FloorInt(box.Min.Y + Epsilon), y1 = MathUtil.FloorInt(box.Max.Y - Epsilon);
            int z0 = MathUtil.FloorInt(box.Min.Z + Epsilon), z1 = MathUtil.FloorInt(box.Max.Z - Epsilon);
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        if (IsSolidCell(w, x, y, z)) return true;
            return false;
        }

        /// <summary>True if any cell the box overlaps is a liquid; <paramref name="water"/> distinguishes water from lava.</summary>
        public static bool BoxInLiquid(World w, Aabb box, out bool lava)
        {
            lava = false;
            bool any = false;
            int x0 = MathUtil.FloorInt(box.Min.X + Epsilon), x1 = MathUtil.FloorInt(box.Max.X - Epsilon);
            int y0 = MathUtil.FloorInt(box.Min.Y + Epsilon), y1 = MathUtil.FloorInt(box.Max.Y - Epsilon);
            int z0 = MathUtil.FloorInt(box.Min.Z + Epsilon), z1 = MathUtil.FloorInt(box.Max.Z - Epsilon);
            for (int y = y0; y <= y1; y++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        byte id = w.GetBlock(x, y, z);
                        if (id == B.Water) any = true;
                        else if (id == B.Lava) { any = true; lava = true; }
                    }
                }
            }
            return any;
        }

        public static bool IsLiquidAt(World w, Vec3 p)
        {
            byte id = w.GetBlock(MathUtil.FloorInt(p.X), MathUtil.FloorInt(p.Y), MathUtil.FloorInt(p.Z));
            return Blocks.IsLiquid(id);
        }

        /// <summary>
        /// Voxel DDA (Amanatides and Woo). Hits the first block that is not air or liquid within <paramref name="maxDistance"/>.
        /// </summary>
        public static RayHit Raycast(World w, Vec3 origin, Vec3 direction, float maxDistance, bool hitLiquids = false)
        {
            RayHit hit = new RayHit();
            Vec3 dir = direction.Normalized;
            if (dir.LengthSq < 1e-8f) return hit;

            int x = MathUtil.FloorInt(origin.X), y = MathUtil.FloorInt(origin.Y), z = MathUtil.FloorInt(origin.Z);
            int stepX = dir.X > 0 ? 1 : -1, stepY = dir.Y > 0 ? 1 : -1, stepZ = dir.Z > 0 ? 1 : -1;
            float tDeltaX = dir.X == 0 ? float.MaxValue : Math.Abs(1f / dir.X);
            float tDeltaY = dir.Y == 0 ? float.MaxValue : Math.Abs(1f / dir.Y);
            float tDeltaZ = dir.Z == 0 ? float.MaxValue : Math.Abs(1f / dir.Z);
            float tMaxX = dir.X == 0 ? float.MaxValue : ((dir.X > 0 ? x + 1 - origin.X : origin.X - x) * tDeltaX);
            float tMaxY = dir.Y == 0 ? float.MaxValue : ((dir.Y > 0 ? y + 1 - origin.Y : origin.Y - y) * tDeltaY);
            float tMaxZ = dir.Z == 0 ? float.MaxValue : ((dir.Z > 0 ? z + 1 - origin.Z : origin.Z - z) * tDeltaZ);

            int prevX = x, prevY = y, prevZ = z;
            Face face = Face.Up;
            float t = 0f;
            for (int i = 0; i < 256; i++)
            {
                byte id = w.GetBlock(x, y, z);
                if (id != 0 && (hitLiquids || !Blocks.IsLiquid(id)))
                {
                    hit.Hit = true;
                    hit.X = x; hit.Y = y; hit.Z = z;
                    hit.Face = face;
                    hit.Distance = t;
                    hit.Point = origin + dir * t;
                    hit.PlaceX = prevX; hit.PlaceY = prevY; hit.PlaceZ = prevZ;
                    return hit;
                }
                prevX = x; prevY = y; prevZ = z;
                if (tMaxX < tMaxY && tMaxX < tMaxZ)
                {
                    t = tMaxX; if (t > maxDistance) return hit;
                    x += stepX; tMaxX += tDeltaX; face = stepX > 0 ? Face.West : Face.East;
                }
                else if (tMaxY < tMaxZ)
                {
                    t = tMaxY; if (t > maxDistance) return hit;
                    y += stepY; tMaxY += tDeltaY; face = stepY > 0 ? Face.Down : Face.Up;
                }
                else
                {
                    t = tMaxZ; if (t > maxDistance) return hit;
                    z += stepZ; tMaxZ += tDeltaZ; face = stepZ > 0 ? Face.North : Face.South;
                }
            }
            return hit;
        }

        /// <summary>First solid block hit by a point moving along a segment (for projectiles); returns the fraction travelled.</summary>
        public static bool SegmentHitsSolid(World w, Vec3 from, Vec3 to, out float fraction)
        {
            Vec3 d = to - from;
            float len = d.Length;
            fraction = 1f;
            if (len < 1e-5f) return false;
            RayHit h = Raycast(w, from, d, len);
            if (h.Hit && Blocks.IsSolid(w.GetBlock(h.X, h.Y, h.Z)))
            {
                fraction = h.Distance / len;
                return true;
            }
            return false;
        }
    }
}
