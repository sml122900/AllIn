using System;

namespace AllIn.Core
{
    /// <summary>
    /// Every random draw in the game goes through this. System.Random / UnityEngine.Random are banned:
    /// their sequences can differ between Mono and .NET, which would split the harness from the editor.
    /// </summary>
    public interface IRng
    {
        uint NextUInt();
    }

    /// <summary>xoshiro128** (Blackman &amp; Vigna), seeded via splitmix64. State is 4 uints and round-trips through saves.</summary>
    public sealed class Xoshiro128 : IRng
    {
        uint _s0, _s1, _s2, _s3;

        public Xoshiro128(ulong seed)
        {
            ulong x = seed;
            ulong a = SplitMix64(ref x), b = SplitMix64(ref x);
            _s0 = (uint)a; _s1 = (uint)(a >> 32); _s2 = (uint)b; _s3 = (uint)(b >> 32);
            if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1;
        }

        public Xoshiro128(uint[] state)
        {
            if (state == null || state.Length != 4) throw new ArgumentException("xoshiro128 state must be 4 uints");
            _s0 = state[0]; _s1 = state[1]; _s2 = state[2]; _s3 = state[3];
            if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1;
        }

        public uint[] GetState() => new[] { _s0, _s1, _s2, _s3 };

        static ulong SplitMix64(ref ulong x)
        {
            ulong z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        static uint Rotl(uint x, int k) => (x << k) | (x >> (32 - k));

        public uint NextUInt()
        {
            uint result = Rotl(_s1 * 5, 7) * 9;
            uint t = _s1 << 9;
            _s2 ^= _s0; _s3 ^= _s1; _s1 ^= _s2; _s0 ^= _s3;
            _s2 ^= t;
            _s3 = Rotl(_s3, 11);
            return result;
        }
    }

    public static class RngExtensions
    {
        /// <summary>Uniform int in [0, n). Lemire's multiply-shift with rejection (unbiased).</summary>
        public static int NextInt(this IRng rng, int n)
        {
            if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
            uint un = (uint)n;
            ulong m = (ulong)rng.NextUInt() * un;
            uint low = (uint)m;
            if (low < un)
            {
                uint threshold = (uint)(-(int)un) % un;
                while (low < threshold)
                {
                    m = (ulong)rng.NextUInt() * un;
                    low = (uint)m;
                }
            }
            return (int)(m >> 32);
        }

        /// <summary>Uniform int in [min, maxExclusive).</summary>
        public static int NextInt(this IRng rng, int min, int maxExclusive) => min + rng.NextInt(maxExclusive - min);

        /// <summary>Uniform double in [0, 1) with 53 bits.</summary>
        public static double NextDouble(this IRng rng)
        {
            ulong a = rng.NextUInt() >> 5, b = rng.NextUInt() >> 6;
            return (a * 67108864.0 + b) / 9007199254740992.0;
        }

        public static bool Chance(this IRng rng, double p) => rng.NextDouble() < p;

        /// <summary>Standard normal via Box–Muller (one value per call; the pair's second value is discarded to keep the stream simple).</summary>
        public static double NextGaussian(this IRng rng)
        {
            double u1 = 1.0 - rng.NextDouble(); // (0, 1]
            double u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        /// <summary>Student-t with 3 degrees of freedom: Z0 / sqrt((Z1² + Z2² + Z3²) / 3).</summary>
        public static double NextStudentT3(this IRng rng)
        {
            double z0 = rng.NextGaussian();
            double z1 = rng.NextGaussian(), z2 = rng.NextGaussian(), z3 = rng.NextGaussian();
            return z0 / Math.Sqrt((z1 * z1 + z2 * z2 + z3 * z3) / 3.0);
        }

        /// <summary>Fisher–Yates in place.</summary>
        public static void Shuffle<T>(this IRng rng, T[] items)
        {
            for (int i = items.Length - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }
    }
}
