using System;
using OpeningBell.Core;

namespace OpeningBell.Market
{
    /// <summary>Live state of one security. Written only by the price engine; read by everything else.</summary>
    public sealed class SecurityRuntimeState
    {
        public SecuritySpec Spec { get; }
        public string Ticker => Spec.Ticker;
        public Quote Quote { get; internal set; }
        public decimal Last => Quote.Last;
        public decimal Bid => Quote.Bid;
        public decimal Ask => Quote.Ask;

        /// <summary>Previous regular-session close.</summary>
        public decimal PreviousClose { get; internal set; }

        /// <summary>Today's range and volume across all sessions. 0 until the first print of the day.</summary>
        public decimal DayHigh { get; internal set; }
        public decimal DayLow { get; internal set; }
        public long DayVolume { get; internal set; }

        /// <summary>Volume-weighted average price of today's prints, extended hours included. 0 before the first print.</summary>
        public decimal Vwap => DayVolume == 0 ? 0m : DayNotional / DayVolume;

        public decimal Change => Last - PreviousClose;
        public decimal ChangePercent => PreviousClose == 0m ? 0m : Change / PreviousClose * 100m;

        public CandleAggregator Candles { get; }

        // Price model state, in log-price space (see PriceEngine).
        internal double FairLog;
        internal double DeviationLog;
        internal double Momentum;
        internal double ActivityLog;
        internal readonly int SectorIndex;
        internal decimal RegularClose;
        internal decimal DayNotional;
        // News catalysts, consumed by the next tick so the move is part of that tick's return (momentum/volume see it).
        internal double NewsImpulseFair;       // immediate permanent part
        internal double NewsImpulseDeviation;  // transient over/under-reaction
        internal double PendingNewsLog;        // permanent part still being delivered
        internal double NewsActivityLog;       // decaying attention boost (log multiplier)
        internal readonly SeededRandom Rng;
        /// <summary>Order-flow model state (participants, book interest, memory, day and regime).</summary>
        internal readonly FlowState Flow = new FlowState();

        /// <summary>The stock's remembered levels (for the chart's liquidity overlays and the debug view).</summary>
        public LevelBook Levels => Flow.Levels;
        /// <summary>Hidden: today's character and the current regime (debug overlay only).</summary>
        public DayType DayType => Flow.Day.Type;
        public Regime Regime => Flow.Regime;
        /// <summary>Hidden: live supply/demand zones and fair value gaps with their remaining interest (debug view).</summary>
        public ZoneBook Zones => Flow.Zones;

        /// <summary>
        /// Hidden participant state for the developer overlay: leg, 5m structure, institutional pressure (net push
        /// still to be worked), retail and momentum weights, market makers' withdrawal and the group's pull.
        /// </summary>
        /// <summary>
        /// TEMPORARY cheat signal, −1..+1 (+ = buy). It reads the hidden market, so it really works (on average):
        /// the gap to fair value (where informed traders are pushing), institutions' unfinished orders, the current
        /// leg of the day and the day's drift. Remove before release.
        /// </summary>
        public double CheatSignal
        {
            get
            {
                FlowState f = Flow;
                double sd = Spec.DailyVolatility, pressure = 0;
                foreach (MetaOrder m in f.Metas) pressure += m.Side * m.Remaining;
                double price = FairLog + DeviationLog;
                double leg = f.Leg == LegKind.Pause ? 0 : Math.Sign(f.LegRate);
                double drift = Math.Sign(f.Day.DriftAt(f.MinutesSinceOpen < 0 ? 0 : f.MinutesSinceOpen));
                double score = (FairLog - price) / (0.15 * sd) + pressure / (0.1 * sd) + 0.8 * leg + 0.4 * drift;
                return Math.Tanh(score / 2);
            }
        }

        public string DebugSummary
        {
            get
            {
                FlowState f = Flow;
                double sd = Spec.DailyVolatility, pressure = 0;
                foreach (MetaOrder m in f.Metas) pressure += m.Side * m.Remaining;
                double group = f.SectorVar > 0 ? f.SectorMom / Math.Sqrt(f.SectorVar) * Math.Sqrt(900) : 0; // ≈ 30 minutes of 2 s steps (display only)
                return $"leg {f.Leg} · structure {(f.Structure > 0 ? "bull" : f.Structure < 0 ? "bear" : "none")} · inst {f.Metas.Count} ({pressure / sd:+0.00;-0.00}σ) · " +
                       $"retail {f.Day.Retail:0.0} · mom {f.Day.Momentum:0.0} · MM pulled {f.Withdraw:0.00} · group z {group:+0.0;-0.0} · zones {f.Zones.All.Count}";
            }
        }

        internal SecurityRuntimeState(SecuritySpec spec, SeededRandom rng, int maxCandles)
        {
            Spec = spec;
            Rng = rng;
            SectorIndex = (int)spec.Sector;
            Candles = new CandleAggregator(maxCandles);
        }
    }

    public sealed class MarketIndex
    {
        public IndexSpec Spec { get; }
        public string Ticker => Spec.Ticker;
        public decimal Level { get; internal set; }
        public decimal PreviousClose { get; internal set; }
        public decimal Change => Level - PreviousClose;
        public decimal ChangePercent => PreviousClose == 0m ? 0m : Change / PreviousClose * 100m;
        public CandleAggregator Candles { get; }

        internal double LogLevel;
        internal double NewsImpulse;
        internal double PendingNewsLog;
        internal decimal RegularClose;

        internal MarketIndex(IndexSpec spec, int maxCandles)
        {
            Spec = spec;
            Candles = new CandleAggregator(maxCandles);
        }
    }
}
