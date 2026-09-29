using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Market;

namespace OpeningBell.Trading
{
    /// <summary>A payout on its way to the bank (firms pay the next weekday).</summary>
    [Serializable]
    public struct PendingPayout
    {
        public string AccountId;
        public string FirmName;
        public decimal Net;
        public DateTime PayOn;
    }

    /// <summary>What a funded account may withdraw right now, or why not.</summary>
    public readonly struct PayoutQuote
    {
        public readonly bool Eligible;
        public readonly decimal Max;
        public readonly string Reason;

        public PayoutQuote(bool eligible, decimal max, string reason)
        {
            Eligible = eligible;
            Max = max;
            Reason = reason;
        }
    }

    /// <summary>
    /// Every prop firm account the player holds, and the firms' rules (PROP_SPEC.md §2): purchase and monthly
    /// billing, drawdown thresholds (EOD or intraday trailing) checked every tick, contract limits, flat by 3:55 PM,
    /// pass/fail, activation, resets and payouts. Money in and out goes through <see cref="Charge"/> and
    /// <see cref="Pay"/> (the bank), which the game wires up.
    /// </summary>
    public sealed partial class PropDesk
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private const int BillingDays = 30;

        private readonly IMarketData _market;
        private readonly BrokerRules _rules;
        private readonly List<PropAccount> _accounts = new List<PropAccount>();
        private readonly List<PendingPayout> _pending = new List<PendingPayout>();
        private long _nextNumber;
        private DateTime _today;

        public IReadOnlyList<PropAccount> Accounts => _accounts;
        public IReadOnlyList<PendingPayout> PendingPayouts => _pending;

        /// <summary>Takes money from the bank: (amount, description) → error, or null when paid.</summary>
        public Func<decimal, string, string> Charge { get; set; }

        /// <summary>Puts money into the bank: (amount, description).</summary>
        public Action<decimal, string> Pay { get; set; }

        /// <summary>Something happened the player should hear about: (account, title, body).</summary>
        public event Action<PropAccount, string, string> Notice;

        /// <summary>Any account was added or changed status.</summary>
        public event Action Changed;

        /// <summary>A fill on any prop account (sounds, notifications).</summary>
        public event Action<PropAccount, Fill> Filled;

        public PropDesk(IMarketData market, BrokerRules rules, ulong seed)
        {
            _market = market;
            _rules = rules;
            // Account numbers look like a firm's: seven digits, different per world.
            _nextNumber = 2_015_151 + (long)(seed % 700_000UL);
            _today = market.Now.Date;
            _market.Ticked += OnTick;
            _market.SessionChanged += OnSessionChanged;
        }

        public PropAccount Find(string id) => _accounts.Find(a => a.Id == id);

        public int FundedCount(PropFirm firm) =>
            _accounts.FindAll(a => a.Firm == firm && a.Phase == PropPhase.Funded && a.Status == PropStatus.Active).Count;

        // ---------------------------------------------------------------- buying and account lifecycle

        /// <summary>Buys an evaluation (first month charged now). Error, or null.</summary>
        public string Buy(PropFirm firm, int size, out PropAccount account)
        {
            account = null;
            PropPlan plan = firm.Plan(size);
            if (plan == null) return $"{firm.Name} has no {size / 1000}K plan.";
            string error = TryCharge(plan.MonthlyPrice, $"{firm.Name} {plan.Label} {firm.EvaluationName}");
            if (error != null) return error;

            account = Open(firm, plan, PropPhase.Evaluation);
            account.Subscribed = true;
            account.NextBilling = _market.Now.Date.AddDays(BillingDays);
            Changed?.Invoke();
            return null;
        }

        /// <summary>Turns a passed evaluation into a funded account (activation fee). Error, or null.</summary>
        public string Activate(PropAccount passed, out PropAccount funded)
        {
            funded = null;
            if (passed.Phase != PropPhase.Evaluation || passed.Status != PropStatus.Passed)
                return "Only a passed evaluation can be activated.";
            if (passed.ActivatedAs.Length > 0) return $"Already activated as {passed.ActivatedAs}.";
            if (FundedCount(passed.Firm) >= passed.Firm.MaxFundedAccounts)
                return $"{passed.Firm.Name} allows {passed.Firm.MaxFundedAccounts} funded accounts at a time.";
            string error = TryCharge(passed.Firm.ActivationFee, $"{passed.Firm.Name} {passed.Firm.FundedName} activation");
            if (error != null) return error;

            funded = Open(passed.Firm, passed.Plan, PropPhase.Funded);
            passed.ActivatedAs = funded.Id;
            passed.StatusReason = "Activated as " + funded.Id;
            Notice?.Invoke(funded, $"{passed.Firm.FundedName} account ready",
                $"Your {passed.Firm.Name} {funded.PhaseName} account {funded.Id} is live. Trade it from the terminal's account menu.");
            Changed?.Invoke();
            return null;
        }

        /// <summary>Restarts an evaluation from its starting balance for the reset fee. Error, or null.</summary>
        public string Reset(PropAccount account)
        {
            if (account.Phase != PropPhase.Evaluation) return "Funded accounts can't be reset.";
            if (account.Status == PropStatus.Passed || account.Status == PropStatus.Closed) return "This evaluation can't be reset.";
            string error = TryCharge(account.Firm.ResetFee, $"{account.Firm.Name} reset {account.Id}");
            if (error != null) return error;

            account.Orders.LiquidateAll("Account reset.");
            Fund(account);
            account.DayList.Clear();
            account.Status = PropStatus.Active;
            account.StatusReason = "Reset";
            Changed?.Invoke();
            return null;
        }

        /// <summary>Stops the monthly billing; the evaluation stays open until the paid month runs out.</summary>
        public void CancelSubscription(PropAccount account)
        {
            if (!account.Subscribed) return;
            account.Subscribed = false;
            if (account.Status == PropStatus.Failed) Close(account, "Subscription cancelled.");
            Changed?.Invoke();
        }

        private PropAccount Open(PropFirm firm, PropPlan plan, PropPhase phase)
        {
            var a = new PropAccount(firm, plan, phase) { Number = _nextNumber++, Opened = _market.Now };
            a.Id = firm.AccountId(phase, plan, a.Number);
            Fund(a);
            _accounts.Add(a);
            return a;
        }

        /// <summary>A fresh Account/OrderManager wired to the firm's rules (empty: funding or a save fills it).</summary>
        private void Wire(PropAccount a)
        {
            a.Account = new Account(_market);
            a.Orders = new OrderManager(_market, a.Account, _rules) { Gate = order => GateOrder(a, order) };
            a.Orders.OrderFilled += fill =>
            {
                a.TradedToday = true;
                a.LastTradingDate = _market.Now.Date;
                Filled?.Invoke(a, fill);
            };
        }

        /// <summary>A fresh account at the plan's size.</summary>
        private void Fund(PropAccount a)
        {
            Wire(a);
            a.Account.Deposit(a.StartBalance, $"{a.Firm.Name} {a.Plan.Label} starting balance");
            a.HighWater = a.StartBalance;
            a.Locked = false;
            a.DayStartBalance = a.StartBalance;
            a.TradedToday = false;
            a.FlatForToday = false;
            a.LastTradingDate = _market.Now.Date;
        }

        // ---------------------------------------------------------------- rules

        /// <summary>The firm's order checks on top of the broker's.</summary>
        private string GateOrder(PropAccount a, Order order)
        {
            if (a.Status == PropStatus.Passed) return $"{a.Id} passed. Activate your {a.Firm.FundedName} account on {a.Firm.Host}.";
            if (a.Status != PropStatus.Active) return $"{a.Id} is {a.Status.ToString().ToLowerInvariant()}: {a.StatusReason}";
            if (a.FlatForToday) return $"{a.Firm.Name} requires you to be flat by 3:55 PM. New orders open next session.";

            long opening = a.Orders.OpeningQuantity(order.Ticker, order.Side, order.Quantity);
            if (opening == 0) return null;
            long committed = a.OpenContracts;
            foreach (Order o in a.Orders.OpenOrders)
                if (o.OcoGroup == 0) committed += a.Orders.OpeningQuantity(o.Ticker, o.Side, o.RemainingQuantity);
            int limit = a.ContractLimit;
            if (committed + opening > limit)
                return $"{a.Firm.Name} {a.Plan.Label} allows {limit} contract{(limit == 1 ? "" : "s")} at a time" +
                       (a.Phase == PropPhase.Funded && a.Plan.Scaling.Length > 0 ? " at your scaling level" : "") +
                       $"; this would make {committed + opening}.";
            return null;
        }

        private void OnTick()
        {
            NewDayIfNeeded();
            bool flatTime = _market.Session == MarketSession.Regular && _market.Now.TimeOfDay >= PropFirms.Ridgeback.FlatBy;
            foreach (PropAccount a in _accounts)
            {
                if (a.Status != PropStatus.Active) continue;
                decimal equity = a.Equity;
                if (a.Firm.FundedDrawdown == DrawdownMode.Intraday && a.Phase == PropPhase.Funded && equity > a.HighWater)
                    a.HighWater = equity;
                if (equity <= a.Threshold)
                {
                    Fail(a, string.Format(C, "Drawdown limit hit: equity ${0:N2} reached the ${1:N2} threshold.", equity, a.Threshold));
                    continue;
                }
                if (flatTime && !a.FlatForToday && _market.Now.TimeOfDay >= a.Firm.FlatBy)
                {
                    a.FlatForToday = true;
                    if (!a.IsFlat) a.Orders.LiquidateAll($"{a.Firm.Name}: flat by 3:55 PM.");
                }
            }
        }

        private void OnSessionChanged(MarketSession previous, MarketSession current)
        {
            NewDayIfNeeded();
            if (previous == MarketSession.Closed && current != MarketSession.Closed) StartDay();
            if (previous == MarketSession.Regular && current != MarketSession.Regular) EndDay();
        }

        private void StartDay()
        {
            foreach (PropAccount a in _accounts)
            {
                a.DayStartBalance = a.Balance;
                a.TradedToday = false;
                a.FlatForToday = false;
                a.WithdrawnToday = 0m;
            }
        }

        /// <summary>
        /// The regular close: record the day, move EOD high-water marks, check the threshold on the settled balance,
        /// and pass evaluations that met every rule.
        /// </summary>
        private void EndDay()
        {
            DateTime date = _market.Now.Date;
            foreach (PropAccount a in _accounts)
            {
                if (a.Status != PropStatus.Active) continue;
                if (!a.IsFlat) a.Orders.LiquidateAll("Closed at the 4:00 PM close.");
                decimal pnl = a.Balance - a.DayStartBalance + a.WithdrawnToday;
                if (a.TradedToday) a.DayList.Add(new PropDay { Date = date, PnL = pnl, Traded = true });

                bool eodTrail = a.Phase == PropPhase.Evaluation || a.Firm.FundedDrawdown == DrawdownMode.EndOfDay;
                if (eodTrail && a.Balance > a.HighWater) a.HighWater = a.Balance;
                if (a.Balance <= a.Threshold)
                {
                    Fail(a, string.Format(C, "Drawdown limit hit at the close: balance ${0:N2}.", a.Balance));
                    continue;
                }

                if (a.Phase == PropPhase.Evaluation && a.Profit >= a.Plan.ProfitTarget && a.TradingDays >= a.Firm.MinTradingDays &&
                    a.ConsistencyMet)
                {
                    a.Status = PropStatus.Passed;
                    a.Subscribed = false;
                    a.StatusReason = "Passed " + date.ToString("MMM d", C);
                    Notice?.Invoke(a, $"You passed the {a.Firm.EvaluationName}!",
                        string.Format(C, "{0} hit the ${1:N0} target in {2} trading days. Activate your {3} account on {4} (${5:N0} activation).",
                            a.Id, a.Plan.ProfitTarget, a.TradingDays, a.Firm.FundedName, a.Firm.Host, a.Firm.ActivationFee));
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>Once per calendar day: payouts land, subscriptions renew, idle funded accounts close.</summary>
        private void NewDayIfNeeded()
        {
            DateTime today = _market.Now.Date;
            if (today == _today) return;
            _today = today;

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].PayOn > today) continue;
                PendingPayout p = _pending[i];
                _pending.RemoveAt(i);
                Pay?.Invoke(p.Net, $"Payout · {p.FirmName} {p.AccountId}");
            }

            foreach (PropAccount a in _accounts)
            {
                if (a.Phase == PropPhase.Evaluation && (a.Status == PropStatus.Active || a.Status == PropStatus.Failed) && today >= a.NextBilling)
                {
                    if (!a.Subscribed)
                    {
                        Close(a, "Subscription ended.");
                        continue;
                    }
                    string error = TryCharge(a.Plan.MonthlyPrice, $"{a.Firm.Name} {a.Plan.Label} {a.Firm.EvaluationName} renewal");
                    if (error != null) Close(a, "Renewal failed: " + error);
                    else a.NextBilling = a.NextBilling.AddDays(BillingDays);
                }
                if (a.Phase == PropPhase.Funded && a.Status == PropStatus.Active && a.Firm.InactivityDays > 0 &&
                    (today - a.LastTradingDate).TotalDays > a.Firm.InactivityDays)
                    Close(a, $"No trading for {a.Firm.InactivityDays} days.");
            }
        }

        private void Fail(PropAccount a, string reason)
        {
            a.Status = PropStatus.Failed;
            a.StatusReason = reason;
            a.Orders.LiquidateAll(reason);
            bool funded = a.Phase == PropPhase.Funded;
            Notice?.Invoke(a, funded ? $"{a.Firm.FundedName} account closed" : $"{a.Firm.EvaluationName} failed",
                reason + (funded ? " The account is closed." : $" Reset it on {a.Firm.Host} (${a.Firm.ResetFee:N0}) to start over."));
            Changed?.Invoke();
        }

        private void Close(PropAccount a, string reason)
        {
            if (!a.IsFlat) a.Orders.LiquidateAll(reason);
            a.Status = PropStatus.Closed;
            a.StatusReason = reason;
            a.Subscribed = false;
            Notice?.Invoke(a, $"{a.Id} closed", reason);
            Changed?.Invoke();
        }

        private string TryCharge(decimal amount, string description) =>
            Charge == null ? "Payments aren't available." : Charge(amount, description);

        // ---------------------------------------------------------------- payouts

        public PayoutQuote PayoutAvailable(PropAccount a)
        {
            if (a.Phase != PropPhase.Funded) return new PayoutQuote(false, 0m, $"Payouts start once you're {a.Firm.FundedName}.");
            if (a.Status != PropStatus.Active) return new PayoutQuote(false, 0m, "This account is not active.");

            decimal max;
            if (a.Firm.Payouts == PayoutStyle.WinningDays)
            {
                int days = a.WinningDaysSincePayout;
                if (days < a.Firm.WinningDaysForPayout)
                    return new PayoutQuote(false, 0m, string.Format(C, "{0} of {1} winning days (${2:N0}+) since your last payout.",
                        days, a.Firm.WinningDaysForPayout, a.Firm.WinningDayMinimum));
                if (a.Profit <= 0m) return new PayoutQuote(false, 0m, "No profit to withdraw.");
                max = a.Profit * a.Firm.PayoutMaxShare;
                if (a.Firm.PayoutCap > 0m) max = Math.Min(max, a.Firm.PayoutCap);
            }
            else
            {
                decimal buffer = a.StartBalance + a.Plan.Drawdown;
                if (a.Balance <= buffer)
                    return new PayoutQuote(false, 0m, string.Format(C, "Build the buffer first: balance ${0:N2} of ${1:N2}.", a.Balance, buffer));
                max = a.Balance - buffer;
            }
            max = Math.Floor(max * 100m) / 100m;
            if (!a.IsFlat) return new PayoutQuote(false, max, "Close your positions and orders before requesting a payout.");
            return new PayoutQuote(true, max, null);
        }

        /// <summary>
        /// Withdraws <paramref name="amount"/> from the account now; the trader's share (less any fee) reaches the
        /// bank the next weekday. Error, or null.
        /// </summary>
        public string RequestPayout(PropAccount a, decimal amount)
        {
            PayoutQuote quote = PayoutAvailable(a);
            if (!quote.Eligible) return quote.Reason;
            if (amount <= 0m) return "Enter an amount.";
            if (amount > quote.Max) return string.Format(C, "The most you can request is ${0:N2}.", quote.Max);
            decimal fee = a.Firm.SmallPayoutFee > 0m && amount <= a.Firm.SmallPayoutThreshold ? a.Firm.SmallPayoutFee : 0m;
            decimal net = Math.Round(amount * a.Firm.ProfitSplit, 2) - fee;
            if (net <= 0m) return "After the split and fee there'd be nothing left.";

            a.Account.Withdraw(amount, $"Payout request (${net:N2} to you after split)");
            a.WithdrawnToday += amount;
            a.LastPayoutDate = _market.Now.Date;
            a.PayoutCount++;
            a.TotalPaidOut += net;
            if (a.Firm.LockAfterPayout) a.Locked = true;
            _pending.Add(new PendingPayout { AccountId = a.Id, FirmName = a.Firm.Name, Net = net, PayOn = NextWeekday(_market.Now.Date) });
            Notice?.Invoke(a, "Payout approved",
                string.Format(C, "${0:N2} from {1} ({2:0%} split{3}) arrives in your bank on {4:ddd MMM d}.",
                    net, a.Id, a.Firm.ProfitSplit, fee > 0m ? string.Format(C, ", ${0:N0} fee", fee) : "", NextWeekday(_market.Now.Date)));
            Changed?.Invoke();
            return null;
        }

        private static DateTime NextWeekday(DateTime date)
        {
            DateTime d = date.AddDays(1);
            while (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(1);
            return d;
        }
    }
}
