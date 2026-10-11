using System;
using System.Collections.Generic;
using UnityEngine;
using BlockRealm.Net;
using BlockRealm.Sim;

namespace BlockRealm.Game
{
    public enum UiScreen { None, Inventory, Container, Chat, Pause, Settings }

    /// <summary>
    /// All 2D interface: in-game HUD, chat, inventory windows and the menus. Uses Unity's immediate-mode GUI scaled
    /// by whole numbers, so the Blender-drawn pixel sprites stay crisp.
    /// </summary>
    public sealed partial class GameUi : MonoBehaviour
    {
        public GameSession Session;

        private UiScreen _screen = UiScreen.None;
        private float _scale = 3f;
        private float _vw, _vh;
        private Vector2 _mouse;                        // in virtual (scaled) coordinates
        private Vector2 _padCursor = new Vector2(200f, 120f);
        private bool _padClick, _padRightClick, _padShift;
        private GUIStyle _label, _small, _title, _button, _field;
        private bool _stylesReady;
        private string _chatInput = "";
        private bool _chatFocus;
        private float _damageFlash;
        private float _airTimer = 15f;
        private float _fpsTimer, _fps;
        private int _fpsFrames;
        private bool _debug;
        private string _toast = "";
        private float _toastTime;
        private readonly List<float> _chatTimes = new List<float>();
        private int _chatSeen;
        private int _lastSelected = -1;
        private float _selectedNameTime;
        private bool _menuInit;

        public bool BlocksInput { get { return _screen != UiScreen.None; } }

        private void Awake()
        {
            _menuInit = false;
        }

        private void BeginFrame()
        {
            _scale = Mathf.Max(2f, Mathf.Round(Screen.height / 300f));
            _vw = Screen.width / _scale;
            _vh = Screen.height / _scale;
            Vector3 mp = Input.mousePosition;
            _mouse = new Vector2(mp.x / _scale, (Screen.height - mp.y) / _scale);
            GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1f));
            if (!_stylesReady) BuildStyles();
        }

        private void BuildStyles()
        {
            _stylesReady = true;
            Font font = GameAssets.UiFont;
            _label = new GUIStyle { font = font, fontSize = 8, normal = { textColor = Color.white }, alignment = TextAnchor.UpperLeft };
            _small = new GUIStyle(_label) { fontSize = 6 };
            _title = new GUIStyle(_label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _button = new GUIStyle(_label) { fontSize = 8, alignment = TextAnchor.MiddleCenter };
            _field = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 8, alignment = TextAnchor.MiddleLeft };
            _field.normal.textColor = Color.white;
            _field.focused.textColor = Color.white;
            _field.normal.background = GameAssets.Ui("slot");
            _field.focused.background = GameAssets.Ui("slot_selected");
            _field.border = new RectOffset(2, 2, 2, 2);
            _field.padding = new RectOffset(3, 3, 2, 2);
        }

        // ------------------------------------------------------------------ small drawing helpers

        private void Tex(Rect r, Texture2D t)
        {
            if (t != null) GUI.DrawTexture(r, t, ScaleMode.StretchToFill, true);
        }

        private void TexTint(Rect r, Texture2D t, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            Tex(r, t);
            GUI.color = old;
        }

        private void Fill(Rect r, Color c)
        {
            TexTint(r, GameAssets.White, c);
        }

        private void Text(Rect r, string s, GUIStyle style, Color color, TextAnchor anchor = TextAnchor.UpperLeft, bool shadow = true)
        {
            TextAnchor old = style.alignment;
            style.alignment = anchor;
            if (shadow)
            {
                Rect sr = new Rect(r.x + 0.6f, r.y + 0.6f, r.width, r.height);
                style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.8f);
                GUI.Label(sr, s, style);
            }
            style.normal.textColor = color;
            GUI.Label(r, s, style);
            style.alignment = old;
        }

        private bool Hover(Rect r)
        {
            Vector2 p = Session != null && Session.Input.UsingGamepad ? _padCursor : _mouse;
            return r.Contains(p);
        }

        private bool ClickedLeft(Rect r)
        {
            if (!Hover(r)) return false;
            if (Session.Input.UsingGamepad) return _padClick;
            Event e = Event.current;
            return e.type == EventType.MouseDown && e.button == 0;
        }

        private bool ClickedRight(Rect r)
        {
            if (!Hover(r)) return false;
            if (Session.Input.UsingGamepad) return _padRightClick;
            Event e = Event.current;
            return e.type == EventType.MouseDown && e.button == 1;
        }

        private bool Btn(Rect r, string label, bool enabled = true)
        {
            bool hover = enabled && Hover(r);
            Texture2D bg = GameAssets.Ui(hover ? "button_hover" : "button");
            TexTint(r, bg, enabled ? Color.white : new Color(0.6f, 0.6f, 0.6f, 1f));
            Text(r, label, _button, enabled ? (hover ? new Color(1f, 1f, 0.6f) : Color.white) : new Color(0.7f, 0.7f, 0.7f), TextAnchor.MiddleCenter);
            if (enabled && ClickedLeft(r))
            {
                if (Event.current != null) Event.current.Use();
                _padClick = false;
                if (Session.Audio != null) Session.Audio.Play(Sfx.Click, Session.Cam.transform.position, 0.5f);
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            if (Session == null) return;
            _fpsFrames++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f) { _fps = _fpsFrames / _fpsTimer; _fpsFrames = 0; _fpsTimer = 0f; }
            if (_damageFlash > 0f) _damageFlash -= Time.unscaledDeltaTime * 1.6f;

            GameClient c = Session.Client;
            InputProvider input = Session.Input;
            if (Session.State == SessionState.Playing && c != null && c.State == ClientState.Joined)
            {
                if (c.Dead) { if (_screen != UiScreen.None && _screen != UiScreen.Settings) _screen = UiScreen.None; }
                HandleInGameCommands(c, input);

                // The server opening a container (crafting table, furnace, chest) shows its window.
                if (c.WindowOpen && _screen != UiScreen.Container) _screen = UiScreen.Container;
                if (!c.WindowOpen && _screen == UiScreen.Container) _screen = UiScreen.None;

                if (c.Selected != _lastSelected)
                {
                    _lastSelected = c.Selected;
                    _selectedNameTime = 2.0f;
                }
                if (_selectedNameTime > 0f) _selectedNameTime -= Time.unscaledDeltaTime;

                if (c.Player.HeadInWater) _airTimer = Mathf.Max(0f, _airTimer - Time.unscaledDeltaTime);
                else _airTimer = Mathf.Min(15f, _airTimer + Time.unscaledDeltaTime * 4f);
            }

            bool menuVisible = Session.State != SessionState.Playing;
            bool captured = Session.State == SessionState.Playing && _screen == UiScreen.None && c != null && !c.Dead;
            Session.UiBlocksInput = _screen != UiScreen.None || (c != null && c.Dead);
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
            if (menuVisible) _screen = UiScreen.None;

            // Gamepad: move a virtual cursor with the stick while an interface is open.
            if (input.UsingGamepad)
            {
                _padCursor += input.PadCursorDelta / _scale;
                _padCursor.x = Mathf.Clamp(_padCursor.x, 0f, _vw);
                _padCursor.y = Mathf.Clamp(_padCursor.y, 0f, _vh);
            }
            _padClick = input.PadClick; _padRightClick = input.PadRightClick; _padShift = input.PadShiftClick;
        }

        private void HandleInGameCommands(GameClient c, InputProvider input)
        {
            if (c.Dead) return;
            if (_screen == UiScreen.None)
            {
                if (input.PausePressed) _screen = UiScreen.Pause;
                else if (input.InventoryPressed) { _screen = UiScreen.Inventory; }
                else if (input.ChatPressed) { _screen = UiScreen.Chat; _chatInput = ""; _chatFocus = true; }
                else if (input.CommandPressed) { _screen = UiScreen.Chat; _chatInput = "/"; _chatFocus = true; }
                if (input.DebugPressed) _debug = !_debug;
                if (input.CameraPressed && Session.View != null) Session.View.CycleCamera();
                if (input.HotbarDirect >= 0) c.SelectSlot(input.HotbarDirect);
                if (input.HotbarDelta != 0) c.SelectSlot((c.Selected + input.HotbarDelta + 9) % 9);
                if (input.DropPressed) c.DropSelected(input.DropStackPressed);
                if (input.JumpDoubleTapped) c.ToggleFlight();
            }
            else
            {
                bool close = input.PausePressed || input.ClosePressed;
                if (_screen == UiScreen.Inventory || _screen == UiScreen.Container)
                {
                    if (input.InventoryPressed || input.ClosePressed || input.PausePressed)
                    {
                        c.CloseWindow();
                        _screen = UiScreen.None;
                    }
                }
                else if (_screen == UiScreen.Chat)
                {
                    if (input.PausePressed) _screen = UiScreen.None;
                }
                else if (_screen == UiScreen.Pause || _screen == UiScreen.Settings)
                {
                    if (close) { GameSettings.Save(); _screen = UiScreen.None; }
                }
            }
        }

        private void OnGUI()
        {
            if (Session == null) return;
            BeginFrame();
            // Run pad-click consumption once per frame, on the layout pass.
            if (Session.State != SessionState.Playing) { DrawMenus(); DrawCursorIfPad(); return; }

            GameClient c = Session.Client;
            if (c == null || c.State != ClientState.Joined || c.World == null) { DrawConnecting(); return; }

            DrawWorldOverlays(c);
            DrawHud(c);
            DrawChat(c);
            DrawLabels(c);
            if (_debug) DrawDebug(c);
            DrawToast();
            if (c.Dead) DrawDeath(c);
            else if (_screen == UiScreen.Inventory || _screen == UiScreen.Container) DrawWindow(c);
            else if (_screen == UiScreen.Pause) DrawPause(c);
            else if (_screen == UiScreen.Settings) DrawSettings(true);
            DrawCursorIfPad();
            if (Event.current.type == EventType.Repaint) { _padClick = false; _padRightClick = false; _padShift = false; }
        }

        private void DrawCursorIfPad()
        {
            bool needsCursor = _screen != UiScreen.None || Session.State != SessionState.Playing || (Session.Client != null && Session.Client.Dead);
            if (Session.Input.UsingGamepad && needsCursor)
                Fill(new Rect(_padCursor.x - 1f, _padCursor.y - 1f, 3f, 3f), Color.white);
        }

        public void Toast(string message)
        {
            _toast = message;
            _toastTime = 3f;
        }

        private void DrawToast()
        {
            if (_toastTime <= 0f) return;
            _toastTime -= Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(_toastTime);
            Text(new Rect(0f, _vh * 0.2f, _vw, 14f), _toast, _label, new Color(1f, 1f, 1f, a), TextAnchor.MiddleCenter);
        }

        // ------------------------------------------------------------------ HUD

        private void DrawWorldOverlays(GameClient c)
        {
            if (c.Player.HeadInWater) Fill(new Rect(0, 0, _vw, _vh), new Color(0.1f, 0.25f, 0.6f, 0.35f));
            if (c.Player.InLava) Fill(new Rect(0, 0, _vw, _vh), new Color(0.9f, 0.3f, 0.05f, 0.5f));
            if (_damageFlash > 0f) Fill(new Rect(0, 0, _vw, _vh), new Color(0.8f, 0.05f, 0.05f, Mathf.Clamp01(_damageFlash) * 0.45f));
            if (c.Health > 0f && c.Health <= 6f)
            {
                float pulse = 0.1f + 0.08f * Mathf.Sin(Time.unscaledTime * 5f);
                Fill(new Rect(0, 0, _vw, _vh), new Color(0.6f, 0f, 0f, pulse));
            }
        }

        private void DrawHud(GameClient c)
        {
            float cx = _vw * 0.5f;
            if (_screen == UiScreen.None && !c.Dead)
                Tex(new Rect(cx - 7.5f, _vh * 0.5f - 7.5f, 15f, 15f), GameAssets.Ui("crosshair"));

            // Hotbar
            float hx = cx - 91f, hy = _vh - 24f;
            for (int i = 0; i < 9; i++)
            {
                Rect slot = new Rect(hx + i * 20f + 1f, hy + 2f, 18f, 18f);
                Tex(slot, GameAssets.Ui("slot"));
                DrawStack(new Rect(slot.x + 1f, slot.y + 1f, 16f, 16f), c.Slots[i]);
            }
            Tex(new Rect(hx + c.Selected * 20f - 2f, hy - 1f, 24f, 24f), GameAssets.Ui("slot_selected"));

            // Held item name pops up briefly.
            if (_selectedNameTime > 0f && !c.Held.IsEmpty)
            {
                float a = Mathf.Clamp01(_selectedNameTime);
                Text(new Rect(0f, hy - 38f, _vw, 10f), c.Held.Info.Name, _label, new Color(1f, 1f, 1f, a), TextAnchor.MiddleCenter);
            }

            if (c.Mode == GameMode.Survival)
            {
                // Hearts on the left, hunger on the right.
                float by = hy - 12f;
                for (int i = 0; i < 10; i++)
                {
                    float hp = c.Health - i * 2f;
                    string state = hp >= 2f ? "full" : (hp >= 1f ? "half" : "empty");
                    float jitter = c.Health <= 4f ? Mathf.Sin(Time.unscaledTime * 30f + i) * 0.6f : 0f;
                    Tex(new Rect(hx + i * 8f + 1f, by + jitter, 9f, 9f), GameAssets.Ui("heart_empty"));
                    if (state != "empty") Tex(new Rect(hx + i * 8f + 1f, by + jitter, 9f, 9f), GameAssets.Ui("heart_" + state));
                }
                for (int i = 0; i < 10; i++)
                {
                    float food = c.Hunger - i * 2f;
                    string state = food >= 2f ? "full" : (food >= 1f ? "half" : "empty");
                    Tex(new Rect(hx + 182f - (i + 1) * 8f - 1f, by, 9f, 9f), GameAssets.Ui("food_" + state));
                }
                if (c.Player.HeadInWater || _airTimer < 15f)
                {
                    int bubbles = Mathf.CeilToInt(_airTimer / 15f * 10f);
                    for (int i = 0; i < bubbles; i++)
                        Tex(new Rect(hx + 182f - (i + 1) * 8f - 1f, by - 10f, 9f, 9f), GameAssets.Ui("bubble"));
                }
            }
            else
            {
                Text(new Rect(hx, hy - 12f, 182f, 10f), c.Player.Flying ? "Creative: flying" : "Creative", _small, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter);
            }

            DrawBossBar(c);
            if (c.Player.Riding)
            {
                Text(new Rect(0f, hy - 50f, _vw, 10f), "Riding - W/S speed, A/D turn, Space climb, Shift dive, LMB sprint-fire off - Shift twice to dismount",
                     _small, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter);
            }
            if (GameSettings.ShowFps) Text(new Rect(3f, 3f, 80f, 10f), Mathf.RoundToInt(_fps) + " fps", _small, Color.white);
        }

        private void DrawBossBar(GameClient c)
        {
            RemoteEntity best = null;
            float bestDist = 140f * 140f;
            Vec3 me = c.Player.Pos;
            foreach (KeyValuePair<int, RemoteEntity> kv in c.Entities)
            {
                RemoteEntity e = kv.Value;
                if (e.Type != EntityType.Dragon || (e.Flags & EntityFlags.Tamed) != 0) continue;
                float d = Vec3.DistanceSq(e.RenderPos, me);
                if (d < bestDist) { bestDist = d; best = e; }
            }
            if (best == null) return;
            float w = 182f;
            float x = _vw * 0.5f - w * 0.5f, y = 8f;
            Text(new Rect(x, y, w, 10f), DragonStats.Names[Mathf.Clamp(best.Variant, 0, 2)], _label, new Color(1f, 0.85f, 0.6f), TextAnchor.MiddleCenter);
            Fill(new Rect(x, y + 11f, w, 5f), new Color(0f, 0f, 0f, 0.6f));
            Fill(new Rect(x + 1f, y + 12f, (w - 2f) * best.HealthPercent / 100f, 3f), new Color(0.85f, 0.15f, 0.15f, 1f));
        }

        private void DrawLabels(GameClient c)
        {
            if (Session.Entities == null || Session.Cam == null) return;
            foreach (EntityView v in Session.Entities.Labelled)
            {
                if (v.Entity.Id == c.PlayerId) continue;
                Vector3 sp = Session.Cam.WorldToScreenPoint(v.LabelPos);
                if (sp.z <= 0.5f || sp.z > 60f) continue;
                float x = sp.x / _scale, y = (Screen.height - sp.y) / _scale;
                Text(new Rect(x - 60f, y - 5f, 120f, 10f), v.Label, _small, new Color(1f, 1f, 1f, 0.95f), TextAnchor.MiddleCenter);
            }
        }

        // ------------------------------------------------------------------ chat

        private void DrawChat(GameClient c)
        {
            // Track when each message arrived so they fade out.
            while (_chatTimes.Count < c.Chat.Count) _chatTimes.Add(Time.unscaledTime);
            while (_chatTimes.Count > c.Chat.Count) _chatTimes.RemoveAt(0);
            bool open = _screen == UiScreen.Chat;
            float y = _vh - 52f;
            int shown = 0;
            for (int i = c.Chat.Count - 1; i >= 0 && shown < (open ? 14 : 7); i--)
            {
                float age = Time.unscaledTime - _chatTimes[i];
                float a = open ? 1f : Mathf.Clamp01((8f - age) / 2f);
                if (a <= 0f) break;
                Rect r = new Rect(4f, y - shown * 9f, _vw * 0.6f, 9f);
                Fill(new Rect(r.x - 1f, r.y, Mathf.Min(r.width, 6f + c.Chat[i].Length * 4.2f), 9f), new Color(0f, 0f, 0f, 0.35f * a));
                Text(r, c.Chat[i], _small, new Color(1f, 1f, 1f, a));
                shown++;
            }
            if (!open) return;
            Rect field = new Rect(4f, _vh - 14f, _vw * 0.6f, 11f);
            Event e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                string text = _chatInput.Trim();
                if (text.Length > 0) c.SendChat(text);
                _chatInput = "";
                _screen = UiScreen.None;
                e.Use();
                return;
            }
            GUI.SetNextControlName("chat");
            _chatInput = GUI.TextField(field, _chatInput, 160, _field);
            if (_chatFocus) { GUI.FocusControl("chat"); _chatFocus = false; }
        }

        // ------------------------------------------------------------------ debug and death

        private void DrawDebug(GameClient c)
        {
            Vec3 p = c.Player.Pos;
            int bx = Mathf.FloorToInt(p.X), by = Mathf.FloorToInt(p.Y), bz = Mathf.FloorToInt(p.Z);
            string facing = Mathf.Repeat(Session.Input.Yaw * Mathf.Rad2Deg, 360f).ToString("0");
            WorldRenderer wr = Session.Renderer;
            string[] lines =
            {
                "BlockRealm  " + Mathf.RoundToInt(_fps) + " fps",
                "XYZ " + p.X.ToString("0.00") + " / " + p.Y.ToString("0.00") + " / " + p.Z.ToString("0.00"),
                "Block " + bx + " " + by + " " + bz + "   chunk " + (bx >> 4) + " " + (bz >> 4) + "   yaw " + facing,
                "Light sky " + c.World.GetSkyLight(bx, by, bz) + "  block " + c.World.GetBlockLightAt(bx, by, bz) + "   day tick " + c.DayTick,
                "Chunks " + c.World.ColumnCount + "  sections " + (wr != null ? wr.ActiveSections : 0) + "  pending " + (wr != null ? wr.PendingSections : 0) +
                    "  tris " + (wr != null ? wr.Triangles / 1000 : 0) + "k",
                "Entities " + c.Entities.Count + "  packets " + c.PacketsReceived + "  columns recv " + c.ColumnsReceived,
                "Server " + c.ServerName + "  mode " + c.Mode + (Session.Server != null ? "  (hosting, " + Session.Server.Players.Count + " players)" : ""),
            };
            for (int i = 0; i < lines.Length; i++)
            {
                Fill(new Rect(3f, 12f + i * 9f, 4f + lines[i].Length * 3.9f, 9f), new Color(0f, 0f, 0f, 0.4f));
                Text(new Rect(4f, 12f + i * 9f, 400f, 9f), lines[i], _small, Color.white);
            }
        }

        private void DrawDeath(GameClient c)
        {
            Fill(new Rect(0, 0, _vw, _vh), new Color(0.45f, 0f, 0f, 0.55f));
            Text(new Rect(0f, _vh * 0.3f, _vw, 30f), "You died!", _title, Color.white, TextAnchor.MiddleCenter);
            Rect b = new Rect(_vw * 0.5f - 60f, _vh * 0.5f, 120f, 18f);
            if (Btn(b, "Respawn")) c.RequestRespawn();
            Rect q = new Rect(_vw * 0.5f - 60f, _vh * 0.5f + 24f, 120f, 18f);
            if (Btn(q, "Title screen")) { Session.Leave(); _screen = UiScreen.None; }
        }

        public void NotifyHurt() { _damageFlash = 1f; }
    }
}
