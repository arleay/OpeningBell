using System;
using System.Collections.Generic;
using System.Linq;
using OpeningBell.Core;

namespace OpeningBell.Casino.Poker
{
    public enum PokerStreet { Waiting, Preflop, Flop, Turn, River, Showdown }

    public enum PokerMove { Fold, Check, Call, Bet, Raise, AllIn, SmallBlind, BigBlind }

    /// <summary>Someone at the table: their stack, cards and what they've put in this street and this hand.</summary>
    public sealed class PokerSeat
    {
        public string Name;
        public decimal Stack;
        public readonly List<Card> Hole = new List<Card>();
        public bool InHand, Folded, AllIn, Acted;
        /// <summary>Put in on this street, and in the whole hand.</summary>
        public decimal Street, Total;
        /// <summary>How many full raises there had been this street when this seat last acted (see <see cref="HoldemTable.CanRaise"/>).</summary>
        public int RaisesSeen;
        public PokerBrain Brain;
        public bool IsPlayer => Brain == null;
        public bool Live => InHand && !Folded;
    }

    public readonly struct PokerAction
    {
        public readonly int Seat;
        public readonly PokerMove Move;
        /// <summary>Chips put in by this action (calls, bets and raises: the amount added; blinds: the blind).</summary>
        public readonly decimal Added;
        /// <summary>For bets and raises: the total this street it raised to.</summary>
        public readonly decimal To;
        public readonly PokerStreet Street;

        public PokerAction(int seat, PokerMove move, decimal added, decimal to, PokerStreet street)
        {
            Seat = seat;
            Move = move;
            Added = added;
            To = to;
            Street = street;
        }
    }

    public sealed class PokerPot
    {
        public decimal Amount;
        public List<int> Eligible = new List<int>();
        public List<int> Winners = new List<int>();
        /// <summary>What each winner took from this pot.</summary>
        public Dictionary<int, decimal> Paid = new Dictionary<int, decimal>();
        public HandValue? WinningHand;
    }

    /// <summary>
    /// No-limit Texas Hold'em (CASINO_SPEC §36–40): button and blinds, hole cards, four betting rounds, fold, check,
    /// call, bet, raise and all-in with proper minimum raises (a short all-in doesn't reopen the betting), uncalled
    /// bets returned, side pots, split pots with odd chips to the first seat left of the button, and a capped rake
    /// once a flop is dealt. Whole dollars (or tournament chips) throughout. Chips are conserved: stacks + pot + rake
    /// never change within a hand.
    /// </summary>
    public sealed class HoldemTable
    {
        private readonly SeededRandom _rng;
        private readonly Shoe _deck;

        public PokerSeat[] Seats { get; }
        public decimal SmallBlind { get; set; }
        public decimal BigBlind { get; set; }
        public decimal RakeRate { get; set; }
        public decimal RakeCap { get; set; }
        public List<Card> Board { get; } = new List<Card>();
        public PokerStreet Street { get; private set; } = PokerStreet.Waiting;
        public int Button { get; private set; } = -1;
        public int ToAct { get; private set; } = -1;
        public decimal CurrentBet { get; private set; }
        /// <summary>Size of the last full bet or raise this street (the next raise must be at least this much more).</summary>
        public decimal MinRaise { get; private set; }
        public int FullRaises { get; private set; }
        public int HandNumber { get; private set; }
        public List<PokerAction> Log { get; } = new List<PokerAction>();
        public List<PokerPot> Pots { get; } = new List<PokerPot>();
        public decimal Rake { get; private set; }
        /// <summary>Returned to the last aggressor when nobody called all of it.</summary>
        public decimal Uncalled { get; private set; }
        public int UncalledSeat { get; private set; } = -1;
        public bool ShowdownReached { get; private set; }
        public bool HandOver => Street == PokerStreet.Showdown;

        public event Action<PokerAction> Acted;

        public HoldemTable(int seats, decimal smallBlind, decimal bigBlind, SeededRandom rng, decimal rakeRate = 0m, decimal rakeCap = 0m)
        {
            Seats = new PokerSeat[seats];
            SmallBlind = smallBlind;
            BigBlind = bigBlind;
            RakeRate = rakeRate;
            RakeCap = rakeCap;
            _rng = rng;
            _deck = new Shoe(1, 0.95, rng);
        }

        public decimal Pot
        {
            get
            {
                decimal p = 0m;
                foreach (PokerSeat s in Seats)
                    if (s != null) p += s.Total;
                return p;
            }
        }

        public int Next(int from, Func<PokerSeat, bool> ok)
        {
            for (int i = 1; i <= Seats.Length; i++)
            {
                int s = (from + i) % Seats.Length;
                if (Seats[s] != null && ok(Seats[s])) return s;
            }
            return -1;
        }

        /// <summary>Deals a new hand: moves the button, posts blinds, deals hole cards. False if fewer than two can play.</summary>
        public bool StartHand()
        {
            if (Seats.Count(s => s != null && s.Stack > 0m) < 2) return false;
            HandNumber++;
            Log.Clear();
            Pots.Clear();
            Board.Clear();
            Rake = 0m;
            Uncalled = 0m;
            UncalledSeat = -1;
            ShowdownReached = false;
            foreach (PokerSeat s in Seats)
            {
                if (s == null) continue;
                s.Hole.Clear();
                s.InHand = s.Stack > 0m;
                s.Folded = s.AllIn = s.Acted = false;
                s.Street = s.Total = 0m;
                s.RaisesSeen = 0;
            }
            _deck.Shuffle();
            Button = Next(Button < 0 ? Seats.Length - 1 : Button, s => s.InHand);
            bool headsUp = Seats.Count(s => s != null && s.InHand) == 2;
            // Heads-up the button is the small blind and acts first before the flop.
            int sb = headsUp ? Button : Next(Button, s => s.InHand);
            int bb = Next(sb, s => s.InHand);
            Street = PokerStreet.Preflop;
            CurrentBet = 0m;
            FullRaises = 0;
            Post(sb, SmallBlind, PokerMove.SmallBlind);
            Post(bb, BigBlind, PokerMove.BigBlind);
            CurrentBet = BigBlind;
            MinRaise = BigBlind;
            for (int round = 0; round < 2; round++)
                for (int i = 0, s = Next(Button, x => x.InHand); i < Seats.Count(x => x != null && x.InHand); i++, s = Next(s, x => x.InHand))
                    Seats[s].Hole.Add(_deck.Draw());
            ToAct = Next(bb, CanStillAct);
            if (ToAct < 0 || !NeedsToAct(ToAct)) Advance();
            return true;
        }

        private void Post(int seat, decimal blind, PokerMove move)
        {
            PokerSeat s = Seats[seat];
            decimal amount = Math.Min(blind, s.Stack);
            Commit(s, amount);
            Log.Add(new PokerAction(seat, move, amount, s.Street, Street));
        }

        private void Commit(PokerSeat s, decimal amount)
        {
            s.Stack -= amount;
            s.Street += amount;
            s.Total += amount;
            if (s.Stack <= 0m) s.AllIn = true;
        }

        private static bool CanStillAct(PokerSeat s) => s.Live && !s.AllIn;

        public PokerSeat Current => ToAct >= 0 ? Seats[ToAct] : null;
        public decimal ToCall(int seat) => Math.Max(0m, Math.Min(CurrentBet - Seats[seat].Street, Seats[seat].Stack));
        public bool CanCheck(int seat) => CurrentBet - Seats[seat].Street <= 0m;
        /// <summary>A raise is allowed unless the seat already acted and only a short all-in has come since.</summary>
        public bool CanRaise(int seat)
        {
            PokerSeat s = Seats[seat];
            if (s.Stack <= CurrentBet - s.Street) return false; // can only call (or less)
            return !s.Acted || s.RaisesSeen < FullRaises;
        }
        /// <summary>Least legal raise-to (or bet) this street; a smaller amount is only allowed as an all-in.</summary>
        public decimal MinRaiseTo => CurrentBet + MinRaise;
        public decimal MaxRaiseTo(int seat) => Seats[seat].Street + Seats[seat].Stack;

        private bool NeedsToAct(int seat)
        {
            PokerSeat s = Seats[seat];
            return CanStillAct(s) && (!s.Acted || s.Street < CurrentBet);
        }

        /// <summary>
        /// The seat to act does <paramref name="move"/>. For Bet/Raise <paramref name="to"/> is the street total to
        /// raise to. Null on success, else why it's not allowed (nothing changes).
        /// </summary>
        public string Act(int seat, PokerMove move, decimal to = 0m)
        {
            if (Street == PokerStreet.Waiting || HandOver) return "No hand in progress.";
            if (seat != ToAct) return "It's not that seat's turn.";
            PokerSeat s = Seats[seat];
            decimal toCall = CurrentBet - s.Street;
            switch (move)
            {
                case PokerMove.Fold:
                    s.Folded = true;
                    Record(seat, move, 0m, 0m);
                    break;
                case PokerMove.Check:
                    if (toCall > 0m) return "You can't check: there's a bet to you.";
                    Record(seat, move, 0m, 0m);
                    break;
                case PokerMove.Call:
                {
                    if (toCall <= 0m) return "Nothing to call.";
                    decimal amount = Math.Min(toCall, s.Stack);
                    Commit(s, amount);
                    Record(seat, s.AllIn ? PokerMove.AllIn : move, amount, s.Street);
                    break;
                }
                case PokerMove.AllIn:
                    return RaiseTo(seat, MaxRaiseTo(seat), allIn: true);
                case PokerMove.Bet:
                case PokerMove.Raise:
                    return RaiseTo(seat, to, allIn: to >= MaxRaiseTo(seat));
                default:
                    return "Not a move.";
            }
            s.Acted = true;
            s.RaisesSeen = FullRaises;
            Advance();
            return null;
        }

        private string RaiseTo(int seat, decimal to, bool allIn)
        {
            PokerSeat s = Seats[seat];
            to = Math.Floor(to);
            decimal max = MaxRaiseTo(seat);
            if (to > max) to = max;
            if (to <= CurrentBet)
            {
                // An all-in for no more than the bet is a call for less (or the whole call).
                if (!allIn) return $"Raise to at least {CasinoMoney.Whole(MinRaiseTo)}.";
                decimal amount = s.Stack;
                Commit(s, amount);
                Record(seat, PokerMove.AllIn, amount, s.Street);
            }
            else
            {
                if (!CanRaise(seat)) return "The betting isn't open to you again: call or fold.";
                if (to < MinRaiseTo && !allIn) return $"Raise to at least {CasinoMoney.Whole(MinRaiseTo)}.";
                decimal raise = to - CurrentBet;
                bool full = raise >= MinRaise;
                PokerMove kind = allIn ? PokerMove.AllIn : CurrentBet == 0m ? PokerMove.Bet : PokerMove.Raise;
                decimal amount = to - s.Street;
                Commit(s, amount);
                CurrentBet = to;
                if (full)
                {
                    MinRaise = raise;
                    FullRaises++;
                }
                Record(seat, kind, amount, to);
            }
            s.Acted = true;
            s.RaisesSeen = FullRaises;
            Advance();
            return null;
        }

        private void Record(int seat, PokerMove move, decimal added, decimal to)
        {
            var a = new PokerAction(seat, move, added, to, Street);
            Log.Add(a);
            Acted?.Invoke(a);
        }

        private void Advance()
        {
            int live = Seats.Count(s => s != null && s.Live);
            if (live <= 1)
            {
                Finish();
                return;
            }
            int next = Next(ToAct, x => x.Live && !x.AllIn && (!x.Acted || x.Street < CurrentBet));
            if (next >= 0)
            {
                ToAct = next;
                return;
            }
            // Street over. If at most one player can still bet, deal out the board and show down.
            int canBet = Seats.Count(s => s != null && CanStillAct(s));
            if (Street == PokerStreet.River || canBet <= 1)
            {
                while (Board.Count < 5) Board.Add(_deck.Draw());
                Finish();
                return;
            }
            Street++;
            int deal = Street == PokerStreet.Flop ? 3 : 1;
            for (int i = 0; i < deal; i++) Board.Add(_deck.Draw());
            foreach (PokerSeat s in Seats)
            {
                if (s == null) continue;
                s.Street = 0m;
                s.Acted = false;
                s.RaisesSeen = 0;
            }
            CurrentBet = 0m;
            MinRaise = BigBlind;
            FullRaises = 0;
            ToAct = Next(Button, CanStillAct);
        }

        /// <summary>Returns any uncalled bet, rakes, builds the main and side pots and pays them.</summary>
        private void Finish()
        {
            ToAct = -1;
            // Uncalled: the biggest contributor gets back whatever nobody else matched.
            PokerSeat[] byTotal = Seats.Where(s => s != null && s.InHand).OrderByDescending(s => s.Total).ToArray();
            decimal second = byTotal.Length > 1 ? byTotal[1].Total : 0m;
            if (byTotal[0].Total > second)
            {
                int idx = Array.IndexOf(Seats, byTotal[0]);
                Uncalled = byTotal[0].Total - second;
                UncalledSeat = idx;
                Seats[idx].Total -= Uncalled;
                Seats[idx].Stack += Uncalled;
            }

            var levels = Seats.Where(s => s != null && s.Live).Select(s => s.Total).Distinct().OrderBy(v => v).ToList();
            decimal previous = 0m;
            foreach (decimal level in levels)
            {
                var pot = new PokerPot();
                for (int i = 0; i < Seats.Length; i++)
                {
                    PokerSeat s = Seats[i];
                    if (s == null || !s.InHand) continue;
                    pot.Amount += Math.Max(0m, Math.Min(s.Total, level) - previous);
                    if (s.Live && s.Total >= level) pot.Eligible.Add(i);
                }
                previous = level;
                if (pot.Amount > 0m) Pots.Add(pot);
            }
            // Folded money above the last live level (rare: only if live players are all-in below it) goes to the last pot.
            decimal leftover = Seats.Where(s => s != null && s.InHand).Sum(s => Math.Max(0m, s.Total - previous));
            if (leftover > 0m && Pots.Count > 0) Pots[Pots.Count - 1].Amount += leftover;

            // House rake: a share of the pot once a flop is seen, capped, whole dollars, from the main pot.
            if (Board.Count >= 3 && RakeRate > 0m && Pots.Count > 0)
            {
                Rake = Math.Min(RakeCap, Math.Floor(Pots.Sum(p => p.Amount) * RakeRate));
                Pots[0].Amount -= Rake;
            }

            ShowdownReached = Seats.Count(s => s != null && s.Live) > 1;
            foreach (PokerPot pot in Pots)
            {
                List<int> contenders = pot.Eligible;
                if (ShowdownReached)
                {
                    int best = int.MinValue;
                    foreach (int i in contenders)
                    {
                        HandValue v = HandEvaluator.Evaluate(Seats[i].Hole.Concat(Board).ToList());
                        if (v.Score > best)
                        {
                            best = v.Score;
                            pot.Winners.Clear();
                            pot.WinningHand = v;
                        }
                        if (v.Score == best) pot.Winners.Add(i);
                    }
                }
                else pot.Winners.AddRange(contenders);
                // Split evenly in whole chips; odd chips one at a time from the first winner left of the button.
                decimal share = Math.Floor(pot.Amount / pot.Winners.Count);
                decimal odd = pot.Amount - share * pot.Winners.Count;
                foreach (int w in pot.Winners) pot.Paid[w] = share;
                for (int s = Next(Button, _ => true), n = 0; odd > 0m && n < Seats.Length; s = Next(s, _ => true), n++)
                    if (pot.Winners.Contains(s))
                    {
                        pot.Paid[s] += 1m;
                        odd -= 1m;
                    }
                foreach (KeyValuePair<int, decimal> p in pot.Paid) Seats[p.Key].Stack += p.Value;
            }
            Street = PokerStreet.Showdown;
        }

        /// <summary>What each seat won in total this hand.</summary>
        public decimal WonBy(int seat)
        {
            decimal w = 0m;
            foreach (PokerPot p in Pots)
                if (p.Paid.TryGetValue(seat, out decimal a)) w += a;
            return w;
        }

        /// <summary>Tests only: these cards come off the deck next (hole cards are dealt one round at a time).</summary>
        internal void StackDeck(params Card[] cards) => _deck.Stack(cards);
    }
}
