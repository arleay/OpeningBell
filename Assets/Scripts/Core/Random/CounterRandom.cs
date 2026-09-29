using System;

namespace OpeningBell.Core
{
    /// <summary>
    /// Counter-based random numbers: every draw is a hash of (seed, stream key, counter), so there is no state to carry,
    /// save or restore, and a value never depends on how many draws came before it or how time was chunked. Used by
    /// systems that decide many small things per person per minute (the hedge fund), where a saved stream per person
    /// would be fragile. Derived from the world seed like <see cref="SeededRandomService"/> streams.
    /// </summary>
    public readonly struct CounterRandom
    {
        private readonly ulong _key;

        public CounterRandom(ulong worldSeed, string stream)
        {
            ulong mixed = worldSeed;
            _key = SeededRandom.SplitMix64(ref mixed) ^ Fnv1a64(stream);
        }

        private CounterRandom(ulong key) => _key = key;

        /// <summary>A sub-stream (e.g. one per employee), independent of the parent's draws.</summary>
        public CounterRandom Sub(string name) => new CounterRandom(Mix(_key ^ Fnv1a64(name)));

        public CounterRandom Sub(long id) => new CounterRandom(Mix(_key + 0x9E3779B97F4A7C15UL * (ulong)id));

        /// <summary>Uniform 64 bits for (a, b, c).</summary>
        public ulong Bits(long a, long b = 0, long c = 0)
        {
            ulong h = _key;
            h = Mix(h ^ (ulong)a);
            h = Mix(h + 0x632BE59BD9B4E019UL ^ (ulong)b);
            h = Mix(h + 0x8CB92BA72F3D8DD7UL ^ (ulong)c);
            return h;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double Double(long a, long b = 0, long c = 0) => (Bits(a, b, c) >> 11) * (1.0 / (1UL << 53));

        /// <summary>Uniform integer in [0, max).</summary>
        public int Int(int max, long a, long b = 0, long c = 0) => max <= 0 ? 0 : (int)(Bits(a, b, c) % (ulong)max);

        /// <summary>Standard normal (Box–Muller from two independent draws).</summary>
        public double Gaussian(long a, long b = 0, long c = 0)
        {
            double u1 = Math.Max(1e-12, Double(a, b, c * 2 + 1));
            double u2 = Double(a, b, c * 2 + 2);
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        /// <summary>True with probability <paramref name="p"/>.</summary>
        public bool Chance(double p, long a, long b = 0, long c = 0) => Double(a, b, c) < p;

        // SplitMix64's finaliser: a strong 64-bit mixer.
        private static ulong Mix(ulong z)
        {
            unchecked
            {
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        private static ulong Fnv1a64(string text)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                foreach (char c in text ?? "")
                {
                    hash ^= c;
                    hash *= 1099511628211UL;
                }
                return hash;
            }
        }
    }
}
