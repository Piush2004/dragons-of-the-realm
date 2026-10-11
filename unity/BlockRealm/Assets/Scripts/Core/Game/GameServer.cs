using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using BlockRealm.Net;

namespace BlockRealm.Sim
{
    public sealed class ServerOptions
    {
        public long Seed = 2024;
        public string Name = "BlockRealm";
        public int ViewDistance = 8;
        public GameMode DefaultMode = GameMode.Survival;
        public bool Pvp = true;
        public int MaxPlayers = 8;
        public bool SpawnMobs = true;
        public bool SpawnDragons = true;
        public bool SynchronousWorldGen;        // tests: generate on the calling thread
        public string SaveDir;                  // null = nothing is saved
        public bool AllowCommands = true;
        /// <summary>
        /// The person running the game on this machine (a connection that never touches the network) may use
        /// commands. Remote players never get this just by connecting.
        /// </summary>
        public bool HostIsOp;
        /// <summary>
        /// Secret that grants operator rights to a remote player who presents it. Null or empty disables remote
        /// operators entirely; a name is never enough, because anyone can type any name.
        /// </summary>
        public string AdminToken;
        /// <summary>Tests and private servers only: every player may use commands.</summary>
        public bool EveryoneIsOp;
        public int MaxPendingConnections = 32;
    }

    /// <summary>
    /// The authoritative game simulation. One instance serves the single-player world (over a loopback connection)
    /// and a dedicated or hosted multiplayer world (over TCP) with exactly the same rules.
    /// </summary>
    public sealed partial class GameServer
    {
        public readonly ServerOptions Options;
        public readonly World World;
        public readonly WorldGenerator Generator;
        public readonly Rng Random;
        public readonly List<ServerPlayer> Players = new List<ServerPlayer>();
        public readonly Dictionary<int, Entity> Entities = new Dictionary<int, Entity>();
        public int TickCount;
        public int DayTick = 1000;
        public Vec3i SpawnPoint;
        public WorldStorage Storage;

        private int _nextEntityId = 1;
        private readonly List<Entity> _entityScratch = new List<Entity>();
        private readonly List<PendingConnection> _pendingConnections = new List<PendingConnection>();
        private readonly List<IConnection> _acceptedLoopbacks = new List<IConnection>();
        private readonly HashSet<ColumnPos> _generating = new HashSet<ColumnPos>();
        private readonly ConcurrentQueue<Column> _generated = new ConcurrentQueue<Column>();
        private readonly Dictionary<ColumnPos, int> _columnNeededTick = new Dictionary<ColumnPos, int>();
        private readonly List<Packet> _chatScratch = new List<Packet>();
        private TcpGameListener _listener;

        private sealed class PendingConnection
        {
            public IConnection Connection;
            public int Deadline;
            public int Stage;            // 0 = waiting for Hello, 1 = challenged, waiting for Auth
            public string Name;
            public string Nonce;
            public string KnownVerifier; // what we remember for this name, if anything
        }

        // Per-source queues are short; the shared "*" source needs room for its (much larger) thresholds.
        private readonly SourceLimiter _adminFailures = new SourceLimiter(512);
        private readonly SourceLimiter _newNames = new SourceLimiter(512);
        // Backstops across all addresses, far above any honest rate, so a many-address flood is still bounded.
        private const string AllSources = "*";
        private const int GlobalAdminFailuresPerMinute = 200;
        private const int GlobalNewNamesPerMinute = 120;

        public GameServer(ServerOptions options)
        {
            Options = options;
            World = new World(options.Seed);
            Generator = new WorldGenerator(options.Seed);
            Random = new Rng(options.Seed ^ 0x5EED);
            SpawnPoint = Generator.FindSpawn();
            if (options.SaveDir != null)
            {
                Storage = new WorldStorage(options.SaveDir);
                WorldStorage.LevelInfo level;
                if (Storage.TryLoadLevel(out level)) { DayTick = level.DayTick; }
            }
            World.BlockChanged = OnBlockChanged;
        }

        // ------------------------------------------------------------------ connections

        public void StartListening(int port)
        {
            _listener = new TcpGameListener(port);
            _listener.Start();
        }

        public int ListeningPort { get { return _listener == null ? 0 : _listener.Port; } }

        public void AddConnection(IConnection c)
        {
            lock (_pendingConnections)
            {
                // Connections that have not said hello yet are cheap to open and cost a thread each: cap them.
                if (_pendingConnections.Count >= Options.MaxPendingConnections) { c.Close(); return; }
                _pendingConnections.Add(new PendingConnection { Connection = c, Deadline = TickCount + 20 * 15 });
            }
        }

        public void Shutdown()
        {
            if (_listener != null) _listener.Dispose();
            foreach (ServerPlayer p in Players.ToArray()) DisconnectPlayer(p, "Server closed");
            SaveAll();
        }

        private void AcceptNetworkConnections()
        {
            if (_listener == null) return;
            IConnection c;
            while (_listener.TryAccept(out c)) AddConnection(c);
        }

        private void ProcessPendingConnections()
        {
            PendingConnection[] snapshot;
            lock (_pendingConnections)
            {
                if (_pendingConnections.Count == 0) return;
                snapshot = _pendingConnections.ToArray();
                _pendingConnections.Clear();
            }
            List<PendingConnection> keep = new List<PendingConnection>();
            foreach (PendingConnection pc in snapshot)
            {
                if (!pc.Connection.IsOpen) continue;
                byte[] data;
                bool done = false;
                if (pc.Connection.TryReceive(out data))
                {
                    Packet p;
                    if (!Packet.TryDeserialize(data, out p)) { pc.Connection.Close(); continue; }
                    if (pc.Stage == 0)
                    {
                        HelloPacket hello = p as HelloPacket;
                        if (hello == null) { pc.Connection.Close(); continue; }
                        if (hello.Protocol != HelloPacket.ProtocolVersion)
                        {
                            pc.Connection.Send(new DisconnectPacket { Reason = "Protocol version mismatch (server " + HelloPacket.ProtocolVersion + ")" }.Serialize());
                            pc.Connection.Close();
                            continue;
                        }
                        pc.Name = CleanName(hello.Name);
                        pc.KnownVerifier = LookupVerifier(pc.Name);
                        pc.Nonce = AuthCrypto.RandomNonce();
                        pc.Stage = 1;
                        pc.Deadline = TickCount + 20 * 15;
                        pc.Connection.Send(new ChallengePacket { Nonce = pc.Nonce, NameKnown = pc.KnownVerifier != null }.Serialize());
                    }
                    else
                    {
                        AuthPacket auth = p as AuthPacket;
                        if (auth == null) { pc.Connection.Close(); continue; }
                        AcceptPlayer(pc, auth);
                        done = true;
                    }
                }
                if (done) continue;
                if (TickCount > pc.Deadline) pc.Connection.Close();
                else keep.Add(pc);
            }
            if (keep.Count > 0) lock (_pendingConnections) _pendingConnections.AddRange(keep);
        }

        private static string CleanName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "Player";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char ch in raw)
            {
                bool ascii = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9');
                if (ascii || ch == '_' || ch == '-') sb.Append(ch);
                if (sb.Length >= 16) break;
            }
            return sb.Length == 0 ? "Player" : sb.ToString();
        }

        private void AcceptPlayer(PendingConnection pc, AuthPacket auth)
        {
            IConnection c = pc.Connection;
            if (Players.Count >= Options.MaxPlayers)
            {
                c.Send(new DisconnectPacket { Reason = "Server is full" }.Serialize());
                c.Close();
                return;
            }
            string name = pc.Name;

            // Identity. The reservation is looked up again now: another connection may have claimed the name since
            // this one said hello, and a stale answer must never overwrite it.
            string verifier = LookupVerifier(name);
            if (verifier != null)
            {
                bool ok;
                if (pc.KnownVerifier != null)
                {
                    // We asked for a proof, so require one.
                    ok = AuthCrypto.ConstantTimeEquals(AuthCrypto.Proof(verifier, pc.Nonce), auth.Proof);
                }
                else
                {
                    // The name was free when challenged and has been claimed meanwhile. It is fine only if the claimant
                    // presents the very same verifier (the same player connecting twice); anyone else is refused.
                    ok = IsHex64(auth.Verifier) && AuthCrypto.ConstantTimeEquals(verifier, auth.Verifier.ToLowerInvariant());
                }
                if (!ok)
                {
                    c.Send(new DisconnectPacket { Reason = "The name \"" + name + "\" belongs to another player." }.Serialize());
                    c.Close();
                    return;
                }
            }
            else
            {
                if (!IsHex64(auth.Verifier)) { c.Close(); return; }
                verifier = auth.Verifier.ToLowerInvariant();
                if (!TryReserveName(name, verifier, SourceKey(c)))
                {
                    c.Send(new DisconnectPacket { Reason = "Too many new names from your address; try again in a minute." }.Serialize());
                    c.Close();
                    return;
                }
            }

            // Names must be unique among connected players. A suffixed variant is taken only if nobody is using it and
            // it is either unreserved or reserved to this same identity.
            string baseName = name; int n = 2;
            while (true)
            {
                bool inUse = FindPlayer(name) != null;
                string reserved = LookupVerifier(name);
                if (!inUse && (reserved == null || AuthCrypto.ConstantTimeEquals(reserved, verifier)))
                {
                    if (reserved == null && !TryReserveName(name, verifier, SourceKey(c)))
                    {
                        c.Send(new DisconnectPacket { Reason = "Too many new names from your address; try again in a minute." }.Serialize());
                        c.Close();
                        return;
                    }
                    break;
                }
                name = baseName + n++;
            }

            // Operator rights: the local host, or a remote that answered the challenge with the admin token.
            bool isLocal = c is LoopbackConnection;
            bool tokenOk = false;
            if (!string.IsNullOrEmpty(Options.AdminToken) && !string.IsNullOrEmpty(auth.AdminProof))
            {
                string source = SourceKey(c);
                if (_adminFailures.Count(source, TickCount, 20 * 60) >= 5 ||
                    _adminFailures.Count(AllSources, TickCount, 20 * 60) >= GlobalAdminFailuresPerMinute)
                {
                    // This source has guessed wrong too often lately: ignore its admin proofs for a minute.
                    // Other sources are unaffected, so an attacker cannot lock the real admin out.
                }
                else
                {
                    string expectedAdmin = AuthCrypto.Proof(AuthCrypto.AdminVerifier(Options.AdminToken), pc.Nonce);
                    tokenOk = AuthCrypto.ConstantTimeEquals(expectedAdmin, auth.AdminProof);
                    if (!tokenOk) { _adminFailures.Add(source, TickCount); _adminFailures.Add(AllSources, TickCount); }
                }
            }

            ServerPlayer p = new ServerPlayer(c, name);
            p.Id = _nextEntityId++;
            p.Mode = Options.DefaultMode;
            p.Op = Options.EveryoneIsOp || (Options.HostIsOp && isLocal) || tokenOk;
            p.LastPacketTick = TickCount;
            Vec3i sp = SpawnPoint;
            int surface = World.GetSurfaceY(sp.X, sp.Z);
            p.Pos = new Vec3(sp.X + 0.5f, (surface >= 0 ? surface + 1 : sp.Y) + 0.1f, sp.Z + 0.5f);
            if (Storage != null) Storage.LoadPlayer(p);
            p.FallPeakY = p.Pos.Y;
            p.LastStatePos = p.Pos;
            p.LastStateTick = TickCount;
            Players.Add(p);
            Entities[p.Id] = p;

            p.Send(new WelcomePacket
            {
                PlayerId = p.Id, Seed = Options.Seed, Position = p.Pos, DayTick = DayTick, Mode = p.Mode,
                ViewDistance = Options.ViewDistance, ServerName = Options.Name,
            });
            p.Welcomed = true;
            SendFullInventory(p);
            SendStats(p);
            Broadcast(new ChatMessagePacket { Text = name + " joined the game" });
        }

        private readonly Dictionary<string, string> _identityHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The verifier reserved for a name (from this session or the saved world), or null if the name is free.</summary>
        private string LookupVerifier(string name)
        {
            string known;
            if (_identityHashes.TryGetValue(name, out known)) return known;
            if (Storage != null)
            {
                known = Storage.LoadIdentity(name);
                if (known != null) { _identityHashes[name] = known; return known; }
            }
            return null;
        }

        private const int MaxReservedNames = 10000;
        private const int MaxNewNamesPerSourcePerMinute = 4;
        private int _persistedIdentities = -1;

        /// <summary>The network address a connection comes from (without the port); in-process connections share "local".</summary>
        private static string SourceKey(IConnection c)
        {
            if (c is LoopbackConnection) return "local";
            return SourceKeyFor(c.Description);
        }

        /// <summary>
        /// Normalises an endpoint description to a rate-limit key: the IPv4 address, or the /64 network for IPv6 (a single
        /// subscriber can rotate through billions of addresses inside their /64, so those must share one allowance).
        /// </summary>
        public static string SourceKeyFor(string description)
        {
            string d = description ?? "unknown";
            string host = d;
            if (d.StartsWith("["))
            {
                int close = d.IndexOf(']');
                host = close > 0 ? d.Substring(1, close - 1) : d.Substring(1);
            }
            else
            {
                int first = d.IndexOf(':'), last = d.LastIndexOf(':');
                if (first > 0 && first == last) host = d.Substring(0, last);        // ipv4:port
            }
            if (host.IndexOf(':') >= 0)
            {
                // IPv6: keep the first four 16-bit groups. A leading "::" or short form still maps deterministically.
                string[] groups = host.Split(':');
                System.Text.StringBuilder sb = new System.Text.StringBuilder("v6/");
                int taken = 0;
                for (int i = 0; i < groups.Length && taken < 4; i++)
                {
                    sb.Append(groups[i].ToLowerInvariant()).Append(':');
                    taken++;
                }
                return sb.ToString();
            }
            return host;
        }

        private static bool IsHex64(string s)
        {
            if (s == null || s.Length != 64) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                bool hex = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        private bool TryReserveName(string name, string verifier, string source)
        {
            if (LookupVerifier(name) != null) return false;
            // One source cannot use up everyone's allowance: the limit is per address.
            bool trusted = source == "local";
            if (!trusted && (_newNames.Count(source, TickCount, 20 * 60) >= MaxNewNamesPerSourcePerMinute ||
                             _newNames.Count(AllSources, TickCount, 20 * 60) >= GlobalNewNamesPerMinute)) return false;
            // The cap counts what is stored on disk, not just this session's memory.
            if (Storage != null)
            {
                if (_persistedIdentities < 0) _persistedIdentities = Storage.CountIdentities();
                if (_persistedIdentities >= MaxReservedNames) return false;
            }
            else if (_identityHashes.Count >= MaxReservedNames) return false;
            if (!trusted) { _newNames.Add(source, TickCount); _newNames.Add(AllSources, TickCount); }
            _identityHashes[name] = verifier;
            if (Storage != null) { Storage.SaveIdentity(name, verifier); _persistedIdentities++; }
            return true;
        }

        public ServerPlayer FindPlayer(string name)
        {
            for (int i = 0; i < Players.Count; i++)
                if (string.Equals(Players[i].Name, name, StringComparison.OrdinalIgnoreCase)) return Players[i];
            return null;
        }

        public void DisconnectPlayer(ServerPlayer p, string reason)
        {
            if (!Players.Contains(p)) return;
            try { p.Send(new DisconnectPacket { Reason = reason }); } catch (Exception) { }
            // Hand back whatever was in the crafting grid or on the cursor before the inventory is saved.
            try { CloseWindow(p); } catch (Exception) { }
            if (Storage != null) Storage.SavePlayer(p);
            Players.Remove(p);
            RemoveEntityInternal(p);
            Broadcast(new ChatMessagePacket { Text = p.Name + " left the game" });
            p.Connection.Close();
        }

        public void Broadcast(Packet packet)
        {
            byte[] data = packet.Serialize();
            for (int i = 0; i < Players.Count; i++) Players[i].Connection.Send(data);
        }

        // ------------------------------------------------------------------ world access for entities

        public bool IsDaylight { get { return DayTick < 13000 || DayTick >= 23000; } }
        public bool IsNight { get { return !IsDaylight; } }

        /// <summary>Sky light at a position as the player would see it, accounting for the time of day.</summary>
        public int SkyLightAt(Vec3 p)
        {
            int sky = World.GetSkyLight(MathUtil.FloorInt(p.X), MathUtil.FloorInt(p.Y), MathUtil.FloorInt(p.Z));
            if (IsNight) sky = Math.Max(0, sky - 11);
            return sky;
        }

        public bool HasLineOfSight(Vec3 a, Vec3 b)
        {
            Vec3 d = b - a;
            float len = d.Length;
            if (len < 0.01f) return true;
            RayHit h = VoxelPhysics.Raycast(World, a, d, len);
            return !h.Hit || h.Distance >= len - 0.3f;
        }

        public Entity GetEntity(int id)
        {
            Entity e;
            return Entities.TryGetValue(id, out e) ? e : null;
        }

        public List<Entity> EntitiesNear(Vec3 pos, float radius)
        {
            List<Entity> list = new List<Entity>();
            float r2 = radius * radius;
            foreach (KeyValuePair<int, Entity> kv in Entities)
            {
                Entity e = kv.Value;
                if (e.Removed) continue;
                if (Vec3.DistanceSq(e.Pos, pos) <= r2) list.Add(e);
            }
            return list;
        }

        public ServerPlayer NearestPlayer(Vec3 pos, float maxDist, bool survivalOnly)
        {
            ServerPlayer best = null; float bestD = maxDist * maxDist;
            for (int i = 0; i < Players.Count; i++)
            {
                ServerPlayer p = Players[i];
                if (p.Dead) continue;
                if (survivalOnly && p.Mode == GameMode.Creative) continue;
                float d = Vec3.DistanceSq(p.Pos, pos);
                if (d <= bestD) { bestD = d; best = p; }
            }
            return best;
        }

        public void AddEntity(Entity e)
        {
            e.Id = _nextEntityId++;
            Entities[e.Id] = e;
        }

        public void RemoveEntity(Entity e) { RemoveEntityInternal(e); }

        private void RemoveEntityInternal(Entity e)
        {
            if (e.Removed) return;
            e.Removed = true;
            Entities.Remove(e.Id);
            byte[] data = new EntityDespawnPacket { EntityId = e.Id }.Serialize();
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].KnownEntities.Remove(e.Id)) Players[i].Connection.Send(data);
            }
        }

        // ------------------------------------------------------------------ main tick

        /// <summary>Advances the simulation by one 50 ms tick. Call it 20 times a second.</summary>
        public void Tick()
        {
            TickCount++;
            AcceptNetworkConnections();
            ProcessPendingConnections();

            for (int i = Players.Count - 1; i >= 0; i--)
            {
                ServerPlayer p = Players[i];
                if (!p.Connection.IsOpen) { DisconnectPlayer(p, "Connection lost"); continue; }
                ProcessPackets(p);
            }

            IntegrateGeneratedColumns();
            DayTick = (DayTick + 1) % WorldConst.DayLengthTicks;
            if (TickCount % 100 == 0) Broadcast(new TimeUpdatePacket { DayTick = DayTick });

            for (int i = 0; i < Players.Count; i++) TickPlayer(Players[i]);
            TickTileEntities();
            TickBlockUpdates();

            _entityScratch.Clear();
            foreach (KeyValuePair<int, Entity> kv in Entities) _entityScratch.Add(kv.Value);
            for (int i = 0; i < _entityScratch.Count; i++)
            {
                Entity e = _entityScratch[i];
                if (!e.Removed && !(e is ServerPlayer)) e.Tick(this);
            }

            if (TickCount % 20 == 0)
            {
                if (Options.SpawnMobs) SpawnMobs();
                DespawnFarMobs();
            }
            if (Options.SpawnDragons && TickCount % 20 == 0) TickLairs();

            StreamColumns();
            ReplicateEntities();
            FlushDirty();
            if (TickCount % (20 * 60) == 0) SaveAll();
        }

        // ------------------------------------------------------------------ column streaming

        private static void ColumnsInRadius(int cx, int cz, int radius, List<ColumnPos> into)
        {
            into.Clear();
            int r2 = radius * radius;
            for (int dz = -radius; dz <= radius; dz++)
                for (int dx = -radius; dx <= radius; dx++)
                    if (dx * dx + dz * dz <= r2) into.Add(new ColumnPos(cx + dx, cz + dz));
            into.Sort((a, b) =>
            {
                int da = (a.X - cx) * (a.X - cx) + (a.Z - cz) * (a.Z - cz);
                int db = (b.X - cx) * (b.X - cx) + (b.Z - cz) * (b.Z - cz);
                return da.CompareTo(db);
            });
        }

        private readonly List<ColumnPos> _columnScratch = new List<ColumnPos>();

        private void StreamColumns()
        {
            int view = Options.ViewDistance;
            for (int pi = 0; pi < Players.Count; pi++)
            {
                ServerPlayer p = Players[pi];
                if (!p.Welcomed) continue;
                int pcx = MathUtil.FloorDiv(MathUtil.FloorInt(p.Pos.X), 16), pcz = MathUtil.FloorDiv(MathUtil.FloorInt(p.Pos.Z), 16);
                ColumnsInRadius(pcx, pcz, view, _columnScratch);
                int sent = 0;
                int requested = 0;
                for (int i = 0; i < _columnScratch.Count; i++)
                {
                    ColumnPos cp = _columnScratch[i];
                    _columnNeededTick[cp] = TickCount;
                    if (p.SentColumns.Contains(cp)) continue;
                    Column col = World.GetColumn(cp.X, cp.Z);
                    if (col != null)
                    {
                        // Only the nearest few go out each tick so a join does not flood the connection.
                        if (sent < 4)
                        {
                            p.Send(new ColumnDataPacket { Cx = cp.X, Cz = cp.Z, Data = ColumnCodec.Encode(col) });
                            p.SentColumns.Add(cp);
                            sent++;
                        }
                    }
                    else if (requested < 6)
                    {
                        RequestColumn(cp.X, cp.Z);
                        requested++;
                    }
                }
                // Tell the client to forget columns that are now far away.
                if (TickCount % 10 == 0)
                {
                    List<ColumnPos> drop = null;
                    foreach (ColumnPos cp in p.SentColumns)
                    {
                        int dx = cp.X - pcx, dz = cp.Z - pcz;
                        if (dx * dx + dz * dz > (view + 2) * (view + 2))
                        {
                            if (drop == null) drop = new List<ColumnPos>();
                            drop.Add(cp);
                        }
                    }
                    if (drop != null)
                        foreach (ColumnPos cp in drop)
                        {
                            p.SentColumns.Remove(cp);
                            p.Send(new ColumnUnloadPacket { Cx = cp.X, Cz = cp.Z });
                        }
                }
            }
            if (TickCount % 100 == 0) UnloadUnusedColumns();
        }

        private void UnloadUnusedColumns()
        {
            List<ColumnPos> remove = null;
            foreach (KeyValuePair<ColumnPos, int> kv in _columnNeededTick)
            {
                if (TickCount - kv.Value < 20 * 20) continue;
                if (remove == null) remove = new List<ColumnPos>();
                remove.Add(kv.Key);
            }
            if (remove == null) return;
            foreach (ColumnPos cp in remove)
            {
                _columnNeededTick.Remove(cp);
                Column col = World.GetColumn(cp.X, cp.Z);
                if (col == null) continue;
                if (Storage != null && col.Modified) SaveColumn(col);
                World.RemoveColumn(cp.X, cp.Z);
                RemoveTileEntitiesIn(cp);
            }
        }

        public void RequestColumn(int cx, int cz)
        {
            ColumnPos cp = new ColumnPos(cx, cz);
            if (World.HasColumn(cx, cz) || _generating.Contains(cp)) return;
            _generating.Add(cp);
            if (Options.SynchronousWorldGen)
            {
                _generated.Enqueue(LoadOrGenerate(cx, cz));
                return;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { _generated.Enqueue(LoadOrGenerate(cx, cz)); }
                catch (Exception) { /* a failed column is retried next time it is needed */ _failedColumns.Enqueue(cp); }
            });
        }

        private readonly ConcurrentQueue<ColumnPos> _failedColumns = new ConcurrentQueue<ColumnPos>();

        private Column LoadOrGenerate(int cx, int cz)
        {
            Column col = null;
            if (Storage != null) col = Storage.TryLoadColumn(cx, cz);
            if (col == null) col = Generator.GenerateColumn(cx, cz);
            LightEngine.InitColumn(col);
            return col;
        }

        private void IntegrateGeneratedColumns()
        {
            ColumnPos failed;
            while (_failedColumns.TryDequeue(out failed)) _generating.Remove(failed);
            int budget = 6;
            Column col;
            while (budget-- > 0 && _generated.TryDequeue(out col))
            {
                ColumnPos cp = new ColumnPos(col.X, col.Z);
                _generating.Remove(cp);
                World.AddColumn(col);
                LightEngine.Stitch(World, col);
                if (Storage != null) Storage.LoadTileEntities(this, col);
                _columnNeededTick[cp] = TickCount;
            }
        }

        /// <summary>Test helper: loads the columns around a point right now (works in synchronous generation mode).</summary>
        public void EnsureLoaded(int blockX, int blockZ, int radiusColumns)
        {
            int cx = MathUtil.FloorDiv(blockX, 16), cz = MathUtil.FloorDiv(blockZ, 16);
            for (int dz = -radiusColumns; dz <= radiusColumns; dz++)
                for (int dx = -radiusColumns; dx <= radiusColumns; dx++)
                    RequestColumn(cx + dx, cz + dz);
            // Drain: with synchronous generation everything is already queued.
            for (int guard = 0; guard < 100 && _generating.Count > 0; guard++)
            {
                Column col;
                while (_generated.TryDequeue(out col))
                {
                    ColumnPos cp = new ColumnPos(col.X, col.Z);
                    _generating.Remove(cp);
                    World.AddColumn(col);
                    LightEngine.Stitch(World, col);
                    if (Storage != null) Storage.LoadTileEntities(this, col);
                    _columnNeededTick[cp] = TickCount;
                }
                if (_generating.Count > 0) Thread.Sleep(5);
            }
        }

        // ------------------------------------------------------------------ replication of entities

        private readonly List<EntityStateEntry> _stateScratch = new List<EntityStateEntry>();

        private void ReplicateEntities()
        {
            const float Range = 100f, DragonRange = 220f;
            bool sendStates = TickCount % 2 == 0;
            for (int pi = 0; pi < Players.Count; pi++)
            {
                ServerPlayer p = Players[pi];
                if (!p.Welcomed) continue;
                _stateScratch.Clear();
                foreach (KeyValuePair<int, Entity> kv in Entities)
                {
                    Entity e = kv.Value;
                    if (e.Removed || e == p) continue;
                    float range = e.Type == EntityType.Dragon ? DragonRange : Range;
                    float d2 = Vec3.DistanceSq(e.Pos, p.Pos);
                    bool inRange = d2 <= range * range;
                    bool known = p.KnownEntities.Contains(e.Id);
                    if (inRange && !known)
                    {
                        EntitySpawnPacket sp = new EntitySpawnPacket
                        {
                            EntityId = e.Id, Type = e.Type, Variant = e.Variant, Position = e.Pos, Yaw = e.Yaw, Name = e.Name,
                        };
                        ItemEntity item = e as ItemEntity;
                        if (item != null) sp.Extra = item.Stack.Id;
                        p.Send(sp);
                        p.KnownEntities.Add(e.Id);
                        known = true;
                    }
                    else if (!inRange && known && d2 > (range + 20f) * (range + 20f))
                    {
                        p.Send(new EntityDespawnPacket { EntityId = e.Id });
                        p.KnownEntities.Remove(e.Id);
                        continue;
                    }
                    if (known && sendStates && _stateScratch.Count < PacketLimits.MaxEntityStates)
                    {
                        EntityStateEntry st = new EntityStateEntry();
                        st.EntityId = e.Id; st.Position = e.Pos; st.Yaw = e.Yaw; st.Pitch = e.Pitch;
                        st.Flags = e.Flags; st.HealthPercent = (byte)(e.HealthFraction * 100f);
                        _stateScratch.Add(st);
                    }
                }
                if (_stateScratch.Count > 0)
                {
                    EntityStatesPacket packet = new EntityStatesPacket();
                    packet.Entries.AddRange(_stateScratch);
                    p.Send(packet);
                }
            }
        }

        private void FlushDirty()
        {
            for (int i = 0; i < Players.Count; i++)
            {
                ServerPlayer p = Players[i];
                if (p.InventoryDirty) { SendFullInventory(p); p.InventoryDirty = false; }
                if (p.StatsDirty) { SendStats(p); p.StatsDirty = false; }
            }
        }

        public void SendStats(ServerPlayer p)
        {
            p.Send(new HealthUpdatePacket { Health = p.Health, Hunger = p.Hunger, Saturation = p.Saturation, Dead = p.Dead });
        }

        public void SendFullInventory(ServerPlayer p)
        {
            Window w = p.OpenWindow ?? p.Personal;
            p.Send(new WindowItemsPacket { Window = w.Id, Items = w.Snapshot() });
            p.Send(new SetCursorPacket { Stack = p.Inventory.Cursor });
            p.Send(new SetSlotPacket { Window = 255, Slot = (short)p.Inventory.Selected, Stack = p.Inventory.Held });
        }
    }
}
