using System;
using System.Collections.Generic;

namespace OpeningBell.Trading
{
    public enum PropStatus
    {
        Active,
        /// <summary>Evaluation passed; waiting for the funded account to be activated.</summary>
        Passed,
        /// <summary>Drawdown breached. An evaluation can be reset; a funded account is gone.</summary>
        Failed,
        /// <summary>Subscription ended, cancelled or inactive.</summary>
        Closed,
    }

    /// <summary>One trading day on a prop account: its P&L and whether it traded.</summary>
    [Serializable]
    public struct PropDay
    {
        public DateTime Date;
        public decimal PnL;
        public bool Traded;
    }

    /// <summary>
    /// A prop firm account: an ordinary Account + OrderManager on the live market, plus the firm's bookkeeping
    /// (high-water mark, threshold, trading days, payouts). The rules themselves run in <see cref="PropDesk"/>.
    /// </summary>
    public sealed class PropAccount
    {
        public string Id { get; internal set; }
        public long Number { get; internal set; }
        public PropFirm Firm { get; }
        public PropPlan Plan { get; }
        public PropPhase Phase { get; }
        public PropStatus Status { get; internal set; } = PropStatus.Active;
        public string StatusReason { get; internal set; } = "";

        public Account Account { get; internal set; }
        public OrderManager Orders { get; internal set; }

        public DateTime Opened { get; internal set; }

        /// <summary>Evaluations are billed monthly until passed or cancelled.</summary>
        public bool Subscribed { get; internal set; }
        public DateTime NextBilling { get; internal set; }

        /// <summary>Highest end-of-day balance (EOD trailing) or highest equity at any tick (intraday trailing).</summary>
        public decimal HighWater { get; internal set; }
        /// <summary>Threshold pinned at the starting balance (after a payout on firms that do that).</summary>
        public bool Locked { get; internal set; }

        public decimal DayStartBalance { get; internal set; }
        public bool TradedToday { get; internal set; }
        public bool FlatForToday { get; internal set; }
        public decimal WithdrawnToday { get; internal set; }
        public DateTime LastTradingDate { get; internal set; }

        internal readonly List<PropDay> DayList = new List<PropDay>();
        public IReadOnlyList<PropDay> Days => DayList;

        public DateTime LastPayoutDate { get; internal set; }
        public int PayoutCount { get; internal set; }
        public decimal TotalPaidOut { get; internal set; }

        /// <summary>The funded account a passed evaluation became.</summary>
        public string ActivatedAs { get; internal set; } = "";

        internal PropAccount(PropFirm firm, PropPlan plan, PropPhase phase)
        {
            Firm = firm;
            Plan = plan;
            Phase = phase;
        }

        public decimal StartBalance => Plan.Size;
        public decimal Balance => Account.Cash;
        public decimal Equity => Account.Equity;
        public decimal Profit => Balance - StartBalance;

        /// <summary>Equity at or below this fails the account. Trails up, never above the starting balance.</summary>
        public decimal Threshold => Locked ? StartBalance : Math.Min(StartBalance, HighWater - Plan.Drawdown);

        public decimal RoomToThreshold => Equity - Threshold;

        public bool IsTradeable => Status == PropStatus.Active;
        public string PhaseName => Phase == PropPhase.Funded ? Firm.FundedName : Firm.EvaluationName;

        public int TradingDays
        {
            get
            {
                int n = 0;
                foreach (PropDay d in DayList)
                    if (d.Traded) n++;
                return n;
            }
        }

        public decimal BestDay
        {
            get
            {
                decimal best = 0m;
                foreach (PropDay d in DayList)
                    if (d.PnL > best) best = d.PnL;
                return best;
            }
        }

        /// <summary>Best day as a share of total profit (0.4 = 40%). Zero until there is profit.</summary>
        public decimal BestDayShare => Profit <= 0m ? 0m : BestDay / Profit;

        public bool ConsistencyMet => Profit > 0m && BestDay <= Firm.Consistency * Profit;

        /// <summary>Winning days (traded, P&L at or above the firm's minimum) since the last payout.</summary>
        public int WinningDaysSincePayout
        {
            get
            {
                int n = 0;
                foreach (PropDay d in DayList)
                    if (d.Traded && d.PnL >= Firm.WinningDayMinimum && d.Date > LastPayoutDate) n++;
                return n;
            }
        }

        /// <summary>Contracts allowed today: the plan's maximum, or the funded scaling tier for the day's opening profit.</summary>
        public int ContractLimit
        {
            get
            {
                if (Phase != PropPhase.Funded || Plan.Scaling.Length == 0) return Plan.MaxContracts;
                decimal profit = DayStartBalance - StartBalance;
                int limit = Plan.Scaling[0].contracts;
                foreach (var (reached, contracts) in Plan.Scaling)
                    if (profit >= reached) limit = contracts;
                return limit;
            }
        }

        /// <summary>Contracts held across all symbols.</summary>
        public long OpenContracts
        {
            get
            {
                long n = 0;
                foreach (Position p in Account.Portfolio.Positions)
                    if (p.IsOpen) n += Math.Abs(p.Quantity);
                return n;
            }
        }

        public bool IsFlat => OpenContracts == 0 && Orders.OpenOrders.Count == 0;
    }
}
