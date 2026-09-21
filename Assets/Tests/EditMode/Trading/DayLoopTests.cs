using System;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Core;
using OpeningBell.Gameplay;
using OpeningBell.Market;
using OpeningBell.Trading;

namespace OpeningBell.Tests
{
    public class DayLoopTests
    {
        private static readonly DateTime Monday = TestMarkets.Monday;
        private static readonly MarketSchedule Schedule = new MarketSchedule(new MarketConfig());
        private static readonly TimeSpan Bedtime = TimeSpan.FromHours(16), Wake = TimeSpan.FromHours(6);

        [Test]
        public void Clock_JumpsForwardOnly()
        {
            var clock = new GameClock(Monday.AddHours(10), 10);
            clock.JumpTo(Monday.AddHours(9));
            Assert.AreEqual(Monday.AddHours(10), clock.Now);
            clock.JumpTo(Monday.AddDays(1));
            Assert.AreEqual(Monday.AddDays(1), clock.Now);
        }

        [TestCase(10, false)]
        [TestCase(15, false)]
        [TestCase(16, true)]
        [TestCase(23, true)]
        [TestCase(2, true)]
        [TestCase(6, false)]
        public void SleepWindow_IsEveningToMorning(int hour, bool canSleep) =>
            Assert.AreEqual(canSleep, SleepRules.CanSleep(Monday.AddHours(hour), Bedtime, Wake));

        [Test]
        public void Waking_IsNextTradingMorning_SkippingWeekends()
        {
            Assert.AreEqual(Monday.AddDays(1).AddHours(6), SleepRules.NextWake(Monday.AddHours(17), Wake, Schedule));
            Assert.AreEqual(Monday.AddDays(1).AddHours(6), SleepRules.NextWake(Monday.AddDays(1).AddHours(2), Wake, Schedule), "after midnight → same morning");
            Assert.AreEqual(Monday.AddDays(7).AddHours(6), SleepRules.NextWake(Monday.AddDays(4).AddHours(21), Wake, Schedule), "Friday night → Monday");
        }

        [Test]
        public void Recorder_ReportsEachTradingDay_AndReconcilesAcrossDays()
        {
            DateTime thursday = Monday.AddDays(3);
            var sim = TestMarkets.Create(21, thursday.AddHours(6), TestMarkets.Basic());
            var account = new Account(sim);
            account.Deposit(10_000m);
            var orders = new OrderManager(sim, account, new BrokerRules());
            var days = new TradingDayRecorder(sim, account, orders);
            int completedEvents = 0;
            days.DayCompleted += _ => completedEvents++;
            Assert.AreEqual(1, days.DayNumber);

            // Thursday: round trip. Friday: buy and hold over the weekend. Monday: sell.
            sim.AdvanceTo(thursday.AddHours(10));
            orders.SubmitMarket("AAA", OrderSide.Buy, 100);
            sim.AdvanceTo(thursday.AddHours(11));
            orders.SubmitMarket("AAA", OrderSide.Sell, 100);
            sim.AdvanceTo(thursday.AddDays(1).AddHours(10));
            orders.SubmitMarket("BBB", OrderSide.Buy, 50);
            sim.AdvanceTo(Monday.AddDays(7).AddHours(10));
            orders.SubmitMarket("BBB", OrderSide.Sell, 50);
            sim.AdvanceTo(Monday.AddDays(7).AddHours(21));

            var reports = days.Completed;
            CollectionAssert.AreEqual(new[] { thursday, thursday.AddDays(1), Monday.AddDays(7) }, reports.Select(r => r.Date));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, reports.Select(r => r.DayNumber));
            CollectionAssert.AreEqual(new[] { 2, 1, 1 }, reports.Select(r => r.Fills));
            Assert.IsTrue(reports.All(r => r.IsComplete));
            Assert.AreEqual(3, completedEvents);
            Assert.IsNull(days.Current, "market closed between days");

            Assert.AreEqual(1, reports[0].Winners + reports[0].Losers, "Thursday's round trip closed one trade");
            Assert.AreEqual(0, reports[1].Winners + reports[1].Losers, "Friday only opened a position");
            Assert.AreEqual(account.TotalCommissions, reports.Sum(r => r.Commissions));
            Assert.AreEqual(account.RealizedPnL, reports.Sum(r => r.RealizedPnL));

            // Marks don't move while the market is closed, so each day starts where the last one ended
            // and the daily results add up exactly to the account's change.
            for (int i = 1; i < reports.Count; i++) Assert.AreEqual(reports[i - 1].EndEquity, reports[i].StartEquity);
            Assert.AreEqual(account.Equity - account.NetDeposits, reports.Sum(r => r.NetPnL));
        }
    }
}
