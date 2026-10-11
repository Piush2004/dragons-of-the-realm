using System;
using System.Collections.Generic;
using UnityEngine;
using BlockRealm.Net;
using BlockRealm.Sim;

namespace BlockRealm.Game
{
    public sealed partial class GameUi
    {
        private enum MenuPage { Main, Single, NewWorld, Multi, Host, Settings }

        private MenuPage _page = MenuPage.Main;
        private List<WorldInfo> _worlds = new List<WorldInfo>();
        private string _newName = "New World";
        private string _newSeed = "";
        private bool _newCreative;
        private string _joinAddress;
        private string _playerName;
        private string _adminToken;
        private string _tokenFor = "";                   // the address the token field currently belongs to
        private string _hostPort = "25565";
        private int _paletteTab;
        private string _tooltip = "";
        private List<ushort> _palette;

        // ------------------------------------------------------------------ item drawing

        private void DrawStack(Rect r, ItemStack s)
        {
            if (s.IsEmpty) return;
            Texture2D icon = GameAssets.Icon(s.Id);
            if (icon != null) Tex(r, icon);
            if (s.Count > 1)
            {
                string n = s.Count.ToString();
                Text(new Rect(r.x, r.y + 8f, r.width + 1f, 9f), n, _small, Color.white, TextAnchor.LowerRight);
            }
            ItemInfo info = s.Info;
            if (info.Durability > 0 && s.Wear > 0)
            {
                float f = 1f - (float)s.Wear / info.Durability;
                Fill(new Rect(r.x + 1f, r.y + r.height - 2.5f, r.width - 2f, 2f), Color.black);
                Fill(new Rect(r.x + 1f, r.y + r.height - 2.5f, (r.width - 2f) * f, 1f), Color.Lerp(Color.red, Color.green, f));
            }
        }

        private void DrawSlot(GameClient c, Rect r, int index, bool interactive = true)
        {
            Tex(r, GameAssets.Ui("slot"));
            ItemStack s = index >= 0 && index < c.WindowItems.Length ? c.WindowItems[index] : ItemStack.Empty;
            // Items the player holds may be mirrored in Slots before the window refresh arrives.
            if (index >= 0 && index < PlayerInventory.Size && index < c.Slots.Length) s = c.Slots[index];
            Rect inner = new Rect(r.x + 1f, r.y + 1f, 16f, 16f);
            DrawStack(inner, s);
            if (!interactive) return;
            if (Hover(r))
            {
                Fill(inner, new Color(1f, 1f, 1f, 0.35f));
                if (!s.IsEmpty) _tooltip = s.Info.Name;
                if (ClickedLeft(r) || (_padShift && Session.Input.UsingGamepad && Hover(r)))
                {
                    bool shift = Event.current.shift || (_padShift && Session.Input.UsingGamepad);
                    c.ClickSlot(index, 0, shift);
                    _padClick = false; _padShift = false;
                    if (Event.current != null) Event.current.Use();
                }
                else if (ClickedRight(r))
                {
                    c.ClickSlot(index, 1, false);
                    _padRightClick = false;
                    if (Event.current != null) Event.current.Use();
                }
            }
        }

        // ------------------------------------------------------------------ inventory windows

        private void DrawWindow(GameClient c)
        {
            Fill(new Rect(0, 0, _vw, _vh), new Color(0f, 0f, 0f, 0.55f));
            _tooltip = "";
            WindowKind kind = _screen == UiScreen.Container ? c.OpenWindowKind : WindowKind.Inventory;
            float pw = 176f;
            float ph = kind == WindowKind.Chest ? 168f : 166f;
            float ox = Mathf.Round((_vw - pw) * 0.5f);
            bool creative = c.Mode == GameMode.Creative && kind == WindowKind.Inventory;
            float oy = Mathf.Round((_vh - ph) * 0.5f) + (creative ? 52f : 0f);
            if (creative && oy + ph > _vh - 2f) oy = _vh - ph - 2f;

            Rect panel = new Rect(ox, oy, pw, ph);
            DrawPanel(panel);

            string title = kind == WindowKind.Inventory ? "Crafting" : (kind == WindowKind.CraftingTable ? "Crafting Table" : (kind == WindowKind.Furnace ? "Furnace" : "Chest"));
            // The personal window carries the armour column on its left, so its title moves over.
            float titleX = kind == WindowKind.Inventory ? 98f : 8f;
            Text(new Rect(ox + titleX, oy + 5f, 150f, 10f), title, _label, new Color(0.25f, 0.25f, 0.25f), TextAnchor.UpperLeft, false);

            float invY = kind == WindowKind.Chest ? 86f : 84f;
            float barY = invY + 58f;
            if (kind != WindowKind.Inventory)
                Text(new Rect(ox + 8f, oy + invY - 11f, 150f, 10f), "Inventory", _label, new Color(0.25f, 0.25f, 0.25f), TextAnchor.UpperLeft, false);
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 9; col++)
                    DrawSlot(c, new Rect(ox + 8f + col * 18f, oy + invY + row * 18f, 18f, 18f), 9 + row * 9 + col);
            for (int col = 0; col < 9; col++)
                DrawSlot(c, new Rect(ox + 8f + col * 18f, oy + barY, 18f, 18f), col);

            int extra = PlayerInventory.Size;
            switch (kind)
            {
                case WindowKind.Inventory:
                    for (int i = 0; i < 4; i++)
                        DrawSlot(c, new Rect(ox + 98f + (i % 2) * 18f, oy + 18f + (i / 2) * 18f, 18f, 18f), extra + i);
                    DrawArrow(ox + 125f, oy + 31f);
                    DrawSlot(c, new Rect(ox + 154f, oy + 28f, 18f, 18f), extra + 4);
                    for (int i = 0; i < 4; i++)                     // head, chest, legs, feet
                        DrawSlot(c, new Rect(ox + 8f, oy + 8f + i * 18f, 18f, 18f), extra + 5 + i);
                    DrawPlayerPreview(ox + 52f, oy + 14f);
                    break;
                case WindowKind.CraftingTable:
                    for (int i = 0; i < 9; i++)
                        DrawSlot(c, new Rect(ox + 30f + (i % 3) * 18f, oy + 17f + (i / 3) * 18f, 18f, 18f), extra + i);
                    DrawArrow(ox + 92f, oy + 36f);
                    DrawSlot(c, new Rect(ox + 124f, oy + 35f, 18f, 18f), extra + 9);
                    break;
                case WindowKind.Furnace:
                    DrawSlot(c, new Rect(ox + 56f, oy + 17f, 18f, 18f), extra + 0);
                    DrawSlot(c, new Rect(ox + 56f, oy + 53f, 18f, 18f), extra + 1);
                    DrawSlot(c, new Rect(ox + 116f, oy + 35f, 18f, 18f), extra + 2);
                    DrawFurnaceGauges(c, ox, oy);
                    break;
                case WindowKind.Chest:
                    for (int i = 0; i < ChestState.Size; i++)
                        DrawSlot(c, new Rect(ox + 8f + (i % 9) * 18f, oy + 18f + (i / 9) * 18f, 18f, 18f), extra + i);
                    break;
            }

            if (creative) DrawPalette(c, ox, oy - 52f, pw);

            // The stack on the cursor follows the pointer; clicking outside the panel throws it away.
            Vector2 m = Session.Input.UsingGamepad ? _padCursor : _mouse;
            if (!c.Cursor.IsEmpty)
            {
                DrawStack(new Rect(m.x - 8f, m.y - 8f, 16f, 16f), c.Cursor);
                bool outside = !panel.Contains(m) && !(creative && new Rect(ox, oy - 52f, pw, 48f).Contains(m));
                if (outside)
                {
                    bool left = Session.Input.UsingGamepad ? _padClick : (Event.current.type == EventType.MouseDown && Event.current.button == 0);
                    bool right = Session.Input.UsingGamepad ? _padRightClick : (Event.current.type == EventType.MouseDown && Event.current.button == 1);
                    if (left) { c.DropCursor(false); if (Event.current != null) Event.current.Use(); }
                    else if (right) { c.DropCursor(true); if (Event.current != null) Event.current.Use(); }
                }
            }
            else if (_tooltip.Length > 0)
            {
                Rect tr = new Rect(m.x + 8f, m.y - 4f, 8f + _tooltip.Length * 4.2f, 11f);
                Fill(tr, new Color(0.1f, 0f, 0.15f, 0.9f));
                Text(new Rect(tr.x + 3f, tr.y + 1f, tr.width, 10f), _tooltip, _small, Color.white);
            }
        }

        private void DrawPanel(Rect r)
        {
            Texture2D t = GameAssets.Ui("panel");
            // Nine-slice by hand: 4 px corners and edges stretched.
            float b = 4f;
            Tex(new Rect(r.x + b, r.y + b, r.width - 2 * b, r.height - 2 * b), t);
            Rect c = new Rect(0, 0, 1, 1);
            GUI.DrawTextureWithTexCoords(new Rect(r.x, r.y, b, b), t, new Rect(0f, 0.75f, 0.25f, 0.25f));
            GUI.DrawTextureWithTexCoords(new Rect(r.xMax - b, r.y, b, b), t, new Rect(0.75f, 0.75f, 0.25f, 0.25f));
            GUI.DrawTextureWithTexCoords(new Rect(r.x, r.yMax - b, b, b), t, new Rect(0f, 0f, 0.25f, 0.25f));
            GUI.DrawTextureWithTexCoords(new Rect(r.xMax - b, r.yMax - b, b, b), t, new Rect(0.75f, 0f, 0.25f, 0.25f));
            GUI.DrawTextureWithTexCoords(new Rect(r.x + b, r.y, r.width - 2 * b, b), t, new Rect(0.25f, 0.75f, 0.5f, 0.25f));
            GUI.DrawTextureWithTexCoords(new Rect(r.x + b, r.yMax - b, r.width - 2 * b, b), t, new Rect(0.25f, 0f, 0.5f, 0.25f));
            GUI.DrawTextureWithTexCoords(new Rect(r.x, r.y + b, b, r.height - 2 * b), t, new Rect(0f, 0.25f, 0.25f, 0.5f));
            GUI.DrawTextureWithTexCoords(new Rect(r.xMax - b, r.y + b, b, r.height - 2 * b), t, new Rect(0.75f, 0.25f, 0.25f, 0.5f));
            if (c.width < 0) return;
        }

        private void DrawArrow(float x, float y)
        {
            Fill(new Rect(x, y + 3f, 18f, 6f), new Color(0.55f, 0.55f, 0.55f, 1f));
            Fill(new Rect(x + 14f, y, 5f, 12f), new Color(0.55f, 0.55f, 0.55f, 1f));
        }

        private void DrawFurnaceGauges(GameClient c, float ox, float oy)
        {
            // Flame between input and fuel, arrow toward the output.
            Rect flame = new Rect(ox + 57f, oy + 37f, 14f, 14f);
            Fill(flame, new Color(0.3f, 0.3f, 0.3f, 1f));
            if (c.FurnaceBurnTotal > 0 && c.FurnaceBurnLeft > 0)
            {
                float f = Mathf.Clamp01((float)c.FurnaceBurnLeft / c.FurnaceBurnTotal);
                Fill(new Rect(flame.x, flame.yMax - 14f * f, 14f, 14f * f), new Color(1f, 0.65f, 0.15f, 1f));
            }
            Rect arrow = new Rect(ox + 80f, oy + 36f, 24f, 12f);
            Fill(arrow, new Color(0.3f, 0.3f, 0.3f, 1f));
            float p = c.FurnaceCookTotal > 0 ? Mathf.Clamp01((float)c.FurnaceProgress / c.FurnaceCookTotal) : 0f;
            Fill(new Rect(arrow.x, arrow.y, arrow.width * p, arrow.height), new Color(0.95f, 0.95f, 0.95f, 1f));
        }

        private void DrawPlayerPreview(float x, float y)
        {
            // A simple framed portrait slot: the real model is shown in third person with F5.
            Fill(new Rect(x, y, 40f, 56f), new Color(0.1f, 0.1f, 0.12f, 1f));
            Text(new Rect(x, y + 18f, 40f, 20f), Session.Client != null ? Session.Client.PlayerName : "", _small, Color.white, TextAnchor.MiddleCenter, false);
        }

        private void BuildPalette()
        {
            _palette = new List<ushort>();
            for (int i = 1; i < 256; i++)
            {
                BlockInfo b = Blocks.All[i];
                if (b != null && b.Render != RenderKind.None) _palette.Add((ushort)i);
            }
            for (int i = 256; i <= ItemIds.MaxId; i++) if (Items.IsDefined((ushort)i)) _palette.Add((ushort)i);
        }

        private void DrawPalette(GameClient c, float ox, float oy, float pw)
        {
            if (_palette == null) BuildPalette();
            Rect panel = new Rect(ox, oy, pw, 48f);
            DrawPanel(panel);
            const int perPage = 18;
            int pages = (_palette.Count + perPage - 1) / perPage;
            _paletteTab = Mathf.Clamp(_paletteTab, 0, pages - 1);
            for (int i = 0; i < perPage; i++)
            {
                int idx = _paletteTab * perPage + i;
                if (idx >= _palette.Count) break;
                Rect r = new Rect(ox + 8f + (i % 9) * 18f, oy + 5f + (i / 9) * 18f, 18f, 18f);
                Tex(r, GameAssets.Ui("slot"));
                ushort id = _palette[idx];
                Rect inner = new Rect(r.x + 1f, r.y + 1f, 16f, 16f);
                Tex(inner, GameAssets.Icon(id));
                if (Hover(r))
                {
                    Fill(inner, new Color(1f, 1f, 1f, 0.35f));
                    _tooltip = id < 256 ? Blocks.Get((byte)id).Name : Items.Get(id).Name;
                    if (ClickedLeft(r)) { c.CreativePick(id, false); _padClick = false; if (Event.current != null) Event.current.Use(); }
                    else if (ClickedRight(r)) { c.CreativePick(id, true); _padRightClick = false; if (Event.current != null) Event.current.Use(); }
                }
            }
            if (pages > 1)
            {
                if (Btn(new Rect(ox + pw - 36f, oy + 38f, 16f, 8f), "<")) _paletteTab = (_paletteTab + pages - 1) % pages;
                if (Btn(new Rect(ox + pw - 18f, oy + 38f, 16f, 8f), ">")) _paletteTab = (_paletteTab + 1) % pages;
            }
        }

        // ------------------------------------------------------------------ menus

        private void MenuBackground()
        {
            Texture2D dirt = GameAssets.Load("Tiles/dirt");
            dirt.wrapMode = TextureWrapMode.Repeat;
            Color old = GUI.color;
            GUI.color = new Color(0.32f, 0.32f, 0.36f, 1f);
            GUI.DrawTextureWithTexCoords(new Rect(0, 0, _vw, _vh), dirt, new Rect(0f, 0f, _vw / 16f, _vh / 16f));
            GUI.color = old;
        }

        private void DrawConnecting()
        {
            MenuBackground();
            Text(new Rect(0f, _vh * 0.4f, _vw, 20f), Session.StatusMessage.Length > 0 ? Session.StatusMessage : "Loading world...", _label, Color.white, TextAnchor.MiddleCenter);
            if (Session.Client != null && Session.Client.World != null)
                Text(new Rect(0f, _vh * 0.4f + 14f, _vw, 12f), "Chunks received: " + Session.Client.World.ColumnCount, _small, new Color(0.8f, 0.8f, 0.8f), TextAnchor.MiddleCenter);
            if (Btn(new Rect(_vw * 0.5f - 50f, _vh * 0.4f + 34f, 100f, 16f), "Cancel")) Session.Leave();
        }

        private void DrawMenus()
        {
            if (Session.State == SessionState.Connecting) { DrawConnecting(); return; }
            if (!_menuInit)
            {
                _menuInit = true;
                _joinAddress = GameSettings.LastHost;
                _playerName = GameSettings.PlayerName;
                _adminToken = "";
                _tokenFor = "";
                _worlds = GameSession.ListWorlds();
                _newSeed = UnityEngine.Random.Range(1, 999999999).ToString();
            }
            MenuBackground();
            Text(new Rect(0f, 16f, _vw, 26f), "BlockRealm", _title, new Color(1f, 0.95f, 0.8f), TextAnchor.MiddleCenter);
            Text(new Rect(0f, 40f, _vw, 10f), "Build, survive, tame dragons - together", _small, new Color(0.85f, 0.85f, 0.9f), TextAnchor.MiddleCenter);

            if (Session.ErrorMessage.Length > 0)
                Text(new Rect(10f, _vh - 24f, _vw - 20f, 12f), Session.ErrorMessage, _label, new Color(1f, 0.45f, 0.4f), TextAnchor.MiddleCenter);

            float cx = _vw * 0.5f;
            switch (_page)
            {
                case MenuPage.Main:
                    Text(new Rect(cx - 100f, 62f, 200f, 10f), "Player name", _small, new Color(0.9f, 0.9f, 0.9f), TextAnchor.UpperLeft);
                    _playerName = GUI.TextField(new Rect(cx - 100f, 72f, 200f, 12f), _playerName, 16, _field);
                    if (Btn(new Rect(cx - 100f, 94f, 200f, 18f), "Singleplayer")) { GameSettings.PlayerName = Clean(_playerName); _worlds = GameSession.ListWorlds(); _page = MenuPage.Single; }
                    if (Btn(new Rect(cx - 100f, 116f, 200f, 18f), "Multiplayer")) { GameSettings.PlayerName = Clean(_playerName); _page = MenuPage.Multi; }
                    if (Btn(new Rect(cx - 100f, 138f, 200f, 18f), "Settings")) _page = MenuPage.Settings;
                    if (Btn(new Rect(cx - 100f, 160f, 200f, 18f), "Quit")) Application.Quit();
                    break;

                case MenuPage.Single:
                    DrawWorldList(cx, false);
                    break;

                case MenuPage.NewWorld:
                    DrawNewWorld(cx);
                    break;

                case MenuPage.Multi:
                    DrawMultiplayer(cx);
                    break;

                case MenuPage.Host:
                    DrawWorldList(cx, true);
                    break;

                case MenuPage.Settings:
                    DrawSettings(false);
                    break;
            }
        }

        private static string Clean(string raw)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char ch in raw ?? "")
            {
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '-';
                if (ok) sb.Append(ch);
                if (sb.Length >= 16) break;
            }
            return sb.Length == 0 ? "Player" : sb.ToString();
        }

        private void DrawWorldList(float cx, bool hosting)
        {
            Text(new Rect(0f, 58f, _vw, 12f), hosting ? "Host a world on your network" : "Select a world", _label, Color.white, TextAnchor.MiddleCenter);
            float y = 74f;
            int shown = 0;
            foreach (WorldInfo w in _worlds)
            {
                if (shown++ >= 6) break;
                Rect row = new Rect(cx - 130f, y, 260f, 18f);
                Fill(row, new Color(0f, 0f, 0f, 0.35f));
                Text(new Rect(row.x + 4f, row.y + 3f, 150f, 12f), w.Name, _label, Color.white);
                Text(new Rect(row.x + 120f, row.y + 5f, 70f, 10f), w.LastPlayed.ToString("yyyy-MM-dd"), _small, new Color(0.7f, 0.7f, 0.7f));
                if (Btn(new Rect(row.xMax - 66f, row.y + 1f, 40f, 16f), "Play"))
                {
                    long seed = w.Seed != 0 ? w.Seed : 2024;
                    bool creative = false;
                    if (hosting) Session.StartHost(w.Name, seed, creative, ParsePort());
                    else Session.StartSinglePlayer(w.Name, seed, creative);
                }
                if (Btn(new Rect(row.xMax - 24f, row.y + 1f, 22f, 16f), "X"))
                {
                    GameSession.DeleteWorld(w);
                    _worlds = GameSession.ListWorlds();
                    break;
                }
                y += 21f;
            }
            if (_worlds.Count == 0) Text(new Rect(0f, y, _vw, 12f), "No saved worlds yet", _small, new Color(0.8f, 0.8f, 0.8f), TextAnchor.MiddleCenter);
            if (hosting)
            {
                Text(new Rect(cx - 130f, _vh - 62f, 70f, 10f), "Port", _small, Color.white);
                _hostPort = GUI.TextField(new Rect(cx - 100f, _vh - 64f, 50f, 12f), _hostPort, 5, _field);
            }
            if (Btn(new Rect(cx - 130f, _vh - 44f, 126f, 18f), "Create new world")) { _newName = "World " + (_worlds.Count + 1); _newHosting = hosting; _page = MenuPage.NewWorld; }
            if (Btn(new Rect(cx + 4f, _vh - 44f, 126f, 18f), "Back")) _page = hosting ? MenuPage.Multi : MenuPage.Main;
        }

        private int ParsePort()
        {
            int p;
            return int.TryParse(_hostPort, out p) && p > 1023 && p < 65536 ? p : 25565;
        }

        private bool _newHosting;

        private void DrawNewWorld(float cx)
        {
            Text(new Rect(0f, 58f, _vw, 12f), "Create a new world", _label, Color.white, TextAnchor.MiddleCenter);
            Text(new Rect(cx - 100f, 76f, 200f, 10f), "World name", _small, Color.white);
            _newName = GUI.TextField(new Rect(cx - 100f, 86f, 200f, 12f), _newName, 24, _field);
            Text(new Rect(cx - 100f, 104f, 200f, 10f), "Seed (number)", _small, Color.white);
            _newSeed = GUI.TextField(new Rect(cx - 100f, 114f, 200f, 12f), _newSeed, 12, _field);
            _newCreative = GUI.Toggle(new Rect(cx - 100f, 132f, 200f, 12f), _newCreative, "  Creative mode (unlimited blocks, flying)");
            if (Btn(new Rect(cx - 100f, 154f, 98f, 18f), "Create"))
            {
                long seed;
                if (!long.TryParse(_newSeed, out seed)) seed = _newSeed.GetHashCode();
                if (seed == 0) seed = 1;
                bool host = _newHosting;
                if (host) Session.StartHost(_newName, seed, _newCreative, ParsePort());
                else Session.StartSinglePlayer(_newName, seed, _newCreative);
            }
            if (Btn(new Rect(cx + 2f, 154f, 98f, 18f), "Cancel")) _page = _newHosting ? MenuPage.Host : MenuPage.Single;
        }

        private void DrawMultiplayer(float cx)
        {
            Text(new Rect(0f, 58f, _vw, 12f), "Multiplayer", _label, Color.white, TextAnchor.MiddleCenter);
            Text(new Rect(cx - 110f, 76f, 220f, 10f), "Server address (host:port)", _small, Color.white);
            _joinAddress = GUI.TextField(new Rect(cx - 110f, 86f, 220f, 12f), _joinAddress, 64, _field);
            // The token field always belongs to the address above it: change the address and the field switches to what
            // was saved for that address (or empties), so a token typed for one server is never offered to another.
            string normalised = AuthCrypto.NormaliseAddress(_joinAddress);
            if (normalised != _tokenFor)
            {
                _tokenFor = normalised;
                _adminToken = GameSettings.AdminTokenFor(_joinAddress);
            }
            Text(new Rect(cx - 110f, 104f, 220f, 10f), "Admin token for this server (optional, only for its owner)", _small, new Color(0.8f, 0.8f, 0.8f));
            _adminToken = GUI.PasswordField(new Rect(cx - 110f, 114f, 220f, 12f), _adminToken, '*', 128, _field);
            if (Btn(new Rect(cx - 110f, 134f, 220f, 18f), "Join server"))
            {
                GameSettings.SetAdminTokenFor(_joinAddress, _adminToken);
                Session.Join(_joinAddress);
            }
            if (Btn(new Rect(cx - 110f, 156f, 220f, 18f), "Host a world on this network")) { _page = MenuPage.Host; _worlds = GameSession.ListWorlds(); }
            if (Btn(new Rect(cx - 110f, 178f, 220f, 18f), "Back")) _page = MenuPage.Main;
        }

        // ------------------------------------------------------------------ pause and settings

        private void DrawPause(GameClient c)
        {
            Fill(new Rect(0, 0, _vw, _vh), new Color(0f, 0f, 0f, 0.5f));
            float cx = _vw * 0.5f;
            Text(new Rect(0f, _vh * 0.18f, _vw, 20f), "Game menu", _title, Color.white, TextAnchor.MiddleCenter);
            if (Session.IsHost)
                Text(new Rect(0f, _vh * 0.18f + 22f, _vw, 10f), "Players can join at  " + Session.HostedAddress, _small, new Color(0.8f, 1f, 0.8f), TextAnchor.MiddleCenter);
            float y = _vh * 0.3f;
            if (Btn(new Rect(cx - 80f, y, 160f, 18f), "Back to game")) _screen = UiScreen.None;
            if (Btn(new Rect(cx - 80f, y + 22f, 160f, 18f), "Settings")) _screen = UiScreen.Settings;
            if (Btn(new Rect(cx - 80f, y + 44f, 160f, 18f), "Save and quit to title"))
            {
                GameSettings.Save();
                Session.Leave();
                _screen = UiScreen.None;
                _page = MenuPage.Main;
                _worlds = GameSession.ListWorlds();
            }
        }

        private void DrawSettings(bool inGame)
        {
            if (inGame) Fill(new Rect(0, 0, _vw, _vh), new Color(0f, 0f, 0f, 0.6f)); else Fill(new Rect(0, 0, _vw, _vh), new Color(0f, 0f, 0f, 0.25f));
            float cx = _vw * 0.5f;
            Text(new Rect(0f, 20f, _vw, 18f), "Settings", _title, Color.white, TextAnchor.MiddleCenter);
            float y = 52f;
            GameSettings.ViewDistance = Mathf.RoundToInt(Slider(cx - 100f, ref y, "View distance: " + GameSettings.ViewDistance + " chunks", GameSettings.ViewDistance, 4f, 20f));
            GameSettings.Fov = Mathf.Round(Slider(cx - 100f, ref y, "Field of view: " + Mathf.RoundToInt(GameSettings.Fov), GameSettings.Fov, 55f, 100f));
            GameSettings.MouseSensitivity = Slider(cx - 100f, ref y, "Mouse sensitivity: " + GameSettings.MouseSensitivity.ToString("0.0"), GameSettings.MouseSensitivity, 0.2f, 3f);
            GameSettings.StickSensitivity = Slider(cx - 100f, ref y, "Gamepad sensitivity: " + GameSettings.StickSensitivity.ToString("0.0"), GameSettings.StickSensitivity, 0.3f, 2.5f);
            GameSettings.Volume = Slider(cx - 100f, ref y, "Volume: " + Mathf.RoundToInt(GameSettings.Volume * 100f) + "%", GameSettings.Volume, 0f, 1f);
            bool shadows = GUI.Toggle(new Rect(cx - 100f, y, 200f, 12f), GameSettings.Shadows, "  Shadows");
            y += 14f;
            bool invert = GUI.Toggle(new Rect(cx - 100f, y, 200f, 12f), GameSettings.InvertY, "  Invert mouse Y");
            y += 14f;
            bool fps = GUI.Toggle(new Rect(cx - 100f, y, 200f, 12f), GameSettings.ShowFps, "  Show FPS");
            y += 18f;
            if (shadows != GameSettings.Shadows) { GameSettings.Shadows = shadows; ApplyQuality(); }
            GameSettings.InvertY = invert;
            GameSettings.ShowFps = fps;
            ApplyView();
            if (Btn(new Rect(cx - 80f, y + 4f, 160f, 18f), "Done"))
            {
                GameSettings.Save();
                if (inGame) _screen = UiScreen.Pause; else _page = MenuPage.Main;
            }
        }

        private float Slider(float x, ref float y, string label, float value, float min, float max)
        {
            Text(new Rect(x, y, 200f, 10f), label, _small, Color.white);
            float v = GUI.HorizontalSlider(new Rect(x, y + 10f, 200f, 10f), value, min, max);
            y += 24f;
            return v;
        }

        private void ApplyView()
        {
            if (Session != null && Session.Sky != null) Session.Sky.ApplyView(GameSettings.ViewDistance);
            // The server streams to its own view distance; hosted worlds adopt the new value at once.
            if (Session != null && Session.Server != null) Session.Server.Options.ViewDistance = Mathf.Clamp(GameSettings.ViewDistance, 4, 16);
        }

        private void ApplyQuality()
        {
            QualitySettings.shadows = GameSettings.Shadows ? ShadowQuality.All : ShadowQuality.Disable;
            if (Session != null && Session.Sky != null && Session.Sky.Sun != null)
                Session.Sky.Sun.shadows = GameSettings.Shadows ? LightShadows.Soft : LightShadows.None;
        }
    }
}
