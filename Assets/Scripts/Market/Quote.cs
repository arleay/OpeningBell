using System;

namespace OpeningBell.Market
{
    /// <summary>Level 1 snapshot plus the most recent tick's trade print.</summary>
    public readonly struct Quote
    {
        public decimal Bid { get; }
        public decimal Ask { get; }
        public long BidSize { get; }
        public long AskSize { get; }
        public decimal Last { get; }

        /// <summary>Shares traded during the latest tick (0 = no print).</summary>
        public long LastVolume { get; }

        /// <summary>+1 buyer-initiated (printed at ask), -1 seller-initiated (at bid), 0 no print.</summary>
        public int LastDirection { get; }

        public DateTime Time { get; }

        /// <summary>Highest and lowest prints during the latest tick (a sweep's wick), for stop triggers. Last if none.</summary>
        public decimal TickHigh => _tickHigh > 0m ? _tickHigh : Last;
        public decimal TickLow => _tickLow > 0m ? _tickLow : Last;
        private readonly decimal _tickHigh, _tickLow;

        public Quote(decimal bid, decimal ask, long bidSize, long askSize, decimal last, long lastVolume, int lastDirection, DateTime time,
            decimal tickHigh = 0m, decimal tickLow = 0m)
        {
            _tickHigh = tickHigh;
            _tickLow = tickLow;
            Bid = bid;
            Ask = ask;
            BidSize = bidSize;
            AskSize = askSize;
            Last = last;
            LastVolume = lastVolume;
            LastDirection = lastDirection;
            Time = time;
        }

        public decimal Mid => (Bid + Ask) / 2m;
        public decimal Spread => Ask - Bid;
    }
}
