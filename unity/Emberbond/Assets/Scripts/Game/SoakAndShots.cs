using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

namespace Emberbond
{
    /// <summary>
    /// A long random-play test. It drives the game with random scripted input for several minutes (walking, sprinting,
    /// fighting, mounting, flying, firing, teleporting) while checking invariants every frame: positions stay finite and
    /// inside the world, health stays sane, nothing throws, memory does not grow without bound, and frame time holds.
    ///   Emberbond.exe -soak [seconds]
    /// </summary>
    public sealed class SoakTest : MonoBehaviour
    {
        private Director _d;
        private float _duration = 300f;
        private int _errors, _violations;
        private float _t, _next;
        private long _memStart, _memPeak;
        private float _worst; private int _frames; private double _sum;
        private float _nextMem = 30f;

        private void Start()
        {
            _d = GetComponent<Director>();
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
            {
                float v;
                if (a[i] == "-soak" && float.TryParse(a[i + 1], out v) && v >= 20f) _duration = v;
            }
            SaveSystem.PathOverride = Path.Combine(Application.temporaryCachePath, "soak_save.json");
            InputState.Scripted = true;
            Application.logMessageReceived += (m, s, t) =>
            {
                if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) { _errors++; Debug.Log("[SOAK] captured " + t + ": " + m); }
            };
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            yield return new WaitForSeconds(3f);
            if (_d.InDialogue) _d.ChooseOption(UnityEngine.Random.Range(0, 3));
            _memStart = Profiler.GetTotalAllocatedMemoryLong() + GC.GetTotalMemory(false);
            _memPeak = _memStart;
            System.Random rnd = new System.Random(4242);
            DragonFlight dr = _d.Dragon;
            ThirdPersonPlayer pl = _d.Player;
            WorldLayout lay = WorldLayout.Load();

            while (_t < _duration)
            {
                float dt = Time.unscaledDeltaTime;
                _t += dt;
                if (_t > 5f && dt < 0.5f) { _frames++; _sum += dt; _worst = Mathf.Max(_worst, dt); }

                // Choose a new behaviour every few seconds.
                if (_t > _next)
                {
                    _next = _t + 1.5f + (float)rnd.NextDouble() * 3.5f;
                    InputState.ScriptedMove = Vector2.zero; InputState.ScriptedLook = Vector2.zero;
                    InputState.ScriptedBoost = false; InputState.ScriptedFire = false;
                    if (_d.Stage == 5) { pl.Health.Health = Mathf.Max(pl.Health.Health, 40f); }
                    int act = rnd.Next(10);
                    switch (act)
                    {
                        case 0: case 1: InputState.ScriptedMove = new Vector2((float)rnd.NextDouble() - 0.5f, 1f); InputState.ScriptedLook = new Vector2((float)(rnd.NextDouble() - 0.5) * 6f, 0f); break;
                        case 2: InputState.ScriptedMove = new Vector2(0f, 1f); InputState.ScriptedBoost = true; break;
                        case 3: InputState.ScriptedAttack = true; break;
                        case 4: InputState.ScriptedBlock = true; break;
                        case 5:
                            if (!dr.Mounted && !pl.Health.Dead)
                            {
                                pl.Teleport(dr.transform.position + new Vector3(6f, 0.5f, 0f));
                                dr.Mount();
                            }
                            break;
                        case 6:
                            if (dr.Mounted) { InputState.ScriptedLook = new Vector2((float)(rnd.NextDouble() - 0.5) * 2.5f, (float)(rnd.NextDouble() - 0.5) * 1.2f); InputState.ScriptedBoost = rnd.Next(2) == 0; }
                            break;
                        case 7: if (dr.Mounted) InputState.ScriptedFire = true; break;
                        case 8:
                            if (dr.Mounted && rnd.Next(3) == 0)
                            {
                                Vector3 q = dr.transform.position;
                                if (q.y - (Terrain.activeTerrain != null ? Terrain.activeTerrain.SampleHeight(q) : 0f) < 30f) dr.Dismount();
                            }
                            break;
                        case 9:
                            if (!dr.Mounted)
                            {
                                float ang = (float)rnd.NextDouble() * 360f * Mathf.Deg2Rad;
                                float r = 30f + (float)rnd.NextDouble() * 140f;
                                Vector3 q = lay.City + new Vector3(Mathf.Sin(ang) * r, 0f, Mathf.Cos(ang) * r);
                                Terrain t = Terrain.activeTerrain;
                                float g = t != null ? t.SampleHeight(q) + t.transform.position.y : q.y;
                                pl.Teleport(new Vector3(q.x, g + 0.6f, q.z));
                            }
                            break;
                    }
                }

                // Invariants.
                Vector3 pp = pl.transform.position;
                if (float.IsNaN(pp.x + pp.y + pp.z) || Mathf.Abs(pp.x) > 2000f || Mathf.Abs(pp.z) > 2000f || pp.y > 1600f) Violation("player out of bounds " + pp);
                Vector3 dp = dr.transform.position;
                if (float.IsNaN(dp.x + dp.y + dp.z) || Mathf.Abs(dp.x) > 1700f || Mathf.Abs(dp.z) > 1700f || dp.y > 1200f) Violation("dragon out of bounds " + dp);
                if (float.IsNaN(pl.Health.Health) || pl.Health.Health > pl.Health.MaxHealth + 0.01f) Violation("player health " + pl.Health.Health);
                if (pl.Stamina < -0.01f || pl.Stamina > pl.MaxStamina + 0.01f) Violation("stamina " + pl.Stamina);

                if (_t > _nextMem)
                {
                    _nextMem += 30f;
                    long mem = Profiler.GetTotalAllocatedMemoryLong() + GC.GetTotalMemory(false);
                    _memPeak = Math.Max(_memPeak, mem);
                    PerformanceScaler ps = FindFirstObjectByType<PerformanceScaler>();
                    Debug.Log("[SOAK] t=" + _t.ToString("0") + " mem=" + (mem / 1048576) + " MB deaths=" + _d.Deaths + " respawns=" + _d.Respawns + " stage=" + _d.Stage +
                              " mounted=" + dr.Mounted + " crashes=" + dr.Crashes + " scale=" + (ps != null ? ps.Scale.ToString("0.00") : "-"));
                }
                yield return null;
            }
            long end = Profiler.GetTotalAllocatedMemoryLong() + GC.GetTotalMemory(false);
            float growth = (end - _memStart) / 1048576f;
            float avg = _frames > 0 ? (float)(_sum / _frames * 1000.0) : 0f;
            bool ok = _errors == 0 && _violations == 0 && growth < 150f;
            Debug.Log("[SOAK] RESULT " + (ok ? "PASS" : "FAIL") + " seconds=" + _t.ToString("0") + " frames=" + _frames + " avg=" + avg.ToString("0.0") + " ms worst=" + (_worst * 1000f).ToString("0") +
                      " ms errors=" + _errors + " violations=" + _violations + " memGrowth=" + growth.ToString("0") + " MB peak=" + (_memPeak / 1048576) + " MB deaths=" + _d.Deaths);
            Application.Quit(ok ? 0 : 1);
        }

        private void Violation(string what)
        {
            _violations++;
            if (_violations <= 10) Debug.Log("[SOAK] VIOLATION " + what + " at t=" + _t.ToString("0.0"));
        }
    }

    /// <summary>Takes screenshots of the title menu, settings, the council, a gameplay frame and the pause menu.</summary>
    public sealed class MenuShots : MonoBehaviour
    {
        private Director _d;
        private Menu _m;

        private void Start()
        {
            _d = GetComponent<Director>();
            _m = GetComponent<Menu>();
            InputState.Scripted = true;
            StartCoroutine(Run());
        }

        private static void Shot(string name)
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "menu");
            Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            Debug.Log("[SHOT] " + name + " " + Screen.width + "x" + Screen.height);
        }

        private IEnumerator Run()
        {
            // Captures are written at the end of the frame, so each shot is followed by a wait before the state changes.
            // Realtime waits are used because the pause menu sets timeScale to 0.
            yield return new WaitForSecondsRealtime(5f);
            Shot("1_title");
            yield return new WaitForSecondsRealtime(1f);
            _m.DebugOpenSettings();
            yield return new WaitForSecondsRealtime(1.5f);
            Shot("2_settings");
            yield return new WaitForSecondsRealtime(1f);
            _m.DebugCloseAll();
            _d.BeginNewGame();
            yield return new WaitForSecondsRealtime(4f);
            Shot("3_council");
            yield return new WaitForSecondsRealtime(1f);
            _d.ChooseOption(1);
            yield return new WaitForSecondsRealtime(7f);
            Shot("4_gameplay");
            yield return new WaitForSecondsRealtime(1f);
            _m.DebugPause();
            yield return new WaitForSecondsRealtime(1.5f);
            Shot("5_pause");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(0);
        }
    }
}
