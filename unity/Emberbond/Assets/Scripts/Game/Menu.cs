using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Emberbond
{
    /// <summary>Title screen, pause menu and settings, navigable with mouse, keyboard or gamepad.</summary>
    public sealed class Menu : MonoBehaviour
    {
        public enum Page { None, Main, Pause, Settings }

        public Director Director;
        public CameraRig Rig;
        public Page Current = Page.None;

        private Page _settingsFrom = Page.Main;
        private int _sel;
        private float _navCooldown;
        private Texture2D _white;
        private GUIStyle _title, _item, _hint, _small;
        private const float W = 1920f, H = 1080f;
        private bool _lockCursor = true;

        private sealed class Row
        {
            public Func<string> Label;
            public Func<string> Value;          // null for plain buttons
            public Action<int> Change;          // -1, 0 (activate), +1
            public Func<bool> Enabled = () => true;
        }

        public bool IsOpen { get { return Current != Page.None; } }

        private void Start()
        {
            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
            _lockCursor = !(GameBootstrap.Has("-autoplay") || GameBootstrap.Has("-selftest") || GameBootstrap.Has("-soak") || GameBootstrap.Has("-menushots"));
        }

        // ------------------------------------------------------------ opening and closing

        public void ShowMain()
        {
            Current = Page.Main;
            _sel = 0;
            if (Rig != null) Rig.Frozen = true;
            InputState.Blocked = true;
            Time.timeScale = 1f;
            SetCursor(true);
            AudioDirector.StartAmbience();
        }

        private void OpenPause()
        {
            Current = Page.Pause;
            _sel = 0;
            Time.timeScale = 0f;
            InputState.Blocked = true;
            SetCursor(true);
        }

        private void Close()
        {
            Current = Page.None;
            Time.timeScale = 1f;
            InputState.Blocked = false;
            if (Rig != null && Director != null && Director.Started && !Director.InDialogue) Rig.Frozen = false;
            SetCursor(false);
            GameSettings.Save();
        }

        private void SetCursor(bool visible)
        {
            if (!_lockCursor) return;
            Cursor.visible = visible;
            Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        }

        // ------------------------------------------------------------ rows

        private List<Row> Rows()
        {
            List<Row> r = new List<Row>();
            switch (Current)
            {
                case Page.Main:
                    r.Add(new Row { Label = () => "Begin the Story", Change = d => { if (d == 0) StartNew(); } });
                    r.Add(new Row { Label = () => "Continue", Enabled = () => SaveSystem.Exists, Change = d => { if (d == 0) ContinueGame(); } });
                    r.Add(new Row { Label = () => "Settings", Change = d => { if (d == 0) OpenSettings(Page.Main); } });
                    r.Add(new Row { Label = () => "Quit", Change = d => { if (d == 0) Quit(); } });
                    break;
                case Page.Pause:
                    r.Add(new Row { Label = () => "Resume", Change = d => { if (d == 0) Close(); } });
                    r.Add(new Row { Label = () => "Save game", Change = d => { if (d == 0) { bool ok = Director.SaveNow(); Director.Say(ok ? "Game saved." : "Could not save here."); } } });
                    r.Add(new Row { Label = () => "Settings", Change = d => { if (d == 0) OpenSettings(Page.Pause); } });
                    r.Add(new Row { Label = () => "Return to title", Change = d => { if (d == 0) ToTitle(); } });
                    r.Add(new Row { Label = () => "Quit game", Change = d => { if (d == 0) Quit(); } });
                    break;
                case Page.Settings:
                    r.Add(Slider("Mouse sensitivity", () => GameSettings.MouseSens, v => GameSettings.MouseSens = v, 0.3f, 3f, 0.1f, "0.0"));
                    r.Add(Toggle("Invert Y axis", () => GameSettings.InvertY, v => GameSettings.InvertY = v));
                    r.Add(Slider("Field of view", () => GameSettings.Fov, v => GameSettings.Fov = v, 50f, 90f, 2f, "0"));
                    r.Add(Slider("Master volume", () => GameSettings.MasterVolume, v => { GameSettings.MasterVolume = v; GameSettings.ApplyAudio(); }, 0f, 1f, 0.1f, "0%"));
                    r.Add(Slider("Music volume", () => GameSettings.MusicVolume, v => { GameSettings.MusicVolume = v; AudioDirector.ApplyVolumes(); }, 0f, 1f, 0.1f, "0%"));
                    r.Add(Choice("Graphics tier (restart)", () => GameSettings.Quality, v => GameSettings.Quality = v, new[] { "Auto", "High", "Low" }));
                    r.Add(Choice("Render scale", () => ScaleIndex(), v => { GameSettings.RenderScale = ScaleFromIndex(v); ApplyScale(); }, new[] { "Auto (hold 60 fps)", "100%", "85%", "70%", "55%" }));
                    r.Add(Toggle("Show frame rate", () => GameSettings.ShowFps, v => GameSettings.ShowFps = v));
                    r.Add(Slider("Interface size", () => GameSettings.UiScale, v => GameSettings.UiScale = v, 0.8f, 1.4f, 0.1f, "0.0"));
                    r.Add(new Row { Label = () => "Back", Change = d => { if (d == 0) { GameSettings.Save(); Current = _settingsFrom; _sel = 0; } } });
                    break;
            }
            return r;
        }

        private static int ScaleIndex()
        {
            switch (GameSettings.RenderScale) { case 100: return 1; case 85: return 2; case 70: return 3; case 55: return 4; default: return 0; }
        }

        private static int ScaleFromIndex(int i)
        {
            switch (i) { case 1: return 100; case 2: return 85; case 3: return 70; case 4: return 55; default: return 0; }
        }

        private static void ApplyScale()
        {
            PerformanceScaler ps = FindFirstObjectByType<PerformanceScaler>();
            if (ps == null) return;
            ps.Pinned = GameSettings.RenderScale == 0 ? -1f : GameSettings.RenderScale / 100f;
        }

        private static Row Slider(string label, Func<float> get, Action<float> set, float min, float max, float step, string fmt)
        {
            return new Row
            {
                Label = () => label,
                Value = () => fmt.EndsWith("%") ? Mathf.RoundToInt(get() * 100f) + "%" : get().ToString(fmt),
                Change = d => { if (d != 0) set(Mathf.Clamp(Mathf.Round((get() + d * step) / step) * step, min, max)); },
            };
        }

        private static Row Toggle(string label, Func<bool> get, Action<bool> set)
        {
            return new Row { Label = () => label, Value = () => get() ? "On" : "Off", Change = d => set(!get()) };
        }

        private static Row Choice(string label, Func<int> get, Action<int> set, string[] options)
        {
            return new Row
            {
                Label = () => label,
                Value = () => options[Mathf.Clamp(get(), 0, options.Length - 1)],
                Change = d => set((get() + (d == 0 ? 1 : d) + options.Length) % options.Length),
            };
        }

        // ------------------------------------------------------------ actions

        private void StartNew()
        {
            SaveSystem.Delete();
            Close();
            Director.BeginNewGame();
        }

        private void ContinueGame()
        {
            Close();
            if (!Director.Continue()) { Director.BeginNewGame(); }
        }

        private void OpenSettings(Page from)
        {
            _settingsFrom = from;
            Current = Page.Settings;
            _sel = 0;
        }

        // Test hooks.
        public void DebugOpenSettings() { Current = Page.Main; OpenSettings(Page.Main); InputState.Blocked = true; }
        public void DebugCloseAll() { Close(); }
        public void DebugPause() { OpenPause(); }

        private void ToTitle()
        {
            Director.SaveNow();
            Time.timeScale = 1f;
            Director.Started = false;
            Director.InDialogue = false;
            ShowMain();
        }

        private void Quit()
        {
            GameSettings.Save();
            Application.Quit();
        }

        // ------------------------------------------------------------ input

        private void Update()
        {
            Keyboard k = Keyboard.current;
            Gamepad g = Gamepad.current;
            bool pausePressed = (k != null && k.escapeKey.wasPressedThisFrame) || (g != null && g.startButton.wasPressedThisFrame);

            if (Current == Page.None)
            {
                if (pausePressed && Director != null && Director.Started && !Director.InDialogue) OpenPause();
                return;
            }

            // The title camera drifts slowly around the city.
            if (Current == Page.Main && Rig != null)
            {
                WorldLayout lay = WorldLayout.Load();
                float a = Time.unscaledTime * 0.05f;
                Vector3 c = lay.City;
                Vector3 p = c + new Vector3(Mathf.Sin(a) * 330f, 120f, Mathf.Cos(a) * 330f);
                Terrain t = Terrain.activeTerrain;
                if (t != null) p.y = Mathf.Max(p.y, t.SampleHeight(p) + t.transform.position.y + 40f);
                Rig.transform.position = p;
                Rig.transform.rotation = Quaternion.LookRotation(c + Vector3.up * 30f - p, Vector3.up);
            }

            List<Row> rows = Rows();
            if (_sel >= rows.Count) _sel = rows.Count - 1;
            _navCooldown -= Time.unscaledDeltaTime;

            int move = 0, change = 0;
            bool activate = false, back = pausePressed;
            if (k != null)
            {
                if (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame) move = 1;
                if (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame) move = -1;
                if (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame) change = -1;
                if (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame) change = 1;
                if (k.enterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame) activate = true;
            }
            if (g != null && _navCooldown <= 0f)
            {
                Vector2 s = g.leftStick.ReadValue();
                if (g.dpad.down.wasPressedThisFrame || s.y < -0.6f) { move = 1; _navCooldown = 0.2f; }
                if (g.dpad.up.wasPressedThisFrame || s.y > 0.6f) { move = -1; _navCooldown = 0.2f; }
                if (g.dpad.left.wasPressedThisFrame || s.x < -0.6f) { change = -1; _navCooldown = 0.15f; }
                if (g.dpad.right.wasPressedThisFrame || s.x > 0.6f) { change = 1; _navCooldown = 0.15f; }
                if (g.buttonSouth.wasPressedThisFrame) activate = true;
                if (g.buttonEast.wasPressedThisFrame) back = true;
            }
            if (move != 0)
            {
                _sel = (_sel + move + rows.Count) % rows.Count;
                AudioDirector.Ui();
            }
            Row cur = rows[_sel];
            if (change != 0 && cur.Value != null) { cur.Change(change); AudioDirector.Ui(); }
            if (activate && cur.Enabled()) { cur.Change(0); AudioDirector.Ui(); }
            if (back)
            {
                if (Current == Page.Settings) { GameSettings.Save(); Current = _settingsFrom; _sel = 0; }
                else if (Current == Page.Pause) Close();
            }
        }

        // ------------------------------------------------------------ drawing

        private void Rect(float x, float y, float w, float h, Color c)
        {
            GUI.color = c; GUI.DrawTexture(new Rect(x, y, w, h), _white); GUI.color = Color.white;
        }

        private void Styles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 64, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.96f, 0.9f, 0.76f) } };
            _item = new GUIStyle(GUI.skin.label) { fontSize = 32, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.9f, 0.88f, 0.82f) } };
            _hint = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.9f, 0.87f, 0.8f) } };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 22, normal = { textColor = new Color(0.8f, 0.78f, 0.7f) } };
        }

        private void OnGUI()
        {
            if (Current == Page.None) return;
            Styles();
            float scale = Mathf.Min(Screen.width / W, Screen.height / H);
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), Vector2.zero);

            if (Current == Page.Main)
            {
                Rect(0f, 0f, W, 260f, new Color(0f, 0f, 0f, 0.7f));
                GUI.Label(new Rect(0f, 70f, W, 90f), "E M B E R B O N D", _title);
                GUI.Label(new Rect(0f, 160f, W, 40f), "A story of dragons and a dying dynasty", _hint);
                Rect(0f, H - 110f, W, 90f, new Color(0f, 0f, 0f, 0.6f));
            }
            else Rect(0f, 0f, W, H, new Color(0f, 0f, 0f, 0.6f));

            List<Row> rows = Rows();
            float rowH = 62f;
            float panelW = Current == Page.Settings ? 900f : 520f;
            float top = Current == Page.Main ? 360f : Mathf.Max(120f, (H - rows.Count * rowH) * 0.5f);
            float x = (W - panelW) * 0.5f;
            // A dark backing panel keeps the rows readable over bright terrain on the title screen.
            if (Current == Page.Main) Rect(x - 24f, top - 16f, panelW + 48f, rows.Count * rowH + 24f, new Color(0.04f, 0.035f, 0.03f, 0.82f));
            if (Current == Page.Settings) GUI.Label(new Rect(0f, top - 100f, W, 90f), "Settings", _title);
            if (Current == Page.Pause) GUI.Label(new Rect(0f, top - 110f, W, 90f), "Paused", _title);

            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                Rect box = new Rect(x, top + i * rowH, panelW, rowH - 8f);
                bool enabled = r.Enabled();
                if (i == _sel) Rect(box.x, box.y, box.width, box.height, new Color(0.7f, 0.55f, 0.25f, 0.35f));
                Color old = GUI.contentColor;
                GUI.contentColor = enabled ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                GUI.Label(new Rect(box.x + 24f, box.y, box.width * 0.6f, box.height), r.Label(), _item);
                if (r.Value != null)
                {
                    GUIStyle right = new GUIStyle(_item) { alignment = TextAnchor.MiddleRight };
                    GUI.Label(new Rect(box.x + box.width * 0.5f, box.y, box.width * 0.5f - 24f, box.height), "<  " + r.Value() + "  >", right);
                }
                GUI.contentColor = old;
                if (Event.current.type == EventType.MouseDown && box.Contains(Event.current.mousePosition))
                {
                    Vector2 m = Event.current.mousePosition;      // already in the scaled GUI space
                    _sel = i;
                    if (r.Value != null) r.Change(m.x < x + panelW * 0.75f ? -1 : 1);
                    else if (enabled) r.Change(0);
                    AudioDirector.Ui();
                    Event.current.Use();
                }
            }
            string hint = Current == Page.Settings ? "Up/Down select    Left/Right change    Esc back"
                        : Current == Page.Main ? "Up/Down select    Enter choose"
                        : "Up/Down select    Enter choose    Esc resume";
            GUI.Label(new Rect(0f, H - 90f, W, 40f), hint, _hint);
        }
    }
}
