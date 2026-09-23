using System;
using System.Collections.Generic;

namespace OpeningBell.Market
{
    /// <summary>A fair value gap: a three-candle imbalance where the middle candle moved so fast it left a price gap.</summary>
    public struct FairValueGap
    {
        /// <summary>Index of the middle (displacement) candle.</summary>
        public int Index;
        public bool Bullish;
        public decimal Low, High;
        /// <summary>0 = untouched, 1 = fully filled (mitigated), by later candles.</summary>
        public double Filled;
    }

    /// <summary>A close through the last swing: with the trend = break of structure, against it = change of character.</summary>
    public struct StructureBreak
    {
        public int SwingIndex, BreakIndex;
        public decimal Price;
        public bool Up, ChangeOfCharacter;
    }

    /// <summary>A wick through a swing high/low that closed back inside: the liquidity beyond it was taken.</summary>
    public struct LiquiditySweep
    {
        public int SwingIndex, Index;
        public decimal Price;
        public bool High;
    }

    /// <summary>Two swing highs (or lows) at about the same price: an obvious pool of stops.</summary>
    public struct EqualLevel
    {
        public int First, Second;
        public decimal Price;
        public bool High;
    }

    public sealed class StructureResult
    {
        public readonly List<FairValueGap> Gaps = new List<FairValueGap>();
        public readonly List<StructureBreak> Breaks = new List<StructureBreak>();
        public readonly List<LiquiditySweep> Sweeps = new List<LiquiditySweep>();
        public readonly List<EqualLevel> Equals = new List<EqualLevel>();
        public readonly List<(int Index, bool High)> Swings = new List<(int, bool)>();

        public void Clear()
        {
            Gaps.Clear();
            Breaks.Clear();
            Sweeps.Clear();
            Equals.Clear();
            Swings.Clear();
        }
    }

    /// <summary>
    /// ICT/SMC-style structure, detected from candles after the fact. It describes what price did; nothing in the
    /// simulation is forced to respect it (see MARKET_SPEC.md). Works on any timeframe's series.
    /// </summary>
    public static class StructureDetector
    {
        /// <summary>Candles on each side that must be lower (higher) for a swing high (low).</summary>
        private const int PivotSpan = 3;

        public static void Detect(CandleSeries s, int first, int count, StructureResult result, int lookback = 200)
        {
            result.Clear();
            int start = Math.Max(0, first - lookback);
            int end = Math.Min(s.Count, first + count); // exclusive
            if (end - start < 5) return;

            // Typical candle range, the yardstick for "significant" gaps and "equal" highs.
            double range = 0;
            for (int i = start; i < end; i++) range += (double)(s[i].High - s[i].Low);
            range /= end - start;
            if (range <= 0) return;

            // ---- fair value gaps, and how much of each later trading filled
            for (int i = start + 1; i < end - 1; i++)
            {
                Candle a = s[i - 1], c = s[i + 1];
                bool bull = c.Low > a.High, bear = c.High < a.Low;
                if (!bull && !bear) continue;
                decimal lo = bull ? a.High : c.High, hi = bull ? c.Low : a.Low;
                if ((double)(hi - lo) < 0.25 * range) continue;
                double filled = 0;
                for (int j = i + 2; j < end && filled < 1; j++)
                {
                    decimal into = bull ? hi - s[j].Low : s[j].High - lo;
                    if (into > 0) filled = Math.Max(filled, Math.Min(1, (double)(into / (hi - lo))));
                }
                if (i + 1 >= first - 30) result.Gaps.Add(new FairValueGap { Index = i, Bullish = bull, Low = lo, High = hi, Filled = filled });
            }

            // ---- swings, then walk forward: breaks (BOS / CHoCH), sweeps, equal highs/lows
            var swingHigh = new bool[end - start];
            var swingLow = new bool[end - start];
            for (int i = start + PivotSpan; i < end - PivotSpan; i++)
            {
                bool hi = true, lo = true;
                for (int k = 1; k <= PivotSpan && (hi || lo); k++)
                {
                    if (s[i - k].High >= s[i].High || s[i + k].High >= s[i].High) hi = false;
                    if (s[i - k].Low <= s[i].Low || s[i + k].Low <= s[i].Low) lo = false;
                }
                swingHigh[i - start] = hi;
                swingLow[i - start] = lo;
                if (hi) result.Swings.Add((i, true));
                if (lo) result.Swings.Add((i, false));
            }

            int trend = 0;
            int lastHigh = -1, lastLow = -1, prevHigh = -1, prevLow = -1;
            bool highBroken = false, lowBroken = false;
            for (int i = start; i < end; i++)
            {
                Candle c = s[i];
                if (lastHigh >= 0 && !highBroken && c.High > s[lastHigh].High)
                {
                    if (c.Close > s[lastHigh].High)
                    {
                        result.Breaks.Add(new StructureBreak { SwingIndex = lastHigh, BreakIndex = i, Price = s[lastHigh].High, Up = true, ChangeOfCharacter = trend < 0 });
                        trend = 1;
                        highBroken = true;
                    }
                    else
                    {
                        result.Sweeps.Add(new LiquiditySweep { SwingIndex = lastHigh, Index = i, Price = s[lastHigh].High, High = true });
                        highBroken = true; // taken once; a new swing makes a new pool
                    }
                }
                if (lastLow >= 0 && !lowBroken && c.Low < s[lastLow].Low)
                {
                    if (c.Close < s[lastLow].Low)
                    {
                        result.Breaks.Add(new StructureBreak { SwingIndex = lastLow, BreakIndex = i, Price = s[lastLow].Low, Up = false, ChangeOfCharacter = trend > 0 });
                        trend = -1;
                        lowBroken = true;
                    }
                    else
                    {
                        result.Sweeps.Add(new LiquiditySweep { SwingIndex = lastLow, Index = i, Price = s[lastLow].Low, High = false });
                        lowBroken = true;
                    }
                }

                // A swing is only known once PivotSpan candles after it have printed.
                int confirmed = i - PivotSpan;
                if (confirmed < start) continue;
                if (swingHigh[confirmed - start])
                {
                    prevHigh = lastHigh;
                    lastHigh = confirmed;
                    highBroken = false;
                    if (prevHigh >= 0 && Math.Abs((double)(s[prevHigh].High - s[lastHigh].High)) < 0.15 * range)
                        result.Equals.Add(new EqualLevel { First = prevHigh, Second = lastHigh, Price = Math.Max(s[prevHigh].High, s[lastHigh].High), High = true });
                }
                if (swingLow[confirmed - start])
                {
                    prevLow = lastLow;
                    lastLow = confirmed;
                    lowBroken = false;
                    if (prevLow >= 0 && Math.Abs((double)(s[prevLow].Low - s[lastLow].Low)) < 0.15 * range)
                        result.Equals.Add(new EqualLevel { First = prevLow, Second = lastLow, Price = Math.Min(s[prevLow].Low, s[lastLow].Low), High = false });
                }
            }
        }
    }
}
