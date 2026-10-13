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
            GameObject dragonGo = GameObject.Find("Dragon");
            Camera cam = Camera.main;
            if (dragonGo == null || cam == null) return;

            float playerSpan = Normalize(dragonGo, 20f);
            GameObject enemyGo0 = GameObject.Find("EnemyDragon");
            if (enemyGo0 != null) Normalize(enemyGo0, 34f);

            Terrain t = Terrain.activeTerrain;
            Vector3 dp = dragonGo.transform.position;
            float ground = t != null ? t.SampleHeight(dp) + t.transform.position.y : dp.y;
            dragonGo.transform.position = new Vector3(dp.x, ground + 1.2f, dp.z);

            Vector3 spawn = dragonGo.transform.position + new Vector3(0f, 0f, -26f);
            float sg = t != null ? t.SampleHeight(spawn) + t.transform.position.y : spawn.y;
            spawn.y = sg + 0.5f;
            ThirdPersonPlayer player = ThirdPersonPlayer.Create(spawn);

            CameraRig rig = cam.gameObject.AddComponent<CameraRig>();
            rig.FootTarget = player.transform;
            rig.Yaw = 0f;                                   // look toward the dragon
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

    /// <summary>Plays a short scripted session and saves screenshots, so the real game can be checked unattended.</summary>
    public sealed class AutoPlay : MonoBehaviour
    {
        private float _t;
        private int _step;
        private Director _d;
        private string _dir;
        private float _worst, _sum; private int _frames;

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
            Debug.Log("[Emberbond] shot " + n);
        }

        private void LateUpdate()
        {
            _t += Time.unscaledDeltaTime;
            if (_t > 3f) { _sum += Time.unscaledDeltaTime; _frames++; _worst = Mathf.Max(_worst, Time.unscaledDeltaTime); }
            switch (_step)
            {
                case 0: if (_t > 5f) { Shot("01_dialogue"); _step++; } break;
                case 1: if (_t > 6f) { InputState.ScriptedChoice = 0; _step++; } break;
                case 2: if (_t > 9f) { Shot("02_onfoot"); _step++; } break;
                case 3:
                    if (_t > 10f) { _d.Dragon.transform.position = _d.Player.transform.position + new Vector3(6f, 0f, 0f); _d.Dragon.Mount(); _step++; }
                    break;
                case 4:
                    if (_t > 14f) { InputState.ScriptedLook = new Vector2(0.4f, -0.1f); _step++; }
                    break;
                case 5:
                    if (_t > 16f) { Shot("03_flight"); InputState.ScriptedFire = true; _step++; }
                    break;
                case 6:
                    if (_t > 17.5f) { Shot("04_fireball"); InputState.ScriptedFire = false; _d.BeginQuestPublic(3); _step++; }
                    break;
                case 7:
                    if (_t > 30f) { Shot("05_enemy"); _step++; }
                    break;
                case 8:
                    if (_t > 32f)
                    {
                        Debug.Log("[Emberbond] autoplay done frames=" + _frames + " avg=" + (_sum / Mathf.Max(1, _frames) * 1000f).ToString("0.0") +
                                  " ms worst=" + (_worst * 1000f).ToString("0.0") + " ms");
                        File.WriteAllText(Path.Combine(_dir, "done.txt"), "ok");
                        Application.Quit(0);
                        _step++;
                    }
                    break;
            }
        }
    }
}
