using System;
using System.IO;
using BlockRealm.Net;

namespace BlockRealm.Sim
{
    /// <summary>
    /// Saves a world to a folder: level.dat, one file per edited column (blocks and container contents) and one per player.
    /// Writes go to a temp file first so a crash never leaves a half-written save.
    /// </summary>
    public sealed class WorldStorage
    {
        public struct LevelInfo { public long Seed; public int DayTick; }

        private const uint LevelMagic = 0x314C5242;     // "BRL1"
        private const uint ColumnMagic = 0x31435242;    // "BRC1"
        private const uint PlayerMagic = 0x31505242;    // "BRP1"
        private const uint TileMagic = 0x31545242;      // "BRT1"

        public readonly string Directory;

        public WorldStorage(string dir)
        {
            Directory = dir;
            System.IO.Directory.CreateDirectory(Path.Combine(dir, "columns"));
            System.IO.Directory.CreateDirectory(Path.Combine(dir, "players"));
        }

        private static void AtomicWrite(string path, byte[] data)
        {
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private string ColumnPath(int cx, int cz, string ext) { return Path.Combine(Directory, "columns", "c." + cx + "." + cz + "." + ext); }

        // ------------------------------------------------------------------ level

        public bool TryLoadLevel(out LevelInfo level)
        {
            level = new LevelInfo();
            string path = Path.Combine(Directory, "level.dat");
            if (!File.Exists(path)) return false;
            try
            {
                using (BinaryReader r = new BinaryReader(File.OpenRead(path)))
                {
                    if (r.ReadUInt32() != LevelMagic) return false;
                    level.Seed = r.ReadInt64();
                    level.DayTick = MathUtil.Mod(r.ReadInt32(), WorldConst.DayLengthTicks);
                    return true;
                }
            }
            catch (Exception) { return false; }
        }

        public void SaveLevel(LevelInfo level)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryWriter w = new BinaryWriter(ms);
                w.Write(LevelMagic); w.Write(level.Seed); w.Write(level.DayTick);
                w.Flush();
                AtomicWrite(Path.Combine(Directory, "level.dat"), ms.ToArray());
            }
        }

        // ------------------------------------------------------------------ columns

        public Column TryLoadColumn(int cx, int cz)
        {
            string path = ColumnPath(cx, cz, "col");
            if (!File.Exists(path)) return null;
            try
            {
                byte[] file = File.ReadAllBytes(path);
                if (file.Length < 8 || file.Length > ColumnCodec.MaxEncodedBytes + 16) return null;
                uint magic = (uint)(file[0] | (file[1] << 8) | (file[2] << 16) | (file[3] << 24));
                if (magic != ColumnMagic) return null;
                byte[] body = new byte[file.Length - 4];
                Buffer.BlockCopy(file, 4, body, 0, body.Length);
                Column c = ColumnCodec.Decode(cx, cz, body);
                c.Modified = false;
                return c;
            }
            catch (Exception) { return null; }     // a damaged file falls back to fresh generation
        }

        public void SaveColumn(GameServer server, Column c)
        {
            byte[] body = ColumnCodec.Encode(c);
            byte[] file = new byte[body.Length + 4];
            file[0] = (byte)(ColumnMagic & 0xFF); file[1] = (byte)((ColumnMagic >> 8) & 0xFF); file[2] = (byte)((ColumnMagic >> 16) & 0xFF); file[3] = (byte)((ColumnMagic >> 24) & 0xFF);
            Buffer.BlockCopy(body, 0, file, 4, body.Length);
            AtomicWrite(ColumnPath(c.X, c.Z, "col"), file);

            // Container contents live in a sidecar file.
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryWriter w = new BinaryWriter(ms);
                w.Write(TileMagic);
                int furnaces = 0, chests = 0;
                foreach (var kv in server.FurnacesIn(c.X, c.Z)) furnaces++;
                foreach (var kv in server.ChestsIn(c.X, c.Z)) chests++;
                w.Write((ushort)furnaces);
                foreach (var kv in server.FurnacesIn(c.X, c.Z))
                {
                    w.Write(kv.Key.X); w.Write(kv.Key.Y); w.Write(kv.Key.Z);
                    WriteStack(w, kv.Value.Input); WriteStack(w, kv.Value.Fuel); WriteStack(w, kv.Value.Output);
                    w.Write(kv.Value.BurnLeft); w.Write(kv.Value.BurnTotal); w.Write(kv.Value.Progress);
                }
                w.Write((ushort)chests);
                foreach (var kv in server.ChestsIn(c.X, c.Z))
                {
                    w.Write(kv.Key.X); w.Write(kv.Key.Y); w.Write(kv.Key.Z);
                    for (int i = 0; i < ChestState.Size; i++) WriteStack(w, kv.Value.Slots[i]);
                }
                w.Flush();
                AtomicWrite(ColumnPath(c.X, c.Z, "te"), ms.ToArray());
            }
        }

        public void LoadTileEntities(GameServer server, Column c)
        {
            string path = ColumnPath(c.X, c.Z, "te");
            if (!File.Exists(path)) return;
            try
            {
                using (BinaryReader r = new BinaryReader(File.OpenRead(path)))
                {
                    if (r.ReadUInt32() != TileMagic) return;
                    int furnaces = r.ReadUInt16();
                    for (int i = 0; i < furnaces && i < 4096; i++)
                    {
                        Vec3i pos = new Vec3i(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
                        FurnaceState f = new FurnaceState();
                        f.Input = ReadStack(r); f.Fuel = ReadStack(r); f.Output = ReadStack(r);
                        f.BurnLeft = r.ReadInt32(); f.BurnTotal = r.ReadInt32(); f.Progress = r.ReadInt32();
                        if ((pos.X >> 4) == c.X && (pos.Z >> 4) == c.Z) server.RestoreFurnace(pos, f);
                    }
                    int chests = r.ReadUInt16();
                    for (int i = 0; i < chests && i < 4096; i++)
                    {
                        Vec3i pos = new Vec3i(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
                        ChestState ch = new ChestState();
                        for (int k = 0; k < ChestState.Size; k++) ch.Slots[k] = ReadStack(r);
                        if ((pos.X >> 4) == c.X && (pos.Z >> 4) == c.Z) server.RestoreChest(pos, ch);
                    }
                }
            }
            catch (Exception) { /* damaged sidecar: containers start empty */ }
        }

        // ------------------------------------------------------------------ players

        private string PlayerPath(string name)
        {
            string safe = Path.GetFileName(name).ToLowerInvariant();
            return Path.Combine(Directory, "players", safe + ".dat");
        }

        public void SavePlayer(ServerPlayer p)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryWriter w = new BinaryWriter(ms);
                w.Write(PlayerMagic);
                w.Write(p.Pos.X); w.Write(p.Pos.Y); w.Write(p.Pos.Z);
                w.Write(p.Yaw); w.Write(p.Pitch);
                w.Write(p.Dead ? p.MaxHealth : p.Health); w.Write(p.Hunger); w.Write(p.Saturation);
                w.Write((byte)p.Mode); w.Write((byte)p.Inventory.Selected);
                for (int i = 0; i < PlayerInventory.Size; i++) WriteStack(w, p.Inventory.Slots[i]);
                w.Flush();
                AtomicWrite(PlayerPath(p.Name), ms.ToArray());
            }
        }

        public void LoadPlayer(ServerPlayer p)
        {
            string path = PlayerPath(p.Name);
            if (!File.Exists(path)) return;
            try
            {
                using (BinaryReader r = new BinaryReader(File.OpenRead(path)))
                {
                    if (r.ReadUInt32() != PlayerMagic) return;
                    Vec3 pos = new Vec3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    float yaw = r.ReadSingle(), pitch = r.ReadSingle();
                    float health = r.ReadSingle(); int hunger = r.ReadInt32(); float sat = r.ReadSingle();
                    byte mode = r.ReadByte(); byte selected = r.ReadByte();
                    ItemStack[] slots = new ItemStack[PlayerInventory.Size];
                    for (int i = 0; i < slots.Length; i++) slots[i] = ReadStack(r);

                    if (float.IsNaN(pos.X) || float.IsNaN(pos.Y) || float.IsNaN(pos.Z) || Math.Abs(pos.X) > 3e7f || Math.Abs(pos.Z) > 3e7f || pos.Y < -50f || pos.Y > 1000f) return;
                    p.Pos = pos; p.Yaw = yaw; p.Pitch = pitch;
                    p.Health = MathUtil.Clamp(health, 1f, p.MaxHealth);
                    p.Hunger = MathUtil.Clamp(hunger, 0, 20);
                    p.Saturation = MathUtil.Clamp(sat, 0f, 20f);
                    p.Mode = mode == 1 ? GameMode.Creative : GameMode.Survival;
                    p.Inventory.Selected = Math.Min((int)selected, PlayerInventory.HotbarSize - 1);
                    Array.Copy(slots, p.Inventory.Slots, slots.Length);
                }
            }
            catch (Exception) { /* unreadable file: start fresh */ }
        }

        // ------------------------------------------------------------------ identities

        private string IdentityPath(string name) { return Path.Combine(Directory, "players", Path.GetFileName(name).ToLowerInvariant() + ".id"); }

        public string LoadIdentity(string name)
        {
            try
            {
                string path = IdentityPath(name);
                return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            }
            catch (Exception) { return null; }
        }

        public void SaveIdentity(string name, string hash)
        {
            try { File.WriteAllText(IdentityPath(name), hash); } catch (Exception) { }
        }

        // ------------------------------------------------------------------ stacks

        private static void WriteStack(BinaryWriter w, ItemStack s) { w.Write(s.Id); w.Write(s.Count); w.Write(s.Wear); }

        private static ItemStack ReadStack(BinaryReader r)
        {
            ItemStack s = new ItemStack();
            s.Id = r.ReadUInt16(); s.Count = r.ReadByte(); s.Wear = r.ReadInt16();
            // Drop anything that no longer exists, or that overflows its stack limit.
            if (s.Id != 0 && !Items.IsDefined(s.Id)) return ItemStack.Empty;
            if (!s.IsEmpty && s.Count > s.MaxStack) s.Count = (byte)s.MaxStack;
            return s;
        }
    }
}
