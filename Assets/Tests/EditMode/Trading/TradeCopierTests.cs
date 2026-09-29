using System.Collections.Generic;
using NUnit.Framework;
using OpeningBell.Market;
using OpeningBell.Trading;

namespace OpeningBell.Tests
{
    public class TradeCopierTests
    {
        private FakeMarketData _market;
        private OrderManager _leader, _a, _b;
        private List<CopyTarget> _followers;
        private readonly TradeCopier _copier = new TradeCopier();

        [SetUp]
        public void SetUp()
        {
            _market = new FakeMarketData();
            _market.SetQuote("TST", 10.00m, 10.02m);
            _leader = Manager(10_000m);
            _a = Manager(10_000m);
            _b = Manager(15m); // can only afford one contract ($10 margin)
            _followers = new List<CopyTarget> { new CopyTarget("A", _a), new CopyTarget("B", _b) };
        }

        private OrderManager Manager(decimal cash)
        {
            var account = new Account(_market);
            account.Deposit(cash);
            return new OrderManager(_market, account, new BrokerRules { CommissionPerContract = 0 });
        }

        [Test]
        public void Orders_AreCopied1To1_AndRefusalsReported()
        {
            Order o = _copier.Place(_leader, _followers, om => om.SubmitMarket("TST", OrderSide.Buy, 3));
            Assert.AreEqual(OrderStatus.Filled, o.Status);
            Assert.AreEqual(1, _a.Fills.Count);
            Assert.AreEqual(3, _a.Fills[0].Quantity);
            Assert.AreEqual(0, _b.Fills.Count);
            StringAssert.Contains("Copied to 1 account", _copier.LastNote);
            StringAssert.Contains("B:", _copier.LastNote);
        }

        [Test]
        public void RejectedLeaderOrder_IsNotCopied()
        {
            _market.SetSession(MarketSession.Closed);
            _copier.Place(_leader, _followers, om => om.SubmitMarket("TST", OrderSide.Buy, 1));
            Assert.AreEqual(0, _a.Orders.Count, "follower never saw the order");
        }

        [Test]
        public void CancelAndModify_FollowTheCopies()
        {
            Order o = _copier.Place(_leader, _followers, om => om.SubmitLimit("TST", OrderSide.Buy, 1, 9.00m));
            Assert.AreEqual(1, _a.OpenOrders.Count);
            Assert.IsNull(_copier.Modify(_leader, o.Id, 9.50m));
            Assert.AreEqual(9.50m, _a.OpenOrders[0].LimitPrice);
            Assert.AreEqual(9.50m, _b.OpenOrders[0].LimitPrice);
            Assert.IsTrue(_copier.Cancel(_leader, o.Id));
            Assert.AreEqual(0, _a.OpenOrders.Count);
            Assert.AreEqual(0, _b.OpenOrders.Count);
        }
    }
}
