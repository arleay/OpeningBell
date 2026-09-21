namespace OpeningBell.Core
{
    /// <summary>
    /// Hands out independent named random streams derived from one world seed. Each consumer gets its
    /// own stream, so adding a new consumer (or a new security) never shifts another consumer's sequence.
    /// </summary>
    public sealed class SeededRandomService
    {
        public ulong Seed { get; }

        public SeededRandomService(ulong seed)
        {
            Seed = seed;
        }

        public SeededRandom CreateStream(string name)
        {
            ulong mixed = Seed;
            ulong a = SeededRandom.SplitMix64(ref mixed);
            return new SeededRandom(a ^ Fnv1a64(name));
        }

        // string.GetHashCode is randomized per process on modern runtimes; FNV-1a is stable.
        private static ulong Fnv1a64(string text)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                foreach (char c in text)
                {
                    hash ^= c;
                    hash *= 1099511628211UL;
                }
                return hash;
            }
        }
    }
}
