using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Market;

namespace OpeningBell.Tests
{
    public class MarketSimulationTests
    {
        private static readonly DateTime Monday = TestMarkets.Monday;
        /// <summary>Independent markets averaged by the statistical tests below.</summary>
        private const int Seeds = 6;

        [Test]
        public void InitialState_IsValidBeforeAnyTick()
        {
            var sim = TestMarkets.Create(1, Monday.AddHours(6), TestMarkets.Basic());
            Assert.AreEqual(MarketSession.Premarket, sim.Session);
            foreach (var sec in sim.Securities)
            {
                Assert.AreEqual(PriceTick.RoundNearest((decimal)sec.Spec.BasePrice), sec.Last);
                Assert.AreEqual(sec.Last, sec.PreviousClose);
                Assert.Less(sec.Bid, sec.Ask);
            }
        }

        [Test]
        public void FullDay_RunsHeadlessThroughEverySession()
        {
            var sim = TestMarkets.Create(1, Monday.AddHours(3), TestMarkets.Basic());
            var sessions = new List<MarketSession>();
            sim.SessionChanged += (_, current) => sessions.Add(current);

            string firstProblem = null;
            int ticks = 0;
            sim.Ticked += () =>
            {
                ticks++;
                if (firstProblem != null) return;
                foreach (var sec in sim.Securities)
                {
                    Quote q = sec.Quote;
                    if (!(q.Bid > 0 && q.Ask > q.Bid && PriceTick.IsOnGrid(q.Bid) && PriceTick.IsOnGrid(q.Ask) &&
                          q.BidSize >= 100 && q.AskSize >= 100))
                        firstProblem = $"{sec.Ticker} bad quote {q.Bid}/{q.Ask} x {q.BidSize}/{q.AskSize} at {sim.Now}";
                    else if (q.LastVolume > 0 && q.Last != q.Bid && q.Last != q.Ask)
                        firstProblem = $"{sec.Ticker} print {q.Last} not at bid/ask at {sim.Now}";
                }
            };

            sim.AdvanceTo(Monday.AddHours(21));

            Assert.IsNull(firstProblem, firstProblem);
            CollectionAssert.AreEqual(new[] { MarketSession.Premarket, MarketSession.Regular, MarketSession.AfterHours, MarketSession.Closed }, sessions);
            Assert.AreEqual(16 * 3600 / 2, ticks);
            Assert.AreEqual(Monday.AddHours(21), sim.Now);

            foreach (var sec in sim.Securities)
            {
                Assert.Greater(sec.DayVolume, 0);
                Assert.That(sec.DayLow, Is.LessThanOrEqualTo(sec.Last).And.GreaterThan(0m));
                Assert.That(sec.DayHigh, Is.GreaterThanOrEqualTo(sec.Last));

                // Every timeframe accounts for exactly the day's volume.
                foreach (Timeframe tf in new[] { Timeframe.Minute1, Timeframe.Minute5, Timeframe.Minute15, Timeframe.Hour1 })
                    Assert.AreEqual(sec.DayVolume, SumVolume(sec.Candles.Get(tf)), $"{sec.Ticker} {tf}");

                // Day VWAP equals the candle-derived VWAP and sits inside the day's range.
                CandleSeries m1 = sec.Candles.Get(Timeframe.Minute1);
                decimal notional = 0m;
                for (int i = 0; i < m1.Count; i++) notional += m1[i].Notional;
                Assert.AreEqual(notional / sec.DayVolume, sec.Vwap);
                Assert.That(sec.Vwap, Is.InRange(sec.DayLow, sec.DayHigh));

                CandleSeries daily = sec.Candles.Get(Timeframe.Day1);
                Assert.AreEqual(1, daily.Count);
                Assert.AreEqual(Monday, daily[0].Start);
                Assert.Less(daily[0].Volume, sec.DayVolume, "daily candle excludes extended hours");
            }

            // The index prints every tick: 16 hours of one-minute candles.
            Assert.AreEqual(16 * 60, sim.Index.Candles.Get(Timeframe.Minute1).Count);
        }

        [Test]
        public void SameSeed_IsDeterministic_RegardlessOfHowTimeIsChunked()
        {
            DateTime start = Monday.AddHours(3), end = Monday.AddHours(17);
            var a = TestMarkets.Create(42, start, TestMarkets.Basic());
            var b = TestMarkets.Create(42, start, TestMarkets.Basic());

            a.AdvanceTo(end);
            for (DateTime t = start; t < end;)
            {
                t = t.AddSeconds(37.3);
                b.AdvanceTo(t < end ? t : end);
            }

            Assert.AreEqual(a.TickCount, b.TickCount);
            Assert.AreEqual(a.Index.Level, b.Index.Level);
            for (int i = 0; i < a.Securities.Count; i++)
            {
                Assert.AreEqual(a.Securities[i].Quote.Last, b.Securities[i].Quote.Last);
                Assert.AreEqual(a.Securities[i].Quote.Bid, b.Securities[i].Quote.Bid);
                Assert.AreEqual(a.Securities[i].DayVolume, b.Securities[i].DayVolume);
            }
        }

        [Test]
        public void DifferentSeeds_Diverge()
        {
            var a = TestMarkets.Create(1, Monday.AddHours(9), TestMarkets.Basic());
            var b = TestMarkets.Create(2, Monday.AddHours(9), TestMarkets.Basic());
            a.AdvanceTo(Monday.AddHours(12));
            b.AdvanceTo(Monday.AddHours(12));
            Assert.AreNotEqual(a.Securities[0].Last, b.Securities[0].Last);
        }

        [Test]
        public void AddingASecurity_DoesNotChangeOtherSecurities()
        {
            var specs = TestMarkets.Basic();
            var small = TestMarkets.Create(5, Monday.AddHours(3), specs.Take(2).ToArray());
            var full = TestMarkets.Create(5, Monday.AddHours(3), specs);
            small.AdvanceTo(Monday.AddHours(18));
            full.AdvanceTo(Monday.AddHours(18));

            for (int i = 0; i < 2; i++)
            {
                Assert.AreEqual(small.Securities[i].Last, full.Securities[i].Last);
                Assert.AreEqual(small.Securities[i].DayVolume, full.Securities[i].DayVolume);
            }
        }

        [Test]
        public void NewTradingDay_RollsPreviousClose_AndSkipsWeekends()
        {
            var sim = TestMarkets.Create(3, Monday.AddHours(3), TestMarkets.Basic());
            sim.AdvanceTo(Monday.AddDays(1).AddHours(10));

            Assert.AreEqual(Monday.AddDays(1), sim.TradingDate);
            foreach (var sec in sim.Securities)
            {
                CandleSeries daily = sec.Candles.Get(Timeframe.Day1);
                Assert.AreEqual(2, daily.Count);
                Assert.AreEqual(daily[0].Close, sec.PreviousClose, sec.Ticker);
            }
            Assert.AreEqual(sim.Index.Candles.Get(Timeframe.Day1)[0].Close, sim.Index.PreviousClose);

            sim.AdvanceTo(Monday.AddDays(4).AddHours(21));
            long fridayTicks = sim.TickCount;
            sim.AdvanceTo(Monday.AddDays(7).AddHours(3));
            Assert.AreEqual(fridayTicks, sim.TickCount, "no ticks over the weekend");
            sim.AdvanceTo(Monday.AddDays(7).AddHours(5));
            Assert.AreEqual(Monday.AddDays(7), sim.TradingDate);
        }

        [Test]
        public void FactorModel_ProducesMarketAndSectorCorrelation()
        {
            var specs = new[]
            {
                TestMarkets.Spec("TEC1", Sector.Technology, 40, vol: 0.01, beta: 1.2),
                TestMarkets.Spec("TEC2", Sector.Technology, 40, vol: 0.01, beta: 1.2),
                TestMarkets.Spec("ENR1", Sector.Energy, 40, vol: 0.01, beta: 1.0),
                TestMarkets.Spec("IND0", Sector.Healthcare, 40, vol: 0.01, beta: 0, sectorBeta: 0),
            };
            // One 4-week path swings these correlations by ±0.07, so a single seed passes or fails on luck.
            // The thresholds apply to the mean over several independent markets.
            double sameSector = 0, crossSector = 0, techToIndex = 0, zeroBetaToIndex = 0;
            for (ulong seed = 9; seed < 9 + Seeds; seed++)
            {
                var config = TestMarkets.FastConfig();
                config.MaxCandlesPerSeries = 20000;
                var sim = TestMarkets.Create(seed, Monday.AddHours(3), specs, config);
                sim.AdvanceTo(Monday.AddDays(28));

                var returns = specs.Select(s =>
                {
                    sim.TryGetSecurity(s.Ticker, out var sec);
                    return RegularReturns(sec.Candles.Get(Timeframe.Minute5), sim.Schedule);
                }).ToArray();
                var index = RegularReturns(sim.Index.Candles.Get(Timeframe.Minute5), sim.Schedule);

                sameSector += Correlation(returns[0], returns[1]) / Seeds;
                crossSector += Correlation(returns[0], returns[2]) / Seeds;
                techToIndex += Correlation(returns[0], index) / Seeds;
                zeroBetaToIndex += Correlation(returns[3], index) / Seeds;
            }
            TestContext.WriteLine($"same-sector {sameSector:F3}, cross-sector {crossSector:F3}, tech-index {techToIndex:F3}, zero-beta-index {zeroBetaToIndex:F3}");

            Assert.Greater(sameSector, crossSector + 0.1);
            Assert.Greater(crossSector, 0.15);
            Assert.Greater(techToIndex, 0.4);
            Assert.Less(Math.Abs(zeroBetaToIndex), 0.1);
        }

        [Test]
        public void IntradayVolume_IsUShaped()
        {
            var config = TestMarkets.FastConfig();
            config.MaxCandlesPerSeries = 20000;
            var sim = TestMarkets.Create(4, Monday.AddHours(3), TestMarkets.Basic(), config);
            sim.AdvanceTo(Monday.AddDays(5));

            sim.TryGetSecurity("AAA", out var sec);
            long open = 0, midday = 0, close = 0;
            foreach (Candle c in sec.Candles.Get(Timeframe.Minute1).Completed)
            {
                TimeSpan t = c.Start.TimeOfDay;
                if (t >= new TimeSpan(9, 30, 0) && t < new TimeSpan(10, 0, 0)) open += c.Volume;
                else if (t >= new TimeSpan(12, 0, 0) && t < new TimeSpan(12, 30, 0)) midday += c.Volume;
                else if (t >= new TimeSpan(15, 30, 0) && t < new TimeSpan(16, 0, 0)) close += c.Volume;
            }
            TestContext.WriteLine($"open {open}, midday {midday}, close {close}");

            Assert.Greater(open, midday * 2);
            Assert.Greater(close, midday * 3 / 2);
        }

        [Test]
        public void DailyVolatilityAndVolume_MatchSpec()
        {
            // 30 daily returns estimate σ to about ±13% (fat tails make it worse), so average several markets.
            var spec = TestMarkets.Spec("VOL", Sector.Healthcare, 50, vol: 0.03, beta: 0, sectorBeta: 0);
            double sd = 0, regularVolumeToAdv = 0;
            for (ulong seed = 6; seed < 6 + Seeds; seed++)
            {
                var sim = TestMarkets.Create(seed, Monday.AddHours(3), new[] { spec }, TestMarkets.FastConfig());
                sim.AdvanceTo(Monday.AddDays(42));

                CandleSeries daily = sim.Securities[0].Candles.Get(Timeframe.Day1);
                Assert.AreEqual(30, daily.Count);
                var logReturns = new List<double>();
                for (int i = 1; i < daily.Count; i++)
                    logReturns.Add(Math.Log((double)daily[i].Close / (double)daily[i - 1].Close));

                double mean = logReturns.Average();
                sd += Math.Sqrt(logReturns.Sum(r => (r - mean) * (r - mean)) / (logReturns.Count - 1)) / Seeds;
                regularVolumeToAdv += daily.Completed.Average(c => (double)c.Volume) / spec.AverageDailyVolume / Seeds;
            }
            TestContext.WriteLine($"{Seeds} markets, mean daily sd {sd:P2}; regular-session volume {regularVolumeToAdv:P0} of ADV");

            Assert.That(sd, Is.InRange(0.024, 0.037));
            Assert.That(regularVolumeToAdv, Is.InRange(0.75, 1.25));
        }

        [Test]
        public void ExtendedHoursSpreads_AreWiderThanRegular()
        {
            var sim = TestMarkets.Create(8, Monday.AddHours(3), TestMarkets.Basic());
            sim.TryGetSecurity("BBB", out var sec);
            decimal regularSum = 0m, afterSum = 0m;
            int regularN = 0, afterN = 0;
            sim.Ticked += () =>
            {
                if (sim.Session == MarketSession.Regular) { regularSum += sec.Quote.Spread; regularN++; }
                else if (sim.Session == MarketSession.AfterHours) { afterSum += sec.Quote.Spread; afterN++; }
            };
            sim.AdvanceTo(Monday.AddHours(21));

            decimal regular = regularSum / regularN, after = afterSum / afterN;
            TestContext.WriteLine($"avg spread regular {regular:F4}, after-hours {after:F4}");
            Assert.Greater(after, regular * 2m);
        }

        private static long SumVolume(CandleSeries series)
        {
            long total = 0;
            for (int i = 0; i < series.Count; i++) total += series[i].Volume;
            return total;
        }

        /// <summary>Log returns between consecutive 5-minute candles inside the same regular session, keyed by candle start.</summary>
        private static Dictionary<DateTime, double> RegularReturns(CandleSeries series, MarketSchedule schedule)
        {
            var result = new Dictionary<DateTime, double>();
            for (int i = 1; i < series.Completed.Count; i++)
            {
                Candle prev = series.Completed[i - 1], cur = series.Completed[i];
                if (cur.Start - prev.Start != TimeSpan.FromMinutes(5)) continue;
                if (schedule.GetSession(prev.Start) != MarketSession.Regular || schedule.GetSession(cur.Start) != MarketSession.Regular) continue;
                result[cur.Start] = Math.Log((double)cur.Close / (double)prev.Close);
            }
            return result;
        }

        private static double Correlation(Dictionary<DateTime, double> x, Dictionary<DateTime, double> y)
        {
            var keys = x.Keys.Where(y.ContainsKey).ToList();
            double mx = keys.Average(k => x[k]), my = keys.Average(k => y[k]);
            double cov = 0, vx = 0, vy = 0;
            foreach (var k in keys)
            {
                double dx = x[k] - mx, dy = y[k] - my;
                cov += dx * dy;
                vx += dx * dx;
                vy += dy * dy;
            }
            return cov / Math.Sqrt(vx * vy);
        }
    }
}
