using System;

namespace OpeningBell.Core
{
    /// <summary>Full generator state for saving. Stored as signed longs/bits so any serializer round-trips it exactly.</summary>
    [Serializable]
    public struct RandomState
    {
        public long S0, S1, S2, S3;
        public long SpareGaussianBits;
        public bool HasSpareGaussian;
    }

    /// <summary>
    /// Deterministic xoshiro256** generator. Simulation code must use this instead of
    /// System.Random / UnityEngine.Random, whose sequences are not guaranteed stable across runtimes.
    /// </summary>
    public sealed class SeededRandom
    {
        private ulong _s0, _s1, _s2, _s3;
        private double _spareGaussian;
        private bool _hasSpareGaussian;

        public SeededRandom(ulong seed)
        {
            // SplitMix64 expansion so nearby seeds produce unrelated states (and never the all-zero state).
            _s0 = SplitMix64(ref seed);
            _s1 = SplitMix64(ref seed);
            _s2 = SplitMix64(ref seed);
            _s3 = SplitMix64(ref seed);
        }

        public ulong NextULong()
        {
            unchecked
            {
                ulong result = RotateLeft(_s1 * 5, 7) * 9;
                ulong t = _s1 << 17;
                _s2 ^= _s0;
                _s3 ^= _s1;
                _s1 ^= _s2;
                _s0 ^= _s3;
                _s2 ^= t;
                _s3 = RotateLeft(_s3, 45);
                return result;
            }
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        /// <summary>Uniform integer in [0, maxExclusive).</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return (int)(NextDouble() * maxExclusive);
        }

        /// <summary>Standard normal via the Marsaglia polar method.</summary>
        public double NextGaussian()
        {
            if (_hasSpareGaussian)
            {
                _hasSpareGaussian = false;
                return _spareGaussian;
            }

            double u, v, s;
            do
            {
                u = NextDouble() * 2.0 - 1.0;
                v = NextDouble() * 2.0 - 1.0;
                s = u * u + v * v;
            } while (s >= 1.0 || s == 0.0);

            double scale = Math.Sqrt(-2.0 * Math.Log(s) / s);
            _spareGaussian = v * scale;
            _hasSpareGaussian = true;
            return u * scale;
        }

        public RandomState CaptureState() => new RandomState
        {
            S0 = unchecked((long)_s0),
            S1 = unchecked((long)_s1),
            S2 = unchecked((long)_s2),
            S3 = unchecked((long)_s3),
            SpareGaussianBits = BitConverter.DoubleToInt64Bits(_spareGaussian),
            HasSpareGaussian = _hasSpareGaussian,
        };

        public void RestoreState(RandomState state)
        {
            _s0 = unchecked((ulong)state.S0);
            _s1 = unchecked((ulong)state.S1);
            _s2 = unchecked((ulong)state.S2);
            _s3 = unchecked((ulong)state.S3);
            _spareGaussian = BitConverter.Int64BitsToDouble(state.SpareGaussianBits);
            _hasSpareGaussian = state.HasSpareGaussian;
        }

        internal static ulong SplitMix64(ref ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                ulong z = x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));
    }
}
