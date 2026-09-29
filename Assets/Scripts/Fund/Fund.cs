using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Core;
using OpeningBell.Home;
using OpeningBell.Market;
using OpeningBell.Trading;

namespace OpeningBell.Fund
{
    public enum OfficeTenure { None, Leased, Owned }

    public enum NoticeLevel { Routine, Important, Urgent }

    /// <summary>Something the owner should hear about (dashboard alerts, phone notifications, the inbox).</summary>
    [Serializable]
    public sealed class FundNotice
    {
        public long Time;
        public NoticeLevel Level;
        public string Title = "", Body = "";
        public long Employee;
        /// <summary>Routine notices of one kind within an hour are folded into one ("3 traders arrived").</summary>
        public string Group = "";
        public int Count = 1;
    }

    /// <summary>One business day of the company, recorded after everyone's gone home (charts and reports).</summary>
    [Serializable]
    public sealed class CompanyDay
    {
        public long Date;
        public string Equity = "0", Cash = "0", Trading = "0", Expenses = "0", Allocated = "0";
        public int Staff, Trades;

        private static decimal P(string v) => string.IsNullOrEmpty(v) ? 0m : decimal.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
        public decimal EquityValue => P(Equity);
        public decimal TradingValue => P(Trading);
        public decimal ExpensesValue => P(Expenses);
        public decimal CashValue => P(Cash);
        public decimal AllocatedValue => P(Allocated);
    }

    /// <summary>
    /// The player's hedge fund (FUND_SPEC): identity, operating cash, office, people, their desks and the rules that
    /// run them. Pure simulation: the world displays it. Time comes from the market clock: <see cref="AdvanceTo"/> in
    /// one-minute steps (payroll, schedules, training, bills) and the market's ticks (trading), so results don't depend
    /// on frame rate, time scale, or whether anyone is watching.
    /// </summary>
    public sealed partial class HedgeFund
    {
        public const string OfficeId = "harborview_office";
        public const string OfficeName = "Harborview Tower, Level 26";
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        private readonly MarketSimulation _market;
        private readonly BrokerRules _rules;
        private readonly Belongings _belongings;
        private readonly CounterRandom _rng;
        private readonly LiquidityShare _liquidity = new LiquidityShare();
        private readonly List<Employee> _employees = new List<Employee>();
        private readonly List<Applicant> _applicants = new List<Applicant>();
        private readonly List<Listing> _listings = new List<Listing>();
        private readonly List<FundNotice> _notices = new List<FundNotice>();
        private readonly List<CompanyDay> _history = new List<CompanyDay>();
        private decimal _tradingAtLastClose;
        private DateTime _clock;
        private long _nextPersonId = 1, _nextListingId = 1, _nextTradeId = 1, _nextJobId = 1;

        public FundConfig Config { get; }
        public CompanyLedger Ledger { get; } = new CompanyLedger();

        // ---- identity
        public bool Exists { get; private set; }
        public string Name { get; private set; } = "";
        public int Logo { get; private set; }
        public int Colour { get; private set; }
        public DateTime Founded { get; private set; }
        public DateTime ClosedOn { get; private set; }
        public string ClosedReason { get; private set; } = "";
        /// <summary>0–100, moves slowly with sustained results, pay reliability, treatment of staff and risk discipline.</summary>
        public double Reputation { get; private set; } = 30;

        // ---- office
        public OfficeTenure Tenure { get; private set; }
        public decimal DepositHeld { get; private set; }
        public decimal PropertyValue { get; private set; }
        public bool ExpansionOpen { get; private set; }
        /// <summary>Fibre paid up: without it no station works.</summary>
        public bool NetworkUp { get; private set; } = true;
        public decimal BillsOverdue { get; private set; }
        /// <summary>Business days in a row with wages or bills overdue.</summary>
        public int DelinquentDays { get; private set; }

        /// <summary>Takes money from the owner's bank: (amount, what) → error or null.</summary>
        public Func<decimal, string, string> ChargeOwner { get; set; }
        /// <summary>Pays into the owner's bank.</summary>
        public Action<decimal, string> PayOwner { get; set; }
        /// <summary>The world's physical check of a station (seat reachable): why not, or null.</summary>
        public Func<Workstation, string> Access { get; set; }

        public event Action Changed;
        public event Action<FundNotice> Noticed;
        /// <summary>A fill on any employee's desk (sounds, screens).</summary>
        public event Action<Employee, Fill> Filled;

        public IReadOnlyList<Employee> Employees => _employees;
        public IReadOnlyList<Applicant> Applicants => _applicants;
        public IReadOnlyList<Listing> Listings => _listings;
        public IReadOnlyList<FundNotice> Notices => _notices;
        public IReadOnlyList<CompanyDay> History => _history;
        public MarketSimulation Market => _market;
        public DateTime Now => _clock;

        public HedgeFund(MarketSimulation market, BrokerRules rules, Belongings belongings, ulong worldSeed, FundConfig config = null)
        {
            _market = market;
            _rules = rules;
            _belongings = belongings;
            Config = config ?? new FundConfig();
            _rng = new CounterRandom(worldSeed, "fund");
            _clock = Minute(market.Now);
            _market.Ticked += OnTick;
            _market.SessionChanged += OnSessionChanged;
        }

        public IEnumerable<Employee> Staff
        {
            get { foreach (Employee e in _employees) if (!e.Former) yield return e; }
        }

        public Employee Find(long id) => _employees.Find(e => e.Id == id);
        public Applicant FindApplicant(long id) => _applicants.Find(a => a.Id == id);

        private void Touch() => Changed?.Invoke();

        // ------------------------------------------------------------------ forming the company

        /// <summary>Why a fund can't be registered right now (career, cooldown, money), or null.</summary>
        public string RegistrationBlocker(int tradingDays, decimal provenProfit, decimal ownerBank)
        {
            if (Exists) return $"You already run {Name}.";
            if (ClosedOn != default && (_market.Now - ClosedOn).TotalDays < Config.ReformCooldownDays)
                return string.Format(C, "The registry won't take a new fund from you until {0:MMM d}.", ClosedOn.AddDays(Config.ReformCooldownDays));
            string career = CareerProgress.Blocker(Config, tradingDays, provenProfit);
            if (career != null) return career;
            decimal need = Config.FormationFee + Config.StartingCapital;
            if (ownerBank < need)
                return string.Format(C, "You need ${0:N0} in your bank: ${1:N0} formation and setup plus ${2:N0} starting capital (you have ${3:N0}).",
                    need, Config.FormationFee, Config.StartingCapital, Math.Max(0m, ownerBank));
            return null;
        }

        public static string NameProblem(string name)
        {
            string n = (name ?? "").Trim();
            if (n.Length < 3) return "The fund's name needs at least 3 characters.";
            if (n.Length > 36) return "Keep the name to 36 characters.";
            foreach (char ch in n)
                if (!(char.IsLetterOrDigit(ch) || ch == ' ' || ch == '&' || ch == '-' || ch == '.' || ch == '\''))
                    return "Letters, digits, spaces, & - . and ' only.";
            return null;
        }

        /// <summary>
        /// Forms the fund: the formation fee is spent from the owner's bank, the starting capital (plus any extra) moves
        /// into the company, and the office is taken (leased: first month and deposit from the company; bought: the price
        /// from the company). One call, so a save can never hold half a registration. Error, or null.
        /// </summary>
        public string Register(string name, int logo, int colour, OfficeTenure office, decimal extraCapital,
            int tradingDays, decimal provenProfit, decimal ownerBank)
        {
            string blocked = RegistrationBlocker(tradingDays, provenProfit, ownerBank - Math.Max(0m, extraCapital));
            if (blocked != null) return blocked;
            string nameProblem = NameProblem(name);
            if (nameProblem != null) return nameProblem;
            if (office == OfficeTenure.None) return "Choose how to take the office: lease or buy.";
            decimal capital = Config.StartingCapital + Math.Max(0m, extraCapital);
            decimal officeCost = office == OfficeTenure.Leased ? Config.OfficeMonthlyRent + Config.OfficeDeposit : Config.OfficePurchasePrice;
            if (officeCost > capital)
                return string.Format(C, "Buying the floor takes ${0:N0} of company cash: contribute at least ${1:N0} extra, or lease it.",
                    Config.OfficePurchasePrice, Config.OfficePurchasePrice - Config.StartingCapital);
            if (ChargeOwner == null) return "Payments aren't available.";

            DateTime now = _market.Now;
            string trimmed = name.Trim();
            // Two separate bank lines: the fee is gone; the capital is still the owner's, inside the company.
            string error = ChargeOwner(Config.FormationFee, $"Fund formation and setup: {trimmed}");
            if (error != null) return error;
            error = ChargeOwner(capital, $"Starting capital to {trimmed}");
            if (error != null)
            {
                PayOwner?.Invoke(Config.FormationFee, "Formation fee refunded (capital transfer failed)");
                return error;
            }

            Exists = true;
            Name = trimmed;
            Logo = logo;
            Colour = colour;
            Founded = now;
            ClosedOn = default;
            ClosedReason = "";
            Reputation = 30;
            _clock = Minute(now);
            Ledger.Post(now, CashKind.OwnerContribution, capital, "Starting capital from the owner");
            TakeOffice(office, now);
            Notify(NoticeLevel.Important, $"{Name} is registered", "Your fund exists. Furnish Level 26, post a job ad on Ledgerline and hire your first trader.");
            Touch();
            return null;
        }

        private void TakeOffice(OfficeTenure tenure, DateTime now)
        {
            Tenure = tenure;
            NetworkUp = true;
            if (tenure == OfficeTenure.Leased)
            {
                Pay(now, CashKind.Rent, ExpenseKind.Rent, Config.OfficeMonthlyRent, $"{OfficeName}: first month's rent");
                Ledger.Post(now, CashKind.Deposit, -Config.OfficeDeposit, "Lease deposit (held by the landlord)");
                DepositHeld = Config.OfficeDeposit;
            }
            else
            {
                Ledger.Post(now, CashKind.OfficePurchase, -Config.OfficePurchasePrice, $"{OfficeName}: purchase");
                PropertyValue = Config.OfficePurchasePrice;
            }
        }

        /// <summary>Buys out a leased floor (company cash). The deposit comes back. Error, or null.</summary>
        public string BuyOffice()
        {
            if (!Exists || Tenure != OfficeTenure.Leased) return "Only a leased office can be bought.";
            if (Ledger.Cash + DepositHeld < Config.OfficePurchasePrice)
                return string.Format(C, "The company needs ${0:N0} (it has ${1:N0} plus the ${2:N0} deposit).", Config.OfficePurchasePrice, Ledger.Cash, DepositHeld);
            DateTime now = _market.Now;
            Ledger.Post(now, CashKind.DepositReturned, DepositHeld, "Lease deposit returned");
            DepositHeld = 0m;
            Ledger.Post(now, CashKind.OfficePurchase, -Config.OfficePurchasePrice, $"{OfficeName}: purchase");
            PropertyValue = Config.OfficePurchasePrice;
            Tenure = OfficeTenure.Owned;
            Touch();
            return null;
        }

        /// <summary>Fits out the east wing: more room for workstation rows. Error, or null.</summary>
        public string OpenExpansion()
        {
            if (!Exists) return "No fund.";
            if (ExpansionOpen) return "The east wing is already open.";
            string error = Spend(_market.Now, CashKind.FitOut, ExpenseKind.FitOut, Config.ExpansionFitOut, "East wing fit-out");
            if (error != null) return error;
            ExpansionOpen = true;
            Notify(NoticeLevel.Important, "East wing open", "The partition is down and the east wing of the trading floor is lit: room for two more rows of desks.");
            Touch();
            return null;
        }

        /// <summary>A new logo or colour (the registered name stays).</summary>
        public void Rebrand(int logo, int colour)
        {
            Logo = FundBrand.Clamp(logo, FundBrand.Logos.Length);
            Colour = FundBrand.Clamp(colour, FundBrand.Colours.Length);
            Touch();
        }

        // ------------------------------------------------------------------ owner money

        /// <summary>Personal bank → company. Error, or null.</summary>
        public string Contribute(decimal amount)
        {
            if (!Exists) return "No fund.";
            if (amount <= 0m) return "Enter an amount.";
            if (ChargeOwner == null) return "Payments aren't available.";
            string error = ChargeOwner(amount, $"Contribution to {Name}");
            if (error != null) return error;
            Ledger.Post(_market.Now, CashKind.OwnerContribution, amount, "Owner contribution");
            SettleOverdue(_market.Now);
            Touch();
            return null;
        }

        /// <summary>Company → personal bank, only from operating cash not needed for what's owed. Error, or null.</summary>
        public string Withdraw(decimal amount)
        {
            if (!Exists) return "No fund.";
            if (amount <= 0m) return "Enter an amount.";
            decimal free = FreeCash;
            if (amount > free)
                return string.Format(C, "Only ${0:N2} is free: the rest covers wages, commissions and bills already owed.", Math.Max(0m, free));
            Ledger.Post(_market.Now, CashKind.OwnerWithdrawal, -amount, "Owner withdrawal");
            PayOwner?.Invoke(amount, $"Withdrawal from {Name}");
            Touch();
            return null;
        }

        // ------------------------------------------------------------------ the numbers

        /// <summary>Wages, commissions and bills owed (accrued or overdue): the company's liabilities.</summary>
        public decimal Liabilities
        {
            get
            {
                decimal sum = BillsOverdue;
                foreach (Employee e in _employees) sum += e.WagesAccrued + e.CommissionAccrued + e.WagesOverdue + e.CommissionOverdue;
                return sum;
            }
        }

        /// <summary>Capital sitting on desks (what was allocated and not recalled).</summary>
        public decimal Allocated
        {
            get
            {
                decimal sum = 0m;
                foreach (Employee e in _employees) sum += e.Base;
                return sum;
            }
        }

        public decimal DeskEquity
        {
            get
            {
                decimal sum = 0m;
                foreach (Employee e in _employees) sum += e.DeskEquity;
                return sum;
            }
        }

        public decimal Unrealized
        {
            get
            {
                decimal sum = 0m;
                foreach (Employee e in _employees) sum += e.Unrealized;
                return sum;
            }
        }

        /// <summary>Everything the company is worth: cash, desks, the deposit and the floor if owned, less what it owes.</summary>
        public decimal Equity => Ledger.Cash + DeskEquity + DepositHeld + PropertyValue - Liabilities;

        /// <summary>Operating cash not already owed to someone.</summary>
        public decimal FreeCash => Ledger.Cash - Liabilities;

        /// <summary>Cumulative trading result of every desk after execution costs (realized + open).</summary>
        public decimal TradingNet
        {
            get
            {
                decimal sum = 0m;
                foreach (Employee e in _employees)
                    if (e.Account != null) sum += e.Account.RealizedPnL + e.Account.UnrealizedPnL - e.Account.TotalCommissions;
                return sum;
            }
        }

        // ------------------------------------------------------------------ capital on desks

        /// <summary>Sets an employee's trading capital: moves the difference between operating cash and their desk. Error, or null.</summary>
        public string Allocate(Employee e, decimal target)
        {
            if (e.Former) return $"{e.Name} no longer works here.";
            if (target < 0m) return "Capital can't be negative.";
            DateTime now = _market.Now;
            decimal delta = target - e.Base;
            if (delta > 0m)
            {
                if (delta > FreeCash) return string.Format(C, "Only ${0:N2} of operating cash is free.", Math.Max(0m, FreeCash));
                Ledger.Post(now, CashKind.Allocation, -delta, $"Capital to {e.Name}", e.Id);
                e.Account.Deposit(delta, "Capital from the company");
                e.Base += delta;
            }
            else if (delta < 0m)
            {
                // Take back what's actually there and free. The base is net capital moved in, so it falls by what moved:
                // a desk that's down keeps its loss to earn back before any profit counts.
                decimal want = -delta;
                decimal free = Math.Min(e.Account.Cash, e.Account.BuyingPower);
                decimal move = Math.Min(want, Math.Max(0m, Math.Floor(free * 100m) / 100m));
                if (move <= 0m) return e.Account.Cash > 0m ? "That capital is posting margin: close positions first." : "The desk has no cash left to recall.";
                e.Account.Withdraw(move, "Capital recalled by the company");
                Ledger.Post(now, CashKind.Recall, move, $"Capital recalled from {e.Name}", e.Id);
                e.Base -= move;
                if (e.Account.Cash <= 0m) e.Base = 0m; // emptied: nothing left to earn back on this desk
            }
            e.Person.Note(now, string.Format(C, "Trading capital set to ${0:N0}", e.Base));
            Touch();
            return null;
        }

        /// <summary>Moves an employee's collectible profit to operating cash. Returns the amount moved (0 with a reason).</summary>
        public decimal Collect(Employee e, out string error)
        {
            error = e.CollectBlocker;
            if (error != null) return 0m;
            decimal amount = e.Collectible;
            DateTime now = _market.Now;
            // Both sides of one transfer: the desk pays out, operating cash receives. Equity doesn't move.
            e.Account.Withdraw(amount, "Profit collected by the company");
            Ledger.Post(now, CashKind.Collection, amount, $"Profit collected from {e.Name}", e.Id);
            e.TotalCollected += amount;
            SettleOverdue(now);
            Touch();
            return amount;
        }

        public decimal CollectAll()
        {
            decimal total = 0m;
            foreach (Employee e in Staff) total += Collect(e, out _);
            return total;
        }

        public decimal CollectibleTotal
        {
            get
            {
                decimal sum = 0m;
                foreach (Employee e in _employees) sum += e.Collectible;
                return sum;
            }
        }

        // ------------------------------------------------------------------ paying things

        /// <summary>Recognises an expense and pays it now if cash allows (else the whole amount is owed). Error when unpaid.</summary>
        private string Pay(DateTime now, CashKind cash, ExpenseKind expense, decimal amount, string memo, long employee = 0)
        {
            Ledger.Recognise(now, expense, amount, memo, employee);
            if (Ledger.Cash >= amount)
            {
                Ledger.Post(now, cash, -amount, memo, employee);
                return null;
            }
            BillsOverdue += amount;
            return string.Format(C, "Not enough operating cash for {0} (${1:N2}): it's overdue.", memo, amount);
        }

        /// <summary>A purchase the company chooses to make: refused (not owed) when cash won't cover it.</summary>
        private string Spend(DateTime now, CashKind cash, ExpenseKind expense, decimal amount, string memo, long employee = 0)
        {
            if (!Exists) return "No fund.";
            if (FreeCash < amount) return string.Format(C, "{0} costs ${1:N0}; the company has ${2:N0} free.", memo, amount, Math.Max(0m, FreeCash));
            Ledger.Recognise(now, expense, amount, memo, employee);
            Ledger.Post(now, cash, -amount, memo, employee);
            return null;
        }

        /// <summary>
        /// A store purchase billed to the company card (furniture and equipment for the office). Error, or null.
        /// </summary>
        public string BuyEquipment(decimal price, string what) => Spend(_market.Now, CashKind.Equipment, ExpenseKind.Equipment, price, what);

        /// <summary>Company-owned equipment sold: the money comes to the company.</summary>
        public void SoldEquipment(decimal price, string what)
        {
            if (!Exists || price <= 0m) return;
            Ledger.Post(_market.Now, CashKind.EquipmentSale, price, what);
            Touch();
        }

        // ------------------------------------------------------------------ notices

        internal void Notify(NoticeLevel level, string title, string body, long employee = 0, string group = "")
        {
            DateTime now = _market.Now;
            if (level == NoticeLevel.Routine && group.Length > 0)
            {
                // Fold routine notices of the same group within the hour.
                for (int i = _notices.Count - 1; i >= 0 && i >= _notices.Count - 12; i--)
                {
                    FundNotice n = _notices[i];
                    if (n.Group == group && now.Ticks - n.Time < TimeSpan.TicksPerHour)
                    {
                        n.Count++;
                        n.Title = title;
                        n.Body = body;
                        n.Time = now.Ticks;
                        Noticed?.Invoke(n);
                        return;
                    }
                }
            }
            var notice = new FundNotice { Time = now.Ticks, Level = level, Title = title, Body = body, Employee = employee, Group = group };
            _notices.Add(notice);
            if (_notices.Count > 300) _notices.RemoveAt(0);
            Noticed?.Invoke(notice);
        }

        private static DateTime Minute(DateTime t) => new DateTime(t.Ticks - t.Ticks % TimeSpan.TicksPerMinute);

        internal static string Money(decimal d) => (d < 0m ? "-$" : "$") + Math.Abs(d).ToString("N2", C);
    }
}
