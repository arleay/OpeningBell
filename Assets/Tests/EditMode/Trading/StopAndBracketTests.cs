using System.Linq;
using NUnit.Framework;
using OpeningBell.Market;
using OpeningBell.Trading;

namespace OpeningBell.Tests
{
    /// <summary>Real stop orders (gap-through fills at the market), stop-limits, brackets (OCO), dragging prices, impact.</summary>
    public class StopAndBracketTests
    {
        private FakeMarketData _market;
        private Account _account;
        private OrderManager _orders;

        [SetUp]
        public void SetUp()
        {
            _market = new FakeMarketData();
            _account = new Account(_market);
            _account.Deposit(10_000m);
            _orders = new OrderManager(_market, _account, new BrokerRules { CommissionPerContract = 0 });
            _market.SetQuote("TST", 50.48m, 50.50m, size: 5000);
            _orders.SubmitMarket("TST", OrderSide.Buy, 100);
        }

        [Test]
        public void SellStop_GapsThrough_FillsAtTheMarketNotTheStop()
        {
            Order stop = _orders.SubmitStop("TST", OrderSide.Sell, 100, 50.00m);
            Assert.AreEqual(OrderStatus.Working, stop.Status);

            _market.SetQuote("TST", 50.18m, 50.20m, size: 5000);
            _market.Tick();
            Assert.AreEqual(OrderStatus.Working, stop.Status, "above the stop: nothing happens");

            // A gap: the next print is well below the stop.
            _market.SetQuote("TST", 49.70m, 49.72m, size: 5000);
            _market.Tick();
            Assert.AreEqual(OrderStatus.Filled, stop.Status);
            Assert.IsTrue(stop.Triggered);
            Assert.AreEqual(49.70m, stop.AverageFillPrice, "a stop is a trigger, not a guaranteed price");
        }

        [Test]
        public void SellStop_TriggersOnAWick_EvenIfTheTickClosesAbove()
        {
            Order stop = _orders.SubmitStop("TST", OrderSide.Sell, 100, 50.00m);
            // The tick swept down to 49.98 and bounced: the stop was hit.
            _market.SetQuote("TST", 50.08m, 50.10m, size: 5000, last: 50.08m, tickHigh: 50.10m, tickLow: 49.98m);
            _market.Tick();
            Assert.AreEqual(OrderStatus.Filled, stop.Status);
            Assert.AreEqual(50.08m, stop.AverageFillPrice, "then fills against the book as it stands");
        }

        [Test]
        public void Stops_DoNotTriggerOutsideTheRegularSession()
        {
            _market.SetSession(MarketSession.AfterHours);
            Assert.AreEqual(0, _account.Portfolio.QuantityOf("TST"), "the regular close flattened the day's position");

            Order buy = _orders.SubmitLimit("TST", OrderSide.Buy, 10, 50.50m);
            Assert.AreEqual(OrderStatus.Filled, buy.Status);
            Order stop = _orders.SubmitStop("TST", OrderSide.Sell, 10, 50.00m, gtc: true);
            _market.SetQuote("TST", 49.50m, 49.60m);
            _market.Tick();
            Assert.AreEqual(OrderStatus.Working, stop.Status, "a thin after-hours print doesn't set off a stop");

            _market.SetSession(MarketSession.Closed);
            Assert.AreEqual(OrderStatus.Cancelled, stop.Status, "nothing is held overnight: the position closes and its stop goes");
            Assert.AreEqual(0, _account.Portfolio.QuantityOf("TST"));
        }

        [Test]
        public void StopLimit_TriggersThenWaitsForItsLimit()
        {
            Order order = _orders.SubmitStop("TST", OrderSide.Sell, 100, 50.00m, limitPrice: 49.90m);
            _market.SetQuote("TST", 49.80m, 49.82m);
            _market.Tick();
            Assert.IsTrue(order.Triggered);
            Assert.AreEqual(OrderStatus.Working, order.Status, "the gap went below its limit, so it waits");
            _market.SetQuote("TST", 49.92m, 49.94m);
            _market.Tick();
            Assert.AreEqual(OrderStatus.Filled, order.Status);
            Assert.GreaterOrEqual(order.AverageFillPrice, 49.90m);
        }

        [Test]
        public void Stop_OnTheWrongSideOfTheMarket_IsRejected()
        {
            Order bad = _orders.SubmitStop("TST", OrderSide.Sell, 100, 50.60m);
            Assert.AreEqual(OrderStatus.Rejected, bad.Status);
            StringAssert.Contains("below the bid", bad.StatusReason);
        }

        [Test]
        public void Bracket_TakeProfitFills_AndCancelsTheStopLoss()
        {
            var legs = _orders.SubmitBracket("TST", 100, 52.00m, 49.00m);
            Assert.AreEqual(2, legs.Count);
            Assert.IsTrue(legs.All(o => o.Status == OrderStatus.Working && o.Gtc), string.Join("; ", legs.Select(o => o.StatusReason)));
            Assert.AreEqual(0, _orders.AvailableToSell("TST"), "both legs protect the same 100 shares");

            _market.SetQuote("TST", 52.05m, 52.07m);
            _market.Tick();
            Order tp = legs[0], sl = legs[1];
            Assert.AreEqual(OrderStatus.Filled, tp.Status);
            Assert.AreEqual(OrderStatus.Cancelled, sl.Status, "one cancels the other");
            Assert.AreEqual(0, _account.Portfolio.QuantityOf("TST"));
        }

        [Test]
        public void BracketOnPartOfAPosition_LeavesTheRestFreeToSell()
        {
            var legs = _orders.SubmitBracket("TST", 60, 52.00m, 49.00m);
            Assert.AreEqual(40, _orders.AvailableToSell("TST"), "the bracket commits 60 once, not twice");
            Order manual = _orders.SubmitMarket("TST", OrderSide.Sell, 40);
            Assert.AreEqual(OrderStatus.Filled, manual.Status);
            Assert.IsTrue(legs.All(o => o.IsOpen && o.RemainingQuantity == 60), "the bracket still protects the 60 left");
            Assert.AreEqual(OrderStatus.Rejected, _orders.SubmitMarket("TST", OrderSide.Sell, 1).Status, "everything left is committed");
        }

        [Test]
        public void DraggingTheStopLine_MovesTheRealOrder()
        {
            var legs = _orders.SubmitBracket("TST", 100, 52.00m, 49.00m);
            Order sl = legs[1];
            Assert.IsNull(_orders.ModifyPrice(sl.Id, 49.50m));
            Assert.AreEqual(49.50m, sl.StopPrice);
            Assert.IsNotNull(_orders.ModifyPrice(sl.Id, 50.60m), "can't drag a sell stop above the bid");
            Assert.AreEqual(49.50m, sl.StopPrice);

            Order tp = legs[0];
            Assert.IsNull(_orders.ModifyPrice(tp.Id, 50.40m), "dragging the take-profit through the bid sells now");
            Assert.AreEqual(OrderStatus.Filled, tp.Status);
            Assert.AreEqual(OrderStatus.Cancelled, sl.Status);
        }

        [Test]
        public void OldShareSave_LoadsFlat_WithTheSharesCostRefunded()
        {
            // A save from before contracts: 100 shares bought for $5,050 in cash, and a working share order.
            var days = new TradingDayRecorder(_market, _account, _orders);
            _orders.SubmitLimit("TST", OrderSide.Buy, 50, 49.00m);
            TradingSaveData data = TradingState.Capture(_account, _orders, days);
            data.Contracts = false;
            data.Positions.Single(p => p.Ticker == "TST").CostBasis = "5050.00";
            data.Ledger.Add(new LedgerEntrySaveData { Id = 99, Type = (int)LedgerEntryType.TradeBuy, Amount = "-5050.00", BalanceAfter = "4950.00", Ticker = "TST" });

            var account = new Account(_market);
            var orders = new OrderManager(_market, account, new BrokerRules { CommissionPerContract = 0 });
            TradingState.Restore(UnityEngine.JsonUtility.FromJson<TradingSaveData>(UnityEngine.JsonUtility.ToJson(data)), account, orders,
                new TradingDayRecorder(_market, account, orders));

            Assert.AreEqual(0, account.Portfolio.QuantityOf("TST"), "no 100-contract position appears");
            Assert.AreEqual(10_000m, account.Cash, "what the shares cost comes back");
            Assert.AreEqual(0, orders.OpenOrders.Count, "share orders don't come back as contract orders");
        }

        [Test]
        public void Bracket_SurvivesSaveAndLoad_AndStillCancelsTheOtherLeg()
        {
            var days = new TradingDayRecorder(_market, _account, _orders);
            var legs = _orders.SubmitBracket("TST", 100, 52.00m, 49.00m);
            _orders.ModifyPrice(legs[1].Id, 49.40m);
            string json = UnityEngine.JsonUtility.ToJson(TradingState.Capture(_account, _orders, days));

            var market = new FakeMarketData();
            market.SetQuote("TST", 50.48m, 50.50m, size: 5000);
            var account = new Account(market);
            var orders = new OrderManager(market, account, new BrokerRules { CommissionPerContract = 0 });
            TradingState.Restore(UnityEngine.JsonUtility.FromJson<TradingSaveData>(json), account, orders, new TradingDayRecorder(market, account, orders));

            Order sl = orders.OpenOrders.Single(o => o.Type == OrderType.Stop);
            Order tp = orders.OpenOrders.Single(o => o.Type == OrderType.Limit);
            Assert.AreEqual(49.40m, sl.StopPrice);
            Assert.IsTrue(sl.Gtc && tp.Gtc);
            Assert.AreEqual(sl.OcoGroup, tp.OcoGroup);

            market.SetQuote("TST", 49.30m, 49.32m, size: 5000);
            market.Tick();
            Assert.AreEqual(OrderStatus.Filled, sl.Status, "the stop-loss fires after the load");
            Assert.AreEqual(OrderStatus.Cancelled, tp.Status, "and still cancels its take-profit");
            var next = orders.SubmitBracket("TST", 1, 60m, 1m);
            Assert.AreNotEqual(sl.OcoGroup, next[0].OcoGroup, "new brackets don't reuse a loaded group");
        }

        [Test]
        public void AggressiveFills_AreReportedToTheMarket_ForImpact()
        {
            Assert.AreEqual(("TST", 100L), _market.ReportedFlow.Single(), "the setup's market buy took liquidity");
            _orders.SubmitMarket("TST", OrderSide.Sell, 100);
            Assert.AreEqual(-100, _market.ReportedFlow[1].Shares);
        }
    }
}
