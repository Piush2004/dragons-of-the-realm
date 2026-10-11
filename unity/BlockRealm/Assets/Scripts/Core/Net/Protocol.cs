using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BlockRealm.Net
{
    public sealed class PacketWriter
    {
        private readonly MemoryStream _ms = new MemoryStream(256);
        private readonly BinaryWriter _w;
        public PacketWriter() { _w = new BinaryWriter(_ms, Encoding.UTF8); }
        public void U8(byte v) { _w.Write(v); }
        public void Bool(bool v) { _w.Write(v ? (byte)1 : (byte)0); }
        public void I16(short v) { _w.Write(v); }
        public void U16(ushort v) { _w.Write(v); }
        public void I32(int v) { _w.Write(v); }
        public void I64(long v) { _w.Write(v); }
        public void F32(float v) { _w.Write(v); }
        public void Str(string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s ?? "");
            if (b.Length > 32000) throw new InvalidDataException("string too long");
            _w.Write((ushort)b.Length); _w.Write(b);
        }
        public void Bytes(byte[] b) { _w.Write(b.Length); _w.Write(b); }
        public void Stack(ItemStack s) { _w.Write(s.Id); _w.Write(s.Count); _w.Write(s.Wear); }
        public void Vec(Vec3 v) { F32(v.X); F32(v.Y); F32(v.Z); }
        public byte[] ToArray() { _w.Flush(); return _ms.ToArray(); }
    }

    public sealed class PacketReader
    {
        private readonly BinaryReader _r;
        public PacketReader(byte[] data) { _r = new BinaryReader(new MemoryStream(data), Encoding.UTF8); }
        public byte U8() { return _r.ReadByte(); }
        public bool Bool() { return _r.ReadByte() != 0; }
        public short I16() { return _r.ReadInt16(); }
        public ushort U16() { return _r.ReadUInt16(); }
        public int I32() { return _r.ReadInt32(); }
        public long I64() { return _r.ReadInt64(); }
        public float F32() { return _r.ReadSingle(); }
        public string Str() { int n = _r.ReadUInt16(); return Encoding.UTF8.GetString(_r.ReadBytes(n)); }
        public byte[] Bytes()
        {
            int n = _r.ReadInt32();
            if (n < 0 || n > 8 * 1024 * 1024) throw new InvalidDataException("bad byte array length " + n);
            return _r.ReadBytes(n);
        }
        public ItemStack Stack() { ItemStack s = new ItemStack(); s.Id = _r.ReadUInt16(); s.Count = _r.ReadByte(); s.Wear = _r.ReadInt16(); return s; }
        public Vec3 Vec() { return new Vec3(F32(), F32(), F32()); }
    }

    public enum PacketId : byte
    {
        // client -> server
        Hello = 1, PlayerState = 2, StartBreak = 3, FinishBreak = 4, CancelBreak = 5, PlaceBlock = 6,
        SelectSlot = 7, UseItem = 8, Interact = 9, InteractEntity = 10, InventoryClick = 11, CloseWindow = 12,
        Chat = 13, Respawn = 14, KeepAlive = 15,
        // server -> client
        Welcome = 40, ColumnData = 41, ColumnUnload = 42, BlockUpdate = 43, EntitySpawn = 44, EntityDespawn = 45,
        EntityStates = 46, PlayerTeleport = 47, HealthUpdate = 48, WindowItems = 49, SetSlot = 50, OpenWindow = 51,
        WindowProperty = 52, TimeUpdate = 53, ChatMessage = 54, EntityEvent = 55, Effect = 56, BreakProgress = 57,
        Disconnect = 58, SetCursor = 59,
    }

    public abstract class Packet
    {
        public abstract PacketId Id { get; }
        public abstract void Write(PacketWriter w);
        public abstract void Read(PacketReader r);

        public byte[] Serialize()
        {
            PacketWriter w = new PacketWriter();
            w.U8((byte)Id);
            Write(w);
            return w.ToArray();
        }

        public static Packet Deserialize(byte[] data)
        {
            PacketReader r = new PacketReader(data);
            PacketId id = (PacketId)r.U8();
            Packet p = Create(id);
            if (p == null) throw new InvalidDataException("unknown packet id " + (int)id);
            p.Read(r);
            return p;
        }

        private static Packet Create(PacketId id)
        {
            switch (id)
            {
                case PacketId.Hello: return new HelloPacket();
                case PacketId.PlayerState: return new PlayerStatePacket();
                case PacketId.StartBreak: return new StartBreakPacket();
                case PacketId.FinishBreak: return new FinishBreakPacket();
                case PacketId.CancelBreak: return new CancelBreakPacket();
                case PacketId.PlaceBlock: return new PlaceBlockPacket();
                case PacketId.SelectSlot: return new SelectSlotPacket();
                case PacketId.UseItem: return new UseItemPacket();
                case PacketId.Interact: return new InteractPacket();
                case PacketId.InteractEntity: return new InteractEntityPacket();
                case PacketId.InventoryClick: return new InventoryClickPacket();
                case PacketId.CloseWindow: return new CloseWindowPacket();
                case PacketId.Chat: return new ChatPacket();
                case PacketId.Respawn: return new RespawnPacket();
                case PacketId.KeepAlive: return new KeepAlivePacket();
                case PacketId.Welcome: return new WelcomePacket();
                case PacketId.ColumnData: return new ColumnDataPacket();
                case PacketId.ColumnUnload: return new ColumnUnloadPacket();
                case PacketId.BlockUpdate: return new BlockUpdatePacket();
                case PacketId.EntitySpawn: return new EntitySpawnPacket();
                case PacketId.EntityDespawn: return new EntityDespawnPacket();
                case PacketId.EntityStates: return new EntityStatesPacket();
                case PacketId.PlayerTeleport: return new PlayerTeleportPacket();
                case PacketId.HealthUpdate: return new HealthUpdatePacket();
                case PacketId.WindowItems: return new WindowItemsPacket();
                case PacketId.SetSlot: return new SetSlotPacket();
                case PacketId.OpenWindow: return new OpenWindowPacket();
                case PacketId.WindowProperty: return new WindowPropertyPacket();
                case PacketId.TimeUpdate: return new TimeUpdatePacket();
                case PacketId.ChatMessage: return new ChatMessagePacket();
                case PacketId.EntityEvent: return new EntityEventPacket();
                case PacketId.Effect: return new EffectPacket();
                case PacketId.BreakProgress: return new BreakProgressPacket();
                case PacketId.Disconnect: return new DisconnectPacket();
                case PacketId.SetCursor: return new SetCursorPacket();
            }
            return null;
        }
    }

    // ------------------------------------------------------------------ client -> server

    public sealed class HelloPacket : Packet
    {
        public const int ProtocolVersion = 1;
        public int Protocol = ProtocolVersion;
        public string Name = "Player";
        public override PacketId Id { get { return PacketId.Hello; } }
        public override void Write(PacketWriter w) { w.I32(Protocol); w.Str(Name); }
        public override void Read(PacketReader r) { Protocol = r.I32(); Name = r.Str(); }
    }

    [Flags]
    public enum PlayerFlags : byte { None = 0, OnGround = 1, Sprinting = 2, Sneaking = 4, Flying = 8, InWater = 16, Swinging = 32 }

    public sealed class PlayerStatePacket : Packet
    {
        public Vec3 Position;
        public float Yaw, Pitch;
        public PlayerFlags Flags;
        public override PacketId Id { get { return PacketId.PlayerState; } }
        public override void Write(PacketWriter w) { w.Vec(Position); w.F32(Yaw); w.F32(Pitch); w.U8((byte)Flags); }
        public override void Read(PacketReader r) { Position = r.Vec(); Yaw = r.F32(); Pitch = r.F32(); Flags = (PlayerFlags)r.U8(); }
    }

    public sealed class StartBreakPacket : Packet
    {
        public int X, Y, Z;
        public override PacketId Id { get { return PacketId.StartBreak; } }
        public override void Write(PacketWriter w) { w.I32(X); w.I32(Y); w.I32(Z); }
        public override void Read(PacketReader r) { X = r.I32(); Y = r.I32(); Z = r.I32(); }
    }

    public sealed class FinishBreakPacket : Packet
    {
        public int X, Y, Z;
        public override PacketId Id { get { return PacketId.FinishBreak; } }
        public override void Write(PacketWriter w) { w.I32(X); w.I32(Y); w.I32(Z); }
        public override void Read(PacketReader r) { X = r.I32(); Y = r.I32(); Z = r.I32(); }
    }

    public sealed class CancelBreakPacket : Packet
    {
        public override PacketId Id { get { return PacketId.CancelBreak; } }
        public override void Write(PacketWriter w) { }
        public override void Read(PacketReader r) { }
    }

    public sealed class PlaceBlockPacket : Packet
    {
        public int X, Y, Z;          // the block that was clicked
        public Face Face;            // which face of it
        public override PacketId Id { get { return PacketId.PlaceBlock; } }
        public override void Write(PacketWriter w) { w.I32(X); w.I32(Y); w.I32(Z); w.U8((byte)Face); }
        public override void Read(PacketReader r) { X = r.I32(); Y = r.I32(); Z = r.I32(); Face = (Face)r.U8(); }
    }

    public sealed class SelectSlotPacket : Packet
    {
        public byte Slot;
        public override PacketId Id { get { return PacketId.SelectSlot; } }
        public override void Write(PacketWriter w) { w.U8(Slot); }
        public override void Read(PacketReader r) { Slot = r.U8(); }
    }

    public sealed class UseItemPacket : Packet
    {
        public override PacketId Id { get { return PacketId.UseItem; } }
        public override void Write(PacketWriter w) { }
        public override void Read(PacketReader r) { }
    }

    public sealed class InteractPacket : Packet
    {
        public int X, Y, Z;
        public override PacketId Id { get { return PacketId.Interact; } }
        public override void Write(PacketWriter w) { w.I32(X); w.I32(Y); w.I32(Z); }
        public override void Read(PacketReader r) { X = r.I32(); Y = r.I32(); Z = r.I32(); }
    }

    public enum EntityAction : byte { Attack = 0, Interact = 1, Dismount = 2 }

    public sealed class InteractEntityPacket : Packet
    {
        public int EntityId;
        public EntityAction Action;
        public override PacketId Id { get { return PacketId.InteractEntity; } }
        public override void Write(PacketWriter w) { w.I32(EntityId); w.U8((byte)Action); }
        public override void Read(PacketReader r) { EntityId = r.I32(); Action = (EntityAction)r.U8(); }
    }

    public sealed class InventoryClickPacket : Packet
    {
        public byte Window;          // 0 = the player's own inventory window
        public short Slot;
        public byte Button;          // 0 left, 1 right
        public bool Shift;
        public override PacketId Id { get { return PacketId.InventoryClick; } }
        public override void Write(PacketWriter w) { w.U8(Window); w.I16(Slot); w.U8(Button); w.Bool(Shift); }
        public override void Read(PacketReader r) { Window = r.U8(); Slot = r.I16(); Button = r.U8(); Shift = r.Bool(); }
    }

    public sealed class CloseWindowPacket : Packet
    {
        public byte Window;
        public override PacketId Id { get { return PacketId.CloseWindow; } }
        public override void Write(PacketWriter w) { w.U8(Window); }
        public override void Read(PacketReader r) { Window = r.U8(); }
    }

    public sealed class ChatPacket : Packet
    {
        public string Text = "";
        public override PacketId Id { get { return PacketId.Chat; } }
        public override void Write(PacketWriter w) { w.Str(Text); }
        public override void Read(PacketReader r) { Text = r.Str(); }
    }

    public sealed class RespawnPacket : Packet
    {
        public override PacketId Id { get { return PacketId.Respawn; } }
        public override void Write(PacketWriter w) { }
        public override void Read(PacketReader r) { }
    }

    public sealed class KeepAlivePacket : Packet
    {
        public long Token;
        public override PacketId Id { get { return PacketId.KeepAlive; } }
        public override void Write(PacketWriter w) { w.I64(Token); }
        public override void Read(PacketReader r) { Token = r.I64(); }
    }

    // ------------------------------------------------------------------ server -> client

    public enum GameMode : byte { Survival = 0, Creative = 1 }

    public sealed class WelcomePacket : Packet
    {
        public int PlayerId;
        public long Seed;
        public Vec3 Position;
        public int DayTick;
        public GameMode Mode;
        public int ViewDistance;
        public string ServerName = "";
        public override PacketId Id { get { return PacketId.Welcome; } }
        public override void Write(PacketWriter w) { w.I32(PlayerId); w.I64(Seed); w.Vec(Position); w.I32(DayTick); w.U8((byte)Mode); w.I32(ViewDistance); w.Str(ServerName); }
        public override void Read(PacketReader r) { PlayerId = r.I32(); Seed = r.I64(); Position = r.Vec(); DayTick = r.I32(); Mode = (GameMode)r.U8(); ViewDistance = r.I32(); ServerName = r.Str(); }
    }

    public sealed class ColumnDataPacket : Packet
    {
        public int Cx, Cz;
        public byte[] Data;
        public override PacketId Id { get { return PacketId.ColumnData; } }
        public override void Write(PacketWriter w) { w.I32(Cx); w.I32(Cz); w.Bytes(Data); }
        public override void Read(PacketReader r) { Cx = r.I32(); Cz = r.I32(); Data = r.Bytes(); }
    }

    public sealed class ColumnUnloadPacket : Packet
    {
        public int Cx, Cz;
        public override PacketId Id { get { return PacketId.ColumnUnload; } }
        public override void Write(PacketWriter w) { w.I32(Cx); w.I32(Cz); }
        public override void Read(PacketReader r) { Cx = r.I32(); Cz = r.I32(); }
    }

    public sealed class BlockUpdatePacket : Packet
    {
        public int X, Y, Z;
        public byte Block;
        public override PacketId Id { get { return PacketId.BlockUpdate; } }
        public override void Write(PacketWriter w) { w.I32(X); w.I32(Y); w.I32(Z); w.U8(Block); }
        public override void Read(PacketReader r) { X = r.I32(); Y = r.I32(); Z = r.I32(); Block = r.U8(); }
    }

    public enum EntityType : byte
    {
        Player = 0, Pig = 1, Cow = 2, Sheep = 3, Chicken = 4, Zombie = 5, Skeleton = 6, Creeper = 7,
        Item = 8, Arrow = 9, Dragon = 10, Fireball = 11,
    }

    public sealed class EntitySpawnPacket : Packet
    {
        public int EntityId;
        public EntityType Type;
        public byte Variant;         // dragon kind, item id low byte is carried in Extra
        public Vec3 Position;
        public float Yaw;
        public string Name = "";
        public int Extra;            // item id for dropped items
        public override PacketId Id { get { return PacketId.EntitySpawn; } }
        public override void Write(PacketWriter w) { w.I32(EntityId); w.U8((byte)Type); w.U8(Variant); w.Vec(Position); w.F32(Yaw); w.Str(Name); w.I32(Extra); }
        public override void Read(PacketReader r) { EntityId = r.I32(); Type = (EntityType)r.U8(); Variant = r.U8(); Position = r.Vec(); Yaw = r.F32(); Name = r.Str(); Extra = r.I32(); }
    }

    public sealed class EntityDespawnPacket : Packet
    {
        public int EntityId;
        public override PacketId Id { get { return PacketId.EntityDespawn; } }
        public override void Write(PacketWriter w) { w.I32(EntityId); }
        public override void Read(PacketReader r) { EntityId = r.I32(); }
    }

    [Flags]
    public enum EntityFlags : ushort
    {
        None = 0, OnGround = 1, Burning = 2, Sneaking = 4, Sprinting = 8, Attacking = 16, Flying = 32,
        BreathingFire = 64, Hurt = 128, Ridden = 256, Tamed = 512, Fuse = 1024, InWater = 2048, Dead = 4096,
    }

    public struct EntityStateEntry
    {
        public int EntityId;
        public Vec3 Position;
        public float Yaw, Pitch;
        public EntityFlags Flags;
        public byte HealthPercent;
    }

    public sealed class EntityStatesPacket : Packet
    {
        public List<EntityStateEntry> Entries = new List<EntityStateEntry>();
        public override PacketId Id { get { return PacketId.EntityStates; } }
        public override void Write(PacketWriter w)
        {
            w.U16((ushort)Entries.Count);
            for (int i = 0; i < Entries.Count; i++)
            {
                EntityStateEntry e = Entries[i];
                w.I32(e.EntityId); w.Vec(e.Position); w.F32(e.Yaw); w.F32(e.Pitch); w.U16((ushort)e.Flags); w.U8(e.HealthPercent);
            }
        }
        public override void Read(PacketReader r)
        {
            int n = r.U16();
            Entries.Clear();
            for (int i = 0; i < n; i++)
            {
                EntityStateEntry e = new EntityStateEntry();
                e.EntityId = r.I32(); e.Position = r.Vec(); e.Yaw = r.F32(); e.Pitch = r.F32(); e.Flags = (EntityFlags)r.U16(); e.HealthPercent = r.U8();
                Entries.Add(e);
            }
        }
    }

    public sealed class PlayerTeleportPacket : Packet
    {
        public Vec3 Position;
        public override PacketId Id { get { return PacketId.PlayerTeleport; } }
        public override void Write(PacketWriter w) { w.Vec(Position); }
        public override void Read(PacketReader r) { Position = r.Vec(); }
    }

    public sealed class HealthUpdatePacket : Packet
    {
        public float Health;
        public int Hunger;
        public float Saturation;
        public bool Dead;
        public override PacketId Id { get { return PacketId.HealthUpdate; } }
        public override void Write(PacketWriter w) { w.F32(Health); w.I32(Hunger); w.F32(Saturation); w.Bool(Dead); }
        public override void Read(PacketReader r) { Health = r.F32(); Hunger = r.I32(); Saturation = r.F32(); Dead = r.Bool(); }
    }

    public sealed class WindowItemsPacket : Packet
    {
        public byte Window;
        public ItemStack[] Items = new ItemStack[0];
        public override PacketId Id { get { return PacketId.WindowItems; } }
        public override void Write(PacketWriter w) { w.U8(Window); w.U16((ushort)Items.Length); for (int i = 0; i < Items.Length; i++) w.Stack(Items[i]); }
        public override void Read(PacketReader r)
        {
            Window = r.U8(); int n = r.U16(); Items = new ItemStack[n];
            for (int i = 0; i < n; i++) Items[i] = r.Stack();
        }
    }

    public sealed class SetSlotPacket : Packet
    {
        public byte Window;
        public short Slot;
        public ItemStack Stack;
        public override PacketId Id { get { return PacketId.SetSlot; } }
        public override void Write(PacketWriter w) { w.U8(Window); w.I16(Slot); w.Stack(Stack); }
        public override void Read(PacketReader r) { Window = r.U8(); Slot = r.I16(); Stack = r.Stack(); }
    }

    public sealed class SetCursorPacket : Packet
    {
        public ItemStack Stack;
        public override PacketId Id { get { return PacketId.SetCursor; } }
        public override void Write(PacketWriter w) { w.Stack(Stack); }
        public override void Read(PacketReader r) { Stack = r.Stack(); }
    }

    public enum WindowKind : byte { Inventory = 0, CraftingTable = 1, Furnace = 2, Chest = 3 }

    public sealed class OpenWindowPacket : Packet
    {
        public byte Window;
        public WindowKind Kind;
        public string Title = "";
        public int X, Y, Z;
        public override PacketId Id { get { return PacketId.OpenWindow; } }
        public override void Write(PacketWriter w) { w.U8(Window); w.U8((byte)Kind); w.Str(Title); w.I32(X); w.I32(Y); w.I32(Z); }
        public override void Read(PacketReader r) { Window = r.U8(); Kind = (WindowKind)r.U8(); Title = r.Str(); X = r.I32(); Y = r.I32(); Z = r.I32(); }
    }

    public sealed class WindowPropertyPacket : Packet
    {
        public byte Window;
        public byte Property;        // furnace: 0 burn left, 1 burn total, 2 cook progress, 3 cook total
        public int Value;
        public override PacketId Id { get { return PacketId.WindowProperty; } }
        public override void Write(PacketWriter w) { w.U8(Window); w.U8(Property); w.I32(Value); }
        public override void Read(PacketReader r) { Window = r.U8(); Property = r.U8(); Value = r.I32(); }
    }

    public sealed class TimeUpdatePacket : Packet
    {
        public int DayTick;
        public override PacketId Id { get { return PacketId.TimeUpdate; } }
        public override void Write(PacketWriter w) { w.I32(DayTick); }
        public override void Read(PacketReader r) { DayTick = r.I32(); }
    }

    public sealed class ChatMessagePacket : Packet
    {
        public string Text = "";
        public override PacketId Id { get { return PacketId.ChatMessage; } }
        public override void Write(PacketWriter w) { w.Str(Text); }
        public override void Read(PacketReader r) { Text = r.Str(); }
    }

    public enum EntityEventKind : byte { Hurt = 0, Died = 1, Roar = 2, Swing = 3, Explode = 4, Eat = 5, Tamed = 6, Mounted = 7, Dismounted = 8 }

    public sealed class EntityEventPacket : Packet
    {
        public int EntityId;
        public EntityEventKind Event;
        public override PacketId Id { get { return PacketId.EntityEvent; } }
        public override void Write(PacketWriter w) { w.I32(EntityId); w.U8((byte)Event); }
        public override void Read(PacketReader r) { EntityId = r.I32(); Event = (EntityEventKind)r.U8(); }
    }

    public enum EffectKind : byte { BlockBreak = 0, Explosion = 1, Fire = 2, Splash = 3, Smoke = 4, Hit = 5 }

    public sealed class EffectPacket : Packet
    {
        public EffectKind Kind;
        public Vec3 Position;
        public int Data;             // block id for breaks, radius*10 for explosions
        public override PacketId Id { get { return PacketId.Effect; } }
        public override void Write(PacketWriter w) { w.U8((byte)Kind); w.Vec(Position); w.I32(Data); }
        public override void Read(PacketReader r) { Kind = (EffectKind)r.U8(); Position = r.Vec(); Data = r.I32(); }
    }

    public sealed class BreakProgressPacket : Packet
    {
        public int EntityId;         // who is breaking
        public int X, Y, Z;
        public byte Stage;           // 0..9, 255 = stopped
        public override PacketId Id { get { return PacketId.BreakProgress; } }
        public override void Write(PacketWriter w) { w.I32(EntityId); w.I32(X); w.I32(Y); w.I32(Z); w.U8(Stage); }
        public override void Read(PacketReader r) { EntityId = r.I32(); X = r.I32(); Y = r.I32(); Z = r.I32(); Stage = r.U8(); }
    }

    public sealed class DisconnectPacket : Packet
    {
        public string Reason = "";
        public override PacketId Id { get { return PacketId.Disconnect; } }
        public override void Write(PacketWriter w) { w.Str(Reason); }
        public override void Read(PacketReader r) { Reason = r.Str(); }
    }
}
