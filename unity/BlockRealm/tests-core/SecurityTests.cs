using System;
using System.IO;
using System.IO.Compression;

namespace BlockRealm.Tests
{
    /// <summary>Malformed and hostile input must be rejected cleanly, never crash or exhaust memory.</summary>
    public static class SecurityTests
    {
        private static byte[] Deflate(byte[] raw)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (DeflateStream d = new DeflateStream(ms, CompressionLevel.Fastest, true)) d.Write(raw, 0, raw.Length);
                return ms.ToArray();
            }
        }

        private static void ExpectInvalid(Action a, string what)
        {
            try { a(); }
            catch (InvalidDataException) { return; }
            catch (Exception e) { throw new CheckFailed(what + ": threw " + e.GetType().Name + " instead of InvalidDataException"); }
            throw new CheckFailed(what + ": was accepted");
        }

        public static void RunAll()
        {
            Harness.Group("hostile input");
            Harness.Run("column decoder rejects a decompression bomb", () =>
            {
                byte[] bomb = Deflate(new byte[50 * 1024 * 1024]);      // 50 MB of zeros, a few KB compressed
                Console.WriteLine("        bomb is " + bomb.Length + " bytes compressed");
                ExpectInvalid(() => ColumnCodec.Decode(0, 0, bomb), "bomb");
            });
            Harness.Run("column decoder rejects bad section counts and runs", () =>
            {
                ExpectInvalid(() => ColumnCodec.Decode(0, 0, Deflate(new byte[] { 200 })), "section count");
                ExpectInvalid(() => ColumnCodec.Decode(0, 0, Deflate(new byte[] { 1, 0xFE, 1, 0xFF, 0xFF })), "run beyond the section");
                ExpectInvalid(() => ColumnCodec.Decode(0, 0, Deflate(new byte[] { 1, 0xFE, 1, 0x00, 0x00 })), "zero length run");
                ExpectInvalid(() => ColumnCodec.Decode(0, 0, Deflate(new byte[] { 2, 0xFE, 0, 0x00, 0x10 })), "truncated second section");
                ExpectInvalid(() => ColumnCodec.Decode(0, 0, new byte[] { 1, 2, 3, 4, 5 }), "garbage");
                ExpectInvalid(() => ColumnCodec.Decode(0, 0, new byte[0]), "empty");
                ExpectInvalid(() => ColumnCodec.Decode(0, 0, new byte[ColumnCodec.MaxEncodedBytes + 1]), "oversized");
            });
            Harness.Run("column decoder maps unknown block ids to air", () =>
            {
                // One section: a run of 4096 blocks with id 200 (undefined) must become air.
                byte[] raw = { 1, 0xFE, 200, 0x00, 0x10 };
                Column c = ColumnCodec.Decode(0, 0, Deflate(raw));
                Check.Equal(B.Air, c.GetBlock(3, 3, 3));
            });
            Harness.Run("the per-address limiter fails closed when it cannot track a source", () =>
            {
                BlockRealm.Sim.SourceLimiter lim = new BlockRealm.Sim.SourceLimiter(64, 4096);
                for (int i = 0; i < 4096; i++) lim.Add("src" + i, 100);          // fill the table with recent sources
                Check.True(lim.Count("fresh-source", 101, 1200) >= 5, "an untrackable source counts as over the limit");
                Check.Equal(1, lim.Count("src5", 101, 1200), "tracked sources count normally");
                // After the window passes the table empties and new sources are tracked again.
                Check.Equal(0, lim.Count("fresh-source", 100 + 1200 + 2400 + 10, 1200), "recovers once old entries expire");
            });
            Harness.Run("limiter queues are long enough for the global thresholds", () =>
            {
                BlockRealm.Sim.SourceLimiter lim = new BlockRealm.Sim.SourceLimiter(512);
                for (int i = 0; i < 250; i++) lim.Add("*", 10);
                Check.Equal(250, lim.Count("*", 11, 1200), "a backstop of 200 is reachable");
                BlockRealm.Sim.SourceLimiter small = new BlockRealm.Sim.SourceLimiter(8);
                for (int i = 0; i < 50; i++) small.Add("x", 10);
                Check.Equal(8, small.Count("x", 11, 1200), "per-source memory is bounded");
            });
            Harness.Run("IPv6 addresses collapse to their /64 so rotation does not escape limits", () =>
            {
                string a = BlockRealm.Sim.GameServer.SourceKeyFor("[2001:db8:1:2:aaaa:bbbb:cccc:dddd]:5000");
                string b = BlockRealm.Sim.GameServer.SourceKeyFor("[2001:DB8:1:2:1:2:3:4]:6000");
                string c = BlockRealm.Sim.GameServer.SourceKeyFor("[2001:db8:1:3::1]:5000");
                Check.Equal(a, b, "same /64");
                Check.True(a != c, "different /64");
                Check.Equal("203.0.113.9", BlockRealm.Sim.GameServer.SourceKeyFor("203.0.113.9:25565"));
            });
            Harness.Run("text from a server is stripped of control characters and markup before display", () =>
            {
                Check.Equal("helloworld", BlockRealm.Sim.GameClient.Sanitize("hello" + (char)13 + (char)10 + "world", 50));
                Check.Equal("(b)bold(/b)", BlockRealm.Sim.GameClient.Sanitize("<b>bold</b>", 50));
                Check.Equal("ab", BlockRealm.Sim.GameClient.Sanitize("a" + (char)0 + (char)7 + "b", 50));
                Check.Equal(10, BlockRealm.Sim.GameClient.Sanitize(new string('x', 500), 10).Length);
                Check.Equal("", BlockRealm.Sim.GameClient.Sanitize(null, 10));
            });
            Harness.Run("random bytes never crash the column decoder", () =>
            {
                Rng rng = new Rng(99);
                for (int i = 0; i < 500; i++)
                {
                    byte[] junk = new byte[rng.Range(1, 300)];
                    for (int k = 0; k < junk.Length; k++) junk[k] = (byte)rng.NextInt(256);
                    try { ColumnCodec.Decode(0, 0, junk); }
                    catch (InvalidDataException) { }
                }
            });
        }
    }
}
