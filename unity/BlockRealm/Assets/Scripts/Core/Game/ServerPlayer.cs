using System;
using System.Collections.Generic;
using BlockRealm.Net;

namespace BlockRealm.Sim
{
    /// <summary>The server's record of a connected player. The player is also an entity so others can see and hit them.</summary>
    public sealed class ServerPlayer : Entity
    {
        public const float EyeHeight = 1.62f;
        public const float Reach = 7f;

        public readonly IConnection Connection;
        public GameMode Mode = GameMode.Survival;
        public readonly PlayerInventory Inventory = new PlayerInventory();
        public CraftingWindow Personal;
        public Window OpenWindow;
        public Vec3i OpenWindowPos;
        public byte NextWindowId = 1;

        public int Hunger = 20;
        public float Saturation = 5f;
        public int HungerTimer, RegenTimer, StarveTimer;
        public int Air = 300;
        public int LavaTimer;
        public int SprintTicks;

        public float FallPeakY;
        public bool ClientOnGround;
        public PlayerFlags ReportedFlags;
        public int LastStateTick;
        public Vec3 LastStatePos;

        public bool Op = true;
        public bool InventoryDirty, StatsDirty;
        public int AttackCooldown;
        public int Invulnerable;

        // Block breaking in progress.
        public bool Breaking;
        public Vec3i BreakPos;
        public int BreakStartTick;
        public float BreakExpectedSeconds;

        // What the client has been told about.
        public readonly HashSet<ColumnPos> SentColumns = new HashSet<ColumnPos>();
        public readonly HashSet<int> KnownEntities = new HashSet<int>();
        public int RidingEntityId;
        public int LastPacketTick;
        public bool Welcomed;
        public long KeepAliveToken;

        public bool GodMode { get { return Mode == GameMode.Creative; } }

        public ServerPlayer(IConnection connection, string name)
        {
            Connection = connection;
            Name = name;
            Type = EntityType.Player;
            HalfWidth = 0.3f; Height = 1.8f;
            MaxHealth = 20f; Health = 20f;
            Personal = new CraftingWindow(0, WindowKind.Inventory, Inventory, 2);
        }

        public Vec3 Eye { get { return new Vec3(Pos.X, Pos.Y + EyeHeight, Pos.Z); } }

        public Vec3 LookDirection
        {
            get
            {
                float cp = (float)Math.Cos(Pitch), sp = (float)Math.Sin(Pitch);
                return new Vec3((float)Math.Sin(Yaw) * cp, -sp, (float)Math.Cos(Yaw) * cp);
            }
        }

        public override bool Hittable { get { return !Dead && Mode != GameMode.Creative; } }

        public override EntityFlags Flags
        {
            get
            {
                EntityFlags f = base.Flags;
                if (ClientOnGround) f |= EntityFlags.OnGround;
                if ((ReportedFlags & PlayerFlags.Sprinting) != 0) f |= EntityFlags.Sprinting;
                if ((ReportedFlags & PlayerFlags.Sneaking) != 0) f |= EntityFlags.Sneaking;
                if ((ReportedFlags & PlayerFlags.Flying) != 0) f |= EntityFlags.Flying;
                if ((ReportedFlags & PlayerFlags.Swinging) != 0) f |= EntityFlags.Attacking;
                if (RidingEntityId != 0) f |= EntityFlags.Ridden;
                return f;
            }
        }

        public override void Tick(GameServer s) { }   // players are driven by their packets, see GameServer.TickPlayer

        public void Send(Packet p) { Connection.Send(p.Serialize()); }

        public ItemStack Held { get { return Inventory.Held; } }

        public void GetTool(out ToolKind kind, out int tier)
        {
            ItemInfo info = Held.Info;
            kind = info.Tool; tier = info.Tier;
            if (Held.IsEmpty || info.Tool == ToolKind.None) { kind = ToolKind.None; tier = 0; }
        }
    }
}
