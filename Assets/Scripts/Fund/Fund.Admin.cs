using System;

namespace OpeningBell.Fund
{
    /// <summary>
    /// Support staff (FUND_SPEC brief §25): an office manager and a receptionist, hired through an agency on the going
    /// hourly rate, one of each. They don't trade and need no workstation; they work from the reception desk on the same
    /// schedule as everyone else. What they do, only on days they come in:
    /// <list type="bullet">
    /// <item>The office manager checks the cash each morning against what's due by payday (wages owed and still to
    /// accrue, overdue and upcoming bills) and warns you if it won't cover it; at payroll, if the cash is short, they
    /// collect the desks' realized profit first (equity-neutral, as the Collect button).</item>
    /// <item>The receptionist lifts every trader's mood a little each day and gets applications through a quarter
    /// faster.</item>
    /// </list>
    /// </summary>
    public sealed partial class HedgeFund
    {
        /// <summary>The going hourly rate: about $900 and $600 for a 40-hour week.</summary>
        public static decimal AdminHourly(Role role) => role == Role.OfficeManager ? 22.50m : 15.00m;

        public static string RoleName(Role role) => role switch
        {
            Role.OfficeManager => "Office manager",
            Role.Receptionist => "Receptionist",
            _ => "Trader",
        };

        /// <summary>What the job does for the firm, for the hiring card.</summary>
        public static string RoleDuty(Role role) => role switch
        {
            Role.OfficeManager => "Checks the cash every morning against payroll and bills and warns you before it runs short; at payroll, collects the desks' profit first if the cash won't cover it.",
            Role.Receptionist => "Runs the front desk: traders are a little happier every day, and applications come through about a quarter faster.",
            _ => "",
        };

        /// <summary>Their job title: support staff by role, traders by experience.</summary>
        public static string Title(Employee e) => e.IsAdmin ? RoleName(e.Role) : e.Person.Seniority;

        /// <summary>Who holds a support role now (including someone hired who hasn't started), or null.</summary>
        public Employee AdminOf(Role role)
        {
            foreach (Employee e in Staff) if (e.Role == role) return e;
            return null;
        }

        /// <summary>Did someone in this role come in today (arrived by <paramref name="t"/>)?</summary>
        private bool Worked(Role role, DateTime t)
        {
            int m = t.Hour * 60 + t.Minute;
            foreach (Employee e in Staff)
                if (e.Role == role && e.Plan != null && e.Plan.Workday && e.Plan.Date == t.Date.Ticks && m >= e.Plan.Arrive) return true;
            return false;
        }

        /// <summary>
        /// Hires support staff through an agency: someone for the job on the going hourly rate, starting the next
        /// business day. One of each role. An error, or null.
        /// </summary>
        public string HireAdmin(Role role)
        {
            if (!Exists) return "Register the fund first.";
            if (WindingUp) return "The fund is closing.";
            if (role == Role.Trader) return "Traders are hired through job listings.";
            if (AdminOf(role) != null) return $"You already have a{(role == Role.OfficeManager ? "n" : "")} {RoleName(role).ToLowerInvariant()}.";
            DateTime now = _market.Now;
            Person p = MakePerson(0.4);
            var a = new Applicant
            {
                Person = p, AvailableFrom = NextWorkday(now.Date.AddDays(1)).Ticks, Interest = 0.7, Status = ApplicantStatus.Open,
                Asking = Contract.Of(PayStructure.Hourly, AdminHourly(role), 0m),
            };
            p.Note(now, $"Placed by an agency as {RoleName(role).ToLowerInvariant()}");
            _applicants.Add(a);
            Hire(a, a.Asking, now, role);
            Touch();
            return null;
        }

        /// <summary>
        /// Cash needed by the next payday: wages and commission owed now, hourly wages still to accrue (eight hours a
        /// business day), bills overdue, and the month's bills if they fall due before then.
        /// </summary>
        public decimal CashNeededByPayday(DateTime t, out DateTime payday)
        {
            payday = t.Date;
            while (payday.DayOfWeek != Config.PayDay || !_market.Schedule.IsTradingDay(payday)) payday = payday.AddDays(1);
            int days = 0;
            for (DateTime d = t.Date; d <= payday; d = d.AddDays(1)) if (_market.Schedule.IsTradingDay(d)) days++;
            decimal need = BillsOverdue;
            int seats = 0;
            foreach (Employee e in Staff)
            {
                need += e.WagesAccrued + e.WagesOverdue + e.CommissionAccrued + e.CommissionOverdue;
                if (e.Contract.PaysHourly && e.Activity != Activity.AwaitingStart) need += e.Contract.Hourly * 8m * days;
                if (!e.IsAdmin && e.Activity != Activity.AwaitingStart) seats++;
            }
            if (payday.Month != t.Month)
            {
                if (Tenure == OfficeTenure.Leased) need += Config.OfficeMonthlyRent;
                need += Config.OfficeUtilities + Config.OfficeConnectivity + Config.MarketDataPerSeat * seats;
            }
            return need;
        }

        /// <summary>The office manager's morning check: will the cash cover what's due by payday?</summary>
        private void AdminMorning(DateTime t)
        {
            if (!Worked(Role.OfficeManager, t)) return;
            Employee om = AdminOf(Role.OfficeManager);
            decimal need = CashNeededByPayday(t, out DateTime payday);
            if (Ledger.Cash >= need) return;
            Notify(NoticeLevel.Important, "Cash running short",
                $"{om.Name}: we need about {Money(need)} by {payday:dddd} for payroll and bills and have {Money(Ledger.Cash)}. " +
                $"Collect profit ({Money(CollectibleTotal)} available), recall capital or put money in.", om.Id);
        }

        /// <summary>At payroll, if the cash won't cover it, the office manager collects the desks' realized profit first.</summary>
        private void BeforePayroll(DateTime t)
        {
            if (!Worked(Role.OfficeManager, t)) return;
            decimal owed = 0m;
            foreach (Employee e in _employees) owed += e.WagesAccrued + e.CommissionAccrued + e.WagesOverdue + e.CommissionOverdue;
            if (Ledger.Cash >= owed) return;
            decimal got = CollectAll();
            if (got > 0m)
                Notify(NoticeLevel.Routine, "Profit collected for payroll",
                    $"{AdminOf(Role.OfficeManager)?.Name ?? "The office manager"} collected {Money(got)} of realized profit from the desks so payroll could be paid.", 0, "payroll");
        }
    }
}
