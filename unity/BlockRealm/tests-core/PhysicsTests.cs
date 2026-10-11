using System;

namespace BlockRealm.Tests
{
    public static class PhysicsTests
    {
        public static void RunAll()
        {
            Harness.Group("physics");
            Harness.Run("a falling box lands exactly on the floor", () =>
            {
                World w = TestWorlds.Flat(64, 1);
                Aabb box = Aabb.FromCenterBottom(new Vec3(8.5f, 80f, 8.5f), 0.3f, 1.8f);
                bool hx, hy, hz;
                float y = box.Min.Y;
                for (int i = 0; i < 400 && !(box.Min.Y <= 65.0001f); i++)
                {
                    VoxelPhysics.Move(w, ref box, new Vec3(0, -0.5f, 0), out hx, out hy, out hz);
                }
                Check.Near(65f, box.Min.Y, 0.001f, "resting height");
            });
            Harness.Run("terminal fall speed does not tunnel through a thin floor", () =>
            {
                World w = TestWorlds.Flat(64, 1);
                Aabb box = Aabb.FromCenterBottom(new Vec3(8.5f, 150f, 8.5f), 0.3f, 1.8f);
                bool hx, hy, hz;
                for (int i = 0; i < 600; i++)
                {
                    VoxelPhysics.Move(w, ref box, new Vec3(0, -78.4f / 60f, 0), out hx, out hy, out hz);
                    Check.True(box.Min.Y >= 64.999f, "fell through at step " + i + " y=" + box.Min.Y);
                }
                Check.Near(65f, box.Min.Y, 0.001f);
            });
            Harness.Run("walking into a wall stops and slides along it", () =>
            {
                World w = TestWorlds.Flat(64, 1);
                for (int y = 65; y <= 67; y++) for (int z = 0; z < 16; z++) w.SetBlock(10, y, z, B.Stone);
                Aabb box = Aabb.FromCenterBottom(new Vec3(5.5f, 65f, 5.5f), 0.3f, 1.8f);
                bool hx, hy, hz;
                for (int i = 0; i < 120; i++) VoxelPhysics.Move(w, ref box, new Vec3(0.1f, 0, 0.05f), out hx, out hy, out hz);
                Check.Near(10f, box.Max.X, 0.01f, "wall contact");
                Check.True(box.Center.Z > 8f, "slid along the wall, z=" + box.Center.Z);
            });
            Harness.Run("cannot step into a one-block-high gap shorter than the player", () =>
            {
                World w = TestWorlds.Flat(64, 1);
                w.SetBlock(8, 66, 8, B.Stone);     // ceiling over a 1-high gap
                w.SetBlock(8, 65, 8, B.Air);
                Aabb box = Aabb.FromCenterBottom(new Vec3(6.5f, 65f, 8.5f), 0.3f, 1.8f);
                bool hx, hy, hz;
                for (int i = 0; i < 40; i++) VoxelPhysics.Move(w, ref box, new Vec3(0.1f, 0, 0), out hx, out hy, out hz);
                Check.True(box.Max.X <= 8.0f + 0.0001f || box.Max.Y > 66.5f || !VoxelPhysics.BoxIntersectsSolid(w, box), "never overlaps solids");
            });
            Harness.Run("raycast reports the block and face", () =>
            {
                World w = TestWorlds.Flat(64, 1);
                w.SetBlock(8, 70, 8, B.Stone);
                RayHit h = VoxelPhysics.Raycast(w, new Vec3(8.5f, 70.5f, 2.5f), new Vec3(0, 0, 1), 10f);
                Check.True(h.Hit, "hit");
                Check.Equal(8, h.X); Check.Equal(70, h.Y); Check.Equal(8, h.Z);
                Check.Equal(Face.North, h.Face, "face");
                Check.Equal(7, h.PlaceZ, "place cell is in front of the face");
                Check.Near(5.5f, h.Distance, 0.01f, "distance");
                RayHit miss = VoxelPhysics.Raycast(w, new Vec3(8.5f, 70.5f, 2.5f), new Vec3(0, 0, 1), 4f);
                Check.True(!miss.Hit, "out of reach");
                RayHit down = VoxelPhysics.Raycast(w, new Vec3(3.5f, 70.5f, 3.5f), new Vec3(0, -1, 0), 20f);
                Check.True(down.Hit && down.Y == 64 && down.Face == Face.Up, "downward ray hits the floor top");
            });
            Harness.Run("raycast skips water unless asked", () =>
            {
                World w = TestWorlds.Flat(64, 1);
                for (int y = 65; y <= 67; y++) w.SetBlock(4, y, 4, B.Water);
                RayHit h = VoxelPhysics.Raycast(w, new Vec3(4.5f, 70f, 4.5f), new Vec3(0, -1, 0), 20f);
                Check.True(h.Hit && h.Y == 64, "ray passes through water to the floor");
                RayHit hl = VoxelPhysics.Raycast(w, new Vec3(4.5f, 70f, 4.5f), new Vec3(0, -1, 0), 20f, true);
                Check.True(hl.Hit && hl.Y == 67, "ray stops at the water when liquids count");
            });
            Harness.Run("unloaded terrain is solid so nothing falls into the void", () =>
            {
                World w = TestWorlds.Flat(64, 0);
                Aabb box = Aabb.FromCenterBottom(new Vec3(40.5f, 80f, 8.5f), 0.3f, 1.8f);
                bool hx, hy, hz;
                VoxelPhysics.Move(w, ref box, new Vec3(0, -5f, 0), out hx, out hy, out hz);
                Check.True(hy, "blocked by unloaded column");
            });
        }
    }
}
