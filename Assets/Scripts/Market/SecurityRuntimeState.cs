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
