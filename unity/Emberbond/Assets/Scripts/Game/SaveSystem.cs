using System;
using System.IO;
using UnityEngine;

namespace Emberbond
{
    [Serializable]
    public sealed class SaveData
    {
        public int Version = 1;
        public int Stage;
        public int Honour, Fear, Cunning;
        public Vector3 PlayerPos, DragonPos;
        public float Health, DragonHealth;
        public int Deaths;
        public float PlaySeconds;
        public string SavedAt;
    }

    /// <summary>One save slot as JSON. Loading validates everything, so a damaged or edited file cannot break the game.</summary>
    public static class SaveSystem
    {
        public static string PathOverride;

        private static string FilePath
        {
            get
            {
                if (!string.IsNullOrEmpty(PathOverride)) return PathOverride;
                return Path.Combine(Application.persistentDataPath, "emberbond_save.json");
            }
        }

        public static bool Exists { get { try { return File.Exists(FilePath); } catch (Exception) { return false; } } }

        public static bool Save(SaveData d)
        {
            try
            {
                d.SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(d, true));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Emberbond] save failed: " + e.Message);
                return false;
            }
        }

        public static bool TryLoad(out SaveData d)
        {
            d = null;
            try
            {
                if (!File.Exists(FilePath)) return false;
                string text = File.ReadAllText(FilePath);
                if (text.Length > 100000) return false;
                SaveData s = JsonUtility.FromJson<SaveData>(text);
                if (s == null || s.Version != 1) return false;
                if (s.Stage != 5 && s.Stage != 1 && s.Stage != 2 && s.Stage != 3 && s.Stage != 4) return false;
                if (!Finite(s.PlayerPos) || !Finite(s.DragonPos)) return false;
                if (Mathf.Abs(s.PlayerPos.x) > 3000f || Mathf.Abs(s.PlayerPos.z) > 3000f || s.PlayerPos.y < 0f || s.PlayerPos.y > 3000f) return false;
                s.Honour = Mathf.Clamp(s.Honour, -99, 99);
                s.Fear = Mathf.Clamp(s.Fear, -99, 99);
                s.Cunning = Mathf.Clamp(s.Cunning, -99, 99);
                s.Health = float.IsNaN(s.Health) ? 120f : Mathf.Clamp(s.Health, 1f, 120f);
                s.DragonHealth = float.IsNaN(s.DragonHealth) ? 400f : Mathf.Clamp(s.DragonHealth, 1f, 400f);
                d = s;
                return true;
            }
            catch (Exception) { return false; }
        }

        public static void Delete()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch (Exception) { }
        }

        private static bool Finite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }
    }
}
