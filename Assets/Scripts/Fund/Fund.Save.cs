using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Trading;

namespace OpeningBell.Fund
{
    // Save models (JsonUtility-friendly): money as invariant decimal strings, doubles that decisions read as raw bits,
    // times as ticks. Everything a reload needs to continue exactly: ledger, desks (orders, positions, fills), people,
    // training progress, what's owed. Nothing is recomputed from scratch that could charge or pay twice.

    [Serializable]
    public sealed class FundSaveData
    {
        public bool Exists;
        public string Name = "", ClosedReason = "", WindUpReason = "";
        public int Logo, Colour, Tenure, DelinquentDays;
        public long Founded, ClosedOn, Clock, DayDate, ReputationBits;
        public string DepositHeld, PropertyValue, BillsOverdue, NetContributions, TotalExpenses, OtherIncome;
        public bool ExpansionOpen, NetworkUp = true, ConnectivityOverdue, WindingUp;
        public long NextPersonId = 1, NextListingId = 1, NextTradeId = 1, NextJobId = 1;
        public List<CashSave> Cash = new List<CashSave>();
        public List<ExpenseSave> Expenses = new List<ExpenseSave>();
        public List<EmployeeSave> Employees = new List<EmployeeSave>();
        public List<ApplicantSave> Applicants = new List<ApplicantSave>();
        public List<Listing> Listings = new List<Listing>();
        public List<FundNotice> Notices = new List<FundNotice>();
        public List<CompanyDay> History = new List<CompanyDay>();
        public string TradingAtLastClose;
    }

    [Serializable] public sealed class CashSave { public long Id, Time, Employee; public int Kind; public string Amount, Balance, Memo; }
    [Serializable] public sealed class ExpenseSave { public long Time, Employee; public int Kind; public string Amount, Memo; }

    [Serializable]
    public sealed class PersonSave
    {
        public long Id;
        public string First, Last, Savings, Car;
        public int Age, Years, Specialty, Strategy;
        public PersonLook Look;
        public long[] Skills, Caps, Traits;
        public long PastWinRate, PastRewardRisk, TradesPerDay;
        public List<HistoryEntry> History = new List<HistoryEntry>();
    }

    [Serializable] public sealed class ContractSave { public int Structure, Version; public string Hourly, Rate; public long Signed; }

    [Serializable]
    public sealed class PolicySave
    {
        public int MaxContracts, MaxPositions, PreferredMaxTrades;
        public string MaxRisk, MaxDailyLoss;
        public List<string> Instruments = new List<string>();
        public List<int> Strategies = new List<int>();
        public bool Authorized, PreferQuality;
    }

    [Serializable]
    public sealed class TradeSave
    {
        public long Id, Contracts, Opened, Closed, ExitQuantity;
        public string Ticker, Notes;
        public int Strategy, Side;
        public string Entry, Exit, Stop, Target, Gross, Fees, Net, Risk, EntryNotional, ExitNotional;
        public long Quality, Managed;
        public bool Followed, Breakeven, Trailed, Widened;
    }

    [Serializable]
    public sealed class DaySave
    {
        public long Date;
        public string Realized, Fees, Wages, Commission;
        public int Trades, Wins, Losses, Waiting, Trading, Training;
        public bool Limit;
    }

    [Serializable]
    public sealed class JobSave
    {
        public long Id, Bought, Started, Finished, Gain, Done;
        public int Skill, Level, Minutes;
        public string Price;
    }

    [Serializable]
    public sealed class PlanSave
    {
        public long Order, Due, Contracts, Quality;
        public string Ticker, Entry, Stop, Target, Notes;
        public int Strategy, Side;
        public bool Followed;
    }

    [Serializable]
    public sealed class EmployeeSave
    {
        public PersonSave Person;
        public ContractSave Contract;
        public PolicySave Policy;
        public long HiredOn, StartsOn, LeftOn, ActivitySince, MentorOf;
        public string LeftReason, LockReason;
        public string Base, NetRealized, HighWater, CommissionAccrued, WagesAccrued, WagesOverdue, CommissionOverdue;
        public string Collected, WagesPaid, CommissionPaid, DayStartEquity;
        public int Role;
        public int Desk, Activity, Break, ConsecutiveLosses, ConsecutiveWins, ResignationNotice, TradesToday;
        public bool LockedToday, RaiseRequested, Flattened, FrequencyNoted, GreedNoted, ImpulseNoted;
        public long LastEntry;
        public long Fatigue, Stress, Satisfaction, Waiting, LastScan;
        public DayPlan Plan;
        public bool HasToday;
        public List<DaySave> Days = new List<DaySave>();
        public List<TradeSave> Trades = new List<TradeSave>();
        public List<long> OpenTrades = new List<long>();
        public List<BehaviorNote> Behavior = new List<BehaviorNote>();
        public List<MoodReason> Mood = new List<MoodReason>();
        public List<JobSave> Training = new List<JobSave>();
        public List<PlanSave> Pending = new List<PlanSave>();
        public List<PlanSave> Delayed = new List<PlanSave>();
        public TradingSaveData Desk_ = new TradingSaveData();
    }

    [Serializable]
    public sealed class ApplicantSave
    {
        public PersonSave Person;
        public long ListingId, Received, AvailableFrom, Expires, Interest;
        public ContractSave Asking, LastCounter;
        public bool HasCounter;
        public int Lowballs, Rounds, Status;
        public string LastLine;
    }

    public sealed partial class HedgeFund
    {
        private static string S(decimal v) => v.ToString(CultureInfo.InvariantCulture);
        private static decimal D(string v) => string.IsNullOrEmpty(v) ? 0m : decimal.Parse(v, NumberStyles.Number, CultureInfo.InvariantCulture);
        private static long B(double v) => BitConverter.DoubleToInt64Bits(v);
        private static double F(long v) => BitConverter.Int64BitsToDouble(v);

        private static long[] B(double[] v)
        {
            var r = new long[v.Length];
            for (int i = 0; i < v.Length; i++) r[i] = B(v[i]);
            return r;
        }

        private static double[] F(long[] v, int n)
        {
            var r = new double[n];
            for (int i = 0; i < n && v != null && i < v.Length; i++) r[i] = F(v[i]);
            return r;
        }

        public FundSaveData CaptureState()
        {
            var d = new FundSaveData
            {
                Exists = Exists, Name = Name, Logo = Logo, Colour = Colour, Founded = Founded.Ticks, ClosedOn = ClosedOn.Ticks, ClosedReason = ClosedReason,
                ReputationBits = B(Reputation), Tenure = (int)Tenure, DepositHeld = S(DepositHeld), PropertyValue = S(PropertyValue),
                ExpansionOpen = ExpansionOpen, NetworkUp = NetworkUp, ConnectivityOverdue = _connectivityOverdue, BillsOverdue = S(BillsOverdue),
                DelinquentDays = DelinquentDays, WindingUp = WindingUp, WindUpReason = _windUpReason, Clock = _clock.Ticks, DayDate = _dayDate.Ticks,
                NextPersonId = _nextPersonId, NextListingId = _nextListingId, NextTradeId = _nextTradeId, NextJobId = _nextJobId,
                NetContributions = S(Ledger.NetContributions), TotalExpenses = S(Ledger.TotalExpenses), OtherIncome = S(Ledger.OtherIncome),
                Listings = new List<Listing>(_listings), Notices = new List<FundNotice>(_notices), History = new List<CompanyDay>(_history),
                TradingAtLastClose = S(_tradingAtLastClose),
            };
            foreach (CashEntry c in Ledger.Entries)
                d.Cash.Add(new CashSave { Id = c.Id, Time = c.Time, Employee = c.Employee, Kind = (int)c.Kind, Amount = S(c.Amount), Balance = S(c.BalanceAfter), Memo = c.Memo });
            foreach (ExpenseEntry x in Ledger.Expenses)
                d.Expenses.Add(new ExpenseSave { Time = x.Time, Employee = x.Employee, Kind = (int)x.Kind, Amount = S(x.Amount), Memo = x.Memo });
            foreach (Employee e in _employees) d.Employees.Add(Capture(e));
            foreach (Applicant a in _applicants)
                d.Applicants.Add(new ApplicantSave
                {
                    Person = Capture(a.Person), ListingId = a.ListingId, Received = a.Received, AvailableFrom = a.AvailableFrom, Expires = a.Expires,
                    Interest = B(a.Interest), Asking = Capture(a.Asking), HasCounter = a.LastCounter != null, LastCounter = Capture(a.LastCounter ?? new Contract()),
                    Lowballs = a.Lowballs, Rounds = a.Rounds, Status = (int)a.Status, LastLine = a.LastLine,
                });
            return d;
        }

        /// <summary>Into a freshly constructed fund (same market, rules and belongings).</summary>
        public void RestoreState(FundSaveData d)
        {
            if (d == null) return;
            Exists = d.Exists;
            Name = d.Name ?? "";
            Logo = d.Logo;
            Colour = d.Colour;
            Founded = new DateTime(d.Founded);
            ClosedOn = new DateTime(d.ClosedOn);
            ClosedReason = d.ClosedReason ?? "";
            Reputation = F(d.ReputationBits);
            Tenure = (OfficeTenure)d.Tenure;
            DepositHeld = D(d.DepositHeld);
            PropertyValue = D(d.PropertyValue);
            ExpansionOpen = d.ExpansionOpen;
            NetworkUp = d.NetworkUp;
            _connectivityOverdue = d.ConnectivityOverdue;
            BillsOverdue = D(d.BillsOverdue);
            DelinquentDays = d.DelinquentDays;
            WindingUp = d.WindingUp;
            _windUpReason = d.WindUpReason ?? "";
            _clock = new DateTime(d.Clock);
            _dayDate = new DateTime(d.DayDate);
            _nextPersonId = d.NextPersonId;
            _nextListingId = d.NextListingId;
            _nextTradeId = d.NextTradeId;
            _nextJobId = d.NextJobId;
            var cash = new List<CashEntry>();
            foreach (CashSave c in d.Cash)
                cash.Add(new CashEntry { Id = c.Id, Time = c.Time, Employee = c.Employee, Kind = (CashKind)c.Kind, Amount = D(c.Amount), BalanceAfter = D(c.Balance), Memo = c.Memo ?? "" });
            var expenses = new List<ExpenseEntry>();
            foreach (ExpenseSave x in d.Expenses)
                expenses.Add(new ExpenseEntry { Time = x.Time, Employee = x.Employee, Kind = (ExpenseKind)x.Kind, Amount = D(x.Amount), Memo = x.Memo ?? "" });
            Ledger.Restore(cash, expenses, D(d.NetContributions), D(d.TotalExpenses), D(d.OtherIncome));
            _listings.Clear();
            _listings.AddRange(d.Listings ?? new List<Listing>());
            _notices.Clear();
            _notices.AddRange(d.Notices ?? new List<FundNotice>());
            _history.Clear();
            _history.AddRange(d.History ?? new List<CompanyDay>());
            _tradingAtLastClose = D(d.TradingAtLastClose);
            _employees.Clear();
            foreach (EmployeeSave s in d.Employees) _employees.Add(Restore(s));
            _applicants.Clear();
            foreach (ApplicantSave s in d.Applicants)
                _applicants.Add(new Applicant
                {
                    Person = Restore(s.Person), ListingId = s.ListingId, Received = s.Received, AvailableFrom = s.AvailableFrom, Expires = s.Expires,
                    Interest = F(s.Interest), Asking = Restore(s.Asking), LastCounter = s.HasCounter ? Restore(s.LastCounter) : null,
                    Lowballs = s.Lowballs, Rounds = s.Rounds, Status = (ApplicantStatus)s.Status, LastLine = s.LastLine ?? "",
                });
            _stationsVersion = -1;
            Touch();
        }

        private static PersonSave Capture(Person p) => new PersonSave
        {
            Id = p.Id, First = p.First, Last = p.Last, Savings = S(p.Savings), Car = p.Car, Age = p.Age, Years = p.Years, Specialty = (int)p.Specialty,
            Strategy = (int)p.Strategy, Look = p.Look, Skills = B(p.Skills), Caps = B(p.Caps), Traits = B(p.Traits),
            PastWinRate = B(p.PastWinRate), PastRewardRisk = B(p.PastRewardRisk), TradesPerDay = B(p.TradesPerDay), History = new List<HistoryEntry>(p.History),
        };

        private static Person Restore(PersonSave s) => new Person
        {
            Id = s.Id, First = s.First ?? "", Last = s.Last ?? "", Savings = D(s.Savings), Car = s.Car ?? "", Age = s.Age, Years = s.Years,
            Specialty = (OpeningBell.Market.Sector)s.Specialty, Strategy = (Strategy)s.Strategy, Look = s.Look ?? new PersonLook(),
            Skills = F(s.Skills, Person.SkillCount), Caps = F(s.Caps, Person.SkillCount), Traits = F(s.Traits, Person.TraitCount),
            PastWinRate = F(s.PastWinRate), PastRewardRisk = F(s.PastRewardRisk), TradesPerDay = F(s.TradesPerDay),
            History = s.History ?? new List<HistoryEntry>(),
        };

        private static ContractSave Capture(Contract c) => new ContractSave { Structure = (int)c.Structure, Version = c.Version, Hourly = S(c.Hourly), Rate = S(c.CommissionRate), Signed = c.Signed };

        private static Contract Restore(ContractSave c) => c == null ? new Contract()
            : new Contract { Structure = (PayStructure)c.Structure, Version = c.Version, Hourly = D(c.Hourly), CommissionRate = D(c.Rate), Signed = c.Signed };

        private static PlanSave Capture(EntryPlan p, long order, long due) => new PlanSave
        {
            Order = order, Due = due, Contracts = p.Contracts, Quality = B(p.Quality), Ticker = p.Ticker, Entry = S(p.Entry), Stop = S(p.Stop),
            Target = S(p.Target), Notes = p.Notes, Strategy = (int)p.Strategy, Side = p.Side, Followed = p.FollowedPlan,
        };

        private static EntryPlan Restore(PlanSave p) => new EntryPlan
        {
            Ticker = p.Ticker, Contracts = p.Contracts, Quality = F(p.Quality), Entry = D(p.Entry), Stop = D(p.Stop), Target = D(p.Target),
            Notes = p.Notes ?? "", Strategy = (Strategy)p.Strategy, Side = p.Side, FollowedPlan = p.Followed,
        };

        private EmployeeSave Capture(Employee e)
        {
            var s = new EmployeeSave
            {
                Person = Capture(e.Person), Contract = Capture(e.Contract),
                Policy = new PolicySave
                {
                    MaxContracts = e.Policy.MaxContracts, MaxPositions = e.Policy.MaxPositions, PreferredMaxTrades = e.Policy.PreferredMaxTrades,
                    MaxRisk = S(e.Policy.MaxRiskPerTrade), MaxDailyLoss = S(e.Policy.MaxDailyLoss), Instruments = new List<string>(e.Policy.Instruments),
                    Authorized = e.Policy.Authorized, PreferQuality = e.Policy.PreferQuality,
                },
                Role = (int)e.Role,
                HiredOn = e.HiredOn, StartsOn = e.StartsOn, LeftOn = e.LeftOn, LeftReason = e.LeftReason, ActivitySince = e.ActivitySince, MentorOf = e.MentorOf,
                LockReason = e.LockReason, Base = S(e.Base), NetRealized = S(e.NetRealized), HighWater = S(e.HighWater),
                CommissionAccrued = S(e.CommissionAccrued), WagesAccrued = S(e.WagesAccrued), WagesOverdue = S(e.WagesOverdue), CommissionOverdue = S(e.CommissionOverdue),
                Collected = S(e.TotalCollected), WagesPaid = S(e.TotalWagesPaid), CommissionPaid = S(e.TotalCommissionPaid), DayStartEquity = S(e.Memory.DayStartEquity),
                Desk = e.Desk, Activity = (int)e.Activity, Break = (int)e.Break, ConsecutiveLosses = e.ConsecutiveLosses, ConsecutiveWins = e.ConsecutiveWins,
                ResignationNotice = e.ResignationNotice, TradesToday = e.Memory.TradesToday, LockedToday = e.LockedToday, RaiseRequested = e.RaiseRequested,
                Flattened = e.Memory.FlattenedForClose, FrequencyNoted = e.Memory.FrequencyNoted, GreedNoted = e.Memory.GreedNoted,
                ImpulseNoted = e.Memory.ImpulseNoted, LastEntry = e.Memory.LastEntryTicks,
                Fatigue = B(e.Fatigue), Stress = B(e.Stress), LastScan = e.Memory.LastScanMinute, Satisfaction = B(e.Satisfaction), Waiting = B(e.WaitingMinutes), Plan = e.Plan,
                HasToday = e.Today != null, Behavior = new List<BehaviorNote>(e.Behavior), Mood = new List<MoodReason>(e.Mood),
                Desk_ = TradingState.Capture(e.Account, e.Orders, null),
            };
            foreach (Strategy st in e.Policy.Strategies) s.Policy.Strategies.Add((int)st);
            foreach (WorkDay w in e.Days)
                s.Days.Add(new DaySave
                {
                    Date = w.Date, Realized = S(w.Realized), Fees = S(w.Fees), Wages = S(w.Wages), Commission = S(w.Commission), Trades = w.Trades,
                    Wins = w.Wins, Losses = w.Losses, Waiting = w.MinutesWaiting, Trading = w.MinutesTrading, Training = w.MinutesTraining, Limit = w.LossLimitHit,
                });
            foreach (TradeRecord t in e.Trades)
                s.Trades.Add(new TradeSave
                {
                    Id = t.Id, Contracts = t.Contracts, Opened = t.Opened, Closed = t.Closed, ExitQuantity = t.ExitQuantity, Ticker = t.Ticker, Notes = t.Notes,
                    Strategy = (int)t.Strategy, Side = t.Side, Entry = S(t.Entry), Exit = S(t.Exit), Stop = S(t.PlannedStop), Target = S(t.PlannedTarget),
                    Gross = S(t.Gross), Fees = S(t.Fees), Net = S(t.Net), Risk = S(t.Risk), EntryNotional = S(t.EntryNotional), ExitNotional = S(t.ExitNotional),
                    Quality = B(t.Quality), Managed = t.LastManagedMinute, Followed = t.FollowedPlan, Breakeven = t.MovedToBreakeven, Trailed = t.Trailed, Widened = t.Widened,
                });
            foreach (TradeRecord t in e.Open.Values) s.OpenTrades.Add(t.Id);
            foreach (TrainingJob j in e.Training)
                s.Training.Add(new JobSave
                {
                    Id = j.Id, Bought = j.Bought, Started = j.Started, Finished = j.Finished, Gain = B(j.Gain), Done = B(j.MinutesDone),
                    Skill = (int)j.Skill, Level = j.Level, Minutes = j.Minutes, Price = S(j.Price),
                });
            foreach (var kv in e.Memory.Pending) s.Pending.Add(Capture(kv.Value, kv.Key, 0));
            foreach (var (due, plan) in e.Memory.Delayed) s.Delayed.Add(Capture(plan, 0, due));
            return s;
        }

        private Employee Restore(EmployeeSave s)
        {
            var e = new Employee
            {
                Person = Restore(s.Person), Contract = Restore(s.Contract),
                Policy = new RiskPolicy
                {
                    MaxContracts = s.Policy.MaxContracts, MaxPositions = s.Policy.MaxPositions, PreferredMaxTrades = s.Policy.PreferredMaxTrades,
                    MaxRiskPerTrade = D(s.Policy.MaxRisk), MaxDailyLoss = D(s.Policy.MaxDailyLoss), Instruments = s.Policy.Instruments ?? new List<string>(),
                    Authorized = s.Policy.Authorized, PreferQuality = s.Policy.PreferQuality,
                },
                Role = (Role)s.Role,
                HiredOn = s.HiredOn, StartsOn = s.StartsOn, LeftOn = s.LeftOn, LeftReason = s.LeftReason ?? "", ActivitySince = s.ActivitySince, MentorOf = s.MentorOf,
                LockReason = s.LockReason ?? "", Base = D(s.Base), NetRealized = D(s.NetRealized), HighWater = D(s.HighWater),
                CommissionAccrued = D(s.CommissionAccrued), WagesAccrued = D(s.WagesAccrued), WagesOverdue = D(s.WagesOverdue), CommissionOverdue = D(s.CommissionOverdue),
                TotalCollected = D(s.Collected), TotalWagesPaid = D(s.WagesPaid), TotalCommissionPaid = D(s.CommissionPaid),
                Desk = s.Desk, Activity = (Activity)s.Activity, Break = (BreakKind)s.Break, ConsecutiveLosses = s.ConsecutiveLosses, ConsecutiveWins = s.ConsecutiveWins,
                ResignationNotice = s.ResignationNotice, LockedToday = s.LockedToday, RaiseRequested = s.RaiseRequested,
                Fatigue = F(s.Fatigue), Stress = F(s.Stress), Satisfaction = F(s.Satisfaction), WaitingMinutes = F(s.Waiting), Plan = s.Plan,
            };
            foreach (int st in s.Policy.Strategies ?? new List<int>()) e.Policy.Strategies.Add((Strategy)st);
            e.Memory.DayStartEquity = D(s.DayStartEquity);
            e.Memory.TradesToday = s.TradesToday;
            e.Memory.LastScanMinute = s.LastScan;
            e.Memory.FlattenedForClose = s.Flattened;
            e.Memory.FrequencyNoted = s.FrequencyNoted;
            e.Memory.GreedNoted = s.GreedNoted;
            e.Memory.ImpulseNoted = s.ImpulseNoted;
            e.Memory.LastEntryTicks = s.LastEntry;
            Wire(e);
            TradingState.Restore(s.Desk_, e.Account, e.Orders, null);
            foreach (DaySave w in s.Days)
                e.Days.Add(new WorkDay
                {
                    Date = w.Date, Realized = D(w.Realized), Fees = D(w.Fees), Wages = D(w.Wages), Commission = D(w.Commission), Trades = w.Trades,
                    Wins = w.Wins, Losses = w.Losses, MinutesWaiting = w.Waiting, MinutesTrading = w.Trading, MinutesTraining = w.Training, LossLimitHit = w.Limit,
                });
            if (s.HasToday && e.Days.Count > 0) e.Today = e.Days[e.Days.Count - 1];
            foreach (TradeSave t in s.Trades)
                e.Trades.Add(new TradeRecord
                {
                    Id = t.Id, Contracts = t.Contracts, Opened = t.Opened, Closed = t.Closed, ExitQuantity = t.ExitQuantity, Ticker = t.Ticker ?? "", Notes = t.Notes ?? "",
                    Strategy = (Strategy)t.Strategy, Side = t.Side, Entry = D(t.Entry), Exit = D(t.Exit), PlannedStop = D(t.Stop), PlannedTarget = D(t.Target),
                    Gross = D(t.Gross), Fees = D(t.Fees), Net = D(t.Net), Risk = D(t.Risk), EntryNotional = D(t.EntryNotional), ExitNotional = D(t.ExitNotional),
                    Quality = F(t.Quality), LastManagedMinute = t.Managed, FollowedPlan = t.Followed, MovedToBreakeven = t.Breakeven, Trailed = t.Trailed, Widened = t.Widened,
                });
            foreach (long id in s.OpenTrades)
            {
                TradeRecord t = e.Trades.Find(x => x.Id == id);
                if (t != null) e.Open[t.Ticker] = t;
            }
            e.Behavior.AddRange(s.Behavior ?? new List<BehaviorNote>());
            e.Mood.AddRange(s.Mood ?? new List<MoodReason>());
            foreach (JobSave j in s.Training)
                e.Training.Add(new TrainingJob
                {
                    Id = j.Id, Bought = j.Bought, Started = j.Started, Finished = j.Finished, Gain = F(j.Gain), MinutesDone = F(j.Done),
                    Skill = (Skill)j.Skill, Level = j.Level, Minutes = j.Minutes, Price = D(j.Price),
                });
            foreach (PlanSave p in s.Pending) e.Memory.Pending[p.Order] = Restore(p);
            foreach (PlanSave p in s.Delayed) e.Memory.Delayed.Add((p.Due, Restore(p)));
            return e;
        }
    }
}
