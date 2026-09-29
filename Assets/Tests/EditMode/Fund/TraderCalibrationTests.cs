using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Fund;
using OpeningBell.Market;

namespace OpeningBell.Tests
{
    /// <summary>
    /// How skill turns into results in the live market (FUND_SPEC §11). Traders identical except for one skill trade the
    /// same sessions; their recorded win rates are compared. Long: explicit, run on demand while tuning.
    /// </summary>
    public class TraderCalibrationTests
    {
        private static SecuritySpec[] Universe() => new[]
        {
            TestMarkets.Spec("TEC1", Sector.Technology, 140, vol: 0.031),
            TestMarkets.Spec("TEC2", Sector.Technology, 24, vol: 0.049),
            TestMarkets.Spec("TEC3", Sector.Technology, 67, vol: 0.039),
            TestMarkets.Spec("ENR1", Sector.Energy, 54, vol: 0.025),
            TestMarkets.Spec("ENR2", Sector.Energy, 18, vol: 0.053),
            TestMarkets.Spec("IND1", Sector.Industrials, 87, vol: 0.021),
            TestMarkets.Spec("CON1", Sector.Consumer, 31, vol: 0.028),
        };

        /// <summary>Company headlines of both signs and a mixed one, so news traders have something to trade.</summary>
        internal static NewsTemplate[] News() => new[]
        {
            new NewsTemplate { Id = "beat", Scope = NewsScope.Security, Headline = "{company} beats estimates", Bias = 1, MinSeverity = 0.3, MaxSeverity = 1, Uncertainty = 0.3 },
            new NewsTemplate { Id = "miss", Scope = NewsScope.Security, Headline = "{company} misses estimates", Bias = -1, MinSeverity = 0.3, MaxSeverity = 1, Uncertainty = 0.3 },
            new NewsTemplate { Id = "mixed", Scope = NewsScope.Security, Headline = "{company} guidance mixed", Bias = 0, MinSeverity = 0.2, MaxSeverity = 0.7, Uncertainty = 1 },
        };

        /// <summary>Runs <paramref name="days"/> sessions on each seed with one trader per profile; returns closed trades per profile.</summary>
        internal static Dictionary<string, List<TradeRecord>> Run(int seeds, int days, Dictionary<string, Action<Person>> profiles, Strategy strategy)
        {
            var result = profiles.Keys.ToDictionary(k => k, k => new List<TradeRecord>());
            for (int s = 0; s < seeds; s++)
            {
                var rig = new FundRig(bank: 5_000_000m, seed: (ulong)(100 + s), specs: Universe(), news: News());
                rig.Register(extra: 3_000_000m);
                var staff = new List<(string, Employee)>();
                int i = 0;
                foreach (var kv in profiles)
                {
                    Employee e = rig.Hire(level: 0.5, strategy: strategy);
                    Person p = e.Person;
                    for (int k = 0; k < Person.SkillCount; k++) p.Skills[k] = 55;
                    for (int k = 0; k < Person.TraitCount; k++) p.Traits[k] = 50;
                    p.TradesPerDay = 4;
                    kv.Value(p);
                    rig.Fund.Assign(e, rig.Station(i * 3, 0).Uid);
                    rig.Fund.Allocate(e, 50_000m);
                    e.Policy.MaxRiskPerTrade = 600m;
                    e.Policy.MaxDailyLoss = 5_000m;
                    e.Policy.MaxContracts = 20;
                    e.Policy.MaxPositions = 2;
                    staff.Add((kv.Key, e));
                    i++;
                }
                rig.Fund.Config.CompanyMaxPerSymbol = 1000;
                rig.Fund.Config.CompanyMaxPerSector = 1000;
                DateTime t = TestMarkets.Monday;
                int run = 0;
                while (run < days)
                {
                    t = t.AddDays(1);
                    if (!rig.Market.Schedule.IsTradingDay(t)) continue;
                    run++;
                    rig.RunTo(t.AddHours(18));
                }
                foreach (var (name, e) in staff) result[name].AddRange(e.Closed());
            }
            return result;
        }

        internal static string Describe(string name, List<TradeRecord> t)
        {
            if (t.Count == 0) return $"{name}: no trades";
            double win = t.Count(x => x.Net > 0m) / (double)t.Count;
            decimal net = t.Sum(x => x.Net);
            var wins = t.Where(x => x.Net > 0m).ToList();
            var losses = t.Where(x => x.Net < 0m).ToList();
            double r = t.Where(x => x.Risk > 0m).Select(x => x.R).DefaultIfEmpty().Average();
            return $"{name}: n={t.Count} win={win:P1} net={net:N0} avgWin={(wins.Count > 0 ? wins.Average(x => x.Net) : 0):N0} avgLoss={(losses.Count > 0 ? losses.Average(x => x.Net) : 0):N0} avgR={r:+0.00;-0.00} plan={t.Count(x => x.FollowedPlan) / (double)t.Count:P0} avgQ={t.Average(x => x.Quality):0.00} lowQ={t.Count(x => x.Quality < 0.5) / (double)t.Count:P0} early={t.Count(x => (x.Notes ?? "").Contains("early")) / (double)t.Count:P0} time={t.Count(x => (x.Notes ?? "").Contains("Time stop")) / (double)t.Count:P0}";
        }

        [Test, Explicit("Long statistical run for tuning")]
        public void Analysis_ShiftsTheRecordedWinRate([Values(Strategy.Breakout, Strategy.Reversion, Strategy.TrendPullback, Strategy.Scalping, Strategy.NewsMomentum)] Strategy strategy)
        {
            var profiles = new Dictionary<string, Action<Person>>
            {
                ["analysis 25"] = p => p.Skills[(int)Skill.Analysis] = 25,
                ["analysis 45"] = p => p.Skills[(int)Skill.Analysis] = 45,
                ["analysis 60"] = p => p.Skills[(int)Skill.Analysis] = 60,
                ["analysis 85"] = p => p.Skills[(int)Skill.Analysis] = 85,
            };
            var r = Run(seeds: 4, days: 15, profiles, strategy);
            foreach (var kv in r) TestContext.WriteLine(strategy + " " + Describe(kv.Key, kv.Value));
        }

        /// <summary>
        /// Does a setup's quality (and each ingredient) predict how it resolves? Every setup the detectors find is played
        /// out on the following 1-minute bars (stop first when a bar spans both), with no trader in between.
        /// </summary>
        [Test, Explicit("Long statistical run for tuning")]
        public void SetupEdge([Values(Strategy.Breakout, Strategy.Reversion, Strategy.TrendPullback, Strategy.Scalping, Strategy.NewsMomentum)] Strategy strategy) =>
            Edge(strategy, TestMarkets.FastConfig(), 5);

        /// <summary>The same at the game's own 2-second tick (slow): does the coarse test tick change the edge?</summary>
        [Test, Explicit("Long statistical run for tuning")]
        public void SetupEdge_GameTick([Values(Strategy.Breakout, Strategy.Scalping)] Strategy strategy) => Edge(strategy, new MarketConfig(), 2);

        private static void Edge(Strategy strategy, MarketConfig config, int seeds)
        {
            var rows = new List<(double q, double[] x, double r)>();
            string[] names = { "quality", "trendiness", "volRatio", "sZ5", "sZ15", "sTrend5", "sVwap", "minutes", "sFromOpen", "sStructure" };
            for (int seed = 0; seed < seeds; seed++)
            {
                var m = new MarketSimulation(config, Universe(), new IndexSpec(), new OpeningBell.Core.SeededRandomService((ulong)(300 + seed)), TestMarkets.Monday.AddHours(5), News());
                var open = new List<(string t, Setup s, double[] x, double q)>();
                DateTime day = TestMarkets.Monday;
                for (int d = 0; d < 20; d++, day = day.AddDays(1))
                {
                    if (!m.Schedule.IsTradingDay(day)) { d--; continue; }
                    DateTime t0 = day + m.Schedule.RegularOpen;
                    for (int min = 1; min <= 386; min++)
                    {
                        m.AdvanceTo(t0.AddMinutes(min));
                        foreach (SecurityRuntimeState sec in m.Securities)
                        {
                            Candle bar = sec.Candles.Get(Timeframe.Minute1).Completed[^1];
                            for (int i = open.Count - 1; i >= 0; i--)
                            {
                                var o = open[i];
                                if (o.t != sec.Ticker) continue;
                                bool stop = o.s.Side > 0 ? (double)bar.Low <= o.s.Stop : (double)bar.High >= o.s.Stop;
                                bool hit = o.s.Side > 0 ? (double)bar.High >= o.s.Target : (double)bar.Low <= o.s.Target;
                                double r = stop ? -1 : hit ? o.s.RewardRisk : double.NaN;
                                if (min == 386 && double.IsNaN(r)) r = ((double)bar.Close - o.s.Entry) * o.s.Side / o.s.Risk;
                                if (double.IsNaN(r)) continue;
                                rows.Add((o.q, o.x, r));
                                open.RemoveAt(i);
                            }
                            if (min >= 370 || open.Exists(o => o.t == sec.Ticker)) continue;
                            Features f = Features.Build(m, sec);
                            if (!Setups.Find(strategy, f, out Setup s)) continue;
                            int side = s.Side;
                            double[] x =
                            {
                                s.Quality, f.Trendiness, f.VolRatio, side * f.Z5, side * f.Z15, side * (f.Ema9x5 - f.Ema21x5) / f.Unit,
                                side * (f.Last - f.Vwap) / f.Unit, f.MinutesIntoSession, side * (f.Last - f.DayOpen) / f.Unit, 0,
                            };
                            open.Add((sec.Ticker, s, x, s.Quality));
                        }
                    }
                    m.AdvanceTo(day.AddHours(20));
                }
            }
            TestContext.WriteLine($"{strategy}: n={rows.Count} win={rows.Count(x => x.r > 0) / (double)Math.Max(1, rows.Count):P1} avgR={rows.Select(x => x.r).DefaultIfEmpty().Average():+0.000;-0.000}");
            for (int k = 0; k < names.Length - 1; k++)
            {
                var sorted = rows.OrderBy(x => x.x[k]).ToList();
                var line = new System.Text.StringBuilder($"  {names[k],-10}");
                for (int b = 0; b < 5; b++)
                {
                    var part = sorted.Skip(sorted.Count * b / 5).Take(sorted.Count / 5).ToList();
                    if (part.Count == 0) continue;
                    line.Append($" | {part[0].x[k]:0.00}..{part[^1].x[k]:0.00} win {part.Count(x => x.r > 0) / (double)part.Count:P0} R {part.Average(x => x.r):+0.00;-0.00}");
                }
                TestContext.WriteLine(line.ToString());
            }
        }

        [Test, Explicit("Long statistical run for tuning")]
        public void SelfControl_ChangesBehaviour()
        {
            var profiles = new Dictionary<string, Action<Person>>
            {
                ["self-control 15"] = p => { p.Skills[(int)Skill.SelfControl] = 15; p.Traits[(int)Trait.Greed] = 80; p.Traits[(int)Trait.Caution] = 80; },
                ["self-control 85"] = p => p.Skills[(int)Skill.SelfControl] = 85,
            };
            var r = Run(seeds: 4, days: 15, profiles, Strategy.TrendPullback);
            foreach (var kv in r) TestContext.WriteLine(Describe(kv.Key, kv.Value));
        }
    }
}
