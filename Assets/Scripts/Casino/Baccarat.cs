using System;
using System.Collections.Generic;

namespace OpeningBell.Casino
{
    public enum BaccaratOutcome { Player, Banker, Tie }

    [Serializable]
    public sealed class BaccaratRules
    {
        public int Decks = 8;
        public decimal MinBet = 25m, MaxBet = 5_000m, TieMax = 500m;
        /// <summary>Commission on winning Banker bets (5%: pays 0.95 to 1).</summary>
        public decimal Commission = 0.05m;
        public int TiePays = 8;
    }

    /// <summary>
    /// Punto banco (CASINO_SPEC §28): the player only bets; the tableau decides every third card. A coup is one step:
    /// the stakes come off, the cards are drawn and the bets pay.
    /// </summary>
    public sealed class BaccaratTable
    {
        private readonly CasinoAccount _account;
        private readonly Shoe _shoe;
        private readonly Func<DateTime> _now;

        public BaccaratRules Rules { get; }
        public List<Card> Player { get; } = new List<Card>();
        public List<Card> Banker { get; } = new List<Card>();
        public BaccaratOutcome Outcome { get; private set; }
        public bool Dealt { get; private set; }
        public decimal LastWagered { get; private set; }
        public decimal LastReturned { get; private set; }
        /// <summary>Order the cards came out in: (true = player hand, index).</summary>
        public List<(bool Player, int Index)> Order { get; } = new List<(bool, int)>();

        public BaccaratTable(BaccaratRules rules, Shoe shoe, CasinoAccount account, Func<DateTime> now)
        {
            Rules = rules;
            _shoe = shoe;
            _account = account;
            _now = now;
        }

        /// <summary>Baccarat value: tens and faces count 0, aces 1; a hand is its total mod 10.</summary>
        public static int Value(Card c) => c.Rank >= 10 ? 0 : c.Rank;

        public static int Total(IReadOnlyList<Card> hand)
        {
            int t = 0;
            foreach (Card c in hand) t += Value(c);
            return t % 10;
        }

        /// <summary>Plays a coup with these stakes (0 = no bet there). Null on success, else why not.</summary>
        public string Deal(decimal onPlayer, decimal onBanker, decimal onTie)
        {
            decimal total = onPlayer + onBanker + onTie;
            if (onPlayer < 0m || onBanker < 0m || onTie < 0m) return "Bets can't be negative.";
            if (total <= 0m) return "Place a bet first.";
            foreach (decimal side in new[] { onPlayer, onBanker })
                if (side > 0m && (side < Rules.MinBet || side > Rules.MaxBet)) return $"Player and Banker take {CasinoMoney.Whole(Rules.MinBet)}–{CasinoMoney.Whole(Rules.MaxBet)}.";
            if (onTie > Rules.TieMax) return $"Tie bets go up to {CasinoMoney.Whole(Rules.TieMax)}.";
            if (onTie > 0m && onTie < 5m) return "Tie bets start at $5.";
            if (total != Math.Floor(total) || onPlayer != Math.Floor(onPlayer) || onBanker != Math.Floor(onBanker)) return "Bets are whole dollars.";
            if (!_account.TryWager(total, _now(), "Baccarat")) return _account.Refusal;

            if (_shoe.PastCut) _shoe.Shuffle();
            Player.Clear();
            Banker.Clear();
            Order.Clear();
            Draw(true);
            Draw(false);
            Draw(true);
            Draw(false);
            Tableau();

            int p = Total(Player), b = Total(Banker);
            Outcome = p > b ? BaccaratOutcome.Player : b > p ? BaccaratOutcome.Banker : BaccaratOutcome.Tie;
            decimal returned = 0m;
            switch (Outcome)
            {
                case BaccaratOutcome.Player:
                    returned += onPlayer * 2m;
                    break;
                case BaccaratOutcome.Banker:
                    returned += onBanker + Math.Round(onBanker * (1m - Rules.Commission), 2);
                    break;
                case BaccaratOutcome.Tie:
                    returned += onTie * (Rules.TiePays + 1) + onPlayer + onBanker; // Player and Banker push on a tie
                    break;
            }
            _account.Settle(CasinoGame.Baccarat, total, returned, _now(), $"Baccarat · {Outcome}");
            LastWagered = total;
            LastReturned = returned;
            Dealt = true;
            return null;
        }

        private Card Draw(bool player)
        {
            Card c = _shoe.Draw();
            List<Card> hand = player ? Player : Banker;
            hand.Add(c);
            Order.Add((player, hand.Count - 1));
            return c;
        }

        /// <summary>
        /// The drawing rules: a natural (8 or 9) on either side stands both. Otherwise the player draws on 0–5; the
        /// banker then draws by its total and the player's third card (or on 0–5 if the player stood).
        /// </summary>
        private void Tableau()
        {
            int p = Total(Player), b = Total(Banker);
            if (p >= 8 || b >= 8) return;
            Card? third = null;
            if (p <= 5) third = Draw(true);
            if (third == null)
            {
                if (b <= 5) Draw(false);
                return;
            }
            int t = Value(third.Value);
            bool draw = b switch
            {
                <= 2 => true,
                3 => t != 8,
                4 => t >= 2 && t <= 7,
                5 => t >= 4 && t <= 7,
                6 => t == 6 || t == 7,
                _ => false,
            };
            if (draw) Draw(false);
        }
    }
}
