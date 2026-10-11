using System;

namespace BlockRealm
{
    /// <summary>
    /// Gradient noise using only + - * and floor, so terrain is bit-identical on every platform
    /// (no sin/cos/pow, whose last bits can differ between runtimes).
    /// </summary>
    public sealed class Noise
    {
        private readonly byte[] _perm = new byte[512];

        public Noise(long seed)
        {
            byte[] p = new byte[256];
            for (int i = 0; i < 256; i++) p[i] = (byte)i;
            Rng rng = new Rng(seed);
            for (int i = 255; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                byte t = p[i]; p[i] = p[j]; p[j] = t;
            }
            for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
        }

        private static float Fade(float t) { return t * t * t * (t * (t * 6f - 15f) + 10f); }
        private static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        private static float Grad2(int hash, float x, float y)
        {
            switch (hash & 7)
            {
                case 0: return x + y;
                case 1: return x - y;
                case 2: return -x + y;
                case 3: return -x - y;
                case 4: return x;
                case 5: return -x;
                case 6: return y;
                default: return -y;
            }
        }

        private static float Grad3(int hash, float x, float y, float z)
        {
            switch (hash & 15)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x + z;
                case 5: return -x + z;
                case 6: return x - z;
                case 7: return -x - z;
                case 8: return y + z;
                case 9: return -y + z;
                case 10: return y - z;
                case 11: return -y - z;
                case 12: return x + y;
                case 13: return -x + y;
                case 14: return -y + z;
                default: return -y - z;
            }
        }

        /// <summary>Perlin noise in roughly [-1, 1].</summary>
        public float Perlin2(float x, float y)
        {
            int xi = MathUtil.FloorInt(x), yi = MathUtil.FloorInt(y);
            float xf = x - xi, yf = y - yi;
            xi &= 255; yi &= 255;
            float u = Fade(xf), v = Fade(yf);
            int aa = _perm[_perm[xi] + yi], ab = _perm[_perm[xi] + yi + 1];
            int ba = _perm[_perm[xi + 1] + yi], bb = _perm[_perm[xi + 1] + yi + 1];
            float x1 = Lerp(Grad2(aa, xf, yf), Grad2(ba, xf - 1, yf), u);
            float x2 = Lerp(Grad2(ab, xf, yf - 1), Grad2(bb, xf - 1, yf - 1), u);
            return Lerp(x1, x2, v) * 1.4142f;
        }

        public float Perlin3(float x, float y, float z)
        {
            int xi = MathUtil.FloorInt(x), yi = MathUtil.FloorInt(y), zi = MathUtil.FloorInt(z);
            float xf = x - xi, yf = y - yi, zf = z - zi;
            xi &= 255; yi &= 255; zi &= 255;
            float u = Fade(xf), v = Fade(yf), w = Fade(zf);
            int a = _perm[xi] + yi, aa = _perm[a] + zi, ab = _perm[a + 1] + zi;
            int b = _perm[xi + 1] + yi, ba = _perm[b] + zi, bb = _perm[b + 1] + zi;
            float x1 = Lerp(Grad3(_perm[aa], xf, yf, zf), Grad3(_perm[ba], xf - 1, yf, zf), u);
            float x2 = Lerp(Grad3(_perm[ab], xf, yf - 1, zf), Grad3(_perm[bb], xf - 1, yf - 1, zf), u);
            float y1 = Lerp(x1, x2, v);
            x1 = Lerp(Grad3(_perm[aa + 1], xf, yf, zf - 1), Grad3(_perm[ba + 1], xf - 1, yf, zf - 1), u);
            x2 = Lerp(Grad3(_perm[ab + 1], xf, yf - 1, zf - 1), Grad3(_perm[bb + 1], xf - 1, yf - 1, zf - 1), u);
            float y2 = Lerp(x1, x2, v);
            return Lerp(y1, y2, w) * 1.1547f;
        }

        /// <summary>Fractal Brownian motion, normalised to roughly [-1, 1].</summary>
        public float Fbm2(float x, float y, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0, amp = 1, freq = 1, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += Perlin2(x * freq, y * freq) * amp;
                norm += amp;
                amp *= gain; freq *= lacunarity;
            }
            return sum / norm;
        }

        public float Fbm3(float x, float y, float z, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0, amp = 1, freq = 1, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += Perlin3(x * freq, y * freq, z * freq) * amp;
                norm += amp;
                amp *= gain; freq *= lacunarity;
            }
            return sum / norm;
        }
    }
}
