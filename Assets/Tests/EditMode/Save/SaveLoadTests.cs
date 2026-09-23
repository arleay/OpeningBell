using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Core;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine;

namespace OpeningBell.Tests
{
    public class SaveLoadTests
    {
        private static readonly DateTime Monday = TestMarkets.Monday;
        private const ulong Seed = 4242;

        private sealed class Game
        {
            public MarketSimulation Market;
            public Account Account;
            public OrderManager Orders;
            public TradingDayRecorder Days;
        }

        private static readonly NewsTemplate[] Templates =
        {
            new NewsTemplate { Id = "up", Scope = NewsScope.Security, Headline = "{company} up news", Bias = 1, MinSeverity = 0.3, MaxSeverity = 0.8, Uncertainty = 0.5 },
            new NewsTemplate { Id = "down", Scope = NewsScope.Security, Headline = "{company} down news", Bias = -1, MinSeverity = 0.3, MaxSeverity = 0.8, Uncertainty = 0.5 },
            new NewsTemplate { Id = "macro", Scope = NewsScope.Market, Headline = "Macro news", Bias = 0.5, MinSeverity = 0.3, MaxSeverity = 0.6, Uncertainty = 0.6 },
        };

        // Wednesday 8:15 relative to the Monday start: still queued when we save on Tuesday.
        private static readonly ScheduledNews[] Scheduled =
        {
            new ScheduledNews { DayOffset = 2, MinuteOfDay = 8 * 60 + 15, TemplateId = "up", Ticker = "AAA", Severity = 0.9 },
        };

        private static Game Build(DateTime start, bool deposit)
        {
            var g = new Game
            {
                Market = new MarketSimulation(new MarketConfig(), TestMarkets.Basic(), new IndexSpec(), new SeededRandomService(Seed),
                    start, Templates, Scheduled),
            };
            g.Account = new Account(g.Market);
            g.Orders = new OrderManager(g.Market, g.Account, new BrokerRules());
            if (deposit) g.Account.Deposit(10_000m);
            g.Days = new TradingDayRecorder(g.Market, g.Account, g.Orders);
            return g;
        }

        [Test]
        public void RandomState_RoundTrips_IncludingCachedGaussian()
        {
            var a = new SeededRandom(7);
            a.NextGaussian(); // leaves a cached spare
            var b = new SeededRandom(1);
            b.RestoreState(a.CaptureState());
            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(a.NextGaussian(), b.NextGaussian());
                Assert.AreEqual(a.NextULong(), b.NextULong());
            }
        }

        [Test]
        public void SavedGame_ResumesExactly_ThroughJson()
        {
            Game original = Build(Monday.AddHours(6), deposit: true);
            original.Market.AdvanceTo(Monday.AddHours(10));
            original.Orders.SubmitMarket("AAA", OrderSide.Buy, 2);
            original.Orders.SubmitLimit("BBB", OrderSide.Buy, 1, PriceTick.RoundDown(original.Market.Securities[1].Bid * 0.99m, 0.01m));

            DateTime saveTime = Monday.AddDays(1).AddHours(10).AddMinutes(17).AddSeconds(36);
            original.Market.AdvanceTo(saveTime);
            Order restingAtSave = original.Orders.SubmitLimit("CCC", OrderSide.Buy, 1, PriceTick.RoundDown(original.Market.Securities[2].Bid * 0.98m, 0.01m));
            Assert.IsTrue(restingAtSave.IsOpen, "an open order is part of the save");

            var save = new SaveGame
            {
                Seed = unchecked((long)Seed),
                Clock = saveTime.Ticks,
                Market = original.Market.CaptureState(),
                Trading = TradingState.Capture(original.Account, original.Orders, original.Days),
            };
            string json = JsonUtility.ToJson(save);
            SaveGame loaded = JsonUtility.FromJson<SaveGame>(json);
            TestContext.WriteLine($"save size {json.Length / 1024} KB");

            Game resumed = Build(new DateTime(loaded.Clock), deposit: false);
            resumed.Market.RestoreState(loaded.Market);
            TradingState.Restore(loaded.Trading, resumed.Account, resumed.Orders, resumed.Days);

            AssertSameGame(original, resumed, "right after loading");
            Assert.AreEqual(original.Account.BuyingPower, resumed.Account.BuyingPower, "open-order reservations restored");

            // Play both forward identically: through Wednesday's scheduled headline and into Thursday.
            foreach (Game g in new[] { original, resumed })
            {
                g.Market.AdvanceTo(Monday.AddDays(2).AddHours(11));
                g.Orders.SubmitMarket("AAA", OrderSide.Sell, 100);
                g.Market.AdvanceTo(Monday.AddDays(3).AddHours(12));
            }

            AssertSameGame(original, resumed, "two days after loading");
            Assert.AreEqual(1, resumed.Market.News.Count(n => n.Headline == "AAA Corp up news" && n.Time == Monday.AddDays(2).AddHours(8).AddMinutes(15)),
                "scheduled headline published once, on time");
        }

        private static void AssertSameGame(Game a, Game b, string when)
        {
            Assert.AreEqual(a.Market.Now, b.Market.Now, when);
            Assert.AreEqual(a.Market.TickCount, b.Market.TickCount, when);
            Assert.AreEqual(a.Market.Session, b.Market.Session, when);
            Assert.AreEqual(a.Market.Index.Level, b.Market.Index.Level, when);

            for (int i = 0; i < a.Market.Securities.Count; i++)
            {
                SecurityRuntimeState x = a.Market.Securities[i], y = b.Market.Securities[i];
                string ctx = $"{x.Ticker} {when}";
                Assert.AreEqual(x.Quote.Bid, y.Quote.Bid, ctx);
                Assert.AreEqual(x.Quote.Ask, y.Quote.Ask, ctx);
                Assert.AreEqual(x.Quote.Last, y.Quote.Last, ctx);
                Assert.AreEqual(x.Quote.AskSize, y.Quote.AskSize, ctx);
                Assert.AreEqual(x.DayVolume, y.DayVolume, ctx);
                Assert.AreEqual(x.Vwap, y.Vwap, ctx);
                Assert.AreEqual(x.PreviousClose, y.PreviousClose, ctx);
                AssertSameTail(x.Candles.Get(Timeframe.Minute1), y.Candles.Get(Timeframe.Minute1), 120, ctx + " 1m");
                AssertSameTail(x.Candles.Get(Timeframe.Minute5), y.Candles.Get(Timeframe.Minute5), 30, ctx + " 5m");
                AssertSameTail(x.Candles.Get(Timeframe.Hour1), y.Candles.Get(Timeframe.Hour1), 5, ctx + " 1h");
                AssertSameTail(x.Candles.Get(Timeframe.Day1), y.Candles.Get(Timeframe.Day1), int.MaxValue, ctx + " 1D");
            }

            CollectionAssert.AreEqual(a.Market.News.Select(n => $"{n.Id} {n.Time:O} {n.Headline}"),
                b.Market.News.Select(n => $"{n.Id} {n.Time:O} {n.Headline}"), when);

            Assert.AreEqual(a.Account.Cash, b.Account.Cash, when);
            Assert.AreEqual(a.Account.Equity, b.Account.Equity, when);
            Assert.AreEqual(a.Account.RealizedPnL, b.Account.RealizedPnL, when);
            Assert.AreEqual(a.Account.TotalCommissions, b.Account.TotalCommissions, when);
            Assert.AreEqual(a.Account.DailyPnL, b.Account.DailyPnL, when);
            Assert.AreEqual(a.Account.Ledger.Entries.Count, b.Account.Ledger.Entries.Count, when);
            CollectionAssert.AreEqual(a.Account.Portfolio.Positions.Select(p => $"{p.Ticker} {p.Quantity} {p.CostBasis}"),
                b.Account.Portfolio.Positions.Select(p => $"{p.Ticker} {p.Quantity} {p.CostBasis}"), when);
            CollectionAssert.AreEqual(a.Orders.Orders.Select(o => $"{o.Id} {o.Status} {o.FilledQuantity}"),
                b.Orders.Orders.Select(o => $"{o.Id} {o.Status} {o.FilledQuantity}"), when);
            Assert.AreEqual(a.Orders.Fills.Count, b.Orders.Fills.Count, when);
            CollectionAssert.AreEqual(a.Days.Completed.Select(r => $"{r.DayNumber} {r.NetPnL} {r.Fills}"),
                b.Days.Completed.Select(r => $"{r.DayNumber} {r.NetPnL} {r.Fills}"), when);
            Assert.AreEqual(a.Days.DayNumber, b.Days.DayNumber, when);
        }

        private static void AssertSameTail(CandleSeries a, CandleSeries b, int count, string ctx)
        {
            int n = Math.Min(count, Math.Min(a.Count, b.Count));
            Assert.Greater(n, 0, ctx);
            for (int i = 1; i <= n; i++)
            {
                // Compare values, not strings: a loaded decimal may print with a different scale (104794.5 vs 104794.50).
                Candle x = a[a.Count - i], y = b[b.Count - i];
                Assert.IsTrue(x.Start == y.Start && x.Open == y.Open && x.High == y.High && x.Low == y.Low &&
                              x.Close == y.Close && x.Volume == y.Volume && x.Notional == y.Notional,
                    $"{ctx}: {x.Start:O} {x.Open}/{x.High}/{x.Low}/{x.Close} {x.Volume} {x.Notional} vs " +
                    $"{y.Start:O} {y.Open}/{y.High}/{y.Low}/{y.Close} {y.Volume} {y.Notional}");
            }
        }

        [Test]
        public void SaveFiles_AreAtomic_KeepABackup_AndRefuseNewerVersions()
        {
            string dir = Path.Combine(Path.GetTempPath(), "OpeningBellSaveTest_" + Guid.NewGuid().ToString("N"));
            SaveSystem.DirectoryOverride = dir;
            try
            {
                Assert.IsFalse(SaveSystem.TryRead("slot", out _, out string missing));
                Assert.IsNull(missing, "a missing save is not an error");

                SaveSystem.Write(new SaveGame { Clock = 1 }, "slot");
                SaveSystem.Write(new SaveGame { Clock = 2 }, "slot");
                Assert.IsTrue(File.Exists(SaveSystem.SlotPath("slot") + ".bak"));
                Assert.IsTrue(SaveSystem.TryRead("slot", out SaveGame latest, out _));
                Assert.AreEqual(2, latest.Clock);

                File.WriteAllText(SaveSystem.SlotPath("slot"), "{ not json");
                Assert.IsTrue(SaveSystem.TryRead("slot", out SaveGame fallback, out string warning), "falls back to the backup");
                Assert.AreEqual(1, fallback.Clock);
                StringAssert.Contains("backup", warning);

                File.WriteAllText(SaveSystem.SlotPath("slot"), "{\"Version\":99}");
                File.Delete(SaveSystem.SlotPath("slot") + ".bak");
                Assert.IsFalse(SaveSystem.TryRead("slot", out _, out string newer));
                StringAssert.Contains("newer version", newer);
            }
            finally
            {
                SaveSystem.DirectoryOverride = null;
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
