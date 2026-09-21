using System;

namespace OpeningBell.Market
{
    public enum Timeframe
    {
        Minute1,
        Minute5,
        Minute15,
        Hour1,
        Day1,
    }

    public static class TimeframeExtensions
    {
        public static TimeSpan Duration(this Timeframe timeframe)
        {
            switch (timeframe)
            {
                case Timeframe.Minute1: return TimeSpan.FromMinutes(1);
                case Timeframe.Minute5: return TimeSpan.FromMinutes(5);
                case Timeframe.Minute15: return TimeSpan.FromMinutes(15);
                case Timeframe.Hour1: return TimeSpan.FromHours(1);
                case Timeframe.Day1: return TimeSpan.FromDays(1);
                default: throw new ArgumentOutOfRangeException(nameof(timeframe));
            }
        }
    }

    public readonly struct Candle
    {
        public DateTime Start { get; }
        public decimal Open { get; }
        public decimal High { get; }
        public decimal Low { get; }
        public decimal Close { get; }
        public long Volume { get; }

        /// <summary>Σ price × shares of the prints in this candle (for VWAP).</summary>
        public decimal Notional { get; }

        public Candle(DateTime start, decimal open, decimal high, decimal low, decimal close, long volume, decimal notional)
        {
            Start = start;
            Open = open;
            High = high;
            Low = low;
            Close = close;
            Volume = volume;
            Notional = notional;
        }

        internal Candle Include(decimal price, long volume) =>
            new Candle(Start, Open, Math.Max(High, price), Math.Min(Low, price), price, Volume + volume, Notional + price * volume);
    }
}
