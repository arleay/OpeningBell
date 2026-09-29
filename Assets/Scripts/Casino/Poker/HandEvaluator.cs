using System;
using System.Collections.Generic;

namespace OpeningBell.Casino.Poker
{
    public enum HandCategory { HighCard, Pair, TwoPair, Trips, Straight, Flush, FullHouse, Quads, StraightFlush }

    /// <summary>
    /// A poker hand's strength: the category and the ranks that break ties, packed so a bigger <see cref="Score"/>
    /// always wins and equal scores split (suits never break ties).
    /// </summary>
    public readonly struct HandValue : IComparable<HandValue>
    {
        public readonly HandCategory Category;
        /// <summary>Category × 16⁵ plus up to five tie-break ranks, 4 bits each, most significant first.</summary>
        public readonly int Score;
        /// <summary>The deciding ranks (2–14, ace 14; a wheel straight's high card is 5).</summary>
        public readonly int[] Ranks;

        public HandValue(HandCategory category, params int[] ranks)
        {
            Category = category;
            Ranks = ranks;
            int score = (int)category;
            for (int i = 0; i < 5; i++) score = score * 16 + (i < ranks.Length ? ranks[i] : 0);
            Score = score;
        }

        public int CompareTo(HandValue other) => Score.CompareTo(other.Score);

        public string Name
        {
            get
            {
                string R(int r) => HandEvaluator.RankName(r);
                string P(int r) => HandEvaluator.Plural(r);
                return Category switch
                {
                    HandCategory.StraightFlush => Ranks[0] == 14 ? "Royal flush" : $"Straight flush, {R(Ranks[0])} high",
                    HandCategory.Quads => $"Four {P(Ranks[0])}",
                    HandCategory.FullHouse => $"Full house, {P(Ranks[0])} over {P(Ranks[1])}",
                    HandCategory.Flush => $"Flush, {R(Ranks[0])} high",
                    HandCategory.Straight => $"Straight, {R(Ranks[0])} high",
                    HandCategory.Trips => $"Three {P(Ranks[0])}",
                    HandCategory.TwoPair => $"Two pair, {P(Ranks[0])} and {P(Ranks[1])}",
                    HandCategory.Pair => $"Pair of {P(Ranks[0])}",
                    _ => $"{R(Ranks[0])} high",
                };
            }
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Best five-card hand from five to seven cards (CASINO_SPEC §37): straight flush, quads, full house, flush,
    /// straight, trips, two pair, pair, high card, with kickers. The ace plays high, and low in A-2-3-4-5.
    /// </summary>
    public static class HandEvaluator
    {
        /// <summary>Poker rank: ace 14, king 13 … two 2.</summary>
        public static int Rank(Card c) => c.Rank == 1 ? 14 : c.Rank;

        public static string RankName(int r) => r switch
        {
            14 => "Ace", 13 => "King", 12 => "Queen", 11 => "Jack", 10 => "Ten", 9 => "Nine", 8 => "Eight", 7 => "Seven",
            6 => "Six", 5 => "Five", 4 => "Four", 3 => "Three", _ => "Two",
        };

        public static string Plural(int r) => r == 6 ? "Sixes" : RankName(r) + "s";

        public static HandValue Evaluate(IReadOnlyList<Card> cards)
        {
            if (cards.Count < 5) throw new ArgumentException("Need at least five cards.");
            var counts = new int[15];
            var suitMask = new int[4];
            var suitCount = new int[4];
            int mask = 0;
            foreach (Card c in cards)
            {
                int r = Rank(c);
                counts[r]++;
                suitMask[(int)c.Suit] |= 1 << r;
                suitCount[(int)c.Suit]++;
                mask |= 1 << r;
            }

            for (int s = 0; s < 4; s++)
            {
                if (suitCount[s] < 5) continue;
                int high = StraightHigh(suitMask[s]);
                if (high > 0) return new HandValue(HandCategory.StraightFlush, high);
            }

            int quads = 0, tripsA = 0, tripsB = 0, pairA = 0, pairB = 0, pairC = 0;
            for (int r = 14; r >= 2; r--)
            {
                if (counts[r] == 4) quads = r;
                else if (counts[r] == 3)
                {
                    if (tripsA == 0) tripsA = r;
                    else if (tripsB == 0) tripsB = r;
                }
                else if (counts[r] == 2)
                {
                    if (pairA == 0) pairA = r;
                    else if (pairB == 0) pairB = r;
                    else if (pairC == 0) pairC = r;
                }
            }

            if (quads > 0) return new HandValue(HandCategory.Quads, quads, Kickers(counts, 1, quads)[0]);
            if (tripsA > 0 && (tripsB > 0 || pairA > 0))
                return new HandValue(HandCategory.FullHouse, tripsA, Math.Max(tripsB, pairA));

            for (int s = 0; s < 4; s++)
            {
                if (suitCount[s] < 5) continue;
                var top = new List<int>();
                for (int r = 14; r >= 2 && top.Count < 5; r--)
                    if ((suitMask[s] & (1 << r)) != 0) top.Add(r);
                return new HandValue(HandCategory.Flush, top.ToArray());
            }

            int straight = StraightHigh(mask);
            if (straight > 0) return new HandValue(HandCategory.Straight, straight);
            if (tripsA > 0)
            {
                int[] k = Kickers(counts, 2, tripsA);
                return new HandValue(HandCategory.Trips, tripsA, k[0], k[1]);
            }
            if (pairA > 0 && pairB > 0)
                return new HandValue(HandCategory.TwoPair, pairA, pairB, Kickers(counts, 1, pairA, pairB)[0]);
            if (pairA > 0)
            {
                int[] k = Kickers(counts, 3, pairA);
                return new HandValue(HandCategory.Pair, pairA, k[0], k[1], k[2]);
            }
            return new HandValue(HandCategory.HighCard, Kickers(counts, 5));
        }

        /// <summary>Highest straight in a rank bit mask (bit r set = rank r present), or 0. The ace also counts as 1.</summary>
        private static int StraightHigh(int mask)
        {
            if ((mask & (1 << 14)) != 0) mask |= 1 << 1;
            for (int high = 14; high >= 5; high--)
            {
                int run = 0x1F << (high - 4);
                if ((mask & run) == run) return high;
            }
            return 0;
        }

        /// <summary>The <paramref name="n"/> highest ranks present, skipping <paramref name="used"/>.</summary>
        private static int[] Kickers(int[] counts, int n, params int[] used)
        {
            var k = new List<int>();
            for (int r = 14; r >= 2 && k.Count < n; r--)
                if (counts[r] > 0 && Array.IndexOf(used, r) < 0) k.Add(r);
            while (k.Count < n) k.Add(0);
            return k.ToArray();
        }

        /// <summary>
        /// Chen formula for a starting hand, scaled to 0–1: the pre-flop strength an experienced player sees at a
        /// glance (AA 1.0, 72 offsuit ≈ 0.0).
        /// </summary>
        public static double StartingStrength(Card a, Card b)
        {
            int hi = Math.Max(Rank(a), Rank(b)), lo = Math.Min(Rank(a), Rank(b));
            double score = hi switch { 14 => 10, 13 => 8, 12 => 7, 11 => 6, _ => hi / 2.0 };
            if (hi == lo) score = Math.Max(5, score * 2);
            if (a.Suit == b.Suit) score += 2;
            int gap = hi - lo - 1;
            if (hi != lo)
            {
                score -= gap switch { 0 => 0, 1 => 1, 2 => 2, 3 => 4, _ => 5 };
                if (gap <= 1 && hi < 12) score += 1;
            }
            return Math.Max(0, Math.Min(1, (Math.Ceiling(score) + 1) / 21.0));
        }
    }
}
