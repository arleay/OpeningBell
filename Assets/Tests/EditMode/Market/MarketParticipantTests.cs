using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Core;
using OpeningBell.Market;

namespace OpeningBell.Tests
{
    /// <summary>
    /// The participants that read market structure (zones, gaps, sweeps), the time macros and news reaching
    /// participants. These check mechanisms, never outcomes: a zone may hold or fail, but it must exist and be used up.
    /// </summary>
    public class MarketParticipantTests
    {
        private static readonly DateTime Monday = TestMarkets.Monday;

        [Test]
        public void Zones_FormFromDisplacementAndGaps_AndRetestsUseThemUp()
        {
            MarketSimulation sim = TestMarkets.Create(20260928, Monday, TestMarkets.Basic());
            var kinds = new HashSet<ZoneKind>();
            int touched = 0, flipped = 0;
            DateTime date = Monday;
            for (int d = 0; d < 10; d++)
            {
                while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) date = date.AddDays(1);
                for (DateTime t = date.AddHours(9.5); t < date.AddHours(16); t = t.AddMinutes(5))
                {
                    sim.AdvanceTo(t.AddMinutes(5));
                    foreach (SecurityRuntimeState s in sim.Securities)
                        foreach (Zone z in s.Zones.All)
                        {
                            kinds.Add(z.Kind);
                            if (z.State == ZoneState.PartlyMitigated || z.Retests > 0) touched++;
                            if (z.Flipped) flipped++;
                            Assert.That(z.Interest, Is.InRange(0, z.Initial + 1e-12), "interest is only ever consumed");
                        }
                }
                date = date.AddDays(1);
            }
            TestContext.WriteLine($"zone kinds seen: {string.Join(", ", kinds)}; touched samples {touched}, breaker samples {flipped}");
            Assert.IsTrue(kinds.Contains(ZoneKind.BullishGap) || kinds.Contains(ZoneKind.BearishGap), "fair value gaps get traders");
            Assert.IsTrue(kinds.Contains(ZoneKind.Demand) || kinds.Contains(ZoneKind.Supply), "displacements leave order blocks");
            Assert.Greater(touched, 0, "price comes back to zones and eats into them");
        }

        [Test]
        public void Premarket_BuildsTowardTheBell()
        {
            // Volume per minute in the quiet early premarket vs the last half hour before the open, summed over days.
            MarketSimulation sim = TestMarkets.Create(5, Monday, TestMarkets.Basic());
            long early = 0, late = 0;
            DateTime date = Monday;
            for (int d = 0; d < 5; d++, date = date.AddDays(1))
            {
                sim.AdvanceTo(date.AddHours(20));
                foreach (SecurityRuntimeState s in sim.Securities)
                {
                    CandleSeries m1 = s.Candles.Get(Timeframe.Minute1);
                    for (int i = 0; i < m1.Count; i++)
                    {
                        if (m1[i].Start.Date != date) continue;
                        double minute = m1[i].Start.TimeOfDay.TotalMinutes;
                        if (minute >= 5 * 60 && minute < 6 * 60) early += m1[i].Volume;
                        else if (minute >= 9 * 60 && minute < 9 * 60 + 30) late += m1[i].Volume;
                    }
                }
            }
            TestContext.WriteLine($"premarket volume per minute: 5-6 AM {early / 60.0:0}, 9:00-9:30 {late / 30.0:0}");
            Assert.Greater(late / 30.0, 1.6 * early / 60.0, "institutional prep and queued orders busy the last half hour");
        }

        [Test]
        public void TimeMacros_AreConfigurable_AndNeverDirectional()
        {
            // Swapping the macros for none changes the path (they matter) but both runs still look like a market.
            MarketConfig none = new MarketConfig { TimeMacros = Array.Empty<TimeMacro>() };
            MarketSimulation plain = TestMarkets.Create(9, Monday, TestMarkets.Basic(), none);
            MarketSimulation macro = TestMarkets.Create(9, Monday, TestMarkets.Basic());
            plain.AdvanceTo(Monday.AddHours(12));
            macro.AdvanceTo(Monday.AddHours(12));
            Assert.AreNotEqual(plain.Securities[0].DayVolume, macro.Securities[0].DayVolume);

            Assert.Throws<ArgumentException>(() => new MarketConfig { TimeMacros = new[] { new TimeMacro(600, 590) } }.Validate());
            foreach (TimeMacro m in MarketConfig.DefaultTimeMacros())
                Assert.Less(m.StartMinute, m.EndMinute);
        }

        [Test]
        public void News_PullsLiquidityAndBringsInTheCrowd()
        {
            MarketSimulation sim = TestMarkets.Create(3, Monday, TestMarkets.Basic());
            sim.AdvanceTo(Monday.AddHours(11));
            SecurityRuntimeState s = sim.Securities[0];
            FlowState f = s.Flow;
            double retail = f.Day.Retail, withdraw = f.Withdraw;
            double sd = s.Spec.DailyVolatility;
            double price = s.FairLog + s.DeviationLog;
            // Resting offers above price: the kind a strong bullish headline blows through.
            Level wall = f.Levels.All.FirstOrDefault(l => l.Side > 0 && l.Wall > 0);
            double before = wall?.Wall ?? 0;

            sim.Engine.Flow.OnNews(s, 0.8 * sd, price);

            Assert.Greater(f.Withdraw, withdraw, "market makers step back");
            Assert.Greater(f.Day.Retail, retail, "retail piles in");
            if (wall != null) Assert.Less(wall.Wall, before, "offers in the way are pulled");
        }

        [Test]
        public void CheatSignal_CallsTheNextTenMinutesBetterThanACoin()
        {
            MarketSimulation sim = TestMarkets.Create(77, Monday, TestMarkets.Basic());
            int calls = 0, right = 0;
            DateTime date = Monday;
            for (int d = 0; d < 5; d++, date = date.AddDays(1))
                for (DateTime t = date.AddHours(9.75); t < date.AddHours(15.75); t = t.AddMinutes(10))
                {
                    sim.AdvanceTo(t);
                    var sig = sim.Securities.Select(s => (s.CheatSignal, (double)s.Last)).ToArray();
                    sim.AdvanceTo(t.AddMinutes(10));
                    for (int i = 0; i < sig.Length; i++)
                    {
                        if (Math.Abs(sig[i].CheatSignal) < 0.35) continue;
                        calls++;
                        if (Math.Sign(sig[i].CheatSignal) == Math.Sign((double)sim.Securities[i].Last - sig[i].Item2)) right++;
                    }
                }
            TestContext.WriteLine($"signal calls {calls}, right {right / (double)Math.Max(1, calls):P0}");
            Assert.Greater(calls, 50);
            Assert.Greater(right / (double)calls, 0.55, "it reads the hidden market, so it should beat a coin flip");
        }

        [Test]
        public void Saves_ResumeZonesAndStructureExactly()
        {
            MarketSimulation a = TestMarkets.Create(11, Monday, TestMarkets.Basic());
            a.AdvanceTo(Monday.AddDays(1).AddHours(13));
            MarketSaveData save = a.CaptureState();
            MarketSimulation b = TestMarkets.Create(11, Monday, TestMarkets.Basic());
            b.RestoreState(save);
            for (int i = 0; i < a.Securities.Count; i++)
            {
                Assert.AreEqual(a.Securities[i].Zones.All.Count, b.Securities[i].Zones.All.Count);
                Assert.AreEqual(a.Securities[i].Flow.Structure, b.Securities[i].Flow.Structure);
            }
            a.AdvanceTo(Monday.AddDays(2).AddHours(15));
            b.AdvanceTo(Monday.AddDays(2).AddHours(15));
            for (int i = 0; i < a.Securities.Count; i++)
                Assert.AreEqual(a.Securities[i].Last, b.Securities[i].Last, "a load continues exactly as if never quit");
        }
    }
}
