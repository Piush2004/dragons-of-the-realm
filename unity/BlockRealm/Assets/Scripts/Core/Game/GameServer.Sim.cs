using System;
using System.Collections.Generic;
using BlockRealm.Net;

namespace BlockRealm.Sim
{
    public sealed partial class GameServer
    {
        private readonly Dictionary<Vec3i, FurnaceState> _furnaces = new Dictionary<Vec3i, FurnaceState>();
        private readonly Dictionary<Vec3i, ChestState> _chests = new Dictionary<Vec3i, ChestState>();
        private readonly Queue<Vec3i> _updateQueue = new Queue<Vec3i>();
        private readonly HashSet<Vec3i> _updateSet = new HashSet<Vec3i>();
        private readonly Dictionary<Vec3i, int> _fires = new Dictionary<Vec3i, int>();
        private struct WaterTask { public Vec3i Pos; public int Due; }
        private readonly Queue<WaterTask> _waterQueue = new Queue<WaterTask>();
        private readonly HashSet<Vec3i> _waterScheduled = new HashSet<Vec3i>();
        private const int WaterDelayTicks = 4;

        // ------------------------------------------------------------------ blocks

        /// <summary>Edits a block and tells every player who has that column.</summary>
        public bool SetBlock(int x, int y, int z, byte id) { return World.SetBlock(x, y, z, id); }

        private void OnBlockChanged(int x, int y, int z, byte oldId, byte newId)
        {
            BlockUpdatePacket packet = new BlockUpdatePacket { X = x, Y = y, Z = z, Block = newId };
            byte[] data = packet.Serialize();
            ColumnPos cp = new ColumnPos(x >> 4, z >> 4);
            for (int i = 0; i < Players.Count; i++)
                if (Players[i].SentColumns.Contains(cp)) Players[i].Connection.Send(data);

            Vec3i pos = new Vec3i(x, y, z);
            if (newId == B.Fire) _fires[pos] = 0; else if (oldId == B.Fire) _fires.Remove(pos);
            Enqueue(pos);
            if (B.IsWater(newId)) ScheduleWater(pos);
            for (int f = 0; f < 6; f++)
            {
                Vec3i n = pos + Vec3i.FaceOffsets[f];
                Enqueue(n);
                // Water next to a change may need to flow into a new gap or drain away from a removed supply.
                if (B.IsWater(World.GetBlock(n.X, n.Y, n.Z))) ScheduleWater(n);
            }
        }

        private void ScheduleWater(Vec3i p)
        {
            if (p.Y < 0 || p.Y >= WorldConst.Height) return;
            if (_waterScheduled.Add(p)) _waterQueue.Enqueue(new WaterTask { Pos = p, Due = TickCount + WaterDelayTicks });
        }

        private static bool WaterCanEnter(byte id)
        {
            if (id == B.Air) return true;
            BlockInfo b = Blocks.All[id];
            return b != null && b.Replaceable && !b.Liquid;
        }

        /// <summary>
        /// One step of the water rules. A source is permanent. A flowing cell needs a supply (water above it, or a
        /// neighbour one level higher) or it drains a level at a time. Water falls straight down when it can, and
        /// otherwise spreads sideways losing a level per block, up to seven blocks from a source.
        /// </summary>
        private void UpdateWater(Vec3i p)
        {
            byte cur;
            if (!World.TryGetBlock(p.X, p.Y, p.Z, out cur) || !B.IsWater(cur)) return;
            int level = B.WaterLevel(cur);

            if (cur != B.Water)
            {
                int supply = 0;
                if (B.IsWater(World.GetBlock(p.X, p.Y + 1, p.Z))) supply = 7;
                for (int d = 0; d < 4; d++)
                {
                    Vec3i n = p + Vec3i.FaceOffsets[(d < 2 ? d : d + 2)];
                    int nl = B.WaterLevel(World.GetBlock(n.X, n.Y, n.Z));
                    // Water only runs sideways off a cell that rests on something; falling water does not feed its sides.
                    if (nl > 1 && !WaterCanEnter(World.GetBlock(n.X, n.Y - 1, n.Z)) && nl - 1 > supply) supply = nl - 1;
                }
                if (supply < level)
                {
                    World.SetBlock(p.X, p.Y, p.Z, supply <= 0 ? B.Air : B.FlowingWater(supply));
                    return;
                }
            }

            // Water that can go down does so and only so: a waterfall does not leak sideways along its length.
            if (p.Y > 0)
            {
                byte below = World.GetBlock(p.X, p.Y - 1, p.Z);
                if (WaterCanEnter(below))
                {
                    World.SetBlock(p.X, p.Y - 1, p.Z, B.FlowWater7);
                    return;
                }
                if (B.IsWater(below))
                {
                    if (below != B.Water && below != B.FlowWater7) World.SetBlock(p.X, p.Y - 1, p.Z, B.FlowWater7);
                    return;
                }
            }
            if (level <= 1) return;
            for (int d = 0; d < 4; d++)
            {
                Vec3i n = p + Vec3i.FaceOffsets[(d < 2 ? d : d + 2)];
                byte nb = World.GetBlock(n.X, n.Y, n.Z);
                int next = level == 8 ? 7 : level - 1;
                if (next < 1) continue;
                if (WaterCanEnter(nb) || (nb != B.Water && B.IsWater(nb) && B.WaterLevel(nb) < next))
                    World.SetBlock(n.X, n.Y, n.Z, B.FlowingWater(next));
            }
        }

        private void TickWater()
        {
            int budget = 500;
            while (_waterQueue.Count > 0 && budget-- > 0)
            {
                WaterTask t = _waterQueue.Peek();
                if (t.Due > TickCount) break;
                _waterQueue.Dequeue();
                _waterScheduled.Remove(t.Pos);
                UpdateWater(t.Pos);
            }
        }

        /// <summary>Crops grow on random samples around each player.</summary>
        private void RandomTicks()
        {
            for (int i = 0; i < Players.Count; i++)
            {
                ServerPlayer p = Players[i];
                if (p.Dead) continue;
                for (int k = 0; k < 24; k++)
                {
                    int x = MathUtil.FloorInt(p.Pos.X) + Random.Range(-48, 48);
                    int z = MathUtil.FloorInt(p.Pos.Z) + Random.Range(-48, 48);
                    GrowCropAt(x, z);
                }
            }
        }

        /// <summary>
        /// Advances the crop standing on the surface of this column by one stage if it is on farmland and lit.
        /// Returns whether it grew.
        /// </summary>
        public bool GrowCropAt(int x, int z)
        {
            if (!World.HasColumn(x >> 4, z >> 4)) return false;
            int y = World.GetSurfaceY(x, z) + 1;
            if (y < 1) return false;
            byte id = World.GetBlock(x, y, z);
            if (!B.IsWheat(id) || id == B.Wheat7) return false;
            if (World.GetBlock(x, y - 1, z) != B.Farmland) return false;
            int light = Math.Max(World.GetSkyLight(x, y, z) - (IsNight ? 11 : 0), World.GetBlockLightAt(x, y, z));
            if (light < 9) return false;
            World.SetBlock(x, y, z, (byte)(id + 1));
            return true;
        }

        private void Enqueue(Vec3i p)
        {
            if (p.Y < 0 || p.Y >= WorldConst.Height) return;
            if (_updateSet.Add(p)) _updateQueue.Enqueue(p);
        }

        private void TickBlockUpdates()
        {
            int budget = 600;
            while (budget-- > 0 && _updateQueue.Count > 0)
            {
                Vec3i p = _updateQueue.Dequeue();
                _updateSet.Remove(p);
                byte id;
                if (!World.TryGetBlock(p.X, p.Y, p.Z, out id) || id == B.Air) continue;
                BlockInfo info = Blocks.Get(id);

                if (info.Gravity)
                {
                    byte below = World.GetBlock(p.X, p.Y - 1, p.Z);
                    if (p.Y > 0 && !Blocks.IsSolid(below) && World.HasColumn(p.X >> 4, p.Z >> 4))
                    {
                        int y2 = p.Y - 1;
                        while (y2 > 0 && !Blocks.IsSolid(World.GetBlock(p.X, y2 - 1, p.Z))) y2--;
                        World.SetBlock(p.X, p.Y, p.Z, B.Air);
                        if (y2 > 0) World.SetBlock(p.X, y2, p.Z, id);
                    }
                }
                else if (info.Render == RenderKind.Cross && id != B.Fire)
                {
                    byte below = World.GetBlock(p.X, p.Y - 1, p.Z);
                    if (!Blocks.IsSolid(below))
                    {
                        ushort drop = id == B.Torch ? (ushort)B.Torch : (id == B.DeadBush ? ItemIds.Stick : (ushort)0);
                        World.SetBlock(p.X, p.Y, p.Z, B.Air);
                        if (drop != 0) DropItem(new Vec3(p.X + 0.5f, p.Y + 0.4f, p.Z + 0.5f), new ItemStack(drop, 1), new Vec3(0, 2f, 0));
                    }
                }
            }

            TickWater();
            if (TickCount % 4 == 0) RandomTicks();

            // Fire burns out and spreads to nearby flammable blocks.
            if (_fires.Count > 0 && TickCount % 10 == 0)
            {
                List<Vec3i> keys = new List<Vec3i>(_fires.Keys);
                foreach (Vec3i k in keys)
                {
                    int age = _fires[k] + 10;
                    _fires[k] = age;
                    if (World.GetBlock(k.X, k.Y, k.Z) != B.Fire) { _fires.Remove(k); continue; }
                    for (int f = 0; f < 6; f++)
                    {
                        Vec3i n = k + Vec3i.FaceOffsets[f];
                        byte nb = World.GetBlock(n.X, n.Y, n.Z);
                        if (nb != B.Air && Blocks.Get(nb).Flammable && Random.Chance(0.12f))
                        {
                            World.SetBlock(n.X, n.Y, n.Z, B.Fire);
                        }
                    }
                    byte below = World.GetBlock(k.X, k.Y - 1, k.Z);
                    bool fuel = below != B.Air && Blocks.Get(below).Flammable;
                    if (age > (fuel ? 160 : 60) + Random.NextInt(60))
                    {
                        World.SetBlock(k.X, k.Y, k.Z, B.Air);
                        if (fuel && Random.Chance(0.5f)) World.SetBlock(k.X, k.Y - 1, k.Z, B.Air);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ tile entities

        public FurnaceState EnsureFurnace(Vec3i pos)
        {
            FurnaceState f;
            if (!_furnaces.TryGetValue(pos, out f)) { f = new FurnaceState(); _furnaces[pos] = f; }
            return f;
        }

        public ChestState EnsureChest(Vec3i pos)
        {
            ChestState c;
            if (!_chests.TryGetValue(pos, out c))
            {
                c = new ChestState();
                _chests[pos] = c;
                // Chests in a dragon's nest hold its hoard.
                if (World.GetBlock(pos.X, pos.Y - 1, pos.Z) == B.ScorchedStone) FillLairLoot(c, pos);
            }
            return c;
        }

        private void FillLairLoot(ChestState c, Vec3i pos)
        {
            Rng r = new Rng((long)(Rng.Hash(Options.Seed, pos.X, pos.Z) & 0x7FFFFFFFFFFFUL));
            int slot = 0;
            Action<ushort, int, int, float> put = (id, min, max, chance) =>
            {
                if (!r.Chance(chance) || slot >= ChestState.Size) return;
                c.Slots[slot++] = new ItemStack(id, r.Range(min, max));
            };
            put(ItemIds.Diamond, 1, 4, 0.9f);
            put(ItemIds.IronIngot, 3, 10, 0.95f);
            put(ItemIds.GoldIngot, 2, 8, 0.7f);
            put(ItemIds.DragonScale, 1, 2, 0.8f);
            put(ItemIds.Saddle, 1, 1, 0.35f);
            put(ItemIds.Steak, 4, 12, 0.8f);
            put(B.Obsidian, 3, 8, 0.6f);
            put(B.Torch, 8, 24, 0.9f);
            put(ItemIds.DiamondPickaxe, 1, 1, 0.12f);
            put(ItemIds.Arrow, 8, 20, 0.6f);
        }

        private void ReleaseTileEntity(Vec3i pos, byte blockId)
        {
            Vec3 at = new Vec3(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f);
            if (blockId == B.Furnace || blockId == B.FurnaceLit)
            {
                FurnaceState f;
                if (_furnaces.TryGetValue(pos, out f))
                {
                    CloseViewers(pos);
                    DropIfAny(at, f.Input); DropIfAny(at, f.Fuel); DropIfAny(at, f.Output);
                    _furnaces.Remove(pos);
                }
            }
            else if (blockId == B.Chest)
            {
                ChestState c;
                if (_chests.TryGetValue(pos, out c))
                {
                    CloseViewers(pos);
                    for (int i = 0; i < c.Slots.Length; i++) DropIfAny(at, c.Slots[i]);
                    _chests.Remove(pos);
                }
            }
        }

        private void DropIfAny(Vec3 at, ItemStack s)
        {
            if (s.IsEmpty) return;
            DropItem(at, s, new Vec3(Random.Range(-2f, 2f), Random.Range(2f, 4f), Random.Range(-2f, 2f)));
        }

        private void CloseViewers(Vec3i pos)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                ServerPlayer p = Players[i];
                if (p.OpenWindow != null && p.OpenWindowPos.Equals(pos))
                {
                    p.OpenWindow = null;
                    p.Send(new CloseWindowPacket { Window = 0 });
                    p.InventoryDirty = true;
                }
            }
        }

        private void RemoveTileEntitiesIn(ColumnPos cp)
        {
            List<Vec3i> gone = null;
            foreach (Vec3i k in _furnaces.Keys)
                if ((k.X >> 4) == cp.X && (k.Z >> 4) == cp.Z) { if (gone == null) gone = new List<Vec3i>(); gone.Add(k); }
            if (gone != null) foreach (Vec3i k in gone) _furnaces.Remove(k);
            gone = null;
            foreach (Vec3i k in _chests.Keys)
                if ((k.X >> 4) == cp.X && (k.Z >> 4) == cp.Z) { if (gone == null) gone = new List<Vec3i>(); gone.Add(k); }
            if (gone != null) foreach (Vec3i k in gone) _chests.Remove(k);
        }

        private void TickTileEntities()
        {
            if (_furnaces.Count == 0) return;
            foreach (KeyValuePair<Vec3i, FurnaceState> kv in _furnaces)
            {
                FurnaceState f = kv.Value;
                bool wasLit = f.IsBurning;
                f.Tick();
                if (f.IsBurning != wasLit)
                {
                    byte cur = World.GetBlock(kv.Key.X, kv.Key.Y, kv.Key.Z);
                    if (cur == B.Furnace || cur == B.FurnaceLit)
                        World.SetBlock(kv.Key.X, kv.Key.Y, kv.Key.Z, f.IsBurning ? B.FurnaceLit : B.Furnace);
                }
            }
            if (TickCount % 10 == 0)
            {
                for (int i = 0; i < Players.Count; i++)
                {
                    ServerPlayer p = Players[i];
                    FurnaceWindow w = p.OpenWindow as FurnaceWindow;
                    if (w == null) continue;
                    p.Send(new WindowItemsPacket { Window = w.Id, Items = w.Snapshot() });
                    p.Send(new WindowPropertyPacket { Window = w.Id, Property = 0, Value = w.State.BurnLeft });
                    p.Send(new WindowPropertyPacket { Window = w.Id, Property = 1, Value = w.State.BurnTotal });
                    p.Send(new WindowPropertyPacket { Window = w.Id, Property = 2, Value = w.State.Progress });
                    p.Send(new WindowPropertyPacket { Window = w.Id, Property = 3, Value = FurnaceState.CookTicks });
                }
            }
        }

        // ------------------------------------------------------------------ players

        private void TickPlayer(ServerPlayer p)
        {
            if (p.Dead) { p.DeadTicks++; return; }
            p.BreakTokens = Math.Min(8f, p.BreakTokens + 0.75f);       // up to about 15 blocks a second
            p.PlaceTokens = Math.Min(10f, p.PlaceTokens + 0.9f);       // about 18 placements a second
            p.ChatTokens = Math.Min(5f, p.ChatTokens + 0.05f);         // one message a second, bursts of five
            if (p.AttackCooldown > 0) p.AttackCooldown--;
            if (p.Invulnerable > 0) p.Invulnerable--;
            if (p.HurtTicks > 0) p.HurtTicks--;
            if (TickCount % 100 == 0) p.Send(new KeepAlivePacket { Token = TickCount });

            if (p.Breaking && TickCount % 2 == 0 && p.BreakExpectedSeconds > 0.1f)
            {
                float elapsed = (TickCount - p.BreakStartTick) * Phys.Dt;
                int stage = Math.Min(9, (int)(10f * elapsed / p.BreakExpectedSeconds));
                BroadcastNear(p.Pos, 48f, new BreakProgressPacket { EntityId = p.Id, X = p.BreakPos.X, Y = p.BreakPos.Y, Z = p.BreakPos.Z, Stage = (byte)stage });
            }
            if (p.GodMode)
            {
                p.Health = p.MaxHealth; p.BurnTicks = 0;
                return;
            }

            // Hunger: activity wears down saturation, then the hunger bar.
            if (TickCount % 20 == 0)
            {
                float exhaustion = 0.1f + p.SprintTicks * 0.012f;
                p.SprintTicks = 0;
                p.Saturation -= exhaustion * 0.25f;
                if (p.Saturation < 0f)
                {
                    p.Saturation = 0f;
                    p.HungerTimer++;
                    if (p.HungerTimer >= 6) { p.HungerTimer = 0; if (p.Hunger > 0) { p.Hunger--; p.StatsDirty = true; } }
                }
            }
            if (p.Health >= p.MaxHealth) p.RegenTimer = 0; else p.RegenTimer++;
            if (p.Hunger >= 18 && p.Health < p.MaxHealth && p.RegenTimer >= 40)
            {
                p.RegenTimer = 0;
                p.Health = Math.Min(p.MaxHealth, p.Health + 1f);
                p.Saturation = Math.Max(0f, p.Saturation - 0.6f);
                p.StatsDirty = true;
            }
            else if (p.Hunger == 0 && p.RegenTimer >= 80)
            {
                p.RegenTimer = 0;
                if (p.Health > 1f) Hurt(p, 1f, null, p.Pos, 0f);
            }

            // Environment: drowning, lava, fire, cactus, the void.
            Aabb box = p.Box;
            bool lava;
            bool inLiquid = VoxelPhysics.BoxInLiquid(World, box, out lava);
            byte headBlock = World.GetBlock(MathUtil.FloorInt(p.Pos.X), MathUtil.FloorInt(p.Pos.Y + ServerPlayer.EyeHeight), MathUtil.FloorInt(p.Pos.Z));
            if (B.IsWater(headBlock))
            {
                p.Air--;
                if (p.Air <= -20) { p.Air = 0; Hurt(p, 2f, null, p.Pos, 0f); }
            }
            else p.Air = Math.Min(300, p.Air + 8);
            if (inLiquid && lava)
            {
                p.BurnTicks = Math.Max(p.BurnTicks, 100);
                if (TickCount % 10 == 0) Hurt(p, 4f, null, p.Pos, 0f);
            }
            if (p.BurnTicks > 0)
            {
                p.BurnTicks--;
                if (p.InWater || (inLiquid && !lava)) p.BurnTicks = 0;
                if (p.BurnTicks % 20 == 0 && p.BurnTicks > 0) Hurt(p, 1f, null, p.Pos, 0f);
            }
            if (TickCount % 10 == 0 && TouchesBlock(box.Expand(0.05f), B.Cactus)) Hurt(p, 1f, null, p.Pos, 0f);
            if (TouchesBlock(box, B.Fire)) p.BurnTicks = Math.Max(p.BurnTicks, 60);
            if (p.Pos.Y < -20f) Hurt(p, 4f, null, p.Pos, 0f);
        }

        private bool TouchesBlock(Aabb box, byte id)
        {
            int x0 = MathUtil.FloorInt(box.Min.X), x1 = MathUtil.FloorInt(box.Max.X);
            int y0 = MathUtil.FloorInt(box.Min.Y), y1 = MathUtil.FloorInt(box.Max.Y);
            int z0 = MathUtil.FloorInt(box.Min.Z), z1 = MathUtil.FloorInt(box.Max.Z);
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        if (World.GetBlock(x, y, z) == id) return true;
            return false;
        }

        // ------------------------------------------------------------------ damage, death and explosions

        public void Hurt(Entity target, float amount, Entity attacker, Vec3 from, float knockback)
        {
            if (target == null || target.Dead || target.Removed || amount <= 0f) return;
            ServerPlayer tp = target as ServerPlayer;
            if (tp != null)
            {
                if (tp.GodMode || tp.Invulnerable > 0) return;
                if (attacker is ServerPlayer && !Options.Pvp) return;
            }
            target.Health -= amount;
            target.HurtTicks = 10;
            if (tp != null) { tp.Invulnerable = 10; tp.StatsDirty = true; }

            Vec3 away = target.Pos - from;
            away.Y = 0;
            Vec3 dir = away.LengthSq > 1e-4f ? away.Normalized : new Vec3(0, 0, 0);
            if (knockback > 0f)
            {
                Vec3 kb = new Vec3(dir.X * knockback, 4.5f, dir.Z * knockback);
                if (tp != null) tp.Send(new KnockbackPacket { Velocity = kb });
                else if (!(target is DragonEntity)) { target.Vel.X += kb.X; target.Vel.Z += kb.Z; target.Vel.Y = Math.Max(target.Vel.Y, kb.Y); }
                else { target.Vel.X += kb.X * 0.2f; target.Vel.Z += kb.Z * 0.2f; }
            }
            MobEntity mob = target as MobEntity;
            if (mob != null && !mob.Hostile)
            {
                mob.PanicTicks = 100;
                mob.WanderYaw = (float)Math.Atan2(dir.X, dir.Z);
                if (dir.LengthSq < 1e-4f) mob.WanderYaw = Random.Range(0f, MathUtil.Pi * 2f);
            }
            DragonEntity dragon = target as DragonEntity;
            if (dragon != null) dragon.OnHurt(this, attacker, amount);

            BroadcastNear(target.Pos, 64f, new EntityEventPacket { EntityId = target.Id, Event = EntityEventKind.Hurt });
            if (target.Health <= 0f) Kill(target, attacker);
        }

        public void Kill(Entity target, Entity attacker)
        {
            if (target.Dead) return;
            target.Dead = true;
            target.Health = 0f;
            target.DeadTicks = 0;
            BroadcastNear(target.Pos, 96f, new EntityEventPacket { EntityId = target.Id, Event = EntityEventKind.Died });

            ServerPlayer p = target as ServerPlayer;
            if (p != null)
            {
                string cause = attacker == null ? " died" : (attacker is ServerPlayer ? " was slain by " + attacker.Name : " was killed by a " + attacker.Type.ToString().ToLowerInvariant());
                Broadcast(new ChatMessagePacket { Text = p.Name + cause });
                p.Breaking = false;
                p.RidingEntityId = 0;
                CloseWindow(p);
                for (int i = 0; i < p.Inventory.Slots.Length; i++)
                {
                    if (p.Inventory.Slots[i].IsEmpty) continue;
                    DropItem(p.Center, p.Inventory.Slots[i], new Vec3(Random.Range(-3f, 3f), Random.Range(2f, 5f), Random.Range(-3f, 3f)));
                    p.Inventory.Slots[i] = ItemStack.Empty;
                }
                p.StatsDirty = true; p.InventoryDirty = true;
                return;
            }

            MobEntity mob = target as MobEntity;
            if (mob != null) DropMobLoot(mob);
            DragonEntity dragon = target as DragonEntity;
            if (dragon != null) dragon.OnKilled(this, attacker);
        }

        private void DropMobLoot(MobEntity m)
        {
            Vec3 at = m.Center;
            Action<ushort, int, int> drop = (id, min, max) =>
            {
                int n = Random.Range(min, max);
                if (n > 0) DropItem(at, new ItemStack(id, n), new Vec3(Random.Range(-1.5f, 1.5f), Random.Range(2f, 4f), Random.Range(-1.5f, 1.5f)));
            };
            switch (m.Type)
            {
                case EntityType.Pig: drop(ItemIds.RawPork, 1, 3); break;
                case EntityType.Cow: drop(ItemIds.RawBeef, 1, 3); drop(ItemIds.Leather, 0, 2); break;
                case EntityType.Sheep: drop(B.Wool, 1, 2); drop(ItemIds.RawMutton, 1, 2); break;
                case EntityType.Chicken: drop(ItemIds.RawChicken, 1, 1); drop(ItemIds.Feather, 0, 2); break;
                case EntityType.Zombie: drop(ItemIds.RottenFlesh, 0, 2); if (Random.Chance(0.04f)) drop(ItemIds.IronIngot, 1, 1); break;
                case EntityType.Skeleton: drop(ItemIds.Bone, 0, 2); drop(ItemIds.Arrow, 0, 2); break;
                case EntityType.Creeper: drop(ItemIds.Gunpowder, 0, 2); break;
            }
        }

        /// <summary>A blast: damages entities by distance, optionally breaks blocks and leaves fire.</summary>
        public void Explode(Vec3 center, float radius, Entity source, float maxDamage, bool breakBlocks, bool fire)
        {
            Effect(EffectKind.Explosion, center, (int)(radius * 10f));
            float reach = radius * 2f;
            foreach (Entity e in EntitiesNear(center, reach + 2f))
            {
                if (e.Type == EntityType.Item || e.Type == EntityType.Arrow || e.Dead) continue;
                if (e == source) continue;
                float d = Vec3.Distance(e.Center, center);
                if (d > reach) continue;
                float falloff = 1f - d / reach;
                Hurt(e, maxDamage * falloff * falloff + 1f, source, center, 10f * falloff);
            }
            if (!breakBlocks) return;
            int r = (int)Math.Ceiling(radius);
            int cx = MathUtil.FloorInt(center.X), cy = MathUtil.FloorInt(center.Y), cz = MathUtil.FloorInt(center.Z);
            for (int dy = -r; dy <= r; dy++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        float d = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        if (d > radius) continue;
                        int x = cx + dx, y = cy + dy, z = cz + dz;
                        byte id = World.GetBlock(x, y, z);
                        if (id == B.Air || id == B.Bedrock) continue;
                        BlockInfo info = Blocks.Get(id);
                        if (info.Hardness > 10f || info.Hardness < 0f) continue;
                        if (info.Liquid) continue;
                        // Ragged edge: the further out, the likelier a block survives.
                        if (Random.NextFloat() < d / (radius + 0.5f) * 0.7f) continue;
                        Vec3i pos = new Vec3i(x, y, z);
                        ReleaseTileEntity(pos, id);
                        World.SetBlock(x, y, z, B.Air);
                        if (Random.Chance(0.25f))
                        {
                            ushort drop = Items.DropFor(id, true, Random, false);
                            if (drop != 0) DropItem(new Vec3(x + 0.5f, y + 0.5f, z + 0.5f), new ItemStack(drop, 1), new Vec3(Random.Range(-3f, 3f), Random.Range(2f, 6f), Random.Range(-3f, 3f)));
                        }
                    }
            if (fire)
            {
                // Scorch: set flammable blocks nearby alight and char the ground.
                for (int dy = -r; dy <= r; dy++)
                    for (int dz = -r - 1; dz <= r + 1; dz++)
                        for (int dx = -r - 1; dx <= r + 1; dx++)
                        {
                            int x = cx + dx, y = cy + dy, z = cz + dz;
                            byte id = World.GetBlock(x, y, z);
                            if (id == B.Air && IsSolidBlock(x, y - 1, z) && Random.Chance(0.18f)) World.SetBlock(x, y, z, B.Fire);
                            else if (id == B.Grass || id == B.SnowGrass) { if (Random.Chance(0.5f)) World.SetBlock(x, y, z, B.Dirt); }
                            else if (id == B.TallGrass || id == B.FlowerRed || id == B.FlowerYellow) World.SetBlock(x, y, z, B.Air);
                            else if (id == B.Snow || id == B.Ice) World.SetBlock(x, y, z, B.Air);
                        }
            }
        }

        // ------------------------------------------------------------------ ambient mobs

        private int CountMobs(bool hostile)
        {
            int n = 0;
            foreach (KeyValuePair<int, Entity> kv in Entities)
            {
                MobEntity m = kv.Value as MobEntity;
                if (m != null && !m.Dead && m.Hostile == hostile) n++;
            }
            return n;
        }

        private void SpawnMobs()
        {
            if (Players.Count == 0) return;
            int passiveCap = 6 + Players.Count * 6, hostileCap = 8 + Players.Count * 8;
            int passive = CountMobs(false), hostile = CountMobs(true);
            for (int i = 0; i < Players.Count; i++)
            {
                ServerPlayer p = Players[i];
                if (p.Dead) continue;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    bool wantHostile = Random.Chance(0.5f) && (IsNight || attempt > 1);
                    if (wantHostile && hostile >= hostileCap) continue;
                    if (!wantHostile && passive >= passiveCap) continue;

                    float ang = Random.Range(0f, MathUtil.Pi * 2f);
                    float dist = Random.Range(wantHostile ? 20f : 18f, 46f);
                    int x = MathUtil.FloorInt(p.Pos.X + (float)Math.Sin(ang) * dist);
                    int z = MathUtil.FloorInt(p.Pos.Z + (float)Math.Cos(ang) * dist);
                    if (!World.HasColumn(x >> 4, z >> 4)) continue;
                    int y;
                    if (wantHostile && (!IsNight || Random.Chance(0.3f)))
                    {
                        // Underground: look for a cave floor near the player's depth.
                        y = MathUtil.FloorInt(p.Pos.Y) + Random.Range(-14, 6);
                        int tries = 0;
                        while (tries++ < 24 && (World.GetBlock(x, y, z) != B.Air || !IsSolidBlock(x, y - 1, z))) y += Random.Range(-1, 1);
                    }
                    else
                    {
                        y = World.GetSurfaceY(x, z) + 1;
                    }
                    if (y < 2 || y >= WorldConst.Height - 3) continue;
                    byte feet = World.GetBlock(x, y, z), head = World.GetBlock(x, y + 1, z), below = World.GetBlock(x, y - 1, z);
                    if (Blocks.IsSolid(feet) || Blocks.IsLiquid(feet) || Blocks.IsSolid(head) || Blocks.IsLiquid(head)) continue;
                    if (!Blocks.IsSolid(below)) continue;

                    int skyEff = Math.Max(0, World.GetSkyLight(x, y, z) - (IsNight ? 11 : 0));
                    int light = Math.Max(skyEff, World.GetBlockLightAt(x, y, z));
                    EntityType type;
                    if (wantHostile)
                    {
                        if (light > 7) continue;
                        float r = Random.NextFloat();
                        type = r < 0.5f ? EntityType.Zombie : (r < 0.8f ? EntityType.Skeleton : EntityType.Creeper);
                    }
                    else
                    {
                        if (below != B.Grass && below != B.SnowGrass) continue;
                        if (skyEff < 9 || IsNight) continue;
                        float r = Random.NextFloat();
                        type = r < 0.3f ? EntityType.Pig : (r < 0.6f ? EntityType.Cow : (r < 0.85f ? EntityType.Sheep : EntityType.Chicken));
                    }
                    int group = wantHostile ? 1 : Random.Range(2, 4);
                    for (int g = 0; g < group; g++)
                    {
                        MobEntity m = new MobEntity(type);
                        m.Pos = new Vec3(x + 0.5f + Random.Range(-2f, 2f), y, z + 0.5f + Random.Range(-2f, 2f));
                        if (VoxelPhysics.BoxIntersectsSolid(World, m.Box)) continue;
                        m.Yaw = Random.Range(0f, MathUtil.Pi * 2f);
                        AddEntity(m);
                        if (wantHostile) hostile++; else passive++;
                    }
                    break;
                }
            }
        }

        private void DespawnFarMobs()
        {
            List<Entity> gone = null;
            foreach (KeyValuePair<int, Entity> kv in Entities)
            {
                Entity e = kv.Value;
                if (e is ServerPlayer || e is DragonEntity) continue;
                float limit = e.Type == EntityType.Item ? 128f : 100f;
                bool far = true;
                for (int i = 0; i < Players.Count; i++)
                    if (Vec3.DistanceSq(Players[i].Pos, e.Pos) < limit * limit) { far = false; break; }
                if (far)
                {
                    if (gone == null) gone = new List<Entity>();
                    gone.Add(e);
                }
            }
            if (gone != null) foreach (Entity e in gone) RemoveEntity(e);
        }

        // ------------------------------------------------------------------ persistence hooks

        public void SaveAll()
        {
            if (Storage == null) return;
            Storage.SaveLevel(new WorldStorage.LevelInfo { Seed = Options.Seed, DayTick = DayTick });
            foreach (ServerPlayer p in Players) Storage.SavePlayer(p);
            foreach (Column c in World.Columns)
                if (c.Modified) SaveColumn(c);
        }

        private void SaveColumn(Column c)
        {
            Storage.SaveColumn(this, c);
            c.Modified = false;
        }

        public IEnumerable<KeyValuePair<Vec3i, FurnaceState>> FurnacesIn(int cx, int cz)
        {
            foreach (KeyValuePair<Vec3i, FurnaceState> kv in _furnaces)
                if ((kv.Key.X >> 4) == cx && (kv.Key.Z >> 4) == cz) yield return kv;
        }

        public IEnumerable<KeyValuePair<Vec3i, ChestState>> ChestsIn(int cx, int cz)
        {
            foreach (KeyValuePair<Vec3i, ChestState> kv in _chests)
                if ((kv.Key.X >> 4) == cx && (kv.Key.Z >> 4) == cz) yield return kv;
        }

        public void RestoreFurnace(Vec3i pos, FurnaceState s) { _furnaces[pos] = s; }
        public void RestoreChest(Vec3i pos, ChestState s) { _chests[pos] = s; }
    }
}
