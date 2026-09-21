using System;
using NUnit.Framework;
using OpeningBell.Market;

namespace OpeningBell.Tests
{
    public class MarketScheduleTests
    {
        private static readonly MarketSchedule Schedule = new MarketSchedule(new MarketConfig());
        private static readonly DateTime Monday = TestMarkets.Monday;

        [TestCase("03:59:59", MarketSession.Closed)]
        [TestCase("04:00:00", MarketSession.Premarket)]
        [TestCase("09:29:59", MarketSession.Premarket)]
        [TestCase("09:30:00", MarketSession.Regular)]
        [TestCase("15:59:59", MarketSession.Regular)]
        [TestCase("16:00:00", MarketSession.AfterHours)]
        [TestCase("19:59:59", MarketSession.AfterHours)]
        [TestCase("20:00:00", MarketSession.Closed)]
        public void SessionBoundaries(string time, MarketSession expected)
        {
            Assert.AreEqual(expected, Schedule.GetSession(Monday + TimeSpan.Parse(time)));
        }

        [Test]
        public void Weekend_IsClosed_AndNextStartIsMondayPremarket()
        {
            DateTime friday = Monday.AddDays(4);
            Assert.AreEqual(MarketSession.Closed, Schedule.GetSession(friday.AddDays(1).AddHours(12)));
            Assert.AreEqual(Monday.AddDays(7).AddHours(4), Schedule.NextSessionStart(friday.AddHours(20)));
            Assert.AreEqual(Monday.AddDays(1).AddHours(4), Schedule.NextSessionStart(Monday.AddDays(1).AddHours(2)));
        }
    }

    public class CandleAggregatorTests
    {
        private static readonly DateTime Open = TestMarkets.Monday.AddHours(9.5);

        [Test]
        public void Ticks_AggregateIntoOhlcv()
        {
            var agg = new CandleAggregator(100);
            agg.Record(Open, 10.00m, 100, true);
            agg.Record(Open.AddSeconds(20), 10.50m, 50, true);
            agg.Record(Open.AddSeconds(40), 9.80m, 25, true);
            agg.Record(Open.AddSeconds(60), 10.10m, 10, true);

            CandleSeries m1 = agg.Get(Timeframe.Minute1);
            Assert.AreEqual(2, m1.Count);
            AssertCandle(m1[0], Open, 10.00m, 10.50m, 9.80m, 9.80m, 175);
            AssertCandle(m1[1], Open.AddMinutes(1), 10.10m, 10.10m, 10.10m, 10.10m, 10);

            CandleSeries m5 = agg.Get(Timeframe.Minute5);
            Assert.AreEqual(1, m5.Count);
            AssertCandle(m5[0], Open, 10.00m, 10.50m, 9.80m, 10.10m, 185);

            Assert.AreEqual(1, agg.Get(Timeframe.Day1).Count);
            Assert.AreEqual(Open.Date, agg.Get(Timeframe.Day1)[0].Start);
        }

        [Test]
        public void DailyCandles_IgnoreExtendedHours()
        {
            var agg = new CandleAggregator(100);
            agg.Record(Open.AddHours(-1), 5m, 10, regularSession: false);
            Assert.AreEqual(1, agg.Get(Timeframe.Minute1).Count);
            Assert.AreEqual(0, agg.Get(Timeframe.Day1).Count);
        }

        [Test]
        public void BucketStart_AlignsToClock()
        {
            var t = TestMarkets.Monday + new TimeSpan(10, 7, 31);
            Assert.AreEqual(TestMarkets.Monday + new TimeSpan(10, 7, 0), CandleAggregator.BucketStart(t, Timeframe.Minute1));
            Assert.AreEqual(TestMarkets.Monday + new TimeSpan(10, 5, 0), CandleAggregator.BucketStart(t, Timeframe.Minute5));
            Assert.AreEqual(TestMarkets.Monday + new TimeSpan(10, 0, 0), CandleAggregator.BucketStart(t, Timeframe.Minute15));
            Assert.AreEqual(TestMarkets.Monday + new TimeSpan(10, 0, 0), CandleAggregator.BucketStart(t, Timeframe.Hour1));
            Assert.AreEqual(TestMarkets.Monday, CandleAggregator.BucketStart(t, Timeframe.Day1));
        }

        [Test]
        public void History_IsTrimmedToCapacity_KeepingNewest()
        {
            var agg = new CandleAggregator(10);
            for (int i = 0; i < 40; i++) agg.Record(Open.AddMinutes(i), 10m + i, 1, true);

            CandleSeries m1 = agg.Get(Timeframe.Minute1);
            Assert.That(m1.Completed.Count, Is.InRange(10, 12));
            Assert.AreEqual(Open.AddMinutes(39), m1[m1.Count - 1].Start);
            Assert.AreEqual(Open.AddMinutes(38), m1.Completed[m1.Completed.Count - 1].Start);
        }

        [Test]
        public void OutOfOrderData_Throws()
        {
            var agg = new CandleAggregator(10);
            agg.Record(Open.AddMinutes(5), 10m, 1, true);
            Assert.Throws<InvalidOperationException>(() => agg.Record(Open, 10m, 1, true));
        }

        private static void AssertCandle(Candle c, DateTime start, decimal o, decimal h, decimal l, decimal close, long v)
        {
            Assert.AreEqual(start, c.Start, "start");
            Assert.AreEqual(o, c.Open, "open");
            Assert.AreEqual(h, c.High, "high");
            Assert.AreEqual(l, c.Low, "low");
            Assert.AreEqual(close, c.Close, "close");
            Assert.AreEqual(v, c.Volume, "volume");
        }
    }
}
