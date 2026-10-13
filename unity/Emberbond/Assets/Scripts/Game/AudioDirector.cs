using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emberbond
{
    public enum Sfx { Swing, Clang, Hit, Fire, Explosion, Flap, Step, Crash, Ui, Death, Fanfare }

    /// <summary>
    /// All sound is synthesised at start-up (no audio files): metallic ringing, thuds, filtered noise for fire and wind,
    /// and a slow modal pad for music. Cheap, deterministic, and it keeps the project free of third-party audio.
    /// </summary>
    public static class AudioDirector
    {
        private const int Rate = 44100;
        private static readonly Dictionary<Sfx, AudioClip> Clips = new Dictionary<Sfx, AudioClip>();
        private static readonly List<AudioSource> Pool = new List<AudioSource>();
        private static AudioSource _music, _wind, _ui;
        private static GameObject _root;
        private static int _next;
        private static bool _ready;

        // ------------------------------------------------------------ synthesis helpers

        private static AudioClip Make(string name, float seconds, Func<int, float, float> f)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(seconds * Rate));
            float[] data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i, i / (float)Rate), -1f, 1f);
            AudioClip c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        private sealed class Noise
        {
            private readonly System.Random _r;
            private float _lp;
            public Noise(int seed) { _r = new System.Random(seed); }
            public float White() { return (float)(_r.NextDouble() * 2.0 - 1.0); }
            public float Low(float k) { _lp += k * (White() - _lp); return _lp; }
        }

        private static float Env(float t, float attack, float decay)
        {
            if (t < attack) return t / Mathf.Max(attack, 1e-4f);
            return Mathf.Exp(-(t - attack) / Mathf.Max(decay, 1e-4f));
        }

        public static void Init()
        {
            if (_ready) return;
            _ready = true;
            _root = new GameObject("Audio");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.AddComponent<AudioListener>().enabled = false;      // the camera's listener is used

            Noise n = new Noise(1);
            Clips[Sfx.Swing] = Make("swing", 0.28f, (i, t) =>
            {
                float band = Mathf.Sin(t * 22f) * 0.5f + 0.5f;
                return n.Low(0.25f + 0.5f * band) * 1.6f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.28f)) * 0.7f;
            });
            Clips[Sfx.Clang] = Make("clang", 0.9f, (i, t) =>
            {
                float s = 0f;
                float[] f = { 1180f, 1860f, 2740f, 3910f, 5230f };
                float[] a = { 1f, 0.7f, 0.5f, 0.3f, 0.2f };
                for (int k = 0; k < f.Length; k++) s += Mathf.Sin(2f * Mathf.PI * f[k] * t) * a[k] * Mathf.Exp(-t * (5f + k * 2.2f));
                return s * 0.35f + n.White() * Mathf.Exp(-t * 60f) * 0.5f;
            });
            Clips[Sfx.Hit] = Make("hit", 0.22f, (i, t) => Mathf.Sin(2f * Mathf.PI * (95f - 40f * t) * t) * Mathf.Exp(-t * 18f) * 0.9f + n.Low(0.3f) * Mathf.Exp(-t * 30f) * 0.6f);
            Clips[Sfx.Fire] = Make("fire", 0.8f, (i, t) => n.Low(0.12f + 0.2f * Mathf.Exp(-t * 3f)) * 2.2f * Env(t, 0.05f, 0.35f));
            Clips[Sfx.Explosion] = Make("explosion", 1.6f, (i, t) =>
                n.Low(0.07f) * 2.6f * Mathf.Exp(-t * 2.4f) + Mathf.Sin(2f * Mathf.PI * (55f - 20f * t) * t) * Mathf.Exp(-t * 3f) * 0.9f + n.White() * Mathf.Exp(-t * 28f) * 0.5f);
            Clips[Sfx.Flap] = Make("flap", 0.55f, (i, t) => (n.Low(0.05f) * 3.0f + Mathf.Sin(2f * Mathf.PI * 48f * t) * 0.5f) * Env(t, 0.06f, 0.14f) * 0.8f);
            Clips[Sfx.Step] = Make("step", 0.12f, (i, t) => n.Low(0.35f) * 1.4f * Mathf.Exp(-t * 38f));
            Clips[Sfx.Crash] = Make("crash", 1.2f, (i, t) =>
                n.Low(0.1f) * 2.4f * Mathf.Exp(-t * 3.5f) + Mathf.Sin(2f * Mathf.PI * (70f - 30f * t) * t) * Mathf.Exp(-t * 5f) + n.White() * Mathf.Exp(-t * 35f) * 0.6f);
            Clips[Sfx.Ui] = Make("ui", 0.08f, (i, t) => Mathf.Sin(2f * Mathf.PI * 880f * t) * Mathf.Exp(-t * 55f) * 0.5f);
            Clips[Sfx.Death] = Make("death", 1.6f, (i, t) => Mathf.Sin(2f * Mathf.PI * (180f - 90f * t) * t) * Mathf.Exp(-t * 2.2f) * 0.6f + n.Low(0.05f) * Mathf.Exp(-t * 3f));
            Clips[Sfx.Fanfare] = Make("fanfare", 2.4f, (i, t) =>
            {
                float[] notes = { 220f, 277.2f, 329.6f, 440f };
                float s = 0f;
                for (int k = 0; k < notes.Length; k++)
                {
                    float t0 = k * 0.28f;
                    if (t < t0) continue;
                    float e = Mathf.Exp(-(t - t0) * 1.6f);
                    s += (Mathf.Sin(2f * Mathf.PI * notes[k] * t) + 0.4f * Mathf.Sin(4f * Mathf.PI * notes[k] * t)) * e * 0.18f;
                }
                return s;
            });

            for (int i = 0; i < 12; i++)
            {
                GameObject g = new GameObject("sfx" + i);
                g.transform.SetParent(_root.transform, false);
                AudioSource a = g.AddComponent<AudioSource>();
                a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Linear; a.minDistance = 6f; a.maxDistance = 160f; a.playOnAwake = false;
                Pool.Add(a);
            }
            GameObject ug = new GameObject("ui");
            ug.transform.SetParent(_root.transform, false);
            _ui = ug.AddComponent<AudioSource>();
            _ui.spatialBlend = 0f; _ui.playOnAwake = false;

            // Ambience: a looping band of wind.
            Noise wn = new Noise(7);
            AudioClip wind = Make("wind", 6f, (i, t) =>
            {
                float gust = 0.55f + 0.45f * Mathf.Sin(t * 2f * Mathf.PI / 6f * 2f) * Mathf.Sin(t * 2f * Mathf.PI / 6f * 3f + 1f);
                return wn.Low(0.03f + 0.02f * gust) * 2.2f * gust;
            });
            GameObject wg = new GameObject("wind");
            wg.transform.SetParent(_root.transform, false);
            _wind = wg.AddComponent<AudioSource>();
            _wind.clip = wind; _wind.loop = true; _wind.spatialBlend = 0f; _wind.volume = 0.25f;

            // Music: a slow A-minor pad with a sparse high line, 32 seconds, looped.
            AudioClip music = Make("music", 32f, (i, t) =>
            {
                float[][] chords =
                {
                    new[] { 110.0f, 164.8f, 220.0f, 261.6f },      // Am
                    new[] { 87.3f, 130.8f, 174.6f, 261.6f },       // F
                    new[] { 98.0f, 146.8f, 196.0f, 246.9f },       // G
                    new[] { 82.4f, 123.5f, 164.8f, 246.9f },       // Em
                };
                int bar = Mathf.FloorToInt(t / 8f) % chords.Length;
                float bt = t % 8f;
                float swell = Mathf.Sin(Mathf.PI * Mathf.Clamp01(bt / 8f));
                float s = 0f;
                for (int k = 0; k < chords[bar].Length; k++)
                {
                    float f = chords[bar][k];
                    s += (Mathf.Sin(2f * Mathf.PI * f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * f * 2f * t + 0.7f)) * 0.07f;
                    s += Mathf.Sin(2f * Mathf.PI * f * 1.004f * t) * 0.04f;               // slight detune for width
                }
                float lineT = t % 2f;
                float[] line = { 440f, 523.3f, 659.3f, 587.3f };
                float lf = line[Mathf.FloorToInt(t / 2f) % line.Length];
                s += Mathf.Sin(2f * Mathf.PI * lf * t) * Mathf.Exp(-lineT * 1.8f) * 0.05f * (bar % 2 == 0 ? 1f : 0.6f);
                return s * swell * 0.9f;
            });
            GameObject mg = new GameObject("music");
            mg.transform.SetParent(_root.transform, false);
            _music = mg.AddComponent<AudioSource>();
            _music.clip = music; _music.loop = true; _music.spatialBlend = 0f;
            ApplyVolumes();
        }

        public static void ApplyVolumes()
        {
            if (!_ready) return;
            GameSettings.ApplyAudio();
            _music.volume = GameSettings.MusicVolume * 0.6f;
        }

        public static void StartAmbience()
        {
            if (!_ready) return;
            if (!_wind.isPlaying) _wind.Play();
            if (!_music.isPlaying) _music.Play();
        }

        public static void SetWind(float amount) { if (_ready && _wind != null) _wind.volume = Mathf.Lerp(0.12f, 0.6f, Mathf.Clamp01(amount)); }

        public static void Play(Sfx id, Vector3 at, float volume = 1f, float pitch = 1f)
        {
            if (!_ready) return;
            AudioClip c;
            if (!Clips.TryGetValue(id, out c)) return;
            if (id == Sfx.Ui || id == Sfx.Death || id == Sfx.Fanfare) { _ui.pitch = pitch; _ui.PlayOneShot(c, volume); return; }
            AudioSource a = Pool[_next];
            _next = (_next + 1) % Pool.Count;
            a.transform.position = at;
            a.pitch = pitch * UnityEngine.Random.Range(0.94f, 1.06f);
            a.volume = volume;
            a.clip = c;
            a.Play();
        }

        public static void Ui() { Play(Sfx.Ui, Vector3.zero, 0.7f); }
    }
}
