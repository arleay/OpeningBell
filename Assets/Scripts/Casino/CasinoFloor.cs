using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpeningBell.Casino
{
    /// <summary>
    /// Everything the Meridian keeps between sessions (CASINO_SPEC §88–89, §98): chips and records, membership,
    /// rewards, the hotel stay, the progressive jackpot, drinks, the gambling budget, what's been spent in the resort,
    /// a car with the valet, first-visit hints, and any blackjack hand still in play when the game was saved (keyed by
    /// table). Tables register their live round when the world is built; a round saved mid-hand is handed back to its
    /// table then, whichever of the two (load or build) happens first. Settled rounds are already in the chips.
    /// </summary>
    public sealed class CasinoFloor
    {
        private const int SpendLimit = 200;

        private readonly Dictionary<string, BlackjackRound> _tables = new Dictionary<string, BlackjackRound>();
        private readonly Dictionary<string, BlackjackRoundSaveData> _pending = new Dictionary<string, BlackjackRoundSaveData>();
        private readonly List<CasinoSpend> _spending = new List<CasinoSpend>();
        private readonly HashSet<string> _seen = new HashSet<string>();

        public CasinoAccount Account { get; } = new CasinoAccount();
        public Rewards Rewards { get; } = new Rewards();
        public Hotel Hotel { get; } = new Hotel();
        public SlotJackpot Jackpot { get; } = new SlotJackpot();
        public Intoxication Drinks { get; } = new Intoxication();
        public GamblingBudget Budget { get; } = new GamblingBudget();
        public IReadOnlyList<CasinoSpend> Spending => _spending;

        /// <summary>Signed up at the cage (a free players' card, asked for on the first visit).</summary>
        public bool Member { get; set; }

        /// <summary>The car the valet has, if any (a fleet vehicle id).</summary>
        public string ValetCar { get; set; }

        /// <summary>
        /// Set while seated at a poker table: the stack at the moment of saving. A reload cashes it out (a hand in
        /// play is abandoned like a player walking away: what was in the pot stays there).
        /// </summary>
        public Func<(string Table, decimal Stack, decimal BoughtIn)?> PokerSeat { get; set; }
        /// <summary>A poker stack restored from a save and waiting to be cashed out.</summary>
        public (string Table, decimal Stack, decimal BoughtIn)? PokerRestored { get; private set; }

        public CasinoFloor()
        {
            Account.Settled += (game, wagered) => Rewards.EarnFromPlay(game, wagered);
            Account.Guard = amount =>
            {
                if (!Budget.Hard || !Budget.Set) return null;
                decimal lost = Budget.Lost(Net);
                return lost + amount > Budget.Limit ? $"That would pass tonight's limit of {CasinoMoney.Whole(Budget.Limit)} (you asked for a hard limit)." : null;
            };
        }

        /// <summary>Lifetime result, house games plus poker.</summary>
        public decimal Net => Account.NetResult + Account.PokerNet;

        public bool FirstTime(string hint) => _seen.Add(hint);

        /// <summary>VIP salon: Gold members, anyone holding $25,000 in chips, and Silver on VIP Night (§43).</summary>
        public bool VipAllowed(DateTime now) =>
            Rewards.VipAccess || Account.Chips >= 25_000m || (CasinoEvents.VipNight(now) && Rewards.Tier >= RewardTier.Silver);

        /// <summary>
        /// Pays for something in the resort from the bank (§58, §91): food, drinks, rooms, valet. Points can cover
        /// part; earns points on what's paid. Null on success, else why not (nothing charged or recorded).
        /// </summary>
        public string Spend(SpendCategory category, decimal amount, string memo, DateTime now, Func<decimal, string, string> charge, bool usePoints = false)
        {
            if (amount < 0m) return "Nothing to pay.";
            // Points cover what they can; they're only taken once the bank has paid the rest.
            decimal comped = usePoints ? Math.Min(amount, Math.Floor(Rewards.PointsValue * 100m) / 100m) : 0m;
            decimal due = amount - comped;
            if (due > 0m)
            {
                string error = charge(due, memo + " · The Meridian");
                if (error != null) return error;
            }
            if (comped > 0m) comped = Rewards.Redeem(comped);
            Rewards.EarnFromSpend(due);
            _spending.Add(new CasinoSpend { Time = now, Category = category, Amount = due, Comped = comped, Memo = memo });
            if (_spending.Count > SpendLimit) _spending.RemoveAt(0);
            return null;
        }

        public decimal SpentOn(SpendCategory category)
        {
            decimal t = 0m;
            foreach (CasinoSpend s in _spending)
                if (s.Category == category) t += s.Amount;
            return t;
        }

        /// <summary>Gives back a poker stack restored from a save (after the account was restored).</summary>
        public decimal SettleRestoredPoker(DateTime now)
        {
            if (PokerRestored is not { } p) return 0m;
            PokerRestored = null;
            Account.FromTable(p.Stack, p.BoughtIn, now, p.Table);
            return p.Stack;
        }

        public void Register(string tableId, BlackjackRound round)
        {
            _tables[tableId] = round;
            if (_pending.TryGetValue(tableId, out BlackjackRoundSaveData data))
            {
                _pending.Remove(tableId);
                round.RestoreState(data);
            }
        }

        /// <summary>The table with a hand in progress, if any (the player can only be at one).</summary>
        public string TableInPlay
        {
            get
            {
                foreach (KeyValuePair<string, BlackjackRound> t in _tables)
                    if (t.Value.Phase == BlackjackPhase.PlayerTurn) return t.Key;
                foreach (string id in _pending.Keys) return id;
                return null;
            }
        }

        // ---------------------------------------------------------------- save

        private static string S(decimal v) => v.ToString(CultureInfo.InvariantCulture);
        private static decimal D(string v) => string.IsNullOrEmpty(v) ? 0m : decimal.Parse(v, NumberStyles.Number, CultureInfo.InvariantCulture);

        public CasinoSaveData CaptureState()
        {
            var data = new CasinoSaveData
            {
                Account = Account.CaptureState(), Member = Member,
                Points = Rewards.Points, Lifetime = Rewards.Lifetime, PointsPartial = Rewards.PartialText,
                HasStay = Hotel.Stay != null,
                Jackpot = S(Jackpot.Pool), JackpotsWon = Jackpot.TimesWon,
                DrinkUnits = Drinks.Units, DrinksUpdated = Drinks.Updated.Ticks,
                BudgetLimit = S(Budget.Limit), BudgetHard = Budget.Hard, BudgetNight = Budget.Night.Ticks, BudgetNet = S(Budget.NetAtStart),
                ValetCar = ValetCar,
                Seen = new List<string>(_seen),
            };
            if (Hotel.Stay is HotelStay stay)
            {
                data.StayClass = (int)stay.Class;
                data.StayRoom = stay.Room;
                data.StayIn = stay.CheckIn.Ticks;
                data.StayOut = stay.CheckOut.Ticks;
                data.StayPaid = S(stay.Paid);
                data.StayNights = stay.Nights;
            }
            foreach (CasinoSpend s in _spending)
                data.Spending.Add(new CasinoSpendSaveData { Time = s.Time.Ticks, Category = (int)s.Category, Amount = S(s.Amount), Comped = S(s.Comped), Memo = s.Memo });
            foreach (KeyValuePair<string, BlackjackRound> t in _tables)
                if (t.Value.Phase == BlackjackPhase.PlayerTurn)
                    data.Rounds.Add(new CasinoTableSaveData { Table = t.Key, Round = t.Value.CaptureState() });
            // A saved hand whose table wasn't built this session (tests without the city) stays saved.
            foreach (KeyValuePair<string, BlackjackRoundSaveData> p in _pending)
                data.Rounds.Add(new CasinoTableSaveData { Table = p.Key, Round = p.Value });
            (string Table, decimal Stack, decimal BoughtIn)? poker = PokerSeat?.Invoke() ?? PokerRestored;
            if (poker is { } seat)
            {
                data.PokerTable = seat.Table;
                data.PokerStack = S(seat.Stack);
                data.PokerBoughtIn = S(seat.BoughtIn);
            }
            return data;
        }

        public void RestoreState(CasinoSaveData data)
        {
            if (data == null) return;
            if (data.Account != null) Account.RestoreState(data.Account);
            Member = data.Member;
            Rewards.Restore(data.Points, data.Lifetime, data.PointsPartial);
            Hotel.Restore(data.HasStay ? new HotelStay
            {
                Class = (RoomClass)data.StayClass, Room = data.StayRoom, CheckIn = new DateTime(Math.Max(0, data.StayIn)),
                CheckOut = new DateTime(Math.Max(0, data.StayOut)), Paid = D(data.StayPaid), Nights = data.StayNights,
            } : null);
            Jackpot.Restore(string.IsNullOrEmpty(data.Jackpot) ? SlotJackpot.Seed : D(data.Jackpot), data.JackpotsWon);
            Drinks.Restore(data.DrinkUnits, data.DrinksUpdated);
            Budget.Restore(D(data.BudgetLimit), data.BudgetHard, new DateTime(Math.Max(0, data.BudgetNight)), D(data.BudgetNet));
            ValetCar = string.IsNullOrEmpty(data.ValetCar) ? null : data.ValetCar;
            _seen.Clear();
            if (data.Seen != null) _seen.UnionWith(data.Seen);
            _spending.Clear();
            if (data.Spending != null)
                foreach (CasinoSpendSaveData s in data.Spending)
                    _spending.Add(new CasinoSpend { Time = new DateTime(s.Time), Category = (SpendCategory)s.Category, Amount = D(s.Amount), Comped = D(s.Comped), Memo = s.Memo });
            PokerRestored = !string.IsNullOrEmpty(data.PokerTable) ? (data.PokerTable, D(data.PokerStack), D(data.PokerBoughtIn)) : null;
            _pending.Clear();
            foreach (CasinoTableSaveData t in data.Rounds)
            {
                if (string.IsNullOrEmpty(t.Table) || t.Round == null) continue;
                if (_tables.TryGetValue(t.Table, out BlackjackRound live)) live.RestoreState(t.Round);
                else _pending[t.Table] = t.Round;
            }
        }
    }

    [Serializable]
    public sealed class CasinoSaveData
    {
        public CasinoAccountSaveData Account = new CasinoAccountSaveData();
        public bool Member;
        public List<CasinoTableSaveData> Rounds = new List<CasinoTableSaveData>();
        public int Points, Lifetime;
        public string PointsPartial;
        public bool HasStay;
        public int StayClass, StayNights;
        public string StayRoom, StayPaid;
        public long StayIn, StayOut;
        public string Jackpot;
        public int JackpotsWon;
        public double DrinkUnits;
        public long DrinksUpdated;
        public string BudgetLimit, BudgetNet;
        public bool BudgetHard;
        public long BudgetNight;
        public string ValetCar;
        public List<string> Seen = new List<string>();
        public List<CasinoSpendSaveData> Spending = new List<CasinoSpendSaveData>();
        public string PokerTable, PokerStack, PokerBoughtIn;
    }

    [Serializable]
    public sealed class CasinoTableSaveData
    {
        public string Table;
        public BlackjackRoundSaveData Round;
    }

    [Serializable]
    public sealed class CasinoSpendSaveData
    {
        public long Time;
        public int Category;
        public string Amount, Comped, Memo;
    }
}
