using System;
using System.Collections.Generic;
using OpeningBell.Core;
using OpeningBell.Home;
using OpeningBell.Market;

namespace OpeningBell.Fund
{
    public sealed partial class HedgeFund
    {
        private DateTime _dayDate;
        private List<Workstation> _stations = new List<Workstation>();
        private int _stationsVersion = -1;
        private bool _stationsNetwork, _connectivityOverdue;
        /// <summary>Bumped by the world when furniture moved in a way the physics check should see.</summary>
        public int AccessVersion { get; set; }
        private int _accessSeen = -1;
        public bool WindingUp { get; private set; }
        private string _windUpReason = "";

        // ------------------------------------------------------------------ time

        /// <summary>Runs every whole minute up to <paramref name="now"/> (market-local). Safe to call often.</summary>
        public void AdvanceTo(DateTime now)
        {
            DateTime target = Minute(now);
            while (_clock < target)
            {
                _clock = _clock.AddMinutes(1);
                if (Exists) Step(_clock);
            }
        }

        private void Step(DateTime t)
        {
            if (t.Date != _dayDate) StartDay(t);
            int m = t.Hour * 60 + t.Minute;
            if (t.Minute == 0) Applications(t);
            RefreshStations();

            foreach (Employee e in _employees) Tick(e, t, m);

            if (m == 9 * 60 + 5 && _market.Schedule.IsTradingDay(t.Date)) AdminMorning(t);
            if (t.DayOfWeek == Config.PayDay && m == Config.PayMinute) Payroll(t);
            if (m == 17 * 60 + 30 && _market.Schedule.IsTradingDay(t.Date)) EndDay(t);
            if (WindingUp) TryFinishWindUp(t);
        }

        private void StartDay(DateTime t)
        {
            _dayDate = t.Date;
            bool workday = _market.Schedule.IsTradingDay(t.Date);
            foreach (Employee e in _employees)
            {
                if (e.Former) continue;
                e.Plan = PlanDay(e, t.Date, workday);
                e.LockedToday = false;
                e.LockReason = "";
                e.Fatigue = 0;
                e.Today = workday ? new WorkDay { Date = t.Date.Ticks } : null;
                if (e.Today != null) e.Days.Add(e.Today);
                if (e.Days.Count > 260) e.Days.RemoveAt(0);
                e.Memory.TradesToday = 0;
                e.Memory.FlattenedForClose = false;
                e.Memory.FrequencyNoted = e.Memory.GreedNoted = e.Memory.ImpulseNoted = false;
            }
            if (t.Day == 1)
            {
                MonthlyBills(t);
                foreach (Employee e in _employees) if (!e.Former) CarReview(e, t);
            }
            SettleOverdue(t);
            if (workday)
            {
                bool owing = BillsOverdue > 0m;
                foreach (Employee e in _employees) if (e.WagesOverdue > 0m || e.CommissionOverdue > 0m) owing = true;
                DelinquentDays = owing ? DelinquentDays + 1 : 0;
                if (owing)
                {
                    foreach (Employee e in Staff) if (e.WagesOverdue > 0m) e.Feel(t, "Wages overdue", -3);
                    if (DelinquentDays >= Config.DefaultDays) WindUp("Wages unpaid for " + Config.DefaultDays + " business days: the fund is in default.");
                    else if (DelinquentDays >= 3)
                        Notify(NoticeLevel.Urgent, "Payments overdue", $"The company has owed money for {DelinquentDays} business days. At {Config.DefaultDays} it will be wound up. Contribute cash, collect profit or recall capital.");
                }
            }
            Touch();
        }

        /// <summary>
        /// Today's timetable for one person: arrive around 8:30 (their own punctuality and a daily wobble), breaks at
        /// times of their own, leave after the close. Drawn from the world seed, the person and the date only.
        /// </summary>
        private DayPlan PlanDay(Employee e, DateTime date, bool workday)
        {
            var plan = new DayPlan { Date = date.Ticks, Workday = workday && date.Ticks >= new DateTime(e.StartsOn).Date.Ticks };
            if (!plan.Workday) return plan;
            CounterRandom r = _rng.Sub("day").Sub(e.Id);
            long d = date.Ticks / TimeSpan.TicksPerDay;
            int k = 0;
            double U() => r.Double(d, k++);
            double G() => r.Gaussian(d, k++);
            double punctual = e.Person.Trait(Trait.Loyalty) / 100.0;
            plan.Arrive = Clamp(Config.ArriveMinute + (int)Math.Round(4 * G() - 6 * (punctual - 0.5)), Config.ArriveMinute - 18, Config.ArriveMinute + 22);
            plan.ReachBuilding = plan.Arrive - Config.BuildingMinutes;
            plan.LeaveHome = plan.ReachBuilding - Config.CommuteMinutes - (int)(4 * U());
            plan.Leave = Config.LeaveMinute + (int)Math.Round(12 * U() - 6);
            plan.ReachHome = plan.Leave + 10 + Config.CommuteMinutes;
            double social = e.Person.Trait(Trait.Sociability) / 100.0;
            plan.MorningBreak = 10 * 60 + 30 + (int)(40 * U());
            plan.MorningBreakLength = 7 + (int)(8 * social);
            plan.MorningKind = U() < social ? BreakKind.Chat : BreakKind.Coffee;
            plan.Lunch = 11 * 60 + 50 + (int)(55 * U());
            plan.LunchLength = 20 + (int)(15 * U());
            plan.AfternoonBreak = 14 * 60 + 10 + (int)(45 * U());
            plan.AfternoonBreakLength = 6 + (int)(7 * U());
            plan.AfternoonKind = U() < 0.5 ? BreakKind.Coffee : BreakKind.Rest;
            if (U() < 0.7) plan.Restroom = 9 * 60 + 50 + (int)(90 * U());
            if (U() < 0.5) plan.Restroom2 = 13 * 60 + 20 + (int)(100 * U());
            return plan;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        // ------------------------------------------------------------------ one person, one minute

        private void Tick(Employee e, DateTime t, int m)
        {
            if (e.Former)
            {
                if (e.Activity == Activity.Leaving && t.Ticks - e.ActivitySince >= TimeSpan.TicksPerMinute * 12) SetActivity(e, Activity.Former, t);
                if (e.Account != null && e.Account.Cash > 0m && e.IsFlat) RecallAll(e, t);
                return;
            }
            Activity next = Decide(e, t, m);
            if (next != e.Activity) SetActivity(e, next, t);

            DayPlan plan = e.Plan;
            bool onSite = plan != null && plan.OnSite(m);
            if (onSite && e.Contract.PaysHourly)
            {
                decimal wage = e.Contract.Hourly / 60m;
                e.WagesAccrued += wage;
                if (e.Today != null) e.Today.Wages += wage;
                Ledger.Recognise(t, ExpenseKind.Wages, wage, "Wages: " + e.Name, e.Id);
            }

            // Focus and stress: work tires (stamina slows it), breaks restore; stress fades with composure.
            double stamina = e.Person[Skill.Stamina] / 100.0;
            switch (e.Activity)
            {
                case Activity.Trading:
                case Activity.Training:
                case Activity.Preparing:
                case Activity.RiskLocked:
                case Activity.Admin:
                    e.Fatigue = Math.Min(1, e.Fatigue + 0.0021 * (1.35 - stamina));
                    break;
                case Activity.OnBreak:
                    e.Fatigue = Math.Max(0, e.Fatigue - (e.Break == BreakKind.Lunch ? 0.012 : 0.008));
                    break;
            }
            e.Stress = Math.Max(0, e.Stress - 0.0015 * (0.4 + e.Person.Trait(Trait.Composure) / 100.0));

            if (e.Activity == Activity.WaitingForWorkstation)
            {
                e.WaitingMinutes++;
                if (e.Today != null) e.Today.MinutesWaiting++;
            }
            if (e.Activity == Activity.Trading && e.Today != null) e.Today.MinutesTrading++;
            if (e.Activity == Activity.Training) TrainMinute(e, t);
        }

        /// <summary>The state the simulation puts someone in at this minute (FUND_SPEC §4).</summary>
        private Activity Decide(Employee e, DateTime t, int m)
        {
            if (t.Ticks < new DateTime(e.StartsOn).Date.Ticks) return Activity.AwaitingStart;
            DayPlan p = e.Plan;
            if (p == null || !p.Workday) return Activity.OffDuty;
            if (m < p.LeaveHome) return Activity.OffDuty;
            if (m < p.ReachBuilding) return Activity.Commuting;
            if (m < p.Arrive) return Activity.Arriving;
            if (m >= p.ReachHome) return Activity.OffDuty;
            if (m >= p.Leave + 10) return Activity.Commuting;
            if (m >= p.Leave) return Activity.Leaving;

            Workstation station = StationOf(e);
            BreakKind brk = p.BreakAt(m);
            bool regular = _market.Schedule.GetSession(t) == MarketSession.Regular;
            // Nobody walks away from an open trade for coffee (the brackets would cope, but traders don't): the break waits.
            if (brk != BreakKind.None && brk != BreakKind.Restroom && regular && !e.IsFlat) brk = BreakKind.None;
            if (brk != BreakKind.None)
            {
                e.Break = brk;
                return Activity.OnBreak;
            }
            e.Break = BreakKind.None;
            // Support staff work from the front desk: no workstation, no trading.
            if (e.IsAdmin) return Activity.Admin;
            if (station == null) return Activity.WaitingForWorkstation;
            if (e.SettleUntil > t.Ticks) return Activity.SettlingIn;

            TrainingJob job = e.CurrentTraining;
            // Training begins only when they're at their desk and flat; one already running carries on.
            if (job != null && (job.Started != 0 || e.IsFlat)) return Activity.Training;
            if (!regular) return m < 9 * 60 + 30 ? Activity.Preparing : Activity.WrappingUp;
            if (e.LockedToday) return Activity.RiskLocked;
            return Activity.Trading;
        }

        internal void SetActivity(Employee e, Activity a, DateTime t)
        {
            Activity was = e.Activity;
            e.Activity = a;
            e.ActivitySince = t.Ticks;
            if (was == Activity.Arriving && (a == Activity.WaitingForWorkstation || a == Activity.Preparing || a == Activity.Training || a == Activity.Trading || a == Activity.Admin))
            {
                Notify(NoticeLevel.Routine, "Traders arrived", $"{e.Name} is in the office.", e.Id, "arrived");
                if (a == Activity.WaitingForWorkstation)
                    Notify(NoticeLevel.Important, "Workstation incomplete", $"{e.Name} arrived with no working desk: {StationProblem(e)}", e.Id, "");
            }
        }

        public long WorkMinuteOf(DateTime t) => t.Hour * 60 + t.Minute;

        // ------------------------------------------------------------------ workstations

        /// <summary>The office's desks and what each lacks, rebuilt when furniture or services change.</summary>
        public IReadOnlyList<Workstation> Stations
        {
            get
            {
                RefreshStations();
                return _stations;
            }
        }

        private void RefreshStations()
        {
            bool network = NetworkUp;
            if (_belongings == null || (_belongings.Version == _stationsVersion && network == _stationsNetwork && AccessVersion == _accessSeen)) return;
            _stationsVersion = _belongings.Version;
            _stationsNetwork = network;
            _accessSeen = AccessVersion;
            bool power = Exists && Tenure != OfficeTenure.None;
            _stations = WorkstationRules.Find(_belongings, OfficeId, power, network && power, Access);
            foreach (Workstation w in _stations)
                foreach (Employee e in _employees)
                    if (!e.Former && e.Desk == w.Desk) w.AssignedTo = e.Id;
            // A desk that was sold or taken away: whoever had it no longer does.
            foreach (Employee e in _employees)
                if (e.Desk != 0 && _stations.Find(w => w.Desk == e.Desk) == null) e.Desk = 0;
        }

        public Workstation StationOf(Employee e)
        {
            if (e.Desk == 0) return null;
            Workstation w = Stations is List<Workstation> list ? list.Find(s => s.Desk == e.Desk) : null;
            return w != null && w.Valid ? w : null;
        }

        /// <summary>Why this person can't work yet (for the dashboard's "Waiting for workstation" status).</summary>
        public string StationProblem(Employee e)
        {
            if (e.Desk == 0) return "No workstation assigned.";
            Workstation w = null;
            foreach (Workstation s in Stations) if (s.Desk == e.Desk) w = s;
            return w == null ? "Their desk is gone." : w.Valid ? "" : w.Summary;
        }

        /// <summary>Assigns a desk (by its item uid). Error, or null. Invalid desks can be assigned: they wait there until fixed.</summary>
        public string Assign(Employee e, int desk)
        {
            if (e.Former) return $"{e.Name} no longer works here.";
            Workstation w = null;
            foreach (Workstation s in Stations) if (s.Desk == desk) w = s;
            if (w == null) return "That desk isn't in the office.";
            // The owner's own desk (the world marks it, see FundWorld.Access): never handed to staff.
            if (w.Problems.Exists(p => p.StartsWith("The principal's desk", StringComparison.Ordinal))) return "That's the principal's desk: yours.";
            foreach (Employee other in _employees)
                if (other != e && !other.Former && other.Desk == desk) return $"Already assigned to {other.Name}.";
            e.Desk = desk;
            w.AssignedTo = e.Id;
            foreach (Workstation s in _stations) if (s.AssignedTo == e.Id && s.Desk != desk) s.AssignedTo = 0;
            // Someone already in the office walks over before starting.
            if (e.Activity != Activity.OffDuty && e.Activity != Activity.AwaitingStart && e.Activity != Activity.Commuting)
                e.SettleUntil = _clock.AddMinutes(3).Ticks;
            e.Person.Note(_market.Now, "Assigned a workstation");
            Touch();
            return null;
        }

        /// <summary>The next valid unassigned station for someone (auto-assign). Error, or null.</summary>
        public string AutoAssign(Employee e)
        {
            foreach (Workstation w in Stations)
                if (w.Valid && w.AssignedTo == 0) return Assign(e, w.Desk);
            foreach (Workstation w in Stations)
                if (w.AssignedTo == 0) return Assign(e, w.Desk) ?? "Assigned, but that desk isn't complete yet: " + w.Summary;
            return "No free desk in the office. Buy and place a desk, chair, computer and monitor.";
        }

        // ------------------------------------------------------------------ money on the calendar

        /// <summary>
        /// Once a month people look at their car against their savings (FUND_SPEC §24): a better one when they can
        /// comfortably afford it, trading the old one in at 60%. What they park in the garage follows their wealth.
        /// </summary>
        private static void CarReview(Employee e, DateTime t)
        {
            Person p = e.Person;
            string want = VehicleLadder.Affordable(p.Savings, p.Traits[(int)Trait.Flash]);
            string had = p.Car ?? "";
            decimal price = VehicleLadder.PriceOf(want), old = VehicleLadder.PriceOf(had);
            if (want.Length == 0 || price <= old) return;
            decimal cost = price - old * 0.6m;
            if (cost > p.Savings) return;
            p.Savings -= cost;
            p.Note(t, had.Length > 0 ? $"Traded the {had} for a {want}." : $"Bought a {want}.");
            p.Car = want;
        }

        private void MonthlyBills(DateTime t)
        {
            string month = t.ToString("MMMM", C);
            if (Tenure == OfficeTenure.Leased) Pay(t, CashKind.Rent, ExpenseKind.Rent, Config.OfficeMonthlyRent, $"{OfficeName}: {month} rent");
            Pay(t, CashKind.Utilities, ExpenseKind.Utilities, Config.OfficeUtilities, $"{month} utilities");
            string net = Pay(t, CashKind.Connectivity, ExpenseKind.Connectivity, Config.OfficeConnectivity, $"{month} fibre and data room");
            if (net != null)
            {
                _connectivityOverdue = true;
                NetworkUp = false;
                Notify(NoticeLevel.Urgent, "Network cut off", "The connectivity bill went unpaid, so the office is offline: nobody can trade until it's paid.");
            }
            int seats = 0;
            foreach (Employee e in Staff) if (e.Activity != Activity.AwaitingStart && !e.IsAdmin) seats++; // support staff need no terminal
            if (seats > 0) Pay(t, CashKind.MarketData, ExpenseKind.MarketData, Config.MarketDataPerSeat * seats, $"{month} market data ({seats} seat{(seats == 1 ? "" : "s")})");
        }

        /// <summary>Weekly payroll: overdue first, then this week's wages and commissions. What can't be paid stays owed.</summary>
        private void Payroll(DateTime t)
        {
            BeforePayroll(t);
            decimal paid = 0m, short_ = 0m;
            foreach (Employee e in _employees)
            {
                if (e.WagesAccrued + e.CommissionAccrued + e.WagesOverdue + e.CommissionOverdue <= 0m) continue;
                (decimal p, decimal s) = PayEmployee(e, t, final: false);
                paid += p;
                short_ += s;
            }
            if (paid > 0m || short_ > 0m)
                Notify(short_ > 0m ? NoticeLevel.Urgent : NoticeLevel.Routine, short_ > 0m ? "Payroll short" : "Payroll paid",
                    short_ > 0m ? $"Paid {Money(paid)}; {Money(short_)} couldn't be paid and is now overdue. Staff morale will fall every day it stays unpaid."
                        : $"Paid {Money(paid)} in wages and commissions.", 0, short_ > 0m ? "" : "payroll");
            Touch();
        }

        /// <summary>
        /// Pays one person what they're owed (overdue first, then this period's unless <paramref name="includeAccrued"/>
        /// is off), in whole cents: the fraction of a cent that per-minute wages leave stays accrued for next time, so
        /// nothing is lost or invented. Returns (paid, still owed).
        /// </summary>
        private (decimal Paid, decimal Short) PayEmployee(Employee e, DateTime t, bool final, bool includeAccrued = true)
        {
            decimal paid = 0m;
            decimal PayPart(decimal amount, CashKind kind, string memo)
            {
                if (amount <= 0m) return 0m;
                decimal now = Math.Floor(Math.Min(amount, Math.Max(0m, Ledger.Cash)) * 100m) / 100m;
                if (now > 0m) Ledger.Post(t, kind, -now, memo, e.Id);
                paid += now;
                return amount - now;
            }
            string who = e.Name + (final ? " (final pay)" : "");
            decimal wTotal = e.WagesOverdue + (includeAccrued ? e.WagesAccrued : 0m);
            decimal cTotal = e.CommissionOverdue + (includeAccrued ? e.CommissionAccrued : 0m);
            decimal wDue = Math.Floor(wTotal * 100m) / 100m, cDue = Math.Floor(cTotal * 100m) / 100m;
            if (final && includeAccrued)
            {
                // Last pay rounds the sub-cent remainder up to a whole cent, recognised as wages so the books still tie.
                decimal wUp = Math.Ceiling(wTotal * 100m) / 100m, cUp = Math.Ceiling(cTotal * 100m) / 100m;
                Ledger.Recognise(t, ExpenseKind.Wages, wUp - wTotal + cUp - cTotal, "Final pay rounding", e.Id);
                wTotal = wDue = wUp;
                cTotal = cDue = cUp;
            }
            decimal wLeft = PayPart(wDue, CashKind.Wages, "Wages: " + who);
            decimal cLeft = PayPart(cDue, CashKind.Commission, "Commission: " + who);
            e.TotalWagesPaid += wDue - wLeft;
            e.TotalCommissionPaid += cDue - cLeft;
            if (includeAccrued)
            {
                // Sub-cent remainders carry to the next payday.
                e.WagesAccrued = wTotal - wDue;
                e.CommissionAccrued = cTotal - cDue;
            }
            e.WagesOverdue = wLeft;
            e.CommissionOverdue = cLeft;
            if (paid > 0m) e.Person.Savings += paid * 0.35m; // what's left after their living costs and taxes
            return (paid, wLeft + cLeft);
        }

        /// <summary>Pays overdue wages and bills as soon as there's cash (contributions, collections, recalls).</summary>
        private void SettleOverdue(DateTime t)
        {
            foreach (Employee e in _employees)
                if (e.WagesOverdue > 0m || e.CommissionOverdue > 0m)
                {
                    decimal w = e.WagesOverdue;
                    PayEmployee(e, t, final: false, includeAccrued: false);
                    if (e.WagesOverdue < w && !e.Former) e.Feel(t, "Back pay received", 2);
                }
            if (BillsOverdue > 0m && Ledger.Cash > 0m)
            {
                decimal pay = Math.Min(BillsOverdue, Ledger.Cash);
                Ledger.Post(t, CashKind.Utilities, -pay, "Overdue bills settled");
                BillsOverdue -= pay;
                if (BillsOverdue <= 0m && _connectivityOverdue)
                {
                    _connectivityOverdue = false;
                    NetworkUp = true;
                    Notify(NoticeLevel.Important, "Network restored", "The overdue bills are paid and the office is back online.");
                }
            }
        }

        // ------------------------------------------------------------------ end of day: mood, reputation, growth

        private void EndDay(DateTime t)
        {
            bool anyOverdue = BillsOverdue > 0m;
            foreach (Employee e in _employees)
            {
                if (e.Former || e.Plan == null || !e.Plan.Workday) continue;
                if (e.WagesOverdue > 0m) anyOverdue = true;
                DailyMood(e, t);
                Mentoring(e, t);
                Resignations(e, t);
            }
            UpdateReputation(t, anyOverdue);
            RecordDay(t);
            Touch();
        }

        private void RecordDay(DateTime t)
        {
            decimal trading = TradingNet;
            int staff = 0, trades = 0;
            foreach (Employee e in Staff)
            {
                staff++;
                if (e.Today != null) trades += e.Today.Trades;
            }
            _history.Add(new CompanyDay
            {
                Date = t.Date.Ticks, Equity = S(Equity), Cash = S(Ledger.Cash), Trading = S(trading - _tradingAtLastClose),
                Expenses = S(Ledger.ExpensesBetween(t.Date, t.Date.AddDays(1))), Allocated = S(Allocated), Staff = staff, Trades = trades,
            });
            _tradingAtLastClose = trading;
            if (_history.Count > 500) _history.RemoveAt(0);
        }

        private void DailyMood(Employee e, DateTime t)
        {
            Person p = e.Person;
            // Pay against what the market would pay them now.
            // Support staff are paid the going hourly rate for their job (traders against their trading market value).
            decimal fair = e.IsAdmin ? AdminHourly(e.Role) : Negotiation.Value(p, Negotiation.Asking(p, Reputation));
            decimal mine = e.IsAdmin ? e.Contract.Hourly : Negotiation.Value(p, e.Contract);
            double ratio = fair <= 0m ? 1 : (double)(mine / fair);
            double ambition = p.Trait(Trait.Ambition) / 100.0;
            if (!e.IsAdmin && Worked(Role.Receptionist, t)) e.Feel(t, "Friendly front desk", 0.5);
            if (ratio < 0.9) e.Feel(t, "Pay below market", -(0.9 - ratio) * 12 * (0.5 + ambition));
            else if (ratio > 1.1) e.Feel(t, "Well paid", Math.Min(1.2, (ratio - 1.1) * 6));

            Workstation w = StationOf(e);
            if (w != null) e.Feel(t, w.Quality >= 0.6 ? "Good workstation" : "Basic workstation", (w.Quality - 0.45) * 1.6);

            int waited = e.Today?.MinutesWaiting ?? 0;
            if (waited > 0)
            {
                // The first couple of days waiting is understandable; after that it grates.
                int daysHere = 0;
                foreach (WorkDay d in e.Days) if (d.MinutesWaiting > 0) daysHere++;
                double hurt = waited / 60.0 * (daysHere <= 2 ? 0.5 : 1.3);
                e.Feel(t, "Waiting for a workstation", -hurt);
            }

            // Amenities in the office.
            int amen = 0;
            foreach (OwnedItem i in _belongings.Items)
            {
                if (i.State != ItemState.Placed || i.Property != OfficeId) continue;
                if (i.ItemId == "coffee_station" || i.ItemId == "water_cooler") amen += 2;
                else if (i.ItemId == "sofa_mid" || i.ItemId == "sofa_premium" || i.ItemId == "armchair" || i.ItemId == "plant") amen++;
            }
            if (amen > 0) e.Feel(t, "Office amenities", Math.Min(0.8, amen * 0.12));
            else e.Feel(t, "No coffee or break area", -0.4);

            bool trained = false;
            foreach (TrainingJob j in e.Training) if (j.Done && (t - new DateTime(j.Finished)).TotalDays < 30) trained = true;
            if (trained) e.Feel(t, "Recent training", 0.4);
            else if (ambition > 0.6 && (t - new DateTime(e.HiredOn)).TotalDays > 45) e.Feel(t, "No development", -0.3);

            e.Feel(t, "Firm's reputation", (Reputation - 40) / 100.0);
            if (e.Stress > 0.6) e.Feel(t, "Stressful run of results", -(e.Stress - 0.6) * 4);
            // Moods drift back toward the middle.
            e.Satisfaction += (65 - e.Satisfaction) * 0.02;
        }

        private void Mentoring(Employee e, DateTime t)
        {
            if (e.MentorOf == 0) return;
            Employee mentor = Find(e.MentorOf);
            if (mentor == null || mentor.Former || mentor.Plan == null || !mentor.Plan.Workday) return;
            double gain = 0.15 * mentor.Person[Skill.Leadership] / 100.0;
            foreach (Skill s in new[] { Skill.Analysis, Skill.SelfControl })
            {
                int i = (int)s;
                e.Person.Skills[i] = Math.Min(e.Person.Caps[i], e.Person.Skills[i] + gain);
            }
        }

        /// <summary>How many juniors someone can mentor.</summary>
        public int MentorSlots(Employee senior) => TrainingCatalog.LevelsTaken(senior, Skill.Leadership) + (senior.Person.Years >= 8 ? 1 : 0);

        public string SetMentor(Employee junior, Employee senior)
        {
            if (senior == null) { junior.MentorOf = 0; Touch(); return null; }
            if (senior == junior) return "Nobody mentors themselves.";
            int used = 0;
            foreach (Employee e in Staff) if (e.MentorOf == senior.Id) used++;
            if (used >= MentorSlots(senior)) return $"{senior.Name} can mentor {MentorSlots(senior)} (Leadership training adds more).";
            if (senior.Person.Years <= junior.Person.Years) return "Mentors need more experience than their mentee.";
            junior.MentorOf = senior.Id;
            Touch();
            return null;
        }

        private void Resignations(Employee e, DateTime t)
        {
            Person p = e.Person;
            // A raise request first: ambitious people who feel underpaid ask before they look elsewhere.
            decimal fair = Negotiation.Value(p, Negotiation.Asking(p, Reputation));
            double ratio = fair <= 0m ? 1 : (double)(Negotiation.Value(p, e.Contract) / fair);
            if (!e.RaiseRequested && ratio < 0.92 && p.Trait(Trait.Ambition) > 55 && e.Satisfaction < 62)
            {
                e.RaiseRequested = true;
                Notify(NoticeLevel.Important, "Raise requested", $"{e.Name} asked for a pay review: they feel paid about {(1 - ratio) * 100:0}% below market.", e.Id);
            }

            if (e.ResignationNotice > 0)
            {
                // Things got better: they stay.
                if (e.Satisfaction >= 50)
                {
                    e.ResignationNotice = 0;
                    Notify(NoticeLevel.Important, "Resignation withdrawn", $"{e.Name} has decided to stay.", e.Id);
                    return;
                }
                if (--e.ResignationNotice <= 0) Resign(e, t, WorstReason(e));
                return;
            }
            double threshold = 32 - 10 * p.Trait(Trait.Loyalty) / 100.0;
            if (e.Satisfaction < threshold)
            {
                e.ResignationNotice = 5;
                Notify(NoticeLevel.Urgent, "Resignation warning", $"{e.Name} has given notice: five business days unless something changes. Main complaint: {WorstReason(e).ToLowerInvariant()}.", e.Id);
            }
        }

        public static string WorstReason(Employee e)
        {
            var sums = new Dictionary<string, double>();
            foreach (MoodReason r in e.Mood) sums[r.Factor] = (sums.TryGetValue(r.Factor, out double v) ? v : 0) + r.Delta;
            string worst = "general unhappiness";
            double low = 0;
            foreach (var kv in sums) if (kv.Value < low) { low = kv.Value; worst = kv.Key; }
            return worst;
        }

        /// <summary>
        /// Reputation drifts toward a target built from sustained results (20 days), pay reliability, how staff are
        /// treated and risk discipline, with a 25-day time constant: one lucky day barely moves it.
        /// </summary>
        private void UpdateReputation(DateTime t, bool overdue)
        {
            decimal equity = Equity, capital = Math.Max(1m, Ledger.NetContributions);
            double ret = (double)((equity - capital) / capital);
            double perf = Math.Max(-25, Math.Min(25, ret * 120));
            double mood = 0;
            int n = 0, limits = 0;
            foreach (Employee e in Staff)
            {
                mood += e.Satisfaction;
                n++;
                foreach (WorkDay d in e.Days) if (d.LossLimitHit && (t - new DateTime(d.Date)).TotalDays < 20) limits++;
            }
            double treatment = n == 0 ? 0 : (mood / n - 60) / 2.5;
            double target = 40 + perf + treatment - (overdue ? 25 : 0) - Math.Min(12, limits * 2);
            target = Math.Max(0, Math.Min(100, target));
            Reputation += (target - Reputation) * 0.04;
        }

        public string ReputationTier => Reputation switch
        {
            < 25 => "Unknown",
            < 45 => "Emerging",
            < 65 => "Established",
            < 82 => "Respected",
            _ => "Prestigious",
        };

        // ------------------------------------------------------------------ closing the fund

        /// <summary>Winds the fund up: no new trades, positions closed through the market, then accounts settled.</summary>
        public void WindUp(string reason)
        {
            if (!Exists || WindingUp) return;
            WindingUp = true;
            _windUpReason = reason;
            foreach (Employee e in Staff)
            {
                e.Policy.Authorized = false;
                CloseOut(e, "Fund closing");
            }
            Notify(NoticeLevel.Urgent, $"{Name} is closing", reason + " Positions are being closed; accounts settle once everything is flat.");
            TryFinishWindUp(_market.Now);
        }

        private void TryFinishWindUp(DateTime t)
        {
            foreach (Employee e in _employees) if (!e.IsFlat) return;
            DateTime now = t;
            foreach (Employee e in Staff)
            {
                e.LeftOn = now.Ticks;
                e.LeftReason = "Fund closed";
                PayEmployee(e, now, final: true);
                SetActivity(e, e.Activity == Activity.OffDuty || e.Activity == Activity.AwaitingStart ? Activity.Former : Activity.Leaving, now);
            }
            foreach (Employee e in _employees) RecallAll(e, now);
            if (DepositHeld > 0m) { Ledger.Post(now, CashKind.DepositReturned, DepositHeld, "Lease deposit returned"); DepositHeld = 0m; }
            if (PropertyValue > 0m) { Ledger.Post(now, CashKind.OfficeSale, PropertyValue, $"{OfficeName}: sold back"); PropertyValue = 0m; }
            SettleOverdue(now);
            decimal owed = Liabilities;
            string shortfall = "";
            if (owed > 0m)
            {
                // The owner settles what the company can't: nothing is written off quietly.
                string error = ChargeOwner?.Invoke(owed, $"{Name}: debts settled on closing");
                if (error == null) Ledger.Post(now, CashKind.ClosureShortfall, owed, "Owner settled the remaining debts");
                SettleOverdue(now);
                shortfall = string.Format(C, " Your bank paid {0} of debts the company couldn't.", Money(owed));
            }
            decimal left = Ledger.Cash;
            if (left > 0m)
            {
                Ledger.Post(now, CashKind.OwnerWithdrawal, -left, "Remaining cash returned to the owner");
                PayOwner?.Invoke(left, $"{Name}: remaining cash after closing");
            }
            Tenure = OfficeTenure.None;
            Exists = false;
            WindingUp = false;
            ClosedOn = now;
            ClosedReason = _windUpReason;
            Notify(NoticeLevel.Urgent, $"{Name} closed", _windUpReason + string.Format(C, " {0} came back to you.", Money(Math.Max(0m, left))) + shortfall);
            Touch();
        }
    }
}
