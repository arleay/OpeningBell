using System;
using System.Collections.Generic;
using NUnit.Framework;
using OpeningBell.Market;
using OpeningBell.Trading;

namespace OpeningBell.Tests
{
    /// <summary>Prop firm rules (PROP_SPEC.md §2): the numbers are the real firms'.</summary>
    public class PropDeskTests
    {
        private const string T = "AAA";
        private FakeMarketData _market;
        private PropDesk _desk;
        private decimal _bank;
        private readonly List<string> _charges = new List<string>();
        private readonly List<(decimal amount, string what)> _paid = new List<(decimal, string)>();

        [SetUp]
        public void SetUp()
        {
            _market = new FakeMarketData();
            _market.Contracts[T] = new ContractSpec(50m, 500m); // $50 a point: 8 points = $400
            _market.SetSession(MarketSession.Closed);
            _market.Now = TestMarkets.Monday.AddHours(8);
            Quote(100m);
            _bank = 5_000m;
            _charges.Clear();
            _paid.Clear();
            _desk = new PropDesk(_market, new BrokerRules { CommissionPerContract = 0 }, 1)
            {
                Charge = (amount, what) =>
                {
                    if (amount > _bank) return "Not enough in the bank.";
                    _bank -= amount;
                    _charges.Add(what);
                    return null;
                },
                Pay = (amount, what) =>
                {
                    _bank += amount;
                    _paid.Add((amount, what));
                },
            };
        }

        private void Quote(decimal price) => _market.SetQuote(T, price, price, size: 1000, last: price);

        private void Open()
        {
            _market.Now = _market.Now.Date.AddHours(9);
            _market.SetSession(MarketSession.Premarket);
            _market.Now = _market.Now.Date.AddHours(9.5);
            _market.SetSession(MarketSession.Regular);
            Quote(100m);
            _market.Tick();
        }

        private void Close()
        {
            _market.Now = _market.Now.Date.AddHours(16);
            _market.SetSession(MarketSession.AfterHours);
            _market.Now = _market.Now.Date.AddHours(20);
            _market.SetSession(MarketSession.Closed);
            _market.Now = NextWeekday(_market.Now.Date).AddHours(8);
            _market.Tick();
        }

        private static DateTime NextWeekday(DateTime d)
        {
            d = d.AddDays(1);
            while (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(1);
            return d;
        }

        /// <summary>One whole trading day: buy 1 at 100, sell at 100 + points (×$50).</summary>
        private void Day(PropAccount a, decimal points)
        {
            Open();
            a.Orders.SubmitMarket(T, OrderSide.Buy, 1);
            Quote(100m + points);
            _market.Tick();
            a.Orders.SubmitMarket(T, OrderSide.Sell, 1);
            Close();
        }

        private PropAccount Buy(PropFirm firm, int size)
        {
            Assert.IsNull(_desk.Buy(firm, size, out PropAccount a));
            return a;
        }

        [Test]
        public void Buy_ChargesTheMonth_AndOpensAtTheFullSize()
        {
            PropAccount a = Buy(PropFirms.Harbor, 50_000);
            Assert.AreEqual(5_000m - 170m, _bank);
            Assert.AreEqual(50_000m, a.Balance);
            StringAssert.StartsWith("profitharbortest", a.Id);
            Assert.AreEqual(48_000m, a.Threshold, "$2,000 EOD trailing drawdown");
            _bank = 100m;
            Assert.IsNotNull(_desk.Buy(PropFirms.Harbor, 150_000, out _), "can't buy what the bank can't cover");
        }

        [Test]
        public void ContractLimit_RejectsOrdersPastThePlanMaximum()
        {
            PropAccount a = Buy(PropFirms.Ridgeback, 50_000);
            Open();
            Assert.AreEqual(OrderStatus.Filled, a.Orders.SubmitMarket(T, OrderSide.Buy, 5).Status);
            Order sixth = a.Orders.SubmitMarket(T, OrderSide.Buy, 1);
            Assert.AreEqual(OrderStatus.Rejected, sixth.Status);
            StringAssert.Contains("5 contracts", sixth.StatusReason);
            Assert.AreEqual(OrderStatus.Filled, a.Orders.SubmitMarket(T, OrderSide.Sell, 5).Status, "closing is always allowed");
        }

        [Test]
        public void EodTrailing_FollowsTheBestClose_AndFailsIntraday()
        {
            PropAccount a = Buy(PropFirms.Harbor, 50_000);
            Day(a, 20m); // +$1,000
            Assert.AreEqual(51_000m, a.HighWater);
            Assert.AreEqual(49_000m, a.Threshold);

            Open();
            a.Orders.SubmitMarket(T, OrderSide.Buy, 1);
            Quote(60m); // -$2,000 open: equity 49,000
            _market.Tick();
            Assert.AreEqual(PropStatus.Failed, a.Status);
            Assert.AreEqual(0, a.OpenContracts, "liquidated");
            Assert.AreEqual(OrderStatus.Rejected, a.Orders.SubmitMarket(T, OrderSide.Buy, 1).Status);

            Assert.IsNull(_desk.Reset(a));
            Assert.AreEqual(PropStatus.Active, a.Status);
            Assert.AreEqual(50_000m, a.Balance);
        }

        [Test]
        public void Evaluation_PassesOnlyWithTargetDaysAndConsistency()
        {
            PropAccount a = Buy(PropFirms.Harbor, 25_000); // target $1,500, 5 days, 50%
            for (int i = 0; i < 4; i++) Day(a, 8m); // 4 × $400 = $1,600: target met, only 4 days
            Assert.AreEqual(PropStatus.Active, a.Status);
            Day(a, 2m); // day 5
            Assert.AreEqual(PropStatus.Passed, a.Status);
            Assert.IsFalse(a.Subscribed, "billing stops on passing");
            Assert.AreEqual(OrderStatus.Rejected, a.Orders.SubmitMarket(T, OrderSide.Buy, 1).Status, "passed accounts don't trade");

            PropAccount b = Buy(PropFirms.Harbor, 25_000);
            Day(b, 32m); // one $1,600 day
            for (int i = 0; i < 4; i++) Day(b, 0.2m); // $10 days
            Assert.AreEqual(PropStatus.Active, b.Status, "best day is 98% of profit: fails the 50% consistency rule");
            Assert.IsFalse(b.ConsistencyMet);
        }

        [Test]
        public void Harbor_Pro_TrailsIntraday_AndPaysDailyAboveTheBuffer()
        {
            PropAccount funded = FundedHarbor25K();
            StringAssert.StartsWith("profitharborpro", funded.Id);
            Assert.IsFalse(_desk.PayoutAvailable(funded).Eligible, "below the $26,500 buffer");

            // Intraday: an open +$1,000 that's given back still moves the threshold.
            Open();
            funded.Orders.SubmitMarket(T, OrderSide.Buy, 1);
            Quote(120m);
            _market.Tick();
            Assert.AreEqual(26_000m, funded.HighWater);
            Assert.AreEqual(24_500m, funded.Threshold);
            Quote(150m); // +$2,500 open
            _market.Tick();
            funded.Orders.SubmitMarket(T, OrderSide.Sell, 1);
            Close();
            Assert.AreEqual(25_000m, funded.Threshold, "locked at the starting balance");

            PayoutQuote quote = _desk.PayoutAvailable(funded);
            Assert.IsTrue(quote.Eligible);
            Assert.AreEqual(1_000m, quote.Max, "27,500 − 26,500 buffer");
            decimal before = _bank;
            Assert.IsNull(_desk.RequestPayout(funded, 200m));
            Assert.AreEqual(27_300m, funded.Balance);
            Assert.AreEqual(before, _bank, "paid the next weekday");
            Close();
            Assert.AreEqual(before + 200m * 0.8m - 50m, _bank, "80/20 split, $50 fee at $250 or less");
        }

        [Test]
        public void Ridgeback_Funded_PaysAfterFiveWinningDays_HalfTheProfitCapped()
        {
            PropAccount eval = Buy(PropFirms.Ridgeback, 50_000);
            Day(eval, 30m);
            Day(eval, 30m); // $3,000, 2 days, 50% each
            Assert.AreEqual(PropStatus.Passed, eval.Status);
            Assert.IsNull(_desk.Activate(eval, out PropAccount funded));
            Assert.AreEqual(2, funded.ContractLimit, "scaling plan starts at 2 contracts");

            for (int i = 0; i < 4; i++) Day(funded, 4m); // $200 winning days
            Assert.IsFalse(_desk.PayoutAvailable(funded).Eligible);
            Day(funded, 4m);
            PayoutQuote quote = _desk.PayoutAvailable(funded);
            Assert.IsTrue(quote.Eligible);
            Assert.AreEqual(500m, quote.Max, "50% of $1,000 profit");
            Assert.IsNull(_desk.RequestPayout(funded, 500m));
            Assert.IsTrue(funded.Locked, "threshold moves to the starting balance after a payout");
            Assert.IsFalse(_desk.PayoutAvailable(funded).Eligible, "winning days start over");
            Close();
            Assert.AreEqual(450m, _paid[_paid.Count - 1].amount, "90/10 split");
        }

        [Test]
        public void FlatBy355_ClosesPositions_AndRefusesNewOrders()
        {
            PropAccount a = Buy(PropFirms.Ridgeback, 50_000);
            Open();
            a.Orders.SubmitMarket(T, OrderSide.Buy, 2);
            _market.Now = _market.Now.Date.Add(new TimeSpan(15, 55, 0));
            _market.Tick();
            Assert.AreEqual(0, a.OpenContracts);
            Order late = a.Orders.SubmitMarket(T, OrderSide.Buy, 1);
            Assert.AreEqual(OrderStatus.Rejected, late.Status);
            StringAssert.Contains("3:55", late.StatusReason);
        }

        [Test]
        public void Subscription_RenewsMonthly_AndClosesWhenThePaymentFails()
        {
            PropAccount a = Buy(PropFirms.Ridgeback, 50_000);
            _market.Now = a.NextBilling.AddHours(8);
            _market.Tick();
            Assert.AreEqual(5_000m - 98m, _bank);
            _bank = 10m;
            _market.Now = a.NextBilling.AddHours(8);
            _market.Tick();
            Assert.AreEqual(PropStatus.Closed, a.Status);
        }

        [Test]
        public void SaveAndLoad_KeepsAccountsRulesAndPayouts()
        {
            PropAccount a = Buy(PropFirms.Ridgeback, 50_000);
            Day(a, 20m);
            PropSaveData saved = _desk.CaptureState();

            var loaded = new PropDesk(_market, new BrokerRules { CommissionPerContract = 0 }, 1);
            loaded.RestoreState(saved);
            PropAccount b = loaded.Find(a.Id);
            Assert.NotNull(b);
            Assert.AreEqual(51_000m, b.Balance);
            Assert.AreEqual(49_000m, b.Threshold);
            Assert.AreEqual(1, b.TradingDays);
            Assert.AreEqual(a.NextBilling, b.NextBilling);
            Open();
            Assert.AreEqual(OrderStatus.Rejected, b.Orders.SubmitMarket(T, OrderSide.Buy, 6).Status, "firm rules wired after loading");
        }

        private PropAccount FundedHarbor25K()
        {
            _bank = 10_000m;
            PropAccount eval = Buy(PropFirms.Harbor, 25_000);
            for (int i = 0; i < 5; i++) Day(eval, 8m);
            Assert.AreEqual(PropStatus.Passed, eval.Status);
            Assert.IsNull(_desk.Activate(eval, out PropAccount funded));
            Assert.AreEqual(funded.Id, eval.ActivatedAs);
            return funded;
        }
    }
}
