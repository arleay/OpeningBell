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

        public Quote(decimal bid, decimal ask, long bidSize, long askSize, decimal last, long lastVolume, int lastDirection, DateTime time)
        {
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
