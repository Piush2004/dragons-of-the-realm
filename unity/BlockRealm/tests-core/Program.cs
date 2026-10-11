using System;
using BlockRealm.Tests;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length > 0 && !args[0].StartsWith("--")) Harness.Filter = args[0];
        Console.WriteLine("BlockRealm core tests");

        if (args.Length > 0 && args[0] == "--debug") { DebugTests.Run(); return 0; }
        if (args.Length >= 2 && args[0] == "--map")
        {
            long seed = args.Length > 2 ? long.Parse(args[2]) : 2024;
            int size = args.Length > 3 ? int.Parse(args[3]) : 4096;
            int step = args.Length > 4 ? int.Parse(args[4]) : 8;
            MapTool.Write(args[1], seed, 0, 0, size, step);
            Console.WriteLine("wrote " + args[1]);
            return 0;
        }
        WorldTests.RunAll();
        PhysicsTests.RunAll();
        SecurityTests.RunAll();
        GameTests.RunAll();
        NetTests.RunAll();

        Console.WriteLine();
        Console.WriteLine("passed " + Harness.Passed + ", failed " + Harness.Failed);
        foreach (string f in Harness.Failures) Console.WriteLine("  * " + f.Split('\n')[0]);
        return Harness.Failed == 0 ? 0 : 1;
    }
}
