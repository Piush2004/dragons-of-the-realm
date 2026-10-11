using System;
using System.Collections.Generic;
using BlockRealm.Net;
using BlockRealm.Sim;

namespace BlockRealm.Tests
{
    /// <summary>A hand-driven client with no game logic of its own: it sends exactly what a cheater would.</summary>
    public sealed class RawClient
    {
        public readonly LoopbackConnection Conn;
        public readonly List<Packet> Received = new List<Packet>();
        public int PlayerId;
        public byte[] LastAuth;
        private readonly string _identity, _token;

        public RawClient(GameServer server, string name, bool remote = false, string identity = "", string token = "")
        {
            LoopbackConnection a, b;
            LoopbackConnection.CreatePair(out a, out b);
            // A wrapped connection stands in for a TCP peer: the server must not treat it as the local host.
            server.AddConnection(remote ? (IConnection)new RemoteLike(b) : b);
            Conn = a;
            _identity = identity; _token = token;
            Send(new HelloPacket { Name = name });
        }

        public void Send(Packet p) { Conn.Send(p.Serialize()); }

        public void Pump()
        {
            byte[] data;
            while (Conn.TryReceive(out data))
            {
                Packet p = Packet.Deserialize(data);
                Received.Add(p);
                WelcomePacket w = p as WelcomePacket;
                if (w != null) PlayerId = w.PlayerId;
                ChallengePacket ch = p as ChallengePacket;
                if (ch != null)
                {
                    AuthPacket auth = new AuthPacket();
                    string verifier = AuthCrypto.Verifier(_identity);
                    if (ch.NameKnown) auth.Proof = AuthCrypto.Proof(verifier, ch.Nonce); else auth.Verifier = verifier;
                    if (!string.IsNullOrEmpty(_token)) auth.AdminProof = AuthCrypto.Proof(AuthCrypto.AdminVerifier(_token), ch.Nonce);
                    LastAuth = auth.Serialize();
                    Send(auth);
                }
            }
        }

        public int Count<T>() where T : Packet
        {
            int n = 0;
            foreach (Packet p in Received) if (p is T) n++;
            return n;
        }
    }

    /// <summary>Test stand-in for a network peer: same behaviour, but not an in-process loopback.</summary>
    public sealed class RemoteLike : IConnection
    {
        private readonly LoopbackConnection _inner;
        public RemoteLike(LoopbackConnection inner) { _inner = inner; }
        public bool IsOpen { get { return _inner.IsOpen; } }
        public string Description { get { return "remote-like"; } }
        public void Send(byte[] payload) { _inner.Send(payload); }
        public bool TryReceive(out byte[] payload) { return _inner.TryReceive(out payload); }
        public void Close() { _inner.Close(); }
        public void Dispose() { _inner.Dispose(); }
    }

    public static class AbuseTests
    {
        private static void Run(GameServer s, RawClient[] clients, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                s.Tick();
                foreach (RawClient c in clients) c.Pump();
            }
        }

        private static GameServer NewServer(Action<ServerOptions> cfg = null)
        {
            ServerOptions o = new ServerOptions { Seed = 2024, ViewDistance = 3, SynchronousWorldGen = true, SpawnMobs = false, SpawnDragons = false };
            if (cfg != null) cfg(o);
            return new GameServer(o);
        }

        public static void RunAll()
        {
            Harness.Group("abuse resistance");
            Harness.Run("operator rights come from being local or presenting the admin token, never from a name", () =>
            {
                GameServer s = NewServer(o => { o.HostIsOp = true; o.AdminToken = "s3cret-token"; });
                try
                {
                    // A remote peer (anything that is not an in-process connection), however early it connects and whatever it is called.
                    RawClient remoteFirst = new RawClient(s, "Admin", true);
                    RawClient host = new RawClient(s, "Host");
                    RawClient token = new RawClient(s, "Trusted", true, "", "s3cret-token");
                    RawClient wrong = new RawClient(s, "Wrong", true, "", "guess");
                    RawClient[] all = { remoteFirst, host, token, wrong };
                    Run(s, all, 5);
                    Check.True(!s.FindPlayer("Admin").Op, "first remote connection is not an operator");
                    Check.True(s.FindPlayer("Host").Op, "the local host is");
                    Check.True(s.FindPlayer("Trusted").Op, "valid token is");
                    Check.True(!s.FindPlayer("Wrong").Op, "wrong token is not");
                    remoteFirst.Send(new ChatPacket { Text = "/give diamond 20" });
                    remoteFirst.Send(new ChatPacket { Text = "/gamemode creative" });
                    token.Send(new ChatPacket { Text = "/give diamond 3" });
                    Run(s, all, 5);
                    Check.Equal(0, s.FindPlayer("Admin").Inventory.Count(ItemIds.Diamond), "remote player got nothing");
                    Check.Equal(GameMode.Survival, s.FindPlayer("Admin").Mode);
                    Check.Equal(3, s.FindPlayer("Trusted").Inventory.Count(ItemIds.Diamond), "token holder may");
                }
                finally { s.Shutdown(); }
            });
            Harness.Run("a saved name cannot be taken over by someone with a different identity", () =>
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "brauth_" + Guid.NewGuid().ToString("N"));
                try
                {
                    GameServer s = NewServer(o => { o.SaveDir = dir; });
                    RawClient ada = new RawClient(s, "Ada", true, "ada-secret-key");
                    Run(s, new[] { ada }, 5);
                    s.FindPlayer("Ada").Inventory.Add(new ItemStack(ItemIds.Diamond, 9));
                    ada.Conn.Close();
                    Run(s, new[] { ada }, 5);
                    Check.True(s.FindPlayer("Ada") == null, "Ada left");
                    s.Shutdown();

                    s = NewServer(o => { o.SaveDir = dir; });
                    RawClient thief = new RawClient(s, "Ada", true, "not-the-key");
                    Run(s, new[] { thief }, 5);
                    Check.True(s.FindPlayer("Ada") == null, "impostor refused");
                    bool told = false;
                    foreach (Packet p in thief.Received) { DisconnectPacket d = p as DisconnectPacket; if (d != null && d.Reason.Contains("belongs")) told = true; }
                    Check.True(told, "impostor told why");
                    RawClient real = new RawClient(s, "Ada", true, "ada-secret-key");
                    Run(s, new[] { real }, 5);
                    Check.True(s.FindPlayer("Ada") != null, "the real Ada is welcome");
                    Check.Equal(9, s.FindPlayer("Ada").Inventory.Count(ItemIds.Diamond), "with her inventory");
                    s.Shutdown();
                }
                finally { try { System.IO.Directory.Delete(dir, true); } catch (Exception) { } }
            });
            Harness.Run("survival players cannot claim flight or bank up a free teleport", () =>
            {
                GameServer s = NewServer(o => o.EveryoneIsOp = false);
                try
                {
                    RawClient r = new RawClient(s, "Cheat");
                    Run(s, new[] { r }, 30);
                    ServerPlayer p = s.FindPlayer("Cheat");
                    Vec3 start = p.Pos;
                    // Claiming Flying in survival changes nothing about what is allowed.
                    r.Send(new PlayerStatePacket { Position = new Vec3(start.X + 6f, start.Y, start.Z), Flags = PlayerFlags.Flying });
                    Run(s, new[] { r }, 2);
                    Check.Equal(0, (int)(p.ReportedFlags & PlayerFlags.Flying), "flying flag ignored");
                    Check.True(Vec3.Distance(p.Pos, start) < 1f, "6 blocks in one tick is not walking");
                    // Staying silent for a while does not allow a long jump afterwards.
                    Run(s, new[] { r }, 100);
                    r.Send(new PlayerStatePacket { Position = new Vec3(start.X + 40f, start.Y, start.Z), Flags = PlayerFlags.OnGround });
                    Run(s, new[] { r }, 2);
                    Check.True(Vec3.Distance(p.Pos, start) < 1f, "40 blocks after silence rejected");
                    Check.True(r.Count<PlayerTeleportPacket>() >= 1, "told to go back");
                    // An honest sequence of small steps is accepted (on a cleared strip, so no hillside is in the way).
                    int sx = MathUtil.FloorInt(start.X), sy = MathUtil.FloorInt(start.Y), sz = MathUtil.FloorInt(start.Z);
                    for (int i = 0; i <= 4; i++)
                        for (int y = sy; y <= sy + 3; y++) s.World.SetBlockRaw(sx + i, y, sz, B.Air);
                    for (int i = 0; i <= 4; i++) s.World.SetBlockRaw(sx + i, sy - 1, sz, B.Stone);
                    for (int i = 1; i <= 10; i++)
                    {
                        r.Send(new PlayerStatePacket { Position = new Vec3(start.X + i * 0.2f, start.Y, start.Z), Flags = PlayerFlags.OnGround });
                        Run(s, new[] { r }, 1);
                    }
                    Check.True(p.Pos.X > start.X + 1.5f, "normal walking accepted: " + (p.Pos.X - start.X));
                }
                finally { s.Shutdown(); }
            });
            Harness.Run("walking through solid ground is refused", () =>
            {
                GameServer s = NewServer(o => o.EveryoneIsOp = false);
                try
                {
                    RawClient r = new RawClient(s, "Ghost");
                    Run(s, new[] { r }, 30);
                    ServerPlayer p = s.FindPlayer("Ghost");
                    Vec3 start = p.Pos;
                    int gx = MathUtil.FloorInt(start.X), gz = MathUtil.FloorInt(start.Z);
                    int surface = s.World.GetSurfaceY(gx, gz);
                    // Ten blocks of stone below the surface is solid.
                    r.Send(new PlayerStatePacket { Position = new Vec3(start.X, surface - 1.0f, start.Z), Flags = PlayerFlags.None });
                    Run(s, new[] { r }, 2);
                    Check.True(p.Pos.Y > surface - 0.5f, "stayed above the ground: " + p.Pos.Y + " surface " + surface);
                }
                finally { s.Shutdown(); }
            });
            Harness.Run("instant mass-mining is rate limited", () =>
            {
                GameServer s = NewServer(o => o.EveryoneIsOp = false);
                try
                {
                    RawClient r = new RawClient(s, "Miner");
                    Run(s, new[] { r }, 30);
                    ServerPlayer p = s.FindPlayer("Miner");
                    int px = MathUtil.FloorInt(p.Pos.X), pz = MathUtil.FloorInt(p.Pos.Z), py = MathUtil.FloorInt(p.Pos.Y);
                    // A field of tall grass (breaks instantly), each plant standing on solid ground.
                    List<Vec3i> cells = new List<Vec3i>();
                    for (int i = 0; i < 30; i++)
                    {
                        int cx = px - 3 + (i % 6), cz = pz + 2 + i / 6;
                        int top = s.World.GetSurfaceY(cx, cz);
                        s.World.SetBlockRaw(cx, top, cz, B.Stone);
                        s.World.SetBlockRaw(cx, top + 1, cz, B.TallGrass);
                        cells.Add(new Vec3i(cx, top + 1, cz));
                    }
                    foreach (Vec3i c in cells) r.Send(new FinishBreakPacket { X = c.X, Y = c.Y, Z = c.Z });
                    Run(s, new[] { r }, 1);
                    int broken = 0;
                    foreach (Vec3i c in cells) if (s.World.GetBlock(c.X, c.Y, c.Z) == B.Air) broken++;
                    Console.WriteLine("        broke " + broken + " of 30 in one tick");
                    Check.True(broken <= 10 && broken >= 1, "burst limited to the token bucket: " + broken);
                }
                finally { s.Shutdown(); }
            });
            Harness.Run("chat floods are limited", () =>
            {
                GameServer s = NewServer(o => o.EveryoneIsOp = false);
                try
                {
                    RawClient spammer = new RawClient(s, "Spam"), listener = new RawClient(s, "Ear");
                    RawClient[] all = { spammer, listener };
                    Run(s, all, 10);
                    int before = listener.Count<ChatMessagePacket>();
                    for (int i = 0; i < 50; i++) spammer.Send(new ChatPacket { Text = "buy now " + i });
                    Run(s, all, 3);
                    int got = listener.Count<ChatMessagePacket>() - before;
                    Console.WriteLine("        delivered " + got + " of 50 flooded messages");
                    Check.True(got <= 6 && got >= 1, "flood cut to the burst allowance: " + got);
                }
                finally { s.Shutdown(); }
            });
            Harness.Run("a flood of half-open connections is capped", () =>
            {
                GameServer s = NewServer(o => o.MaxPendingConnections = 8);
                try
                {
                    List<LoopbackConnection> peers = new List<LoopbackConnection>();
                    for (int i = 0; i < 50; i++)
                    {
                        LoopbackConnection a, b;
                        LoopbackConnection.CreatePair(out a, out b);
                        s.AddConnection(b);
                        peers.Add(b);
                    }
                    int open = 0;
                    foreach (LoopbackConnection c in peers) if (c.IsOpen) open++;
                    Check.Equal(8, open, "only the cap stays open");
                }
                finally { s.Shutdown(); }
            });
            Harness.Run("a recorded login cannot be replayed and secrets never appear on the wire", () =>
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "brreplay_" + Guid.NewGuid().ToString("N"));
                try
                {
                    GameServer s = NewServer(o => { o.SaveDir = dir; o.AdminToken = "top-secret-admin"; });
                    RawClient first = new RawClient(s, "Ada", true, "ada-secret-key", "top-secret-admin");
                    Run(s, new[] { first }, 5);
                    first.Conn.Close();
                    Run(s, new[] { first }, 5);
                    // The second visit is the one an eavesdropper would record.
                    RawClient visit = new RawClient(s, "Ada", true, "ada-secret-key", "top-secret-admin");
                    Run(s, new[] { visit }, 5);
                    Check.True(s.FindPlayer("Ada") != null && s.FindPlayer("Ada").Op, "legitimate return is welcome and keeps admin");
                    byte[] recording = visit.LastAuth;
                    string wire = System.Text.Encoding.UTF8.GetString(recording);
                    Check.True(!wire.Contains("ada-secret-key") && !wire.Contains("top-secret-admin"), "the secrets are not in the auth packet");
                    visit.Conn.Close();
                    Run(s, new[] { visit }, 5);

                    // An eavesdropper replays the recorded answer on a fresh connection (new nonce).
                    LoopbackConnection a, b;
                    LoopbackConnection.CreatePair(out a, out b);
                    s.AddConnection(new RemoteLike(b));
                    a.Send(new HelloPacket { Name = "Ada" }.Serialize());
                    Run(s, new RawClient[0], 3);
                    a.Send(recording);
                    Run(s, new RawClient[0], 3);
                    Check.True(s.FindPlayer("Ada") == null, "replayed login refused");
                    s.Shutdown();
                }
                finally { try { System.IO.Directory.Delete(dir, true); } catch (Exception) { } }
            });
            Harness.Run("guessing the admin token is locked out", () =>
            {
                GameServer s = NewServer(o => { o.AdminToken = "right-token"; });
                try
                {
                    for (int i = 0; i < 6; i++)
                    {
                        RawClient guess = new RawClient(s, "G" + i, true, "", "wrong-" + i);
                        Run(s, new[] { guess }, 3);
                        Check.True(!s.FindPlayer("G" + i).Op, "guess " + i + " fails");
                    }
                    // Even the right token is refused for a while after repeated failures.
                    RawClient real = new RawClient(s, "Real", true, "", "right-token");
                    Run(s, new[] { real }, 3);
                    Check.True(s.FindPlayer("Real") != null && !s.FindPlayer("Real").Op, "locked out after repeated failures");
                }
                finally { s.Shutdown(); }
            });
            Harness.Run("attacks and interactions out of reach are ignored", () =>
            {
                GameServer s = NewServer(o => o.EveryoneIsOp = true);
                try
                {
                    RawClient r = new RawClient(s, "Far");
                    Run(s, new[] { r }, 30);
                    ServerPlayer p = s.FindPlayer("Far");
                    MobEntity pig = new MobEntity(EntityType.Pig);
                    pig.Pos = p.Pos + new Vec3(12f, 0f, 0f);
                    s.AddEntity(pig);
                    r.Send(new InteractEntityPacket { EntityId = pig.Id, Action = EntityAction.Attack });
                    Run(s, new[] { r }, 2);
                    Check.Equal(pig.MaxHealth, pig.Health, "pig untouched at 12 blocks");
                    pig.Pos = p.Pos + new Vec3(2f, 0f, 0f);
                    r.Send(new InteractEntityPacket { EntityId = pig.Id, Action = EntityAction.Attack });
                    Run(s, new[] { r }, 2);
                    Check.True(pig.Health < pig.MaxHealth, "pig hit at 2 blocks");
                }
                finally { s.Shutdown(); }
            });
        }
    }
}
