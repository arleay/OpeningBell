using System;
using OpeningBell.Core;

namespace OpeningBell.Market
{
    /// <summary>Hidden character of one trading day (never shown to the player). Append only: saved by value.</summary>
    public enum DayType
    {
        Range,
        TrendUp,
        TrendDown,
        GapAndGo,
        GapAndFade,
        MorningReversal,
        Chop,
        Grind,
        Panic,
        Recovery,
        Squeeze,
    }

    /// <summary>
    /// The conditions a day starts with: how much new information arrives in one direction (drift of fair value),
    /// how busy and volatile it is, and how much each kind of participant shows up. It never prescribes candles:
    /// a trend day's drift still has to get through the book, and levels, stops and other traders can fight it.
    /// </summary>
    internal struct DayProfile
    {
        public DayType Type;
        /// <summary>Regular-session drift of fair value in daily-volatility units (+ up). Premarket gets a little.</summary>
        public double Drift;
        /// <summary>Morning-reversal days: minutes after the open when the information flips, and the second drift.</summary>
        public double FlipMinute, DriftAfterFlip;
        /// <summary>Extra overnight gap in daily-volatility units (gap days).</summary>
        public double Gap;
        public double RelativeVolume, Volatility, Liquidity;
        /// <summary>Participation weights around 1.</summary>
        public double Institutional, Momentum, MeanReversion, Breakout, LevelRespect, Retail;

        public static DayProfile Neutral => new DayProfile
        {
            Type = DayType.Range, RelativeVolume = 1, Volatility = 1, Liquidity = 1,
            Institutional = 1, Momentum = 1, MeanReversion = 1, Breakout = 1, LevelRespect = 1, Retail = 1,
            FlipMinute = double.MaxValue,
        };

        /// <summary>
        /// Draws a stock's day. <paramref name="marketDrift"/> (the market's own day, in market-vol units) tilts which
        /// way trend days go, so most stocks lean with the tape without all doing the same thing. Small caps (high
        /// volatility, low float) get squeezes and wilder days.
        /// </summary>
        public static DayProfile Draw(SeededRandom rng, double marketDrift, bool smallCap)
        {
            DayProfile p = Neutral;
            double u = rng.NextDouble();
            double upOdds = Clamp(0.5 + 0.25 * marketDrift, 0.15, 0.85);
            bool up = rng.NextDouble() < upOdds;

            // Rough frequencies of US single-stock days: ranges and trend days dominate, the rest are spice.
            if (u < 0.22) p.Type = DayType.Range;
            else if (u < 0.55) p.Type = up ? DayType.TrendUp : DayType.TrendDown;
            else if (u < 0.61) p.Type = DayType.GapAndGo;
            else if (u < 0.67) p.Type = DayType.GapAndFade;
            else if (u < 0.77) p.Type = DayType.MorningReversal;
            else if (u < 0.85) p.Type = DayType.Chop;
            else if (u < 0.94) p.Type = DayType.Grind;
            else if (u < 0.97) p.Type = up ? DayType.Recovery : DayType.Panic;
            else p.Type = smallCap && up ? DayType.Squeeze : (up ? DayType.TrendUp : DayType.TrendDown);

            double sign = up ? 1 : -1;
            double n() => rng.NextGaussian();
            switch (p.Type)
            {
                case DayType.Range:
                    p.Drift = 0.15 * n();
                    p.MeanReversion = 1.5; p.LevelRespect = 1.4; p.Breakout = 0.6; p.Momentum = 0.7;
                    p.RelativeVolume = 0.85; p.Volatility = 0.9;
                    break;
                case DayType.TrendUp:
                case DayType.TrendDown:
                    sign = p.Type == DayType.TrendUp ? 1 : -1;
                    p.Drift = sign * (1.2 + 0.35 * Math.Abs(n()));
                    p.Momentum = 1.5; p.MeanReversion = 0.6; p.Breakout = 1.3; p.LevelRespect = 0.8; p.Institutional = 1.4;
                    p.RelativeVolume = 1.2; p.Volatility = 0.85;
                    break;
                case DayType.GapAndGo:
                    p.Gap = sign * (0.8 + 0.4 * Math.Abs(n()));
                    p.Drift = sign * (1.1 + 0.35 * Math.Abs(n()));
                    p.Momentum = 1.5; p.Breakout = 1.4; p.MeanReversion = 0.6; p.Institutional = 1.3;
                    p.RelativeVolume = 1.8; p.Volatility = 1.2;
                    break;
                case DayType.GapAndFade:
                    p.Gap = sign * (0.8 + 0.4 * Math.Abs(n()));
                    p.Drift = -sign * (0.9 + 0.3 * Math.Abs(n()));
                    p.MeanReversion = 1.3; p.Breakout = 0.8; p.Institutional = 1.2;
                    p.RelativeVolume = 1.5; p.Volatility = 1.15;
                    break;
                case DayType.MorningReversal:
                    p.Drift = sign * (0.7 + 0.25 * Math.Abs(n()));
                    p.FlipMinute = 25 + 80 * rng.NextDouble();
                    p.DriftAfterFlip = -sign * (1.2 + 0.35 * Math.Abs(n()));
                    p.Momentum = 1.1; p.RelativeVolume = 1.2; p.Volatility = 1.1;
                    break;
                case DayType.Chop:
                    p.Drift = 0.1 * n();
                    p.Volatility = 1.45; p.MeanReversion = 1.6; p.Breakout = 1.1; p.LevelRespect = 0.8; p.Retail = 1.4;
                    p.RelativeVolume = 1.15; p.Liquidity = 0.85;
                    break;
                case DayType.Grind:
                    p.Drift = sign * (0.85 + 0.25 * Math.Abs(n()));
                    p.Volatility = 0.6; p.RelativeVolume = 0.65; p.Momentum = 1.2; p.MeanReversion = 0.8; p.Retail = 0.7;
                    p.Liquidity = 1.2;
                    break;
                case DayType.Panic:
                    p.Gap = -(0.4 + 0.4 * Math.Abs(n()));
                    p.Drift = -(1.2 + 0.4 * Math.Abs(n()));
                    p.Volatility = 1.7; p.RelativeVolume = 2.4; p.Liquidity = 0.6; p.Momentum = 1.7; p.MeanReversion = 0.5;
                    p.Retail = 1.6; p.Institutional = 1.5;
                    break;
                case DayType.Recovery:
                    p.Gap = 0.3 * n();
                    p.Drift = 1.1 + 0.4 * Math.Abs(n());
                    p.Volatility = 1.25; p.RelativeVolume = 1.5; p.Momentum = 1.3; p.Institutional = 1.4;
                    break;
                case DayType.Squeeze:
                    p.Gap = 0.5 + 0.5 * Math.Abs(n());
                    p.Drift = 1.3 + 0.5 * Math.Abs(n());
                    p.Volatility = 1.9; p.RelativeVolume = 3.5; p.Liquidity = 0.55; p.Momentum = 1.9; p.Retail = 2.2;
                    p.Breakout = 1.6; p.MeanReversion = 0.5;
                    break;
            }

            // Every day also varies a little on its own, so no two range days look the same.
            p.RelativeVolume *= Math.Exp(0.25 * n() - 0.03);
            p.Volatility *= Math.Exp(0.15 * n() - 0.01);
            return p;
        }

        /// <summary>The fair-value drift in force <paramref name="minutesSinceOpen"/> into the regular session.</summary>
        public double DriftAt(double minutesSinceOpen) => minutesSinceOpen >= FlipMinute ? DriftAfterFlip : Drift;

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }

    /// <summary>Persistent intraday state of one stock. Append only: saved by value.</summary>
    public enum Regime
    {
        Range,
        Compression,
        Expansion,
        Trend,
    }
}
