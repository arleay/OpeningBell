using System;
using System.Collections.Generic;

namespace OpeningBell.Market
{
    /// <summary>Why a price is remembered. Append only: saved by value.</summary>
    public enum LevelKind
    {
        PreviousDayHigh,
        PreviousDayLow,
        PreviousClose,
        PreviousWeekHigh,
        PreviousWeekLow,
        PremarketHigh,
        PremarketLow,
        OpeningRangeHigh,
        OpeningRangeLow,
        SwingHigh,
        SwingLow,
        RoundNumber,
        DayHigh,
        DayLow,
    }

    /// <summary>
    /// A price the market remembers, with the resting interest around it. Level traders keep a wall of limit
    /// orders at it (offers above price, bids below), and stops pile up just beyond it: the losing side's stop-losses
    /// plus breakout traders' entry stops. Walls get eaten by tests and only partly come back; stops fire when price
    /// crosses them, adding market orders that push further (the fuel for sweeps and breakouts). Nothing here moves
    /// price by itself: it only changes what flow meets on the way.
    /// </summary>
    public sealed class Level
    {
        public LevelKind Kind;
        /// <summary>Log price.</summary>
        public double Log;
        /// <summary>How much traders care, 0..1+. Fades with age, rises with equal highs/lows and reactions.</summary>
        public double Strength;
        public int Touches;
        /// <summary>+1: the level is above the last price (resistance), −1: below (support).</summary>
        public int Side;
        /// <summary>Resting limit interest at the level, in log-price push it can absorb.</summary>
        public double Wall;
        /// <summary>Stops resting just beyond the level, in log-price push they add when triggered.</summary>
        public double Stops;
        /// <summary>Minutes since this level was created or last reinforced.</summary>
        public double Age;
        /// <summary>Minutes since price last broke through the level (large if never): a quick break back is a sweep.</summary>
        public double SinceBreak = double.MaxValue;

        public bool IsHighSide => IsHigh(Kind);

        public static bool IsHigh(LevelKind k) => k == LevelKind.PreviousDayHigh || k == LevelKind.PreviousWeekHigh || k == LevelKind.PremarketHigh ||
                                                 k == LevelKind.OpeningRangeHigh || k == LevelKind.SwingHigh || k == LevelKind.DayHigh;
    }

    /// <summary>
    /// A stock's market memory: previous-session, premarket, opening-range, swing, round-number and session-extreme
    /// levels, with fixed capacity (the weakest are forgotten first). Equal highs/lows merge and grow stronger.
    /// </summary>
    public sealed class LevelBook
    {
        public const int Capacity = 28;
        private readonly List<Level> _levels = new List<Level>(Capacity);

        public IReadOnlyList<Level> All => _levels;

        internal List<Level> Mutable => _levels;

        /// <summary>
        /// Adds or reinforces a level. Within <paramref name="mergeLog"/> of an existing level of the same family it
        /// merges: equal highs/lows are more obvious, so more traders watch them and more stops rest beyond.
        /// </summary>
        public Level Add(LevelKind kind, double log, double strength, int side, double mergeLog)
        {
            foreach (Level l in _levels)
            {
                if (Math.Abs(l.Log - log) > mergeLog) continue;
                if (l.IsHighSide != Level.IsHigh(kind) && l.Kind != LevelKind.RoundNumber && kind != LevelKind.RoundNumber) continue;
                l.Strength = Math.Min(2.0, Math.Max(l.Strength, strength) + 0.25 * Math.Min(l.Strength, strength));
                l.Age = 0;
                if (Priority(kind) > Priority(l.Kind)) l.Kind = kind;
                return l;
            }

            var level = new Level { Kind = kind, Log = log, Strength = strength, Side = side };
            if (_levels.Count >= Capacity)
            {
                int weakest = 0;
                for (int i = 1; i < _levels.Count; i++)
                    if (_levels[i].Strength < _levels[weakest].Strength) weakest = i;
                if (_levels[weakest].Strength >= strength) return null;
                _levels.RemoveAt(weakest);
            }
            _levels.Add(level);
            return level;
        }

        /// <summary>Moves (or creates) the single live level of a kind, e.g. today's high as it extends.</summary>
        public Level Track(LevelKind kind, double log, double strength, int side)
        {
            foreach (Level l in _levels)
                if (l.Kind == kind)
                {
                    // A new extreme: the level moves out with price and is resistance (or support) again, not
                    // something price has just broken.
                    if (l.Log != log) l.Side = side;
                    l.Log = log;
                    l.Age = 0;
                    return l;
                }
            return Add(kind, log, strength, side, 0);
        }

        public void Remove(LevelKind kind) => _levels.RemoveAll(l => l.Kind == kind);

        public void Clear() => _levels.Clear();

        /// <summary>Named session levels outrank anonymous swings when two merge.</summary>
        private static int Priority(LevelKind k) => k switch
        {
            LevelKind.PreviousDayHigh or LevelKind.PreviousDayLow => 6,
            LevelKind.PremarketHigh or LevelKind.PremarketLow => 5,
            LevelKind.OpeningRangeHigh or LevelKind.OpeningRangeLow => 4,
            LevelKind.PreviousWeekHigh or LevelKind.PreviousWeekLow => 4,
            LevelKind.PreviousClose => 3,
            LevelKind.DayHigh or LevelKind.DayLow => 2,
            LevelKind.SwingHigh or LevelKind.SwingLow => 1,
            _ => 0,
        };
    }
}
