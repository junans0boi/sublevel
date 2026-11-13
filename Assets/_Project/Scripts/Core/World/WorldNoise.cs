using System;

namespace Game.Core
{
    // Hash-based value noise: integer hashing only, so the same seed gives the same world on every machine.
    internal static class WorldNoise
    {
        static uint Hash(int x, int z, uint seed)
        {
            unchecked
            {
                uint h = seed ^ 0x9E3779B9u;
                h ^= (uint)x * 0x85EBCA6Bu;
                h = (h << 13) | (h >> 19);
                h *= 0xC2B2AE35u;
                h ^= (uint)z * 0x27D4EB2Fu;
                h = (h << 15) | (h >> 17);
                h *= 0x165667B1u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return h;
            }
        }

        // Uniform in [0, 1).
        public static float Hash01(int x, int z, uint seed) => (Hash(x, z, seed) & 0xFFFFFFu) / 16777216f;

        // Smooth value noise in [-1, 1], one feature per unit.
        public static float Value(float x, float z, uint seed)
        {
            int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
            float fx = x - ix, fz = z - iz;
            float ux = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
            float uz = fz * fz * fz * (fz * (fz * 6f - 15f) + 10f);
            float a = Hash01(ix, iz, seed), b = Hash01(ix + 1, iz, seed);
            float c = Hash01(ix, iz + 1, seed), d = Hash01(ix + 1, iz + 1, seed);
            float low = a + (b - a) * ux, high = c + (d - c) * ux;
            return (low + (high - low) * uz) * 2f - 1f;
        }

        // Fractal sum in [-1, 1].
        public static float Fbm(float x, float z, uint seed, int octaves)
        {
            float sum = 0f, amp = 1f, norm = 0f, freq = 1f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Value(x * freq, z * freq, unchecked(seed + (uint)o * 131u));
                norm += amp;
                amp *= 0.5f;
                freq *= 2.03f;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        // Ridged fractal in [0, 1]: sharp crests where the noise crosses zero.
        public static float Ridged(float x, float z, uint seed, int octaves)
        {
            float sum = 0f, amp = 1f, norm = 0f, freq = 1f;
            for (int o = 0; o < octaves; o++)
            {
                float r = 1f - Math.Abs(Value(x * freq, z * freq, unchecked(seed + (uint)o * 131u)));
                sum += amp * r * r;
                norm += amp;
                amp *= 0.5f;
                freq *= 2.03f;
            }
            return norm > 0f ? sum / norm : 0f;
        }
    }
}
