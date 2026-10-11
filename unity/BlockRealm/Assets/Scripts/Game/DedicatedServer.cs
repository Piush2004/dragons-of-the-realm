using System;
using System.IO;
using UnityEngine;
using BlockRealm.Net;
using BlockRealm.Sim;

namespace BlockRealm.Game
{
    /// <summary>
    /// Headless server mode, started with command-line arguments (run the game with -batchmode -nographics -server):
    ///   -world NAME  -seed N  -port 25565  -viewdistance 8  -maxplayers 8  -public host:port  -admintoken TOKEN  -creative
    /// Remote administration needs both a token of at least 32 characters and the public address players dial. If no
    /// token is given, a strong one is generated and written to admin_token.txt in the world folder.
    /// </summary>
    public sealed class DedicatedServer : MonoBehaviour
    {
        private GameServer _server;
        private float _accumulator;

        public static bool WantsServer(string[] args)
        {
            foreach (string a in args) if (string.Equals(a, "-server", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Arg(string[] args, string name, string fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return fallback;
        }

        private static bool Flag(string[] args, string name)
        {
            foreach (string a in args) if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public void Begin(string[] args)
        {
            string world = GameSession.SafeWorldName(Arg(args, "-world", "Server World"));
            long seed;
            if (!long.TryParse(Arg(args, "-seed", "2024"), out seed)) seed = 2024;
            int port;
            if (!int.TryParse(Arg(args, "-port", "25565"), out port)) port = 25565;
            int view;
            if (!int.TryParse(Arg(args, "-viewdistance", "8"), out view)) view = 8;
            int max;
            if (!int.TryParse(Arg(args, "-maxplayers", "8"), out max)) max = 8;
            string folder = Path.Combine(GameSession.WorldsRoot, world);
            Directory.CreateDirectory(folder);

            ServerOptions o = new ServerOptions();
            o.Seed = seed;
            o.Name = Arg(args, "-name", "BlockRealm Server");
            o.ViewDistance = Mathf.Clamp(view, 3, 16);
            o.MaxPlayers = Mathf.Clamp(max, 1, 64);
            o.SaveDir = folder;
            o.DefaultMode = Flag(args, "-creative") ? GameMode.Creative : GameMode.Survival;
            o.HostIsOp = false;                                   // nobody is "local" on a dedicated server

            string token = Arg(args, "-admintoken", "");
            string tokenFile = Path.Combine(folder, "admin_token.txt");
            if (token.Length == 0)
            {
                try { if (File.Exists(tokenFile)) token = File.ReadAllText(tokenFile).Trim(); } catch (Exception) { }
                if (token.Length < GameServer.MinAdminTokenLength)
                {
                    token = AuthCrypto.NewAdminToken();
                    try { File.WriteAllText(tokenFile, token); } catch (Exception) { }
                }
            }
            string publicAddress = Arg(args, "-public", "");
            if (token.Length >= GameServer.MinAdminTokenLength && publicAddress.Length > 0)
            {
                o.AdminToken = token;
                o.PublicAddresses.Add(publicAddress);
            }

            _server = new GameServer(o);
            _server.LogLine = line => Debug.Log("[server] " + line);
            _server.StartListening(port);
            Debug.Log("[server] " + o.Name + " listening on port " + _server.ListeningPort + ", world \"" + world + "\", seed " + seed +
                      ", view distance " + o.ViewDistance + ", up to " + o.MaxPlayers + " players");
            Debug.Log("[server] server id " + _server.ServerId);
            if (o.AdminToken == null)
                Debug.Log("[server] remote administration is OFF. Start with -public host:port to enable it; the token is in " + tokenFile);
            else
                Debug.Log("[server] remote administration is ON for " + publicAddress + "; token is in " + tokenFile);
            Application.targetFrameRate = 40;
            Application.runInBackground = true;
        }

        private void Update()
        {
            if (_server == null) return;
            _accumulator += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            int guard = 0;
            while (_accumulator >= 0.05f && guard++ < 8)
            {
                try { _server.Tick(); }
                catch (Exception e) { Debug.LogException(e); }
                _accumulator -= 0.05f;
            }
        }

        private void OnApplicationQuit()
        {
            if (_server != null) _server.Shutdown();
        }
    }
}
