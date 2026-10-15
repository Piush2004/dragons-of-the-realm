import io
base = r'D:\Projects\Games\claude\unity\Emberbond\Assets\Scripts\Game\\'

def rd(n):
    return io.open(base + n, encoding='utf-8').read()

def wr(n, s):
    io.open(base + n, 'w', encoding='utf-8', newline='').write(s)

def rep(s, old, new, must=True):
    if old not in s:
        if must:
            raise SystemExit('missing: ' + old[:70])
        return s
    return s.replace(old, new, 1)

# ---------------- GameSettings
s = rd('GameSettings.cs')
s = rep(s, "        public static float UiScale = 1f;            // 0.8 .. 1.4\n",
        "        public static float UiScale = 1f;            // 0.8 .. 1.4\n        public static bool ShowHints = true;         // onboarding tips\n")
s = rep(s, '            UiScale = Clamp(PlayerPrefs.GetFloat(Prefix + "ui", 1f), 0.8f, 1.4f);\n',
        '            UiScale = Clamp(PlayerPrefs.GetFloat(Prefix + "ui", 1f), 0.8f, 1.4f);\n            ShowHints = PlayerPrefs.GetInt(Prefix + "hints", 1) == 1;\n            Bindings.Load();\n')
s = rep(s, '            PlayerPrefs.SetFloat(Prefix + "ui", UiScale);\n',
        '            PlayerPrefs.SetFloat(Prefix + "ui", UiScale);\n            PlayerPrefs.SetInt(Prefix + "hints", ShowHints ? 1 : 0);\n            Bindings.Save();\n')
wr('GameSettings.cs', s)

# ---------------- Menu
s = rd('Menu.cs')
s = rep(s, "public enum Page { None, Main, Pause, Settings }", "public enum Page { None, Main, Pause, Settings, Controls }")
s = rep(s, "            public Func<bool> Enabled = () => true;\n",
        "            public Func<bool> Enabled = () => true;\n            public bool Arrows = true;           // draw < value > around the value\n")
s = rep(s, '''                    r.Add(Toggle("Show frame rate", () => GameSettings.ShowFps, v => GameSettings.ShowFps = v));
''', '''                    r.Add(Toggle("Show frame rate", () => GameSettings.ShowFps, v => GameSettings.ShowFps = v));
                    r.Add(Toggle("Show tips", () => GameSettings.ShowHints, v => GameSettings.ShowHints = v));
''')
s = rep(s, '''                    r.Add(new Row { Label = () => "Back", Change = d => { if (d == 0) { GameSettings.Save(); Current = _settingsFrom; _sel = 0; } } });
                    break;
''', '''                    r.Add(new Row { Label = () => "Controls...", Change = d => { if (d == 0) { Current = Page.Controls; _sel = 0; } } });
                    r.Add(new Row { Label = () => "Back", Change = d => { if (d == 0) { GameSettings.Save(); Current = _settingsFrom; _sel = 0; } } });
                    break;
                case Page.Controls:
                    foreach (GameAction act in Bindings.All)
                    {
                        GameAction a = act;
                        r.Add(new Row
                        {
                            Label = () => Bindings.ActionName(a),
                            Value = () => _capturing.HasValue && _capturing.Value == a ? "press a key or mouse button" : Bindings.Label(a),
                            Arrows = false,
                            Change = d => { if (d == 0) { _capturing = a; _captureStart = Time.unscaledTime; } },
                        });
                    }
                    r.Add(new Row { Label = () => "Reset to defaults", Change = d => { if (d == 0) { Bindings.ResetDefaults(); Bindings.Save(); AudioDirector.Ui(); } } });
                    r.Add(new Row { Label = () => "Back", Change = d => { if (d == 0) { Bindings.Save(); Current = Page.Settings; _sel = 0; } } });
                    break;
''')
s = rep(s, "        private bool _lockCursor = true;\n",
        "        private bool _lockCursor = true;\n        private GameAction? _capturing;\n        private float _captureStart;\n")
s = rep(s, '''            List<Row> rows = Rows();
            if (_sel >= rows.Count) _sel = rows.Count - 1;
            _navCooldown -= Time.unscaledDeltaTime;
''', '''            if (_capturing.HasValue)
            {
                UpdateCapture(k);
                return;
            }

            List<Row> rows = Rows();
            if (_sel >= rows.Count) _sel = rows.Count - 1;
            _navCooldown -= Time.unscaledDeltaTime;
''')
s = rep(s, '''                if (Current == Page.Settings) { GameSettings.Save(); Current = _settingsFrom; _sel = 0; }
                else if (Current == Page.Pause) Close();''', '''                if (Current == Page.Controls) { Bindings.Save(); Current = Page.Settings; _sel = 0; }
                else if (Current == Page.Settings) { GameSettings.Save(); Current = _settingsFrom; _sel = 0; }
                else if (Current == Page.Pause) Close();''')
s = rep(s, '''        // ------------------------------------------------------------ drawing
''', '''        /// <summary>Waits for a key or mouse button and binds it. Escape cancels.</summary>
        private void UpdateCapture(Keyboard k)
        {
            if (Time.unscaledTime - _captureStart < 0.25f) return;       // ignore the click that started the capture
            if (k != null && k.escapeKey.wasPressedThisFrame) { _capturing = null; return; }
            Bind? got = null;
            if (k != null)
            {
                foreach (UnityEngine.InputSystem.Controls.KeyControl kc in k.allKeys)
                {
                    if (kc.wasPressedThisFrame && Bindings.Assignable(kc.keyCode)) { got = Bind.K(kc.keyCode); break; }
                }
            }
            Mouse m = Mouse.current;
            if (got == null && m != null)
            {
                if (m.leftButton.wasPressedThisFrame) got = Bind.M(0);
                else if (m.rightButton.wasPressedThisFrame) got = Bind.M(1);
                else if (m.middleButton.wasPressedThisFrame) got = Bind.M(2);
                else if (m.backButton.wasPressedThisFrame) got = Bind.M(3);
                else if (m.forwardButton.wasPressedThisFrame) got = Bind.M(4);
            }
            if (got.HasValue)
            {
                Bindings.Set(_capturing.Value, got.Value);
                Bindings.Save();
                _capturing = null;
                AudioDirector.Ui();
            }
        }

        // ------------------------------------------------------------ drawing
''')
s = rep(s, '''                    string shown = r.Arrows''', '''                    string shown = r.Arrows''', must=False)
s = rep(s, '''GUI.Label(new Rect(box.x + box.width * 0.5f, box.y, box.width * 0.5f - 24f, box.height), "<  " + r.Value() + "  >", right);''',
        '''string shown = r.Arrows ? "<  " + r.Value() + "  >" : r.Value();
                    GUI.Label(new Rect(box.x + box.width * 0.5f, box.y, box.width * 0.5f - 24f, box.height), shown, right);''')
s = rep(s, 'float panelW = Current == Page.Settings ? 900f : 520f;',
        'float panelW = (Current == Page.Settings || Current == Page.Controls) ? 900f : 520f;')
s = rep(s, '''            if (Current == Page.Settings) GUI.Label(new Rect(0f, top - 100f, W, 90f), "Settings", _title);''',
        '''            if (Current == Page.Settings) GUI.Label(new Rect(0f, top - 100f, W, 90f), "Settings", _title);
            if (Current == Page.Controls) GUI.Label(new Rect(0f, top - 100f, W, 90f), "Controls", _title);''')
s = rep(s, '''            string hint = Current == Page.Settings ? "Up/Down select    Left/Right change    Esc back"''',
        '''            string hint = Current == Page.Controls ? (_capturing.HasValue ? "Press the new key or mouse button    Esc cancel" : "Up/Down select    Enter rebind    Esc back")
                        : Current == Page.Settings ? "Up/Down select    Left/Right change    Esc back"''')
s = rep(s, "if (r.Value != null) r.Change(m.x < x + panelW * 0.75f ? -1 : 1);",
        "if (r.Value != null && r.Arrows) r.Change(m.x < x + panelW * 0.75f ? -1 : 1);\n                    else if (r.Value != null) r.Change(0);")
s = rep(s, "        public void DebugPause() { OpenPause(); }",
        "        public void DebugPause() { OpenPause(); }\n        public void DebugOpenControls() { Current = Page.Controls; _sel = 0; InputState.Blocked = true; }")
wr('Menu.cs', s)
print('ok')
