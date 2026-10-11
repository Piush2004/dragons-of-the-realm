using System;
using System.Security.Cryptography;
using System.Text;

namespace BlockRealm.Net
{
    /// <summary>
    /// Login proofs. A player's identity key and the server's admin token are never sent: the client proves it knows
    /// them by answering a fresh random challenge, so a recording of one login cannot be replayed on another.
    /// (There is no encryption of the game traffic itself; use a trusted network or a tunnel on the open internet.)
    /// </summary>
    public static class AuthCrypto
    {
        public static string Hex(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        public static string RandomNonce()
        {
            byte[] b = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return Hex(b);
        }

        public static string NewIdentityKey() { return RandomNonce() + RandomNonce(); }

        /// <summary>What the server remembers for a player: a hash of their secret key.</summary>
        public static string Verifier(string identityKey)
        {
            using (SHA256 sha = SHA256.Create())
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes("blockrealm:identity:" + (identityKey ?? ""))));
        }

        public static string AdminVerifier(string token)
        {
            using (SHA256 sha = SHA256.Create())
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes("blockrealm:admin:" + (token ?? ""))));
        }

        /// <summary>HMAC of the challenge keyed by a verifier; only someone who knows the key can produce it.</summary>
        public static string Proof(string verifierHex, string nonceHex)
        {
            using (HMACSHA256 h = new HMACSHA256(Encoding.UTF8.GetBytes(verifierHex ?? "")))
                return Hex(h.ComputeHash(Encoding.UTF8.GetBytes(nonceHex ?? "")));
        }

        public static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            int diff = a.Length ^ b.Length;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
