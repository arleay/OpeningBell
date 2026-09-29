using System;
using System.Collections.Generic;

namespace OpeningBell.Trading
{
    public enum PropPhase { Evaluation, Funded }

    /// <summary>How the drawdown threshold follows the account up (see PROP_SPEC §2.3).</summary>
    public enum DrawdownMode
    {
        /// <summary>Trails the highest end-of-day balance.</summary>
        EndOfDay,
        /// <summary>Trails the highest equity seen at any tick, open profit included.</summary>
        Intraday,
    }

    /// <summary>When a funded account may take money out.</summary>
    public enum PayoutStyle
    {
        /// <summary>After N winning days since the last payout; up to a share of the profit, capped (Topstep).</summary>
        WinningDays,
        /// <summary>Any day, once the balance clears start + drawdown; only the profit above that (Take Profit Trader).</summary>
        DailyAboveBuffer,
    }

    /// <summary>One account size of a firm.</summary>
    public sealed class PropPlan
    {
        public int Size;
        public decimal MonthlyPrice;
        public decimal ProfitTarget;
        public decimal Drawdown;
        public int MaxContracts;

        /// <summary>
        /// Funded scaling plan: contracts allowed from the profit at the start of the day, as (profit reached,
        /// contracts), ascending. Empty = <see cref="MaxContracts"/> always.
        /// </summary>
        public (decimal profit, int contracts)[] Scaling = Array.Empty<(decimal, int)>();

        public string Label => Size / 1000 + "K";
    }

    /// <summary>
    /// A prop firm's rules. Numbers are copied from real firms (PROP_SPEC.md §2); names are fictional.
    /// </summary>
    public sealed class PropFirm
    {
        public string Id;
        public string Name;
        public string Host;
        public string EvaluationName;
        public string FundedName;

        public IReadOnlyList<PropPlan> Plans;

        // Evaluation
        public int MinTradingDays;
        /// <summary>The best day may be at most this share of total profit to pass (0.5 = 50%).</summary>
        public decimal Consistency;
        public decimal ResetFee;

        // Funded
        public decimal ActivationFee;
        public DrawdownMode FundedDrawdown;
        public PayoutStyle Payouts;
        /// <summary>Trader's share of a payout (0.9 = 90/10).</summary>
        public decimal ProfitSplit;
        public int WinningDaysForPayout;
        public decimal WinningDayMinimum;
        /// <summary>Largest request as a share of the profit balance (0.5), and its cap. 0 = no limit.</summary>
        public decimal PayoutMaxShare;
        public decimal PayoutCap;
        /// <summary>Flat fee on requests at or below <see cref="SmallPayoutThreshold"/>.</summary>
        public decimal SmallPayoutFee;
        public decimal SmallPayoutThreshold;
        /// <summary>After a payout the threshold locks at the starting balance (Topstep sets the MLL to $0).</summary>
        public bool LockAfterPayout;
        /// <summary>A funded account with no trading day for this many calendar days is closed. 0 = never.</summary>
        public int InactivityDays;
        public int MaxFundedAccounts;

        /// <summary>Positions are closed and new orders refused from this time until the next session.</summary>
        public TimeSpan FlatBy = new TimeSpan(15, 55, 0);

        public PropPlan Plan(int size)
        {
            foreach (PropPlan p in Plans)
                if (p.Size == size) return p;
            return null;
        }

        /// <summary>Account IDs as the firm prints them: "profitharborpro2015151", "RB50K-448210".</summary>
        public string AccountId(PropPhase phase, PropPlan plan, long number) => Id switch
        {
            "harbor" => (phase == PropPhase.Funded ? "profitharborpro" : "profitharbortest") + number,
            _ => (phase == PropPhase.Funded ? "RBX" : "RB") + plan.Label + "-" + number % 1_000_000,
        };
    }

    public static class PropFirms
    {
        /// <summary>Topstep model: monthly Trading Challenge, Express Funded, payouts after 5 winning days, 90/10.</summary>
        public static readonly PropFirm Ridgeback = new PropFirm
        {
            Id = "ridgeback",
            Name = "Ridgeback Funding",
            Host = "ridgebackfunding.com",
            EvaluationName = "Trading Challenge",
            FundedName = "Express Funded",
            Plans = new[]
            {
                new PropPlan { Size = 50_000, MonthlyPrice = 49m, ProfitTarget = 3_000m, Drawdown = 2_000m, MaxContracts = 5,
                    Scaling = new[] { (0m, 2), (1_500m, 3), (2_000m, 5) } },
                new PropPlan { Size = 100_000, MonthlyPrice = 99m, ProfitTarget = 6_000m, Drawdown = 3_000m, MaxContracts = 10,
                    Scaling = new[] { (0m, 3), (1_500m, 6), (3_000m, 10) } },
                new PropPlan { Size = 150_000, MonthlyPrice = 149m, ProfitTarget = 9_000m, Drawdown = 4_500m, MaxContracts = 15,
                    Scaling = new[] { (0m, 3), (1_500m, 6), (3_000m, 10), (4_500m, 15) } },
            },
            MinTradingDays = 2,
            Consistency = 0.5m,
            ResetFee = 49m,
            ActivationFee = 149m,
            FundedDrawdown = DrawdownMode.EndOfDay,
            Payouts = PayoutStyle.WinningDays,
            ProfitSplit = 0.9m,
            WinningDaysForPayout = 5,
            WinningDayMinimum = 150m,
            PayoutMaxShare = 0.5m,
            PayoutCap = 5_000m,
            LockAfterPayout = true,
            MaxFundedAccounts = 5,
        };

        /// <summary>Take Profit Trader model: monthly Test, PRO with intraday trailing drawdown and daily payouts, 80/20.</summary>
        public static readonly PropFirm Harbor = new PropFirm
        {
            Id = "harbor",
            Name = "Profit Harbor",
            Host = "profitharbor.com",
            EvaluationName = "Test",
            FundedName = "PRO",
            Plans = new[]
            {
                new PropPlan { Size = 25_000, MonthlyPrice = 150m, ProfitTarget = 1_500m, Drawdown = 1_500m, MaxContracts = 3 },
                new PropPlan { Size = 50_000, MonthlyPrice = 170m, ProfitTarget = 3_000m, Drawdown = 2_000m, MaxContracts = 6 },
                new PropPlan { Size = 75_000, MonthlyPrice = 245m, ProfitTarget = 4_500m, Drawdown = 2_500m, MaxContracts = 9 },
                new PropPlan { Size = 100_000, MonthlyPrice = 330m, ProfitTarget = 6_000m, Drawdown = 3_000m, MaxContracts = 12 },
                new PropPlan { Size = 150_000, MonthlyPrice = 360m, ProfitTarget = 9_000m, Drawdown = 4_500m, MaxContracts = 15 },
            },
            MinTradingDays = 5,
            Consistency = 0.5m,
            ResetFee = 99m,
            ActivationFee = 130m,
            FundedDrawdown = DrawdownMode.Intraday,
            Payouts = PayoutStyle.DailyAboveBuffer,
            ProfitSplit = 0.8m,
            SmallPayoutFee = 50m,
            SmallPayoutThreshold = 250m,
            InactivityDays = 7,
            MaxFundedAccounts = 5,
        };

        public static readonly IReadOnlyList<PropFirm> All = new[] { Ridgeback, Harbor };

        public static PropFirm ById(string id)
        {
            foreach (PropFirm f in All)
                if (f.Id == id) return f;
            return null;
        }
    }
}
