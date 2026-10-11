using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BlockRealm.Tests
{
    /// <summary>A tiny test runner so the core can be verified without Unity or any test package.</summary>
    public static class Harness
    {
        public static int Passed, Failed;
        public static readonly List<string> Failures = new List<string>();
        public static string Filter;

        public static void Run(string name, Action body)
        {
            if (Filter != null && name.IndexOf(Filter, StringComparison.OrdinalIgnoreCase) < 0) return;
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                body();
                sw.Stop();
                Passed++;
                Console.WriteLine("  PASS  " + name + "  (" + sw.ElapsedMilliseconds + " ms)");
            }
            catch (Exception e)
            {
                sw.Stop();
                Failed++;
                string msg = e is CheckFailed ? e.Message : e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace;
                Failures.Add(name + ": " + msg);
                Console.WriteLine("  FAIL  " + name + "\n        " + msg.Replace("\n", "\n        "));
            }
        }

        public static void Group(string title) { Console.WriteLine(); Console.WriteLine("== " + title); }
    }

    public sealed class CheckFailed : Exception
    {
        public CheckFailed(string m) : base(m) { }
    }

    public static class Check
    {
        public static void True(bool cond, string message = "expected true")
        {
            if (!cond) throw new CheckFailed(message);
        }

        public static void Equal<T>(T expected, T actual, string what = "value")
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new CheckFailed(what + ": expected " + expected + " but got " + actual);
        }

        public static void Near(float expected, float actual, float tolerance, string what = "value")
        {
            if (Math.Abs(expected - actual) > tolerance)
                throw new CheckFailed(what + ": expected " + expected + " +/- " + tolerance + " but got " + actual);
        }

        public static void InRange(float value, float lo, float hi, string what = "value")
        {
            if (value < lo || value > hi) throw new CheckFailed(what + ": " + value + " not in [" + lo + ", " + hi + "]");
        }

        public static void Throws(Action a, string what = "exception")
        {
            try { a(); }
            catch (CheckFailed) { throw; }
            catch (Exception) { return; }
            throw new CheckFailed("expected " + what + " but nothing was thrown");
        }
    }

    public static class TestWorlds
    {
        /// <summary>A flat stone world with a grass top at floorY across (2r+1)^2 columns, fully lit.</summary>
        public static World Flat(int floorY = 64, int radius = 2)
        {
            World w = new World(1);
            for (int cz = -radius; cz <= radius; cz++)
            {
                for (int cx = -radius; cx <= radius; cx++)
                {
                    Column c = new Column(cx, cz);
                    c.EnsureSection((floorY + 16) >> 4);
                    for (int lz = 0; lz < 16; lz++)
                        for (int lx = 0; lx < 16; lx++)
                        {
                            c.SetBlock(lx, 0, lz, B.Bedrock);
                            for (int y = 1; y < floorY; y++) c.SetBlock(lx, y, lz, B.Stone);
                            c.SetBlock(lx, floorY, lz, B.Grass);
                        }
                    LightEngine.InitColumn(c);
                    w.AddColumn(c);
                    LightEngine.Stitch(w, c);
                }
            }
            return w;
        }
    }
}
