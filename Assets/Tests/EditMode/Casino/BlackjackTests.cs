using System;
using System.Collections.Generic;
using NUnit.Framework;
using OpeningBell.Casino;
using OpeningBell.Core;

namespace OpeningBell.Tests
{
    /// <summary>CASINO_SPEC §84: blackjack rules and payouts, with the shoe stacked so every case is exact.</summary>
    public class BlackjackTests
    {
        private static readonly DateTime Now = new DateTime(2030, 1, 7, 21, 0, 0);
        private CasinoAccount _account;
        private Shoe _shoe;
        private BlackjackRound _round;

        [SetUp]
        public void SetUp()
        {
            _account = new CasinoAccount();
            Assert.IsNull(_account.BuyChips(1_000m, Now, (a, w) => null));
            _shoe = new Shoe(6, 0.75, new SeededRandom(7));
            _round = new BlackjackRound(new BlackjackRules { MinBet = 25m, MaxBet = 2_500m }, _shoe, _account, () => Now);
        }

        private static Card C(string rank) => Card.Of(rank);

        /// <summary>Deal order: player, dealer up, player, dealer hole, then whatever's drawn next.</summary>
        private void Stack(params string[] ranks)
        {
            var cards = new List<Card>();
            foreach (string r in ranks) cards.Add(C(r));
            _shoe.Stack(cards.ToArray());
        }

        [Test]
        public void AceCountsOneOrEleven()
        {
            Assert.AreEqual(20, BlackjackMath.Total(new[] { C("A"), C("9") }, out bool soft));
            Assert.IsTrue(soft);
            Assert.AreEqual(15, BlackjackMath.Total(new[] { C("A"), C("9"), C("5") }, out soft));
            Assert.IsFalse(soft);
            Assert.AreEqual(12, BlackjackMath.Total(new[] { C("A"), C("A") }, out soft));
            Assert.AreEqual(21, BlackjackMath.Total(new[] { C("A"), C("K") }, out _));
            Assert.AreEqual(13, BlackjackMath.Total(new[] { C("A"), C("A"), C("A"), C("K") }, out _));
            Assert.AreEqual(20, BlackjackMath.Total(new[] { C("K"), C("Q") }, out _), "faces count ten");
        }

        [Test]
        public void NormalWin_PaysEvenMoney()
        {
            Stack("10", "9", "9", "8"); // player 19, dealer 17 stands
            Assert.IsNull(_round.Deal(100m));
            Assert.AreEqual(900m, _account.Chips, "stake off the chips when bet");
            _round.Stand();
            Assert.AreEqual(BlackjackPhase.Settled, _round.Phase);
            Assert.AreEqual(HandOutcome.Win, _round.Hands[0].Outcome);
            Assert.AreEqual(1_100m, _account.Chips);
        }

        [Test]
        public void DealerWin_And_Push()
        {
            Stack("10", "10", "7", "8"); // 17 vs 18
            _round.Deal(100m);
            _round.Stand();
            Assert.AreEqual(HandOutcome.Lose, _round.Hands[0].Outcome);
            Assert.AreEqual(900m, _account.Chips);

            Stack("10", "10", "8", "8"); // 18 vs 18
            _round.Deal(100m);
            _round.Stand();
            Assert.AreEqual(HandOutcome.Push, _round.Hands[0].Outcome);
            Assert.AreEqual(900m, _account.Chips, "push returns the stake");
        }

        [Test]
        public void Blackjack_PaysThreeToTwo_AndDealerNaturalBeatsEverythingElse()
        {
            Stack("A", "9", "K", "7");
            _round.Deal(100m);
            Assert.AreEqual(BlackjackPhase.Settled, _round.Phase, "a natural ends the round at once");
            Assert.AreEqual(HandOutcome.Blackjack, _round.Hands[0].Outcome);
            Assert.AreEqual(1_150m, _account.Chips);

            Stack("10", "A", "9", "K"); // dealer peeks: natural
            _round.Deal(100m);
            Assert.AreEqual(BlackjackPhase.Settled, _round.Phase);
            Assert.AreEqual(HandOutcome.Lose, _round.Hands[0].Outcome);
            Assert.AreEqual(1_050m, _account.Chips);

            Stack("A", "A", "K", "K"); // both naturals: push
            _round.Deal(100m);
            Assert.AreEqual(HandOutcome.Push, _round.Hands[0].Outcome);
            Assert.AreEqual(1_050m, _account.Chips);
        }

        [Test]
        public void ThreeToTwo_OnAFiveDollarBet_PaysCents()
        {
            _round = new BlackjackRound(new BlackjackRules { MinBet = 5m, MaxBet = 500m }, _shoe, _account, () => Now);
            Stack("A", "9", "K", "7");
            _round.Deal(5m);
            Assert.AreEqual(1_007.50m, _account.Chips, "$5 natural pays $7.50");
            Assert.IsNull(_account.CashOut(7.50m, Now, (a, w) => { }), "cents cash out");
        }

        [Test]
        public void Bust_LosesEvenIfTheDealerBusts()
        {
            Stack("10", "6", "6", "10", "K"); // player 16 hits a K → 26
            _round.Deal(100m);
            _round.Hit();
            Assert.AreEqual(BlackjackPhase.Settled, _round.Phase);
            Assert.AreEqual(HandOutcome.Bust, _round.Hands[0].Outcome);
            Assert.AreEqual(2, _round.Dealer.Count, "dealer doesn't draw when every hand busted");
            Assert.AreEqual(900m, _account.Chips);
        }

        [Test]
        public void DoubleDown_DoublesTheStake_ForOneCard()
        {
            Stack("6", "6", "5", "10", "10", "10"); // 11 doubles to 21; dealer 16 draws 10 → bust
            _round.Deal(100m);
            Assert.IsTrue(_round.CanDouble);
            _round.Double();
            Assert.AreEqual(BlackjackPhase.Settled, _round.Phase);
            Assert.AreEqual(200m, _round.Hands[0].Bet);
            Assert.AreEqual(3, _round.Hands[0].Cards.Count);
            Assert.AreEqual(HandOutcome.Win, _round.Hands[0].Outcome);
            Assert.AreEqual(1_200m, _account.Chips);
        }

        [Test]
        public void Split_PlaysTwoHands_WithSeparateStakes()
        {
            // 8,8 vs dealer 10 (hole 7 = 17). Split: hand 1 gets 3 (11) then doubles into 10 (21); hand 2 gets K (18).
            Stack("8", "10", "8", "7", "3", "K", "10");
            _round.Deal(100m);
            Assert.IsTrue(_round.CanSplit);
            _round.Split();
            Assert.AreEqual(2, _round.Hands.Count);
            Assert.AreEqual(800m, _account.Chips);
            _round.Double(); // double after split allowed
            _round.Stand();  // second hand 18
            Assert.AreEqual(BlackjackPhase.Settled, _round.Phase);
            Assert.AreEqual(HandOutcome.Win, _round.Hands[0].Outcome);
            Assert.AreEqual(HandOutcome.Win, _round.Hands[1].Outcome);
            Assert.AreEqual(300m, _round.TotalWagered);
            Assert.AreEqual(1_300m, _account.Chips, "+200 on the doubled hand, +100 on the other");
        }

        [Test]
        public void SplitAces_GetOneCardEach_AndTwentyOneIsNotBlackjack()
        {
            Stack("A", "9", "A", "9", "K", "5"); // dealer 18
            _round.Deal(100m);
            _round.Split();
            Assert.AreEqual(BlackjackPhase.Settled, _round.Phase, "split aces stand automatically");
            Assert.AreEqual(HandOutcome.Win, _round.Hands[0].Outcome, "A+K after a split is 21, paid 1:1");
            Assert.AreEqual(HandOutcome.Lose, _round.Hands[1].Outcome, "A+5 = 16 loses to 18");
            Assert.AreEqual(1_000m, _account.Chips);
        }

        [Test]
        public void InsufficientChips_AndTableLimits_AreRefused()
        {
            Assert.IsNotNull(_round.Deal(10m), "under the minimum");
            Assert.IsNotNull(_round.Deal(5_000m), "over the maximum");
            Assert.IsNotNull(_round.Deal(-100m));
            var broke = new CasinoAccount();
            var round = new BlackjackRound(new BlackjackRules(), _shoe, broke, () => Now);
            StringAssert.Contains("Not enough chips", round.Deal(25m));
            Assert.AreEqual(0m, broke.Chips);
        }

        [Test]
        public void Cage_BuysAndCashesOut_Exactly()
        {
            var a = new CasinoAccount();
            decimal bank = 10_000m;
            Func<decimal, string, string> charge = (amt, w) => { if (amt > bank) return "declined"; bank -= amt; return null; };
            Assert.IsNull(a.BuyChips(2_000m, Now, charge));
            Assert.AreEqual(8_000m, bank);
            Assert.AreEqual(2_000m, a.Chips);
            Assert.IsNotNull(a.BuyChips(20_000m, Now, charge), "bank declines");
            Assert.IsNotNull(a.BuyChips(0.5m, Now, charge), "no fractional buys");
            Assert.IsNotNull(a.CashOut(2_001m, Now, (amt, w) => bank += amt), "can't cash out more than you hold");
            Assert.IsNull(a.CashOut(2_000m, Now, (amt, w) => bank += amt));
            Assert.AreEqual(10_000m, bank);
            Assert.AreEqual(0m, a.Chips);
            Assert.IsNotNull(a.CashOut(1m, Now, (amt, w) => bank += amt), "the same chips can't be cashed twice");
        }

        [Test]
        public void HandInProgress_SurvivesSaveAndLoad_WithTheSameCards()
        {
            Stack("10", "9", "6", "7"); // 16 vs 9 up
            _round.Deal(100m);
            CasinoAccountSaveData chips = _account.CaptureState();
            BlackjackRoundSaveData hand = _round.CaptureState();
            Card next = _shoe.Draw(); // what the next hit would have been... (not stacked: from the shoe)

            var account = new CasinoAccount();
            account.RestoreState(chips);
            var shoe = new Shoe(6, 0.75, new SeededRandom(99)); // a different shuffle before restoring
            var round = new BlackjackRound(new BlackjackRules { MinBet = 25m, MaxBet = 2_500m }, shoe, account, () => Now);
            round.RestoreState(hand);
            Assert.AreEqual(900m, account.Chips, "stake already taken, not refunded");
            Assert.AreEqual(BlackjackPhase.PlayerTurn, round.Phase);
            Assert.AreEqual(16, round.Hands[0].Total);
            round.Hit();
            Assert.AreEqual(next, round.Hands[0].Cards[2], "a reload can't re-roll the hit");
        }

        [Test]
        public void HouseEdge_ComesFromTheRules_OverManyHands()
        {
            // Mimic-the-dealer play (hit to 17, never double or split) loses about 5.5% of what it bets.
            var account = new CasinoAccount();
            account.BuyChips(10_000_000m, Now, (a, w) => null);
            var round = new BlackjackRound(new BlackjackRules { MinBet = 10m, MaxBet = 10m }, new Shoe(6, 0.75, new SeededRandom(2030)), account, () => Now);
            const int hands = 200_000;
            for (int i = 0; i < hands; i++)
            {
                round.Deal(10m);
                while (round.Phase == BlackjackPhase.PlayerTurn)
                    if (round.Current.Total < 17) round.Hit();
                    else round.Stand();
            }
            decimal edge = account.NetResult / account.TotalWagered;
            Assert.Less(edge, -0.02m, $"house keeps an edge ({edge:P2})");
            Assert.Greater(edge, -0.09m, $"but a realistic one ({edge:P2})");
        }
    }
}
