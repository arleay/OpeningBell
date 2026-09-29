using System;
using System.Collections.Generic;

namespace OpeningBell.Casino
{
    /// <summary>A table's rules (CASINO_SPEC §18, §21–22). Defaults: a common Las Vegas six-deck shoe game.</summary>
    [Serializable]
    public sealed class BlackjackRules
    {
        public int Decks = 6;
        /// <summary>Share of the shoe dealt before the cut card calls a reshuffle.</summary>
        public double Penetration = 0.75;
        /// <summary>False: the dealer stands on all 17s (S17). True: hits soft 17 (H17).</summary>
        public bool DealerHitsSoft17;
        /// <summary>A natural pays this times the bet (1.5 = 3:2; 1.2 = the worse 6:5).</summary>
        public decimal BlackjackPays = 1.5m;
        public bool DoubleAfterSplit = true;
        public int MaxHands = 4;
        /// <summary>Split aces get one card each and can't be hit or re-split (almost universal).</summary>
        public bool SplitAcesOneCard = true;
        public decimal MinBet = 5m;
        public decimal MaxBet = 500m;

        /// <summary>"Dealer stands on all 17s · Blackjack pays 3 to 2" for the felt.</summary>
        public string Felt => (DealerHitsSoft17 ? "DEALER HITS SOFT 17" : "DEALER MUST STAND ON ALL 17s") + "  ·  BLACKJACK PAYS " +
                              (BlackjackPays == 1.5m ? "3 TO 2" : BlackjackPays == 1.2m ? "6 TO 5" : BlackjackPays + " TO 1");
    }

    public enum HandOutcome { Pending, Blackjack, Win, Push, Lose, Bust }

    /// <summary>One player hand: its cards, its stake (doubles and splits carry their own).</summary>
    public sealed class BlackjackHand
    {
        public readonly List<Card> Cards = new List<Card>();
        public decimal Bet;
        public bool Doubled;
        public bool FromSplit;
        public bool SplitAces;
        public bool Done;
        public HandOutcome Outcome = HandOutcome.Pending;
        public decimal Returned;

        public int Total => BlackjackMath.Total(Cards, out _);
        public bool IsSoft { get { BlackjackMath.Total(Cards, out bool soft); return soft; } }
        public bool Busted => Total > 21;
        /// <summary>A natural: two cards making 21 on the original hand (21 after a split is just 21).</summary>
        public bool IsBlackjack => !FromSplit && Cards.Count == 2 && Total == 21;
    }

    public static class BlackjackMath
    {
        /// <summary>
        /// Best total: aces count 1, and one of them counts 11 when that doesn't bust (then the hand is soft).
        /// A + 9 = 20 (soft); A + 9 + 5 = 15 (hard); A + A = 12 (soft).
        /// </summary>
        public static int Total(IReadOnlyList<Card> cards, out bool soft)
        {
            int total = 0;
            bool ace = false;
            foreach (Card c in cards)
            {
                total += c.Points;
                if (c.IsAce) ace = true;
            }
            soft = ace && total + 10 <= 21;
            return soft ? total + 10 : total;
        }
    }

    public enum BlackjackPhase { Betting, PlayerTurn, Settled }

    /// <summary>
    /// One round at the table, from bet to settlement (CASINO_SPEC §17–22). The rules decide everything here, in
    /// order: deal, the dealer peeks for a natural under an ace or ten, the player plays each hand (hit, stand,
    /// double, split), the dealer draws to 17, then every hand settles against the dealer in one step. Stakes come
    /// off the chips when they're placed; winnings go back in <see cref="Settle"/>. The dealer and the cards on the
    /// table only show what already happened.
    /// </summary>
    public sealed class BlackjackRound
    {
        private readonly BlackjackRules _rules;
        private readonly Shoe _shoe;
        private readonly CasinoAccount _account;
        private readonly Func<DateTime> _now;
        private readonly List<BlackjackHand> _hands = new List<BlackjackHand>();
        private readonly List<Card> _dealer = new List<Card>();

        public BlackjackPhase Phase { get; private set; } = BlackjackPhase.Betting;
        public IReadOnlyList<BlackjackHand> Hands => _hands;
        public IReadOnlyList<Card> Dealer => _dealer;
        public int ActiveHand { get; private set; }
        public BlackjackRules Rules => _rules;

        /// <summary>The hole card is face down until the player's hands are done.</summary>
        public bool HoleRevealed { get; private set; }
        public int DealerTotal => BlackjackMath.Total(_dealer, out _);
        public bool DealerBlackjack => _dealer.Count == 2 && DealerTotal == 21;

        /// <summary>Everything this round took and gave back (for the result line).</summary>
        public decimal TotalWagered { get; private set; }
        public decimal TotalReturned { get; private set; }

        public event Action Changed;
        private bool _reshuffle;

        public BlackjackRound(BlackjackRules rules, Shoe shoe, CasinoAccount account, Func<DateTime> now)
        {
            _rules = rules;
            _shoe = shoe;
            _account = account;
            _now = now;
        }

        public BlackjackHand Current => Phase == BlackjackPhase.PlayerTurn ? _hands[ActiveHand] : null;

        public string Deal(decimal bet)
        {
            if (Phase == BlackjackPhase.PlayerTurn) return "Finish this hand first.";
            if (bet < _rules.MinBet || bet > _rules.MaxBet) return $"This table takes ${_rules.MinBet:N0} to ${_rules.MaxBet:N0}.";
            if (bet != Math.Floor(bet)) return "Bets are whole dollars.";
            if (!_account.TryWager(bet, _now(), "Blackjack bet")) return _account.Refusal;

            // After a restored hand, reshuffle: a reload must not let anyone scout the shoe.
            if (_shoe.PastCut || _reshuffle) _shoe.Shuffle();
            _reshuffle = false;
            _hands.Clear();
            _dealer.Clear();
            TotalWagered = bet;
            TotalReturned = 0m;
            HoleRevealed = false;
            var hand = new BlackjackHand { Bet = bet };
            _hands.Add(hand);
            ActiveHand = 0;
            // Player, dealer up, player, dealer hole.
            hand.Cards.Add(_shoe.Draw());
            _dealer.Add(_shoe.Draw());
            hand.Cards.Add(_shoe.Draw());
            _dealer.Add(_shoe.Draw());
            Phase = BlackjackPhase.PlayerTurn;

            // The dealer checks under an ace or ten; a natural on either side ends the round now.
            bool peek = _dealer[0].IsAce || _dealer[0].Points == 10;
            if ((peek && DealerBlackjack) || hand.IsBlackjack)
            {
                hand.Done = true;
                Finish();
                return null;
            }
            if (hand.Total == 21) hand.Done = true;
            Changed?.Invoke();
            return null;
        }

        public bool CanHit => Current != null && !(Current.SplitAces && _rules.SplitAcesOneCard);
        public bool CanStand => Current != null;

        public bool CanDouble =>
            Current != null && Current.Cards.Count == 2 && !Current.SplitAces &&
            (!Current.FromSplit || _rules.DoubleAfterSplit) && _account.Chips >= Current.Bet;

        public bool CanSplit =>
            Current != null && Current.Cards.Count == 2 && Current.Cards[0].Points == Current.Cards[1].Points &&
            _hands.Count < _rules.MaxHands && !(Current.SplitAces) && _account.Chips >= Current.Bet;

        public string Hit()
        {
            if (!CanHit) return "You can't hit this hand.";
            BlackjackHand h = Current;
            h.Cards.Add(_shoe.Draw());
            if (h.Total >= 21) h.Done = true;
            Advance();
            return null;
        }

        public string Stand()
        {
            if (!CanStand) return "Nothing to stand on.";
            Current.Done = true;
            Advance();
            return null;
        }

        /// <summary>Doubles the stake for exactly one more card.</summary>
        public string Double()
        {
            if (!CanDouble) return "You can't double this hand.";
            BlackjackHand h = Current;
            if (!_account.TryWager(h.Bet, _now(), "Blackjack double")) return "Not enough chips to double.";
            TotalWagered += h.Bet;
            h.Bet *= 2m;
            h.Doubled = true;
            h.Cards.Add(_shoe.Draw());
            h.Done = true;
            Advance();
            return null;
        }

        /// <summary>Splits a pair into two hands, each with the original stake and a second card.</summary>
        public string Split()
        {
            if (!CanSplit) return "You can't split this hand.";
            BlackjackHand h = Current;
            if (!_account.TryWager(h.Bet, _now(), "Blackjack split")) return "Not enough chips to split.";
            TotalWagered += h.Bet;
            bool aces = h.Cards[0].IsAce;
            var second = new BlackjackHand { Bet = h.Bet, FromSplit = true, SplitAces = aces };
            second.Cards.Add(h.Cards[1]);
            h.Cards.RemoveAt(1);
            h.FromSplit = true;
            h.SplitAces = aces;
            _hands.Insert(ActiveHand + 1, second);
            h.Cards.Add(_shoe.Draw());
            second.Cards.Add(_shoe.Draw());
            foreach (BlackjackHand x in new[] { h, second })
                if ((aces && _rules.SplitAcesOneCard) || x.Total == 21) x.Done = true;
            Advance();
            return null;
        }

        /// <summary>Moves to the next unfinished hand, or plays the dealer and settles when none are left.</summary>
        private void Advance()
        {
            while (ActiveHand < _hands.Count && _hands[ActiveHand].Done) ActiveHand++;
            if (ActiveHand >= _hands.Count) Finish();
            else Changed?.Invoke();
        }

        private void Finish()
        {
            ActiveHand = Math.Min(ActiveHand, _hands.Count - 1);
            HoleRevealed = true;
            bool anyLive = _hands.Exists(h => !h.Busted) && !(_hands.Count == 1 && _hands[0].IsBlackjack);
            if (anyLive && !DealerBlackjack)
                while (true)
                {
                    int total = BlackjackMath.Total(_dealer, out bool soft);
                    if (total < 17 || (total == 17 && soft && _rules.DealerHitsSoft17)) _dealer.Add(_shoe.Draw());
                    else break;
                }
            Settle();
        }

        /// <summary>Every hand against the dealer at once; the total returned goes back on the chips in one step.</summary>
        private void Settle()
        {
            int dealer = DealerTotal;
            bool dealerBust = dealer > 21;
            bool dealerNatural = DealerBlackjack;
            decimal returned = 0m;
            foreach (BlackjackHand h in _hands)
            {
                if (h.Busted) h.Outcome = HandOutcome.Bust;
                else if (h.IsBlackjack) h.Outcome = dealerNatural ? HandOutcome.Push : HandOutcome.Blackjack;
                else if (dealerNatural) h.Outcome = HandOutcome.Lose;
                else if (dealerBust || h.Total > dealer) h.Outcome = HandOutcome.Win;
                else if (h.Total == dealer) h.Outcome = HandOutcome.Push;
                else h.Outcome = HandOutcome.Lose;

                h.Returned = h.Outcome switch
                {
                    HandOutcome.Blackjack => h.Bet + Math.Round(h.Bet * _rules.BlackjackPays, 2),
                    HandOutcome.Win => h.Bet * 2m,
                    HandOutcome.Push => h.Bet,
                    _ => 0m,
                };
                returned += h.Returned;
                h.Done = true;
            }
            TotalReturned = returned;
            _account.Settle(CasinoGame.Blackjack, TotalWagered, returned, _now(), "Blackjack");
            Phase = BlackjackPhase.Settled;
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- save (a hand in progress survives a save)

        public BlackjackRoundSaveData CaptureState()
        {
            var data = new BlackjackRoundSaveData
            {
                Phase = (int)Phase, ActiveHand = ActiveHand, HoleRevealed = HoleRevealed,
                TotalWagered = TotalWagered.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            if (Phase == BlackjackPhase.PlayerTurn) (data.Shoe, data.ShoeNext) = _shoe.Capture();
            foreach (Card c in _dealer) data.Dealer.Add(c.Code);
            foreach (BlackjackHand h in _hands)
            {
                var hs = new BlackjackHandSaveData
                {
                    Bet = h.Bet.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Doubled = h.Doubled, FromSplit = h.FromSplit, SplitAces = h.SplitAces, Done = h.Done,
                };
                foreach (Card c in h.Cards) hs.Cards.Add(c.Code);
                data.Hands.Add(hs);
            }
            return data;
        }

        /// <summary>Only an unfinished hand needs restoring; settled rounds are already in the chips.</summary>
        public void RestoreState(BlackjackRoundSaveData data)
        {
            if ((BlackjackPhase)data.Phase != BlackjackPhase.PlayerTurn || data.Hands.Count == 0) return;
            _dealer.Clear();
            _hands.Clear();
            foreach (int code in data.Dealer) _dealer.Add(Card.FromCode(code));
            foreach (BlackjackHandSaveData hs in data.Hands)
            {
                var h = new BlackjackHand
                {
                    Bet = decimal.Parse(hs.Bet, System.Globalization.CultureInfo.InvariantCulture),
                    Doubled = hs.Doubled, FromSplit = hs.FromSplit, SplitAces = hs.SplitAces, Done = hs.Done,
                };
                foreach (int code in hs.Cards) h.Cards.Add(Card.FromCode(code));
                _hands.Add(h);
            }
            TotalWagered = decimal.Parse(data.TotalWagered, System.Globalization.CultureInfo.InvariantCulture);
            ActiveHand = data.ActiveHand;
            HoleRevealed = data.HoleRevealed;
            _shoe.Restore(data.Shoe, data.ShoeNext);
            _reshuffle = true;
            Phase = BlackjackPhase.PlayerTurn;
            Changed?.Invoke();
        }
    }

    [Serializable]
    public sealed class BlackjackRoundSaveData
    {
        public int Phase, ActiveHand;
        public bool HoleRevealed;
        public string TotalWagered;
        public List<int> Dealer = new List<int>();
        public List<int> Shoe = new List<int>();
        public int ShoeNext;
        public List<BlackjackHandSaveData> Hands = new List<BlackjackHandSaveData>();
    }

    [Serializable]
    public sealed class BlackjackHandSaveData
    {
        public string Bet;
        public bool Doubled, FromSplit, SplitAces, Done;
        public List<int> Cards = new List<int>();
    }
}

namespace OpeningBell.Casino
{
    /// <summary>
    /// Basic strategy for six decks, dealer stands on soft 17, double after split (CASINO_SPEC §100: an optional
    /// learning hint). It's the textbook chart: it lowers the house edge to about half a percent, never below zero.
    /// </summary>
    public static class BlackjackStrategy
    {
        public static string Advice(BlackjackHand hand, Card up, bool canDouble, bool canSplit)
        {
            int d = up.IsAce ? 11 : up.Points;
            if (canSplit && hand.Cards.Count == 2)
            {
                int p = hand.Cards[0].IsAce ? 11 : hand.Cards[0].Points;
                bool split = p switch
                {
                    11 or 8 => true,
                    9 => d != 7 && d != 10 && d != 11,
                    7 => d <= 7,
                    6 => d <= 6,
                    4 => d == 5 || d == 6,
                    3 or 2 => d <= 7,
                    _ => false,
                };
                if (split) return "Split";
            }
            int total = hand.Total;
            if (hand.IsSoft && total < 21)
            {
                int other = total - 11;
                if (other >= 8) return "Stand";
                if (other == 7) return d >= 3 && d <= 6 && canDouble ? "Double" : d <= 8 ? "Stand" : "Hit";
                if (other == 6) return d >= 3 && d <= 6 && canDouble ? "Double" : "Hit";
                if (other >= 4) return d >= 4 && d <= 6 && canDouble ? "Double" : "Hit";
                return d >= 5 && d <= 6 && canDouble ? "Double" : "Hit";
            }
            if (total >= 17) return "Stand";
            if (total >= 13) return d <= 6 ? "Stand" : "Hit";
            if (total == 12) return d >= 4 && d <= 6 ? "Stand" : "Hit";
            if (total == 11) return canDouble && d != 11 ? "Double" : "Hit";
            if (total == 10) return canDouble && d <= 9 ? "Double" : "Hit";
            if (total == 9) return canDouble && d >= 3 && d <= 6 ? "Double" : "Hit";
            return "Hit";
        }
    }
}
