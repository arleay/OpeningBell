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
        public void SetUp() => Build(new BrokerRules { CommissionPerContract = 0 });

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
            Assert.AreEqual(10_000m, _account.Cash, "contracts aren't paid for");
            Assert.AreEqual(1000m, _account.MarginInUse, "100 contracts × $10 margin");
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
            Build(new BrokerRules { CommissionPerContract = 0, BookLevelsPerTick = 3 });
            _market.SetQuote("TST", 10.00m, 10.02m, size: 100);

            Order order = _orders.SubmitMarket("TST", OrderSide.Buy, 600);

            // Levels: 100 @ 10.02, 150 @ 10.03, 200 @ 10.04
            Assert.AreEqual(OrderStatus.PartiallyFilled, order.Status);
            Assert.AreEqual(450, order.FilledQuantity);
            Assert.Greater(order.AverageFillPrice, 10.02m, "size beyond displayed liquidity costs slippage");

            _market.Tick();

            Assert.AreEqual(OrderStatus.Filled, order.Status);
            Assert.AreEqual(6018m, order.FilledNotional);
            Assert.AreEqual(0m, _account.ReservedCash, "reservation released");
            Assert.AreEqual(_account.Equity - _account.MarginInUse, _account.BuyingPower);
        }

        [Test]
        public void UnfilledMarketOrder_IsCancelledAtRegularClose()
        {
            Build(new BrokerRules { CommissionPerContract = 0, BookLevelsPerTick = 3 });
            _market.SetQuote("TST", 10.00m, 10.02m, size: 100);
            Order order = _orders.SubmitMarket("TST", OrderSide.Buy, 600);

            Assert.AreEqual(450, order.FilledQuantity);
            _market.SetSession(MarketSession.AfterHours);

            Assert.AreEqual(OrderStatus.Cancelled, order.Status);
            Assert.AreEqual(0, _account.Portfolio.QuantityOf("TST"), "and what did fill is closed at the close");
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
            Assert.AreEqual(10_000m, _account.Cash);
            Assert.AreEqual(1000m, _account.MarginInUse);
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
            _market.Contracts["TST"] = new ContractSpec(20m, 1000m); // $1,000 day margin a contract
            Order market = _orders.SubmitMarket("TST", OrderSide.Buy, 11);
            Assert.AreEqual(OrderStatus.Rejected, market.Status);
            StringAssert.Contains("buying power", market.StatusReason);

            Order limit = _orders.SubmitLimit("TST", OrderSide.Buy, 10, 9.99m);
            Assert.AreEqual(OrderStatus.Working, limit.Status);
            Assert.AreEqual(OrderStatus.Rejected, _orders.SubmitLimit("TST", OrderSide.Buy, 1, 9.99m).Status,
                "margin reserved by the first order is not available");
        }

        [Test]
        public void Short_OpensWithMargin_AndProfitsWhenPriceFalls()
        {
            _market.Contracts["TST"] = new ContractSpec(20m, 1000m);
            Order sell = _orders.SubmitMarket("TST", OrderSide.Sell, 3);                   // short 3 @ 10.18
            Assert.AreEqual(OrderStatus.Filled, sell.Status, sell.StatusReason);
            Assert.AreEqual(-3, _account.Portfolio.QuantityOf("TST"));
            Assert.AreEqual(3000m, _account.MarginInUse);

            _market.SetQuote("TST", 9.66m, 9.68m, last: 9.67m);
            Assert.AreEqual(30.60m, _account.UnrealizedPnL, "3 × 0.51 × $20 in the short's favour");
            _orders.SubmitMarket("TST", OrderSide.Buy, 3);                                  // cover @ 9.68
            Assert.AreEqual(30m, _account.RealizedPnL);
            Assert.AreEqual(0, _account.Portfolio.QuantityOf("TST"));
            Assert.AreEqual(0m, _account.MarginInUse);
        }

        [Test]
        public void SellingMoreThanHeld_FlipsShort_AndOnlyTheNewPartPostsMargin()
        {
            _market.Contracts["TST"] = new ContractSpec(20m, 1000m);
            _orders.SubmitMarket("TST", OrderSide.Buy, 2);
            Assert.AreEqual(3, _orders.OpeningQuantity("TST", OrderSide.Sell, 5), "2 close the long, 3 open a short");
            Assert.AreEqual(0, _orders.OpeningQuantity("TST", OrderSide.Sell, 2));

            _orders.SubmitMarket("TST", OrderSide.Sell, 5);
            Assert.AreEqual(-3, _account.Portfolio.QuantityOf("TST"));
            Assert.AreEqual(-3, _account.Portfolio.Find("TST").Quantity);
            Assert.AreEqual(10.18m, _account.Portfolio.Find("TST").AveragePrice, "the short's entry is the flip price");

            Order tooBig = _orders.SubmitMarket("TST", OrderSide.Sell, 8);                  // just under 7,000 free (the flip cost a little): 6 more
            Assert.AreEqual(OrderStatus.Rejected, tooBig.Status);
            StringAssert.Contains("buying power", tooBig.StatusReason);
            Assert.AreEqual(9, _orders.MaxQuantity("TST", OrderSide.Buy), "cover 3, then open 6 long");
        }

        [Test]
        public void ShortsAreClosedAtTheCloseToo()
        {
            _market.Contracts["TST"] = new ContractSpec(20m, 1000m);
            _orders.SubmitMarket("TST", OrderSide.Sell, 2);                                 // short 2 @ 10.18
            _market.SetQuote("TST", 10.07m, 10.09m, last: 10.08m);
            _market.SetSession(MarketSession.AfterHours);
            Assert.AreEqual(0, _account.Portfolio.QuantityOf("TST"));
            Assert.AreEqual(4m, _account.RealizedPnL, "bought back at the 10.08 close: 2 × 0.10 × $20");
        }

        [Test]
        public void MaxBuyQuantity_IsExactlyTheLargestAcceptedOrder()
        {
            Build(new BrokerRules()); // real commissions, 5% market reserve
            long maxMarket = _orders.MaxBuyQuantity("TST", OrderType.Market, 0m);
            long maxLimit = _orders.MaxBuyQuantity("TST", OrderType.Limit, 9.50m);

            Assert.AreEqual(OrderStatus.Rejected, _orders.SubmitMarket("TST", OrderSide.Buy, maxMarket + 1).Status);
            Assert.AreEqual(OrderStatus.Rejected, _orders.SubmitLimit("TST", OrderSide.Buy, maxLimit + 1, 9.50m).Status);
            Assert.AreEqual(OrderStatus.Working, _orders.SubmitLimit("TST", OrderSide.Buy, maxLimit, 9.50m).Status);
            Assert.AreEqual(0, _orders.MaxBuyQuantity("TST", OrderType.Limit, 9.50m), "all buying power is now reserved");
            Assert.Greater(maxMarket, 0);
        }

        [Test]
        public void AvailableToSell_ExcludesSharesCommittedToOpenSells()
        {
            _orders.SubmitMarket("TST", OrderSide.Buy, 100);
            _orders.SubmitLimit("TST", OrderSide.Sell, 60, 11m);
            Assert.AreEqual(40, _orders.AvailableToClose("TST"));
            Assert.AreEqual(0, _orders.AvailableToClose("NOPE"));
        }

        [Test]
        public void Cancel_ReleasesReservation()
        {
            Order order = _orders.SubmitLimit("TST", OrderSide.Buy, 100, 9.00m);
            Assert.AreEqual(9000m, _account.BuyingPower, "100 contracts × $10 margin held");

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
        public void Commission_IsPerContract_AndChargedOnceAcrossPartialFills()
        {
            Build(new BrokerRules { BookLevelsPerTick = 2 }); // $2.50 a contract a side

            Assert.AreEqual(2.50m, _orders.SubmitMarket("TST", OrderSide.Buy, 1).Commission);

            _market.SetQuote("TST", 10.00m, 10.02m, size: 100);
            Order big = _orders.SubmitMarket("TST", OrderSide.Buy, 300); // 250 per tick
            _market.Tick();

            Assert.AreEqual(OrderStatus.Filled, big.Status);
            Assert.GreaterOrEqual(big.Fills.Count, 2);
            Assert.AreEqual(750m, big.Commission, "300 × $2.50, however many fills");

            decimal ledgerCommissions = -_account.Ledger.Entries.Where(e => e.Type == LedgerEntryType.Commission).Sum(e => e.Amount);
            Assert.AreEqual(752.50m, _account.TotalCommissions);
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

            Assert.AreEqual(_account.NetDeposits + _account.RealizedPnL - _account.TotalCommissions, _account.Cash,
                "only realized results and fees settle in cash");
            Assert.AreEqual(_account.Cash + _account.UnrealizedPnL, _account.Equity);
            Assert.AreEqual(_account.Equity - _account.NetDeposits,
                _account.RealizedPnL + _account.UnrealizedPnL - _account.TotalCommissions);
            Assert.AreEqual(_account.Equity - 10_000m, _account.DailyPnL);
            Assert.AreEqual(_account.Equity - 110 * 10m - (10 * 10m + 25m), _account.BuyingPower,
                "margin for 110 contracts, plus the open order's 10 contracts and their commission");
        }

        // ---- Futures-style contracts ----

        [Test]
        public void PointValue_SetsTheDollars_AndMarginLimitsSize()
        {
            _market.Contracts["TST"] = new ContractSpec(20m, 1500m); // $20 a point, $1,500 margin
            Assert.AreEqual(6, _orders.MaxBuyQuantity("TST", OrderType.Market, 0m), "10,000 / 1,500");

            _orders.SubmitMarket("TST", OrderSide.Buy, 2);                    // 2 @ 10.20
            _market.SetQuote("TST", 10.70m, 10.72m, last: 10.71m);
            Assert.AreEqual(20.40m, _account.UnrealizedPnL, "2 × 0.51 × $20");

            _orders.SubmitMarket("TST", OrderSide.Sell, 2);                   // 2 @ 10.70
            Assert.AreEqual(20m, _account.RealizedPnL, "2 × 0.50 × $20");
            Assert.AreEqual(10_020m, _account.Cash);
            Assert.AreEqual(0m, _account.MarginInUse);
        }

        [Test]
        public void Positions_AreClosedAutomaticallyAtTheClose()
        {
            _market.Contracts["TST"] = new ContractSpec(20m, 1000m);
            _orders.SubmitMarket("TST", OrderSide.Buy, 3);                                // 3 @ 10.20
            var bracket = _orders.SubmitBracket("TST", 3, 11.00m, 9.50m);
            Assert.IsTrue(bracket.All(o => o.Status == OrderStatus.Working));

            _market.SetQuote("TST", 10.39m, 10.41m, last: 10.40m);                          // the closing print
            _market.SetSession(MarketSession.AfterHours);

            Assert.AreEqual(0, _account.Portfolio.QuantityOf("TST"), "flat at the close");
            Assert.AreEqual(12m, _account.RealizedPnL, "3 × 0.20 × $20 at the closing price");
            Assert.IsTrue(bracket.All(o => o.Status == OrderStatus.Cancelled), "its TP/SL are cancelled");
            Order flatten = _orders.Orders.Last();
            Assert.AreEqual(OrderSide.Sell, flatten.Side);
            Assert.AreEqual(OrderStatus.Filled, flatten.Status);
            StringAssert.Contains("close", flatten.StatusReason);

            // Something opened after hours is closed when the market closes for the night.
            _orders.SubmitLimit("TST", OrderSide.Buy, 1, 10.45m);
            Assert.AreEqual(1, _account.Portfolio.QuantityOf("TST"));
            _market.SetSession(MarketSession.Closed);
            Assert.AreEqual(0, _account.Portfolio.QuantityOf("TST"));
        }
    }
}
