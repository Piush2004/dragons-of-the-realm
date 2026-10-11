using System;
namespace BlockRealm.Tests
{
    public static class DebugTests
    {
        public static void Run()
        {
            World w = TestWorlds.Flat(64, 1);
            for (int y = 64; y <= 70; y++)
                for (int x = -3; x <= 3; x++)
                    for (int z = -3; z <= 3; z++)
                    {
                        bool shell = y == 64 || y == 70 || Math.Abs(x) == 3 || Math.Abs(z) == 3;
                        w.SetBlock(x, y, z, shell ? B.Stone : B.Air);
                    }
            for (int y = 71; y >= 63; y--)
            {
                string row = "y=" + y + "  ";
                for (int x = -4; x <= 4; x++) row += w.GetSkyLight(x, y, 0).ToString("00") + " ";
                Console.WriteLine(row + "   blocks: " + w.GetBlock(0, y, 0));
            }
        }
    }
}
