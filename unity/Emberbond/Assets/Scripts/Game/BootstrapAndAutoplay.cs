using System;
using System.IO;
using UnityEngine;

namespace Emberbond
{
    /// <summary>Wires the island scene into a playable game when it loads.</summary>
    public static class GameBootstrap
    {
        public static bool Has(string flag)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length; i++) if (string.Equals(a[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Scales a model so its largest horizontal extent (the wingspan) is the given size in metres.</summary>
        public static float Normalize(GameObject go, float targetSpan)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return targetSpan;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            float span = Mathf.Max(b.size.x, b.size.z);
            if (span > 0.01f) go.transform.localScale *= targetSpan / span;
            return targetSpan;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Has("-emberShots")) return;                   // the flyover screenshot tool owns the camera
            QualityGovernor.Apply();
            if (Has("-nohall"))
            {
                GameObject hallGo = GameObject.Find("ThroneHall");
                if (hallGo != null) hallGo.SetActive(false);
            }
            if (Has("-nocity"))
            {
                GameObject cityGo = GameObject.Find("Hearthmoor");
                if (cityGo != null) cityGo.SetActive(false);
            }
            GameObject dragonGo = GameObject.Find("Dragon");
            Camera cam = Camera.main;
            if (dragonGo == null || cam == null) return;

            float playerSpan = Normalize(dragonGo, 20f);
            GameObject enemyGo0 = GameObject.Find("EnemyDragon");
            if (enemyGo0 != null) Normalize(enemyGo0, 34f);

            Terrain t = Terrain.activeTerrain;
            WorldLayout lay = WorldLayout.Load();
            Vector3 dp = lay.City + lay.SeaDir * 215f;
            float ground = t != null ? t.SampleHeight(dp) + t.transform.position.y : dp.y;
            dragonGo.transform.position = new Vector3(dp.x, ground + 1.2f, dp.z);

            Vector3 spawn = lay.City + lay.SeaDir * 105f + Vector3.Cross(Vector3.up, lay.SeaDir) * 5f;
            float sg = t != null ? t.SampleHeight(spawn) + t.transform.position.y : spawn.y;
            spawn.y = sg + 0.5f;
            ThirdPersonPlayer player = ThirdPersonPlayer.Create(spawn);

            CameraRig rig = cam.gameObject.AddComponent<CameraRig>();
            rig.FootTarget = player.transform;
            rig.Yaw = lay.SeaYaw + 180f;                    // look inland, toward the keep
            rig.FlightBack = playerSpan * 1.15f;
            rig.FlightUp = playerSpan * 0.3f;

            dragonGo.AddComponent<DragonAnimator>();
            DragonFlight dragon = dragonGo.AddComponent<DragonFlight>();
            dragon.Init(player, rig);
            dragon.ModelYaw = 0f;

            EnemyDragon enemy = null;
            GameObject enemyGo = GameObject.Find("EnemyDragon");
            if (enemyGo != null) { enemyGo.AddComponent<DragonAnimator>(); enemy = enemyGo.AddComponent<EnemyDragon>(); enemyGo.SetActive(false); }

            GameObject dir = new GameObject("Director");
            Director d = dir.AddComponent<Director>();
            d.Player = player; d.Dragon = dragon; d.Enemy = enemy; d.Rig = rig;
            Hud hud = dir.AddComponent<Hud>();
            hud.Director = d;

            if (Has("-autoplay")) dir.AddComponent<AutoPlay>();
            else { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }
    }

    /// <summary>Plays a scripted session and saves screenshots, so the real game can be checked unattended.</summary>
    public sealed class AutoPlay : MonoBehaviour
    {
        private float _t;
        private int _step;
        private Director _d;
        private string _dir;
        private float _worst, _sum; private int _frames;
        private float _nextSwing;
        private float _bucket; private int _bucketFrames;

        private void Start()
        {
            _d = GetComponent<Director>();
            _dir = Path.Combine(Directory.GetCurrentDirectory(), "play");
            Directory.CreateDirectory(_dir);
            InputState.Scripted = true;
            Debug.Log("[Emberbond] autoplay started");
        }

        private void Shot(string n)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, n + ".png"));
            Debug.Log("[Emberbond] shot " + n + " stage=" + _d.QuestTitle);
        }

        private void LateUpdate()
        {
            float dtf = Time.unscaledDeltaTime;
            _t += dtf;
            if (_t > 3f) { _sum += dtf; _frames++; _worst = Mathf.Max(_worst, dtf); }
            if (dtf > 0.5f) Debug.Log("[Emberbond] STALL " + dtf.ToString("0.0") + " s at t=" + _t.ToString("0.0") + " step=" + _step + " quest=" + _d.QuestTitle);
            _bucket += dtf; _bucketFrames++;
            if (_bucket >= 5f)
            {
                Debug.Log("[Emberbond] perf t=" + _t.ToString("0") + " fps=" + (_bucketFrames / _bucket).ToString("0") + " step=" + _step);
                _bucket = 0f; _bucketFrames = 0;
            }

            // While the fight is on, keep swinging and raise a guard now and then.
            if (_step >= 2 && _step <= 5 && _t > _nextSwing)
            {
                _nextSwing = _t + 0.65f;
                if (Mathf.FloorToInt(_t) % 4 == 0) InputState.ScriptedBlock = true; else InputState.ScriptedAttack = true;
            }

            switch (_step)
            {
                case 0: if (_t > 5f) { Shot("01_hall"); _step++; } break;
                case 1: if (_t > 6f) { InputState.ScriptedChoice = 0; _step++; } break;
                case 2: if (_t > 9f) { Shot("02_soldiers"); _step++; } break;
                case 3: if (_t > 15f) { Shot("03_combat"); _step++; } break;
                case 4: if (_t > 22f) { Shot("04_aftermath"); _step++; } break;
                case 5:
                    if (_t > 24f)
                    {
                        _d.BeginQuestPublic(1);
                        _d.Dragon.transform.position = _d.Player.transform.position + new Vector3(8f, 0f, 0f);
                        _d.Dragon.Mount();
                        _step++;
                    }
                    break;
                case 6: if (_t > 28f) { InputState.ScriptedLook = new Vector2(0.4f, -0.1f); _step++; } break;
                case 7: if (_t > 31f) { Shot("05_flight"); InputState.ScriptedFire = true; _step++; } break;
                case 8: if (_t > 32.5f) { Shot("06_fire"); InputState.ScriptedFire = false; _d.BeginQuestPublic(3); _step++; } break;
                case 9: if (_t > 45f) { Shot("07_rival"); _step++; } break;
                case 10:
                    if (_t > 47f)
                    {
                        Debug.Log("[Emberbond] autoplay done frames=" + _frames + " avg=" + (_sum / Mathf.Max(1, _frames) * 1000f).ToString("0.0") +
                                  " ms worst=" + (_worst * 1000f).ToString("0.0") + " ms playerHealth=" + _d.Player.Health.Health.ToString("0"));
                        File.WriteAllText(Path.Combine(_dir, "done.txt"), "ok");
                        Application.Quit(0);
                        _step++;
                    }
                    break;
            }
        }
    }
}
