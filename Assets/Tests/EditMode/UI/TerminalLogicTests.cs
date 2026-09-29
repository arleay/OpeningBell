using System;
using NUnit.Framework;
using OpeningBell.Market;
using OpeningBell.UI;

namespace OpeningBell.Tests
{
    public class FormattingAndInputTests
    {
        [Test]
        public void Fmt_UsesStableInvariantFormats()
        {
            Assert.AreEqual("23.50", Fmt.Price(23.5m));
            Assert.AreEqual("0.1234", Fmt.Price(0.1234m));
            Assert.AreEqual("4,803.89", Fmt.Price(4803.89m));
            Assert.AreEqual("-$240.00", Fmt.Money(-240m));
            Assert.AreEqual("+$160.00", Fmt.SignedMoney(160m));
            Assert.AreEqual("-0.71", Fmt.PriceDelta(-0.71m, 23.04m));
            Assert.AreEqual("0.0012", Fmt.PriceDelta(0.0012m, 0.85m, signed: false));
            Assert.AreEqual("-1.23%", Fmt.Percent(-1.234m));
            Assert.AreEqual("1.23M", Fmt.Volume(1_234_567));
            Assert.AreEqual("845.2K", Fmt.Volume(845_200));
            Assert.AreEqual("9,000", Fmt.Volume(9_000));
        }

        [TestCase("1,000", 1000)]
        [TestCase(" 250 ", 250)]
        public void Quantity_ParsesFriendlyInput(string text, long expected)
        {
            Assert.IsTrue(TicketInput.TryParseQuantity(text, out long q));
            Assert.AreEqual(expected, q);
        }

        [TestCase("0")]
        [TestCase("-5")]
        [TestCase("1.5")]
        [TestCase("abc")]
        [TestCase("")]
        public void Quantity_RejectsInvalidInput(string text) => Assert.IsFalse(TicketInput.TryParseQuantity(text, out _));

        [TestCase("12a", ",", "12")]
        [TestCase("1,0x00", ",", "1,000")]
        [TestCase("abc", ",", "")]
        [TestCase("-5.5", ",", "55")]
        [TestCase("$12.5q0", ".,$", "$12.50")]
        public void Restricted_Fields_KeepOnlyDigitsAndAllowedMarks(string typed, string allowed, string kept) =>
            Assert.AreEqual(kept, TicketInput.Keep(typed, allowed));

        [Test]
        public void Price_ParsesDollarsAndCommas_RejectsNonPositive()
        {
            Assert.IsTrue(TicketInput.TryParsePrice("$23.45", out decimal a));
            Assert.AreEqual(23.45m, a);
            Assert.IsTrue(TicketInput.TryParsePrice("1,234.5", out decimal b));
            Assert.AreEqual(1234.5m, b);
            Assert.IsFalse(TicketInput.TryParsePrice("-1", out _));
            Assert.IsFalse(TicketInput.TryParsePrice("0", out _));
        }
    }

    public class ChartViewportTests
    {
        [Test]
        public void LiveView_ShowsNewestCandles()
        {
            var v = new ChartViewport(90);
            v.VisibleRange(500, out int first, out int count);
            Assert.AreEqual(410, first);
            Assert.AreEqual(90, count);

            v.VisibleRange(30, out first, out count);
            Assert.AreEqual(0, first);
            Assert.AreEqual(30, count);
        }

        [Test]
        public void PannedView_StaysAnchoredWhileDataArrives()
        {
            var v = new ChartViewport(90);
            v.Pan(100, 500);
            v.VisibleRange(500, out int first, out _);
            Assert.AreEqual(310, first);

            v.SeriesGrew(10);
            v.VisibleRange(510, out int after, out _);
            Assert.AreEqual(310, after);

            v.FollowLive();
            v.SeriesGrew(10);
            Assert.IsTrue(v.IsLive);
        }

        [Test]
        public void ZoomAndPan_AreClamped()
        {
            var v = new ChartViewport(90);
            v.Zoom(100f);
            Assert.AreEqual(ChartViewport.MaxVisible, v.VisibleCount);
            v.Zoom(0.0001f);
            Assert.AreEqual(ChartViewport.MinVisible, v.VisibleCount);

            // Dragged left past the newest candle: empty space on the right, but a few candles always stay in view.
            v.Pan(-50, 500);
            Assert.Less(v.RightOffset, 0f);
            v.VisibleRange(500, out int first, out int count);
            Assert.AreEqual(500, first + count, "the newest candle is still shown");
            Assert.GreaterOrEqual(count, 1);
            Assert.AreEqual(v.VisibleCount, count + v.BlankSlots, "candles plus empty space fill the view");
            v.FollowLive();
            v.Pan(10_000, 500);
            Assert.AreEqual(500 - ChartViewport.MinVisible, v.RightOffset);
        }

        [TestCase(10.0, 5, 2.0)]
        [TestCase(0.37, 6, 0.05)]
        [TestCase(96.0, 6, 20.0)]
        public void NiceStep_Picks125Steps(double range, int ticks, double expected) =>
            Assert.AreEqual(expected, ChartViewport.NiceStep(range, ticks), 1e-9);

        [Test]
        public void Vwap_IncludesOffscreenCandlesOfTheDay_AndResetsDaily()
        {
            var agg = new CandleAggregator(100);
            DateTime day1 = TestMarkets.Monday.AddHours(9.5);
            agg.Record(day1, 10m, 100, true);
            agg.Record(day1.AddMinutes(1), 12m, 100, true);
            agg.Record(day1.AddDays(1), 20m, 50, true);
            CandleSeries m1 = agg.Get(Timeframe.Minute1);

            var output = new double[2];
            ChartViewport.Vwap(m1, first: 1, count: 2, output);

            Assert.AreEqual(11.0, output[0], 1e-9, "day 1 VWAP includes the off-screen first candle");
            Assert.AreEqual(20.0, output[1], 1e-9, "resets on a new day");
        }

        [Test]
        public void FindCandle_LocatesBucketOrReturnsMinusOne()
        {
            var agg = new CandleAggregator(100);
            DateTime open = TestMarkets.Monday.AddHours(9.5);
            for (int i = 0; i < 10; i++) agg.Record(open.AddMinutes(i * 2), 10m, 1, true);
            CandleSeries m1 = agg.Get(Timeframe.Minute1);

            Assert.AreEqual(3, ChartViewport.FindCandle(m1, Timeframe.Minute1, open.AddMinutes(6).AddSeconds(30)));
            Assert.AreEqual(-1, ChartViewport.FindCandle(m1, Timeframe.Minute1, open.AddMinutes(1)));
        }
    }
}
