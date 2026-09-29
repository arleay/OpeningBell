using System;
using System.Collections.Generic;
using NUnit.Framework;
using OpeningBell.Casino;
using OpeningBell.Core;

namespace OpeningBell.Tests
{
    /// <summary>CASINO_SPEC §85 roulette (and §28 baccarat): every bet type, zeros, many bets at once, the house edge.</summary>
    public class RouletteBaccaratTests
    {
        private static readonly DateTime Now = new DateTime(2030, 1, 8, 22, 0, 0);

        private static (RouletteTable table, CasinoAccount account) Table(bool doubleZero = false, decimal chips = 1_000m)
        {
            var account = new CasinoAccount();
            account.BuyChips(chips, Now, (a, w) => null);
            var rules = new RouletteRules { DoubleZero = doubleZero, TableMin = 5m, InsideMax = 500m, OutsideMax = 5_000m };
            return (new RouletteTable(rules, account, new SeededRandom(11), () => Now), account);
        }

        private static decimal SpinOn(RouletteTable t, int pocket, params RouletteBet[] bets)
        {
            foreach (RouletteBet b in bets) Assert.IsNull(t.Place(b), b.Describe());
            t.ForceNext(pocket);
            Assert.IsNull(t.Spin());
            return t.LastReturned;
        }

        [Test]
        public void Straight_Pays35To1()
        {
            var (t, account) = Table();
            Assert.AreEqual(360m, SpinOn(t, 17, Roulette.Straight(17, 10m)));
            Assert.AreEqual(1_350m, account.Chips);
            Assert.AreEqual(0m, SpinOn(t, 18, Roulette.Straight(17, 10m)));
            Assert.AreEqual(1_340m, account.Chips);
        }

        [Test]
        public void Colours_OddEven_Halves_PayEvenMoney_AndZeroBeatsThemAll()
        {
            var (t, _) = Table();
            Assert.IsTrue(Roulette.IsRed(1) && Roulette.IsBlack(2) && Roulette.IsRed(36) && Roulette.IsBlack(35));
            Assert.AreEqual(20m, SpinOn(t, 1, Roulette.Outside(RouletteBetKind.Red, 10m)));
            Assert.AreEqual(0m, SpinOn(t, 2, Roulette.Outside(RouletteBetKind.Red, 10m)));
            Assert.AreEqual(20m, SpinOn(t, 2, Roulette.Outside(RouletteBetKind.Black, 10m)));
            Assert.AreEqual(20m, SpinOn(t, 7, Roulette.Outside(RouletteBetKind.Odd, 10m)));
            Assert.AreEqual(20m, SpinOn(t, 8, Roulette.Outside(RouletteBetKind.Even, 10m)));
            Assert.AreEqual(20m, SpinOn(t, 18, Roulette.Outside(RouletteBetKind.Low, 10m)));
            Assert.AreEqual(20m, SpinOn(t, 19, Roulette.Outside(RouletteBetKind.High, 10m)));
            decimal zero = SpinOn(t, 0, Roulette.Outside(RouletteBetKind.Red, 10m), Roulette.Outside(RouletteBetKind.Black, 10m),
                Roulette.Outside(RouletteBetKind.Odd, 10m), Roulette.Outside(RouletteBetKind.Even, 10m), Roulette.Outside(RouletteBetKind.Low, 10m));
            Assert.AreEqual(0m, zero, "0 is none of these");
            Assert.AreEqual(360m, SpinOn(t, 0, Roulette.Straight(0, 10m)), "but pays on the zero itself");
        }

        [Test]
        public void Dozens_And_Columns_Pay2To1()
        {
            var (t, _) = Table();
            Assert.AreEqual(30m, SpinOn(t, 12, Roulette.Outside(RouletteBetKind.Dozen, 10m, 1)));
            Assert.AreEqual(0m, SpinOn(t, 13, Roulette.Outside(RouletteBetKind.Dozen, 10m, 1)));
            Assert.AreEqual(30m, SpinOn(t, 25, Roulette.Outside(RouletteBetKind.Dozen, 10m, 3)));
            Assert.AreEqual(30m, SpinOn(t, 34, Roulette.Outside(RouletteBetKind.Column, 10m, 1)), "1, 4 … 34");
            Assert.AreEqual(30m, SpinOn(t, 36, Roulette.Outside(RouletteBetKind.Column, 10m, 3)));
            Assert.AreEqual(0m, SpinOn(t, 0, Roulette.Outside(RouletteBetKind.Column, 10m, 1)));
        }

        [Test]
        public void InsideBets_CoverTheRightNumbers_AndPayByHowMany()
        {
            var (t, _) = Table();
            Assert.AreEqual(180m, SpinOn(t, 5, Roulette.Split(5, 8, 10m)), "split 17:1");
            Assert.AreEqual(120m, SpinOn(t, 36, Roulette.Street(12, 10m)), "street 11:1");
            Assert.AreEqual(90m, SpinOn(t, 5, Roulette.Corner(1, 10m)), "corner 1-2-4-5 8:1");
            Assert.AreEqual(60m, SpinOn(t, 6, Roulette.SixLine(1, 10m)), "six line 1–6 5:1");
            Assert.AreEqual(0m, SpinOn(t, 7, Roulette.SixLine(1, 10m)));
            Assert.Throws<ArgumentException>(() => Roulette.Split(3, 4, 10m), "3 and 4 are on different rows' ends");
            Assert.Throws<ArgumentException>(() => Roulette.Split(1, 5, 10m));
            Assert.Throws<ArgumentException>(() => Roulette.Corner(3, 10m), "no corner from column 3");
            Assert.DoesNotThrow(() => Roulette.Split(0, 2, 10m));
        }

        [Test]
        public void MultipleBets_SettleTogether_InOneStep()
        {
            var (t, account) = Table();
            // 17 hits: straight 17 (+350), black (+10 back 20), odd (+10), split 17-20 (+170), column 2 loses? 17 is column 2: wins.
            decimal returned = SpinOn(t, 17, Roulette.Straight(17, 10m), Roulette.Outside(RouletteBetKind.Black, 10m), Roulette.Outside(RouletteBetKind.Odd, 10m),
                Roulette.Split(17, 20, 10m), Roulette.Outside(RouletteBetKind.Column, 10m, 2), Roulette.Straight(3, 10m));
            Assert.AreEqual(360m + 20m + 20m + 180m + 30m, returned);
            Assert.AreEqual(1_000m - 60m + returned, account.Chips);
            Assert.AreEqual(5, t.LastWinners.Count);
            Assert.AreEqual(1, account.Count(CasinoGame.Roulette));
            Assert.AreEqual(0, t.Bets.Count, "the layout is cleared");
            Assert.IsNull(t.Rebet());
            Assert.AreEqual(60m, t.Staked, "rebet puts the same chips back");
        }

        [Test]
        public void Limits_Minimums_AndDoubleZero()
        {
            var (t, account) = Table();
            Assert.IsNotNull(t.Place(Roulette.Straight(5, 600m)), "inside max");
            Assert.IsNotNull(t.Place(Roulette.Straight(Roulette.DoubleZeroPocket, 5m)), "no 00 on a single-zero wheel");
            t.Place(Roulette.Straight(5, 1m));
            Assert.IsNotNull(t.Spin(), "under the table minimum");
            Assert.AreEqual(1_000m, account.Chips);
            Assert.IsNotNull(t.Place(Roulette.Outside(RouletteBetKind.Red, 5_000m)), "more than the chips");

            var (american, _) = Table(doubleZero: true);
            Assert.AreEqual(38, american.Rules.Pockets);
            Assert.AreEqual(360m, SpinOn(american, Roulette.DoubleZeroPocket, Roulette.Straight(Roulette.DoubleZeroPocket, 10m)));
            Assert.AreEqual(0m, SpinOn(american, Roulette.DoubleZeroPocket, Roulette.Outside(RouletteBetKind.Black, 10m)));
            Assert.AreEqual("00 Green", Roulette.Describe(Roulette.DoubleZeroPocket));
            Assert.AreEqual("17 Black", Roulette.Describe(17));
        }

        [Test]
        public void HouseEdge_IsTheZeros()
        {
            foreach ((bool dz, double edge) in new[] { (false, 1.0 / 37), (true, 2.0 / 38) })
            {
                var (t, account) = Table(dz, 10_000_000m);
                for (int i = 0; i < 200_000; i++)
                {
                    t.Place(Roulette.Outside(RouletteBetKind.Red, 5m));
                    t.Place(Roulette.Outside(RouletteBetKind.Odd, 5m)); // outside bets: low variance, same edge
                    t.Spin();
                }
                double lost = (double)(-account.NetResult / account.TotalWagered);
                Assert.AreEqual(edge, lost, 0.012, $"{(dz ? "American" : "European")} edge {lost:P2}");
            }
        }

        // ---------------------------------------------------------------- baccarat

        private static (BaccaratTable table, Shoe shoe, CasinoAccount account) Baccarat(decimal chips = 1_000m, ulong seed = 4)
        {
            var account = new CasinoAccount();
            account.BuyChips(chips, Now, (a, w) => null);
            var shoe = new Shoe(8, 0.8, new SeededRandom(seed));
            return (new BaccaratTable(new BaccaratRules { MinBet = 25m }, shoe, account, () => Now), shoe, account);
        }

        private static void Stack(Shoe shoe, params string[] ranks)
        {
            var cards = new List<Card>();
            foreach (string r in ranks) cards.Add(Card.Of(r));
            shoe.Stack(cards.ToArray());
        }

        [Test]
        public void Baccarat_NaturalsStand_ThirdCardRules_AndPayouts()
        {
            var (t, shoe, account) = Baccarat();
            // Deal order P B P B. Player 9 natural vs banker 7.
            Stack(shoe, "4", "3", "5", "4");
            Assert.IsNull(t.Deal(100m, 0m, 0m));
            Assert.AreEqual(2, t.Player.Count);
            Assert.AreEqual(2, t.Banker.Count, "a natural stands both");
            Assert.AreEqual(BaccaratOutcome.Player, t.Outcome);
            Assert.AreEqual(1_100m, account.Chips);

            // Player 3 draws (a 2 → 5); banker 3 draws unless the player's third was an 8 → banker takes a 4 → 7, wins.
            Stack(shoe, "A", "2", "2", "A", "2", "4");
            t.Deal(0m, 100m, 0m);
            Assert.AreEqual(3, t.Player.Count);
            Assert.AreEqual(3, t.Banker.Count);
            Assert.AreEqual(BaccaratOutcome.Banker, t.Outcome);
            Assert.AreEqual(1_195m, account.Chips, "banker pays 0.95 (5% commission)");

            // Player 3 + 8 = 1; banker on 3 doesn't draw against a third-card 8. Banker 3 > 1.
            Stack(shoe, "A", "2", "2", "A", "8");
            t.Deal(0m, 100m, 0m);
            Assert.AreEqual(2, t.Banker.Count);
            Assert.AreEqual(BaccaratOutcome.Banker, t.Outcome);

            // Player stands on 6; banker 5 draws (0–5 draws when the player stood).
            Stack(shoe, "3", "2", "3", "3", "K");
            t.Deal(100m, 0m, 25m);
            Assert.AreEqual(2, t.Player.Count);
            Assert.AreEqual(3, t.Banker.Count);
            Assert.AreEqual(BaccaratOutcome.Player, t.Outcome, "6 beats 5");
        }

        [Test]
        public void Baccarat_Tie_Pays8To1_AndPushesPlayerAndBanker()
        {
            var (t, shoe, account) = Baccarat();
            Stack(shoe, "4", "4", "4", "4"); // 8 v 8 naturals
            decimal before = account.Chips;
            t.Deal(100m, 50m, 10m);
            Assert.AreEqual(BaccaratOutcome.Tie, t.Outcome);
            Assert.AreEqual(before + 80m, account.Chips, "tie wins 8 × 10; the others come back");
            Assert.IsNotNull(t.Deal(10m, 0m, 0m), "under the minimum");
            Assert.IsNotNull(t.Deal(0m, 0m, 0m));
            Assert.AreEqual(1, account.Count(CasinoGame.Baccarat));
        }

        [Test]
        public void Baccarat_HouseEdge_IsSmall_AndReal()
        {
            var (t, _, account) = Baccarat(10_000_000m, 99);
            for (int i = 0; i < 150_000; i++) t.Deal(0m, 100m, 0m);
            double edge = (double)(-account.NetResult / account.TotalWagered);
            Assert.AreEqual(0.0106, edge, 0.006, $"banker bet loses about 1.06% ({edge:P2})");
        }
    }
}
