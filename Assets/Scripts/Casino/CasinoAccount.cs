using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpeningBell.Casino
{
    public enum CasinoEntryKind { BuyChips, CashOut, Wager, Return, ToTable, FromTable }

    /// <summary>Games whose results are recorded (CASINO_SPEC §59).</summary>
    public enum CasinoGame { Blackjack, Roulette, Slots, Baccarat, Poker }

    [Serializable]
    public struct CasinoEntry
    {
        public DateTime Time;
        public CasinoEntryKind Kind;
        public decimal Amount;
        public decimal ChipsAfter;
        public string Memo;
    }

    /// <summary>Chip values, largest last. Visual stacks and the cashier's buttons come from here.</summary>
    public static class ChipDenominations
    {
        public static readonly decimal[] Values = { 1m, 5m, 25m, 100m, 500m, 1_000m, 5_000m };

        /// <summary>Fewest chips making <paramref name="amount"/> (whole dollars), largest first.</summary>
        public static List<(decimal value, int count)> Breakdown(decimal amount)
        {
            var result = new List<(decimal, int)>();
            decimal left = Math.Floor(Math.Max(0m, amount));
            for (int i = Values.Length - 1; i >= 0 && left > 0m; i--)
            {
                int n = (int)Math.Floor(left / Values[i]);
                if (n <= 0) continue;
                result.Add((Values[i], n));
                left -= n * Values[i];
            }
            return result;
        }
    }

    /// <summary>
    /// The player's chips at the casino (CASINO_SPEC §10–12, §58–59, §83, §88). Chips are bought from the bank and
    /// cashed back into it; they can't be spent anywhere else. Every change is one call that updates the balance,
    /// the statistics and the history together, so a save taken between two frames is always consistent. Amounts
    /// are whole dollars, positive, and capped, so nothing negative, fractional or absurd gets in.
    /// </summary>
    public sealed class CasinoAccount
    {
        public const decimal MaxTransaction = 10_000_000m;
        private const int HistoryLimit = 200;

        private readonly List<CasinoEntry> _history = new List<CasinoEntry>();

        public decimal Chips { get; private set; }
        public IReadOnlyList<CasinoEntry> History => _history;

        // Statistics (§59).
        public decimal TotalBought { get; private set; }
        public decimal TotalCashedOut { get; private set; }
        public decimal TotalWagered { get; private set; }
        public decimal TotalReturned { get; private set; }
        public decimal BiggestWin { get; private set; }
        public decimal BiggestLoss { get; private set; }
        /// <summary>Sum of every winning round's profit, and of every losing round's loss.</summary>
        public decimal TotalWon { get; private set; }
        public decimal TotalLost { get; private set; }
        public int BlackjackHands => Count(CasinoGame.Blackjack);
        private readonly int[] _rounds = new int[5];
        /// <summary>Rounds played per game: hands, spins, sessions.</summary>
        public int Count(CasinoGame game) => _rounds[(int)game];
        /// <summary>Poker is played against people, not the house: its result is tracked on its own.</summary>
        public decimal PokerNet { get; private set; }

        /// <summary>Raised inside every settlement (rewards points hang off it, so they can't be earned twice).</summary>
        public event Action<CasinoGame, decimal> Settled;

        /// <summary>Lifetime result at the tables: returned − wagered.</summary>
        public decimal NetResult => TotalReturned - TotalWagered;

        /// <summary>
        /// Buys and bets are whole dollars; cashing out takes cents too (a $5 blackjack pays $7.50, like the half-dollar
        /// pieces real tables use).
        /// </summary>
        public static string Validate(decimal amount, bool cents = false)
        {
            if (amount <= 0m) return "Enter an amount.";
            if (amount != (cents ? Math.Round(amount, 2) : Math.Floor(amount))) return cents ? "Cents at most." : "Chips come in whole dollars.";
            if (amount > MaxTransaction) return "That's more than the cage handles in one go.";
            return null;
        }

        /// <summary>Buys chips; <paramref name="charge"/> takes the money from the bank (error, or null).</summary>
        public string BuyChips(decimal amount, DateTime now, Func<decimal, string, string> charge)
        {
            string error = Validate(amount) ?? charge(amount, "Chips · The Meridian");
            if (error != null) return error;
            Chips += amount;
            TotalBought += amount;
            Log(now, CasinoEntryKind.BuyChips, amount, "Bought chips");
            return null;
        }

        /// <summary>Cashes chips back to the bank via <paramref name="pay"/>.</summary>
        public string CashOut(decimal amount, DateTime now, Action<decimal, string> pay)
        {
            string error = Validate(amount, cents: true);
            if (error != null) return error;
            if (amount > Chips) return "You don't have that many chips.";
            Chips -= amount;
            TotalCashedOut += amount;
            Log(now, CasinoEntryKind.CashOut, amount, "Cashed out");
            pay(amount, "Cash out · The Meridian");
            return null;
        }

        /// <summary>A last check on every wager (the hard gambling budget): a reason to refuse, or null.</summary>
        public Func<decimal, string> Guard { get; set; }
        /// <summary>Why the last <see cref="TryWager"/> failed, for the table to show.</summary>
        public string Refusal { get; private set; }

        /// <summary>Takes a wager off the chips. False (and nothing taken) if invalid, short or refused.</summary>
        public bool TryWager(decimal amount, DateTime now, string memo)
        {
            Refusal = Validate(amount) ?? (amount > Chips ? "Not enough chips. Visit the cashier." : Guard?.Invoke(amount));
            if (Refusal != null) return false;
            Chips -= amount;
            TotalWagered += amount;
            Log(now, CasinoEntryKind.Wager, amount, memo);
            return true;
        }

        /// <summary>
        /// Settles a finished round: <paramref name="returned"/> (stake back plus winnings, 0 on a loss) goes back on the
        /// chips; <paramref name="wagered"/> is what the round took in total, for the records. One call, one round.
        /// </summary>
        public void Settle(CasinoGame game, decimal wagered, decimal returned, DateTime now, string memo)
        {
            if (returned > 0m)
            {
                Chips += returned;
                TotalReturned += returned;
                Log(now, CasinoEntryKind.Return, returned, memo);
            }
            decimal net = returned - wagered;
            if (net > BiggestWin) BiggestWin = net;
            if (-net > BiggestLoss) BiggestLoss = -net;
            if (net > 0m) TotalWon += net;
            else TotalLost -= net;
            _rounds[(int)game]++;
            Settled?.Invoke(game, wagered);
        }

        /// <summary>Sits down at a poker table with <paramref name="amount"/> of chips (they're in play there now).</summary>
        public bool ToTable(decimal amount, DateTime now, string table)
        {
            if (Validate(amount) != null || amount > Chips) return false;
            Chips -= amount;
            Log(now, CasinoEntryKind.ToTable, amount, table);
            return true;
        }

        /// <summary>Leaves a poker table: its stack comes back as chips, and the session's result is recorded.</summary>
        public void FromTable(decimal stack, decimal boughtIn, DateTime now, string table)
        {
            stack = Math.Max(0m, Math.Floor(stack));
            Chips += stack;
            Log(now, CasinoEntryKind.FromTable, stack, table);
            decimal net = stack - boughtIn;
            PokerNet += net;
            if (net > 0m) TotalWon += net;
            else TotalLost -= net;
            if (net > BiggestWin) BiggestWin = net;
            if (-net > BiggestLoss) BiggestLoss = -net;
            _rounds[(int)CasinoGame.Poker]++;
            Settled?.Invoke(CasinoGame.Poker, 0m);
        }

        private void Log(DateTime now, CasinoEntryKind kind, decimal amount, string memo)
        {
            _history.Add(new CasinoEntry { Time = now, Kind = kind, Amount = amount, ChipsAfter = Chips, Memo = memo });
            if (_history.Count > HistoryLimit) _history.RemoveAt(0);
        }

        // ---------------------------------------------------------------- save

        private static string S(decimal v) => v.ToString(CultureInfo.InvariantCulture);
        private static decimal D(string v) => string.IsNullOrEmpty(v) ? 0m : decimal.Parse(v, NumberStyles.Number, CultureInfo.InvariantCulture);

        public CasinoAccountSaveData CaptureState()
        {
            var data = new CasinoAccountSaveData
            {
                Chips = S(Chips), TotalBought = S(TotalBought), TotalCashedOut = S(TotalCashedOut), TotalWagered = S(TotalWagered),
                TotalReturned = S(TotalReturned), BiggestWin = S(BiggestWin), BiggestLoss = S(BiggestLoss), BlackjackHands = BlackjackHands,
                TotalWon = S(TotalWon), TotalLost = S(TotalLost), PokerNet = S(PokerNet), Rounds = new List<int>(_rounds),
            };
            foreach (CasinoEntry e in _history)
                data.History.Add(new CasinoEntrySaveData { Time = e.Time.Ticks, Kind = (int)e.Kind, Amount = S(e.Amount), ChipsAfter = S(e.ChipsAfter), Memo = e.Memo });
            return data;
        }

        public void RestoreState(CasinoAccountSaveData data)
        {
            Chips = Math.Max(0m, D(data.Chips));
            TotalBought = D(data.TotalBought);
            TotalCashedOut = D(data.TotalCashedOut);
            TotalWagered = D(data.TotalWagered);
            TotalReturned = D(data.TotalReturned);
            BiggestWin = D(data.BiggestWin);
            BiggestLoss = D(data.BiggestLoss);
            TotalWon = D(data.TotalWon);
            TotalLost = D(data.TotalLost);
            PokerNet = D(data.PokerNet);
            Array.Clear(_rounds, 0, _rounds.Length);
            if (data.Rounds != null && data.Rounds.Count > 0)
                for (int i = 0; i < Math.Min(_rounds.Length, data.Rounds.Count); i++) _rounds[i] = Math.Max(0, data.Rounds[i]);
            else _rounds[(int)CasinoGame.Blackjack] = data.BlackjackHands; // saves from before the other games
            _history.Clear();
            foreach (CasinoEntrySaveData e in data.History)
                _history.Add(new CasinoEntry { Time = new DateTime(e.Time), Kind = (CasinoEntryKind)e.Kind, Amount = D(e.Amount), ChipsAfter = D(e.ChipsAfter), Memo = e.Memo });
        }
    }

    [Serializable]
    public sealed class CasinoAccountSaveData
    {
        public string Chips, TotalBought, TotalCashedOut, TotalWagered, TotalReturned, BiggestWin, BiggestLoss, TotalWon, TotalLost, PokerNet;
        public int BlackjackHands;
        public List<int> Rounds = new List<int>();
        public List<CasinoEntrySaveData> History = new List<CasinoEntrySaveData>();
    }

    [Serializable]
    public sealed class CasinoEntrySaveData
    {
        public long Time;
        public int Kind;
        public string Amount, ChipsAfter, Memo;
    }
}
