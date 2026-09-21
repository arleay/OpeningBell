using System.Linq;
using NUnit.Framework;
using OpeningBell.Market;
using OpeningBell.Trading;

namespace OpeningBell.Tests
{
    public class OrderExecutionTests
    {
        private FakeMarketData _market;
        private Account _account;
        private OrderManager _orders;

        [SetUp]
        public void SetUp() => Build(new BrokerRules { CommissionPerShare = 0, MinimumCommission = 0 });

        private void Build(BrokerRules rules)
        {
            _market = new FakeMarketData();
            _account = new Account(_market);
            _account.Deposit(10_000m);
            _orders = new OrderManager(_market, _account, rules);
            _market.SetQuote("TST", 10.18m, 10.20m);
        }

        // ---- Market orders ----

        [Test]
        public void MarketBuy_ExecutesAtAsk()
        {
            Order order = _orders.SubmitMarket("TST", OrderSide.Buy, 100);

            Assert.AreEqual(OrderStatus.Filled, order.Status);
            Assert.AreEqual(10.20m, order.AverageFillPrice);
            Assert.AreEqual(8980m, _account.Cash);
            Assert.AreEqual(100, _account.Portfolio.QuantityOf("TST"));
            Assert.AreEqual(10.20m, _account.Portfolio.Find("TST").AveragePrice);
        }

        [Test]
        public void MarketSell_ExecutesAtBid()
        {
            _orders.SubmitMarket("TST", OrderSide.Buy, 100);
            _market.SetQuote("TST", 10.30m, 10.32m);

            Order sell = _orders.SubmitMarket("TST", OrderSide.Sell, 100);

            Assert.AreEqual(OrderStatus.Filled, sell.Status);
            Assert.AreEqual(10.30m, sell.AverageFillPrice);
            Assert.AreEqual(10.00m, _account.RealizedPnL);
            Assert.AreEqual(10_010m, _account.Cash);
        }

        [Test]
        public void LargeMarketOrder_WalksBook_AndFillsOverSeveralTicks()
        {
            Build(new BrokerRules { CommissionPerShare = 0, MinimumCommission = 0, BookLevelsPerTick = 3 });
            _market.SetQuote("TST", 10.00m, 10.02m, size: 100);

            Order order = _orders.SubmitMarket("TST", OrderSide.Buy, 600);

            // Levels: 100 @ 10.02, 150 @ 10.03, 200 @ 10.04
            Assert.AreEqual(OrderStatus.PartiallyFilled, order.Status);
            Assert.AreEqual(450, order.FilledQuantity);
            Assert.Greater(order.AverageFillPrice, 10.02m, "size beyond displayed liquidity costs slippage");

            _market.Tick();

            Assert.AreEqual(OrderStatus.Filled, order.Status);
            Assert.AreEqual(6018m, order.FilledNotional);
            Assert.AreEqual(10_000m - 6018m, _account.Cash);
            Assert.AreEqual(_account.Cash, _account.BuyingPower, "reservation released");
        }

        [Test]
        public void UnfilledMarketOrder_IsCancelledAtRegularClose()
        {
            Build(new BrokerRules { CommissionPerShare = 0, MinimumCommission = 0, BookLevelsPerTick = 3 });
            _market.SetQuote("TST", 10.00m, 10.02m, size: 100);
            Order order = _orders.SubmitMarket("TST", OrderSide.Buy, 600);

            _market.SetSession(MarketSession.AfterHours);

            Assert.AreEqual(OrderStatus.Cancelled, order.Status);
            Assert.AreEqual(450, _account.Portfolio.QuantityOf("TST"));
            Assert.AreEqual(_account.Cash, _account.BuyingPower);
        }

        // ---- Limit orders ----

        [Test]
        public void LimitBuy_WaitsForMarket_ThenFillsAtLimit()
        {
            Order order = _orders.SubmitLimit("TST", OrderSide.Buy, 100, 10.00m);
            Assert.AreEqual(OrderStatus.Working, order.Status);
            Assert.AreEqual(9000m, _account.BuyingPower);

            _market.SetQuote("TST", 10.05m, 10.07m);
            _market.Tick();
            Assert.AreEqual(OrderStatus.Working, order.Status);

            _market.SetQuote("TST", 9.95m, 9.97m);
            _market.Tick();
            Assert.AreEqual(OrderStatus.Filled, order.Status);
            Assert.IsTrue(order.Fills.All(f => f.Price == 10.00m));
            Assert.AreEqual(9000m, _account.Cash);
        }

        [Test]
        public void MarketableLimitBuy_GetsPriceImprovement()
        {
            Order order = _orders.SubmitLimit("TST", OrderSide.Buy, 100, 10.50m);
            Assert.AreEqual(OrderStatus.Filled, order.Status);
            Assert.AreEqual(10.20m, order.AverageFillPrice);
        }

        [Test]
        public void LimitSell_NeverFillsBelowLimit()
        {
            _orders.SubmitMarket("TST", OrderSide.Buy, 100);
            Order sell = _orders.SubmitLimit("TST", OrderSide.Sell, 100, 10.50m);

            _market.SetQuote("TST", 10.40m, 10.42m);
            _market.Tick();
            Assert.AreEqual(OrderStatus.Working, sell.Status);

            _market.SetQuote("TST", 10.60m, 10.62m);
            _market.Tick();
            Assert.AreEqual(OrderStatus.Filled, sell.Status);
            Assert.IsTrue(sell.Fills.All(f => f.Price >= 10.50m));
            Assert.AreEqual(30m, _account.RealizedPnL);
        }

        [Test]
        public void LimitInsideSpread_FillsFromOpposingFlowOnly()
        {
            _market.SetQuote("TST", 10.00m, 10.10m);
            Order order = _orders.SubmitLimit("TST", OrderSide.Buy, 200, 10.05m);

            _market.SetQuote("TST", 10.00m, 10.10m, last: 10.00m, lastVolume: 150, direction: -1);
            _market.Tick();
            Assert.AreEqual(OrderStatus.PartiallyFilled, order.Status);
            Assert.AreEqual(150, order.FilledQuantity);

            _market.SetQuote("TST", 10.00m, 10.10m, last: 10.10m, lastVolume: 300, direction: 1);
            _market.Tick();
            Assert.AreEqual(150, order.FilledQuantity, "buyer-initiated flow does not hit a bid");

            _market.SetQuote("TST", 10.00m, 10.10m, last: 10.00m, lastVolume: 500, direction: -1);
            _market.Tick();
            Assert.AreEqual(OrderStatus.Filled, order.Status);
            Assert.IsTrue(order.Fills.All(f => f.Price == 10.05m));
        }

        [Test]
        public void LimitJoiningTheBid_WaitsForMarketToTradeThrough()
        {
            _market.SetQuote("TST", 10.00m, 10.10m);
            Order order = _orders.SubmitLimit("TST", OrderSide.Buy, 100, 10.00m);

            _market.SetQuote("TST", 10.00m, 10.10m, last: 10.00m, lastVolume: 1000, direction: -1);
            _market.Tick();

            Assert.AreEqual(OrderStatus.Working, order.Status);
        }

        // ---- Validation & lifecycle ----

        [Test]
        public void OutsideRegularHours_MarketRejected_LimitAccepted()
        {
            _market.SetSession(MarketSession.Premarket);

            Order market = _orders.SubmitMarket("TST", OrderSide.Buy, 10);
            Order limit = _orders.SubmitLimit("TST", OrderSide.Buy, 10, 10.00m);

            Assert.AreEqual(OrderStatus.Rejected, market.Status);
            StringAssert.Contains("regular session", market.StatusReason);
            Assert.AreEqual(OrderStatus.Working, limit.Status);
        }

        [Test]
        public void ClosedMarket_RejectsEverything()
        {
            _market.SetSession(MarketSession.Closed);
            Assert.AreEqual(OrderStatus.Rejected, _orders.SubmitLimit("TST", OrderSide.Buy, 10, 10.00m).Status);
        }

        [Test]
        public void InvalidOrders_AreRejected()
        {
            Assert.AreEqual(OrderStatus.Rejected, _orders.SubmitMarket("TST", OrderSide.Buy, 0).Status);
            Assert.AreEqual(OrderStatus.Rejected, _orders.SubmitMarket("NOPE", OrderSide.Buy, 10).Status);
            Assert.AreEqual(OrderStatus.Rejected, _orders.SubmitLimit("TST", OrderSide.Buy, 10, 10.005m).Status);
        }

        [Test]
        public void BuyBeyondBuyingPower_IsRejected()
        {
            // Market buys reserve ask + 5%: 1000 × 10.71 > 10,000.
            Order market = _orders.SubmitMarket("TST", OrderSide.Buy, 1000);
            Assert.AreEqual(OrderStatus.Rejected, market.Status);
            StringAssert.Contains("buying power", market.StatusReason);

            Order limit = _orders.SubmitLimit("TST", OrderSide.Buy, 1000, 9.99m);
            Assert.AreEqual(OrderStatus.Working, limit.Status);
            Assert.AreEqual(OrderStatus.Rejected, _orders.SubmitLimit("TST", OrderSide.Buy, 2, 9.99m).Status,
                "cash reserved by the first order is not available");
        }

        [Test]
        public void SellBeyondPosition_IsRejected_NoShorting()
        {
            Assert.AreEqual(OrderStatus.Rejected, _orders.SubmitMarket("TST", OrderSide.Sell, 1).Status);

            _orders.SubmitMarket("TST", OrderSide.Buy, 100);
            Assert.AreEqual(OrderStatus.Working, _orders.SubmitLimit("TST", OrderSide.Sell, 60, 11m).Status);
            Order tooMany = _orders.SubmitLimit("TST", OrderSide.Sell, 50, 11m);
            Assert.AreEqual(OrderStatus.Rejected, tooMany.Status);
            StringAssert.Contains("Only 40", tooMany.StatusReason);
        }

        [Test]
        public void Cancel_ReleasesReservation()
        {
            Order order = _orders.SubmitLimit("TST", OrderSide.Buy, 100, 9.00m);
            Assert.AreEqual(9100m, _account.BuyingPower);

            Assert.IsTrue(_orders.Cancel(order.Id));
            Assert.AreEqual(OrderStatus.Cancelled, order.Status);
            Assert.AreEqual(10_000m, _account.BuyingPower);
            Assert.IsFalse(_orders.Cancel(order.Id));
        }

        [Test]
        public void DayOrders_ExpireWhenMarketCloses()
        {
            Order order = _orders.SubmitLimit("TST", OrderSide.Buy, 100, 9.00m);

            _market.SetSession(MarketSession.AfterHours);
            Assert.AreEqual(OrderStatus.Working, order.Status);

            _market.SetSession(MarketSession.Closed);
            Assert.AreEqual(OrderStatus.Cancelled, order.Status);
            Assert.AreEqual(10_000m, _account.BuyingPower);
        }

        // ---- Commissions & accounting ----

        [Test]
        public void Commission_AppliesMinimum_Cap_AndIsNotOverchargedOnPartialFills()
        {
            Build(new BrokerRules { BookLevelsPerTick = 2 }); // $0.005/share, $1 min, 1% cap

            Assert.AreEqual(1.00m, _orders.SubmitMarket("TST", OrderSide.Buy, 100).Commission, "minimum");
            Assert.AreEqual(0.10m, _orders.SubmitMarket("TST", OrderSide.Buy, 1).Commission, "1% cap on a $10 order");

            _market.SetQuote("TST", 10.00m, 10.02m, size: 100);
            Order big = _orders.SubmitMarket("TST", OrderSide.Buy, 700); // 250 per tick
            _market.Tick();
            _market.Tick();

            Assert.AreEqual(OrderStatus.Filled, big.Status);
            Assert.Greater(big.Fills.Count, 2);
            Assert.AreEqual(3.50m, big.Commission, "700 × $0.005, not a minimum per fill");

            decimal ledgerCommissions = -_account.Ledger.Entries.Where(e => e.Type == LedgerEntryType.Commission).Sum(e => e.Amount);
            Assert.AreEqual(4.60m, _account.TotalCommissions);
            Assert.AreEqual(_account.TotalCommissions, ledgerCommissions);
        }

        [Test]
        public void Accounting_CashLedgerAndPnL_Reconcile()
        {
            Build(new BrokerRules()); // real commissions

            _orders.SubmitMarket("TST", OrderSide.Buy, 100);                   // 100 @ 10.20
            _market.SetQuote("TST", 10.50m, 10.52m);
            _orders.SubmitMarket("TST", OrderSide.Sell, 40);                   // 40 @ 10.50
            _orders.SubmitMarket("TST", OrderSide.Buy, 50);                    // 50 @ 10.52
            _orders.SubmitLimit("TST", OrderSide.Buy, 10, 9.00m);              // open, reserves cash
            _market.SetQuote("TST", 10.30m, 10.32m, last: 10.31m);

            Position position = _account.Portfolio.Find("TST");
            Assert.AreEqual(110, position.Quantity);
            Assert.AreEqual(12.00m, _account.RealizedPnL); // 40 × 10.50 − 408.00 cost removed

            decimal ledgerSum = _account.Ledger.Entries.Sum(e => e.Amount);
            Assert.AreEqual(ledgerSum, _account.Cash);
            Assert.AreEqual(_account.Ledger.Entries.Last().BalanceAfter, _account.Cash);

            Assert.AreEqual(_account.Cash + 110 * 10.31m, _account.Equity);
            Assert.AreEqual(_account.Equity - _account.NetDeposits,
                _account.RealizedPnL + _account.UnrealizedPnL - _account.TotalCommissions);
            Assert.AreEqual(_account.Equity - 10_000m, _account.DailyPnL);
            Assert.AreEqual(_account.Cash - 90m - 0.90m, _account.BuyingPower, "open order reserves 10 × 9.00 + commission");
        }
    }
}
