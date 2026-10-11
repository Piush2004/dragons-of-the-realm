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
                BlockRealm.Sim.SourceLimiter lim = new BlockRealm.Sim.SourceLimiter();
                for (int i = 0; i < 4096; i++) lim.Add("src" + i, 100);          // fill the table with recent sources
                Check.True(lim.Count("fresh-source", 101, 1200) >= 5, "an untrackable source counts as over the limit");
                Check.Equal(1, lim.Count("src5", 101, 1200), "tracked sources count normally");
                // After the window passes the table empties and new sources are tracked again.
                Check.Equal(0, lim.Count("fresh-source", 100 + 1200 + 2400 + 10, 1200), "recovers once old entries expire");
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
