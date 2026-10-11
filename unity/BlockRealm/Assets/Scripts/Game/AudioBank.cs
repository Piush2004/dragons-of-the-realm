using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockRealm.Game
{
    public enum Sfx
    {
        BreakStone, BreakDirt, BreakWood, BreakGlass, BreakSand, BreakLeaves, Place, Step, Hurt, Eat, Pop,
        Explosion, FireCrackle, Roar, Splash, Click, Bow, Swing, Death, Fireball,
    }

    /// <summary>
    /// Sound effects synthesised in code at start-up (noise bursts, thumps and sweeps), so no audio files are needed.
    /// Playback uses a small pool of spatial audio sources.
    /// </summary>
    public sealed class AudioBank : MonoBehaviour
    {
        private const int Rate = 22050;
        private readonly Dictionary<Sfx, AudioClip[]> _clips = new Dictionary<Sfx, AudioClip[]>();
        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private int _next;
        private System.Random _rng = new System.Random(12345);

        private void Awake()
        {
            for (int i = 0; i < 16; i++)
            {
                GameObject go = new GameObject("Sfx " + i);
                go.transform.SetParent(transform, false);
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.minDistance = 3f;
                s.maxDistance = 48f;
                s.rolloffMode = AudioRolloffMode.Linear;
                _pool.Add(s);
            }
            Build();
        }

        private float Noise() { return (float)(_rng.NextDouble() * 2.0 - 1.0); }

        private AudioClip Make(string name, float seconds, Func<float, int, float> sample)
        {
            int n = Mathf.Max(1, (int)(seconds * Rate));
            float[] data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                data[i] = Mathf.Clamp(sample(t, i), -1f, 1f);
            }
            AudioClip clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip NoiseBurst(string name, float seconds, float lowpass, float decay, float tone = 0f, float toneHz = 0f)
        {
            float lp = 0f;
            return Make(name, seconds, (t, i) =>
            {
                lp += (Noise() - lp) * lowpass;
                float env = Mathf.Exp(-t * decay);
                float v = lp * env;
                if (tone > 0f) v += Mathf.Sin(t * toneHz * Mathf.PI * 2f) * env * tone;
                return v * 0.9f;
            });
        }

        private AudioClip Sweep(string name, float seconds, float f0, float f1, float decay, float noiseMix)
        {
            float phase = 0f;
            return Make(name, seconds, (t, i) =>
            {
                float f = Mathf.Lerp(f0, f1, t / seconds);
                phase += f / Rate;
                float saw = (phase - Mathf.Floor(phase)) * 2f - 1f;
                float env = Mathf.Exp(-t * decay) * Mathf.Min(1f, t * 40f);
                return (saw * (1f - noiseMix) + Noise() * noiseMix) * env * 0.6f;
            });
        }

        private void Set(Sfx kind, params AudioClip[] clips) { _clips[kind] = clips; }

        private void Build()
        {
            Set(Sfx.BreakStone, NoiseBurst("stone1", 0.28f, 0.22f, 14f), NoiseBurst("stone2", 0.26f, 0.3f, 16f));
            Set(Sfx.BreakDirt, NoiseBurst("dirt1", 0.22f, 0.12f, 18f), NoiseBurst("dirt2", 0.24f, 0.1f, 15f));
            Set(Sfx.BreakWood, NoiseBurst("wood1", 0.25f, 0.35f, 20f, 0.5f, 180f), NoiseBurst("wood2", 0.25f, 0.3f, 22f, 0.45f, 140f));
            Set(Sfx.BreakGlass, NoiseBurst("glass1", 0.35f, 0.9f, 10f, 0.25f, 2200f), NoiseBurst("glass2", 0.3f, 0.8f, 12f, 0.2f, 3000f));
            Set(Sfx.BreakSand, NoiseBurst("sand1", 0.3f, 0.5f, 9f), NoiseBurst("sand2", 0.28f, 0.45f, 10f));
            Set(Sfx.BreakLeaves, NoiseBurst("leaf1", 0.2f, 0.7f, 22f), NoiseBurst("leaf2", 0.2f, 0.65f, 24f));
            Set(Sfx.Place, NoiseBurst("place1", 0.12f, 0.2f, 40f, 0.5f, 120f), NoiseBurst("place2", 0.12f, 0.25f, 40f, 0.5f, 150f));
            Set(Sfx.Step, NoiseBurst("step1", 0.09f, 0.18f, 46f), NoiseBurst("step2", 0.09f, 0.22f, 44f), NoiseBurst("step3", 0.09f, 0.15f, 50f));
            Set(Sfx.Hurt, Sweep("hurt", 0.28f, 320f, 160f, 8f, 0.25f));
            Set(Sfx.Eat, NoiseBurst("eat", 0.18f, 0.3f, 14f, 0.3f, 220f));
            Set(Sfx.Pop, Make("pop", 0.1f, (t, i) => Mathf.Sin(t * Mathf.Lerp(900f, 1500f, t / 0.1f) * Mathf.PI * 2f) * Mathf.Exp(-t * 40f) * 0.5f));
            Set(Sfx.Explosion, NoiseBurst("boom", 1.3f, 0.07f, 3.2f, 0.9f, 55f));
            Set(Sfx.FireCrackle, NoiseBurst("fire", 0.5f, 0.6f, 6f));
            Set(Sfx.Roar, Sweep("roar", 1.1f, 180f, 55f, 2.0f, 0.45f));
            Set(Sfx.Splash, NoiseBurst("splash", 0.45f, 0.55f, 7f));
            Set(Sfx.Click, Make("click", 0.05f, (t, i) => Mathf.Sin(t * 1800f * Mathf.PI * 2f) * Mathf.Exp(-t * 90f) * 0.4f));
            Set(Sfx.Bow, Sweep("bow", 0.25f, 600f, 150f, 14f, 0.1f));
            Set(Sfx.Swing, NoiseBurst("swing", 0.12f, 0.5f, 26f));
            Set(Sfx.Death, Sweep("death", 0.55f, 260f, 70f, 5f, 0.3f));
            Set(Sfx.Fireball, NoiseBurst("fireball", 0.6f, 0.4f, 5f, 0.4f, 110f));
        }

        public void Play(Sfx kind, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            AudioClip[] list;
            if (!_clips.TryGetValue(kind, out list) || list.Length == 0) return;
            AudioSource s = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            s.transform.position = position;
            s.clip = list[UnityEngine.Random.Range(0, list.Length)];
            s.volume = Mathf.Clamp01(volume) * GameSettings.Volume;
            s.pitch = pitch * UnityEngine.Random.Range(0.92f, 1.08f);
            s.Play();
        }

        public Sfx BreakSoundFor(byte blockId)
        {
            switch (blockId)
            {
                case B.Glass: case B.Ice: return Sfx.BreakGlass;
                case B.Sand: case B.Gravel: case B.Snow: case B.Clay: return Sfx.BreakSand;
                case B.Dirt: case B.Grass: case B.SnowGrass: return Sfx.BreakDirt;
                case B.Planks: case B.LogOak: case B.LogSpruce: case B.LogBirch: case B.CraftingTable: case B.Chest: return Sfx.BreakWood;
                case B.LeavesOak: case B.LeavesSpruce: case B.LeavesBirch: case B.TallGrass: case B.FlowerRed: case B.FlowerYellow: case B.Wool: return Sfx.BreakLeaves;
            }
            return Sfx.BreakStone;
        }
    }
}
