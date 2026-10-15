using UnityEngine;

namespace Emberbond
{
    /// <summary>Player-facing options, stored in PlayerPrefs.</summary>
    public static class GameSettings
    {
        public static float MouseSens = 1f;          // 0.3 .. 3
        public static bool InvertY;
        public static float Fov = 60f;               // 50 .. 90
        public static float MasterVolume = 0.8f;
        public static float MusicVolume = 0.5f;
        public static int Quality;                   // 0 auto, 1 high, 2 low
        public static int RenderScale;               // 0 auto, else percent 50..100
        public static bool ShowFps;
        public static float UiScale = 1f;            // 0.8 .. 1.4
        public static bool ShowHints = true;         // onboarding tips

        private const string Prefix = "emberbond.";

        public static void Load()
        {
            MouseSens = Clamp(PlayerPrefs.GetFloat(Prefix + "sens", 1f), 0.3f, 3f);
            InvertY = PlayerPrefs.GetInt(Prefix + "invy", 0) == 1;
            Fov = Clamp(PlayerPrefs.GetFloat(Prefix + "fov", 60f), 50f, 90f);
            MasterVolume = Clamp(PlayerPrefs.GetFloat(Prefix + "vol", 0.8f), 0f, 1f);
            MusicVolume = Clamp(PlayerPrefs.GetFloat(Prefix + "music", 0.5f), 0f, 1f);
            Quality = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "quality", 0), 0, 2);
            RenderScale = PlayerPrefs.GetInt(Prefix + "scale", 0);
            if (RenderScale != 0) RenderScale = Mathf.Clamp(RenderScale, 50, 100);
            ShowFps = PlayerPrefs.GetInt(Prefix + "fps", 0) == 1;
            UiScale = Clamp(PlayerPrefs.GetFloat(Prefix + "ui", 1f), 0.8f, 1.4f);
            ShowHints = PlayerPrefs.GetInt(Prefix + "hints", 1) == 1;
            Bindings.Load();
            ApplyAudio();
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat(Prefix + "sens", MouseSens);
            PlayerPrefs.SetInt(Prefix + "invy", InvertY ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "fov", Fov);
            PlayerPrefs.SetFloat(Prefix + "vol", MasterVolume);
            PlayerPrefs.SetFloat(Prefix + "music", MusicVolume);
            PlayerPrefs.SetInt(Prefix + "quality", Quality);
            PlayerPrefs.SetInt(Prefix + "scale", RenderScale);
            PlayerPrefs.SetInt(Prefix + "fps", ShowFps ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "ui", UiScale);
            PlayerPrefs.SetInt(Prefix + "hints", ShowHints ? 1 : 0);
            Bindings.Save();
            PlayerPrefs.Save();
        }

        public static void ApplyAudio() { AudioListener.volume = MasterVolume; }

        private static float Clamp(float v, float lo, float hi)
        {
            if (float.IsNaN(v)) return lo;
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
