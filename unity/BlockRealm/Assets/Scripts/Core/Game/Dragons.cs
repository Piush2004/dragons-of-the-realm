using System;
using System.Collections.Generic;
using BlockRealm.Net;

namespace BlockRealm.Sim
{
    public static class DragonStats
    {
        public static readonly string[] Names = { "Cinderwing", "Brazenhide", "Elderscale" };
        public static readonly float[] Health = { 160f, 240f, 400f };
        public static readonly float[] Bite = { 7f, 9f, 13f };
        public static readonly float[] Fire = { 5f, 6f, 8f };
        public static readonly float[] Cruise = { 17f, 14f, 11f };         // blocks per second
        public static readonly float[] HalfWidth = { 1.2f, 1.5f, 1.9f };
        public static readonly float[] BoxHeight = { 2.2f, 2.8f, 3.4f };
        public static readonly float[] RideSpeed = { 30f, 24f, 20f };
    }

    public enum DragonState : byte { Perched, Circling, Attacking, Retreating, Following, Ridden, Fleeing }

    /// <summary>A fireball breathed by a dragon: flies straight, bursts on contact and sets the area alight.</summary>
    public sealed class FireballEntity : Entity
    {
        public int OwnerId;
        public float Damage = 6f;

        public FireballEntity()
        {
            Type = EntityType.Fireball; HalfWidth = 0.3f; Height = 0.6f; Health = 1; MaxHealth = 1;
        }

        public override bool Hittable { get { return false; } }

        public override void Tick(GameServer s)
        {
            Age++;
            Vec3 from = Pos, to = Pos + Vel * Phys.Dt;
            Entity owner = s.GetEntity(OwnerId);
            foreach (Entity e in s.EntitiesNear(to, 2.2f))
            {
                if (e.Id == OwnerId || e is FireballEntity || e is ItemEntity || e is ArrowEntity || e.Dead) continue;
                if (e is DragonEntity && owner is DragonEntity) continue;
                DragonEntity ownerDragon = owner as DragonEntity;
                // A tamed dragon's fire never hurts its owner or its owner's other dragons.
                if (ownerDragon != null && ownerDragon.Tamed && e.Id == ownerDragon.OwnerId) continue;
                if (e.Box.Expand(0.3f).Intersects(Box)) { Burst(s, owner); return; }
            }
            float frac;
            if (VoxelPhysics.SegmentHitsSolid(s.World, from, to, out frac))
            {
                Pos = from + (to - from) * frac;
                Burst(s, owner);
                return;
            }
            Pos = to;
            if (Age > 20 * 5) s.RemoveEntity(this);
        }

        private void Burst(GameServer s, Entity owner)
        {
            s.Explode(Pos, 2.4f, owner, Damage, true, true);
            s.RemoveEntity(this);
        }
    }

    /// <summary>A dragon: a lair guardian when wild, a flying companion and mount once tamed.</summary>
    public sealed class DragonEntity : Entity
    {
        public readonly int Kind;
        public Lair HomeLair;
        public bool HasLair;
        public bool Summoned;
        public bool Tamed, Saddled;
        public int OwnerId, RiderId;
        public DragonState State = DragonState.Perched;

        private int _breathTicks, _fireCooldown, _biteCooldown, _stateTicks, _feedCount;
        private float _orbitAngle;
        private int _targetId;
        private Vec3 _retreatDir;

        public static readonly Vec3 RiderOffset = new Vec3(0f, 2.3f, 0f);

        public DragonEntity(int kind)
        {
            Kind = MathUtil.Clamp(kind, 0, 2);
            Type = EntityType.Dragon;
            Variant = (byte)Kind;
            Name = DragonStats.Names[Kind];
            MaxHealth = DragonStats.Health[Kind]; Health = MaxHealth;
            HalfWidth = DragonStats.HalfWidth[Kind]; Height = DragonStats.BoxHeight[Kind];
        }

        public override EntityFlags Flags
        {
            get
            {
                EntityFlags f = base.Flags;
                if (State != DragonState.Perched) f |= EntityFlags.Flying;
                if (_breathTicks > 0) f |= EntityFlags.BreathingFire;
                if (Tamed) f |= EntityFlags.Tamed;
                if (RiderId != 0) f |= EntityFlags.Ridden;
                return f;
            }
        }

        public override bool Hittable { get { return !Dead && !Removed; } }

        // ------------------------------------------------------------------ hooks from the server

        public void OnHurt(GameServer s, Entity attacker, float amount)
        {
            if (Tamed || attacker == null) return;
            ServerPlayer p = attacker as ServerPlayer;
            if (p != null)
            {
                _targetId = p.Id;
                if (State == DragonState.Perched || State == DragonState.Circling) SetState(DragonState.Attacking);
            }
        }

        public void OnKilled(GameServer s, Entity attacker)
        {
            if (RiderId != 0)
            {
                ServerPlayer rider = s.GetEntity(RiderId) as ServerPlayer;
                if (rider != null)
                {
                    rider.RidingEntityId = 0; rider.FallPeakY = rider.Pos.Y;
                    rider.Send(new MountPacket { EntityId = Id, Kind = (byte)Kind, Mounted = false });
                }
                RiderId = 0;
            }
            if (!Tamed)
            {
                s.DropItem(Center, new ItemStack(ItemIds.DragonScale, s.Random.Range(2, 5)), new Vec3(0, 4f, 0));
                s.DropItem(Center, new ItemStack(ItemIds.Diamond, s.Random.Range(1, 3)), new Vec3(1.5f, 4f, 0));
                s.Broadcast(new ChatMessagePacket { Text = Name + " has been slain!" });
            }
            s.LairDragonDied(this);
        }

        public override void Interact(GameServer s, ServerPlayer p)
        {
            ItemStack held = p.Held;
            bool meat = held.Id == ItemIds.RawPork || held.Id == ItemIds.RawBeef || held.Id == ItemIds.RawChicken ||
                        held.Id == ItemIds.RawMutton || held.Id == ItemIds.Steak || held.Id == ItemIds.CookedPork;
            if (!Tamed)
            {
                if (!meat) { s.Send(p, "The " + Name + " is wild. Wear it down, then offer it meat."); return; }
                if (HealthFraction > 0.35f) { s.Send(p, "It is too strong to trust you yet."); return; }
                if (!p.GodMode) { p.Inventory.ConsumeHeld(); p.InventoryDirty = true; }
                _feedCount++;
                if (_feedCount >= 2 || s.Random.Chance(0.4f))
                {
                    Tamed = true; OwnerId = p.Id; HasLair = false; _targetId = 0;
                    Health = Math.Max(Health, MaxHealth * 0.5f);
                    SetState(DragonState.Following);
                    s.BroadcastNear(Pos, 96f, new EntityEventPacket { EntityId = Id, Event = EntityEventKind.Tamed });
                    s.Send(p, Name + " is yours.");
                }
                else s.Send(p, "The " + Name + " eyes you warily.");
                return;
            }
            if (OwnerId != p.Id) { s.Send(p, "This dragon belongs to someone else."); return; }
            if (held.Id == ItemIds.Saddle && !Saddled)
            {
                Saddled = true;
                if (!p.GodMode) { p.Inventory.ConsumeHeld(); p.InventoryDirty = true; }
                s.Send(p, "Saddled.");
                return;
            }
            if (meat && Health < MaxHealth)
            {
                Health = Math.Min(MaxHealth, Health + 25f);
                if (!p.GodMode) { p.Inventory.ConsumeHeld(); p.InventoryDirty = true; }
                return;
            }
            if (Saddled && RiderId == 0 && p.RidingEntityId == 0)
            {
                RiderId = p.Id; p.RidingEntityId = Id;
                SetState(DragonState.Ridden);
                p.FallPeakY = p.Pos.Y;
                s.TeleportPlayer(p, Pos + RiderOffset);
                p.Send(new MountPacket { EntityId = Id, Kind = (byte)Kind, Mounted = true });
                s.BroadcastNear(Pos, 96f, new EntityEventPacket { EntityId = Id, Event = EntityEventKind.Mounted });
            }
            else if (!Saddled) s.Send(p, "It needs a saddle to be ridden.");
        }

        public void FollowRider(ServerPlayer rider)
        {
            Pos = rider.Pos - RiderOffset;
            Yaw = rider.Yaw;
            Pitch = rider.Pitch * 0.5f;
            Vel = Vec3.Zero;
        }

        public void Dismount(GameServer s, ServerPlayer p)
        {
            if (RiderId != p.Id) return;
            RiderId = 0;
            p.FallPeakY = p.Pos.Y;
            SetState(Tamed ? DragonState.Following : DragonState.Circling);
            p.Send(new MountPacket { EntityId = Id, Kind = (byte)Kind, Mounted = false });
            s.BroadcastNear(Pos, 96f, new EntityEventPacket { EntityId = Id, Event = EntityEventKind.Dismounted });
        }

        private void SetState(DragonState st) { State = st; _stateTicks = 0; }

        // ------------------------------------------------------------------ behaviour

        public override void Tick(GameServer s)
        {
            Age++;
            _stateTicks++;
            if (HurtTicks > 0) HurtTicks--;
            if (AttackAnim > 0) AttackAnim--;
            if (_fireCooldown > 0) _fireCooldown--;
            if (_biteCooldown > 0) _biteCooldown--;
            if (_breathTicks > 0) _breathTicks--;

            if (Dead)
            {
                DeadTicks++;
                Vel.Y -= Phys.Gravity * 0.6f * Phys.Dt;
                bool dx, dz;
                MoveBody(s, false, out dx, out dz);
                if (DeadTicks > 20 * 6) s.RemoveEntity(this);
                return;
            }

            // Slow regeneration when nothing is going on.
            if (Age % 20 == 0 && Health < MaxHealth) Health = Math.Min(MaxHealth, Health + (Tamed ? 1f : 2f));

            if (RiderId != 0)
            {
                ServerPlayer r = s.GetEntity(RiderId) as ServerPlayer;
                if (r == null || r.Dead || r.RidingEntityId != Id) { RiderId = 0; SetState(DragonState.Following); }
                else { State = DragonState.Ridden; return; }   // position comes from the rider's packets
            }

            if (Tamed) TickTamed(s);
            else TickWild(s);
        }

        private void TickWild(GameServer s)
        {
            ServerPlayer target = s.GetEntity(_targetId) as ServerPlayer;
            if (target != null && (target.Dead || target.GodMode || Vec3.Distance(target.Pos, Pos) > 140f)) { target = null; _targetId = 0; }
            if (target == null)
            {
                target = s.NearestPlayer(Pos, State == DragonState.Perched ? 55f : 100f, true);
                if (target != null) _targetId = target.Id;
            }

            Vec3 home = HasLair ? new Vec3(HomeLair.X + 0.5f, HomeLair.Y + 1f, HomeLair.Z + 0.5f) : Pos;
            switch (State)
            {
                case DragonState.Perched:
                    Vel = Vec3.Zero;
                    if (HasLair) { Pos = new Vec3(home.X, home.Y, home.Z); }
                    if (target != null) SetState(DragonState.Circling);
                    break;

                case DragonState.Circling:
                {
                    if (target == null && _stateTicks > 20 * 20 && HasLair) { SetState(DragonState.Fleeing); break; }
                    _orbitAngle += DragonStats.Cruise[Kind] / 30f * Phys.Dt;
                    Vec3 center = HasLair ? home : Pos;
                    Vec3 goal = new Vec3(center.X + (float)Math.Cos(_orbitAngle) * 30f, center.Y + 30f, center.Z + (float)Math.Sin(_orbitAngle) * 30f);
                    Steer(s, goal, DragonStats.Cruise[Kind], 0.06f);
                    if (target != null && Vec3.Distance(target.Pos, Pos) < 70f && _stateTicks > 40) SetState(DragonState.Attacking);
                    break;
                }

                case DragonState.Attacking:
                {
                    if (target == null) { SetState(DragonState.Circling); break; }
                    if (HealthFraction < 0.15f && HasLair) { SetState(DragonState.Fleeing); break; }
                    Vec3 to = target.Pos + new Vec3(0, 2f, 0) - Pos;
                    float dist = to.Length;
                    // Close in a little above the target, then drop to bite range.
                    Vec3 goal = target.Pos + new Vec3(0, dist > 14f ? 7f : 1.5f, 0);
                    Steer(s, goal, DragonStats.Cruise[Kind] * 1.15f, 0.09f);

                    if (dist < 34f && dist > 6f && _fireCooldown == 0 && _breathTicks == 0)
                    {
                        _breathTicks = 36;
                        _fireCooldown = 130;
                        s.BroadcastNear(Pos, 128f, new EntityEventPacket { EntityId = Id, Event = EntityEventKind.Roar });
                    }
                    if (_breathTicks > 0 && _breathTicks % 4 == 0) SpitFireball(s, target);
                    if (dist < DragonStats.HalfWidth[Kind] + 4.5f && _biteCooldown == 0)
                    {
                        s.Hurt(target, DragonStats.Bite[Kind], this, Pos, 9f);
                        AttackAnim = 8; _biteCooldown = 30;
                        if (s.Random.Chance(0.4f)) { _retreatDir = new Vec3(s.Random.Range(-1f, 1f), 0.6f, s.Random.Range(-1f, 1f)).Normalized; SetState(DragonState.Retreating); }
                    }
                    if (_stateTicks > 20 * 14 && s.Random.Chance(0.02f)) { _retreatDir = new Vec3(s.Random.Range(-1f, 1f), 0.5f, s.Random.Range(-1f, 1f)).Normalized; SetState(DragonState.Retreating); }
                    break;
                }

                case DragonState.Retreating:
                {
                    Steer(s, Pos + _retreatDir * 40f + new Vec3(0, 6f, 0), DragonStats.Cruise[Kind] * 1.2f, 0.06f);
                    if (_stateTicks > 60) SetState(target != null ? DragonState.Attacking : DragonState.Circling);
                    break;
                }

                case DragonState.Fleeing:
                {
                    // Return to the nest and settle, healing.
                    Steer(s, home + new Vec3(0, 3f, 0), DragonStats.Cruise[Kind], 0.07f);
                    if (Vec3.Distance(Pos, home) < 3f) SetState(DragonState.Perched);
                    break;
                }

                default:
                    SetState(DragonState.Circling);
                    break;
            }
            Fly(s);
        }

        private void TickTamed(GameServer s)
        {
            ServerPlayer owner = s.GetEntity(OwnerId) as ServerPlayer;
            if (owner == null) { s.RemoveEntity(this); return; }
            // Engage hostile creatures near the owner.
            MobEntity foe = null; float best = 24f * 24f;
            foreach (Entity e in s.EntitiesNear(owner.Pos, 24f))
            {
                MobEntity m = e as MobEntity;
                if (m == null || !m.Hostile || m.Dead) continue;
                float d = Vec3.DistanceSq(m.Pos, Pos);
                if (d < best) { best = d; foe = m; }
            }
            if (foe != null)
            {
                float dist = (float)Math.Sqrt(best);
                Steer(s, foe.Pos + new Vec3(0, 3f, 0), DragonStats.Cruise[Kind], 0.08f);
                if (dist < 22f && _fireCooldown == 0 && _breathTicks == 0) { _breathTicks = 24; _fireCooldown = 100; }
                if (_breathTicks > 0 && _breathTicks % 4 == 0) SpitFireball(s, foe);
                if (dist < 5f && _biteCooldown == 0) { s.Hurt(foe, DragonStats.Bite[Kind], this, Pos, 7f); AttackAnim = 8; _biteCooldown = 30; }
            }
            else
            {
                Vec3 toOwner = owner.Pos - Pos;
                float d = toOwner.HorizontalLength;
                if (d > 90f) { Pos = owner.Pos + new Vec3(3f, 4f, 3f); Vel = Vec3.Zero; }
                else if (d > 10f)
                {
                    Steer(s, owner.Pos + new Vec3(0, 6f, 0), Math.Min(DragonStats.Cruise[Kind] * 1.4f, 8f + d), 0.08f);
                }
                else
                {
                    // Hover and drift near the owner.
                    float a = Age * 0.02f;
                    Steer(s, owner.Pos + new Vec3((float)Math.Cos(a) * 6f, 5f + (float)Math.Sin(a * 1.7f) * 1.5f, (float)Math.Sin(a) * 6f), 8f, 0.06f);
                }
            }
            Fly(s);
        }

        private void SpitFireball(GameServer s, Entity target)
        {
            Vec3 mouth = Pos + new Vec3((float)Math.Sin(Yaw) * (HalfWidth + 1.2f), Height * 0.6f, (float)Math.Cos(Yaw) * (HalfWidth + 1.2f));
            Vec3 aim = (target.Center - mouth);
            float dist = aim.Length;
            // Lead the target a little and add spread so a volley fans out.
            aim = aim + target.Vel * (dist / 24f * 0.6f);
            Vec3 dir = aim.Normalized;
            dir = (dir + new Vec3(s.Random.Range(-0.08f, 0.08f), s.Random.Range(-0.05f, 0.05f), s.Random.Range(-0.08f, 0.08f))).Normalized;
            FireballEntity fb = new FireballEntity();
            fb.Pos = mouth; fb.Vel = dir * 26f; fb.OwnerId = Id; fb.Damage = DragonStats.Fire[Kind];
            s.AddEntity(fb);
        }

        /// <summary>Accelerate toward a point, turning at a limited rate.</summary>
        private void Steer(GameServer s, Vec3 goal, float speed, float agility)
        {
            Vec3 to = goal - Pos;
            float dist = to.Length;
            if (dist < 0.5f) { Vel = Vel * 0.9f; return; }
            Vec3 desired = to / dist * Math.Min(speed, dist * 3f + 2f);
            Vel = Vel + (desired - Vel) * agility;
            // Avoid hitting terrain: climb when something solid is just ahead.
            Vec3 ahead = Pos + Vel.Normalized * (HalfWidth + 6f);
            if (Vel.LengthSq > 1f)
            {
                RayHit h = VoxelPhysics.Raycast(s.World, Center, Vel.Normalized, HalfWidth + 9f);
                if (h.Hit && Blocks.IsSolid(s.World.GetBlock(h.X, h.Y, h.Z)))
                {
                    float urgency = 1f - h.Distance / (HalfWidth + 9f);
                    Vel.Y += urgency * 22f * Phys.Dt * 4f;
                }
            }
        }

        private void Fly(GameServer s)
        {
            Aabb box = Box;
            bool hx, hy, hz;
            VoxelPhysics.Move(s.World, ref box, Vel * Phys.Dt, out hx, out hy, out hz);
            Vec3 c = box.Center;
            Pos = new Vec3(c.X, box.Min.Y, c.Z);
            if (hx) Vel.X = 0f;
            if (hz) Vel.Z = 0f;
            if (hy) Vel.Y = Vel.Y > 0 ? 0f : Math.Max(Vel.Y, 0f);
            if (hx || hz) Vel.Y += 14f * Phys.Dt;          // scramble upward over cliffs
            float flat = Vel.HorizontalLength;
            if (flat > 0.5f) Yaw = MathUtil.LerpAngle(Yaw, (float)Math.Atan2(Vel.X, Vel.Z), 0.15f);
            Pitch = MathUtil.Clamp(-(float)Math.Atan2(Vel.Y, Math.Max(flat, 1f)), -0.8f, 0.8f);
            OnGround = false;
        }
    }
}
