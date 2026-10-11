using System;
using System.IO;
using System.Threading;
using BlockRealm.Net;
using BlockRealm.Sim;

namespace BlockRealm.Tests
{
    /// <summary>
    /// Runs the real game server or a scripted bot player from the command line, outside Unity:
    ///   core.sh test --serve 25565 MyWorld 2024 [seconds]
    ///   core.sh test --bot 127.0.0.1:25565 BotName [seconds]
    /// The Unity game uses exactly the same GameServer and GameClient code.
    /// </summary>
    public static class ConsoleModes
    {
        public static int Serve(string[] args)
        {
            int port = args.Length > 1 ? int.Parse(args[1]) : 25565;
            string world = args.Length > 2 ? args[2] : "ConsoleWorld";
            long seed = args.Length > 3 ? long.Parse(args[3]) : 2024;
            int seconds = args.Length > 4 ? int.Parse(args[4]) : 0;

            ServerOptions o = new ServerOptions();
            o.Seed = seed;
            o.Name = "BlockRealm console server";
            o.ViewDistance = 6;
            o.MaxPlayers = 16;
            o.SaveDir = Path.Combine(Path.GetTempPath(), "BlockRealmConsole", world);
            o.SpawnDragons = true;
            GameServer server = new GameServer(o);
            server.LogLine = line => Console.WriteLine("[server] " + line);
            server.StartListening(port);
            Console.WriteLine("[server] listening on port " + server.ListeningPort + ", world \"" + world + "\", seed " + seed + ", id " + server.ServerId);

            DateTime end = seconds > 0 ? DateTime.UtcNow.AddSeconds(seconds) : DateTime.MaxValue;
            DateTime next = DateTime.UtcNow;
            int lastPlayers = -1;
            while (DateTime.UtcNow < end)
            {
                server.Tick();
                if (server.Players.Count != lastPlayers)
                {
                    lastPlayers = server.Players.Count;
                    Console.WriteLine("[server] players online: " + lastPlayers);
                }
                next = next.AddMilliseconds(50);
                int wait = (int)(next - DateTime.UtcNow).TotalMilliseconds;
                if (wait > 0) Thread.Sleep(wait); else if (wait < -500) next = DateTime.UtcNow;
            }
            server.Shutdown();
            Console.WriteLine("[server] stopped");
            return 0;
        }

        public static int Bot(string[] args)
        {
            string address = args.Length > 1 ? args[1] : "127.0.0.1:25565";
            string name = args.Length > 2 ? args[2] : "Bot";
            int seconds = args.Length > 3 ? int.Parse(args[3]) : 20;
            int colon = address.LastIndexOf(':');
            string host = address.Substring(0, colon);
            int port = int.Parse(address.Substring(colon + 1));

            TcpConnection conn = TcpConnection.Connect(host, port, 5000);
            GameClient client = new GameClient(conn, name, address, "bot-identity-" + name);
            client.ChatReceived = text => Console.WriteLine("[" + name + "] chat: " + text);
            Rng rng = new Rng(name.GetHashCode());
            PlayerInput input = new PlayerInput();
            ClientActions actions = new ClientActions();
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            DateTime next = DateTime.UtcNow;
            int ticks = 0;
            bool greeted = false;
            Vec3 start = Vec3.Zero;
            while (DateTime.UtcNow < end && client.State != ClientState.Disconnected)
            {
                if (ticks % 40 == 0)
                {
                    input.MoveZ = 1f;
                    input.MoveX = rng.Range(-0.4f, 0.4f);
                    input.Yaw += rng.Range(-0.8f, 0.8f);
                    input.Jump = rng.Chance(0.3f);
                    input.Sprint = rng.Chance(0.5f);
                    actions.AttackHeld = rng.Chance(0.5f);
                }
                client.Update(0.05f, input, actions);
                if (client.State == ClientState.Joined)
                {
                    if (!greeted)
                    {
                        greeted = true;
                        start = client.Player.Pos;
                        client.SendChat("hello from " + name);
                        Console.WriteLine("[" + name + "] joined as player " + client.PlayerId + " at " + client.Player.Pos);
                    }
                }
                ticks++;
                next = next.AddMilliseconds(50);
                int wait = (int)(next - DateTime.UtcNow).TotalMilliseconds;
                if (wait > 0) Thread.Sleep(wait); else if (wait < -500) next = DateTime.UtcNow;
            }
            float moved = greeted ? Vec3.Distance(start, client.Player.Pos) : 0f;
            Console.WriteLine("[" + name + "] done: state " + client.State + ", moved " + moved.ToString("0.0") + " blocks, " +
                client.World.ColumnCount + " columns, " + client.Entities.Count + " entities, " + client.PacketsReceived + " packets, " +
                client.Chat.Count + " chat lines");
            client.Disconnect();
            return client.State == ClientState.Disconnected && !greeted ? 1 : 0;
        }
    }
}
