using System;
using UnityEngine;
using BlockRealm.Net;

namespace BlockRealm.Game
{
    /// <summary>Player preferences, stored in PlayerPrefs. Every access is wrapped so a missing store never breaks the game.</summary>
    public static class GameSettings
    {
        private static int GetInt(string key, int fallback) { try { return PlayerPrefs.GetInt(key, fallback); } catch (Exception) { return fallback; } }
        private static float GetFloat(string key, float fallback) { try { return PlayerPrefs.GetFloat(key, fallback); } catch (Exception) { return fallback; } }
        private static string GetString(string key, string fallback) { try { return PlayerPrefs.GetString(key, fallback); } catch (Exception) { return fallback; } }
        private static void Set(string key, int v) { try { PlayerPrefs.SetInt(key, v); } catch (Exception) { } }
        private static void Set(string key, float v) { try { PlayerPrefs.SetFloat(key, v); } catch (Exception) { } }
        private static void Set(string key, string v) { try { PlayerPrefs.SetString(key, v); } catch (Exception) { } }

        /// <summary>Chunks of view distance (16 blocks each).</summary>
        public static int ViewDistance
        {
            get { return Mathf.Clamp(GetInt("br.view", 10), 3, 20); }
            set { Set("br.view", Mathf.Clamp(value, 3, 20)); }
        }

        public static float Fov
        {
            get { return Mathf.Clamp(GetFloat("br.fov", 75f), 50f, 110f); }
            set { Set("br.fov", Mathf.Clamp(value, 50f, 110f)); }
        }

        public static float MouseSensitivity
        {
            get { return Mathf.Clamp(GetFloat("br.mouse", 1f), 0.1f, 4f); }
            set { Set("br.mouse", Mathf.Clamp(value, 0.1f, 4f)); }
        }

        public static float StickSensitivity
        {
            get { return Mathf.Clamp(GetFloat("br.stick", 1f), 0.2f, 3f); }
            set { Set("br.stick", Mathf.Clamp(value, 0.2f, 3f)); }
        }

        public static float Volume
        {
            get { return Mathf.Clamp01(GetFloat("br.volume", 0.8f)); }
            set { Set("br.volume", Mathf.Clamp01(value)); }
        }

        public static bool Shadows
        {
            get { return GetInt("br.shadows", 1) != 0; }
            set { Set("br.shadows", value ? 1 : 0); }
        }

        public static bool ShowFps
        {
            get { return GetInt("br.fps", 0) != 0; }
            set { Set("br.fps", value ? 1 : 0); }
        }

        public static bool InvertY
        {
            get { return GetInt("br.invert", 0) != 0; }
            set { Set("br.invert", value ? 1 : 0); }
        }

        public static string PlayerName
        {
            get { return GetString("br.name", "Wayfarer" + UnityEngine.Random.Range(100, 999)); }
            set { Set("br.name", value); }
        }

        public static string LastHost
        {
            get { return GetString("br.host", "127.0.0.1:25565"); }
            set { Set("br.host", value); }
        }

        /// <summary>
        /// Admin tokens are remembered per server address and only ever used for that address (and even then only a proof
        /// derived from the token is sent, never the token). They live in PlayerPrefs, so treat this machine's user
        /// profile as the trust boundary.
        /// </summary>
        public static string AdminTokenFor(string address)
        {
            return GetString("br.admin." + AuthCrypto.NormaliseAddress(address), "");
        }

        public static void SetAdminTokenFor(string address, string token)
        {
            string key = "br.admin." + AuthCrypto.NormaliseAddress(address);
            try
            {
                if (string.IsNullOrEmpty(token)) PlayerPrefs.DeleteKey(key); else PlayerPrefs.SetString(key, token);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// A random secret generated on first run. It never leaves this machine; it is used to prove ownership of the
        /// player name on each server (see AuthCrypto).
        /// </summary>
        public static string IdentityKey
        {
            get
            {
                string key = GetString("br.identity", "");
                if (key.Length < 32)
                {
                    key = AuthCrypto.NewIdentityKey();
                    Set("br.identity", key);
                    try { PlayerPrefs.Save(); } catch (Exception) { }
                }
                return key;
            }
        }

        public static void Save() { try { PlayerPrefs.Save(); } catch (Exception) { } }
    }
}
