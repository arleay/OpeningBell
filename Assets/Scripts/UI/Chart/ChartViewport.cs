using System;
using OpeningBell.Market;

namespace OpeningBell.UI
{
    /// <summary>Which candles are visible (zoom/pan) plus chart math that does not depend on rendering.</summary>
    public sealed class ChartViewport
    {
        public const int MinVisible = 15;
        public const int MaxVisible = 400;

        public int VisibleCount { get; private set; }

        /// <summary>
        /// Candles scrolled back from the newest. 0 = newest at the right edge; negative = empty space to the right of
        /// the newest candle (dragged left), kept as new candles arrive.
        /// </summary>
        public float RightOffset { get; private set; }

        public bool IsLive => RightOffset < 0.5f;

        public ChartViewport(int visibleCount = 90)
        {
            VisibleCount = Math.Clamp(visibleCount, MinVisible, MaxVisible);
        }

        public void Zoom(float factor) =>
            VisibleCount = Math.Clamp((int)Math.Round(VisibleCount * factor), MinVisible, MaxVisible);

        /// <summary>Positive = move back in time.</summary>
        public void Pan(float candles, int total) =>
            RightOffset = Math.Clamp(RightOffset + candles, -MaxBlank, Math.Max(0, total - MinVisible));

        /// <summary>Empty slots right of the newest candle.</summary>
        public int BlankSlots => RightOffset < 0 ? Math.Min(VisibleCount - 1, (int)Math.Round(-RightOffset)) : 0;

        /// <summary>At most this much of the view can be empty space right of the newest candle.</summary>
        private float MaxBlank => VisibleCount - MinVisible / 3f;

        public void FollowLive() => RightOffset = 0f;

        /// <summary>Keeps a scrolled-back view anchored on the same candles while new ones arrive.</summary>
        public void SeriesGrew(int added)
        {
            if (!IsLive) RightOffset += added;
        }

        public void VisibleRange(int total, out int first, out int count)
        {
            int end = Math.Clamp(total - (int)Math.Round(RightOffset), Math.Min(total, 1), total);
            // Empty space on the right takes slots from the view, so the newest candle sits further left.
            first = Math.Max(0, end - (VisibleCount - BlankSlots));
            count = end - first;
        }

        /// <summary>1/2/5 × 10ⁿ step giving roughly <paramref name="targetTicks"/> gridlines over the range.</summary>
        public static double NiceStep(double range, int targetTicks)
        {
            if (range <= 0) return 1;
            double raw = range / Math.Max(1, targetTicks);
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double normalized = raw / magnitude;
            double nice = normalized < 1.5 ? 1 : normalized < 3 ? 2 : normalized < 7 ? 5 : 10;
            return nice * magnitude;
        }

        /// <summary>
        /// Session VWAP at each visible candle, reset at each trading day's first candle. Sums start from the
        /// day's first candle even when it is off-screen, so the line matches the quote panel's VWAP.
        /// </summary>
        public static void Vwap(CandleSeries series, int first, int count, double[] output)
        {
            if (count <= 0) return;
            DateTime day = series[first].Start.Date;
            int start = first;
            while (start > 0 && series[start - 1].Start.Date == day) start--;

            decimal notional = 0m;
            long volume = 0;
            for (int i = start; i < first + count; i++)
            {
                Candle c = series[i];
                if (c.Start.Date != day)
                {
                    day = c.Start.Date;
                    notional = 0m;
                    volume = 0;
                }
                notional += c.Notional;
                volume += c.Volume;
                if (i >= first) output[i - first] = volume > 0 ? (double)(notional / volume) : double.NaN;
            }
        }

        /// <summary>First candle whose bucket starts at or after the bucket containing <paramref name="time"/>, or -1.</summary>
        public static int FirstCandleAtOrAfter(CandleSeries series, Timeframe timeframe, DateTime time)
        {
            DateTime bucket = CandleAggregator.BucketStart(time, timeframe);
            int lo = 0, hi = series.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (series[mid].Start < bucket) lo = mid + 1;
                else hi = mid;
            }
            return lo < series.Count ? lo : -1;
        }

        /// <summary>Index of the candle whose bucket contains <paramref name="time"/>, or -1.</summary>
        public static int FindCandle(CandleSeries series, Timeframe timeframe, DateTime time)
        {
            DateTime bucket = CandleAggregator.BucketStart(time, timeframe);
            int lo = 0, hi = series.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                DateTime start = series[mid].Start;
                if (start == bucket) return mid;
                if (start < bucket) lo = mid + 1;
                else hi = mid - 1;
            }
            return -1;
        }
    }
}
