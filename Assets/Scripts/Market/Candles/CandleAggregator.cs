using System;

namespace OpeningBell.Market
{
    /// <summary>
    /// Builds every timeframe directly from trade prints. Intraday candles include extended hours and are
    /// aligned to clock boundaries; daily candles use regular-session prints only.
    /// </summary>
    public sealed class CandleAggregator
    {
        private readonly CandleSeries[] _series;

        public CandleAggregator(int maxCandlesPerSeries)
        {
            var timeframes = (Timeframe[])Enum.GetValues(typeof(Timeframe));
            _series = new CandleSeries[timeframes.Length];
            foreach (Timeframe tf in timeframes)
                _series[(int)tf] = new CandleSeries(tf, maxCandlesPerSeries);
        }

        public CandleSeries Get(Timeframe timeframe) => _series[(int)timeframe];

        internal void Record(DateTime time, decimal price, long volume, bool regularSession)
        {
            for (int i = 0; i < _series.Length; i++)
            {
                var tf = (Timeframe)i;
                if (tf == Timeframe.Day1 && !regularSession) continue;
                _series[i].Record(BucketStart(time, tf), price, volume);
            }
        }

        public static DateTime BucketStart(DateTime time, Timeframe timeframe)
        {
            if (timeframe == Timeframe.Day1) return time.Date;
            long period = timeframe.Duration().Ticks;
            return time.Date + TimeSpan.FromTicks(time.TimeOfDay.Ticks / period * period);
        }
    }
}
