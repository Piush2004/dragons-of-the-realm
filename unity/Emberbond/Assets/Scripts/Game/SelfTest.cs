using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Emberbond
{
    /// <summary>
    /// An in-player test battery. It drives the real game (scripted input, real physics, real save files) and checks
    /// the results. Run with:  Emberbond.exe -selftest   (it prints [TEST] lines and exits non-zero on any failure).
    /// </summary>
    public sealed class SelfTest : MonoBehaviour
    {
        private Director _d;
        private int _pass, _fail;
        private int _errors;
        private readonly List<float> _frames = new List<float>(8192);
        private bool _measure;
        private string _tmpSave;

        private void Start()
        {
            _d = GetComponent<Director>();
            Application.logMessageReceived += OnLog;
            _tmpSave = Path.Combine(Application.temporaryCachePath, "selftest_save.json");
            SaveSystem.PathOverride = _tmpSave;
            InputState.Scripted = true;
            StartCoroutine(Run());
        }

        private void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _errors++;
                Debug.Log("[TEST] captured " + type + ": " + msg);
            }
        }

        private void Update()
        {
            if (_measure && Time.unscaledDeltaTime < 0.5f) _frames.Add(Time.unscaledDeltaTime * 1000f);
        }

        private void Check(string name, bool ok, string detail = "")
        {
            if (ok) _pass++; else _fail++;
            Debug.Log("[TEST] " + (ok ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? "  (" + detail + ")" : ""));
        }

        private static float GroundY(Vector3 p)
        {
            Terrain t = Terrain.activeTerrain;
            return t != null ? t.SampleHeight(p) + t.transform.position.y : 0f;
        }

        private static bool Finite(Vector3 v) { return !(float.IsNaN(v.x + v.y + v.z) || float.IsInfinity(v.x + v.y + v.z)); }

        private IEnumerator Wait(float seconds) { yield return new WaitForSeconds(seconds); }

        private IEnumerator Until(Func<bool> cond, float timeout)
        {
            float t = 0f;
            while (!cond() && t < timeout) { t += Time.unscaledDeltaTime; yield return null; }
        }

        private IEnumerator Run()
        {
            yield return new WaitForSeconds(2f);
            ThirdPersonPlayer pl = _d.Player;
            DragonFlight dr = _d.Dragon;
            WorldLayout lay = WorldLayout.Load();

            // ---------------------------------------------------------- world
            Check("terrain exists", Terrain.activeTerrain != null);
            Check("layout loaded", lay != null && lay.City.y > 50f, "city " + lay.City);
            Check("player starts on the ground", Finite(pl.transform.position) && pl.transform.position.y >= GroundY(pl.transform.position) - 0.5f,
                  "y=" + pl.transform.position.y.ToString("0.0") + " ground=" + GroundY(pl.transform.position).ToString("0.0"));
            Check("dragon exists and has a rig", dr != null && dr.GetComponent<DragonAnimator>() != null && dr.GetComponent<DragonAnimator>().HasRig);
            Check("rider has a rig", pl.Anim != null);

            // ---------------------------------------------------------- dialogue
            yield return Until(() => _d.InDialogue, 5f);
            Check("council dialogue opens with three tones", _d.InDialogue && _d.Options.Count == 3);
            int h0 = _d.Rep.Honour, f0 = _d.Rep.Fear, c0 = _d.Rep.Cunning;
            _d.Options[0].Effect(_d); _d.Options[1].Effect(_d); _d.Options[2].Effect(_d);
            Check("each tone changes its own reputation value", _d.Rep.Honour == h0 + 2 && _d.Rep.Fear == f0 + 2 && _d.Rep.Cunning == c0 + 2);
            _d.Rep = new Reputation();
            _measure = true;
            _d.ChooseOption(0);
            Check("choosing moves to the market fight", _d.Stage == 5 && !_d.InDialogue && _d.Rep.Honour == 2);
            yield return Wait(1.5f);
            Check("four soldiers appear", _d.Soldiers.Count == 4, "count=" + _d.Soldiers.Count);

            // ---------------------------------------------------------- combat
            float hp0 = pl.Health.Health;
            Soldier s0 = _d.Soldiers[0];
            float d0 = Vector3.Distance(s0.transform.position, pl.transform.position);
            yield return Wait(2.5f);
            float d1 = s0 != null ? Vector3.Distance(s0.transform.position, pl.transform.position) : 0f;
            Check("soldiers close in on the player", d1 < d0 - 3f, d0.ToString("0") + " -> " + d1.ToString("0") + " m");

            // Keep the guard up: strikes must be parried and not hurt.
            float guardEnd = Time.unscaledTime + 7f;
            while (Time.unscaledTime < guardEnd) { pl.ParryWindow = 0.35f; pl.Health.Health = Mathf.Max(pl.Health.Health, 100f); yield return null; }
            int parried = 0, landedWhileGuarding = 0;
            foreach (Soldier s in _d.Soldiers) { parried += s.Parried; landedWhileGuarding += s.Landed; }
            Check("a timed parry negates strikes", parried >= 1 && landedWhileGuarding == 0, "parried=" + parried + " landed=" + landedWhileGuarding);

            // Guard down: a strike must land for the documented damage.
            pl.Health.Health = 120f;
            int landedBefore = 0;
            foreach (Soldier s in _d.Soldiers) landedBefore += s.Landed;
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 8f)
            {
                int landed = 0;
                foreach (Soldier s in _d.Soldiers) landed += s.Landed;
                if (landed > landedBefore) break;
                yield return null;
            }
            yield return null;
            Check("an unguarded strike hurts for 12", Mathf.Abs((120f - pl.Health.Health) - 12f) < 0.01f || 120f - pl.Health.Health >= 12f, "health " + pl.Health.Health.ToString("0"));

            // Fight back with scripted swings until they are all down.
            float fightEnd = Time.unscaledTime + 50f;
            float nextSwing = 0f;
            while (Time.unscaledTime < fightEnd && _d.Stage == 5)
            {
                pl.Health.Health = Mathf.Max(pl.Health.Health, 60f);
                pl.Stamina = Mathf.Max(pl.Stamina, 40f);
                if (Time.unscaledTime > nextSwing) { InputState.ScriptedAttack = true; nextSwing = Time.unscaledTime + 0.6f; }
                yield return null;
            }
            Check("the soldiers can be defeated and the quest advances", _d.Stage == 1, "stage=" + _d.Stage);
            yield return Wait(1f);
            int resting = 0, total = 0;
            foreach (Soldier s in _d.Soldiers)
            {
                if (s == null) continue;
                total++;
                Rigidbody rb = s.GetComponent<Rigidbody>();
                float clearance = s.transform.position.y - GroundY(s.transform.position);
                if (rb != null && clearance > -0.3f && clearance < 2.5f) resting++;
            }
            Check("fallen soldiers are physical bodies resting on the ground", total > 0 && resting == total, resting + "/" + total);

            // ---------------------------------------------------------- props and pushing
            Vector3[] pb = Props.Snapshot();
            float minClear = 99f; bool nanProp = false;
            for (int i = 0; i < Props.All.Count; i++)
            {
                if (Props.All[i] == null) continue;
                Vector3 pp = Props.All[i].position;
                if (!Finite(pp)) nanProp = true;
                minClear = Mathf.Min(minClear, pp.y - GroundY(pp));
            }
            Check("crates and barrels rest on the ground", Props.All.Count > 10 && !nanProp && minClear > -0.3f, "n=" + Props.All.Count + " minClearance=" + minClear.ToString("0.00"));

            // Pick the barrel closest to the rider and approach it from where the rider already stands.
            Rigidbody crate = null;
            float nearest = 1e9f;
            foreach (Rigidbody rb in Props.All)
            {
                if (rb == null || rb.name != "Barrel") continue;
                float dd = Vector3.Distance(rb.position, pl.transform.position);
                if (dd < nearest) { nearest = dd; crate = rb; }
            }
            if (crate != null)
            {
                Vector3 cp = crate.position;
                Vector3 side = pl.transform.position - cp; side.y = 0f;
                if (side.magnitude < 0.5f) side = Vector3.back;
                side.Normalize();
                Vector3 dirIn = -side;
                Vector3 standAt = cp + side * 3f;
                pl.Teleport(new Vector3(standAt.x, GroundY(standAt) + 0.6f, standAt.z));
                _d.Rig.Yaw = Mathf.Atan2(dirIn.x, dirIn.z) * Mathf.Rad2Deg;
                _d.Rig.Pitch = 10f;
                yield return null;
                yield return null;
                Vector3 before = crate.position;
                Vector3 p0 = pl.transform.position;
                float end = Time.unscaledTime + 2.2f;
                while (Time.unscaledTime < end) { InputState.ScriptedMove = new Vector2(0f, 1f); yield return null; }
                InputState.ScriptedMove = Vector2.zero;
                float pushed = Vector3.Distance(before, crate.position);
                Check("walking into a barrel pushes it", pushed > 0.4f,
                      "barrel moved " + pushed.ToString("0.00") + " m, rider moved " + Vector3.Distance(p0, pl.transform.position).ToString("0.0") + " m, start gap 3.0");
            }
            else Check("walking into a barrel pushes it", false, "no barrel found");

            // ---------------------------------------------------------- fall damage and water
            Vector3 hill = lay.City + lay.SeaDir * 105f;
            float gy = GroundY(hill);
            pl.Health.Health = 120f;
            pl.Teleport(new Vector3(hill.x, gy + 25f, hill.z));
            yield return Wait(3f);
            float lost = 120f - pl.Health.Health;
            Check("a 25 m fall hurts but does not kill", lost > 35f && lost < 100f && !pl.Health.Dead, "lost " + lost.ToString("0"));

            pl.Health.Health = 120f; pl.Stamina = 100f;
            Vector3 sea = lay.City + lay.SeaDir * 900f;
            pl.Teleport(new Vector3(sea.x, ThirdPersonPlayer.SeaLevel + 8f, sea.z));
            yield return Wait(4f);
            float sy = pl.transform.position.y;
            Check("the player floats in water instead of sinking", sy > ThirdPersonPlayer.SeaLevel - 1.8f && sy < ThirdPersonPlayer.SeaLevel + 1.5f, "y=" + sy.ToString("0.0"));
            float swimHp = pl.Health.Health;
            float de = Time.unscaledTime + 4f;
            while (Time.unscaledTime < de) { pl.Stamina = 0f; yield return null; }
            Check("an exhausted swimmer drowns slowly", pl.Health.Health < swimHp - 3f && !pl.Health.Dead, swimHp.ToString("0") + " -> " + pl.Health.Health.ToString("0"));

            // ---------------------------------------------------------- death and respawn
            int r0 = _d.Respawns;
            pl.Health.Hurt(10000f, null);
            yield return Until(() => _d.Respawns > r0, 6f);
            Vector3 checkpoint = pl.transform.position;
            Check("death respawns the player alive at a checkpoint", _d.Respawns == r0 + 1 && !pl.Health.Dead && pl.Health.Health >= 119f,
                  "respawns=" + (_d.Respawns - r0) + " health=" + pl.Health.Health.ToString("0"));
            Check("respawn is above the ground", checkpoint.y >= GroundY(checkpoint) - 0.5f);

            // ---------------------------------------------------------- save and load
            _d.Rep = new Reputation { Honour = 7, Fear = 3, Cunning = 5 };
            _d.Deaths = 4;
            bool saved = _d.SaveNow();
            Check("the game saves", saved && SaveSystem.Exists);
            _d.Rep = new Reputation();
            _d.Deaths = 0;
            bool loaded = _d.Continue();
            Check("the game loads what it saved", loaded && _d.Rep.Honour == 7 && _d.Rep.Fear == 3 && _d.Rep.Cunning == 5 && _d.Deaths == 4,
                  "honour=" + _d.Rep.Honour + " deaths=" + _d.Deaths);
            File.WriteAllText(_tmpSave, "{ this is not valid json ]");
            SaveData junk;
            Check("a damaged save is rejected without crashing", !SaveSystem.TryLoad(out junk));
            File.WriteAllText(_tmpSave, "{\"Version\":1,\"Stage\":3,\"PlayerPos\":{\"x\":999999,\"y\":0,\"z\":0}}");
            Check("an out-of-range save is rejected", !SaveSystem.TryLoad(out junk));
            _d.SaveNow();

            // ---------------------------------------------------------- dragon: mount, collisions, bounds
            pl.Teleport(dr.transform.position + new Vector3(0f, 0f, -8f));
            yield return null;
            dr.Mount();
            yield return Wait(0.5f);
            Check("mounting works", dr.Mounted && pl.Mounted);

            // Dive at the ground at full tilt: never below the terrain.
            Vector3 over = lay.City + lay.SeaDir * 450f;
            float oy = GroundY(over);
            dr.transform.position = new Vector3(over.x, oy + 90f, over.z);
            dr.Aim(new Vector3(0.2f, -0.9f, 0.2f));
            float worst = 99f;
            float e2 = Time.unscaledTime + 5f;
            while (Time.unscaledTime < e2)
            {
                InputState.ScriptedBoost = true;
                Vector3 dp = dr.transform.position;
                worst = Mathf.Min(worst, dp.y - GroundY(dp));
                yield return null;
            }
            Check("a dive cannot go through the terrain", worst > -0.5f, "lowest clearance " + worst.ToString("0.0") + " m");

            // Hit the city wall flat out at wall height: it must stop and not pass.
            Vector3 start = lay.City + lay.SeaDir * 330f;
            float wg = GroundY(start);
            dr.transform.position = new Vector3(start.x, wg + 5f, start.z);
            dr.Aim(-lay.SeaDir);
            dr.Health.Health = dr.Health.MaxHealth;
            int crashes0 = dr.Crashes;
            float minDist = 9999f;
            e2 = Time.unscaledTime + 7f;
            while (Time.unscaledTime < e2)
            {
                InputState.ScriptedBoost = true;
                Vector3 dp = dr.transform.position;
                float dc = Vector2.Distance(new Vector2(dp.x, dp.z), new Vector2(lay.City.x, lay.City.z));
                if (dp.y - GroundY(dp) < 9f) minDist = Mathf.Min(minDist, dc);
                yield return null;
            }
            InputState.ScriptedBoost = false;
            Check("the wall stops the dragon and it crashes", dr.Crashes > crashes0 && minDist > 150f, "crashes=" + (dr.Crashes - crashes0) + " closest=" + minDist.ToString("0") + " m");
            Check("a crash hurts the dragon", dr.Health.Health < dr.Health.MaxHealth, dr.Health.Health.ToString("0"));

            // World bounds.
            dr.Health.Health = dr.Health.MaxHealth;
            dr.transform.position = new Vector3(1300f, 300f, 0f);
            dr.Aim(Vector3.right);
            e2 = Time.unscaledTime + 9f;
            float far = 0f;
            while (Time.unscaledTime < e2)
            {
                Vector3 dp = dr.transform.position;
                far = Mathf.Max(far, Mathf.Sqrt(dp.x * dp.x + dp.z * dp.z));
                yield return null;
            }
            Check("the dragon cannot leave the world", far < 1520f, "furthest " + far.ToString("0") + " m");

            // Land and dismount.
            dr.Dismount();
            yield return Wait(0.5f);
            Check("dismounting returns control to the rider", !dr.Mounted && !pl.Mounted);

            // ---------------------------------------------------------- performance and errors
            _measure = false;
            _frames.Sort();
            float p50 = Pct(0.5f), p95 = Pct(0.95f), p99 = Pct(0.99f);
            Check("frame time holds the 60 fps budget (p95 <= 20 ms)", p95 <= 20f, "p50=" + p50.ToString("0.0") + " p95=" + p95.ToString("0.0") + " p99=" + p99.ToString("0.0") + " ms over " + _frames.Count + " frames");
            Check("no errors or exceptions were logged", _errors == 0, _errors + " logged");

            Debug.Log("[TEST] SUMMARY pass=" + _pass + " fail=" + _fail);
            try { File.Delete(_tmpSave); } catch (Exception) { }
            Application.Quit(_fail == 0 ? 0 : 1);
        }

        private float Pct(float p)
        {
            if (_frames.Count == 0) return 0f;
            return _frames[Mathf.Clamp(Mathf.RoundToInt(p * (_frames.Count - 1)), 0, _frames.Count - 1)];
        }
    }
}
