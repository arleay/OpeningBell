using System.Collections.Generic;

namespace OpeningBell.Market
{
    /// <summary>
    /// An institution working a large order over time (buy 40k, pause, 65k, absorb sellers...). Part of it is taken
    /// aggressively, part rests as a pegged bid/offer that absorbs the other side. It stops chasing past its limit and
    /// just waits there, which is how demand and supply zones come about.
    /// </summary>
    internal sealed class MetaOrder
    {
        public int Side;
        /// <summary>Push still to deliver, in log-price units at normal liquidity.</summary>
        public double Remaining;
        /// <summary>Aggressive push per step while working.</summary>
        public double Rate;
        /// <summary>0..1: share taken aggressively (the rest rests passively).</summary>
        public double Urgency;
        /// <summary>Log price past which it stops chasing.</summary>
        public double LimitLog;
        /// <summary>Passive interest resting at the peg right now (log push it can absorb).</summary>
        public double Passive;
    }

    /// <summary>What the current leg of the day is doing. Append only: saved by value.</summary>
    public enum LegKind
    {
        Pause,
        Impulse,
        Pullback,
        Rotation,
    }

    /// <summary>Everything the order-flow model remembers about one stock between steps (all of it is saved).</summary>
    internal sealed class FlowState
    {
        public DayProfile Day = DayProfile.Neutral;
        public Regime Regime = Regime.Range;
        public double RegimeMinutes;

        // Short-term statistics of the stock's own prints.
        public double Mom5, Mom30;          // EMAs of the per-step log return (≈5 and 30 minutes)
        public double Var, VarSlow;          // EMAs of the squared per-step return (realised volatility)
        public double GrossEma;              // typical gross flow per step (for volume normalisation)
        public double Withdraw;              // market makers pulling depth after violent moves (0 = normal)

        // Bursts of one-sided flow, in log push still to deliver.
        public double Burst;                 // breakout traders piling in after a level breaks
        public double NewsFlow;              // news traders hitting the tape right after a headline

        public readonly List<MetaOrder> Metas = new List<MetaOrder>();
        public readonly LevelBook Levels = new LevelBook();
        public readonly ZoneBook Zones = new ZoneBook();

        /// <summary>5-minute market structure as swing traders read it: +1 after a swing high breaks, −1 after a swing low.</summary>
        public int Structure;
        /// <summary>EMAs of the per-step market + sector move and its square (rotation traders follow the group).</summary>
        public double SectorMom, SectorVar;
        /// <summary>Signed push institutions absorbed this minute (+ = buyers soaked up selling): where demand shows.</summary>
        public double Absorbed;

        // Session trackers (log prices; NaN = none yet).
        public double PremarketHigh = double.NaN, PremarketLow = double.NaN;
        public double SessionHigh = double.NaN, SessionLow = double.NaN;
        public double SessionOpen = double.NaN;
        public double MinutesSinceOpen = -1;
        public int StepsInMinute;
        public bool OpenAuctionDone;
        /// <summary>Start (ticks) of the last 5-minute bar checked for swings (by time: counts change with trimming and loads).</summary>
        public long LastSwingTicks;
        /// <summary>Start (ticks) of the last hourly bar checked for swings.</summary>
        public long LastHourSwingTicks;

        // The current leg of the day's move (regular session): how the day's direction actually arrives.
        public LegKind Leg;
        /// <summary>Log move per step the leg carries into fair value and price together (program-like flow).</summary>
        public double LegRate;
        public double LegMinutes;
        /// <summary>Log intensity of the current leg's delivery (AR(1) around 0): legs arrive in bursts, not ramps.</summary>
        public double LegPulse;

        // The last step's traded range (for wicks and, later, stop triggers).
        public double PathHigh, PathLow;
    }
}
