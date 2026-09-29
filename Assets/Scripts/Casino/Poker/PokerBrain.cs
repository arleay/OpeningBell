using System;
using System.Collections.Generic;
using System.Linq;
using OpeningBell.Core;

namespace OpeningBell.Casino.Poker
{
    /// <summary>
    /// How an NPC plays (CASINO_SPEC §39): how many hands they enter, how often they bet and raise rather than call,
    /// how often they bluff, and how much they vary from one decision to the next. Nobody is perfectly predictable.
    /// </summary>
    public sealed class PokerStyle
    {
        public string Name, Blurb;
        /// <summary>0 plays almost anything, 1 only strong hands.</summary>
        public double Tightness;
        /// <summary>0 checks and calls, 1 bets and raises.</summary>
        public double Aggression;
        public double Bluff;
        /// <summary>Random spread on their read of a hand.</summary>
        public double Noise;

        public static readonly PokerStyle TightPassive = new PokerStyle { Name = "Tight-passive", Blurb = "waits for big hands, rarely raises", Tightness = 0.8, Aggression = 0.2, Bluff = 0.02, Noise = 0.05 };
        public static readonly PokerStyle TightAggressive = new PokerStyle { Name = "Tight-aggressive", Blurb = "few hands, played hard", Tightness = 0.75, Aggression = 0.75, Bluff = 0.1, Noise = 0.05 };
        public static readonly PokerStyle LoosePassive = new PokerStyle { Name = "Loose-passive", Blurb = "calls too much", Tightness = 0.2, Aggression = 0.15, Bluff = 0.03, Noise = 0.12 };
        public static readonly PokerStyle LooseAggressive = new PokerStyle { Name = "Loose-aggressive", Blurb = "lots of hands, lots of pressure", Tightness = 0.25, Aggression = 0.8, Bluff = 0.22, Noise = 0.1 };
        public static readonly PokerStyle Recreational = new PokerStyle { Name = "Recreational", Blurb = "here for fun", Tightness = 0.35, Aggression = 0.35, Bluff = 0.06, Noise = 0.2 };
        public static readonly PokerStyle Gambler = new PokerStyle { Name = "Gambler", Blurb = "loves a big pot", Tightness = 0.15, Aggression = 0.65, Bluff = 0.25, Noise = 0.22 };
        public static readonly PokerStyle Regular = new PokerStyle { Name = "Disciplined regular", Blurb = "solid, hard to read", Tightness = 0.65, Aggression = 0.6, Bluff = 0.12, Noise = 0.04 };

        public static readonly PokerStyle[] All = { TightPassive, TightAggressive, LoosePassive, LooseAggressive, Recreational, Gambler, Regular };
    }

    /// <summary>
    /// An NPC's decisions (CASINO_SPEC §38). It sees only what a real player at the table sees: its own two cards,
    /// the board, the pot and the bets. Hand strength comes from the Chen formula before the flop and from a quick
    /// Monte Carlo against random hands after it (never the real hole cards of anyone else).
    /// </summary>
    public sealed class PokerBrain
    {
        private readonly SeededRandom _rng;
        public PokerStyle Style { get; }

        public PokerBrain(PokerStyle style, SeededRandom rng)
        {
            Style = style;
            _rng = rng;
        }

        private double Roll() => _rng.NextDouble();

        /// <summary>Chance to win at showdown against <paramref name="opponents"/> random hands, by sampling.</summary>
        public double Equity(IReadOnlyList<Card> hole, IReadOnlyList<Card> board, int opponents, int samples = 160)
        {
            var known = new HashSet<int>(hole.Concat(board).Select(c => c.Code));
            var deck = new List<Card>();
            for (int code = 0; code < 52; code++)
                if (!known.Contains(code)) deck.Add(Card.FromCode(code));
            double score = 0;
            var mine = new List<Card>(7);
            var theirs = new List<Card>(7);
            var full = new List<Card>(5);
            for (int n = 0; n < samples; n++)
            {
                // Partial shuffle: just enough cards for the board and the opponents.
                int need = (5 - board.Count) + opponents * 2;
                for (int i = 0; i < need; i++)
                {
                    int j = i + _rng.NextInt(deck.Count - i);
                    (deck[i], deck[j]) = (deck[j], deck[i]);
                }
                full.Clear();
                full.AddRange(board);
                int k = 0;
                while (full.Count < 5) full.Add(deck[k++]);
                mine.Clear();
                mine.AddRange(hole);
                mine.AddRange(full);
                int me = HandEvaluator.Evaluate(mine).Score;
                bool lost = false;
                int ties = 0;
                for (int o = 0; o < opponents && !lost; o++)
                {
                    theirs.Clear();
                    theirs.Add(deck[k++]);
                    theirs.Add(deck[k++]);
                    theirs.AddRange(full);
                    int them = HandEvaluator.Evaluate(theirs).Score;
                    if (them > me) lost = true;
                    else if (them == me) ties++;
                }
                if (!lost) score += 1.0 / (1 + ties);
            }
            return score / samples;
        }

        /// <summary>The move for the seat to act. Always a legal one.</summary>
        public (PokerMove Move, decimal To) Decide(HoldemTable t, int seat)
        {
            PokerSeat me = t.Seats[seat];
            decimal toCall = t.ToCall(seat);
            decimal pot = t.Pot;
            int opponents = Math.Max(1, t.Seats.Count(s => s != null && s.Live) - 1);
            double noise = (Roll() - 0.5) * 2 * Style.Noise;

            double strength;
            if (t.Board.Count == 0)
            {
                // Pre-flop: the Chen score, less for each opponent still to beat.
                strength = HandEvaluator.StartingStrength(me.Hole[0], me.Hole[1]) + noise;
                double enter = 0.28 + 0.3 * Style.Tightness + (toCall > t.BigBlind ? 0.12 : 0);
                if (strength < enter && toCall > 0m)
                {
                    if (Roll() < Style.Bluff * 0.4 && t.CanRaise(seat)) return Raise(t, seat, t.BigBlind * 3m + toCall);
                    return toCall <= t.BigBlind * 0.5m && Roll() < 0.6 ? (PokerMove.Call, 0m) : (PokerMove.Fold, 0m);
                }
                if (toCall == 0m && strength < enter) return (PokerMove.Check, 0m);
                if (strength > 0.72 - 0.1 * Style.Aggression && Roll() < 0.35 + 0.6 * Style.Aggression && t.CanRaise(seat))
                    return Raise(t, seat, Math.Max(t.CurrentBet * 3m, t.BigBlind * 3m));
                return toCall == 0m ? (PokerMove.Check, 0m) : (PokerMove.Call, 0m);
            }

            strength = Equity(me.Hole, t.Board, opponents) + noise;
            double odds = toCall > 0m ? (double)(toCall / (pot + toCall)) : 0;
            if (toCall == 0m)
            {
                if (t.CanRaise(seat) && ((strength > 0.6 && Roll() < 0.3 + 0.6 * Style.Aggression) || Roll() < Style.Bluff * 0.5))
                    return Raise(t, seat, Math.Max(t.BigBlind, Math.Round(pot * (decimal)(0.5 + 0.5 * Roll()))));
                return (PokerMove.Check, 0m);
            }
            double margin = 0.05 + 0.1 * Style.Tightness;
            if (strength > 0.8 && t.CanRaise(seat) && Roll() < 0.25 + 0.6 * Style.Aggression)
                return Roll() < 0.15 ? (PokerMove.AllIn, 0m) : Raise(t, seat, t.CurrentBet + Math.Round(pot * (decimal)(0.6 + 0.6 * Roll())));
            if (strength > odds + margin) return (PokerMove.Call, 0m);
            if (Roll() < Style.Bluff * 0.25 && t.CanRaise(seat)) return Raise(t, seat, t.CurrentBet * 2.5m);
            return (PokerMove.Fold, 0m);
        }

        private static (PokerMove, decimal) Raise(HoldemTable t, int seat, decimal to)
        {
            decimal max = t.MaxRaiseTo(seat);
            to = Math.Floor(Math.Max(to, t.MinRaiseTo));
            if (to >= max) return (PokerMove.AllIn, 0m);
            return (t.CurrentBet == 0m ? PokerMove.Bet : PokerMove.Raise, to);
        }
    }
}
