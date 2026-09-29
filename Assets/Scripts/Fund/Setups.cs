using System;
using System.Collections.Generic;
using OpeningBell.Market;

namespace OpeningBell.Fund
{
    /// <summary>
    /// What anyone at a screen can see about one symbol this minute: price, VWAP, the day's range, averages, recent
    /// momentum, the opening range, the latest headline. Built only from quotes, completed candles and published news
    /// (never the flow model's hidden state or anything in the future), once per symbol per minute, shared by every trader.
    /// </summary>
    public sealed class Features
    {
        public string Ticker;
        public Sector Sector;
        public decimal PointValue, Tick;
        public double Last, Vwap, High, Low, PrevClose, DayOpen;
        /// <summary>Typical daily move as a fraction of price (from daily candles, else the listing's published volatility).</summary>
        public double DailyVol;
        /// <summary>Average 1-minute range (price units) and one "intraday unit" (daily move × price × 0.35).</summary>
        public double Atr1, Unit;
        public double Ema9, Ema21, Ema9x5, Ema21x5;
        /// <summary>Returns over the last 5 and 15 minutes in units of the expected move over that span.</summary>
        public double Z5, Z15;
        /// <summary>Volume of the last 5 minutes against the day's average pace.</summary>
        public double VolRatio;
        /// <summary>How directional the day has been: |last − open| / (high − low), 0 chop … 1 trend.</summary>
        public double Trendiness;
        public double OrHigh, OrLow;
        public bool OrReady;
        public double LastOpen, LastClose, LastHigh, LastLow, PrevBarHigh, PrevBarLow, PrevBarClose;
        public int MinutesIntoSession;
        /// <summary>The latest headline naming this symbol in the last 30 minutes (null if none), its age and the price then.</summary>
        public NewsItem News;
        public double NewsAge, PriceAtNews, HighSinceNews, LowSinceNews;
        public bool Valid;

        public static Features Build(MarketSimulation market, SecurityRuntimeState sec)
        {
            var f = new Features { Ticker = sec.Ticker, Sector = sec.Spec.Sector };
            f.PointValue = ContractSpec.For(sec.Spec).PointValue;
            f.Tick = PriceTick.For(sec.Last);
            CandleSeries m1 = sec.Candles.Get(Timeframe.Minute1);
            IReadOnlyList<Candle> bars = m1.Completed;
            if (bars.Count < 25 || sec.Last <= 0m) return f;
            DateTime now = market.Now;
            DateTime open = now.Date + market.Schedule.RegularOpen;
            f.MinutesIntoSession = (int)(now - open).TotalMinutes;
            f.Last = (double)sec.Last;
            f.Vwap = sec.Vwap > 0m ? (double)sec.Vwap : f.Last;
            f.High = (double)sec.DayHigh;
            f.Low = (double)sec.DayLow;
            f.PrevClose = (double)sec.PreviousClose;
            f.DailyVol = DailyVolatility(sec);
            f.Unit = f.DailyVol * f.Last * 0.35;

            int n = bars.Count;
            double rangeSum = 0;
            for (int i = n - 20; i < n; i++) rangeSum += (double)(bars[i].High - bars[i].Low);
            f.Atr1 = Math.Max(rangeSum / 20.0, (double)f.Tick);
            f.Ema9 = Ema(bars, 9, 40);
            f.Ema21 = Ema(bars, 21, 60);
            Candle last = bars[n - 1], prev = bars[n - 2];
            f.LastOpen = (double)last.Open; f.LastClose = (double)last.Close; f.LastHigh = (double)last.High; f.LastLow = (double)last.Low;
            f.PrevBarHigh = (double)prev.High; f.PrevBarLow = (double)prev.Low; f.PrevBarClose = (double)prev.Close;
            // Expected move over k minutes: the day's move spread over a 390-minute session, by √time.
            double perMinute = f.DailyVol * f.Last / Math.Sqrt(390.0);
            f.Z5 = ((double)last.Close - (double)bars[n - 6].Close) / (perMinute * Math.Sqrt(5));
            f.Z15 = ((double)last.Close - (double)bars[Math.Max(0, n - 16)].Close) / (perMinute * Math.Sqrt(15));

            IReadOnlyList<Candle> b5 = sec.Candles.Get(Timeframe.Minute5).Completed;
            if (b5.Count >= 25)
            {
                f.Ema9x5 = Ema(b5, 9, 40);
                f.Ema21x5 = Ema(b5, 21, 60);
            }
            else f.Ema9x5 = f.Ema21x5 = f.Last;

            // Today's regular-session bars: the opening range, the day's open and its volume pace.
            long dayVol = 0, recentVol = 0;
            int dayBars = 0;
            double orH = double.MinValue, orL = double.MaxValue;
            for (int i = n - 1; i >= 0 && bars[i].Start >= open; i--)
            {
                Candle c = bars[i];
                dayVol += c.Volume;
                dayBars++;
                if (i >= n - 5) recentVol += c.Volume;
                if (c.Start < open.AddMinutes(15))
                {
                    orH = Math.Max(orH, (double)c.High);
                    orL = Math.Min(orL, (double)c.Low);
                }
                f.DayOpen = (double)c.Open;
            }
            if (dayBars == 0) f.DayOpen = f.Last;
            f.OrReady = f.MinutesIntoSession >= 15 && orH > double.MinValue;
            f.OrHigh = orH;
            f.OrLow = orL;
            f.VolRatio = dayBars >= 6 && dayVol > 0 ? recentVol / 5.0 / (dayVol / (double)dayBars) : 1.0;
            double span = f.High - f.Low;
            f.Trendiness = span > 0 ? Math.Abs(f.Last - f.DayOpen) / span : 0;

            // The latest headline about it.
            IReadOnlyList<NewsItem> news = market.News;
            for (int i = news.Count - 1; i >= 0 && i >= news.Count - 40; i--)
            {
                NewsItem item = news[i];
                double age = (now - item.Time).TotalMinutes;
                if (age > 30) break;
                if (age < 0 || !item.Mentions(sec.Ticker)) continue;
                f.News = item;
                f.NewsAge = age;
                double hi = double.MinValue, lo = double.MaxValue, before = f.Last;
                for (int j = n - 1; j >= 0 && bars[j].Start >= item.Time.AddMinutes(-1); j--)
                {
                    hi = Math.Max(hi, (double)bars[j].High);
                    lo = Math.Min(lo, (double)bars[j].Low);
                    before = (double)bars[j].Open;
                }
                f.PriceAtNews = before;
                f.HighSinceNews = hi > double.MinValue ? hi : f.Last;
                f.LowSinceNews = lo < double.MaxValue ? lo : f.Last;
                break;
            }
            f.Valid = f.Unit > 0 && f.Atr1 > 0;
            return f;
        }

        private static double DailyVolatility(SecurityRuntimeState sec)
        {
            IReadOnlyList<Candle> d = sec.Candles.Get(Timeframe.Day1).Completed;
            if (d.Count >= 8)
            {
                int k = Math.Min(20, d.Count - 1);
                double sum = 0, sum2 = 0;
                for (int i = d.Count - k; i < d.Count; i++)
                {
                    double r = Math.Log((double)d[i].Close / (double)d[i - 1].Close);
                    sum += r;
                    sum2 += r * r;
                }
                double var = (sum2 - sum * sum / k) / Math.Max(1, k - 1);
                if (var > 0) return Math.Sqrt(var);
            }
            return sec.Spec.DailyVolatility;
        }

        private static double Ema(IReadOnlyList<Candle> bars, int period, int lookback)
        {
            int start = Math.Max(0, bars.Count - lookback);
            double k = 2.0 / (period + 1), e = (double)bars[start].Close;
            for (int i = start + 1; i < bars.Count; i++) e += k * ((double)bars[i].Close - e);
            return e;
        }
    }

    /// <summary>A trade idea: side, where it's wrong (stop), where it pays (target), and how good it looks (0–1).</summary>
    public struct Setup
    {
        public Strategy Strategy;
        public int Side;
        public double Entry, Stop, Target, Quality;
        /// <summary>How far price has already run past the ideal entry, in units of the risk (chasing).</summary>
        public double Late;
        /// <summary>Not one of the strategy's setups at all: a misread or an impulse (quality ~0).</summary>
        public bool Impulsive;
        public double Risk => Math.Abs(Entry - Stop);
        public double RewardRisk => Risk > 0 ? Math.Abs(Target - Entry) / Risk : 0;
    }

    /// <summary>
    /// The five strategies as setup detectors on <see cref="Features"/>. Quality blends the textbook ingredients of each
    /// (strength of the signal, volume, trend alignment, time of day, the day's character), so a sharper reader of the
    /// same features picks better trades; it knows nothing the chart doesn't show.
    /// </summary>
    public static class Setups
    {
        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        public static bool Find(Strategy s, Features f, out Setup setup)
        {
            setup = default;
            if (!f.Valid || f.MinutesIntoSession < 3 || f.MinutesIntoSession > 380) return false;
            switch (s)
            {
                case Strategy.Breakout: return Breakout(f, out setup);
                case Strategy.Reversion: return Reversion(f, out setup);
                case Strategy.TrendPullback: return Pullback(f, out setup);
                case Strategy.NewsMomentum: return NewsMove(f, out setup);
                default: return Scalp(f, out setup);
            }
        }

        /// <summary>
        /// A setup that isn't there: the last bar's direction read as a signal, stop and target shaped like the trader's
        /// own play. True quality is zero; only a weak reader takes it.
        /// </summary>
        public static bool Misread(Strategy s, Features f, out Setup setup)
        {
            setup = default;
            if (!f.Valid || f.MinutesIntoSession < 10 || f.MinutesIntoSession > 360) return false;
            int side = f.LastClose > f.LastOpen ? 1 : f.LastClose < f.LastOpen ? -1 : 0;
            if (side == 0) return false;
            double risk = 1.2 * f.Atr1, r = s == Strategy.Scalping ? 1.3 : s == Strategy.Reversion ? 1.5 : 2.0;
            setup = new Setup { Strategy = s, Side = side, Entry = f.Last, Stop = f.Last - side * risk, Target = f.Last + side * r * risk, Quality = 0.05, Impulsive = true };
            return true;
        }

        /// <summary>Jumping on a move already under way: in its direction, a tight stop, hoping for more.</summary>
        public static bool Chase(Features f, out Setup setup)
        {
            setup = default;
            if (!f.Valid || f.MinutesIntoSession < 5 || f.MinutesIntoSession > 360) return false;
            int side = f.Z5 > 0 ? 1 : -1;
            double risk = 1.0 * f.Atr1;
            setup = new Setup { Strategy = Strategy.Scalping, Side = side, Entry = f.Last, Stop = f.Last - side * risk, Target = f.Last + side * 2.0 * risk, Quality = 0.05, Impulsive = true };
            return true;
        }

        /// <summary>A close through the 15-minute opening range with volume, in the direction of the 5-minute trend.</summary>
        private static bool Breakout(Features f, out Setup s)
        {
            s = default;
            if (!f.OrReady || f.MinutesIntoSession > 240) return false;
            int side = f.LastClose > f.OrHigh && f.PrevBarClose <= f.OrHigh ? 1 : f.LastClose < f.OrLow && f.PrevBarClose >= f.OrLow ? -1 : 0;
            if (side == 0) return false;
            double level = side > 0 ? f.OrHigh : f.OrLow;
            double orSize = f.OrHigh - f.OrLow;
            double stop = side > 0 ? Math.Max(f.OrLow, level - Math.Max(0.5 * orSize, 1.2 * f.Atr1)) : Math.Min(f.OrHigh, level + Math.Max(0.5 * orSize, 1.2 * f.Atr1));
            double risk = Math.Abs(f.Last - stop);
            if (risk <= 0) return false;
            // What separates the breaks that hold from the fakeouts (measured in SetupEdge): the first hour's breaks
            // mostly fail; a break that arrives already stretched (a 15-minute sprint, far above VWAP or the open) has
            // spent its move; a quiet, coiled approach holds.
            double run15 = side * f.Z15, fromVwap = side * (f.Last - f.Vwap) / f.Unit, fromOpen = side * (f.Last - f.DayOpen) / f.Unit;
            s = new Setup
            {
                Strategy = Strategy.Breakout, Side = side, Entry = f.Last, Stop = stop, Target = f.Last + side * 2.0 * risk,
                Quality = Clamp01(0.15 + 0.3 * Clamp01((f.MinutesIntoSession - 35) / 40.0) + 0.2 * (1 - Clamp01((run15 - 0.9) / 1.2))
                                  + 0.2 * (1 - Clamp01((fromVwap - 0.3) / 0.9)) + 0.15 * (1 - Clamp01((fromOpen - 0.3) / 1.0))),
                Late = Math.Max(0, (f.Last - level) * side / risk - 0.3),
            };
            return true;
        }

        /// <summary>Stretched far from VWAP, the last bar turning back: fade toward VWAP. Better on choppy days.</summary>
        private static bool Reversion(Features f, out Setup s)
        {
            s = default;
            if (f.MinutesIntoSession < 20) return false;
            double stretch = (f.Last - f.Vwap) / f.Unit;
            if (Math.Abs(stretch) < 1.1) return false;
            int side = stretch > 0 ? -1 : 1;
            bool turned = side < 0 ? f.LastClose < f.LastOpen && f.LastClose < f.PrevBarClose : f.LastClose > f.LastOpen && f.LastClose > f.PrevBarClose;
            if (!turned) return false;
            // Stop beyond the extreme with room for a retest; aim for half the way back to VWAP, no more than 2R: a
            // fade is a high-hit-rate, modest-payoff trade, not a lottery ticket on the full round trip.
            double extreme = side < 0 ? Math.Max(f.LastHigh, f.PrevBarHigh) : Math.Min(f.LastLow, f.PrevBarLow);
            double stop = extreme - side * 1.0 * f.Atr1;
            double risk = Math.Abs(f.Last - stop);
            if (risk <= 0) return false;
            double target = f.Last + side * Math.Min(Math.Abs(f.Vwap - f.Last) * 0.5, 2.0 * risk);
            if (Math.Abs(target - f.Last) < 0.8 * risk) return false;
            // The fades that work: a climactic spike (a sharp 15-minute run into the extreme) on a day that has been
            // two-sided, early or late in the session rather than in the midday drift; never against fresh news.
            double spike = -side * f.Z15;
            int m = f.MinutesIntoSession;
            s = new Setup
            {
                Strategy = Strategy.Reversion, Side = side, Entry = f.Last, Stop = stop, Target = target,
                Quality = Clamp01(0.15 + 0.3 * Clamp01((spike - 0.8) / 1.5) + 0.25 * Clamp01(1 - f.Trendiness * 1.2)
                                  + 0.15 * (m < 100 || m > 320 ? 1 : 0) + 0.1 * Clamp01((Math.Abs(stretch) - 1.1) / 1.5) - (f.News != null ? 0.25 : 0)),
                Late = 0,
            };
            return true;
        }

        /// <summary>5-minute trend, a 1-minute pullback to the averages, then the first bar back with the trend.</summary>
        private static bool Pullback(Features f, out Setup s)
        {
            s = default;
            if (f.MinutesIntoSession < 25) return false;
            double trend = (f.Ema9x5 - f.Ema21x5) / f.Unit;
            int side = trend > 0.08 && f.Last > f.Vwap ? 1 : trend < -0.08 && f.Last < f.Vwap ? -1 : 0;
            if (side == 0) return false;
            bool touched = side > 0 ? f.PrevBarLow <= f.Ema21 + 0.3 * f.Atr1 : f.PrevBarHigh >= f.Ema21 - 0.3 * f.Atr1;
            bool resumed = side > 0 ? f.LastClose > f.PrevBarHigh : f.LastClose < f.PrevBarLow;
            if (!touched || !resumed) return false;
            double stop = side > 0 ? Math.Min(f.PrevBarLow, f.LastLow) - 0.4 * f.Atr1 : Math.Max(f.PrevBarHigh, f.LastHigh) + 0.4 * f.Atr1;
            double risk = Math.Abs(f.Last - stop);
            if (risk <= 0) return false;
            s = new Setup
            {
                Strategy = Strategy.TrendPullback, Side = side, Entry = f.Last, Stop = stop, Target = f.Last + side * 2.2 * risk,
                // A trend that has proven itself: later in the day, a directional day, price well on its side of VWAP;
                // an over-steep 5-minute slope is a blow-off more often than a trend.
                Quality = Clamp01(0.15 + 0.3 * Clamp01((f.MinutesIntoSession - 50) / 130.0) + 0.2 * Clamp01((f.Trendiness - 0.3) / 0.5)
                                  + 0.2 * Clamp01((side * (f.Last - f.Vwap) / f.Unit - 0.3) / 1.0) + 0.15 * (1 - Clamp01((Math.Abs(trend) - 0.35) / 0.4))),
                Late = Math.Max(0, (f.Last - (side > 0 ? f.PrevBarHigh : f.PrevBarLow)) * side / risk - 0.5),
            };
            return true;
        }

        /// <summary>A fresh headline moved it; after a pause, the break of the post-news extreme in the same direction.</summary>
        private static bool NewsMove(Features f, out Setup s)
        {
            s = default;
            if (f.News == null || f.NewsAge < 3 || f.NewsAge > 25) return false;
            double move = (f.Last - f.PriceAtNews) / f.Unit;
            if (Math.Abs(move) < 0.5) return false;
            int side = move > 0 ? 1 : -1;
            bool breaking = side > 0 ? f.LastClose >= f.HighSinceNews - 0.2 * f.Atr1 && f.LastClose > f.PrevBarHigh
                                     : f.LastClose <= f.LowSinceNews + 0.2 * f.Atr1 && f.LastClose < f.PrevBarLow;
            if (!breaking) return false;
            double stop = f.Last - side * Math.Max(1.5 * f.Atr1, 0.35 * Math.Abs(f.Last - f.PriceAtNews));
            double risk = Math.Abs(f.Last - stop);
            if (risk <= 0) return false;
            s = new Setup
            {
                Strategy = Strategy.NewsMomentum, Side = side, Entry = f.Last, Stop = stop, Target = f.Last + side * 2.0 * risk,
                // The textbook rule held up in measurement: don't chase the first spike. A measured move that has paused
                // and is breaking again some minutes after the headline carries; a huge, fresh spike has usually spent it.
                Quality = Clamp01(0.2 + 0.35 * (1 - Clamp01((Math.Abs(move) - 0.8) / 1.7)) + 0.25 * Clamp01((f.NewsAge - 4) / 12.0)
                                  + 0.1 * Clamp01((f.VolRatio - 1) / 1.5) + (f.News.IsMajor ? 0.1 : 0)),
                Late = Math.Max(0, Math.Abs(move) - 2.5) * 0.5,
            };
            return true;
        }

        /// <summary>A sharp one-to-five-minute burst on volume: ride it for a quick 1.2R.</summary>
        private static bool Scalp(Features f, out Setup s)
        {
            s = default;
            if (Math.Abs(f.Z5) < 1.3) return false;
            int side = f.Z5 > 0 ? 1 : -1;
            bool strongBar = (f.LastClose - f.LastOpen) * side > 0.5 * f.Atr1;
            if (!strongBar) return false;
            double stop = f.Last - side * 1.1 * f.Atr1;
            double risk = Math.Abs(f.Last - stop);
            s = new Setup
            {
                Strategy = Strategy.Scalping, Side = side, Entry = f.Last, Stop = stop, Target = f.Last + side * 1.3 * risk,
                // The bursts that carry: toward value (from the far side of VWAP) rather than away from it, not already
                // stretched from the open, not a blow-off bar, and not in the opening minutes' noise.
                Quality = Clamp01(0.15 + 0.3 * Clamp01((0.6 - side * (f.Last - f.Vwap) / f.Unit) / 1.2) + 0.2 * Clamp01((0.8 - side * (f.Last - f.DayOpen) / f.Unit) / 1.5)
                                  + 0.2 * Clamp01((2.6 - Math.Abs(f.Z5)) / 1.3) + 0.15 * (f.MinutesIntoSession >= 15 ? 1 : 0)),
                Late = Math.Max(0, Math.Abs(f.Z5) - 3.2) * 0.4,
            };
            return true;
        }
    }
}
