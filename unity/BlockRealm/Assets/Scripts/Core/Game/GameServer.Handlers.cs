using System;
using System.Collections.Generic;
using System.Text;
using BlockRealm.Net;

namespace BlockRealm.Sim
{
    public sealed partial class GameServer
    {
        // ------------------------------------------------------------------ packets

        private void ProcessPackets(ServerPlayer p)
        {
            byte[] data;
            int budget = 400;
            while (budget-- > 0 && p.Connection.TryReceive(out data))
            {
                Packet pk;
                if (!Packet.TryDeserialize(data, out pk)) { DisconnectPlayer(p, "Invalid packet"); return; }
                p.LastPacketTick = TickCount;
                try { Handle(p, pk); }
                catch (Exception) { DisconnectPlayer(p, "Server error"); return; }
                if (!Players.Contains(p)) return;
            }
            if (TickCount - p.LastPacketTick > 20 * 60) DisconnectPlayer(p, "Timed out");
        }

        private void Handle(ServerPlayer p, Packet packet)
        {
            switch (packet.Id)
            {
                case PacketId.PlayerState: OnPlayerState(p, (PlayerStatePacket)packet); break;
                case PacketId.StartBreak: OnStartBreak(p, (StartBreakPacket)packet); break;
                case PacketId.FinishBreak: OnFinishBreak(p, (FinishBreakPacket)packet); break;
                case PacketId.CancelBreak: StopBreaking(p); break;
                case PacketId.PlaceBlock: OnPlaceBlock(p, (PlaceBlockPacket)packet); break;
                case PacketId.SelectSlot:
                    p.Inventory.Selected = Math.Min((int)((SelectSlotPacket)packet).Slot, PlayerInventory.HotbarSize - 1);
                    StopBreaking(p);
                    break;
                case PacketId.UseItem: OnUseItem(p); break;
                case PacketId.Interact: OnInteract(p, (InteractPacket)packet); break;
                case PacketId.InteractEntity: OnInteractEntity(p, (InteractEntityPacket)packet); break;
                case PacketId.InventoryClick: OnInventoryClick(p, (InventoryClickPacket)packet); break;
                case PacketId.CloseWindow: CloseWindow(p); break;
                case PacketId.Chat: OnChat(p, ((ChatPacket)packet).Text); break;
                case PacketId.Respawn: if (p.Dead) RespawnPlayer(p); break;
                case PacketId.Wake: WakePlayer(p); break;
                case PacketId.KeepAlive: break;
                case PacketId.Hello: break;   // duplicate hello is ignored
                default: break;               // server -> client ids are never valid here
            }
        }

        // ------------------------------------------------------------------ movement

        private void OnPlayerState(ServerPlayer p, PlayerStatePacket st)
        {
            if (p.Dead) return;
            Vec3 pos = st.Position;
            if (Math.Abs(pos.X) > 3e7f || Math.Abs(pos.Z) > 3e7f || pos.Y < -100f || pos.Y > 5000f) return;
            PlayerFlags flags = st.Flags;
            // Flight is a creative-mode privilege; a survival client cannot simply claim it.
            if (!p.GodMode) flags &= ~PlayerFlags.Flying;

            bool riding = p.RidingEntityId != 0;
            bool flyingNow = (flags & PlayerFlags.Flying) != 0;
            // Allowed distance since the last accepted state, per tick. Elapsed time is capped so that going quiet
            // does not bank up a long free jump.
            float ticks = Math.Min(Math.Max(1, TickCount - p.LastStateTick), 10);
            float horizontalPerTick = riding ? 2.2f : (flyingNow ? 1.5f : 0.65f);
            float upPerTick = riding ? 2.2f : (flyingNow ? 0.9f : 0.7f);
            float downPerTick = 4.2f;                                  // terminal fall speed is ~3.9 blocks per tick
            Vec3 delta = pos - p.Pos;
            float flat = (float)Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
            bool tooFar = flat > horizontalPerTick * ticks + 1.5f ||
                          delta.Y > upPerTick * ticks + 1.5f || -delta.Y > downPerTick * ticks + 1.5f;
            // Walking into solid terrain is rejected, unless the player was already inside it (sand landed on them).
            if (!tooFar && !p.GodMode && !riding)
            {
                Aabb now = new Aabb(new Vec3(pos.X - 0.25f, pos.Y + 0.05f, pos.Z - 0.25f), new Vec3(pos.X + 0.25f, pos.Y + 1.7f, pos.Z + 0.25f));
                if (VoxelPhysics.BoxIntersectsSolid(World, now))
                {
                    Aabb before = new Aabb(new Vec3(p.Pos.X - 0.25f, p.Pos.Y + 0.05f, p.Pos.Z - 0.25f), new Vec3(p.Pos.X + 0.25f, p.Pos.Y + 1.7f, p.Pos.Z + 0.25f));
                    if (!VoxelPhysics.BoxIntersectsSolid(World, before)) tooFar = true;
                }
            }
            if (tooFar)
            {
                // Not real movement: pull the player back to where the server last had them (at most a few times a second).
                if (TickCount - p.LastCorrectionTick >= 4)
                {
                    p.LastCorrectionTick = TickCount;
                    p.Send(new PlayerTeleportPacket { Position = p.Pos });
                }
                return;
            }
            st.Flags = flags;
            bool wasOnGround = p.ClientOnGround;
            p.Pos = pos;
            p.Yaw = st.Yaw; p.Pitch = st.Pitch;
            p.ReportedFlags = st.Flags;
            p.ClientOnGround = (st.Flags & PlayerFlags.OnGround) != 0;
            p.InWater = (st.Flags & PlayerFlags.InWater) != 0;
            p.LastStateTick = TickCount;
            p.LastStatePos = pos;

            // Fall damage: the drop since the highest point of the fall, when landing on solid ground.
            bool flying = (st.Flags & PlayerFlags.Flying) != 0;
            if (flying || p.InWater || p.RidingEntityId != 0) p.FallPeakY = pos.Y;
            else if (p.ClientOnGround)
            {
                float fall = p.FallPeakY - pos.Y;
                if (fall > 3.2f && !p.GodMode) Hurt(p, (float)Math.Floor(fall - 3f), null, pos, 0f);
                p.FallPeakY = pos.Y;
            }
            else if (pos.Y > p.FallPeakY) p.FallPeakY = pos.Y;

            if ((st.Flags & PlayerFlags.Sprinting) != 0) p.SprintTicks++;

            if (p.RidingEntityId != 0)
            {
                DragonEntity d = GetEntity(p.RidingEntityId) as DragonEntity;
                if (d != null && d.RiderId == p.Id) d.FollowRider(p);
                else p.RidingEntityId = 0;
            }
        }

        // ------------------------------------------------------------------ mining and building

        private void StopBreaking(ServerPlayer p)
        {
            if (!p.Breaking) return;
            p.Breaking = false;
            BroadcastNear(p.Pos, 48f, new BreakProgressPacket { EntityId = p.Id, X = p.BreakPos.X, Y = p.BreakPos.Y, Z = p.BreakPos.Z, Stage = 255 });
        }

        private static bool WithinReach(ServerPlayer p, int x, int y, int z)
        {
            Vec3 c = new Vec3(x + 0.5f, y + 0.5f, z + 0.5f);
            return Vec3.Distance(p.Eye, c) <= ServerPlayer.Reach + 1.2f;
        }

        private void OnStartBreak(ServerPlayer p, StartBreakPacket pk)
        {
            if (p.Dead || !WithinReach(p, pk.X, pk.Y, pk.Z)) return;
            byte id = World.GetBlock(pk.X, pk.Y, pk.Z);
            if (id == B.Air || Blocks.IsLiquid(id)) return;
            ToolKind kind; int tier; p.GetTool(out kind, out tier);
            float t = p.GodMode ? 0f : Blocks.BreakTime(id, kind, tier);
            if (t < 0f) return;
            p.Breaking = true;
            p.BreakPos = new Vec3i(pk.X, pk.Y, pk.Z);
            p.BreakStartTick = TickCount;
            p.BreakExpectedSeconds = t;
        }

        private void OnFinishBreak(ServerPlayer p, FinishBreakPacket pk)
        {
            if (p.Dead || !WithinReach(p, pk.X, pk.Y, pk.Z)) { ResyncBlock(p, pk.X, pk.Y, pk.Z); return; }
            if (p.BreakTokens < 1f) { ResyncBlock(p, pk.X, pk.Y, pk.Z); return; }      // breaking far faster than a hand can
            p.BreakTokens -= 1f;
            byte id = World.GetBlock(pk.X, pk.Y, pk.Z);
            if (id == B.Air || Blocks.IsLiquid(id) || id == B.Bedrock) { ResyncBlock(p, pk.X, pk.Y, pk.Z); return; }

            ToolKind kind; int tier; p.GetTool(out kind, out tier);
            float expected = p.GodMode ? 0f : Blocks.BreakTime(id, kind, tier);
            if (expected < 0f) { ResyncBlock(p, pk.X, pk.Y, pk.Z); return; }
            if (!p.GodMode && expected > 0.12f)
            {
                bool sameBlock = p.Breaking && p.BreakPos.X == pk.X && p.BreakPos.Y == pk.Y && p.BreakPos.Z == pk.Z;
                float elapsed = (TickCount - p.BreakStartTick) * Phys.Dt;
                // 40% leeway for latency and tick quantisation, never accepting an instant break of something slow.
                if (!sameBlock || elapsed < expected * 0.6f - 0.1f) { ResyncBlock(p, pk.X, pk.Y, pk.Z); return; }
            }
            StopBreaking(p);
            BreakBlockAs(p, pk.X, pk.Y, pk.Z);
        }

        private void ResyncBlock(ServerPlayer p, int x, int y, int z)
        {
            p.Send(new BlockUpdatePacket { X = x, Y = y, Z = z, Block = World.GetBlock(x, y, z) });
        }

        public void BreakBlockAs(ServerPlayer p, int x, int y, int z)
        {
            byte id = World.GetBlock(x, y, z);
            if (id == B.Air || id == B.Bedrock) return;
            ToolKind kind; int tier; p.GetTool(out kind, out tier);
            bool harvested = !p.GodMode && Blocks.Harvests(id, kind, tier);
            ushort drop = Items.DropFor(id, harvested, Random, kind == ToolKind.Shears);

            Vec3i pos = new Vec3i(x, y, z);
            ReleaseTileEntity(pos, id);
            SetBlock(x, y, z, B.Air);
            Effect(EffectKind.BlockBreak, new Vec3(x + 0.5f, y + 0.5f, z + 0.5f), id);
            if (id == B.Wheat7 && !p.GodMode)
                DropItem(new Vec3(x + 0.5f, y + 0.4f, z + 0.5f), new ItemStack(ItemIds.Seeds, Random.Range(1, 2)), new Vec3(0, 2.5f, 0));
            if (drop != 0)
            {
                int count = Blocks.Get(id).DropCount;
                if (id == B.DiamondOre || id == B.CoalOre) count = 1;
                DropItem(new Vec3(x + 0.5f, y + 0.5f, z + 0.5f), new ItemStack(drop, count),
                    new Vec3(Random.Range(-1.5f, 1.5f), Random.Range(2f, 4f), Random.Range(-1.5f, 1.5f)));
            }
            if (!p.GodMode)
            {
                if (Blocks.Get(id).Hardness > 0f && p.Held.Info.Durability > 0)
                {
                    p.Inventory.WearHeld();
                    p.InventoryDirty = true;
                }
                p.SprintTicks += 6;       // mining is hungry work
            }
        }

        private void OnPlaceBlock(ServerPlayer p, PlaceBlockPacket pk)
        {
            if (p.Dead) return;
            if (!WithinReach(p, pk.X, pk.Y, pk.Z)) return;
            if (p.PlaceTokens < 1f) { ResyncBlock(p, pk.X, pk.Y, pk.Z); return; }
            p.PlaceTokens -= 1f;
            ItemStack held = p.Held;
            if (held.IsEmpty || held.Id >= 256 || !Blocks.IsDefined(held.Id)) return;
            byte block = (byte)held.Id;
            BlockInfo info = Blocks.Get(block);
            if (info.Render == RenderKind.None) return;

            int tx, ty, tz;
            byte clicked = World.GetBlock(pk.X, pk.Y, pk.Z);
            if (Blocks.Get(clicked).Replaceable && !Blocks.IsLiquid(clicked)) { tx = pk.X; ty = pk.Y; tz = pk.Z; }
            else
            {
                Vec3i n = Vec3i.FaceOffsets[(int)pk.Face];
                tx = pk.X + n.X; ty = pk.Y + n.Y; tz = pk.Z + n.Z;
            }
            if (ty < 0 || ty >= WorldConst.Height) return;
            if (!World.HasColumn(tx >> 4, tz >> 4)) return;
            byte existing = World.GetBlock(tx, ty, tz);
            if (!Blocks.Get(existing).Replaceable) { ResyncBlock(p, tx, ty, tz); return; }

            // Solid blocks cannot be placed inside a player or mob.
            if (info.Solid)
            {
                Aabb cell = new Aabb(new Vec3(tx, ty, tz), new Vec3(tx + 1, ty + 1, tz + 1));
                foreach (Entity e in EntitiesNear(new Vec3(tx + 0.5f, ty + 0.5f, tz + 0.5f), 3f))
                {
                    if (e.Type == EntityType.Item || e.Type == EntityType.Arrow || e.Dead) continue;
                    if (e.Box.Intersects(cell)) { ResyncBlock(p, tx, ty, tz); return; }
                }
            }
            if (block == B.Torch && !IsSolidBlock(tx, ty - 1, tz)) { ResyncBlock(p, tx, ty, tz); return; }
            if (info.Render == RenderKind.Cross && block != B.Torch && !IsSolidBlock(tx, ty - 1, tz)) { ResyncBlock(p, tx, ty, tz); return; }

            SetBlock(tx, ty, tz, block);
            if (block == B.Furnace) EnsureFurnace(new Vec3i(tx, ty, tz));
            if (block == B.Chest) EnsureChest(new Vec3i(tx, ty, tz));
            if (!p.GodMode)
            {
                p.Inventory.ConsumeHeld();
                p.InventoryDirty = true;
            }
        }

        private bool IsSolidBlock(int x, int y, int z) { return Blocks.IsSolid(World.GetBlock(x, y, z)); }

        // ------------------------------------------------------------------ items

        private void OnUseItem(ServerPlayer p)
        {
            if (p.Dead) return;
            ItemStack held = p.Held;
            if (held.IsEmpty) return;
            ItemInfo info = held.Info;
            if (UseTargetedItem(p, held)) return;
            if (info.FoodHunger > 0 && (p.Hunger < 20 || p.GodMode))
            {
                p.Hunger = Math.Min(20, p.Hunger + info.FoodHunger);
                p.Saturation = Math.Min(p.Hunger, p.Saturation + info.FoodSaturation);
                if (held.Id == ItemIds.RottenFlesh && Random.Chance(0.8f)) Hurt(p, 1f, null, p.Pos, 0f);
                if (!p.GodMode) p.Inventory.ConsumeHeld();
                p.InventoryDirty = true; p.StatsDirty = true;
                Broadcast(new EntityEventPacket { EntityId = p.Id, Event = EntityEventKind.Eat });
            }
            else if (held.Id == ItemIds.Bow)
            {
                if (!p.GodMode && p.Inventory.Count(ItemIds.Arrow) == 0) return;
                if (p.AttackCooldown > 0) return;
                if (!p.GodMode) { p.Inventory.Remove(ItemIds.Arrow, 1); p.Inventory.WearHeld(); }
                Vec3 dir = p.LookDirection;
                FireArrow(p, p.Eye + dir * 0.5f, dir * 38f, 6f);
                p.AttackCooldown = 12;
                p.InventoryDirty = true;
            }
        }

        /// <summary>Items that act on the block the player is looking at: buckets, hoes and seeds.</summary>
        private bool UseTargetedItem(ServerPlayer p, ItemStack held)
        {
            ushort id = held.Id;
            bool bucket = id == ItemIds.Bucket, water = id == ItemIds.WaterBucket;
            bool hoe = held.Info.Tool == ToolKind.Hoe, seeds = id == ItemIds.Seeds;
            if (!bucket && !water && !hoe && !seeds) return false;
            if (p.AttackCooldown > 0) return true;

            RayHit h = VoxelPhysics.Raycast(World, p.Eye, p.LookDirection, ServerPlayer.Reach, bucket);
            if (!h.Hit) return true;
            p.AttackCooldown = 4;

            if (bucket)
            {
                if (World.GetBlock(h.X, h.Y, h.Z) != B.Water) return true;          // only a full source can be scooped
                SetBlock(h.X, h.Y, h.Z, B.Air);
                SwapHeld(p, ItemIds.WaterBucket);
                Effect(EffectKind.Splash, new Vec3(h.X + 0.5f, h.Y + 0.5f, h.Z + 0.5f), 0);
            }
            else if (water)
            {
                int x = h.PlaceX, y = h.PlaceY, z = h.PlaceZ;
                if (y < 0 || y >= WorldConst.Height || !World.HasColumn(x >> 4, z >> 4)) return true;
                if (!WaterCanEnter(World.GetBlock(x, y, z))) return true;
                SetBlock(x, y, z, B.Water);
                SwapHeld(p, ItemIds.Bucket);
                Effect(EffectKind.Splash, new Vec3(x + 0.5f, y + 0.5f, z + 0.5f), 0);
            }
            else if (hoe)
            {
                byte id0 = World.GetBlock(h.X, h.Y, h.Z);
                if (id0 != B.Grass && id0 != B.Dirt && id0 != B.SnowGrass) return true;
                if (World.GetBlock(h.X, h.Y + 1, h.Z) != B.Air) return true;
                SetBlock(h.X, h.Y, h.Z, B.Farmland);
                if (!p.GodMode) { p.Inventory.WearHeld(); p.InventoryDirty = true; }
            }
            else
            {
                if (World.GetBlock(h.X, h.Y, h.Z) != B.Farmland) return true;
                if (World.GetBlock(h.X, h.Y + 1, h.Z) != B.Air) return true;
                SetBlock(h.X, h.Y + 1, h.Z, B.Wheat0);
                if (!p.GodMode) { p.Inventory.ConsumeHeld(); p.InventoryDirty = true; }
            }
            return true;
        }

        private void SwapHeld(ServerPlayer p, ushort newItem)
        {
            if (p.GodMode) return;
            p.Inventory.ConsumeHeld();
            if (p.Inventory.Held.IsEmpty) p.Inventory.Slots[p.Inventory.Selected] = new ItemStack(newItem, 1);
            else
            {
                int left = p.Inventory.Add(new ItemStack(newItem, 1));
                if (left > 0) DropItem(p.Eye, new ItemStack(newItem, 1), p.LookDirection * 3f);
            }
            p.InventoryDirty = true;
        }

        public void DropItem(Vec3 pos, ItemStack stack, Vec3 vel)
        {
            if (stack.IsEmpty) return;
            ItemEntity e = new ItemEntity(stack);
            e.Pos = pos; e.Vel = vel;
            AddEntity(e);
        }

        public void FireArrow(Entity shooter, Vec3 pos, Vec3 vel, float damage)
        {
            ArrowEntity a = new ArrowEntity();
            a.Pos = pos; a.Vel = vel; a.ShooterId = shooter.Id; a.Damage = damage;
            AddEntity(a);
        }

        // ------------------------------------------------------------------ interaction with blocks and entities

        private void OnInteract(ServerPlayer p, InteractPacket pk)
        {
            if (p.Dead || !WithinReach(p, pk.X, pk.Y, pk.Z)) return;
            byte id = World.GetBlock(pk.X, pk.Y, pk.Z);
            Vec3i pos = new Vec3i(pk.X, pk.Y, pk.Z);
            switch (id)
            {
                case B.CraftingTable: OpenContainer(p, WindowKind.CraftingTable, pos, "Crafting"); break;
                case B.Furnace:
                case B.FurnaceLit: OpenContainer(p, WindowKind.Furnace, pos, "Furnace"); break;
                case B.Chest: OpenContainer(p, WindowKind.Chest, pos, "Chest"); break;
                case B.Bed: UseBed(p, pos); break;
            }
        }

        private void OpenContainer(ServerPlayer p, WindowKind kind, Vec3i pos, string title)
        {
            CloseWindow(p);
            byte wid = p.NextWindowId++;
            if (p.NextWindowId > 200) p.NextWindowId = 1;
            Window w;
            switch (kind)
            {
                case WindowKind.CraftingTable: w = new CraftingWindow(wid, kind, p.Inventory, 3); break;
                case WindowKind.Furnace: w = new FurnaceWindow(wid, p.Inventory, EnsureFurnace(pos)); break;
                case WindowKind.Chest: w = new ChestWindow(wid, p.Inventory, EnsureChest(pos)); break;
                default: return;
            }
            p.OpenWindow = w;
            p.OpenWindowPos = pos;
            p.Send(new OpenWindowPacket { Window = wid, Kind = kind, Title = title, X = pos.X, Y = pos.Y, Z = pos.Z });
            SendFullInventory(p);
        }

        public void CloseWindow(ServerPlayer p)
        {
            DropOpenWindowContents(p);
            p.OpenWindow = null;
            // The personal crafting grid also returns its items when the inventory screen closes.
            ReturnToPlayer(p, p.Personal.TakeLeftovers());
            ItemStack cursor = p.Inventory.Cursor;
            if (!cursor.IsEmpty)
            {
                p.Inventory.Cursor = ItemStack.Empty;
                ReturnToPlayer(p, new List<ItemStack> { cursor });
            }
            p.InventoryDirty = true;
        }

        private void DropOpenWindowContents(ServerPlayer p)
        {
            if (p.OpenWindow == null) return;
            ReturnToPlayer(p, p.OpenWindow.TakeLeftovers());
        }

        private void ReturnToPlayer(ServerPlayer p, List<ItemStack> stacks)
        {
            foreach (ItemStack s in stacks)
            {
                int left = p.Inventory.Add(s);
                if (left > 0)
                {
                    ItemStack rest = s; rest.Count = (byte)left;
                    DropItem(p.Eye, rest, p.LookDirection * 4f);
                }
            }
        }

        /// <summary>Window id used by the creative item palette: Slot is the item id, Button 1 asks for a single item.</summary>
        public const byte CreativePaletteWindow = 253;

        private void OnInventoryClick(ServerPlayer p, InventoryClickPacket pk)
        {
            if (pk.Window == CreativePaletteWindow)
            {
                if (!p.GodMode || pk.Slot <= 0 || pk.Slot > ItemIds.MaxId || !Items.IsDefined((ushort)pk.Slot)) return;
                ushort pick = (ushort)pk.Slot;
                int count = pk.Button == 1 ? 1 : Items.Get(pick).MaxStack;
                p.Inventory.Cursor = new ItemStack(pick, count);
                p.InventoryDirty = true;
                return;
            }
            int armorBefore = p.Inventory.ArmorPoints();
            Window w = null;
            if (p.OpenWindow != null && p.OpenWindow.Id == pk.Window) w = p.OpenWindow;
            else if (pk.Window == 0 && p.OpenWindow == null) w = p.Personal;
            if (w == null) { SendFullInventory(p); return; }

            if (pk.Slot == -1)
            {
                // Click outside the window: throw the cursor stack (or one item).
                ItemStack cur = p.Inventory.Cursor;
                if (!cur.IsEmpty)
                {
                    ItemStack drop = cur;
                    if (pk.Button == 1) { drop.Count = 1; cur.Count--; p.Inventory.Cursor = cur.Count == 0 ? ItemStack.Empty : cur; }
                    else p.Inventory.Cursor = ItemStack.Empty;
                    DropItem(p.Eye, drop, p.LookDirection * 5f + new Vec3(0, 1.5f, 0));
                }
            }
            else if (pk.Slot == -2 || pk.Slot == -3)
            {
                ItemStack held = p.Inventory.Held;
                if (!held.IsEmpty)
                {
                    ItemStack drop = held;
                    if (pk.Slot == -2) { drop.Count = 1; held.Count--; p.Inventory.Slots[p.Inventory.Selected] = held.Count == 0 ? ItemStack.Empty : held; }
                    else p.Inventory.Slots[p.Inventory.Selected] = ItemStack.Empty;
                    DropItem(p.Eye, drop, p.LookDirection * 5f + new Vec3(0, 1.5f, 0));
                }
            }
            else
            {
                w.Click(pk.Slot, pk.Button, pk.Shift);
            }
            if (p.Inventory.ArmorPoints() != armorBefore) p.StatsDirty = true;
            p.InventoryDirty = true;
        }

        private void OnInteractEntity(ServerPlayer p, InteractEntityPacket pk)
        {
            if (p.Dead) return;
            if (pk.Action == EntityAction.Dismount)
            {
                DragonEntity ride = GetEntity(p.RidingEntityId) as DragonEntity;
                if (ride != null) ride.Dismount(this, p);
                p.RidingEntityId = 0;
                return;
            }
            Entity e = GetEntity(pk.EntityId);
            if (e == null || e.Removed || e == p) return;
            if (Vec3.Distance(p.Eye, e.Center) > 6.5f + e.HalfWidth + e.Height * 0.5f) return;

            if (pk.Action == EntityAction.Attack)
            {
                if (p.AttackCooldown > 0) return;
                if (e is ServerPlayer && !Options.Pvp) return;
                if (!e.Hittable) return;
                ItemInfo info = p.Held.Info;
                float dmg = info.Attack > 1f && info.Tool != ToolKind.None ? info.Attack : 1f;
                if (info.Tool == ToolKind.Pickaxe || info.Tool == ToolKind.Shovel) dmg = Math.Max(1f, info.Attack * 0.7f);
                p.AttackCooldown = 8;
                Hurt(e, dmg, p, p.Pos, 7f);
                if (!p.GodMode && info.Durability > 0 && info.Tool != ToolKind.None)
                {
                    p.Inventory.WearHeld(); p.InventoryDirty = true;
                }
                p.SprintTicks += 4;
            }
            else
            {
                e.Interact(this, p);
            }
        }

        // ------------------------------------------------------------------ chat and commands

        private static string CleanChat(string text)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in text)
            {
                if (ch < 32 || ch == 127) continue;
                sb.Append(ch);
                if (sb.Length >= 200) break;
            }
            return sb.ToString().Trim();
        }

        private void Tell(ServerPlayer p, string text) { p.Send(new ChatMessagePacket { Text = text }); }

        private void OnChat(ServerPlayer p, string raw)
        {
            string text = CleanChat(raw);
            if (text.Length == 0) return;
            if (p.ChatTokens < 1f) { Tell(p, "You are sending messages too quickly."); return; }
            p.ChatTokens -= 1f;
            if (text[0] == '/') { RunCommand(p, text.Substring(1)); return; }
            Broadcast(new ChatMessagePacket { Text = "<" + p.Name + "> " + text });
        }

        public static ushort ParseItem(string name)
        {
            string key = name.Replace(" ", "").Replace("_", "").ToLowerInvariant();
            for (int i = 1; i < 256; i++)
            {
                BlockInfo b = Blocks.All[i];
                if (b != null && b.Name.Replace(" ", "").ToLowerInvariant() == key) return (ushort)i;
            }
            for (int i = 256; i <= ItemIds.MaxId; i++)
            {
                if (!Items.IsDefined((ushort)i)) continue;
                if (Items.Get((ushort)i).Name.Replace(" ", "").ToLowerInvariant() == key) return (ushort)i;
            }
            return 0;
        }

        private void RunCommand(ServerPlayer p, string line)
        {
            if (!Options.AllowCommands || !p.Op) { Tell(p, "You are not allowed to use commands."); return; }
            string[] a = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0) return;
            string cmd = a[0].ToLowerInvariant();
            switch (cmd)
            {
                case "help":
                    Tell(p, "/gamemode survival|creative, /time set day|night|<0-24000>, /tp <x> <y> <z>|<player>, /give <item> [n],");
                    Tell(p, "/summon <pig|cow|sheep|chicken|zombie|skeleton|creeper|cinderwing|brazenhide|elderscale>, /heal, /kill, /spawn, /seed");
                    break;
                case "gamemode":
                case "gm":
                    if (a.Length < 2) { Tell(p, "Usage: /gamemode survival|creative"); break; }
                    p.Mode = a[1].StartsWith("c", StringComparison.OrdinalIgnoreCase) ? GameMode.Creative : GameMode.Survival;
                    p.Send(new WelcomePacket
                    {
                        PlayerId = p.Id, Seed = Options.Seed, Position = p.Pos, DayTick = DayTick, Mode = p.Mode,
                        ViewDistance = Options.ViewDistance, ServerName = Options.Name,
                    });
                    Tell(p, "Game mode set to " + p.Mode);
                    break;
                case "time":
                    if (a.Length >= 3 && a[1] == "set")
                    {
                        int t;
                        if (a[2] == "day") t = 1000;
                        else if (a[2] == "noon") t = 6000;
                        else if (a[2] == "night") t = 14000;
                        else if (a[2] == "midnight") t = 18000;
                        else if (!int.TryParse(a[2], out t)) { Tell(p, "Unknown time"); break; }
                        DayTick = MathUtil.Mod(t, WorldConst.DayLengthTicks);
                        Broadcast(new TimeUpdatePacket { DayTick = DayTick });
                    }
                    break;
                case "tp":
                    if (a.Length >= 4)
                    {
                        float x, y, z;
                        if (float.TryParse(a[1], out x) && float.TryParse(a[2], out y) && float.TryParse(a[3], out z))
                            TeleportPlayer(p, new Vec3(x, y, z));
                    }
                    else if (a.Length == 2)
                    {
                        ServerPlayer t = FindPlayer(a[1]);
                        if (t != null) TeleportPlayer(p, t.Pos + new Vec3(0, 0.1f, 0));
                        else Tell(p, "No such player");
                    }
                    break;
                case "give":
                    if (a.Length >= 2)
                    {
                        ushort id;
                        if (!ushort.TryParse(a[1], out id)) id = ParseItem(a[1]);
                        if (id == 0 || !Items.IsDefined(id)) { Tell(p, "Unknown item"); break; }
                        int n = 1;
                        if (a.Length >= 3) int.TryParse(a[2], out n);
                        n = MathUtil.Clamp(n, 1, 64);
                        int left = p.Inventory.Add(new ItemStack(id, n));
                        p.InventoryDirty = true;
                        Tell(p, "Gave " + (n - left) + " x " + Items.Get(id).Name);
                    }
                    break;
                case "summon":
                    if (a.Length >= 2) Summon(p, a[1].ToLowerInvariant());
                    break;
                case "heal":
                    p.Health = p.MaxHealth; p.Hunger = 20; p.Saturation = 5f; p.StatsDirty = true;
                    break;
                case "kill":
                    Hurt(p, 1000f, null, p.Pos, 0f);
                    break;
                case "spawn":
                    TeleportPlayer(p, new Vec3(SpawnPoint.X + 0.5f, Math.Max(SpawnPoint.Y, World.GetSurfaceY(SpawnPoint.X, SpawnPoint.Z) + 1) + 0.2f, SpawnPoint.Z + 0.5f));
                    break;
                case "seed":
                    Tell(p, "Seed: " + Options.Seed);
                    break;
                default:
                    Tell(p, "Unknown command. Try /help");
                    break;
            }
        }

        public void TeleportPlayer(ServerPlayer p, Vec3 pos)
        {
            p.Pos = pos; p.FallPeakY = pos.Y; p.LastStatePos = pos; p.LastStateTick = TickCount;
            p.Send(new PlayerTeleportPacket { Position = pos });
        }

        private void Summon(ServerPlayer p, string what)
        {
            Vec3 at = p.Pos + p.LookDirection.Normalized * 4f;
            at.Y = p.Pos.Y + 1f;
            Entity e = null;
            switch (what)
            {
                case "pig": e = new MobEntity(EntityType.Pig); break;
                case "cow": e = new MobEntity(EntityType.Cow); break;
                case "sheep": e = new MobEntity(EntityType.Sheep); break;
                case "chicken": e = new MobEntity(EntityType.Chicken); break;
                case "zombie": e = new MobEntity(EntityType.Zombie); break;
                case "skeleton": e = new MobEntity(EntityType.Skeleton); break;
                case "creeper": e = new MobEntity(EntityType.Creeper); break;
                case "cinderwing": e = new DragonEntity(0); at.Y += 6; break;
                case "brazenhide": e = new DragonEntity(1); at.Y += 6; break;
                case "elderscale": e = new DragonEntity(2); at.Y += 6; break;
            }
            if (e == null) { Tell(p, "Cannot summon " + what); return; }
            e.Pos = at;
            AddEntity(e);
            DragonEntity dragon = e as DragonEntity;
            if (dragon != null) dragon.Summoned = true;
        }

        public void RespawnPlayer(ServerPlayer p)
        {
            p.Dead = false; p.DeadTicks = 0;
            p.Health = p.MaxHealth; p.Hunger = 20; p.Saturation = 5f; p.Air = 300;
            p.BurnTicks = 0; p.Invulnerable = 60;
            p.Sleeping = false;
            Vec3 pos;
            if (!TryBedRespawn(p, out pos))
            {
                int surface = World.GetSurfaceY(SpawnPoint.X, SpawnPoint.Z);
                pos = new Vec3(SpawnPoint.X + 0.5f, (surface >= 0 ? surface + 1 : SpawnPoint.Y) + 0.2f, SpawnPoint.Z + 0.5f);
            }
            TeleportPlayer(p, pos);
            p.StatsDirty = true;
            p.InventoryDirty = true;
        }

        // ------------------------------------------------------------------ beds

        /// <summary>Using a bed always sets the respawn point; at night, with no monsters near, it also puts you to sleep.</summary>
        private void UseBed(ServerPlayer p, Vec3i pos)
        {
            if (p.Sleeping) { WakePlayer(p); return; }
            bool changed = !p.HasBed || !p.BedPos.Equals(pos);
            p.HasBed = true; p.BedPos = pos;
            if (changed) Tell(p, "Respawn point set");
            if (!IsNight) { Tell(p, "You can only sleep at night"); return; }
            if (Vec3.Distance(p.Pos, new Vec3(pos.X + 0.5f, pos.Y, pos.Z + 0.5f)) > 3f) { Tell(p, "You are too far away to sleep"); return; }
            foreach (Entity e in EntitiesNear(new Vec3(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f), 8f))
            {
                MobEntity m = e as MobEntity;
                if (m != null && m.Hostile && !m.Dead) { Tell(p, "You cannot sleep with monsters nearby"); return; }
            }
            p.Sleeping = true;
            p.StatsDirty = true;
            Broadcast(new ChatMessagePacket { Text = p.Name + " is sleeping" });
        }

        public void WakePlayer(ServerPlayer p)
        {
            if (!p.Sleeping) return;
            p.Sleeping = false;
            p.StatsDirty = true;
        }

        /// <summary>When every living player is in bed the night is skipped; sleepers wake if they leave the bed or day breaks.</summary>
        private void TickSleep()
        {
            int living = 0, asleep = 0;
            for (int i = 0; i < Players.Count; i++)
            {
                ServerPlayer p = Players[i];
                if (p.Dead) continue;
                if (p.Sleeping)
                {
                    bool bedGone = World.GetBlock(p.BedPos.X, p.BedPos.Y, p.BedPos.Z) != B.Bed;
                    Vec3 bed = new Vec3(p.BedPos.X + 0.5f, p.BedPos.Y, p.BedPos.Z + 0.5f);
                    if (bedGone || Vec3.Distance(p.Pos, bed) > 3.5f || IsDaylight) { WakePlayer(p); }
                }
                living++;
                if (p.Sleeping) asleep++;
            }
            if (asleep > 0 && asleep == living) _sleepTicks++; else _sleepTicks = 0;
            if (_sleepTicks >= 60)
            {
                _sleepTicks = 0;
                DayTick = 1000;
                Broadcast(new TimeUpdatePacket { DayTick = DayTick });
                for (int i = 0; i < Players.Count; i++) WakePlayer(Players[i]);
            }
        }

        private int _sleepTicks;

        /// <summary>The spot beside the player's bed, if the bed is still there and has room to stand.</summary>
        private bool TryBedRespawn(ServerPlayer p, out Vec3 pos)
        {
            pos = default(Vec3);
            if (!p.HasBed) return false;
            Vec3i b = p.BedPos;
            if (!World.HasColumn(b.X >> 4, b.Z >> 4)) return false;
            if (World.GetBlock(b.X, b.Y, b.Z) != B.Bed)
            {
                p.HasBed = false;
                Tell(p, "Your bed was missing or obstructed");
                return false;
            }
            int[] dx = { 0, 1, -1, 0, 0 }, dz = { 0, 0, 0, 1, -1 };
            for (int k = 0; k < 5; k++)
            {
                int x = b.X + dx[k], z = b.Z + dz[k];
                bool ground = k == 0 || Blocks.IsSolid(World.GetBlock(x, b.Y, z));
                int y = k == 0 ? b.Y + 1 : b.Y + 1;
                if (!ground) continue;
                if (Blocks.IsSolid(World.GetBlock(x, y, z)) || Blocks.IsSolid(World.GetBlock(x, y + 1, z))) continue;
                pos = new Vec3(x + 0.5f, y + 0.05f, z + 0.5f);
                return true;
            }
            p.HasBed = false;
            Tell(p, "Your bed was missing or obstructed");
            return false;
        }

        // ------------------------------------------------------------------ small broadcast helpers

        public void BroadcastNear(Vec3 pos, float radius, Packet packet)
        {
            byte[] data = packet.Serialize();
            float r2 = radius * radius;
            for (int i = 0; i < Players.Count; i++)
                if (Vec3.DistanceSq(Players[i].Pos, pos) <= r2) Players[i].Connection.Send(data);
        }

        public void Effect(EffectKind kind, Vec3 pos, int data)
        {
            BroadcastNear(pos, 96f, new EffectPacket { Kind = kind, Position = pos, Data = data });
        }
    }
}
