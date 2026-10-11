using System;
using System.Runtime.CompilerServices;

namespace BlockRealm
{
    /// <summary>World-wide constants shared by the server, the client and the tests.</summary>
    public static class WorldConst
    {
        public const int SectionSize = 16;
        public const int Height = 384;                       // y = 0 .. 383
        public const int SectionsPerColumn = Height / SectionSize;
        public const int SeaLevel = 112;                     // top of the water surface is y = SeaLevel
        public const int ColumnArea = SectionSize * SectionSize;
        public const int TicksPerSecond = 20;
        public const int DayLengthTicks = 24000;             // 20 real minutes
    }

    /// <summary>Block faces. The order is used by meshing, textures and light lookups.</summary>
    public enum Face : byte { East = 0, West = 1, Up = 2, Down = 3, South = 4, North = 5 }

    public struct Vec3
    {
        public float X, Y, Z;
        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static readonly Vec3 Zero = new Vec3(0, 0, 0);
        public static readonly Vec3 Up = new Vec3(0, 1, 0);

        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Vec3 operator *(Vec3 a, float s) { return new Vec3(a.X * s, a.Y * s, a.Z * s); }
        public static Vec3 operator *(float s, Vec3 a) { return new Vec3(a.X * s, a.Y * s, a.Z * s); }
        public static Vec3 operator /(Vec3 a, float s) { return new Vec3(a.X / s, a.Y / s, a.Z / s); }
        public static Vec3 operator -(Vec3 a) { return new Vec3(-a.X, -a.Y, -a.Z); }

        public float Length { get { return (float)Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public float LengthSq { get { return X * X + Y * Y + Z * Z; } }
        public float HorizontalLength { get { return (float)Math.Sqrt(X * X + Z * Z); } }
        public Vec3 Normalized
        {
            get { float l = Length; return l < 1e-6f ? Zero : new Vec3(X / l, Y / l, Z / l); }
        }
        public static float Dot(Vec3 a, Vec3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        public static float Distance(Vec3 a, Vec3 b) { return (a - b).Length; }
        public static float DistanceSq(Vec3 a, Vec3 b) { return (a - b).LengthSq; }
        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) { return a + (b - a) * t; }
        public override string ToString() { return "(" + X.ToString("0.00") + ", " + Y.ToString("0.00") + ", " + Z.ToString("0.00") + ")"; }
    }

    public struct Vec3i : IEquatable<Vec3i>
    {
        public int X, Y, Z;
        public Vec3i(int x, int y, int z) { X = x; Y = y; Z = z; }
        public static Vec3i operator +(Vec3i a, Vec3i b) { return new Vec3i(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public bool Equals(Vec3i o) { return X == o.X && Y == o.Y && Z == o.Z; }
        public override bool Equals(object o) { return o is Vec3i && Equals((Vec3i)o); }
        public override int GetHashCode() { return (X * 73856093) ^ (Y * 19349663) ^ (Z * 83492791); }
        public override string ToString() { return "[" + X + "," + Y + "," + Z + "]"; }

        public static readonly Vec3i[] FaceOffsets =
        {
            new Vec3i(1, 0, 0), new Vec3i(-1, 0, 0), new Vec3i(0, 1, 0),
            new Vec3i(0, -1, 0), new Vec3i(0, 0, 1), new Vec3i(0, 0, -1),
        };
    }

    /// <summary>Column coordinate (a 16x16 footprint).</summary>
    public struct ColumnPos : IEquatable<ColumnPos>
    {
        public int X, Z;
        public ColumnPos(int x, int z) { X = x; Z = z; }
        public bool Equals(ColumnPos o) { return X == o.X && Z == o.Z; }
        public override bool Equals(object o) { return o is ColumnPos && Equals((ColumnPos)o); }
        public override int GetHashCode() { return X * 73856093 ^ Z * 19349663; }
        public override string ToString() { return "col(" + X + "," + Z + ")"; }
        public static ColumnPos FromBlock(int bx, int bz) { return new ColumnPos(MathUtil.FloorDiv(bx, 16), MathUtil.FloorDiv(bz, 16)); }
    }

    public struct Aabb
    {
        public Vec3 Min, Max;
        public Aabb(Vec3 min, Vec3 max) { Min = min; Max = max; }
        public static Aabb FromCenterBottom(Vec3 feet, float halfWidth, float height)
        {
            return new Aabb(new Vec3(feet.X - halfWidth, feet.Y, feet.Z - halfWidth), new Vec3(feet.X + halfWidth, feet.Y + height, feet.Z + halfWidth));
        }
        public bool Intersects(Aabb o)
        {
            return Min.X < o.Max.X && Max.X > o.Min.X && Min.Y < o.Max.Y && Max.Y > o.Min.Y && Min.Z < o.Max.Z && Max.Z > o.Min.Z;
        }
        public Aabb Offset(Vec3 d) { return new Aabb(Min + d, Max + d); }
        public Aabb Expand(float amount)
        {
            Vec3 e = new Vec3(amount, amount, amount);
            return new Aabb(Min - e, Max + e);
        }
        public Vec3 Center { get { return (Min + Max) * 0.5f; } }
    }

    public static class MathUtil
    {
        public const float Pi = 3.14159265f;
        public const float Deg2Rad = Pi / 180f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorInt(float v) { int i = (int)v; return v < i ? i - 1 : i; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorDiv(int a, int b) { int q = a / b; return (a % b != 0 && ((a < 0) != (b < 0))) ? q - 1 : q; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Mod(int a, int b) { int r = a % b; return r < 0 ? r + b : r; }
        public static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        public static float Saturate(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
        public static float SmoothStep(float e0, float e1, float x)
        {
            float t = Saturate((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }
        public static float MoveToward(float cur, float target, float maxDelta)
        {
            if (Math.Abs(target - cur) <= maxDelta) return target;
            return cur + Math.Sign(target - cur) * maxDelta;
        }
        public static float WrapAngle(float a)
        {
            while (a > Pi) a -= 2 * Pi;
            while (a < -Pi) a += 2 * Pi;
            return a;
        }
        public static float LerpAngle(float a, float b, float t) { return a + WrapAngle(b - a) * t; }
    }

    /// <summary>SplitMix64 based generator: small, fast and identical on every runtime.</summary>
    public sealed class Rng
    {
        private ulong _state;
        public Rng(long seed) { _state = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x1234567UL; }

        public ulong NextU64()
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        public int NextInt(int maxExclusive) { return (int)(NextU64() % (ulong)maxExclusive); }
        public int Range(int minInclusive, int maxInclusive) { return minInclusive + NextInt(maxInclusive - minInclusive + 1); }
        public float NextFloat() { return (NextU64() >> 40) / (float)(1 << 24); }
        public float Range(float min, float max) { return min + (max - min) * NextFloat(); }
        public bool Chance(float p) { return NextFloat() < p; }

        public static ulong Hash(long a, long b, long c)
        {
            ulong h = (ulong)a * 0x9E3779B97F4A7C15UL;
            h ^= (ulong)b + 0x7F4A7C15UL + (h << 6) + (h >> 2);
            h *= 0xBF58476D1CE4E5B9UL;
            h ^= (ulong)c + 0x94D049BBUL + (h << 6) + (h >> 2);
            h ^= h >> 31; h *= 0x94D049BB133111EBUL; h ^= h >> 29;
            return h;
        }
        public static float Hash01(long a, long b, long c) { return (Hash(a, b, c) >> 40) / (float)(1 << 24); }
    }
}
