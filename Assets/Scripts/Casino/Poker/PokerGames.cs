using System;
using System.Collections.Generic;
using System.Linq;
using OpeningBell.Core;

namespace OpeningBell.Casino.Poker
{
    /// <summary>A cash table's stakes (CASINO_SPEC §40): blinds, buy-in range, rake.</summary>
    public sealed class PokerStakes
    {
        public string Id, Name;
        public decimal SmallBlind, BigBlind, MinBuyIn, MaxBuyIn;
        /// <summary>5% of pots that see a flop, capped: the house's cut (poker is played against people).</summary>
        public decimal RakeRate = 0.05m, RakeCap = 4m;
        public int Seats = 6;

        public static readonly PokerStakes OneTwo = new PokerStakes { Id = "nlh-1-2", Name = "$1/$2 No-Limit", SmallBlind = 1m, BigBlind = 2m, MinBuyIn = 80m, MaxBuyIn = 200m };
        public static readonly PokerStakes TwoFive = new PokerStakes { Id = "nlh-2-5", Name = "$2/$5 No-Limit", SmallBlind = 2m, BigBlind = 5m, MinBuyIn = 200m, MaxBuyIn = 500m, RakeCap = 5m };
        public static readonly PokerStakes FiveTen = new PokerStakes { Id = "nlh-5-10", Name = "$5/$10 No-Limit (VIP)", SmallBlind = 5m, BigBlind = 10m, MinBuyIn = 500m, MaxBuyIn = 1_500m, RakeCap = 6m };
    }

    /// <summary>Names for the NPCs who sit down (fictional).</summary>
    public static class PokerNames
    {
        public static readonly string[] All =
        {
            "Marty", "Dolores", "Vince", "Priya", "Hank", "Celeste", "Owen", "Ruth", "Teddy", "Imani", "Glen", "Sasha", "Walt", "Bea",
            "Nico", "June", "Reggie", "Lena", "Frank", "Mae", "Desmond", "Kit", "Arlo", "Nadia",
        };
    }

    /// <summary>
    /// A cash game with the player in seat 0 and five NPCs (§35–40). NPCs who go broke or get bored leave and new
    /// ones sit down. The player's money comes from their chips when they sit and goes back when they stand up; a
    /// hand is never interrupted: standing up waits for it to finish.
    /// </summary>
    public sealed class PokerCashGame
    {
        public const int PlayerSeat = 0;
        private readonly SeededRandom _rng;
        private readonly CasinoAccount _account;
        private readonly Func<DateTime> _now;

        public PokerStakes Stakes { get; }
        public HoldemTable Table { get; }
        public bool Seated { get; private set; }
        /// <summary>All chips brought to the table this session (buy-in plus top-ups): the session result is against this.</summary>
        public decimal BoughtIn { get; private set; }
        public decimal PlayerStack => Seated ? Table.Seats[PlayerSeat].Stack : 0m;
        /// <summary>Who sat down or got up after the last hand ("Marty sits down.").</summary>
        public List<string> Arrivals { get; } = new List<string>();

        public PokerCashGame(PokerStakes stakes, CasinoAccount account, SeededRandom rng, Func<DateTime> now)
        {
            Stakes = stakes;
            _account = account;
            _rng = rng;
            _now = now;
            Table = new HoldemTable(stakes.Seats, stakes.SmallBlind, stakes.BigBlind, rng, stakes.RakeRate, stakes.RakeCap);
            for (int s = 1; s < stakes.Seats; s++) Table.Seats[s] = NewNpc();
        }

        private PokerSeat NewNpc()
        {
            var used = new HashSet<string>(Table.Seats.Where(x => x != null).Select(x => x.Name));
            string name;
            do name = PokerNames.All[_rng.NextInt(PokerNames.All.Length)];
            while (used.Contains(name));
            PokerStyle style = PokerStyle.All[_rng.NextInt(PokerStyle.All.Length)];
            decimal stack = Math.Floor(Stakes.BigBlind * (60 + _rng.NextInt(90)));
            return new PokerSeat { Name = name, Stack = stack, Brain = new PokerBrain(style, _rng) };
        }

        public string Sit(decimal buyIn, string playerName = "You")
        {
            if (Seated) return "You're already seated.";
            if (buyIn < Stakes.MinBuyIn || buyIn > Stakes.MaxBuyIn) return $"Buy in for {CasinoMoney.Whole(Stakes.MinBuyIn)}–{CasinoMoney.Whole(Stakes.MaxBuyIn)}.";
            if (!_account.ToTable(buyIn, _now(), Stakes.Name)) return "Not enough chips.";
            Table.Seats[PlayerSeat] = new PokerSeat { Name = playerName, Stack = buyIn };
            BoughtIn = buyIn;
            Seated = true;
            return null;
        }

        /// <summary>Tops up between hands, up to the maximum buy-in.</summary>
        public string TopUp(decimal amount)
        {
            if (!Seated) return "Sit down first.";
            if (!Table.HandOver && Table.Street != PokerStreet.Waiting) return "Between hands.";
            if (PlayerStack + amount > Stakes.MaxBuyIn) return $"Max at this table is {CasinoMoney.Whole(Stakes.MaxBuyIn)}.";
            if (!_account.ToTable(amount, _now(), Stakes.Name)) return "Not enough chips.";
            Table.Seats[PlayerSeat].Stack += amount;
            BoughtIn += amount;
            return null;
        }

        /// <summary>Between hands only: the seat's chips in the pot belong to the hand until it's settled.</summary>
        public bool CanLeave => Seated && (Table.HandOver || Table.Street == PokerStreet.Waiting);

        /// <summary>Stands up: the stack goes back on the chips and the session is recorded.</summary>
        public string Leave()
        {
            if (!Seated) return null;
            if (!CanLeave) return "Finish the hand first.";
            decimal stack = Table.Seats[PlayerSeat].Stack;
            _account.FromTable(stack, BoughtIn, _now(), Stakes.Name);
            Table.Seats[PlayerSeat] = null;
            Seated = false;
            BoughtIn = 0m;
            return null;
        }

        /// <summary>
        /// For saving mid-hand: the player's seat as if they'd folded this hand (what's in the pot stays in, as when a
        /// player walks away), so a reload can't take back a bet.
        /// </summary>
        public decimal StackIfAbandoned => Seated ? Table.Seats[PlayerSeat].Stack : 0m;

        /// <summary>Starts the next hand, first seating replacements for NPCs who left.</summary>
        public bool NextHand()
        {
            Arrivals.Clear();
            for (int s = 1; s < Table.Seats.Length; s++)
            {
                PokerSeat seat = Table.Seats[s];
                bool leaves = seat == null || seat.Stack < Stakes.BigBlind * 10m || _rng.NextDouble() < 0.03;
                if (!leaves) continue;
                if (seat != null) Arrivals.Add($"{seat.Name} leaves the table.");
                Table.Seats[s] = NewNpc();
                Arrivals.Add($"{Table.Seats[s].Name} sits down ({Table.Seats[s].Brain.Style.Blurb}).");
            }
            return Table.StartHand();
        }

        /// <summary>Plays NPC turns until it's the player's turn or the hand ends (tests and fast-forward).</summary>
        public void RunNpcs()
        {
            while (!Table.HandOver && Table.ToAct >= 0 && !Table.Seats[Table.ToAct].IsPlayer) StepNpc();
        }

        /// <summary>One NPC decision (the table animates these one at a time).</summary>
        public PokerAction? StepNpc()
        {
            if (Table.HandOver || Table.ToAct < 0) return null;
            int seat = Table.ToAct;
            PokerSeat s = Table.Seats[seat];
            if (s.IsPlayer) return null;
            (PokerMove move, decimal to) = s.Brain.Decide(Table, seat);
            if (Table.Act(seat, move, to) != null)
            {
                // Never stall on an illegal choice: check if possible, else call or fold.
                if (Table.Act(seat, Table.CanCheck(seat) ? PokerMove.Check : PokerMove.Call) != null) Table.Act(seat, PokerMove.Fold);
            }
            return Table.Log.Count > 0 ? Table.Log[Table.Log.Count - 1] : (PokerAction?)null;
        }
    }

    /// <summary>
    /// A six-player Sit & Go (CASINO_SPEC §66): entry plus a fee from chips, 1,500 tournament chips each (separate
    /// from cash chips), blinds up every eight hands, the top two paid 65/35 of the entries. Busting out ends your
    /// tournament; finishing in the money pays at once.
    /// </summary>
    public sealed class SitAndGo
    {
        public const decimal BuyIn = 100m, Fee = 10m, StartingChips = 1_500m;
        public const int Players = 6, HandsPerLevel = 8;
        public static readonly (decimal Sb, decimal Bb)[] Levels =
            { (10, 20), (15, 30), (25, 50), (50, 100), (75, 150), (100, 200), (150, 300), (200, 400), (300, 600), (500, 1000), (1000, 2000) };
        public static readonly decimal[] Payouts = { 0.65m, 0.35m };

        private readonly CasinoAccount _account;
        private readonly SeededRandom _rng;
        private readonly Func<DateTime> _now;
        private int _hands;

        public HoldemTable Table { get; }
        public bool Registered { get; private set; }
        public bool Finished { get; private set; }
        /// <summary>1 = won. 0 until the player's tournament ends.</summary>
        public int Place { get; private set; }
        public decimal Prize { get; private set; }
        public int Level { get; private set; }
        public decimal PrizePool => BuyIn * Players;
        public int Remaining => Table.Seats.Count(s => s != null && s.Stack > 0m);

        public SitAndGo(CasinoAccount account, SeededRandom rng, Func<DateTime> now)
        {
            _account = account;
            _rng = rng;
            _now = now;
            Table = new HoldemTable(Players, Levels[0].Sb, Levels[0].Bb, rng);
        }

        public string Register(string playerName = "You")
        {
            if (Registered) return "You're already registered.";
            if (!_account.ToTable(BuyIn + Fee, _now(), "Sit & Go")) return "Not enough chips for the entry ($110).";
            Table.Seats[0] = new PokerSeat { Name = playerName, Stack = StartingChips };
            var used = new HashSet<string>();
            for (int s = 1; s < Players; s++)
            {
                string name;
                do name = PokerNames.All[_rng.NextInt(PokerNames.All.Length)];
                while (!used.Add(name));
                Table.Seats[s] = new PokerSeat { Name = name, Stack = StartingChips, Brain = new PokerBrain(PokerStyle.All[_rng.NextInt(PokerStyle.All.Length)], _rng) };
            }
            Registered = true;
            return null;
        }

        /// <summary>After a hand: knock out the busted, finish the player's run if they're out or have won, raise blinds.</summary>
        public void AfterHand()
        {
            if (!Registered || Finished) return;
            for (int s = 1; s < Players; s++)
                if (Table.Seats[s] != null && Table.Seats[s].Stack <= 0m) Table.Seats[s] = null;
            PokerSeat me = Table.Seats[0];
            if (me.Stack <= 0m) End(Remaining + 1);
            else if (Remaining == 1) End(1);
        }

        private void End(int place)
        {
            Finished = true;
            Place = place;
            Prize = place <= Payouts.Length ? Math.Floor(PrizePool * Payouts[place - 1]) : 0m;
            _account.FromTable(Prize, BuyIn + Fee, _now(), "Sit & Go");
        }

        public bool NextHand()
        {
            if (!Registered || Finished) return false;
            Level = Math.Min(Levels.Length - 1, _hands / HandsPerLevel);
            Table.SmallBlind = Levels[Level].Sb;
            Table.BigBlind = Levels[Level].Bb;
            _hands++;
            return Table.StartHand();
        }

        /// <summary>Leaving early forfeits the entry (recorded as a loss).</summary>
        public void Forfeit()
        {
            if (Registered && !Finished) End(Math.Max(Remaining, 3));
        }

        public PokerAction? StepNpc()
        {
            if (Table.HandOver || Table.ToAct < 0) return null;
            int seat = Table.ToAct;
            PokerSeat s = Table.Seats[seat];
            if (s.IsPlayer) return null;
            (PokerMove move, decimal to) = s.Brain.Decide(Table, seat);
            if (Table.Act(seat, move, to) != null && Table.Act(seat, Table.CanCheck(seat) ? PokerMove.Check : PokerMove.Call) != null)
                Table.Act(seat, PokerMove.Fold);
            return Table.Log[Table.Log.Count - 1];
        }
    }
}
