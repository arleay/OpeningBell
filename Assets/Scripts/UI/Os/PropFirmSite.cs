using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Trading;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// A prop firm's website (PROP_SPEC §4). host/ shows the plans and the rules, with buying (paid from the bank,
    /// a second click confirms); host/dashboard shows the player's accounts at the firm with their progress and the
    /// actions: activate the funded account, reset, cancel billing, request a payout, trade it in the terminal.
    /// </summary>
    internal sealed class PropFirmSite : BrowserPage
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        private readonly PropFirm _firm;
        private readonly VisualElement _content;
        private readonly Label _status;
        private readonly List<Action> _live = new List<Action>();
        private string _path = "";
        private string _armed = ""; // the buy button waiting for its confirming click
        private string _knownSignature = "";

        public PropFirmSite(BrowserApp browser, PropFirm firm, string tagline) : base(browser, "firm-site")
        {
            _firm = firm;
            VisualElement header = Header(Root, "site-" + firm.Id, GlyphKind.Trading, firm.Name, tagline);
            Ui.Box("spacer", header);
            Ui.Button("PLANS", () => Browser.Navigate(firm.Host), "site-link", header, firm.Id + "-plans");
            Ui.Button("MY ACCOUNTS", () => Browser.Navigate(firm.Host + "/dashboard"), "site-link", header, firm.Id + "-dashboard");
            _status = Ui.Label("firm-status", Root);
            _status.name = firm.Id + "-status";
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("site-body");
            scroll.AddToClassList("app-grow");
            Root.Add(scroll);
            _content = scroll.contentContainer;
        }

        private PropDesk Desk => Context.Game.Prop;

        public override string Show(string path)
        {
            _path = path;
            _armed = "";
            Say("", false);
            Build();
            return path == "dashboard" ? $"My accounts · {_firm.Name}" : $"{_firm.Name} · Get funded";
        }

        private void Build()
        {
            _live.Clear();
            _content.Clear();
            _knownSignature = Signature();
            if (_path == "dashboard") BuildDashboard();
            else BuildPlans();
            Refresh();
        }

        /// <summary>Rebuilds when an account appears or changes status (bought, passed, failed, activated).</summary>
        public override void Refresh()
        {
            if (Signature() != _knownSignature)
            {
                Build();
                return;
            }
            foreach (Action update in _live) update();
        }

        private string Signature()
        {
            var parts = new List<string>();
            foreach (PropAccount a in Desk.Accounts)
                if (a.Firm == _firm) parts.Add(a.Id + a.Status + a.ActivatedAs + a.Subscribed);
            return string.Join("|", parts);
        }

        private void Say(string text, bool error)
        {
            Ui.SetText(_status, text);
            Ui.Show(_status, text.Length > 0);
            _status.EnableInClassList("error", error);
        }

        // ---------------------------------------------------------------- plans

        private void BuildPlans()
        {
            Ui.Label("tp-title", _content, $"Choose your {_firm.EvaluationName}");
            Ui.Label("firm-lede", _content, _firm.Payouts == PayoutStyle.WinningDays
                ? $"Hit the profit target without touching the Max Loss Limit, then trade an {_firm.FundedName} account and keep {_firm.ProfitSplit:0%} of what you make."
                : $"Pass the {_firm.EvaluationName}, go {_firm.FundedName} and withdraw every day once you've built your buffer. You keep {_firm.ProfitSplit:0%}.");

            var grid = Ui.Box("firm-plans", _content);
            foreach (PropPlan plan in _firm.Plans)
            {
                var card = Ui.Box("app-card firm-plan", grid);
                Ui.Label("firm-plan-size", card, "$" + plan.Label);
                Ui.Label("firm-plan-price", card, string.Format(C, "${0:N0}/mo", plan.MonthlyPrice));
                Row(card, "Profit target", string.Format(C, "${0:N0}", plan.ProfitTarget));
                Row(card, _firm.Payouts == PayoutStyle.WinningDays ? "Max Loss Limit (EOD)" : "Drawdown (EOD)", string.Format(C, "${0:N0}", plan.Drawdown));
                Row(card, "Max contracts", plan.MaxContracts.ToString(C));
                Row(card, "Min trading days", _firm.MinTradingDays.ToString(C));
                int size = plan.Size;
                string id = $"buy-{_firm.Id}-{plan.Label.ToLowerInvariant()}";
                Button buy = Ui.Button("GET STARTED", null, "tp-cta firm-buy", card, id);
                buy.clicked += () => Buy(size, id, buy);
            }

            Ui.Label("panel-title tp-section", _content, "THE RULES");
            var rules = Ui.Box("app-card firm-rules", _content);
            foreach (string rule in Rules()) Ui.Label("firm-rule", rules, "•  " + rule);
        }

        private IEnumerable<string> Rules()
        {
            yield return $"{_firm.EvaluationName}: reach the profit target in at least {_firm.MinTradingDays} trading days. No daily loss limit.";
            yield return $"Consistency: your best day can be at most {_firm.Consistency:0%} of your total profit when you pass.";
            yield return "Drawdown trails your highest end-of-day balance and stops at your starting balance. Touch it and the account fails.";
            yield return "Be flat by 3:55 PM. Positions are closed for you and new orders wait for the next session.";
            yield return $"Billed monthly until you pass or cancel. Reset a failed {_firm.EvaluationName} for ${_firm.ResetFee:N0}.";
            if (_firm.Payouts == PayoutStyle.WinningDays)
            {
                yield return $"{_firm.FundedName}: ${_firm.ActivationFee:N0} activation. Contracts scale up with your profit.";
                yield return $"Payouts after {_firm.WinningDaysForPayout} winning days (${_firm.WinningDayMinimum:N0}+) since the last one: up to {_firm.PayoutMaxShare:0%} of your profit, ${_firm.PayoutCap:N0} max per request. You keep {_firm.ProfitSplit:0%}.";
                yield return "After your first payout, the Max Loss Limit locks at your starting balance.";
            }
            else
            {
                yield return $"{_firm.FundedName}: ${_firm.ActivationFee:N0} activation. The drawdown now trails intraday, open profit included.";
                yield return $"Withdraw any day once your balance clears start + drawdown (the buffer). You keep {_firm.ProfitSplit:0%}. ${_firm.SmallPayoutFee:N0} fee on requests of ${_firm.SmallPayoutThreshold:N0} or less.";
                yield return $"Trade at least once every {_firm.InactivityDays} days or the account closes.";
            }
            yield return $"Up to {_firm.MaxFundedAccounts} {_firm.FundedName} accounts at a time. Copy trading across your own accounts is allowed.";
        }

        private void Buy(int size, string id, Button button)
        {
            PropPlan plan = _firm.Plan(size);
            if (_armed != id)
            {
                _armed = id;
                Ui.SetText(button, string.Format(C, "CONFIRM ${0:N0}", plan.MonthlyPrice));
                Say(string.Format(C, "{0} {1}: ${2:N0} now from your bank, then every 30 days until you pass. Click again to confirm.",
                    plan.Label, _firm.EvaluationName, plan.MonthlyPrice), false);
                return;
            }
            _armed = "";
            Ui.SetText(button, "GET STARTED");
            string error = Desk.Buy(_firm, size, out PropAccount account);
            if (error != null)
            {
                Say(error, true);
                return;
            }
            Browser.Navigate(_firm.Host + "/dashboard");
            Say($"Welcome aboard. {account.Id} is ready: pick it from the account menu in your trading terminal.", false);
        }

        // ---------------------------------------------------------------- dashboard

        private void BuildDashboard()
        {
            string name = Context.Game.Look != null && !string.IsNullOrEmpty(Context.Game.Look.Name) ? Context.Game.Look.Name : "Trader";
            Ui.Label("tp-title", _content, $"Welcome back, {name}");
            var mine = Desk.Accounts;
            int shown = 0;
            foreach (PropAccount a in mine)
            {
                if (a.Firm != _firm) continue;
                AccountCard(a);
                shown++;
            }
            if (shown == 0)
            {
                Ui.Label("firm-lede", _content, $"No accounts yet. Pick a {_firm.EvaluationName} to get started.");
                Ui.Button("SEE PLANS", () => Browser.Navigate(_firm.Host), "tp-cta", _content);
            }

            var pending = Desk.PendingPayouts;
            foreach (PendingPayout p in pending)
                if (p.FirmName == _firm.Name)
                    Ui.Label("firm-pending", _content, string.Format(C, "Payout of ${0:N2} from {1} arrives {2:ddd MMM d}.", p.Net, p.AccountId, p.PayOn));
        }

        private void AccountCard(PropAccount a)
        {
            var card = Ui.Box("app-card firm-account", _content);
            card.name = "firm-account-" + a.Id;
            var head = Ui.Box("firm-account-head", card);
            var titles = Ui.Box("", head);
            Ui.Label("firm-account-id", titles, a.Id);
            Ui.Label("muted", titles, string.Format(C, "{0} {1} · opened {2:MMM d}", a.Plan.Label, a.PhaseName, a.Opened));
            Ui.Box("spacer", head);
            Ui.Label("account-row-status " + a.Status.ToString().ToLowerInvariant(), head, a.Status.ToString().ToUpperInvariant());
            if (a.StatusReason.Length > 0) Ui.Label("muted firm-reason", card, a.StatusReason);

            var stats = Ui.Box("firm-stats", card);
            Label balance = Stat(stats, "BALANCE"), threshold = Stat(stats, a.Phase == PropPhase.Funded && a.Firm.FundedDrawdown == DrawdownMode.Intraday ? "INTRADAY THRESHOLD" : "EOD THRESHOLD");
            Label room = Stat(stats, "ROOM TO THRESHOLD"), days = Stat(stats, "TRADING DAYS");
            Label third = Stat(stats, a.Phase == PropPhase.Evaluation ? "PROFIT / TARGET" : a.Firm.Payouts == PayoutStyle.WinningDays ? "WINNING DAYS" : "BUFFER");
            Label fourth = Stat(stats, a.Phase == PropPhase.Evaluation ? "BEST DAY SHARE" : "PAID OUT");

            var bar = Ui.Box("firm-progress", card);
            var fill = Ui.Box("firm-progress-fill", bar);

            _live.Add(() =>
            {
                Ui.SetText(balance, Fmt.Money(a.Balance));
                Ui.SetText(threshold, Fmt.Money(a.Threshold));
                Ui.SetText(room, a.IsTradeable ? Fmt.Money(a.RoomToThreshold) : "—");
                Ui.SetText(days, a.Phase == PropPhase.Evaluation ? $"{a.TradingDays} / {a.Firm.MinTradingDays}" : a.TradingDays.ToString(C));
                decimal progress;
                if (a.Phase == PropPhase.Evaluation)
                {
                    Ui.SetText(third, $"{Fmt.Money(Math.Max(0m, a.Profit))} / {Fmt.Money(a.Plan.ProfitTarget)}");
                    Ui.SetText(fourth, a.Profit > 0m ? $"{a.BestDayShare:0%} (max {a.Firm.Consistency:0%})" : "—");
                    fourth.EnableInClassList("down", a.Profit > 0m && !a.ConsistencyMet);
                    progress = a.Plan.ProfitTarget <= 0m ? 0m : Math.Max(0m, a.Profit) / a.Plan.ProfitTarget;
                }
                else
                {
                    decimal buffer = a.StartBalance + a.Plan.Drawdown;
                    Ui.SetText(third, a.Firm.Payouts == PayoutStyle.WinningDays
                        ? $"{a.WinningDaysSincePayout} / {a.Firm.WinningDaysForPayout}"
                        : $"{Fmt.Money(buffer)}");
                    Ui.SetText(fourth, $"{Fmt.Money(a.TotalPaidOut)} ({a.PayoutCount})");
                    progress = a.Firm.Payouts == PayoutStyle.WinningDays
                        ? (decimal)a.WinningDaysSincePayout / a.Firm.WinningDaysForPayout
                        : a.Balance <= a.StartBalance ? 0m : (a.Balance - a.StartBalance) / a.Plan.Drawdown;
                }
                fill.style.width = Length.Percent((float)Math.Min(1m, Math.Max(0m, progress)) * 100f);
            });

            var actions = Ui.Box("quick-row firm-actions", card);
            string id = a.Id;
            if (a.IsTradeable)
                Ui.Button("TRADE IN TERMINAL", () => TradeIn(a), "tp-cta", actions, "trade-" + id);
            if (a.Status == PropStatus.Passed && a.ActivatedAs.Length == 0)
                Ui.Button(string.Format(C, "ACTIVATE {0} (${1:N0})", _firm.FundedName.ToUpperInvariant(), _firm.ActivationFee),
                    () => Act(Desk.Activate(a, out PropAccount f), f == null ? null : $"{f.Id} is live. Good trading."), "tp-cta", actions, "activate-" + id);
            if (a.Phase == PropPhase.Evaluation && (a.Status == PropStatus.Failed || a.Status == PropStatus.Active))
                Ui.Button(string.Format(C, "RESET (${0:N0})", _firm.ResetFee), () => Act(Desk.Reset(a), $"{id} starts over."), "site-link firm-secondary", actions, "reset-" + id);
            if (a.Phase == PropPhase.Evaluation && a.Subscribed)
                Ui.Button("CANCEL BILLING", () => { Desk.CancelSubscription(a); Act(null, $"Billing stopped for {id}."); }, "site-link firm-secondary", actions, "cancel-" + id);

            if (a.Phase == PropPhase.Funded && a.IsTradeable) PayoutRow(card, a);
        }

        private void PayoutRow(VisualElement card, PropAccount a)
        {
            var row = Ui.Box("firm-payout", card);
            Ui.Label("panel-title", row, "REQUEST PAYOUT");
            var line = Ui.Box("quick-row", row);
            var amount = new TextField { name = "payout-amount-" + a.Id, value = "" };
            amount.AddToClassList("search-field");
            amount.AddToClassList("firm-amount");
            TicketInput.Restrict(amount, ".,$");
            line.Add(amount);
            Ui.Button("MAX", () => amount.value = PayoutMax(a).ToString("0.00", C), "site-link firm-secondary", line, "payout-max-" + a.Id);
            Ui.Button("REQUEST", () =>
            {
                if (!decimal.TryParse(amount.value.Replace("$", "").Replace(",", ""), NumberStyles.Number, C, out decimal value))
                {
                    Say("Enter an amount.", true);
                    return;
                }
                Act(Desk.RequestPayout(a, value), string.Format(C, "Payout requested: {0:0%} of ${1:N2} goes to your bank the next weekday.", a.Firm.ProfitSplit, value));
            }, "tp-cta", line, "payout-request-" + a.Id);
            var note = Ui.Label("muted firm-payout-note", row);
            _live.Add(() =>
            {
                PayoutQuote q = Desk.PayoutAvailable(a);
                Ui.SetText(note, q.Eligible ? string.Format(C, "Up to ${0:N2} available now.", q.Max) : q.Reason);
            });
        }

        private decimal PayoutMax(PropAccount a) => Desk.PayoutAvailable(a).Max;

        private void Act(string error, string success)
        {
            if (error != null) Say(error, true);
            else Say(success ?? "", false);
            Build();
        }

        private void TradeIn(PropAccount a)
        {
            foreach (TradingAccount t in Context.Accounts)
                if (t.Prop == a) Context.SetActive(t);
            Context.ShowApp(TerminalApp.Broker);
        }

        private static void Row(VisualElement parent, string caption, string value)
        {
            var row = Ui.Box("firm-plan-row", parent);
            Ui.Label("muted", row, caption);
            Ui.Box("spacer", row);
            Ui.Label("firm-plan-value", row, value);
        }

        private static Label Stat(VisualElement parent, string caption) => Ui.Stat(caption, parent, "stat tp-stat firm-stat");
    }
}
