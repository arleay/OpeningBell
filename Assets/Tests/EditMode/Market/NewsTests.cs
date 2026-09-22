using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Core;
using OpeningBell.Market;

namespace OpeningBell.Tests
{
    public class NewsTests
    {
        private static readonly DateTime Monday = TestMarkets.Monday;

        private static NewsTemplate T(string id, NewsScope scope, double bias, double severity, double uncertainty,
            string headline = "{company} news") =>
            new NewsTemplate
            {
                Id = id, Scope = scope, Headline = headline, Bias = bias,
                MinSeverity = severity, MaxSeverity = severity, Uncertainty = uncertainty,
            };

        /// <summary>No random news: only what a test schedules.</summary>
        private static MarketConfig ScheduledOnly() =>
            new MarketConfig { SecurityNewsPerDay = 0, SectorNewsPerDay = 0, MarketNewsPerDay = 0 };

        private static MarketSimulation Sim(ulong seed, MarketConfig config, NewsTemplate[] templates = null, params ScheduledNews[] scheduled) =>
            new MarketSimulation(config, TestMarkets.Basic(), new IndexSpec(), new SeededRandomService(seed), Monday.AddHours(6),
                templates, scheduled);

        private static ScheduledNews At(int minuteOfDay, string template, string ticker = "", Sector sector = default, double severity = 0) =>
            new ScheduledNews { MinuteOfDay = minuteOfDay, TemplateId = template, Ticker = ticker, Sector = sector, Severity = severity };

        [Test]
        public void ScheduledNews_PublishesOnTime_WithFormattedHeadline()
        {
            var templates = new[] { T("deal", NewsScope.Security, 1, 0.8, 0, "{company} announces major distribution agreement") };
            var sim = Sim(1, ScheduledOnly(), templates, At(8 * 60 + 15, "deal", "AAA"));
            var published = new List<NewsItem>();
            sim.NewsPublished += published.Add;

            sim.AdvanceTo(Monday.AddHours(8).AddMinutes(14));
            Assert.AreEqual(0, sim.News.Count);

            sim.AdvanceTo(Monday.AddHours(8).AddMinutes(16));
            NewsItem item = sim.News.Single();
            Assert.AreSame(item, published.Single());
            Assert.AreEqual(Monday.AddHours(8).AddMinutes(15), item.Time);
            Assert.AreEqual("AAA Corp announces major distribution agreement", item.Headline);
            CollectionAssert.AreEqual(new[] { "AAA" }, item.Tickers);
        }

        [Test]
        public void SecurityNews_MovesOnlyItsTarget_AndLiftsVolume()
        {
            var templates = new[] { T("deal", NewsScope.Security, 1, 1.0, 0) };
            var control = Sim(7, ScheduledOnly());
            var news = Sim(7, ScheduledOnly(), templates, At(8 * 60 + 15, "deal", "AAA"));
            control.AdvanceTo(Monday.AddHours(11));
            news.AdvanceTo(Monday.AddHours(11));

            for (int i = 1; i < 3; i++)
            {
                Assert.AreEqual(control.Securities[i].Last, news.Securities[i].Last, "untouched securities keep identical paths");
                Assert.AreEqual(control.Securities[i].DayVolume, news.Securities[i].DayVolume);
            }

            double move = news.News.Single().RealizedMove;
            double diff = Math.Log((double)news.Securities[0].Quote.Mid / (double)control.Securities[0].Quote.Mid);
            TestContext.WriteLine($"drawn move {move:P2}, realized price difference {diff:P2}");
            Assert.Greater(move, 0, "zero uncertainty + positive bias → positive move");
            Assert.AreEqual(move, diff, Math.Abs(move) * 0.3, "permanent move is delivered into the price");

            long openVolume(MarketSimulation s) => OpeningVolume(s.Securities[0]);
            Assert.Greater(openVolume(news), openVolume(control) * 3 / 2, "news keeps the stock active into the open");
        }

        [Test]
        public void SectorNews_MovesOnlyThatSector()
        {
            var templates = new[] { T("sector", NewsScope.Sector, 1, 1.0, 0, "{sector} rallies") };
            var control = Sim(3, ScheduledOnly());
            var news = Sim(3, ScheduledOnly(), templates, At(10 * 60, "sector", sector: Sector.Technology));
            control.AdvanceTo(Monday.AddHours(12));
            news.AdvanceTo(Monday.AddHours(12));

            Assert.AreEqual("Technology rallies", news.News.Single().Headline);
            Assert.AreNotEqual(control.Securities[0].Last, news.Securities[0].Last, "AAA is Technology");
            Assert.AreEqual(control.Securities[1].Last, news.Securities[1].Last, "BBB is Energy");
            Assert.AreEqual(control.Securities[2].Last, news.Securities[2].Last, "CCC is Healthcare");
        }

        [Test]
        public void FlavourNews_HasNoMarketEffect()
        {
            var templates = new[] { T("conference", NewsScope.Security, 0, 0, 0) };
            var control = Sim(5, ScheduledOnly());
            var news = Sim(5, ScheduledOnly(), templates, At(10 * 60, "conference", "AAA"));
            control.AdvanceTo(Monday.AddHours(14));
            news.AdvanceTo(Monday.AddHours(14));

            Assert.AreEqual(1, news.News.Count);
            Assert.AreEqual(control.Securities[0].Last, news.Securities[0].Last);
            Assert.AreEqual(control.Securities[0].DayVolume, news.Securities[0].DayVolume);
        }

        [Test]
        public void MarketNews_MovesIndexAndStocksByBeta()
        {
            var templates = new[] { T("jobs", NewsScope.Market, 1, 1.0, 0, "Jobs beat") };
            var control = Sim(2, ScheduledOnly());
            var news = Sim(2, ScheduledOnly(), templates, At(10 * 60, "jobs"));
            control.AdvanceTo(Monday.AddHours(12));
            news.AdvanceTo(Monday.AddHours(12));

            double move = news.News.Single().RealizedMove;
            double indexDiff = Math.Log((double)news.Index.Level / (double)control.Index.Level);
            Assert.AreEqual(move, indexDiff, 1e-3, "index takes the full market move");
            Assert.IsEmpty(news.News.Single().Tickers);
        }

        [Test]
        public void MarketNews_SectorTilt_MovesThatSectorAgainstTheMarket()
        {
            // A war threat: the market sells off, energy rallies. Same seed with and without the tilt.
            NewsTemplate plain = T("threat", NewsScope.Market, -1, 1.0, 0, "Leader threatens strikes");
            NewsTemplate tilted = T("threat", NewsScope.Market, -1, 1.0, 0, "Leader threatens strikes");
            tilted.Tilts.Add(new SectorTilt { Sector = Sector.Energy, Bias = 1 });
            var control = Sim(9, ScheduledOnly(), new[] { plain }, At(10 * 60, "threat"));
            var news = Sim(9, ScheduledOnly(), new[] { tilted }, At(10 * 60, "threat"));
            control.AdvanceTo(Monday.AddHours(12));
            news.AdvanceTo(Monday.AddHours(12));

            Assert.Less(news.News.Single().RealizedMove, 0, "the market move is down");
            CollectionAssert.AreEqual(new[] { "BBB" }, news.News.Single().Tickers, "the tilted sector is tagged");
            Assert.Greater(news.Securities[1].Last, control.Securities[1].Last, "BBB (Energy) ends higher with the tilt");
            Assert.AreEqual(control.Securities[0].Last, news.Securities[0].Last, "AAA (Technology) is untouched by the tilt");
            Assert.AreEqual(control.Index.Level, news.Index.Level, "the index takes only the market move");
        }

        [Test]
        public void MarketNews_OnlyMarketScopeMayTilt()
        {
            NewsTemplate bad = T("x", NewsScope.Sector, 1, 0.5, 0);
            bad.Tilts.Add(new SectorTilt { Sector = Sector.Energy, Bias = 1 });
            Assert.Throws<ArgumentException>(bad.Validate);
        }

        [Test]
        public void RandomNews_IsDeterministic_AndNearTheConfiguredRate()
        {
            var templates = new[]
            {
                T("up", NewsScope.Security, 1, 0.5, 0.5),
                T("down", NewsScope.Security, -1, 0.5, 0.5),
                T("sector", NewsScope.Sector, 1, 0.5, 0.5, "{sector} news"),
                T("macro", NewsScope.Market, -1, 0.5, 0.5, "Macro news"),
            };
            var config = TestMarkets.FastConfig();
            var a = new MarketSimulation(config, TestMarkets.Basic(), new IndexSpec(), new SeededRandomService(11), Monday.AddHours(3), templates);
            var b = new MarketSimulation(config, TestMarkets.Basic(), new IndexSpec(), new SeededRandomService(11), Monday.AddHours(3), templates);
            a.AdvanceTo(Monday.AddDays(28));
            b.AdvanceTo(Monday.AddDays(28));

            CollectionAssert.AreEqual(a.News.Select(n => n.Time + n.Headline), b.News.Select(n => n.Time + n.Headline));
            double perDay = a.News.Count / 20.0;
            TestContext.WriteLine($"{a.News.Count} headlines over 20 trading days ({perDay:F1}/day)");
            Assert.That(perDay, Is.InRange(3.5, 8.5), "configured ≈ 5.8/day");
            Assert.IsTrue(a.News.All(n => n.Time.DayOfWeek != DayOfWeek.Saturday && n.Time.DayOfWeek != DayOfWeek.Sunday));
        }

        [Test]
        public void Reactions_AreLikelyButNeverGuaranteed()
        {
            var template = T("good", NewsScope.Security, 1, 0.6, 0.5);
            var sim = Sim(1, ScheduledOnly(), new[] { template });
            var engine = new NewsEngine(ScheduledOnly(), sim.Schedule, new SeededRandom(99), new[] { template },
                null, sim.Securities, Monday.AddHours(6));

            var draws = Enumerable.Range(0, 4000).Select(_ => engine.DrawReaction(template, 0.6, 0.03)).ToList();
            double negative = draws.Count(d => d < 0) / (double)draws.Count;
            TestContext.WriteLine($"positive-bias headline fell {negative:P1} of the time; mean move {draws.Average():P2}");
            Assert.Greater(draws.Average(), 0);
            Assert.That(negative, Is.InRange(0.02, 0.3));
        }

        private static long OpeningVolume(SecurityRuntimeState s)
        {
            long total = 0;
            foreach (Candle c in s.Candles.Get(Timeframe.Minute1).Completed)
                if (c.Start.TimeOfDay >= new TimeSpan(9, 30, 0) && c.Start.TimeOfDay < new TimeSpan(10, 0, 0))
                    total += c.Volume;
            return total;
        }
    }
}
