using System;
using System.IO;
using UnityEngine;
using BlockRealm.Sim;

namespace BlockRealm.Game
{
    /// <summary>
    /// Unattended check of the real client: starts a single-player world, looks around, takes screenshots and
    /// logs frame rate and any errors, then quits. Run the built player with:
    ///   BlockRealm.exe -smoke [seconds] -shots outputFolder
    /// </summary>
    public sealed class SmokeTest : MonoBehaviour
    {
        public GameSession Session;
        private float _t, _shotAt;
        private int _shot, _frames, _errors;
        private float _minFps = 1e9f;
        private string _dir;
        private float _duration = 40f;
        private bool _started;

        public static bool Wanted(string[] args)
        {
            foreach (string a in args) if (string.Equals(a, "-smoke", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public void Begin(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-shots", StringComparison.OrdinalIgnoreCase)) _dir = args[i + 1];
                if (string.Equals(args[i], "-smoke", StringComparison.OrdinalIgnoreCase))
                {
                    float d;
                    if (float.TryParse(args[i + 1], out d) && d > 5f) _duration = d;
                }
            }
            if (string.IsNullOrEmpty(_dir)) _dir = Path.Combine(Application.persistentDataPath, "smoke");
            Directory.CreateDirectory(_dir);
            Application.logMessageReceived += OnLog;
            Debug.Log("[Smoke] begin, " + _duration + " s, shots in " + _dir);
        }

        private void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _errors++;
                if (_errors <= 20) Debug.Log("[Smoke] captured " + type + ": " + msg);
            }
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            _frames++;
            if (!_started && _t > 1.5f)
            {
                _started = true;
                Session.StartSinglePlayer("Smoke" + DateTime.Now.Ticks % 100000, 2024, false);
            }
            GameClient c = Session.Client;
            if (c != null && c.State == ClientState.Joined && _t > 6f)
            {
                float fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
                if (_t > 10f && fps < _minFps) _minFps = fps;
                // Turn slowly so every direction gets drawn, and take a picture every few seconds.
                Session.Input.SetLook(_t * 0.35f, 0.15f + 0.1f * Mathf.Sin(_t * 0.5f));
                if (_t > _shotAt)
                {
                    _shotAt = _t + 6f;
                    string path = Path.Combine(_dir, "shot_" + _shot++ + ".png");
                    ScreenCapture.CaptureScreenshot(path);
                    Debug.Log("[Smoke] shot " + path + " columns=" + c.World.ColumnCount + " pos=" + c.Player.Pos + " fps=" + fps.ToString("0"));
                }
            }
            if (_t > _duration)
            {
                Debug.Log("[Smoke] done: state=" + (c != null ? c.State.ToString() : "none") + " frames=" + _frames +
                          " avgFps=" + (_frames / _t).ToString("0.0") + " minFps=" + _minFps.ToString("0") + " errors=" + _errors);
                Application.Quit(_errors == 0 && c != null && c.State == ClientState.Joined ? 0 : 2);
            }
        }
    }
}
