using System;
using BlockRealm.Net;

namespace BlockRealm.Sim
{
    public struct PlayerInput
    {
        public float MoveX;          // strafe: -1 left .. +1 right
        public float MoveZ;          // forward: -1 back .. +1 forward
        public bool Jump;            // held
        public bool Sneak;           // held (fly down while flying, descend while riding)
        public bool Sprint;          // held
        public float Yaw;            // radians, 0 = +Z
        public float Pitch;          // radians, positive looks down
    }

    /// <summary>
    /// Movement of the local player on the block grid: walking, sprinting, sneaking (no stepping off edges),
    /// swimming, creative flight and dragon riding. Pure logic so it runs the same in the game and in tests.
    /// </summary>
    public sealed class PlayerController
    {
        public const float HalfWidth = 0.3f;
        public const float StandHeight = 1.8f;
        public const float SneakHeight = 1.5f;
        public const float EyeHeight = 1.62f;
        public const float SneakEyeHeight = 1.32f;

        public const float WalkSpeed = 4.3f, SprintSpeed = 5.6f, SneakSpeed = 1.4f, SwimSpeed = 2.6f;
        public const float FlySpeed = 11f, FlySprintSpeed = 22f, FlyVertical = 8f;
        public const float JumpSpeed = 9.0f;

        public Vec3 Pos;                 // feet
        public Vec3 Vel;
        public bool OnGround, InWater, HeadInWater, InLava;
        public bool Flying, Sprinting, Sneaking;
        public bool CanFly;              // creative mode
        public float LandedFall;         // how far the last landing fell, for effects

        // Riding
        public bool Riding;
        public int RideKind;
        public float RideSpeedNow;
        public float RideYaw, RidePitch;

        private float _peak;
        private Vec3 _impulse;

        public float EyeOffset { get { return Sneaking ? SneakEyeHeight : EyeHeight; } }
        public Vec3 EyePos { get { return new Vec3(Pos.X, Pos.Y + EyeOffset, Pos.Z); } }
        public float Height { get { return Sneaking ? SneakHeight : StandHeight; } }
        public Aabb Box { get { return Aabb.FromCenterBottom(Pos, HalfWidth, Height); } }

        public void Teleport(Vec3 p)
        {
            Pos = p; Vel = Vec3.Zero; _peak = p.Y; OnGround = false;
        }

        public void AddImpulse(Vec3 v) { _impulse = _impulse + v; }

        public PlayerFlags Flags
        {
            get
            {
                PlayerFlags f = PlayerFlags.None;
                if (OnGround) f |= PlayerFlags.OnGround;
                if (Sprinting) f |= PlayerFlags.Sprinting;
                if (Sneaking) f |= PlayerFlags.Sneaking;
                if (Flying) f |= PlayerFlags.Flying;
                if (InWater) f |= PlayerFlags.InWater;
                return f;
            }
        }

        public void Mount(int kind, float yaw)
        {
            Riding = true; RideKind = kind; RideSpeedNow = 0f; RideYaw = yaw; RidePitch = 0f;
            Flying = false; Vel = Vec3.Zero;
        }

        public void Dismount() { Riding = false; RideSpeedNow = 0f; }

        /// <summary>Advances the player by <paramref name="dt"/> seconds, in fixed sub-steps for stable collision.</summary>
        public void Step(World world, PlayerInput input, float dt)
        {
            while (dt > 1e-5f)
            {
                float h = Math.Min(dt, 1f / 60f);
                if (Riding) StepRiding(world, input, h);
                else StepWalking(world, input, h);
                dt -= h;
            }
        }

        private void StepWalking(World world, PlayerInput input, float dt)
        {
            bool wantSneak = input.Sneak && !Flying;
            if (wantSneak != Sneaking)
            {
                // Standing up needs headroom.
                if (!wantSneak)
                {
                    Aabb tall = Aabb.FromCenterBottom(Pos, HalfWidth, StandHeight);
                    if (!VoxelPhysics.BoxIntersectsSolid(world, tall)) Sneaking = false;
                }
                else Sneaking = true;
            }

            bool lava;
            Aabb body = Box;
            Aabb inner = new Aabb(new Vec3(body.Min.X, body.Min.Y + 0.2f, body.Min.Z), new Vec3(body.Max.X, body.Max.Y - 0.2f, body.Max.Z));
            bool liquid = VoxelPhysics.BoxInLiquid(world, inner, out lava);
            InLava = liquid && lava;
            InWater = liquid && !lava;
            HeadInWater = world.GetBlock(MathUtil.FloorInt(Pos.X), MathUtil.FloorInt(Pos.Y + EyeOffset), MathUtil.FloorInt(Pos.Z)) == B.Water;

            float sinY = (float)Math.Sin(input.Yaw), cosY = (float)Math.Cos(input.Yaw);
            // Forward is +Z at yaw 0; right is +X.
            float wx = input.MoveX * cosY + input.MoveZ * sinY;
            float wz = -input.MoveX * sinY + input.MoveZ * cosY;
            float mag = (float)Math.Sqrt(wx * wx + wz * wz);
            if (mag > 1f) { wx /= mag; wz /= mag; }

            Sprinting = input.Sprint && input.MoveZ > 0.1f && !Sneaking && !InWater;

            if (CanFly && Flying)
            {
                float speed = input.Sprint ? FlySprintSpeed : FlySpeed;
                Vel.X += (wx * speed - Vel.X) * Math.Min(1f, 10f * dt);
                Vel.Z += (wz * speed - Vel.Z) * Math.Min(1f, 10f * dt);
                float vy = (input.Jump ? FlyVertical : 0f) - (input.Sneak ? FlyVertical : 0f);
                Vel.Y += (vy - Vel.Y) * Math.Min(1f, 10f * dt);
            }
            else if (InWater || InLava)
            {
                float speed = SwimSpeed * (InLava ? 0.5f : 1f);
                Vel.X += (wx * speed - Vel.X) * Math.Min(1f, 6f * dt);
                Vel.Z += (wz * speed - Vel.Z) * Math.Min(1f, 6f * dt);
                Vel.Y -= 6f * dt;                      // sinks slowly
                if (input.Jump) Vel.Y += 26f * dt;
                if (Vel.Y > 4f) Vel.Y = 4f;
                if (Vel.Y < -3f) Vel.Y = -3f;
            }
            else
            {
                float speed = Sneaking ? SneakSpeed : (Sprinting ? SprintSpeed : WalkSpeed);
                float control = OnGround ? 14f : 3.2f;
                Vel.X += (wx * speed - Vel.X) * Math.Min(1f, control * dt);
                Vel.Z += (wz * speed - Vel.Z) * Math.Min(1f, control * dt);
                Vel.Y -= Phys.Gravity * dt;
                if (Vel.Y < -Phys.TerminalVelocity) Vel.Y = -Phys.TerminalVelocity;
                if (input.Jump && OnGround) { Vel.Y = JumpSpeed; OnGround = false; }
            }

            if (_impulse.LengthSq > 0f)
            {
                Vel = Vel + _impulse;
                _impulse = Vec3.Zero;
                OnGround = false;
            }

            MoveWithCollision(world, Vel * dt);

            if (Flying && OnGround && !input.Jump) Flying = false;      // landing ends flight
            if (OnGround || InWater || Flying) { if (OnGround && _peak - Pos.Y > 0.01f) LandedFall = _peak - Pos.Y; _peak = Pos.Y; }
            else if (Pos.Y > _peak) _peak = Pos.Y;
        }

        private void MoveWithCollision(World world, Vec3 delta)
        {
            Aabb box = Box;
            // Sneaking on the ground never walks off an edge: drop the component that would leave the ledge.
            if (Sneaking && OnGround && !Flying)
            {
                Aabb probe = box;
                if (delta.X != 0f && !HasGroundAfter(world, probe, new Vec3(delta.X, 0, 0)))
                {
                    delta.X = 0f; Vel.X = 0f;
                }
                if (delta.Z != 0f && !HasGroundAfter(world, probe.Offset(new Vec3(delta.X, 0, 0)), new Vec3(0, 0, delta.Z)))
                {
                    delta.Z = 0f; Vel.Z = 0f;
                }
            }

            bool hx, hy, hz;
            VoxelPhysics.Move(world, ref box, delta, out hx, out hy, out hz);
            Vec3 c = box.Center;
            Pos = new Vec3(c.X, box.Min.Y, c.Z);
            if (hx) Vel.X = 0f;
            if (hz) Vel.Z = 0f;
            if (hy)
            {
                if (delta.Y < 0f) OnGround = true;
                Vel.Y = 0f;
            }
            else if (delta.Y != 0f) OnGround = false;
            if (!hy && delta.Y >= 0f && Vel.Y <= 0f) OnGround = false;
            // Re-check ground contact so standing still keeps OnGround true.
            if (!OnGround && Vel.Y <= 0f && !Flying)
            {
                Aabb under = Box.Offset(new Vec3(0, -0.02f, 0));
                if (VoxelPhysics.BoxIntersectsSolid(world, under)) OnGround = true;
            }
        }

        private static bool HasGroundAfter(World world, Aabb box, Vec3 move)
        {
            Aabb moved = box.Offset(move).Offset(new Vec3(0, -0.6f, 0));
            return VoxelPhysics.BoxIntersectsSolid(world, moved);
        }

        // ------------------------------------------------------------------ dragon riding

        private void StepRiding(World world, PlayerInput input, float dt)
        {
            float top = DragonStats.RideSpeed[MathUtil.Clamp(RideKind, 0, 2)];
            // W accelerates, S brakes; the dragon always glides forward at a minimum speed.
            if (input.MoveZ > 0.1f) RideSpeedNow = Math.Min(top, RideSpeedNow + 14f * dt);
            else if (input.MoveZ < -0.1f) RideSpeedNow = Math.Max(0f, RideSpeedNow - 20f * dt);
            else RideSpeedNow += (Math.Min(top * 0.45f, 9f) - RideSpeedNow) * Math.Min(1f, 0.8f * dt);

            // A and D turn the dragon (and bank it); the mouse still aims the camera independently via input.Yaw.
            RideYaw -= input.MoveX * 1.5f * dt;

            float climb = (input.Jump ? 1f : 0f) - (input.Sneak ? 1f : 0f);
            float targetPitch = -climb * 0.55f;
            RidePitch += (targetPitch - RidePitch) * Math.Min(1f, 3f * dt);

            float cp = (float)Math.Cos(RidePitch);
            Vec3 dir = new Vec3((float)Math.Sin(RideYaw) * cp, -(float)Math.Sin(RidePitch), (float)Math.Cos(RideYaw) * cp);
            Vel = dir * RideSpeedNow;
            if (RideSpeedNow < 3f) Vel.Y -= 6f * (1f - RideSpeedNow / 3f);      // stalls sink

            // The rider's body sits on the dragon; move the dragon's box and place the rider on top of it.
            float halfWidth = DragonStats.HalfWidth[MathUtil.Clamp(RideKind, 0, 2)];
            float height = DragonStats.BoxHeight[MathUtil.Clamp(RideKind, 0, 2)];
            Vec3 dragonPos = Pos - DragonEntity.RiderOffset;
            Aabb box = Aabb.FromCenterBottom(dragonPos, halfWidth, height);
            bool hx, hy, hz;
            VoxelPhysics.Move(world, ref box, Vel * dt, out hx, out hy, out hz);
            if (hx || hz) RideSpeedNow *= 0.6f;
            if (hy) { Vel.Y = 0f; }
            Vec3 c = box.Center;
            Pos = new Vec3(c.X, box.Min.Y, c.Z) + DragonEntity.RiderOffset;
            OnGround = false; InWater = false; HeadInWater = false;
            _peak = Pos.Y;
        }
    }
}
