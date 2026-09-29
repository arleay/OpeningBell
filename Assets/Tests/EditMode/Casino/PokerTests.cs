using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Casino;
using OpeningBell.Casino.Poker;
using OpeningBell.Core;

namespace OpeningBell.Tests
{
    /// <summary>CASINO_SPEC §87: hand rankings, ties and kickers, split and side pots, betting rounds, blinds, all-ins.</summary>
    public class PokerTests
    {
        private static readonly DateTime Now = new DateTime(2030, 1, 9, 21, 0, 0);

        /// <summary>"Ah", "Td", "2c": rank then suit.</summary>
        private static Card C(string s)
        {
            string r = s.Substring(0, s.Length - 1);
            int rank = r switch { "A" => 1, "K" => 13, "Q" => 12, "J" => 11, "T" => 10, _ => int.Parse(r) };
            Suit suit = s[s.Length - 1] switch { 'c' => Suit.Clubs, 'd' => Suit.Diamonds, 'h' => Suit.Hearts, _ => Suit.Spades };
            return new Card(rank, suit);
        }

        private static HandValue H(string cards) => HandEvaluator.Evaluate(cards.Split(' ').Select(C).ToList());

        [Test]
        public void AllRankings_InOrder()
        {
            HandValue[] hands =
            {
                H("2c 5d 9h Js Kc 3d 7h"),      // high card
                H("2c 2d 9h Js Kc 3d 7h"),      // pair
                H("2c 2d 9h 9s Kc 3d 7h"),      // two pair
                H("2c 2d 2h 9s Kc 3d 7h"),      // trips
                H("5c 6d 7h 8s 9c 2d Kh"),      // straight
                H("2h 5h 9h Jh Kh 3d 7c"),      // flush
                H("2c 2d 2h 9s 9c 3d 7h"),      // full house
                H("2c 2d 2h 2s Kc 3d 7h"),      // quads
                H("5h 6h 7h 8h 9h 2d Kc"),      // straight flush
            };
            for (int i = 0; i < hands.Length; i++)
            {
                Assert.AreEqual((HandCategory)i, hands[i].Category, hands[i].Name);
                if (i > 0) Assert.Greater(hands[i].Score, hands[i - 1].Score, $"{hands[i].Name} beats {hands[i - 1].Name}");
            }
            Assert.AreEqual("Royal flush", H("Th Jh Qh Kh Ah 2c 3d").Name);
            Assert.AreEqual("Full house, Twos over Nines", hands[6].Name);
        }

        [Test]
        public void Kickers_And_Ties()
        {
            Assert.Greater(H("Ah Ad Kc 7s 4d 3c 2h").Score, H("Ah Ad Qc 7s 4d 3c 2h").Score, "pair of aces, king kicker wins");
            Assert.Greater(H("9h 9d 5c 5s Kd 3c 2h").Score, H("9h 9d 5c 5s Qd 3c 2h").Score, "two pair kicker");
            Assert.Greater(H("9h 9d 5c 5s 4d 4c 2h").Score, H("9h 9d 5c 5s 3d 3c 2h").Score - 1, "best two pair used");
            Assert.AreEqual(H("9h 9d 5c 5s 4d 4c Kh").Ranks[2], 13, "third pair doesn't beat a king kicker");
            Assert.Greater(H("2h 5h 9h Jh Ah").Score, H("3h 5h 9h Jh Kh").Score, "flushes compare from the top");
            Assert.Greater(H("3c 3d 3h Ks Kc").Score, H("2c 2d 2h As Ac").Score, "full house: trips first");
            Assert.AreEqual(H("Ac Kd Qh Js 9c 3d 2h").Score, H("As Kh Qd Jc 9d 4s 2c").Score, "suits never break ties");
            Assert.AreEqual(H("Ks Kh 7c 7d 2s").Score, H("Kd Kc 7h 7s 2c").Score);
        }

        [Test]
        public void Straights_AceHighAndLow()
        {
            HandValue wheel = H("Ah 2d 3c 4s 5h Kd Kc");
            Assert.AreEqual(HandCategory.Straight, wheel.Category);
            Assert.AreEqual(5, wheel.Ranks[0], "the wheel is five-high");
            Assert.Greater(H("2h 3d 4c 5s 6h").Score, wheel.Score);
            Assert.AreEqual(14, H("Th Jd Qc Ks Ah").Ranks[0]);
            Assert.AreEqual(HandCategory.HighCard, H("Qh Kd Ac 2s 3h").Category, "no wrap-around straights");
            Assert.AreEqual(HandCategory.StraightFlush, H("Ah 2h 3h 4h 5h 9c").Category, "steel wheel");
        }

        private static HoldemTable Table(params decimal[] stacks)
        {
            var t = new HoldemTable(stacks.Length, 1m, 2m, new SeededRandom(1));
            for (int i = 0; i < stacks.Length; i++) t.Seats[i] = new PokerSeat { Name = "P" + i, Stack = stacks[i] };
            return t;
        }

        private static decimal Chips(HoldemTable t) => t.Seats.Where(s => s != null).Sum(s => s.Stack) + t.Pot;

        [Test]
        public void HeadsUp_ButtonPostsSmallBlind_ActsFirstPreflop_LastAfter()
        {
            HoldemTable t = Table(200m, 200m);
            Assert.IsTrue(t.StartHand());
            Assert.AreEqual(0, t.Button);
            Assert.AreEqual(1m, t.Seats[0].Street, "button is the small blind");
            Assert.AreEqual(2m, t.Seats[1].Street);
            Assert.AreEqual(0, t.ToAct);
            Assert.IsNull(t.Act(0, PokerMove.Call));
            Assert.AreEqual(1, t.ToAct, "big blind has the option");
            Assert.IsNull(t.Act(1, PokerMove.Check));
            Assert.AreEqual(PokerStreet.Flop, t.Street);
            Assert.AreEqual(3, t.Board.Count);
            Assert.AreEqual(1, t.ToAct, "big blind first after the flop");
            Assert.AreEqual(400m, Chips(t));
        }

        [Test]
        public void Fold_WinsUncontested_UncalledBetReturned_NoRakeWithoutAFlop()
        {
            var t = new HoldemTable(3, 1m, 2m, new SeededRandom(2), 0.05m, 4m);
            for (int i = 0; i < 3; i++) t.Seats[i] = new PokerSeat { Name = "P" + i, Stack = 100m };
            t.StartHand(); // button 0, SB 1, BB 2, seat 0 first
            Assert.IsNull(t.Act(0, PokerMove.Raise, 10m));
            Assert.IsNull(t.Act(1, PokerMove.Fold));
            Assert.IsNull(t.Act(2, PokerMove.Fold));
            Assert.IsTrue(t.HandOver);
            Assert.AreEqual(8m, t.Uncalled, "raised to 10, called by nobody beyond the 2 blind");
            Assert.AreEqual(0m, t.Rake);
            Assert.AreEqual(103m, t.Seats[0].Stack, "wins the blinds");
            Assert.AreEqual(300m, t.Seats.Sum(s => s.Stack));
            Assert.IsFalse(t.ShowdownReached);
        }

        [Test]
        public void MinimumRaise_IsTheLastRaiseSize()
        {
            HoldemTable t = Table(500m, 500m, 500m);
            t.StartHand();
            Assert.IsNotNull(t.Act(0, PokerMove.Raise, 3m), "a raise must be at least another big blind");
            Assert.IsNull(t.Act(0, PokerMove.Raise, 10m)); // raise of 8
            Assert.AreEqual(18m, t.MinRaiseTo);
            Assert.IsNotNull(t.Act(1, PokerMove.Raise, 15m));
            Assert.IsNull(t.Act(1, PokerMove.Raise, 30m)); // raise of 20
            Assert.AreEqual(50m, t.MinRaiseTo);
            Assert.IsNotNull(t.Act(2, PokerMove.Check), "can't check facing a bet");
        }

        [Test]
        public void ShortAllIn_DoesNotReopenTheBetting()
        {
            HoldemTable t = Table(1_000m, 1_000m, 120m);
            t.StartHand(); // button 0, SB 1, BB 2; seat 0 first
            t.Act(0, PokerMove.Call);
            t.Act(1, PokerMove.Call);
            t.Act(2, PokerMove.Check);
            Assert.AreEqual(PokerStreet.Flop, t.Street);
            Assert.AreEqual(1, t.ToAct);
            t.Act(1, PokerMove.Bet, 100m);           // full bet
            Assert.IsNull(t.Act(2, PokerMove.AllIn)); // 118 total: a raise of 18, less than 100 → short
            Assert.AreEqual(118m, t.CurrentBet);
            Assert.IsTrue(t.CanRaise(0), "seat 0 hasn't acted yet: may raise");
            Assert.IsNull(t.Act(0, PokerMove.Call));
            Assert.AreEqual(1, t.ToAct);
            Assert.IsFalse(t.CanRaise(1), "the bettor only faces a short all-in: call or fold");
            Assert.IsNotNull(t.Act(1, PokerMove.Raise, 400m));
            Assert.IsNull(t.Act(1, PokerMove.Call));
            Assert.AreEqual(PokerStreet.Turn, t.Street);
        }

        [Test]
        public void SidePots_PayTheRightPeople()
        {
            HoldemTable t = Table(50m, 100m, 200m);
            // Hole cards deal from the small blind (seat 1) round: 1, 2, 0, 1, 2, 0. Board after.
            t.StackDeck(C("Kh"), C("2c"), C("Ah"), C("Kd"), C("7d"), C("Ad"),
                C("As"), C("Ks"), C("9c"), C("5h"), C("3s"));
            t.StartHand();
            t.Act(0, PokerMove.AllIn);  // 50
            t.Act(1, PokerMove.AllIn);  // 100
            t.Act(2, PokerMove.Call);   // 100 (200 stack): 100 uncalled? no: calls 100, keeps 100
            Assert.IsTrue(t.HandOver);
            // Seat 0 (AA → trips aces) wins the main pot 150; seat 1 (KK → trips kings) wins the side pot 100.
            Assert.AreEqual(2, t.Pots.Count);
            Assert.AreEqual(150m, t.Pots[0].Amount);
            Assert.AreEqual(100m, t.Pots[1].Amount);
            Assert.AreEqual(150m, t.Seats[0].Stack);
            Assert.AreEqual(100m, t.Seats[1].Stack);
            Assert.AreEqual(100m, t.Seats[2].Stack);
            Assert.AreEqual(350m, t.Seats.Sum(s => s.Stack));
        }

        [Test]
        public void SplitPot_OddChipGoesLeftOfTheButton()
        {
            HoldemTable t = Table(101m, 101m, 101m);
            // Everyone plays the board's broadway straight.
            t.StackDeck(C("2c"), C("3c"), C("4d"), C("2d"), C("3d"), C("5h"),
                C("Ts"), C("Jd"), C("Qc"), C("Kh"), C("Ac"));
            t.StartHand();
            t.Act(0, PokerMove.AllIn);
            t.Act(1, PokerMove.AllIn);
            t.Act(2, PokerMove.AllIn);
            Assert.AreEqual(1, t.Pots.Count);
            Assert.AreEqual(3, t.Pots[0].Winners.Count);
            Assert.AreEqual(303m, t.Seats.Sum(s => s.Stack));
            Assert.IsTrue(t.Seats.All(s => s.Stack == 101m), "303 three ways");

            HoldemTable two = Table(100m, 100m, 1m);
            two.StackDeck(C("2c"), C("3c"), C("4d"), C("2d"), C("3d"), C("5h"), C("Ts"), C("Jd"), C("Qc"), C("Kh"), C("Ac"));
            two.StartHand(); // seat 2 posts a 1 all-in big blind (short)
            two.Act(0, PokerMove.Call);
            two.Act(1, PokerMove.Call);
            two.Act(1, PokerMove.Check);
            while (!two.HandOver) two.Act(two.ToAct, PokerMove.Check);
            Assert.AreEqual(201m, two.Seats.Sum(s => s.Stack), "chips conserved through an odd split");
        }

        [Test]
        public void Rake_IsTakenOnceAFlopIsSeen_Capped()
        {
            var t = new HoldemTable(2, 1m, 2m, new SeededRandom(8), 0.05m, 4m);
            t.Seats[0] = new PokerSeat { Name = "A", Stack = 500m };
            t.Seats[1] = new PokerSeat { Name = "B", Stack = 500m };
            t.StartHand();
            t.Act(0, PokerMove.Raise, 50m);
            t.Act(1, PokerMove.Call);
            while (!t.HandOver) t.Act(t.ToAct, PokerMove.Check);
            Assert.AreEqual(4m, t.Rake, "5% of 100 is 5, capped at 4");
            Assert.AreEqual(996m, t.Seats.Sum(s => s.Stack));
        }

        [Test]
        public void NpcsOnly_ThousandsOfHands_AlwaysLegal_ChipsConserved()
        {
            var rng = new SeededRandom(2030);
            var t = new HoldemTable(6, 1m, 2m, rng, 0.05m, 4m);
            for (int i = 0; i < 6; i++) t.Seats[i] = new PokerSeat { Name = "N" + i, Stack = 200m, Brain = new PokerBrain(PokerStyle.All[i % PokerStyle.All.Length], rng) };
            decimal total = 1_200m, raked = 0m;
            int hands = 0, showdowns = 0;
            for (int h = 0; h < 1_500; h++)
            {
                for (int i = 0; i < 6; i++)
                    if (t.Seats[i].Stack < 20m) t.Seats[i].Stack += 200m - t.Seats[i].Stack; // rebuy
                total = t.Seats.Sum(s => s.Stack) + raked;
                if (!t.StartHand()) break;
                hands++;
                int guard = 0;
                while (!t.HandOver && guard++ < 200)
                {
                    int seat = t.ToAct;
                    (PokerMove move, decimal to) = t.Seats[seat].Brain.Decide(t, seat);
                    Assert.IsNull(t.Act(seat, move, to), $"{t.Seats[seat].Brain.Style.Name} chose an illegal {move} to {to}");
                }
                Assert.IsTrue(t.HandOver, "every hand ends");
                raked += t.Rake;
                if (t.ShowdownReached) showdowns++;
                Assert.AreEqual(total, t.Seats.Sum(s => s.Stack) + raked, $"hand {h}: chips conserved");
            }
            Assert.Greater(hands, 1_000);
            Assert.Greater(showdowns, 50, "hands reach showdown");
            Assert.Less(showdowns, hands, "and many are won without one");
        }

        [Test]
        public void Npc_DecisionsDependOnlyOnWhatTheyCanSee()
        {
            // Same table, same NPC, same random stream: changing the other players' hidden cards changes nothing.
            (PokerMove, decimal) Decide(string otherHole)
            {
                HoldemTable t = Table(300m, 300m, 300m);
                t.Seats[1].Brain = new PokerBrain(PokerStyle.LooseAggressive, new SeededRandom(42));
                t.StartHand();
                t.Act(0, PokerMove.Call);
                t.Seats[1].Hole[0] = C("Ah");
                t.Seats[1].Hole[1] = C("Qh");
                t.Seats[2].Hole[0] = C(otherHole.Split(' ')[0]);
                t.Seats[2].Hole[1] = C(otherHole.Split(' ')[1]);
                return t.Seats[1].Brain.Decide(t, 1);
            }
            Assert.AreEqual(Decide("2c 7d"), Decide("As Ad"));
        }

        [Test]
        public void CashGame_BuyInAndLeave_ThroughTheChips()
        {
            var account = new CasinoAccount();
            account.BuyChips(1_000m, Now, (a, w) => null);
            var game = new PokerCashGame(PokerStakes.OneTwo, account, new SeededRandom(5), () => Now);
            Assert.IsNotNull(game.Sit(20m), "under the minimum buy-in");
            Assert.IsNull(game.Sit(200m));
            Assert.AreEqual(800m, account.Chips);
            Assert.IsTrue(game.NextHand());
            Assert.IsFalse(game.CanLeave, "mid-hand");
            while (!game.Table.HandOver)
            {
                game.RunNpcs();
                if (!game.Table.HandOver && game.Table.ToAct == PokerCashGame.PlayerSeat) game.Table.Act(0, PokerMove.Fold);
            }
            decimal stack = game.PlayerStack;
            Assert.IsNull(game.Leave());
            Assert.AreEqual(800m + stack, account.Chips);
            Assert.AreEqual(stack - 200m, account.PokerNet);
            Assert.AreEqual(1, account.Count(CasinoGame.Poker));
        }

        [Test]
        public void SitAndGo_EntryFromChips_EndsWithAPlace()
        {
            var account = new CasinoAccount();
            account.BuyChips(1_000m, Now, (a, w) => null);
            var sng = new SitAndGo(account, new SeededRandom(6), () => Now);
            Assert.IsNull(sng.Register());
            Assert.AreEqual(890m, account.Chips, "$100 entry + $10 fee");
            int hands = 0;
            while (!sng.Finished && hands++ < 3_000)
            {
                Assert.IsTrue(sng.NextHand());
                while (!sng.Table.HandOver)
                {
                    if (sng.Table.ToAct == 0)
                    {
                        // The player goes all in with any pair or ace, folds the rest.
                        List<Card> hole = sng.Table.Seats[0].Hole;
                        bool strong = hole[0].Rank == hole[1].Rank || hole[0].IsAce || hole[1].IsAce;
                        if (sng.Table.Act(0, strong ? PokerMove.AllIn : sng.Table.CanCheck(0) ? PokerMove.Check : PokerMove.Fold) != null)
                            sng.Table.Act(0, sng.Table.CanCheck(0) ? PokerMove.Check : PokerMove.Call); // a short all-in closed raising
                    }
                    else sng.StepNpc();
                }
                Assert.AreEqual(SitAndGo.StartingChips * SitAndGo.Players, sng.Table.Seats.Where(s => s != null).Sum(s => s.Stack), "tournament chips conserved");
                sng.AfterHand();
            }
            Assert.IsTrue(sng.Finished);
            Assert.That(sng.Place, Is.InRange(1, 6));
            decimal expected = sng.Place == 1 ? 390m : sng.Place == 2 ? 210m : 0m;
            Assert.AreEqual(expected, sng.Prize);
            Assert.AreEqual(890m + expected, account.Chips);
        }
    }
}
