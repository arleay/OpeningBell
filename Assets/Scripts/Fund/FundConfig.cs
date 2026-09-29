using System;

namespace OpeningBell.Fund
{
    /// <summary>
    /// Every balancing number of the hedge fund in one place (FUND_SPEC). Money is decimal; rates are fractions.
    /// A game creates one with the defaults; tests shrink costs and durations as they need.
    /// </summary>
    public sealed class FundConfig
    {
        // ---- forming the company
        /// <summary>Registration, legal and setup: spent, not kept.</summary>
        public decimal FormationFee = 50_000m;
        /// <summary>Moved from the owner's bank into the company as its first operating cash.</summary>
        public decimal StartingCapital = 100_000m;
        /// <summary>Career milestone: completed trading days (personal or prop) before a fund may be formed.</summary>
        public int RequiredTradingDays = 10;
        /// <summary>Career milestone: proven trading profit (personal realized net of costs, plus prop payouts received).</summary>
        public decimal RequiredProvenProfit = 10_000m;
        /// <summary>After a closure, a new fund can't be registered for this many days.</summary>
        public int ReformCooldownDays = 30;

        // ---- the office floor
        public decimal OfficeMonthlyRent = 9_500m;
        /// <summary>Held by the landlord, returned when the lease ends.</summary>
        public decimal OfficeDeposit = 9_500m;
        public decimal OfficePurchasePrice = 1_650_000m;
        /// <summary>Power, water, cleaning: monthly while the office is held.</summary>
        public decimal OfficeUtilities = 1_400m;
        /// <summary>Fibre and the data room: monthly. Unpaid bills cut the network (no station works).</summary>
        public decimal OfficeConnectivity = 650m;
        /// <summary>Fitting out the east wing of the trading floor (walls down, lights, floor boxes).</summary>
        public decimal ExpansionFitOut = 185_000m;
        /// <summary>Per active trader per month: the market data and platform licence.</summary>
        public decimal MarketDataPerSeat = 325m;

        // ---- people
        /// <summary>Scheduled work day, market-local (arrive, leave), minutes after midnight.</summary>
        public int ArriveMinute = 8 * 60 + 30, LeaveMinute = 16 * 60 + 20;
        /// <summary>One-way commute (door to garage), minutes.</summary>
        public int CommuteMinutes = 28;
        /// <summary>From the parked car (or the bus stop) to the office floor, minutes.</summary>
        public int BuildingMinutes = 8;
        /// <summary>A listing stays up this many days.</summary>
        public int ListingDays = 10;
        /// <summary>An applicant nobody answers withdraws after this many days.</summary>
        public int ApplicantPatienceDays = 12;
        /// <summary>Unreasonable offers an applicant takes before walking away.</summary>
        public int LowballsBeforeWithdraw = 3;

        // ---- money rhythm
        /// <summary>Payroll day and time (after the close).</summary>
        public DayOfWeek PayDay = DayOfWeek.Friday;
        public int PayMinute = 17 * 60;
        /// <summary>Business days of unpaid wages before the company is wound up.</summary>
        public int DefaultDays = 15;
        /// <summary>Warn the owner when operating cash covers fewer business days of costs than this.</summary>
        public decimal WarnRunwayDays = 10m;

        // ---- training
        public decimal FirstTrainingPrice = 50_000m;
        public int FirstTrainingMinutes = 10;
        /// <summary>Each level costs this much more than the last, and takes this much longer.</summary>
        public double TrainingPriceGrowth = 1.55, TrainingTimeGrowth = 1.8;

        // ---- risk defaults for a new hire
        public decimal DefaultAllocation = 25_000m;
        public int DefaultMaxContracts = 4;
        public int DefaultMaxPositions = 2;
        public decimal DefaultRiskPerTrade = 400m;
        public decimal DefaultDailyLoss = 1_500m;
        /// <summary>Company-wide: most contracts across all desks in one symbol, and in one sector.</summary>
        public int CompanyMaxPerSymbol = 30, CompanyMaxPerSector = 50;
    }
}
