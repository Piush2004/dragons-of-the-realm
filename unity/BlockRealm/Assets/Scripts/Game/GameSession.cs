using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using BlockRealm.Net;
using BlockRealm.Sim;

namespace BlockRealm.Game
{
    public enum SessionState { Menu, Connecting, Playing }

    public sealed class WorldInfo
    {
        public string Name;
        public string Folder;
        public long Seed;
        public DateTime LastPlayed;
    }

    /// <summary>
    /// Owns one game: the embedded server (single player and hosted LAN worlds), the client, and all the views.
    /// Single player is simply a server that only this machine can reach, over an in-process connection, so it runs
    /// exactly the code multiplayer does.
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        public SessionState State = SessionState.Menu;
        public GameServer Server;
        public GameClient Client;
        public string StatusMessage = "";
        public string ErrorMessage = "";
        public string HostedAddress = "";
        public bool IsHost;

        public Camera Cam;
        public WorldRenderer Renderer;
        public EntityViews Entities;
        public PlayerView View;
        public EffectsController Effects;
        public SkyController Sky;
        public AudioBank Audio;
        public InputProvider Input = new InputProvider();

        public bool UiBlocksInput;                 // set by GameUi when a menu, inventory or chat is open
        public Action OnPlayerHurt;                // lets the interface flash the screen
        private float _tickAccumulator;
        private Task<TcpConnection> _connectTask;
        private string _connectAddress;
        private string _connectHost;
        private float _statTimer;

        public static string WorldsRoot
        {
            get { return Path.Combine(Application.persistentDataPath, "worlds"); }
        }

        // ------------------------------------------------------------------ world list

        public static List<WorldInfo> ListWorlds()
        {
            List<WorldInfo> list = new List<WorldInfo>();
            try
            {
                if (!Directory.Exists(WorldsRoot)) return list;
                foreach (string dir in Directory.GetDirectories(WorldsRoot))
                {
                    WorldInfo w = new WorldInfo { Folder = dir, Name = Path.GetFileName(dir) };
                    string level = Path.Combine(dir, "level.dat");
                    w.LastPlayed = Directory.GetLastWriteTime(dir);
                    try
                    {
                        if (File.Exists(level))
                        {
                            using (BinaryReader r = new BinaryReader(File.OpenRead(level)))
                            {
                                r.ReadUInt32();
                                w.Seed = r.ReadInt64();
                            }
                        }
                    }
                    catch (Exception) { }
                    list.Add(w);
                }
                list.Sort((a, b) => b.LastPlayed.CompareTo(a.LastPlayed));
            }
            catch (Exception) { }
            return list;
        }

        public static void DeleteWorld(WorldInfo w)
        {
            try { if (Directory.Exists(w.Folder)) Directory.Delete(w.Folder, true); } catch (Exception) { }
        }

        public static string SafeWorldName(string raw)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char ch in raw ?? "")
            {
                if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_') sb.Append(ch);
                if (sb.Length >= 32) break;
            }
            string name = sb.ToString().Trim();
            return name.Length == 0 ? "World" : name;
        }

        // ------------------------------------------------------------------ starting and stopping

        public void StartSinglePlayer(string worldName, long seed, bool creative)
        {
            Leave();
            ErrorMessage = "";
            string folder = Path.Combine(WorldsRoot, SafeWorldName(worldName));
            Directory.CreateDirectory(folder);
            LaunchServer(folder, seed, creative, false, 0);
            ConnectLocal();
        }

        public void StartHost(string worldName, long seed, bool creative, int port)
        {
            Leave();
            ErrorMessage = "";
            string folder = Path.Combine(WorldsRoot, SafeWorldName(worldName));
            Directory.CreateDirectory(folder);
            LaunchServer(folder, seed, creative, true, port);
            ConnectLocal();
            HostedAddress = DescribeLanAddresses(port);
        }

        private void LaunchServer(string folder, long seed, bool creative, bool listen, int port)
        {
            ServerOptions o = new ServerOptions();
            o.Seed = seed;
            o.Name = "BlockRealm";
            o.ViewDistance = Mathf.Clamp(GameSettings.ViewDistance, 4, 16);
            o.DefaultMode = creative ? GameMode.Creative : GameMode.Survival;
            o.SaveDir = folder;
            o.HostIsOp = true;
            o.MaxPlayers = listen ? 8 : 1;
            // Remote players are never operators unless an admin token is configured; the host is the local player.
            Server = new GameServer(o);
            if (listen)
            {
                try { Server.StartListening(port); IsHost = true; }
                catch (Exception e) { ErrorMessage = "Could not open port " + port + ": " + e.Message; }
            }
            // A world that was saved remembers its own seed.
            WorldStorage.LevelInfo li;
            if (Server.Storage != null && Server.Storage.TryLoadLevel(out li)) { }
        }

        private void ConnectLocal()
        {
            LoopbackConnection a, b;
            LoopbackConnection.CreatePair(out a, out b);
            Server.AddConnection(b);
            BeginClient(new GameClient(a, GameSettings.PlayerName, "local", GameSettings.IdentityKey));
        }

        public void Join(string address)
        {
            Leave();
            ErrorMessage = "";
            string host = address.Trim();
            int port = 25565;
            int colon = host.LastIndexOf(':');
            if (colon > 0 && colon == host.IndexOf(':'))
            {
                int p;
                if (int.TryParse(host.Substring(colon + 1), out p)) { port = p; host = host.Substring(0, colon); }
            }
            if (host.Length == 0) { ErrorMessage = "Enter a server address."; return; }
            _connectHost = host;
            _connectAddress = host + ":" + port;
            State = SessionState.Connecting;
            StatusMessage = "Connecting to " + _connectAddress + "...";
            string h = host; int pt = port;
            _connectTask = Task.Run(() => TcpConnection.Connect(h, pt, 5000));
            GameSettings.LastHost = _connectAddress;
        }

        private static string PinKey(string address) { return "br.pin." + AuthCrypto.NormaliseAddress(address); }

        private void BeginClient(GameClient client)
        {
            Client = client;
            client.Disconnected = OnDisconnected;
            client.Hurt = () =>
            {
                if (Audio != null && Cam != null) Audio.Play(Sfx.Hurt, Cam.transform.position, 0.8f);
                if (OnPlayerHurt != null) OnPlayerHurt();
            };
            client.EntityEvent = OnEntityEvent;
            State = SessionState.Connecting;
            StatusMessage = "Logging in...";
            Renderer.Clear();
            Entities.Clear();
            Renderer.Bind(client);
            Entities.Bind(client, Sky);
            Effects.Bind(client);
            if (View != null) View.SetClient(client);
        }

        private void OnEntityEvent(RemoteEntity e, EntityEventKind kind)
        {
            if (Audio == null) return;
            Vector3 p = new Vector3(e.RenderPos.X, e.RenderPos.Y, e.RenderPos.Z);
            switch (kind)
            {
                case EntityEventKind.Hurt: Audio.Play(Sfx.Hurt, p, 0.6f, e.Type == EntityType.Dragon ? 0.5f : 1f); break;
                case EntityEventKind.Died: Audio.Play(Sfx.Death, p, 0.8f, e.Type == EntityType.Dragon ? 0.4f : 1f); break;
                case EntityEventKind.Roar: Audio.Play(Sfx.Roar, p, 1f); break;
                case EntityEventKind.Eat: Audio.Play(Sfx.Eat, p, 0.6f); break;
                case EntityEventKind.Tamed: Audio.Play(Sfx.Pop, p, 1f, 0.7f); break;
            }
        }

        private void OnDisconnected(string reason)
        {
            ErrorMessage = reason;
        }

        public void Leave()
        {
            if (_connectTask != null) _connectTask = null;
            if (Client != null)
            {
                try { Client.Disconnect(); } catch (Exception) { }
                Client = null;
            }
            if (Server != null)
            {
                try { Server.Shutdown(); } catch (Exception) { }
                Server = null;
            }
            if (Renderer != null) Renderer.Clear();
            if (Entities != null) Entities.Clear();
            IsHost = false;
            HostedAddress = "";
            State = SessionState.Menu;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }

        public static string DescribeLanAddresses(int port)
        {
            List<string> addrs = new List<string>();
            try
            {
                foreach (IPAddress ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) addrs.Add(ip + ":" + port);
                }
            }
            catch (Exception) { }
            return addrs.Count == 0 ? "localhost:" + port : string.Join("  or  ", addrs.ToArray());
        }

        // ------------------------------------------------------------------ frame loop

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            // Finish a TCP connection attempt started on a worker thread.
            if (_connectTask != null && _connectTask.IsCompleted)
            {
                Task<TcpConnection> t = _connectTask;
                _connectTask = null;
                if (t.IsFaulted || t.IsCanceled)
                {
                    ErrorMessage = "Could not connect to " + _connectAddress + ".";
                    State = SessionState.Menu;
                }
                else
                {
                    string pin = null;
                    try { pin = PlayerPrefs.GetString(PinKey(_connectAddress), ""); } catch (Exception) { }
                    string addr = _connectAddress;
                    GameClient c = new GameClient(t.Result, GameSettings.PlayerName, _connectAddress, GameSettings.IdentityKey,
                        GameSettings.AdminTokenFor(_connectAddress), pin);
                    c.ServerIdLearned = id =>
                    {
                        try { if (PlayerPrefs.GetString(PinKey(addr), "").Length == 0) { PlayerPrefs.SetString(PinKey(addr), id); PlayerPrefs.Save(); } }
                        catch (Exception) { }
                    };
                    BeginClient(c);
                }
            }

            if (Server != null)
            {
                _tickAccumulator += dt;
                int guard = 0;
                while (_tickAccumulator >= 0.05f && guard++ < 6)
                {
                    try { Server.Tick(); }
                    catch (Exception e) { ErrorMessage = "Server error: " + e.Message; Debug.LogException(e); }
                    _tickAccumulator -= 0.05f;
                }
                if (_tickAccumulator > 0.5f) _tickAccumulator = 0f;
            }

            if (Client == null) return;

            bool uiOpen = UiBlocksInput || State != SessionState.Playing;
            Input.Update(dt, uiOpen);
            Client.Update(dt, uiOpen ? new PlayerInput { Yaw = Input.Yaw, Pitch = Input.Pitch } : Input.Move,
                uiOpen ? new ClientActions() : Input.Actions);

            if (Client.State == ClientState.Joined)
            {
                if (State != SessionState.Playing)
                {
                    State = SessionState.Playing;
                    Input.SetLook(0f, 0.1f);
                    StatusMessage = "";
                }
                Sky.SetServerTick(Client.DayTick);
            }
            else if (Client.State == ClientState.Disconnected)
            {
                string reason = Client.DisconnectReason;
                Leave();
                ErrorMessage = string.IsNullOrEmpty(reason) ? "Disconnected." : reason;
            }
        }

        private void OnApplicationQuit()
        {
            if (Server != null) { try { Server.Shutdown(); } catch (Exception) { } }
        }
    }
}
