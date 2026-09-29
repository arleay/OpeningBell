using System;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Casino;
using OpeningBell.Core;
using UnityEngine;

namespace OpeningBell.Tests
{
    /// <summary>CASINO_SPEC §86: wagers, payouts, impossible combinations, RTP by maths and by simulation, the jackpot, saves.</summary>
    public class SlotTests
    {
        private static readonly DateTime Now = new DateTime(2030, 1, 8, 22, 0, 0);

        private static (SlotMachine machine, CasinoAccount account) Machine(SlotDefinition d, decimal chips = 1_000m, ulong seed = 5, SlotJackpot jackpot = null)
        {
            var account = new CasinoAccount();
            account.BuyChips(chips, Now, (a, w) => null);
            return (new SlotMachine(d, account, new SeededRandom(seed), () => Now, jackpot), account);
        }

        private static int StopOf(SlotDefinition d, SlotSymbol s) => Array.FindIndex(d.Strip, x => x.Symbol == s);

        [Test]
        public void EveryMachine_ReturnsLessThanItTakes_ByExactMaths()
        {
            foreach (SlotDefinition d in SlotMachines.All)
            {
                SlotMath.Figures f = SlotMath.Compute(d);
                Assert.Greater(f.Rtp, 0.88, d.Name);
                Assert.Less(f.Rtp, 0.97, d.Name + " keeps a house edge");
                Assert.Greater(f.HitFrequency, 0.05, d.Name);
            }
            Assert.AreEqual(0.9561, SlotMath.Compute(SlotMachines.Meridian7s).Rtp, 0.0005, "matches the design sheet");
            Assert.AreEqual(0.9306, SlotMath.Compute(SlotMachines.LuckyHarbor).Rtp, 0.0005);
        }

        [Test]
        public void Volatility_IsReal_NotJustALabel()
        {
            double high = SlotMath.Compute(SlotMachines.LuckyHarbor).Deviation;
            foreach (SlotDefinition d in SlotMachines.All.Where(x => x.Volatility != Volatility.High))
                Assert.Greater(high, SlotMath.Compute(d).Deviation * 2, d.Name + " swings less than Lucky Harbor");
            Assert.Greater(SlotMath.Compute(SlotMachines.TripleKell).HitFrequency, SlotMath.Compute(SlotMachines.LuckyHarbor).HitFrequency * 5,
                "low volatility: frequent small wins");
        }

        [Test]
        public void Spin_TakesTheBet_AndPaysTheLine()
        {
            SlotDefinition d = SlotMachines.Meridian7s;
            var (m, account) = Machine(d);
            int seven = StopOf(d, SlotSymbol.Seven);
            m.ForceNext(seven, seven, seven);
            Assert.IsNull(m.Spin(2m));
            Assert.AreEqual(200m, m.Last.Won, "7-7-7 pays 100 × the line bet");
            Assert.AreEqual(1_000m - 2m + 200m, account.Chips);

            int blank = StopOf(d, SlotSymbol.Blank);
            m.ForceNext(blank, blank, blank);
            m.Spin(5m);
            Assert.AreEqual(0m, m.Last.Won);
            Assert.AreEqual(1_000m - 2m + 200m - 5m, account.Chips, "a losing spin takes exactly the bet");

            int cherry = StopOf(d, SlotSymbol.Cherry);
            m.ForceNext(cherry, blank, cherry);
            m.Spin(1m);
            Assert.AreEqual(5m, m.Last.Won, "two cherries pay 5");
            Assert.AreEqual(3, account.Count(CasinoGame.Slots));
        }

        [Test]
        public void MixedBars_PayAnyBar_AndMultiLine_PaysEachLine()
        {
            SlotDefinition d = SlotMachines.Meridian7s;
            Assert.AreEqual(5, d.Pay(SlotSymbol.Bar, SlotSymbol.TripleBar, SlotSymbol.DoubleBar));
            Assert.AreEqual(40, d.Pay(SlotSymbol.TripleBar, SlotSymbol.TripleBar, SlotSymbol.TripleBar));
            Assert.AreEqual(0, d.Pay(SlotSymbol.Seven, SlotSymbol.Seven, SlotSymbol.Bar));

            SlotDefinition g = SlotMachines.GoldTide;
            var (m, _) = Machine(g);
            m.Spin(1m);
            Assert.AreEqual(5m, m.Last.Bet, "five lines at $1");
            int paid = 0;
            for (int l = 0; l < g.LineCount; l++)
            {
                int[] line = g.Lines[l];
                paid += g.Pay(g.At(m.Last.Stops[0] + line[0]), g.At(m.Last.Stops[1] + line[1]), g.At(m.Last.Stops[2] + line[2]));
            }
            Assert.AreEqual(paid, (int)m.Last.Won, "every line counted");
        }

        [Test]
        public void ImpossibleCombinations_NeverAppear_AndBadBetsAreRefused()
        {
            var (m, account) = Machine(SlotMachines.Meridian7s, 1_000_000m);
            for (int i = 0; i < 20_000; i++)
            {
                m.Spin(1m);
                for (int r = 0; r < 3; r++)
                    for (int row = 0; row < 3; row++)
                        Assert.AreNotEqual(SlotSymbol.Diamond, m.Last.Symbol(SlotMachines.Meridian7s, r, row), "no diamonds on the sevens machine");
            }
            decimal before = account.Chips;
            Assert.IsNotNull(m.Spin(0m));
            Assert.IsNotNull(m.Spin(-5m));
            Assert.IsNotNull(m.Spin(1.5m));
            Assert.IsNotNull(m.Spin(6m), "over the max line bet");
            var (broke, poor) = Machine(SlotMachines.Meridian7s, 1m);
            Assert.IsNotNull(broke.Spin(2m));
            Assert.AreEqual(1m, poor.Chips);
            Assert.AreEqual(before, account.Chips, "refused spins take nothing");
        }

        [Test]
        public void Simulation_MatchesTheMaths_OverManySpins()
        {
            foreach (SlotDefinition d in new[] { SlotMachines.TripleKell, SlotMachines.Meridian7s })
            {
                var (m, account) = Machine(d, 10_000_000m, 77);
                const int spins = 600_000;
                for (int i = 0; i < spins; i++) m.Spin(d.MinLineBet);
                double rtp = (double)(account.TotalReturned / account.TotalWagered);
                Assert.AreEqual(SlotMath.Compute(d).Rtp, rtp, 0.02, d.Name + $" simulated {rtp:P2}");
            }
        }

        [Test]
        public void Progressive_GrowsFromBets_PaysOnlyAtMaxBet_ThenReseeds()
        {
            SlotDefinition d = SlotMachines.DiamondDusk;
            var pool = new SlotJackpot();
            var (m, account) = Machine(d, 10_000m, 3, pool);
            int diamond = StopOf(d, SlotSymbol.Diamond);

            m.ForceNext(diamond, diamond, diamond);
            m.Spin(1m);
            Assert.AreEqual(250m, m.Last.Won, "under max bet: the fixed award");
            Assert.AreEqual(0m, m.Last.Jackpot);
            Assert.AreEqual(SlotJackpot.Seed + 0.015m, pool.Pool, "1.5% of the bet went in");

            for (int i = 0; i < 100; i++) m.Spin(5m);
            decimal before = pool.Pool, chips = account.Chips;
            Assert.Greater(before, SlotJackpot.Seed);
            m.ForceNext(diamond, diamond, diamond);
            m.Spin(5m);
            Assert.AreEqual(Math.Floor(before + 0.075m), m.Last.Jackpot, "the whole pool");
            Assert.AreEqual(chips - 5m + m.Last.Jackpot, account.Chips);
            Assert.AreEqual(1, pool.TimesWon);
            Assert.GreaterOrEqual(pool.Pool, SlotJackpot.Seed);
            Assert.Less(pool.Pool, SlotJackpot.Seed + 1m, "reseeded");
        }

        [Test]
        public void Jackpot_And_Stats_SurviveSave()
        {
            var floor = new CasinoFloor();
            floor.Account.BuyChips(500m, Now, (a, w) => null);
            var m = new SlotMachine(SlotMachines.DiamondDusk, floor.Account, new SeededRandom(9), () => Now, floor.Jackpot);
            for (int i = 0; i < 40; i++) m.Spin(5m);
            string json = JsonUtility.ToJson(floor.CaptureState());
            var loaded = new CasinoFloor();
            loaded.RestoreState(JsonUtility.FromJson<CasinoSaveData>(json));
            Assert.AreEqual(floor.Jackpot.Pool, loaded.Jackpot.Pool);
            Assert.AreEqual(40, loaded.Account.Count(CasinoGame.Slots));
            Assert.AreEqual(floor.Account.Chips, loaded.Account.Chips);
            Assert.AreEqual(floor.Rewards.Points, loaded.Rewards.Points);
            Assert.AreEqual(40, floor.Rewards.Lifetime, "a point per $5 on slots");
        }
    }
}
