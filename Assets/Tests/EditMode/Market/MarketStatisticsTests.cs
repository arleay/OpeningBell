using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OpeningBell.Market;
using UnityEngine;

namespace OpeningBell.Tests
{
    /// <summary>
    /// The market judged the way a trader would: many simulated sessions, then statistics (volatility, volume and
    /// range by time of day, day types, how often remembered levels hold or break) plus rendered 1-minute charts
    /// (TestResults/market-*.png). Tuning happens here, on the participants and parameters, never on outcomes.
    /// </summary>
    public class MarketStatisticsTests
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private const string Results = "TestResults";

        private sealed class DayRecord
        {
            public string Ticker;
            public DateTime Date;
            public DayType Type;
            public double Sd;
            public List<Candle> Minutes = new List<Candle>();
            public List<(LevelKind Kind, double Price)> Levels = new List<(LevelKind, double)>();
            public decimal PrevClose;
        }

        [Test]
        public void ManySessions_LookLikeARealMarket()
        {
            const int days = 60;
            SecuritySpec[] specs = TestMarkets.Basic();
            MarketSimulation sim = TestMarkets.Create(20260922, TestMarkets.Monday, specs);
            var records = new List<DayRecord>();
            var dayTypes = new Dictionary<DayType, int>();
            var regimeMinutes = new Dictionary<Regime, int>();
            var dailyReturns = specs.ToDictionary(s => s.Ticker, _ => new List<double>());
            var fairMoves = specs.ToDictionary(s => s.Ticker, _ => new List<double>());
            var devMoves = specs.ToDictionary(s => s.Ticker, _ => new List<double>());
            var lastFair = new Dictionary<string, double>();
            var lastDev = new Dictionary<string, double>();

            DateTime date = TestMarkets.Monday;
            for (int d = 0; d < days; d++)
            {
                while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) date = date.AddDays(1);
                sim.AdvanceTo(date.AddHours(9.5)); // premarket done; levels for the day are known
                var today = new Dictionary<string, DayRecord>();
                foreach (SecurityRuntimeState s in sim.Securities)
                {
                    var r = new DayRecord { Ticker = s.Ticker, Date = date, Sd = s.Spec.DailyVolatility, PrevClose = s.PreviousClose };
                    foreach (Level l in s.Levels.All) r.Levels.Add((l.Kind, Math.Exp(l.Log)));
                    today[s.Ticker] = r;
                }
                // Walk the regular session minute by minute to sample regimes.
                for (DateTime t = date.AddHours(9.5); t < date.AddHours(16); t = t.AddMinutes(1))
                {
                    sim.AdvanceTo(t.AddMinutes(1));
                    foreach (SecurityRuntimeState s in sim.Securities)
                        regimeMinutes[s.Regime] = regimeMinutes.TryGetValue(s.Regime, out int n) ? n + 1 : 1;
                }
                sim.AdvanceTo(date.AddHours(20));
                foreach (SecurityRuntimeState s in sim.Securities)
                {
                    DayRecord r = today[s.Ticker];
                    r.Type = s.DayType;
                    dayTypes[r.Type] = dayTypes.TryGetValue(r.Type, out int n) ? n + 1 : 1;
                    CandleSeries m1 = s.Candles.Get(Timeframe.Minute1);
                    for (int i = 0; i < m1.Count; i++)
                        if (m1[i].Start.Date == date) r.Minutes.Add(m1[i]);
                    records.Add(r);
                    Candle daily = s.Candles.Get(Timeframe.Day1).Completed.LastOrDefault();
                    if (s.Candles.Get(Timeframe.Day1).HasCurrent) daily = s.Candles.Get(Timeframe.Day1).Current;
                    if (r.PrevClose > 0m) dailyReturns[s.Ticker].Add(Math.Log((double)s.RegularClose() / (double)r.PrevClose));
                    if (lastFair.TryGetValue(s.Ticker, out double f0))
                    {
                        fairMoves[s.Ticker].Add(s.FairLog - f0);
                        devMoves[s.Ticker].Add(s.DeviationLog - lastDev[s.Ticker]);
                    }
                    lastFair[s.Ticker] = s.FairLog;
                    lastDev[s.Ticker] = s.DeviationLog;
                }
                date = date.AddDays(1);
            }

            var report = new StringBuilder();
            report.AppendLine($"== {days} sessions × {specs.Length} stocks");
            foreach (SecuritySpec spec in specs)
            {
                List<double> rets = dailyReturns[spec.Ticker];
                double sd = Sd(rets);
                report.AppendLine($"{spec.Ticker}: daily σ {sd:P2} (spec {spec.DailyVolatility:P1}), mean |ret| {rets.Average(Math.Abs):P2}, max |ret| {rets.Max(Math.Abs):P2}" +
                                  $"   fair σ {Sd(fairMoves[spec.Ticker]):P2}, flow deviation σ {Sd(devMoves[spec.Ticker]):P2}");
            }

            // Time of day: mean |1m return| and volume share by half hour (regular session), all stocks normalised by σ.
            var absByBucket = new double[13];
            var volByBucket = new double[13];
            var countByBucket = new int[13];
            foreach (DayRecord r in records)
            {
                decimal prev = 0m;
                foreach (Candle c in r.Minutes)
                {
                    double minutes = (c.Start.TimeOfDay - TimeSpan.FromHours(9.5)).TotalMinutes;
                    if (minutes < 0 || minutes >= 390) { prev = c.Close; continue; }
                    int b = (int)(minutes / 30);
                    if (prev > 0m) absByBucket[b] += Math.Abs(Math.Log((double)c.Close / (double)prev)) / r.Sd;
                    volByBucket[b] += c.Volume;
                    countByBucket[b]++;
                    prev = c.Close;
                }
            }
            double totalVol = volByBucket.Sum();
            report.AppendLine("\n== Time of day (half hours from 9:30): |1m return|/σ ×1000, volume share");
            for (int b = 0; b < 13; b++)
                report.AppendLine($"{9.5 + b * 0.5,5:0.0}h  |r| {1000 * absByBucket[b] / Math.Max(1, countByBucket[b]),6:0.0}   vol {volByBucket[b] / totalVol,6:P1}");

            // Day shape: trend days (close near an extreme, body most of the range) vs ranges.
            int trendish = 0, rangeish = 0;
            foreach (DayRecord r in records)
            {
                var regular = r.Minutes.Where(c => c.Start.TimeOfDay >= TimeSpan.FromHours(9.5) && c.Start.TimeOfDay < TimeSpan.FromHours(16)).ToList();
                if (regular.Count == 0) continue;
                double hi = (double)regular.Max(c => c.High), lo = (double)regular.Min(c => c.Low);
                double body = Math.Abs((double)(regular[regular.Count - 1].Close - regular[0].Open));
                if (hi <= lo) continue;
                if (body / (hi - lo) > 0.65) trendish++;
                else if (body / (hi - lo) < 0.25) rangeish++;
            }
            report.AppendLine($"\n== Day shape: trend-like {trendish}, range-like {rangeish}, of {records.Count}");
            // Chop: 30-minute windows of the session that go nowhere (net move under 30% of the window's range).
            int windows = 0, chop = 0;
            foreach (DayRecord r in records)
            {
                List<Candle> session = r.Minutes.Where(c => c.Start.TimeOfDay >= TimeSpan.FromHours(9.5) && c.Start.TimeOfDay < TimeSpan.FromHours(16)).ToList();
                for (int w = 0; w + 30 <= session.Count; w += 30)
                {
                    List<Candle> win = session.GetRange(w, 30);
                    double hi = (double)win.Max(c => c.High), lo = (double)win.Min(c => c.Low);
                    if (hi <= lo) continue;
                    windows++;
                    if (Math.Abs((double)(win[29].Close - win[0].Open)) < 0.3 * (hi - lo)) chop++;
                }
            }
            double chopShare = chop / (double)Math.Max(1, windows);
            report.AppendLine($"Chop: {chopShare:P0} of {windows} half-hour windows");
            report.AppendLine("Hidden day types: " + string.Join(", ", dayTypes.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}")));
            int totalRegime = regimeMinutes.Values.Sum();
            report.AppendLine("Regime time: " + string.Join(", ", regimeMinutes.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value / (double)totalRegime:P0}")));

            // Levels known at the open: when price first comes to one, does it hold or break?
            int held = 0, broke = 0, swept = 0;
            foreach (DayRecord r in records)
                foreach (var (kind, level) in r.Levels)
                {
                    string outcome = Test(r, level);
                    if (outcome == "held") held++;
                    else if (outcome == "broke") broke++;
                    else if (outcome == "swept") { swept++; held++; }
                }
            report.AppendLine($"\n== First tests of remembered levels: held {held} (of which swept then held {swept}), broke {broke}, hold rate {held / (double)Math.Max(1, held + broke):P0}");

            Directory.CreateDirectory(Results);
            File.WriteAllText(Path.Combine(Results, "market-stats.txt"), report.ToString());
            TestContext.WriteLine(report.ToString());

            // A few days as charts, to judge the price action by eye.
            foreach (DayRecord r in records.Where((_, i) => i % 7 == 0).Take(8))
                Render(r, Path.Combine(Results, $"market-{r.Ticker}-{r.Date:MMdd}-{r.Type}.png"));

            foreach (SecuritySpec spec in specs)
                Assert.That(Sd(dailyReturns[spec.Ticker]), Is.InRange(spec.DailyVolatility * 0.6, spec.DailyVolatility * 1.6), spec.Ticker);
            double holdRate = held / (double)Math.Max(1, held + broke);
            Assert.That(holdRate, Is.InRange(0.3, 0.8), "levels matter, but no level is guaranteed");
        }

        [Test]
        public void HugePlayerOrders_MoveThePrice_RetailOnesDont()
        {
            // Averaged over several markets: a single push can land on a resting wall and get absorbed, like in reality.
            double Move(long shares) => Enumerable.Range(0, 24).Average(seed => MoveIn((ulong)(31 + seed), shares));

            double MoveIn(ulong seed, long shares)
            {
                MarketSimulation sim = TestMarkets.Create(seed, TestMarkets.Monday, TestMarkets.Basic());
                sim.AdvanceTo(TestMarkets.Monday.AddHours(11));
                var account = new OpeningBell.Trading.Account(sim);
                account.Deposit(100_000_000m);
                var orders = new OpeningBell.Trading.OrderManager(sim, account, new OpeningBell.Trading.BrokerRules { BookLevelsPerTick = 50 });
                decimal before = sim.Securities[0].Last;
                if (shares > 0) orders.SubmitMarket("AAA", OpeningBell.Trading.OrderSide.Buy, shares);
                sim.AdvanceTo(TestMarkets.Monday.AddHours(11).AddMinutes(3));
                return Math.Log((double)sim.Securities[0].Last / (double)before);
            }

            // Orders are in contracts: one is retail size; the huge one carries 100k shares of exposure (AAA trades 2M a day: 5% of ADV).
            long pointValue = (long)ContractSpec.For(TestMarkets.Basic()[0]).PointValue;
            double control = Move(0), retail = Move(1), huge = Move(100_000 / pointValue);
            TestContext.WriteLine($"3-minute move: none {control:P3}, 1 contract {retail:P3}, 100k shares of exposure {huge:P3}");
            Assert.AreEqual(control, retail, 0.0005, "retail size is invisible");
            // Square-root law: 5% of ADV ≈ 0.8 σ × √0.05 ≈ 0.54% push, some of it absorbed by resting walls.
            Assert.Greater(huge - control, 0.0015, "5% of a day's volume in one go pushes the price up");
            Assert.Less(huge - control, 0.02, "but it doesn't launch the stock");
        }

        [Test]
        public void TenStocksAndTheIndex_RunFarFasterThan120x()
        {
            var specs = Enumerable.Range(0, 10).Select(i => TestMarkets.Spec("S" + i, (Sector)(i % 7), 10 + 9 * i, vol: 0.02 + 0.004 * i)).ToArray();
            MarketSimulation sim = TestMarkets.Create(77, TestMarkets.Monday, specs);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            sim.AdvanceTo(TestMarkets.Monday.AddDays(1)); // a whole trading day: 4:00–20:00 at the game's 2-second step
            watch.Stop();
            // At 120× a 16-hour market day lasts 8 real minutes; the simulation must be a tiny fraction of that.
            double seconds = watch.Elapsed.TotalSeconds;
            TestContext.WriteLine($"one full day, 10 stocks + index: {seconds:F2} s ({480 / seconds:F0}× headroom over 120× speed)");
            Assert.Less(seconds, 8.0, "at least 60× headroom over real time at 120×");
        }

        /// <summary>First approach to a level during the regular session: held (price backs off 0.3σ first), broke
        /// (goes 0.3σ through first), swept (poked through by up to 0.3σ, then backed off), or never tested.</summary>
        private static string Test(DayRecord r, double level)
        {
            double sd = r.Sd;
            bool started = false;
            int side = 0;
            double deepest = 0;
            foreach (Candle c in r.Minutes)
            {
                if (c.Start.TimeOfDay < TimeSpan.FromHours(9.5) || c.Start.TimeOfDay >= TimeSpan.FromHours(16)) continue;
                double hi = Math.Log((double)c.High / level) / sd, lo = Math.Log((double)c.Low / level) / sd, close = Math.Log((double)c.Close / level) / sd;
                if (side == 0) side = close > 0 ? 1 : -1;
                if (!started)
                {
                    if (side > 0 ? lo < 0.05 : hi > -0.05) started = true;
                    else continue;
                }
                double through = side > 0 ? -lo : hi;
                deepest = Math.Max(deepest, through);
                if (through > 0.3) return "broke";
                if (side > 0 ? close > 0.3 : close < -0.3) return deepest > 0.03 ? "swept" : "held";
            }
            return "untested";
        }

        /// <summary>
        /// The game's own stocks, settings and news, judged on what the player looks at: 5-minute session candles.
        /// Candle bodies (a real tape averages about half the bar's range; mostly-wick dojis are the minority) and how
        /// efficiently price travels over an hour of bars (a random walk scores about 0.29; trends score higher).
        /// Writes TestResults/game-5m-*.png and game-5m-stats.txt.
        /// </summary>
        [Test]
        public void GameStocks_FiveMinuteCandles_HaveBodiesAndTravel()
        {
            var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<SecurityCatalog>("Assets/ScriptableObjects/Securities/SecurityCatalog.asset");
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<MarketSettings>("Assets/ScriptableObjects/Settings/MarketSettings.asset");
            var library = UnityEditor.AssetDatabase.LoadAssetAtPath<NewsLibrary>("Assets/ScriptableObjects/News/NewsLibrary.asset");
            DateTime monday = TestMarkets.Monday;
            var sim = new MarketSimulation(settings.Config, catalog.CreateSpecs(), catalog.Index, new OpeningBell.Core.SeededRandomService(7),
                monday.AddHours(3), library.Templates);

            const int days = 10;
            double bodySum = 0, efficiencySum = 0, rangeSum = 0;
            int bars = 0, dojis = 0, hours = 0, sessions = 0;
            var perTicker = new Dictionary<string, double[]>(); // body sum, bars, dojis, efficiency sum, hours, ticks per bar, σ-normalised range
            double normRangeSum = 0;
            var report = new StringBuilder();
            DateTime date = monday;
            for (int d = 0; d < days; d++)
            {
                while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) date = date.AddDays(1);
                sim.AdvanceTo(date.AddHours(16.1));
                foreach (SecurityRuntimeState s in sim.Securities)
                {
                    CandleSeries m5 = s.Candles.Get(Timeframe.Minute5);
                    var session = new List<Candle>();
                    for (int i = 0; i < m5.Count; i++)
                        if (m5[i].Start.Date == date && m5[i].Start.TimeOfDay >= TimeSpan.FromHours(9.5) && m5[i].Start.TimeOfDay < TimeSpan.FromHours(16))
                            session.Add(m5[i]);
                    if (session.Count < 60) continue;
                    sessions++;
                    if (!perTicker.TryGetValue(s.Ticker, out double[] t)) perTicker[s.Ticker] = t = new double[7];
                    double barSigma = s.Spec.DailyVolatility / Math.Sqrt(78); // a random walk's 5-minute σ at the stock's daily volatility
                    foreach (Candle c in session)
                    {
                        t[1]++;
                        t[5] += (double)((c.High - c.Low) / PriceTick.For(c.Close));
                        double normRange = (double)((c.High - c.Low) / c.Close) / barSigma;
                        t[6] += normRange;
                        normRangeSum += normRange;
                        double range = (double)(c.High - c.Low);
                        if (range <= 0) { dojis++; bars++; continue; }
                        double body = Math.Abs((double)(c.Close - c.Open)) / range;
                        bodySum += body;
                        t[0] += body;
                        if (body < 0.2) { dojis++; t[2]++; }
                        bars++;
                    }
                    for (int k = 0; k + 12 <= session.Count; k += 12)
                    {
                        double path = 0;
                        for (int j = k + 1; j < k + 12; j++) path += Math.Abs((double)(session[j].Close - session[j - 1].Close));
                        double net = Math.Abs((double)(session[k + 11].Close - session[k].Close));
                        if (path > 0) { efficiencySum += net / path; hours++; t[3] += net / path; t[4]++; }
                    }
                    double hi = (double)session.Max(c => c.High), lo = (double)session.Min(c => c.Low);
                    rangeSum += (hi - lo) / lo / s.Spec.DailyVolatility;
                    if (d < 3 && (s.Ticker == "BLZE" || s.Ticker == "APEX" || s.Ticker == "NVRA" || s.Ticker == "CYRA"))
                        Render(new DayRecord { Ticker = s.Ticker, Date = date, Minutes = session },
                            Path.Combine(Results, $"game-5m-{s.Ticker}-{date:MMdd}-{s.DayType}.png"));
                }
                date = date.AddDays(1);
            }

            double meanBody = bodySum / bars, dojiShare = dojis / (double)bars, efficiency = efficiencySum / hours, meanRange = rangeSum / sessions;
            report.AppendLine($"{sessions} sessions, {bars} five-minute bars");
            double meanNormRange = normRangeSum / bars;
            report.AppendLine($"mean body/range {meanBody:F2}, doji share {dojiShare:P0}, hourly efficiency {efficiency:F2} (random walk 0.29), session range {meanRange:F2} daily σ, bar range {meanNormRange:F2} bar σ");
            foreach (var (ticker, t) in perTicker.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)))
                report.AppendLine($"  {ticker}: body {t[0] / t[1]:F2}, doji {t[2] / t[1]:P0}, efficiency {t[3] / Math.Max(1, t[4]):F2}, bar range {t[5] / t[1]:F1} ticks = {t[6] / t[1]:F2} bar σ");
            Directory.CreateDirectory(Results);
            File.WriteAllText(Path.Combine(Results, "game-5m-stats.txt"), report.ToString());
            TestContext.WriteLine(report.ToString());

            Assert.Greater(meanBody, 0.4, "candles have bodies, not just wicks");
            Assert.Less(dojiShare, 0.3, "dojis are the minority");
            Assert.Greater(efficiency, 0.33, "price travels: more directional than a random walk");
            // A random walk's 5-minute bar spans about 1.6 bar σ high to low, but daily volatility also holds the overnight
            // gap and extended hours, so session bars carry less; below ~1 they shrink into tick-and-spread noise.
            Assert.Greater(meanNormRange, 1.0, "bars move: price travels inside them, not just across the day");
        }

        private static void Render(DayRecord r, string file)
        {
            const int w = 1600, h = 760, volH = 140;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var bg = new Color32(19, 23, 30, 255);
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            List<Candle> cs = r.Minutes;
            if (cs.Count == 0) return;
            double hi = cs.Max(c => (double)c.High), lo = cs.Min(c => (double)c.Low);
            foreach (var (_, price) in r.Levels)
                if (price < hi * 1.02 && price > lo * 0.98) { hi = Math.Max(hi, price); lo = Math.Min(lo, price); }
            double pad = (hi - lo) * 0.05;
            hi += pad;
            lo -= pad;
            long vmax = cs.Max(c => c.Volume);
            int Y(double p) => volH + 10 + (int)((p - lo) / (hi - lo) * (h - volH - 20));
            void Put(int x, int y, Color32 col)
            {
                if (x >= 0 && x < w && y >= 0 && y < h) px[y * w + x] = col;
            }

            // Level lines (known at the open): session levels gold, round numbers grey, the rest blue.
            foreach (var (kind, price) in r.Levels)
            {
                if (price > hi || price < lo) continue;
                Color32 col = kind == LevelKind.RoundNumber ? new Color32(90, 95, 105, 255)
                    : kind == LevelKind.PreviousDayHigh || kind == LevelKind.PreviousDayLow || kind == LevelKind.PremarketHigh || kind == LevelKind.PremarketLow
                        ? new Color32(224, 169, 59, 255) : new Color32(90, 156, 245, 255);
                int y = Y(price);
                for (int x = 0; x < w; x += (x / 6) % 2 == 0 ? 1 : 5) Put(x, y, col);
            }

            double cw = (double)w / cs.Count;
            decimal notional = 0m;
            long volume = 0;
            int prevVy = -1;
            for (int i = 0; i < cs.Count; i++)
            {
                Candle c = cs[i];
                bool regular = c.Start.TimeOfDay >= TimeSpan.FromHours(9.5) && c.Start.TimeOfDay < TimeSpan.FromHours(16);
                bool up = c.Close >= c.Open;
                Color32 col = up ? new Color32(52, 199, 123, 255) : new Color32(232, 84, 76, 255);
                if (!regular) col = up ? new Color32(40, 110, 80, 255) : new Color32(120, 60, 60, 255);
                int x0 = (int)(i * cw), x1 = Math.Max(x0 + 1, (int)((i + 1) * cw) - 1);
                int xm = (x0 + x1) / 2;
                for (int y = Y((double)c.Low); y <= Y((double)c.High); y++) Put(xm, y, col);
                int yo = Y((double)c.Open), yc = Y((double)c.Close);
                for (int y = Math.Min(yo, yc); y <= Math.Max(yo, yc); y++)
                    for (int x = x0; x < x1; x++) Put(x, y, col);
                int vh = (int)(c.Volume / (double)vmax * (volH - 10));
                for (int y = 0; y < vh; y++)
                    for (int x = x0; x < x1; x++) Put(x, y + 2, new Color32(col.r, col.g, col.b, 255));
                if (c.Start.TimeOfDay.TotalMinutes == 9.5 * 60)
                    for (int y = 0; y < h; y++) if (y % 4 < 2) Put(xm, y, new Color32(255, 255, 255, 60));
                if (regular)
                {
                    notional += c.Close * c.Volume;
                    volume += c.Volume;
                    int vy = Y((double)(notional / Math.Max(1, volume)));
                    if (prevVy >= 0) for (int y = Math.Min(prevVy, vy); y <= Math.Max(prevVy, vy); y++) Put(xm, y, new Color32(232, 182, 60, 255));
                    prevVy = vy;
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static double Sd(List<double> x)
        {
            double m = x.Average();
            return Math.Sqrt(x.Sum(v => (v - m) * (v - m)) / Math.Max(1, x.Count - 1));
        }
    }

    internal static class SecurityTestExtensions
    {
        /// <summary>Today's regular-session close (the last print before 16:00), or the previous close before any.</summary>
        public static decimal RegularClose(this SecurityRuntimeState s)
        {
            CandleSeries d = s.Candles.Get(Timeframe.Day1);
            if (d.HasCurrent) return d.Current.Close;
            return d.Count > 0 ? d[d.Count - 1].Close : s.PreviousClose;
        }
    }
}
