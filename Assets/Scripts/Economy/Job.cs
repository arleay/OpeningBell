using System;

namespace OpeningBell.Economy
{
    /// <summary>What a finished shift paid.</summary>
    public readonly struct ShiftSummary
    {
        public readonly TimeSpan Worked;
        public readonly int Orders, Missed;
        public readonly decimal Wage, Tips;

        public ShiftSummary(TimeSpan worked, int orders, int missed, decimal wage, decimal tips)
        {
            Worked = worked;
            Orders = orders;
            Missed = missed;
            Wage = wage;
            Tips = tips;
        }

        public decimal Total => Wage + Tips;
    }

    /// <summary>
    /// A paid job (PROP_SPEC §5): hired once, then shifts. Pay is an hourly wage by the minute worked, plus tips per
    /// order that shrink the longer the customer waited. Everything is paid into the bank at clock-out.
    /// </summary>
    public sealed class Job
    {
        public string Employer { get; }
        public decimal HourlyRate { get; }

        public bool Hired { get; private set; }
        public bool OnShift => ClockedInAt.HasValue;
        public DateTime? ClockedInAt { get; private set; }
        public int ShiftOrders { get; private set; }
        public int ShiftMissed { get; private set; }
        public decimal ShiftTips { get; private set; }

        /// <summary>Lifetime totals (shown to the player; saved).</summary>
        public int TotalShifts { get; private set; }
        public decimal TotalEarned { get; private set; }

        public Job(string employer, decimal hourlyRate)
        {
            Employer = employer;
            HourlyRate = hourlyRate;
        }

        public void Hire() => Hired = true;

        public bool ClockIn(DateTime now)
        {
            if (!Hired || OnShift) return false;
            ClockedInAt = now;
            ShiftOrders = 0;
            ShiftMissed = 0;
            ShiftTips = 0m;
            return true;
        }

        /// <summary>Wage so far this shift: whole minutes worked at the hourly rate.</summary>
        public decimal WageSoFar(DateTime now)
        {
            if (!ClockedInAt.HasValue) return 0m;
            long minutes = Math.Max(0L, (long)(now - ClockedInAt.Value).TotalMinutes);
            return Math.Round(HourlyRate * minutes / 60m, 2);
        }

        /// <summary>
        /// A served order: the tip is a share of the ticket by how quickly it came (under 40% of the customer's
        /// patience: 20%, under 70%: 12%, otherwise 5%), rounded to the quarter like a cash tip. Returns the tip.
        /// </summary>
        public decimal Served(decimal ticket, double waited, double patience)
        {
            double used = patience <= 0 ? 1 : waited / patience;
            decimal share = used < 0.4 ? 0.20m : used < 0.7 ? 0.12m : 0.05m;
            decimal tip = Math.Round(ticket * share * 4m, MidpointRounding.AwayFromZero) / 4m;
            ShiftOrders++;
            ShiftTips += tip;
            return tip;
        }

        /// <summary>A customer who gave up waiting.</summary>
        public void Missed() => ShiftMissed++;

        /// <summary>Ends the shift; the caller pays <see cref="ShiftSummary.Total"/> into the bank.</summary>
        public ShiftSummary ClockOut(DateTime now)
        {
            if (!ClockedInAt.HasValue) return default;
            var summary = new ShiftSummary(now - ClockedInAt.Value, ShiftOrders, ShiftMissed, WageSoFar(now), ShiftTips);
            ClockedInAt = null;
            TotalShifts++;
            TotalEarned += summary.Total;
            return summary;
        }

        public JobSaveData CaptureState() => new JobSaveData
        {
            Hired = Hired, TotalShifts = TotalShifts,
            TotalEarned = TotalEarned.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        public void RestoreState(JobSaveData data)
        {
            Hired = data.Hired;
            TotalShifts = data.TotalShifts;
            TotalEarned = string.IsNullOrEmpty(data.TotalEarned) ? 0m
                : decimal.Parse(data.TotalEarned, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    [Serializable]
    public sealed class JobSaveData
    {
        public bool Hired;
        public int TotalShifts;
        public string TotalEarned;
    }
}
