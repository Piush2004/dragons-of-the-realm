using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using BlockRealm.Net;
using BlockRealm.Sim;

namespace BlockRealm.Game
{
    /// <summary>
    /// Headless server mode, started with command-line arguments (run the game with -batchmode -nographics -server):
    ///   -world NAME  -seed N  -port 25565  -viewdistance 8  -maxplayers 8  -public host:port  -creative
    /// Remote administration is off unless -public is given. The admin token is read from the BLOCKREALM_ADMIN_TOKEN
    /// environment variable or -admintokenfile PATH (it is deliberately not a plain command-line argument, which any user
    /// on the machine can read from the process list). If neither is set, a strong token is generated and shown once on
    /// the console. Only a derived verifier is written to disk (admin_verifiers.txt), never the token itself.
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

            // Verifiers from a previous run are reloaded so the token never has to be stored.
            string verifierFile = Path.Combine(folder, "admin_verifiers.txt");
            try
            {
                if (File.Exists(verifierFile))
                    foreach (string line in File.ReadAllLines(verifierFile))
                    {
                        int eq = line.LastIndexOf('=');
                        if (eq > 0) o.AdminVerifiers[AuthCrypto.NormaliseAddress(line.Substring(0, eq))] = line.Substring(eq + 1).Trim();
                    }
            }
            catch (Exception) { }

            string publicAddress = Arg(args, "-public", "");
            string token = Environment.GetEnvironmentVariable("BLOCKREALM_ADMIN_TOKEN") ?? "";
            string tokenFile = Arg(args, "-admintokenfile", "");
            if (token.Length == 0 && tokenFile.Length > 0)
            {
                try { token = File.ReadAllText(tokenFile).Trim(); } catch (Exception e) { Debug.LogWarning("[server] could not read the admin token file: " + e.Message); }
            }
            bool generated = false;
            if (publicAddress.Length > 0 && token.Length == 0 && !o.AdminVerifiers.ContainsKey(AuthCrypto.NormaliseAddress(publicAddress)))
            {
                token = AuthCrypto.NewAdminToken();
                generated = true;
            }
            if (publicAddress.Length > 0 && token.Length >= GameServer.MinAdminTokenLength)
            {
                o.AdminToken = token;
                o.PublicAddresses.Add(publicAddress);
            }
            else if (token.Length > 0)
            {
                Debug.LogWarning("[server] the admin token is shorter than " + GameServer.MinAdminTokenLength + " characters and was ignored");
            }

            _server = new GameServer(o);
            _server.LogLine = line => Debug.Log("[server] " + line);
            _server.StartListening(port);

            // Persist only what is needed to check proofs later.
            try
            {
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> kv in _server.Options.AdminVerifiers) lines.Add(kv.Key + "=" + kv.Value);
                if (lines.Count > 0) File.WriteAllLines(verifierFile, lines.ToArray());
            }
            catch (Exception) { }

            Debug.Log("[server] " + o.Name + " listening on port " + _server.ListeningPort + ", world \"" + world + "\", seed " + seed +
                      ", view distance " + o.ViewDistance + ", up to " + o.MaxPlayers + " players");
            Debug.Log("[server] server id " + _server.ServerId);
            if (generated)
                Debug.Log("[server] ADMIN TOKEN (shown once, store it safely; the server keeps only a hash): " + token);
            if (_server.Options.AdminVerifiers.Count == 0)
                Debug.Log("[server] remote administration is OFF (start with -public host:port to enable it)");
            else
                Debug.Log("[server] remote administration is ON for " + string.Join(", ", new List<string>(_server.Options.AdminVerifiers.Keys).ToArray()));
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
