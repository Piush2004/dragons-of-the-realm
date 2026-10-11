using System;
using System.Collections.Generic;
using System.IO;
using BlockRealm.Net;

namespace BlockRealm.Sim
{
    /// <summary>Collision sizes for entity types, shared by the client (picking) and anything that needs them.</summary>
    public static class EntitySizes
    {
        public static void For(EntityType type, byte variant, out float halfWidth, out float height)
        {
            switch (type)
            {
                case EntityType.Player: halfWidth = 0.3f; height = 1.8f; break;
                case EntityType.Pig: halfWidth = 0.4f; height = 0.9f; break;
                case EntityType.Cow: halfWidth = 0.45f; height = 1.4f; break;
                case EntityType.Sheep: halfWidth = 0.4f; height = 1.3f; break;
                case EntityType.Chicken: halfWidth = 0.2f; height = 0.7f; break;
                case EntityType.Zombie: halfWidth = 0.3f; height = 1.8f; break;
                case EntityType.Skeleton: halfWidth = 0.3f; height = 1.9f; break;
                case EntityType.Creeper: halfWidth = 0.3f; height = 1.7f; break;
                case EntityType.Item: halfWidth = 0.15f; height = 0.3f; break;
                case EntityType.Arrow: halfWidth = 0.1f; height = 0.2f; break;
                case EntityType.Fireball: halfWidth = 0.3f; height = 0.6f; break;
                case EntityType.Dragon:
                    halfWidth = DragonStats.HalfWidth[Math.Min((int)variant, 2)];
                    height = DragonStats.BoxHeight[Math.Min((int)variant, 2)];
                    break;
                default: halfWidth = 0.3f; height = 1.0f; break;
            }
        }
    }

    /// <summary>The client's copy of an entity: what the server last said, plus a smoothed position for drawing.</summary>
    public sealed class RemoteEntity
    {
        public int Id;
        public EntityType Type;
        public byte Variant;
        public string Name = "";
        public int ItemId;
        public Vec3 TargetPos, RenderPos;
        public float TargetYaw, RenderYaw, Pitch;
        public EntityFlags Flags;
        public byte HealthPercent = 100;
        public float HurtTime;
        public float Age;
        public bool JustDied;

        public Aabb Box
        {
            get
            {
                float hw, h;
                EntitySizes.For(Type, Variant, out hw, out h);
                return Aabb.FromCenterBottom(RenderPos, hw, h);
            }
        }
    }

    public struct ClientActions
    {
        public bool AttackHeld;      // left mouse / right trigger
        public bool UsePressed;      // right mouse went down this frame
        public bool UseHeld;
        public bool DismountPressed;
    }

    public enum ClientState { Connecting, Joined, Disconnected }

    /// <summary>
    /// Everything a player's machine knows: a replica of nearby blocks, entities, inventory and stats, plus the local
    /// movement simulation. The Unity layer reads this and draws it; user input goes in through <see cref="Update"/>.
    /// </summary>
    public sealed class GameClient
    {
        public readonly IConnection Connection;
        public readonly string PlayerName;
        public ClientState State = ClientState.Connecting;
        public string DisconnectReason = "";

        public World World;
        public readonly PlayerController Player = new PlayerController();
        public int PlayerId;
        public long Seed;
        public GameMode Mode;
        public int ViewDistance = 8;
        public string ServerName = "";
        public int DayTick;

        // Stats
        public float Health = 20f, MaxHealth = 20f;
        public int Hunger = 20;
        public float Saturation;
        public bool Dead;
        public int Air = 300;

        // Inventory replica
        public readonly ItemStack[] Slots = new ItemStack[PlayerInventory.Size];
        public ItemStack Cursor;
        public int Selected;
        public ItemStack[] WindowItems = new ItemStack[0];
        public byte WindowId;                         // 0 = own inventory, otherwise an open container
        public WindowKind OpenWindowKind = WindowKind.Inventory;
        public string WindowTitle = "";
        public bool WindowOpen;
        public Vec3i WindowPos;
        public int FurnaceBurnLeft, FurnaceBurnTotal, FurnaceProgress, FurnaceCookTotal = FurnaceState.CookTicks;

        public readonly Dictionary<int, RemoteEntity> Entities = new Dictionary<int, RemoteEntity>();
        public readonly List<string> Chat = new List<string>();
        public int RideEntityId;

        // What the player is looking at.
        public RayHit TargetBlock;
        public RemoteEntity TargetEntity;
        public float BreakProgress;                   // 0..1 for the crack overlay
        public Vec3i BreakingPos;
        public bool IsBreaking;

        // Events for the presentation layer.
        public Action<int, int> ColumnLoaded;
        public Action<int, int> ColumnUnloaded;
        public Action<string> ChatReceived;
        public Action<EffectKind, Vec3, int> EffectReceived;
        public Action<RemoteEntity, EntityEventKind> EntityEvent;
        public Action<int, Vec3i, int> BreakStage;    // entityId, pos, stage (255 = stopped)
        public Action<string> Disconnected;
        public Action Respawned;
        public Action Hurt;

        private readonly Queue<Column> _incoming = new Queue<Column>();
        private float _sendTimer;
        private float _attackCooldown, _useCooldown, _breakCooldown;
        private float _breakTimeNeeded;
        private float _lightingBudgetMs = 4f;
        private bool _sentHello;
        public int ColumnsReceived, PacketsReceived, BadPackets;
        private const int MaxEntities = 4096;
        private const int MaxQueuedColumns = 1024;

        private readonly string _identityKey, _adminToken, _expectedServerId, _dialAddress;
        /// <summary>Raised with the server's id the first time it is learned, so the caller can remember (pin) it.</summary>
        public Action<string> ServerIdLearned;

        /// <param name="identityKey">A random secret kept by this machine; proves the name is yours on later visits.</param>
        /// <param name="expectedServerId">The id this server had on a previous visit; a different one is refused.</param>
        /// <param name="dialAddress">
        /// The address the player connected to ("localhost:25565"); credentials are bound to it, so a proof given to one
        /// host cannot be relayed to another. In-process games use "local".
        /// </param>
        public GameClient(IConnection connection, string name, string identityKey = "", string adminToken = "",
            string expectedServerId = "", string dialAddress = "local")
        {
            _dialAddress = dialAddress ?? "local";
            Connection = connection;
            PlayerName = name;
            _identityKey = identityKey ?? "";
            _adminToken = adminToken ?? "";
            _expectedServerId = expectedServerId ?? "";
        }

        public void SendHello()
        {
            if (_sentHello) return;
            _sentHello = true;
            Send(new HelloPacket { Name = PlayerName });
        }

        public void Send(Packet p) { if (Connection.IsOpen) Connection.Send(p.Serialize()); }

        public ItemStack Held { get { return Slots[Selected]; } }

        // ------------------------------------------------------------------ frame update

        public void Update(float dt, PlayerInput input, ClientActions actions)
        {
            ProcessPackets();
            if (State == ClientState.Connecting && !_sentHello) SendHello();
            if (State != ClientState.Joined) return;

            if (!Connection.IsOpen) { Fail("Connection lost"); return; }

            IntegrateColumns();

            if (!Dead)
            {
                Player.CanFly = Mode == GameMode.Creative;
                if (Mode != GameMode.Creative) Player.Flying = false;
                Player.Step(World, input, dt);
            }
            UpdateEntities(dt);
            _attackCooldown -= dt; _useCooldown -= dt; _breakCooldown -= dt;

            if (!Dead)
            {
                UpdateTarget(input);
                HandleActions(dt, input, actions);
            }
            else
            {
                IsBreaking = false; BreakProgress = 0f;
            }

            _sendTimer += dt;
            if (_sendTimer >= 0.05f)
            {
                _sendTimer = 0f;
                if (!Dead)
                {
                    float yaw = Player.Riding ? Player.RideYaw : input.Yaw;
                    Send(new PlayerStatePacket { Position = Player.Pos, Yaw = yaw, Pitch = input.Pitch, Flags = Player.Flags });
                }
            }
        }

        private void Fail(string reason)
        {
            State = ClientState.Disconnected;
            DisconnectReason = reason;
            if (Disconnected != null) Disconnected(reason);
        }

        public void Disconnect()
        {
            if (State == ClientState.Joined) Send(new DisconnectPacket { Reason = "quit" });
            Connection.Close();
            State = ClientState.Disconnected;
        }

        // ------------------------------------------------------------------ packets

        private void ProcessPackets()
        {
            byte[] data;
            int budget = 600;
            while (budget-- > 0 && Connection.TryReceive(out data))
            {
                Packet p;
                if (!Packet.TryDeserialize(data, out p)) { BadPackets++; if (BadPackets > 20) Fail("Too many malformed packets"); continue; }
                PacketsReceived++;
                try { Handle(p); }
                catch (InvalidDataException) { BadPackets++; }
            }
            if (State == ClientState.Joined && !Connection.IsOpen) Fail("Connection lost");
            if (State == ClientState.Connecting && !Connection.IsOpen) Fail("Could not connect");
        }

        private void Handle(Packet packet)
        {
            switch (packet.Id)
            {
                case PacketId.Welcome:
                {
                    WelcomePacket w = (WelcomePacket)packet;
                    bool first = State != ClientState.Joined;
                    PlayerId = w.PlayerId; Seed = w.Seed; Mode = w.Mode; ViewDistance = w.ViewDistance;
                    ServerName = w.ServerName; DayTick = w.DayTick;
                    if (first)
                    {
                        World = new World(Seed);
                        Player.Teleport(w.Position);
                        State = ClientState.Joined;
                    }
                    break;
                }
                case PacketId.ColumnData:
                {
                    ColumnDataPacket c = (ColumnDataPacket)packet;
                    if (_incoming.Count >= MaxQueuedColumns) break;       // a flooding server cannot exhaust memory
                    try
                    {
                        Column col = ColumnCodec.Decode(c.Cx, c.Cz, c.Data);
                        _incoming.Enqueue(col);
                        ColumnsReceived++;
                    }
                    catch (InvalidDataException) { BadPackets++; }
                    break;
                }
                case PacketId.ColumnUnload:
                {
                    ColumnUnloadPacket c = (ColumnUnloadPacket)packet;
                    if (World != null && World.RemoveColumn(c.Cx, c.Cz) && ColumnUnloaded != null) ColumnUnloaded(c.Cx, c.Cz);
                    break;
                }
                case PacketId.BlockUpdate:
                {
                    BlockUpdatePacket b = (BlockUpdatePacket)packet;
                    if (World != null && Blocks.IsDefined(b.Block)) World.SetBlock(b.X, b.Y, b.Z, b.Block);
                    break;
                }
                case PacketId.EntitySpawn:
                {
                    EntitySpawnPacket e = (EntitySpawnPacket)packet;
                    if (Entities.Count >= MaxEntities && !Entities.ContainsKey(e.EntityId)) break;
                    RemoteEntity r = new RemoteEntity
                    {
                        Id = e.EntityId, Type = e.Type, Variant = e.Variant, Name = e.Name, ItemId = e.Extra,
                        TargetPos = e.Position, RenderPos = e.Position, TargetYaw = e.Yaw, RenderYaw = e.Yaw,
                    };
                    Entities[e.EntityId] = r;
                    break;
                }
                case PacketId.EntityDespawn:
                    Entities.Remove(((EntityDespawnPacket)packet).EntityId);
                    break;
                case PacketId.EntityStates:
                {
                    EntityStatesPacket s = (EntityStatesPacket)packet;
                    for (int i = 0; i < s.Entries.Count; i++)
                    {
                        EntityStateEntry st = s.Entries[i];
                        RemoteEntity r;
                        if (!Entities.TryGetValue(st.EntityId, out r)) continue;
                        r.TargetPos = st.Position; r.TargetYaw = st.Yaw; r.Pitch = st.Pitch;
                        r.Flags = st.Flags; r.HealthPercent = st.HealthPercent;
                    }
                    break;
                }
                case PacketId.PlayerTeleport:
                    Player.Teleport(((PlayerTeleportPacket)packet).Position);
                    if (Respawned != null && Dead) { }
                    break;
                case PacketId.Knockback:
                    Player.AddImpulse(((KnockbackPacket)packet).Velocity);
                    break;
                case PacketId.HealthUpdate:
                {
                    HealthUpdatePacket h = (HealthUpdatePacket)packet;
                    bool wasDead = Dead;
                    if (h.Health < Health && Hurt != null) Hurt();
                    Health = h.Health; Hunger = h.Hunger; Saturation = h.Saturation; Dead = h.Dead;
                    if (wasDead && !Dead && Respawned != null) Respawned();
                    break;
                }
                case PacketId.WindowItems:
                {
                    WindowItemsPacket w = (WindowItemsPacket)packet;
                    WindowId = w.Window;
                    WindowItems = w.Items;
                    for (int i = 0; i < Slots.Length && i < w.Items.Length; i++) Slots[i] = w.Items[i];
                    break;
                }
                case PacketId.SetSlot:
                {
                    SetSlotPacket s = (SetSlotPacket)packet;
                    if (s.Window == 255) { Selected = MathUtil.Clamp(s.Slot, 0, PlayerInventory.HotbarSize - 1); }
                    else if (s.Slot >= 0 && s.Slot < WindowItems.Length && s.Window == WindowId)
                    {
                        WindowItems[s.Slot] = s.Stack;
                        if (s.Slot < Slots.Length) Slots[s.Slot] = s.Stack;
                    }
                    break;
                }
                case PacketId.SetCursor:
                    Cursor = ((SetCursorPacket)packet).Stack;
                    break;
                case PacketId.OpenWindow:
                {
                    OpenWindowPacket o = (OpenWindowPacket)packet;
                    WindowId = o.Window; OpenWindowKind = o.Kind; WindowTitle = o.Title; WindowOpen = true;
                    WindowPos = new Vec3i(o.X, o.Y, o.Z);
                    break;
                }
                case PacketId.CloseWindow:
                    WindowOpen = false; WindowId = 0; OpenWindowKind = WindowKind.Inventory;
                    break;
                case PacketId.WindowProperty:
                {
                    WindowPropertyPacket w = (WindowPropertyPacket)packet;
                    if (w.Property == 0) FurnaceBurnLeft = w.Value;
                    else if (w.Property == 1) FurnaceBurnTotal = w.Value;
                    else if (w.Property == 2) FurnaceProgress = w.Value;
                    else if (w.Property == 3) FurnaceCookTotal = w.Value;
                    break;
                }
                case PacketId.TimeUpdate:
                    DayTick = ((TimeUpdatePacket)packet).DayTick;
                    break;
                case PacketId.ChatMessage:
                {
                    string text = ((ChatMessagePacket)packet).Text;
                    Chat.Add(text);
                    if (Chat.Count > 100) Chat.RemoveAt(0);
                    if (ChatReceived != null) ChatReceived(text);
                    break;
                }
                case PacketId.EntityEvent:
                {
                    EntityEventPacket ev = (EntityEventPacket)packet;
                    RemoteEntity r;
                    if (Entities.TryGetValue(ev.EntityId, out r))
                    {
                        if (ev.Event == EntityEventKind.Hurt) r.HurtTime = 0.4f;
                        if (ev.Event == EntityEventKind.Died) r.JustDied = true;
                        if (EntityEvent != null) EntityEvent(r, ev.Event);
                    }
                    break;
                }
                case PacketId.Effect:
                {
                    EffectPacket f = (EffectPacket)packet;
                    if (EffectReceived != null) EffectReceived(f.Kind, f.Position, f.Data);
                    break;
                }
                case PacketId.BreakProgress:
                {
                    BreakProgressPacket b = (BreakProgressPacket)packet;
                    if (b.EntityId != PlayerId && BreakStage != null) BreakStage(b.EntityId, new Vec3i(b.X, b.Y, b.Z), b.Stage);
                    break;
                }
                case PacketId.Mount:
                {
                    MountPacket m = (MountPacket)packet;
                    if (m.Mounted) { RideEntityId = m.EntityId; Player.Mount(m.Kind, Player.RideYaw); }
                    else { RideEntityId = 0; Player.Dismount(); }
                    break;
                }
                case PacketId.Challenge:
                {
                    // Answer with proofs derived from the secrets; the secrets themselves never leave this machine.
                    ChallengePacket ch = (ChallengePacket)packet;
                    // A server whose id has changed since the last visit is not the server we trusted: stop before
                    // sending anything derived from our secrets.
                    if (_expectedServerId.Length > 0 && !AuthCrypto.ConstantTimeEquals(_expectedServerId, ch.ServerId))
                    {
                        Fail("This server's identity has changed since your last visit; not logging in.");
                        Connection.Close();
                        break;
                    }
                    if (ServerIdLearned != null && ch.ServerId.Length > 0) ServerIdLearned(ch.ServerId);
                    AuthPacket auth = new AuthPacket();
                    string verifier = AuthCrypto.Verifier(_identityKey, ch.ServerId, _dialAddress);
                    if (ch.NameKnown) auth.Proof = AuthCrypto.Proof(verifier, ch.Nonce);
                    else auth.Verifier = verifier;
                    if (_adminToken.Length > 0) auth.AdminProof = AuthCrypto.Proof(AuthCrypto.AdminVerifier(_adminToken, ch.ServerId, _dialAddress), ch.Nonce);
                    Send(auth);
                    break;
                }
                case PacketId.Disconnect:
                    Fail(((DisconnectPacket)packet).Reason);
                    Connection.Close();
                    break;
                case PacketId.KeepAlive:
                    Send(new KeepAlivePacket { Token = ((KeepAlivePacket)packet).Token });
                    break;
            }
        }

        private void IntegrateColumns()
        {
            if (World == null) return;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            // Light and neighbour stitching cost real time, so integrate columns within a small per-frame budget.
            while (_incoming.Count > 0 && (sw.Elapsed.TotalMilliseconds < _lightingBudgetMs || _incoming.Count > 64 && sw.Elapsed.TotalMilliseconds < 12))
            {
                Column col = _incoming.Dequeue();
                World.RemoveColumn(col.X, col.Z);
                LightEngine.InitColumn(col);
                World.AddColumn(col);
                LightEngine.Stitch(World, col);
                // The new column's sections (and the border sections of its neighbours) need meshes.
                int sections = col.TopSection + 1;
                if (World.SectionChanged != null)
                {
                    for (int sy = 0; sy < sections; sy++)
                    {
                        World.SectionChanged(col.X, sy, col.Z);
                        World.SectionChanged(col.X - 1, sy, col.Z); World.SectionChanged(col.X + 1, sy, col.Z);
                        World.SectionChanged(col.X, sy, col.Z - 1); World.SectionChanged(col.X, sy, col.Z + 1);
                    }
                }
                if (ColumnLoaded != null) ColumnLoaded(col.X, col.Z);
            }
        }

        public int PendingColumns { get { return _incoming.Count; } }

        // ------------------------------------------------------------------ entities and aiming

        private void UpdateEntities(float dt)
        {
            float k = 1f - (float)Math.Exp(-14f * dt);
            foreach (KeyValuePair<int, RemoteEntity> kv in Entities)
            {
                RemoteEntity e = kv.Value;
                e.Age += dt;
                if (e.HurtTime > 0f) e.HurtTime -= dt;
                Vec3 d = e.TargetPos - e.RenderPos;
                if (d.LengthSq > 64f * 64f) e.RenderPos = e.TargetPos;       // teleported
                else e.RenderPos = e.RenderPos + d * k;
                e.RenderYaw = MathUtil.LerpAngle(e.RenderYaw, e.TargetYaw, k);
            }
        }

        private void UpdateTarget(PlayerInput input)
        {
            Vec3 eye = Player.EyePos;
            float cp = (float)Math.Cos(input.Pitch), sp = (float)Math.Sin(input.Pitch);
            Vec3 dir = new Vec3((float)Math.Sin(input.Yaw) * cp, -sp, (float)Math.Cos(input.Yaw) * cp);
            float reach = Mode == GameMode.Creative ? 6f : 5f;
            TargetBlock = VoxelPhysics.Raycast(World, eye, dir, reach);
            TargetEntity = null;
            float best = TargetBlock.Hit ? TargetBlock.Distance : reach;
            foreach (KeyValuePair<int, RemoteEntity> kv in Entities)
            {
                RemoteEntity e = kv.Value;
                if (e.Type == EntityType.Item || e.Type == EntityType.Arrow || e.Type == EntityType.Fireball) continue;
                if (e.Id == RideEntityId) continue;
                if ((e.Flags & EntityFlags.Dead) != 0) continue;
                float t;
                if (RayVsBox(eye, dir, e.Box.Expand(0.1f), out t) && t < best) { best = t; TargetEntity = e; }
            }
        }

        private static bool RayVsBox(Vec3 o, Vec3 d, Aabb b, out float t)
        {
            float tmin = 0f, tmax = 1000f;
            t = 0f;
            for (int axis = 0; axis < 3; axis++)
            {
                float oa = axis == 0 ? o.X : (axis == 1 ? o.Y : o.Z);
                float da = axis == 0 ? d.X : (axis == 1 ? d.Y : d.Z);
                float lo = axis == 0 ? b.Min.X : (axis == 1 ? b.Min.Y : b.Min.Z);
                float hi = axis == 0 ? b.Max.X : (axis == 1 ? b.Max.Y : b.Max.Z);
                if (Math.Abs(da) < 1e-6f)
                {
                    if (oa < lo || oa > hi) return false;
                }
                else
                {
                    float t1 = (lo - oa) / da, t2 = (hi - oa) / da;
                    if (t1 > t2) { float s = t1; t1 = t2; t2 = s; }
                    if (t1 > tmin) tmin = t1;
                    if (t2 < tmax) tmax = t2;
                    if (tmin > tmax) return false;
                }
            }
            t = tmin;
            return true;
        }

        // ------------------------------------------------------------------ actions

        private void HandleActions(float dt, PlayerInput input, ClientActions a)
        {
            if (a.DismountPressed && RideEntityId != 0)
            {
                Send(new InteractEntityPacket { EntityId = RideEntityId, Action = EntityAction.Dismount });
                RideEntityId = 0; Player.Dismount();
                return;
            }
            if (Player.Riding) { CancelBreak(); return; }

            if (a.AttackHeld)
            {
                if (TargetEntity != null)
                {
                    CancelBreak();
                    if (_attackCooldown <= 0f)
                    {
                        Send(new InteractEntityPacket { EntityId = TargetEntity.Id, Action = EntityAction.Attack });
                        _attackCooldown = 0.4f;
                    }
                }
                else if (TargetBlock.Hit)
                {
                    ContinueBreaking(dt);
                }
                else CancelBreak();
            }
            else CancelBreak();

            if (a.UsePressed || (a.UseHeld && _useCooldown <= 0f))
            {
                _useCooldown = 0.22f;
                UseOnTarget(a.UsePressed, input);
            }
        }

        private void ContinueBreaking(float dt)
        {
            RayHit h = TargetBlock;
            byte id = World.GetBlock(h.X, h.Y, h.Z);
            if (id == B.Air || id == B.Bedrock) { CancelBreak(); return; }
            if (_breakCooldown > 0f) return;
            Vec3i pos = new Vec3i(h.X, h.Y, h.Z);
            if (!IsBreaking || !BreakingPos.Equals(pos))
            {
                IsBreaking = true;
                BreakingPos = pos;
                BreakProgress = 0f;
                ItemInfo info = Held.IsEmpty ? null : Held.Info;
                ToolKind kind = info != null ? info.Tool : ToolKind.None;
                int tier = info != null ? info.Tier : 0;
                _breakTimeNeeded = Mode == GameMode.Creative ? 0f : Blocks.BreakTime(id, kind, tier);
                if (_breakTimeNeeded < 0f) { IsBreaking = false; return; }
                Send(new StartBreakPacket { X = pos.X, Y = pos.Y, Z = pos.Z });
            }
            if (_breakTimeNeeded <= 0.0001f) BreakProgress = 1f;
            else BreakProgress += dt / _breakTimeNeeded;
            if (BreakProgress >= 1f)
            {
                Send(new FinishBreakPacket { X = pos.X, Y = pos.Y, Z = pos.Z });
                World.SetBlock(pos.X, pos.Y, pos.Z, B.Air);            // predicted; the server may put it back
                IsBreaking = false;
                BreakProgress = 0f;
                _breakCooldown = Mode == GameMode.Creative ? 0.25f : 0.08f;
            }
        }

        public void CancelBreak()
        {
            if (!IsBreaking) return;
            IsBreaking = false;
            BreakProgress = 0f;
            Send(new CancelBreakPacket());
        }

        private void UseOnTarget(bool pressed, PlayerInput input)
        {
            if (TargetEntity != null && pressed)
            {
                Send(new InteractEntityPacket { EntityId = TargetEntity.Id, Action = EntityAction.Interact });
                return;
            }
            if (TargetBlock.Hit)
            {
                RayHit h = TargetBlock;
                byte id = World.GetBlock(h.X, h.Y, h.Z);
                bool interactable = id == B.CraftingTable || id == B.Furnace || id == B.FurnaceLit || id == B.Chest;
                if (interactable && !Player.Sneaking)
                {
                    if (pressed) Send(new InteractPacket { X = h.X, Y = h.Y, Z = h.Z });
                    return;
                }
                ItemStack held = Held;
                if (!held.IsEmpty && held.Id < 256 && Blocks.IsDefined(held.Id))
                {
                    TryPlace(h, (byte)held.Id);
                    return;
                }
            }
            if (pressed && !Held.IsEmpty && (Held.Info.FoodHunger > 0 || Held.Id == ItemIds.Bow)) Send(new UseItemPacket());
        }

        private void TryPlace(RayHit h, byte block)
        {
            BlockInfo info = Blocks.Get(block);
            int tx = h.PlaceX, ty = h.PlaceY, tz = h.PlaceZ;
            byte target = World.GetBlock(h.X, h.Y, h.Z);
            if (Blocks.Get(target).Replaceable && !Blocks.IsLiquid(target)) { tx = h.X; ty = h.Y; tz = h.Z; }
            if (ty < 0 || ty >= WorldConst.Height) return;
            if (!Blocks.Get(World.GetBlock(tx, ty, tz)).Replaceable) return;
            if (info.Solid)
            {
                Aabb cell = new Aabb(new Vec3(tx, ty, tz), new Vec3(tx + 1, ty + 1, tz + 1));
                if (Player.Box.Intersects(cell)) return;
            }
            Send(new PlaceBlockPacket { X = h.X, Y = h.Y, Z = h.Z, Face = h.Face });
            World.SetBlock(tx, ty, tz, block);                       // predicted; the server confirms or reverts
            if (Mode != GameMode.Creative)
            {
                ItemStack s = Slots[Selected];
                if (!s.IsEmpty)
                {
                    s.Count--;
                    Slots[Selected] = s.Count == 0 ? ItemStack.Empty : s;
                }
            }
        }

        public void SelectSlot(int slot)
        {
            slot = MathUtil.Clamp(slot, 0, PlayerInventory.HotbarSize - 1);
            if (slot == Selected) return;
            Selected = slot;
            CancelBreak();
            Send(new SelectSlotPacket { Slot = (byte)slot });
        }

        public void ClickSlot(int slot, int button, bool shift)
        {
            Send(new InventoryClickPacket { Window = WindowId, Slot = (short)slot, Button = (byte)button, Shift = shift });
        }

        public void DropCursor(bool single) { Send(new InventoryClickPacket { Window = WindowId, Slot = -1, Button = (byte)(single ? 1 : 0) }); }
        public void DropSelected(bool whole) { Send(new InventoryClickPacket { Window = 0, Slot = (short)(whole ? -3 : -2), Button = 0 }); }
        public void CloseWindow() { Send(new CloseWindowPacket { Window = WindowId }); WindowOpen = false; WindowId = 0; OpenWindowKind = WindowKind.Inventory; }
        public void SendChat(string text) { Send(new ChatPacket { Text = text }); }
        public void RequestRespawn() { Send(new RespawnPacket()); }
        public void ToggleFlight()
        {
            if (Mode != GameMode.Creative || Player.Riding) return;
            Player.Flying = !Player.Flying;
            if (Player.Flying) Player.Vel.Y = 0f;
        }
    }
}
