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

        public RawClient(GameServer server, string name, bool remote = false, string identity = "", string token = "", string source = "10.0.0.1:5000")
        {
            LoopbackConnection a, b;
            LoopbackConnection.CreatePair(out a, out b);
            // A wrapped connection stands in for a TCP peer: the server must not treat it as the local host.
            server.AddConnection(remote ? (IConnection)new RemoteLike(b, source) : b);
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
        private readonly string _description;
        public RemoteLike(LoopbackConnection inner, string description = "10.0.0.1:5000") { _inner = inner; _description = description; }
        public bool IsOpen { get { return _inner.IsOpen; } }
        public string Description { get { return _description; } }
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
                GameServer s = NewServer(o => { o.HostIsOp = true; o.AdminToken = "s3cret-token-0123456789"; });
                try
                {
                    // A remote peer (anything that is not an in-process connection), however early it connects and whatever it is called.
                    RawClient remoteFirst = new RawClient(s, "Admin", true);
                    RawClient host = new RawClient(s, "Host");
                    RawClient token = new RawClient(s, "Trusted", true, "", "s3cret-token-0123456789");
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
            Harness.Run("a too-short admin token is ignored entirely", () =>
            {
                GameServer s = NewServer(o => { o.AdminToken = "short"; });
                try
                {
                    RawClient r = new RawClient(s, "Boss", true, "", "short", "4.4.4.4:100");
                    Run(s, new[] { r }, 4);
                    Check.True(s.FindPlayer("Boss") != null && !s.FindPlayer("Boss").Op, "weak token grants nothing");
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
                    GameServer s = NewServer(o => { o.SaveDir = dir; o.AdminToken = "top-secret-admin-0123456789"; });
                    RawClient first = new RawClient(s, "Ada", true, "ada-secret-key", "top-secret-admin-0123456789");
                    Run(s, new[] { first }, 5);
                    first.Conn.Close();
                    Run(s, new[] { first }, 5);
                    // The second visit is the one an eavesdropper would record.
                    RawClient visit = new RawClient(s, "Ada", true, "ada-secret-key", "top-secret-admin-0123456789");
                    Run(s, new[] { visit }, 5);
                    Check.True(s.FindPlayer("Ada") != null && s.FindPlayer("Ada").Op, "legitimate return is welcome and keeps admin");
                    byte[] recording = visit.LastAuth;
                    string wire = System.Text.Encoding.UTF8.GetString(recording);
                    Check.True(!wire.Contains("ada-secret-key") && !wire.Contains("top-secret-admin-0123456789"), "the secrets are not in the auth packet");
                    visit.Conn.Close();
                    Run(s, new[] { visit }, 5);

                    // An eavesdropper replays the recorded answer on a fresh connection (new nonce).
                    LoopbackConnection a, b;
                    LoopbackConnection.CreatePair(out a, out b);
                    s.AddConnection(new RemoteLike(b, "10.0.0.9:7000"));
                    a.Send(new HelloPacket { Name = "Ada" }.Serialize());
                    Run(s, new RawClient[0], 3);
                    a.Send(recording);
                    Run(s, new RawClient[0], 3);
                    Check.True(s.FindPlayer("Ada") == null, "replayed login refused");
                    s.Shutdown();
                }
                finally { try { System.IO.Directory.Delete(dir, true); } catch (Exception) { } }
            });
            Harness.Run("two connections racing for a free name cannot steal it from each other", () =>
            {
                GameServer s = NewServer();
                try
                {
                    // Both are challenged while the name is still free, then both answer in the same tick.
                    RawClient first = new RawClient(s, "Racer", true, "key-of-the-first");
                    RawClient second = new RawClient(s, "Racer", true, "key-of-the-second");
                    RawClient[] both = { first, second };
                    Run(s, both, 6);
                    int online = 0;
                    foreach (ServerPlayer pl in s.Players) if (pl.Name == "Racer") online++;
                    Check.Equal(1, online, "only one claimant gets the name");
                    int refused = 0;
                    foreach (RawClient rc in both)
                        foreach (Packet pk in rc.Received) { DisconnectPacket d = pk as DisconnectPacket; if (d != null && d.Reason.Contains("belongs")) refused++; }
                    // (Whichever lost is told the name is taken, or was given a numbered name; it is never silently merged.)
                    first.Conn.Close(); second.Conn.Close();
                    Run(s, both, 5);
                    RawClient again1 = new RawClient(s, "Racer", true, "key-of-the-first");
                    RawClient again2 = new RawClient(s, "Racer", true, "key-of-the-second");
                    Run(s, new[] { again1, again2 }, 6);
                    int holders = 0;
                    foreach (ServerPlayer pl in s.Players) if (pl.Name == "Racer") holders++;
                    Check.Equal(1, holders, "the reservation has exactly one owner afterwards");
                }
                finally { s.Shutdown(); }
            });
            Harness.Run("names are plain ASCII so look-alike letters cannot impersonate", () =>
            {
                GameServer s = NewServer();
                try
                {
                    RawClient real = new RawClient(s, "Admin", true, "k1");
                    RawClient fake = new RawClient(s, "Аdmіn", true, "k2");     // Cyrillic A and i
                    Run(s, new[] { real, fake }, 6);
                    Check.True(s.FindPlayer("Admin") != null, "real name present");
                    foreach (ServerPlayer pl in s.Players)
                        foreach (char ch in pl.Name) Check.True(ch < 128, "non-ASCII character in a player name");
                }
                finally { s.Shutdown(); }
            });
            Harness.Run("a flood of throwaway names from one address is bounded and does not block others", () =>
            {
                GameServer s = NewServer(o => o.MaxPlayers = 200);
                try
                {
                    List<RawClient> flood = new List<RawClient>();
                    for (int i = 0; i < 40; i++) flood.Add(new RawClient(s, "Flood" + i, true, "k" + i, "", "5.5.5.5:" + (3000 + i)));
                    Run(s, flood.ToArray(), 8);
                    Console.WriteLine("        accepted " + s.Players.Count + " of 40 fresh names from one address");
                    Check.True(s.Players.Count <= 5, "one address is capped: " + s.Players.Count);
                    // Someone else is not affected by that flood.
                    RawClient honest = new RawClient(s, "Honest", true, "hk", "", "8.8.8.8:1234");
                    Run(s, new[] { honest }, 6);
                    Check.True(s.FindPlayer("Honest") != null, "an honest player from another address still gets in");
                    // And many different addresses are fine.
                    List<RawClient> many = new List<RawClient>();
                    for (int i = 0; i < 25; i++) many.Add(new RawClient(s, "Many" + i, true, "m" + i, "", "9.9.9." + i + ":1000"));
                    Run(s, many.ToArray(), 8);
                    int joined = 0;
                    for (int i = 0; i < 25; i++) if (s.FindPlayer("Many" + i) != null) joined++;
                    Check.Equal(25, joined, "distinct addresses are not throttled");
                }
                finally { s.Shutdown(); }
            });
            Harness.Run("guessing the admin token locks out the guesser, not the real admin", () =>
            {
                GameServer s = NewServer(o => { o.AdminToken = "right-token-0123456789"; o.MaxPlayers = 50; });
                try
                {
                    // One address guesses six times.
                    for (int i = 0; i < 6; i++)
                    {
                        RawClient guess = new RawClient(s, "G" + (i % 3), true, "", "wrong-" + i, "6.6.6.6:" + (4000 + i));
                        Run(s, new[] { guess }, 3);
                        guess.Conn.Close();
                        Run(s, new[] { guess }, 2);
                    }
                    // Even the right token from that address is refused for a while.
                    RawClient attackerRight = new RawClient(s, "Mallet", true, "", "right-token-0123456789", "6.6.6.6:4999");
                    Run(s, new[] { attackerRight }, 3);
                    Check.True(s.FindPlayer("Mallet") != null && !s.FindPlayer("Mallet").Op, "the guessing address is locked out");
                    // The real admin from elsewhere is unaffected.
                    RawClient real = new RawClient(s, "Real", true, "", "right-token-0123456789", "7.7.7.7:4000");
                    Run(s, new[] { real }, 3);
                    Check.True(s.FindPlayer("Real") != null && s.FindPlayer("Real").Op, "other addresses are not locked out");
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
