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
        [Test]
        public void MainScene_HasNoUnassignedReferences()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/Scenes/Main.unity", UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                int checkedComponents = 0;
                foreach (var go in scene.GetRootGameObjects())
                foreach (var component in go.GetComponentsInChildren<UnityEngine.MonoBehaviour>(true))
                {
                    if (component is UnityEngine.UIElements.UIDocument document)
                    {
                        Assert.NotNull(document.panelSettings, $"{go.name} UIDocument has no PanelSettings");
                        continue;
                    }
                    if (component.GetType().Namespace?.StartsWith("OpeningBell") != true) continue;
                    checkedComponents++;
                    var so = new SerializedObject(component);
                    SerializedProperty p = so.GetIterator();
                    while (p.NextVisible(true))
                        if (p.propertyType == SerializedPropertyType.ObjectReference && p.name != "m_Script")
                            Assert.IsTrue(p.objectReferenceValue != null, $"{go.name}.{component.GetType().Name}.{p.propertyPath} is unassigned");
                }
                Assert.GreaterOrEqual(checkedComponents, 2, "bootstrap and terminal");
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        private const string CatalogPath = "Assets/ScriptableObjects/Securities/SecurityCatalog.asset";
        private const string SettingsPath = "Assets/ScriptableObjects/Settings/MarketSettings.asset";

        [Test]
        public void OnboardingScenario_ApexHeadlineDrivesTheOpen()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SecurityCatalog>(CatalogPath);
            var settings = AssetDatabase.LoadAssetAtPath<MarketSettings>(SettingsPath);
            var library = AssetDatabase.LoadAssetAtPath<NewsLibrary>("Assets/ScriptableObjects/News/NewsLibrary.asset");
            var scenario = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>("Assets/ScriptableObjects/News/OnboardingScenario.asset");
            Assert.NotNull(library);
            Assert.NotNull(scenario);
            foreach (NewsTemplate t in library.Templates) t.Validate();
            Assert.GreaterOrEqual(library.Templates.Select(t => t.Type).Distinct().Count(), 15, "covers the spec's catalyst types");

            // Isolate the scripted headline: no random news in either run.
            MarketConfig config = settings.Config.Clone();
            config.SecurityNewsPerDay = config.SectorNewsPerDay = config.MarketNewsPerDay = 0;
            DateTime monday = TestMarkets.Monday;
            MarketSimulation Run(bool withScenario)
            {
                var sim = new MarketSimulation(config, catalog.CreateSpecs(), catalog.Index, new SeededRandomService(18492),
                    monday.AddHours(6), library.Templates, withScenario ? scenario.ScheduledNews : null);
                sim.AdvanceTo(monday.AddHours(10));
                return sim;
            }

            MarketSimulation control = Run(false), onboarding = Run(true);
            NewsItem headline = onboarding.News.Single();
            Assert.AreEqual(monday.AddHours(8).AddMinutes(15), headline.Time);
            StringAssert.Contains("distribution agreement", headline.Headline);
            CollectionAssert.AreEqual(new[] { "APEX" }, headline.Tickers);

            onboarding.TryGetSecurity("APEX", out var apex);
            control.TryGetSecurity("APEX", out var apexControl);
            long OpenVolume(SecurityRuntimeState s) => s.Candles.Get(Timeframe.Minute1).Completed
                .Where(c => c.Start.TimeOfDay >= new TimeSpan(9, 30, 0)).Sum(c => c.Volume);
            TestContext.WriteLine($"drawn move {headline.RealizedMove:P2}; APEX {apexControl.Last} → {apex.Last} with news; " +
                                  $"open volume {OpenVolume(apexControl)} → {OpenVolume(apex)}");
            Assert.Greater(OpenVolume(apex), OpenVolume(apexControl) * 2, "APEX is highly active at the open");
        }

        [Test]
        public void Performance_FullContentDay_SimulatesQuickly()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SecurityCatalog>(CatalogPath);
            var settings = AssetDatabase.LoadAssetAtPath<MarketSettings>(SettingsPath);
            var library = AssetDatabase.LoadAssetAtPath<NewsLibrary>("Assets/ScriptableObjects/News/NewsLibrary.asset");
            DateTime monday = TestMarkets.Monday;
            var sim = new MarketSimulation(settings.Config, catalog.CreateSpecs(), catalog.Index, new SeededRandomService(1),
                monday.AddHours(3), library.Templates);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            sim.AdvanceTo(monday.AddHours(21));
            watch.Stop();
            double perTickUs = watch.Elapsed.TotalMilliseconds * 1000 / sim.TickCount;
            TestContext.WriteLine($"{sim.TickCount} ticks × {sim.Securities.Count} stocks in {watch.ElapsedMilliseconds} ms " +
                                  $"({perTickUs:F1} µs per market tick, {sim.News.Count} headlines)");
            // At 10× speed a live game needs 5 ticks/s; a full-day skip (sleep) must stay well under a second.
            Assert.Less(watch.ElapsedMilliseconds, 3000);
        }

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
