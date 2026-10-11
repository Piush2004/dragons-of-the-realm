using System;
using System.Collections.Generic;
using BlockRealm.Net;

namespace BlockRealm.Sim
{
    public static class Phys
    {
        public const float Gravity = 32f;            // blocks per second squared
        public const float TerminalVelocity = 78f;
        public const float JumpSpeed = 9.2f;
        public const float Dt = 1f / WorldConst.TicksPerSecond;
    }

    /// <summary>Anything that lives in the world and is replicated to clients.</summary>
    public abstract class Entity
    {
        public int Id;
        public EntityType Type;
        public byte Variant;
        public string Name = "";
        public Vec3 Pos, Vel;
        public float Yaw, Pitch;
        public float Health = 1f, MaxHealth = 1f;
        public bool Dead;
        public int DeadTicks;
        public float HalfWidth = 0.3f, Height = 1.8f;
        public bool OnGround, InWater;
        public int BurnTicks;
        public int HurtTicks;
        public int Age;
        public int AttackAnim;                        // ticks left of the attack swing, shown to clients
        public bool Removed;

        public Aabb Box { get { return Aabb.FromCenterBottom(Pos, HalfWidth, Height); } }
        public Vec3 Center { get { return new Vec3(Pos.X, Pos.Y + Height * 0.5f, Pos.Z); } }
        public Vec3 EyePos { get { return new Vec3(Pos.X, Pos.Y + Height * 0.9f, Pos.Z); } }
        public float HealthFraction { get { return MaxHealth <= 0 ? 0 : MathUtil.Saturate(Health / MaxHealth); } }

        public virtual EntityFlags Flags
        {
            get
            {
                EntityFlags f = EntityFlags.None;
                if (OnGround) f |= EntityFlags.OnGround;
                if (BurnTicks > 0) f |= EntityFlags.Burning;
                if (HurtTicks > 0) f |= EntityFlags.Hurt;
                if (AttackAnim > 0) f |= EntityFlags.Attacking;
                if (InWater) f |= EntityFlags.InWater;
                if (Dead) f |= EntityFlags.Dead;
                return f;
            }
        }

        public virtual bool Hittable { get { return !Dead && !Removed; } }

        public abstract void Tick(GameServer s);

        /// <summary>Right-click on this entity (feeding, saddling, mounting).</summary>
        public virtual void Interact(GameServer s, ServerPlayer p) { }

        /// <summary>Applies velocity with collision. Returns which axes were blocked.</summary>
        protected void MoveBody(GameServer s, bool applyGravity, out bool hitX, out bool hitZ)
        {
            Aabb box = Box;
            bool liquid, lava;
            liquid = VoxelPhysics.BoxInLiquid(s.World, box, out lava);
            InWater = liquid && !lava;
            if (applyGravity)
            {
                if (liquid)
                {
                    Vel.Y -= Phys.Gravity * 0.25f * Phys.Dt;
                    Vel.Y *= 0.85f; Vel.X *= 0.9f; Vel.Z *= 0.9f;
                }
                else
                {
                    Vel.Y -= Phys.Gravity * Phys.Dt;
                }
                if (Vel.Y < -Phys.TerminalVelocity) Vel.Y = -Phys.TerminalVelocity;
            }
            Vec3 d = Vel * Phys.Dt;
            bool hy;
            VoxelPhysics.Move(s.World, ref box, d, out hitX, out hy, out hitZ);
            Vec3 c = box.Center;
            Pos = new Vec3(c.X, box.Min.Y, c.Z);
            OnGround = hy && Vel.Y <= 0f;
            if (hy) Vel.Y = 0f;
            if (hitX) Vel.X = 0f;
            if (hitZ) Vel.Z = 0f;
        }
    }

    public sealed class ItemEntity : Entity
    {
        public ItemStack Stack;
        public int PickupDelay = 10;
        public const int Lifetime = 20 * 60 * 5;

        public ItemEntity(ItemStack stack)
        {
            Type = EntityType.Item; Stack = stack; HalfWidth = 0.15f; Height = 0.3f; Health = 1; MaxHealth = 1;
        }

        public override void Tick(GameServer s)
        {
            Age++;
            if (PickupDelay > 0) PickupDelay--;
            bool hx, hz;
            MoveBody(s, true, out hx, out hz);
            if (OnGround) { Vel.X *= 0.6f; Vel.Z *= 0.6f; }
            if (Age > Lifetime || Pos.Y < -10) { s.RemoveEntity(this); return; }
            Yaw += 0.1f;

            // Merge with a neighbouring stack of the same kind.
            if (Age % 10 == 0 && Stack.Count < Stack.MaxStack)
            {
                foreach (Entity other in s.EntitiesNear(Pos, 1.0f))
                {
                    ItemEntity o = other as ItemEntity;
                    if (o == null || o == this || o.Removed || !Stacks.Same(Stack, o.Stack)) continue;
                    if (o.Id < Id) continue;   // the older entity absorbs the newer one
                    Stacks.Merge(ref Stack, ref o.Stack);
                    if (o.Stack.IsEmpty) s.RemoveEntity(o);
                }
            }
            if (PickupDelay == 0)
            {
                ServerPlayer p = s.NearestPlayer(Pos, 1.6f, false);
                if (p != null && !p.Dead)
                {
                    int left = p.Inventory.Add(Stack);
                    if (left < Stack.Count)
                    {
                        Stack.Count = (byte)left;
                        p.InventoryDirty = true;
                        if (left == 0) s.RemoveEntity(this);
                    }
                }
            }
        }
    }

    public sealed class ArrowEntity : Entity
    {
        public int ShooterId;
        public float Damage = 3f;
        private int _stuckTicks;
        private bool _stuck;

        public ArrowEntity()
        {
            Type = EntityType.Arrow; HalfWidth = 0.1f; Height = 0.2f; Health = 1; MaxHealth = 1;
        }

        public override bool Hittable { get { return false; } }

        public override void Tick(GameServer s)
        {
            Age++;
            if (_stuck)
            {
                if (++_stuckTicks > 20 * 30) s.RemoveEntity(this);
                return;
            }
            Vel.Y -= Phys.Gravity * 0.4f * Phys.Dt;
            Vec3 from = Pos, to = Pos + Vel * Phys.Dt;
            float frac;
            // Entities first: the closest one on the segment.
            Entity hit = null; float best = 1f;
            Vec3 seg = to - from;
            foreach (Entity e in s.EntitiesNear(from + seg * 0.5f, seg.Length + 2f))
            {
                if (e == this || e.Id == ShooterId || !e.Hittable) continue;
                if (e.Type == EntityType.Item || e.Type == EntityType.Arrow || e.Type == EntityType.Fireball) continue;
                Aabb box = e.Box.Expand(0.15f);
                // Sample along the segment; cheap and good enough at arrow speeds.
                for (int i = 0; i <= 4; i++)
                {
                    float t = i / 4f;
                    Vec3 p = from + seg * t;
                    if (p.X >= box.Min.X && p.X <= box.Max.X && p.Y >= box.Min.Y && p.Y <= box.Max.Y && p.Z >= box.Min.Z && p.Z <= box.Max.Z)
                    {
                        if (t < best) { best = t; hit = e; }
                        break;
                    }
                }
            }
            if (hit != null)
            {
                Entity shooter = s.GetEntity(ShooterId);
                s.Hurt(hit, Damage, shooter, from, 5f);
                s.RemoveEntity(this);
                return;
            }
            if (VoxelPhysics.SegmentHitsSolid(s.World, from, to, out frac))
            {
                Pos = from + seg * frac;
                Vel = Vec3.Zero;
                _stuck = true;
                return;
            }
            Pos = to;
            // Point along the flight path for the renderer.
            Yaw = (float)Math.Atan2(Vel.X, Vel.Z);
            Pitch = (float)Math.Atan2(Vel.Y, Math.Sqrt(Vel.X * Vel.X + Vel.Z * Vel.Z));
            if (Age > 20 * 60 || Pos.Y < -20) s.RemoveEntity(this);
        }
    }

    /// <summary>Everything a hostile or passive creature needs; behaviour is chosen by <see cref="EntityType"/>.</summary>
    public sealed class MobEntity : Entity
    {
        public float Speed = 2f;
        public int Cooldown;
        public int WanderTicks, IdleTicks;
        public float WanderYaw;
        public int PanicTicks;
        public int FuseTicks;
        public int StuckTicks;
        private Vec3 _lastPos;
        public int TargetId;

        public static bool IsHostile(EntityType t) { return t == EntityType.Zombie || t == EntityType.Skeleton || t == EntityType.Creeper; }
        public bool Hostile { get { return IsHostile(Type); } }

        public MobEntity(EntityType type)
        {
            Type = type;
            switch (type)
            {
                case EntityType.Pig: MaxHealth = 10; HalfWidth = 0.4f; Height = 0.9f; Speed = 2.2f; break;
                case EntityType.Cow: MaxHealth = 10; HalfWidth = 0.45f; Height = 1.4f; Speed = 2.0f; break;
                case EntityType.Sheep: MaxHealth = 8; HalfWidth = 0.4f; Height = 1.3f; Speed = 2.1f; break;
                case EntityType.Chicken: MaxHealth = 4; HalfWidth = 0.2f; Height = 0.7f; Speed = 2.4f; break;
                case EntityType.Zombie: MaxHealth = 20; HalfWidth = 0.3f; Height = 1.8f; Speed = 3.3f; break;
                case EntityType.Skeleton: MaxHealth = 20; HalfWidth = 0.3f; Height = 1.9f; Speed = 3.2f; break;
                case EntityType.Creeper: MaxHealth = 20; HalfWidth = 0.3f; Height = 1.7f; Speed = 3.1f; break;
                default: MaxHealth = 10; break;
            }
            Health = MaxHealth;
        }

        public override EntityFlags Flags
        {
            get
            {
                EntityFlags f = base.Flags;
                if (FuseTicks > 0) f |= EntityFlags.Fuse;
                return f;
            }
        }

        public override void Tick(GameServer s)
        {
            Age++;
            if (HurtTicks > 0) HurtTicks--;
            if (AttackAnim > 0) AttackAnim--;
            if (Dead)
            {
                DeadTicks++;
                Vel.X *= 0.8f; Vel.Z *= 0.8f;
                bool dx, dz; MoveBody(s, true, out dx, out dz);
                if (DeadTicks > 20) s.RemoveEntity(this);
                return;
            }

            // Burning in daylight (undead) and from fire or lava.
            if (Hostile && Type != EntityType.Creeper && s.IsDaylight && !InWater && s.SkyLightAt(Pos) >= 15 && Age % 20 == 0)
                BurnTicks = Math.Max(BurnTicks, 60);
            if (BurnTicks > 0)
            {
                BurnTicks--;
                if (InWater) BurnTicks = 0;
                if (Age % 20 == 0) s.Hurt(this, 1f, null, Pos, 0f);
            }
            if (Pos.Y < -20) { s.Hurt(this, 1000f, null, Pos, 0f); return; }

            float desiredX = 0, desiredZ = 0, speed = Speed;
            bool sprint = false;
            if (Hostile) Think_Hostile(s, ref desiredX, ref desiredZ, ref speed, ref sprint);
            else Think_Passive(s, ref desiredX, ref desiredZ, ref speed);

            // Horizontal velocity eases toward the desired one; knockback is whatever is on top of it.
            float accel = OnGround ? 0.35f : 0.08f;
            Vel.X += (desiredX * speed - Vel.X) * accel;
            Vel.Z += (desiredZ * speed - Vel.Z) * accel;
            if (desiredX != 0 || desiredZ != 0) Yaw = MathUtil.LerpAngle(Yaw, (float)Math.Atan2(desiredX, desiredZ), 0.35f);

            bool hx, hz;
            MoveBody(s, true, out hx, out hz);
            if ((hx || hz) && OnGround && (desiredX != 0 || desiredZ != 0)) Vel.Y = Phys.JumpSpeed;
            if (InWater && (desiredX != 0 || desiredZ != 0) || (InWater && Age % 10 < 5)) Vel.Y = Math.Max(Vel.Y, 2.2f);

            // Give up and wiggle if wedged against terrain.
            if ((desiredX != 0 || desiredZ != 0) && Vec3.DistanceSq(Pos, _lastPos) < 0.0004f) StuckTicks++; else StuckTicks = 0;
            if (Age % 5 == 0) _lastPos = Pos;
            if (StuckTicks > 30) { WanderYaw += 1.7f; WanderTicks = 20; IdleTicks = 0; StuckTicks = 0; if (OnGround) Vel.Y = Phys.JumpSpeed; }
        }

        private void Think_Passive(GameServer s, ref float dx, ref float dz, ref float speed)
        {
            if (PanicTicks > 0)
            {
                PanicTicks--;
                dx = (float)Math.Sin(WanderYaw); dz = (float)Math.Cos(WanderYaw);
                speed = Speed * 1.6f;
                return;
            }
            if (WanderTicks > 0)
            {
                WanderTicks--;
                dx = (float)Math.Sin(WanderYaw); dz = (float)Math.Cos(WanderYaw);
            }
            else if (IdleTicks > 0) IdleTicks--;
            else
            {
                if (s.Random.Chance(0.5f)) { WanderYaw = s.Random.Range(0f, MathUtil.Pi * 2f); WanderTicks = s.Random.Range(20, 70); }
                IdleTicks = s.Random.Range(30, 120);
            }
            // Chickens flutter down slowly.
            if (Type == EntityType.Chicken && Vel.Y < -2f) Vel.Y = -2f;
        }

        private void Think_Hostile(GameServer s, ref float dx, ref float dz, ref float speed, ref bool sprint)
        {
            ServerPlayer target = s.NearestPlayer(Pos, 22f, true);
            if (target == null) { Think_Passive(s, ref dx, ref dz, ref speed); FuseTicks = 0; return; }
            TargetId = target.Id;
            Vec3 to = target.Pos - Pos;
            float dist = to.HorizontalLength;
            Vec3 dir = dist > 0.01f ? new Vec3(to.X / dist, 0, to.Z / dist) : Vec3.Zero;
            if (Cooldown > 0) Cooldown--;

            switch (Type)
            {
                case EntityType.Zombie:
                    dx = dir.X; dz = dir.Z;
                    if (dist < 1.5f && Math.Abs(to.Y) < 2f && Cooldown == 0)
                    {
                        s.Hurt(target, 3f, this, Pos, 5f);
                        AttackAnim = 6; Cooldown = 20;
                    }
                    break;
                case EntityType.Skeleton:
                    if (dist > 13f) { dx = dir.X; dz = dir.Z; }
                    else if (dist < 6f) { dx = -dir.X; dz = -dir.Z; }
                    else
                    {
                        // Strafe sideways while shooting.
                        float side = ((Id & 1) == 0 ? 1f : -1f) * 0.6f;
                        dx = -dir.Z * side; dz = dir.X * side;
                    }
                    if (dist < 18f && Cooldown == 0 && s.HasLineOfSight(EyePos, target.EyePos))
                    {
                        Vec3 aim = target.EyePos - EyePos;
                        float flat = aim.HorizontalLength;
                        aim.Y += flat * 0.08f;                 // lob a little to compensate for drop
                        s.FireArrow(this, EyePos, aim.Normalized * 28f, 3f);
                        AttackAnim = 8; Cooldown = 40;
                    }
                    break;
                case EntityType.Creeper:
                    if (dist < 3.0f && Math.Abs(to.Y) < 3f)
                    {
                        FuseTicks++;
                        if (FuseTicks >= 30)
                        {
                            s.Explode(Center, 3.2f, this, 20f, true, false);
                            Dead = true; DeadTicks = 100;
                            s.RemoveEntity(this);
                            return;
                        }
                    }
                    else
                    {
                        if (FuseTicks > 0) FuseTicks--;
                        dx = dir.X; dz = dir.Z;
                    }
                    break;
            }
            if (dist < 0.8f && Type != EntityType.Creeper) { dx = 0; dz = 0; }
        }
    }
}
