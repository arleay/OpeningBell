using System;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Core;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEditor;

namespace OpeningBell.Tests
{
    /// <summary>End-to-end against the real simulation: market → quotes → orders → fills → account.</summary>
    public class TradingIntegrationTests
    {
        private static readonly DateTime Monday = TestMarkets.Monday;

        [Test]
        public void RoundTripOnLiveMarket_ReconcilesExactly()
        {
            var sim = TestMarkets.Create(11, Monday.AddHours(9.4), TestMarkets.Basic());
            var account = new Account(sim);
            account.Deposit(10_000m);
            var orders = new OrderManager(sim, account, new BrokerRules());

            sim.AdvanceTo(Monday.AddHours(9).AddMinutes(40));
            Assert.AreEqual(MarketSession.Regular, sim.Session);

            sim.TryGetQuote("AAA", out Quote entry);
            Order buy = orders.SubmitMarket("AAA", OrderSide.Buy, 100);
            Assert.AreEqual(OrderStatus.Filled, buy.Status);
            Assert.AreEqual(entry.Ask, buy.Fills[0].Price);

            Order farBid = orders.SubmitLimit("AAA", OrderSide.Buy, 10, PriceTick.RoundDown(entry.Bid * 0.5m, 0.01m));

            sim.AdvanceTo(Monday.AddHours(11));
            sim.TryGetQuote("AAA", out Quote exit);
            Order sell = orders.SubmitMarket("AAA", OrderSide.Sell, 100);
            Assert.AreEqual(OrderStatus.Filled, sell.Status);
            Assert.AreEqual(exit.Bid, sell.Fills[0].Price);
            Assert.AreEqual(100 * (exit.Bid - entry.Ask), account.RealizedPnL);

            decimal nearLimit = exit.Bid - 0.05m;
            Order nearBid = orders.SubmitLimit("AAA", OrderSide.Buy, 50, nearLimit);

            sim.AdvanceTo(Monday.AddHours(21));

            Assert.IsTrue(nearBid.Fills.All(f => f.Price <= nearLimit), "limit buy never fills above its limit");
            Assert.AreEqual(OrderStatus.Cancelled, farBid.Status);
            Assert.AreEqual(0, farBid.FilledQuantity);
            Assert.AreEqual(0, orders.OpenOrders.Count);
            Assert.AreEqual(account.Cash, account.BuyingPower);

            decimal ledgerSum = account.Ledger.Entries.Sum(e => e.Amount);
            Assert.AreEqual(ledgerSum, account.Cash);
            Assert.AreEqual(account.Equity - account.NetDeposits,
                account.RealizedPnL + account.UnrealizedPnL - account.TotalCommissions);
            TestContext.WriteLine($"near-bid order: {nearBid.Status}, filled {nearBid.FilledQuantity} @ {nearBid.AverageFillPrice}");
        }
    }

    /// <summary>Guards the shipped content assets.</summary>
    public class DefaultContentTests
    {
        private const string CatalogPath = "Assets/ScriptableObjects/Securities/SecurityCatalog.asset";
        private const string SettingsPath = "Assets/ScriptableObjects/Settings/MarketSettings.asset";

        [Test]
        public void DefaultUniverse_IsValid_AndSimulatesAFullDay()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SecurityCatalog>(CatalogPath);
            var settings = AssetDatabase.LoadAssetAtPath<MarketSettings>(SettingsPath);
            Assert.NotNull(catalog, CatalogPath);
            Assert.NotNull(settings, SettingsPath);

            var specs = catalog.CreateSpecs();
            CollectionAssert.AreEquivalent(
                new[] { "BLZE", "NVRA", "OCEA", "APEX", "CYRA", "VSTA", "NRTK", "GLXY", "MTRX", "SOLR" },
                specs.Select(s => s.Ticker));
            double apexBase = specs.First(s => s.Ticker == "APEX").BasePrice;

            DateTime monday = TestMarkets.Monday;
            var sim = new MarketSimulation(settings.Config, specs, catalog.Index, new SeededRandomService(18492), monday.AddHours(6));
            sim.AdvanceTo(monday.AddHours(20));

            foreach (var sec in sim.Securities)
            {
                Candle day = sec.Candles.Get(Timeframe.Day1)[0];
                TestContext.WriteLine(
                    $"{sec.Ticker,-5} {sec.PreviousClose,8:F2} → {sec.Last,8:F2} ({sec.ChangePercent,6:F2}%)  " +
                    $"range {(day.High - day.Low) / day.Open * 100m,5:F2}%  vol {sec.DayVolume / (double)sec.Spec.AverageDailyVolume,5:P0} ADV  " +
                    $"spread {sec.Ask - sec.Bid:F4}");
                Assert.Greater(sec.Last, 0m);
                Assert.Greater(sec.DayVolume, 0);
            }
            TestContext.WriteLine($"{sim.Index.Ticker} {sim.Index.PreviousClose:F2} → {sim.Index.Level:F2} ({sim.Index.ChangePercent:F2}%)");

            Assert.AreEqual(apexBase, catalog.Securities.First(d => d.Spec.Ticker == "APEX").Spec.BasePrice,
                "running the market must not modify definition assets");
        }
    }
}
