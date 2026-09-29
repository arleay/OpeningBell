using System;
using System.Collections.Generic;
using System.Linq;
using OpeningBell.Fund;
using OpeningBell.Home;
using OpeningBell.Market;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    internal sealed partial class LedgerlineSite
    {
        // ------------------------------------------------------------------ performance

        private void Performance()
        {
            Title("Trading performance", "Results come from real fills in the shared market. Win rates are measured over the labelled sample.");
            var all = Fund.Employees.SelectMany(e => e.Closed()).OrderBy(t => t.Closed).ToList();
            var kpis = Ui.Box("fund-kpis", _page);
            var realized = FundUi.Kpi(kpis, "Net realized, all desks");
            var open = FundUi.Kpi(kpis, "Unrealized now");
            var fees = FundUi.Kpi(kpis, "Execution costs");
            var win = FundUi.Kpi(kpis, "Win rate, last 100 trades");
            _live.Add(() =>
            {
                decimal net = Fund.Employees.Sum(e => e.NetRealized);
                FundUi.SetSigned(realized.Value, net);
                FundUi.SetSigned(open.Value, Fund.Unrealized);
                Ui.SetText(fees.Value, FundUi.Money(Fund.Employees.Sum(e => e.Trades.Sum(t => t.Fees))));
                var last = all.Skip(Math.Max(0, all.Count - 100)).ToList();
                Ui.SetText(win.Value, last.Count == 0 ? "—" : FundUi.Pct(last.Count(t => t.Net > 0m) / (double)last.Count));
                Ui.SetText(win.Sub, $"{last.Count} trades");
            });

            VisualElement daily = FundUi.Card(_page, "Daily trading result (after execution costs), last 40 business days");
            var bars = new BarChart(140f);
            daily.Add(bars);
            bars.Set(Fund.History.Skip(Math.Max(0, Fund.History.Count - 40)).Select(d => (double)d.TradingValue));
            if (Fund.History.Count == 0) Ui.Label("fund-muted", daily, "Filled in at the end of each business day.");

            VisualElement table = FundUi.Card(_page, "By trader (all trades since hire)");
            FundUi.Row(table, "fund-row-head", ("TRADER", 0), ("TRADES", 70), ("WIN RATE", 80), ("AVG WIN", 90), ("AVG LOSS", 90), ("AVG R", 70), ("MAX DD", 90), ("ON PLAN", 80), ("NET", 110));
            foreach (Employee e in Fund.Employees)
            {
                var t = e.Closed();
                if (t.Count == 0 && e.Former) continue;
                var wins = t.Where(x => x.Net > 0m).ToList();
                var losses = t.Where(x => x.Net < 0m).ToList();
                decimal peak = 0m, run = 0m, dd = 0m;
                foreach (TradeRecord x in t) { run += x.Net; peak = Math.Max(peak, run); dd = Math.Max(dd, peak - run); }
                var withR = t.Where(x => x.Risk > 0m).ToList();
                var cells = FundUi.Row(table, e.Former ? "fund-row-former" : "", (e.Name + (e.Former ? " (former)" : ""), 0), (t.Count.ToString(FundUi.C), 70),
                    (t.Count == 0 ? "—" : FundUi.Pct(wins.Count / (double)t.Count), 80), (wins.Count == 0 ? "—" : FundUi.Money0(wins.Average(x => x.Net)), 90),
                    (losses.Count == 0 ? "—" : FundUi.Money0(losses.Average(x => x.Net)), 90), (withR.Count == 0 ? "—" : withR.Average(x => x.R).ToString("+0.00;-0.00", FundUi.C), 70),
                    (FundUi.Money0(dd), 90), (t.Count == 0 ? "—" : FundUi.Pct(t.Count(x => x.FollowedPlan) / (double)t.Count), 80), ("", 110));
                FundUi.SetSigned(cells[8], e.NetRealized);
            }
            Ui.Label("fund-note", table, "A high win rate can still lose money when the average loss is larger than the average win; a low one can profit when winners are bigger.");
            _signature = () => Fund.Employees.Sum(e => e.Trades.Count) + "|" + Fund.History.Count;
        }

        // ------------------------------------------------------------------ capital and risk

        private void Capital()
        {
            Title("Capital and risk", "Traders only ever trade the capital you give them. Hard limits are enforced by the order system; preferences depend on their self-control.");
            var kpis = Ui.Box("fund-kpis", _page);
            var cash = FundUi.Kpi(kpis, "Operating cash");
            var free = FundUi.Kpi(kpis, "Free to allocate");
            var alloc = FundUi.Kpi(kpis, "Allocated");
            var desk = FundUi.Kpi(kpis, "Desk equity");
            _live.Add(() =>
            {
                Ui.SetText(cash.Value, FundUi.Money(Fund.Ledger.Cash));
                Ui.SetText(free.Value, FundUi.Money(Math.Max(0m, Fund.FreeCash)));
                Ui.SetText(alloc.Value, FundUi.Money(Fund.Allocated));
                Ui.SetText(desk.Value, FundUi.Money(Fund.DeskEquity));
            });

            VisualElement allocations = FundUi.Card(_page, "Allocations and limits");
            FundUi.Row(allocations, "fund-row-head", ("TRADER", 0), ("ALLOCATED", 110), ("EQUITY", 110), ("MAX CTS", 70), ("RISK/TRADE", 90), ("DAILY LOSS", 90), ("SET CAPITAL", 230));
            foreach (Employee e in Fund.Staff)
            {
                var cells = FundUi.Row(allocations, "", (e.Name, 0), ("", 110), ("", 110), (e.Policy.MaxContracts.ToString(FundUi.C), 70),
                    (FundUi.Money0(e.Policy.MaxRiskPerTrade), 90), (FundUi.Money0(e.Policy.MaxDailyLoss), 90), ("", 230));
                var set = cells[6];
                var holder = Ui.Box("fund-inline", set.parent);
                holder.style.width = 230;
                set.RemoveFromHierarchy();
                TextField field = FundUi.Field(holder, e.Base.ToString("0", FundUi.C), "capital-" + e.Id, ".,$", 110f);
                Employee emp = e;
                FundUi.Secondary("SET", () =>
                {
                    if (!FundUi.TryMoney(field.value, out decimal v)) { Say("Enter an amount.", true); return; }
                    Act(Fund.Allocate(emp, v), $"{emp.Person.First}'s capital set to {FundUi.Money0(emp.Base)}.");
                }, holder, "capital-set-" + e.Id);
                Label a = cells[1], q = cells[2];
                _live.Add(() => { Ui.SetText(a, FundUi.Money0(emp.Base)); Ui.SetText(q, FundUi.Money0(emp.DeskEquity)); });
            }
            if (!Fund.Staff.Any()) Ui.Label("fund-muted", allocations, "No traders yet.");
            Ui.Label("fund-note", allocations, "Per-trader limits, instruments and strategies are on each employee's Permissions tab.");

            var cols = Ui.Box("fund-cols", _page);
            VisualElement exposure = FundUi.Card(cols, "Company exposure", "fund-grow");
            var list = Ui.Box("", exposure);
            string sig = null;
            _live.Add(() =>
            {
                var rows = Fund.Exposure();
                string s = string.Join(",", rows.Select(r => r.Ticker + r.Net + r.Gross));
                if (s == sig) return;
                sig = s;
                list.Clear();
                FundUi.Row(list, "fund-row-head", ("SYMBOL", 90), ("SECTOR", 130), ("NET", 70), ("GROSS", 70), ("TYPICAL DAY RISK", 0));
                foreach (var r in rows)
                    FundUi.Row(list, "", (r.Ticker, 90), (Person.SectorName(r.Sector), 130), (r.Net.ToString("+0;-0;0", FundUi.C), 70), (r.Gross.ToString(FundUi.C), 70), (FundUi.Money0(r.DayRisk), 0));
                foreach (var g in rows.GroupBy(r => r.Sector))
                {
                    long net = g.Sum(r => r.Net), gross = g.Sum(r => r.Gross);
                    FundUi.Row(list, "fund-row-total", ("Σ " + Person.SectorName(g.Key), 220), (net.ToString("+0;-0;0", FundUi.C), 70), (gross.ToString(FundUi.C), 70),
                        (gross > Fund.Config.CompanyMaxPerSector * 0.8 ? "near the sector limit" : "", 0));
                }
                if (rows.Count == 0) Ui.Label("fund-muted", list, "The company is flat.");
            });
            VisualElement limits = FundUi.Card(cols, "Company limits", "fund-side");
            Ui.Label("fund-label", limits, "Max contracts across all desks, one symbol");
            TextField sym = FundUi.Field(limits, Fund.Config.CompanyMaxPerSymbol.ToString(FundUi.C), "limit-symbol");
            Ui.Label("fund-label", limits, "Max contracts across all desks, one sector");
            TextField sec = FundUi.Field(limits, Fund.Config.CompanyMaxPerSector.ToString(FundUi.C), "limit-sector");
            FundUi.Primary("SAVE", () =>
            {
                if (int.TryParse(sym.value, out int a) && a > 0) Fund.Config.CompanyMaxPerSymbol = a;
                if (int.TryParse(sec.value, out int b) && b > 0) Fund.Config.CompanyMaxPerSector = b;
                Act(null, "Company limits saved.");
            }, limits, "limits-save");
            Ui.Label("fund-note", limits, "Five traders buying related stocks add up: the sector limit stops correlated bets from stacking.");
            _signature = () => string.Join(",", Fund.Staff.Select(e => e.Id));
        }

        // ------------------------------------------------------------------ office

        private void Office()
        {
            Title("Office and workstations", HedgeFund.OfficeName + ": you furnish it. Each trader needs a complete workstation to trade.");
            var cols = Ui.Box("fund-cols", _page);
            VisualElement lease = FundUi.Card(cols, "The floor", "fund-grow");
            FundUi.Line(lease, "Tenure", Fund.Tenure == OfficeTenure.Owned ? "Owned" : $"Leased · {FundUi.Money0(Fund.Config.OfficeMonthlyRent)}/month");
            if (Fund.Tenure == OfficeTenure.Leased) FundUi.Line(lease, "Deposit held", FundUi.Money0(Fund.DepositHeld));
            Label network = FundUi.Line(lease, "Network");
            FundUi.Line(lease, "East wing", Fund.ExpansionOpen ? "Open" : "Partitioned (fit-out available)");
            var actions = Ui.Box("fund-actions", lease);
            if (Fund.Tenure == OfficeTenure.Leased)
                FundUi.Armed($"BUY THE FLOOR ({FundUi.Money0(Fund.Config.OfficePurchasePrice)})", "CONFIRM PURCHASE", () => Act(Fund.BuyOffice(), "The floor is the company's now. No more rent."), actions, "fund-btn-secondary", "office-buy");
            if (!Fund.ExpansionOpen)
                FundUi.Armed($"FIT OUT THE EAST WING ({FundUi.Money0(Fund.Config.ExpansionFitOut)})", "CONFIRM FIT-OUT", () => Act(Fund.OpenExpansion(), "The east wing is open: room for more desks."), actions, "fund-btn-secondary", "office-expand");
            _live.Add(() =>
            {
                Ui.SetText(network, Fund.NetworkUp ? "Online" : "Offline: connectivity bill unpaid");
                network.EnableInClassList("fund-down", !Fund.NetworkUp);
            });

            VisualElement amen = FundUi.Card(cols, "Amenities", "fund-side");
            var placed = Game.Belongings.Items.Where(i => i.State == ItemState.Placed && i.Property == HedgeFund.OfficeId).ToList();
            void Has(string label, params string[] ids) => FundUi.Line(amen, label, placed.Any(i => ids.Contains(i.ItemId)) ? "Yes" : "No");
            Has("Coffee station", "coffee_station");
            Has("Water cooler", "water_cooler");
            Has("Break-area seating", "sofa_budget", "sofa_mid", "sofa_premium", "armchair");
            Has("Waiting-area seating", "waiting_bench", "armchair", "sofa_budget");
            Has("Plants", "plant");
            Ui.Label("fund-note", amen, "Amenities lift morale a little every day; a better chair and screens add comfort and workflow, never guaranteed profit.");

            VisualElement stations = FundUi.Card(_page, "Workstations");
            Ui.Label("fund-muted fund-wrap", stations, "A workstation is a desk with a work chair in front of it, a computer (tower beside it or a laptop on it), at least one monitor switched on, a keyboard and a mouse, the office's power and network, and room to reach the chair. Buy furniture at Timberline Home and tech at Circuit Stop; at the till, [F] bills the company card.");
            var list = Ui.Box("", stations);
            FundUi.Row(list, "fund-row-head", ("DESK", 80), ("STATUS", 0), ("QUALITY", 80), ("ASSIGNED TO", 230));
            var staff = Fund.Staff.ToList();
            foreach (Workstation w in Fund.Stations)
            {
                var cells = FundUi.Row(list, "", ("#" + w.Desk, 80), (w.Valid ? "Ready" : w.Summary, 0), (FundUi.Pct(w.Quality), 80), ("", 230));
                cells[1].EnableInClassList("fund-down", !w.Valid);
                cells[1].EnableInClassList("fund-up", w.Valid);
                var choices = new List<string> { "Unassigned" };
                choices.AddRange(staff.Select(s => s.Name));
                int current = staff.FindIndex(s => s.Desk == w.Desk);
                var drop = new DropdownField(choices, current + 1) { name = "assign-" + w.Desk };
                drop.AddToClassList("fund-drop");
                drop.style.width = 220;
                cells[3].parent.Add(drop);
                cells[3].RemoveFromHierarchy();
                int deskId = w.Desk;
                drop.RegisterValueChangedCallback(ev =>
                {
                    int i = drop.index - 1;
                    if (i < 0)
                    {
                        foreach (Employee s in staff) if (s.Desk == deskId) s.Desk = 0;
                        Act(null, "Desk unassigned.");
                        return;
                    }
                    Act(Fund.Assign(staff[i], deskId), $"{staff[i].Name} assigned to desk #{deskId}.");
                });
            }
            if (!Fund.Stations.Any()) Ui.Label("fund-muted", list, "No desks on Level 26 yet.");
            FundUi.Secondary("AUTO-ASSIGN EVERYONE WITHOUT A DESK", () =>
            {
                string last = null;
                foreach (Employee e in staff) if (e.Desk == 0) last = Fund.AutoAssign(e) ?? last;
                Act(last, "Everyone who could be seated has a desk.");
            }, stations, "office-autoassign");
            _signature = () => string.Join(",", Fund.Stations.Select(w => w.Desk + (w.Valid ? "v" : "x") + w.AssignedTo)) + "|" + Fund.Tenure + Fund.ExpansionOpen + Game.Belongings.Version;
        }

        // ------------------------------------------------------------------ training

        private void TrainingPage()
        {
            Title("Training", "Courses cost company money and in-game time. Each is bought for one employee, who studies at their desk.");
            VisualElement table = FundUi.Card(_page, "Team");
            FundUi.Row(table, "fund-row-head", ("TRADER", 0), ("NOW", 320), ("COURSES DONE", 110), ("", 150));
            foreach (Employee e in Fund.Staff)
            {
                var cells = FundUi.Row(table, "", (e.Name, 0), ("", 320), (e.Training.Count(j => j.Done).ToString(FundUi.C), 110), ("", 150));
                Employee emp = e;
                Label now = cells[1];
                _live.Add(() => Ui.SetText(now, Fund.CurrentTrainingLabel(emp) ?? "—"));
                FundUi.Primary("CHOOSE A COURSE", () => Browser.Navigate(Host + "/employee/" + emp.Id + "/training"), cells[3].parent, "train-" + e.Id);
                cells[3].RemoveFromHierarchy();
            }
            if (!Fund.Staff.Any()) Ui.Label("fund-muted", table, "Hire someone first.");
            VisualElement catalog = FundUi.Card(_page, "Catalog");
            foreach (Course c in TrainingCatalog.Courses)
                FundUi.Line(catalog, $"{Person.SkillName(c.Skill)} (up to level {c.MaxLevel})",
                    $"from {FundUi.Money0(TrainingCatalog.Price(Fund.Config, c.Skill, 1))} · {c.Effect}");
            _signature = () => string.Join(",", Fund.Staff.Select(e => e.Id + ":" + e.Training.Count));
        }

        // ------------------------------------------------------------------ finances

        private string _period = "month";

        private void Finances()
        {
            Title("Finances", "Expenses are counted when they're incurred; paying them settles what's owed, it isn't counted again.");
            var periods = Ui.Box("fund-segment", _page);
            foreach (var (id, label) in new[] { ("today", "Today"), ("week", "Last 7 days"), ("month", "Last 30 days"), ("all", "All time") })
            {
                string p = id;
                Button b = Ui.Button(label, () => { _period = p; Build(); }, "fund-seg", periods, "period-" + id);
                b.EnableInClassList("active", _period == id);
            }
            DateTime from = _period == "today" ? Now.Date : _period == "week" ? Now.Date.AddDays(-6) : _period == "month" ? Now.Date.AddDays(-29) : DateTime.MinValue;
            DateTime to = Now.Date.AddDays(1);

            var cols = Ui.Box("fund-cols", _page);
            VisualElement pl = FundUi.Card(cols, "Profit and loss", "fund-grow");
            decimal gross = 0m, costs = 0m;
            foreach (Employee e in Fund.Employees)
                foreach (WorkDay d in e.Days)
                    if (d.Date >= from.Ticks && d.Date < to.Ticks) { gross += d.Realized; costs += d.Fees; }
            FundUi.SetSigned(FundUi.Line(pl, "Gross trading results (realized)"), gross);
            Label c1 = FundUi.Line(pl, "Trading costs (commissions, fees)", FundUi.Money(-costs));
            c1.AddToClassList("fund-down");
            FundUi.SetSigned(FundUi.Line(pl, "Net trading", "", "fund-line-total"), gross - costs);
            Ui.Box("fund-sep", pl);
            decimal totalExp = 0m;
            foreach (ExpenseKind k in Enum.GetValues(typeof(ExpenseKind)))
            {
                decimal v = Fund.Ledger.ExpensesBetween(from, to, k);
                if (v == 0m) continue;
                totalExp += v;
                string title = k == ExpenseKind.Wages || k == ExpenseKind.Commission ? "Employee compensation: " + CompanyLedger.ExpenseName(k).ToLowerInvariant() : CompanyLedger.ExpenseName(k);
                FundUi.Line(pl, title, FundUi.Money(-v)).AddToClassList("fund-down");
            }
            FundUi.Line(pl, "Operating expenses", FundUi.Money(-totalExp), "fund-line-total");
            Ui.Box("fund-sep", pl);
            FundUi.SetSigned(FundUi.Line(pl, "Net business result", "", "fund-line-total fund-line-big"), gross - costs - totalExp);
            Label unreal = FundUi.Line(pl, "Open positions (unrealized, not in the result)");
            _live.Add(() => FundUi.SetSigned(unreal, Fund.Unrealized));

            VisualElement side = FundUi.Card(cols, "Obligations and runway", "fund-side");
            DateTime payday = Now.Date;
            while (payday.DayOfWeek != Fund.Config.PayDay || payday.AddMinutes(Fund.Config.PayMinute) <= Now) payday = payday.AddDays(1);
            Label wages = FundUi.Line(side, $"Payroll due {payday:ddd MMM d}");
            Label overdue = FundUi.Line(side, "Overdue");
            DateTime first = new DateTime(Now.Year, Now.Month, 1).AddMonths(1);
            decimal bills = Fund.Config.OfficeUtilities + Fund.Config.OfficeConnectivity + (Fund.Tenure == OfficeTenure.Leased ? Fund.Config.OfficeMonthlyRent : 0m)
                            + Fund.Config.MarketDataPerSeat * Fund.Staff.Count();
            FundUi.Line(side, $"Office bills {first:MMM d}", FundUi.Money(bills));
            Label runway = FundUi.Line(side, "Estimated runway", "", "fund-line-total");
            _live.Add(() =>
            {
                decimal due = Fund.Staff.Sum(e => e.WagesAccrued + e.CommissionAccrued);
                Ui.SetText(wages, FundUi.Money(due));
                decimal od = Fund.Liabilities - due;
                Ui.SetText(overdue, FundUi.Money(Math.Max(0m, od)));
                overdue.EnableInClassList("fund-down", od > 0.005m);
                decimal days = RunwayDays();
                Ui.SetText(runway, days >= 999m ? "no running costs" : $"{days:0} business days");
                runway.EnableInClassList("fund-down", days < Fund.Config.WarnRunwayDays);
            });
            Ui.Label("fund-note", side, "Short of cash? Contribute from your bank, collect profit, recall unused capital, pause hiring and training, or sell equipment.");

            VisualElement owner = FundUi.Card(side, "Owner transfers");
            Label bank = FundUi.Line(owner, "Your bank");
            _live.Add(() => Ui.SetText(bank, FundUi.Money(Game.Economy.Bank.Balance)));
            TextField amount = FundUi.Field(owner, "", "owner-amount", ".,$", 160f);
            var buttons = Ui.Box("fund-actions", owner);
            FundUi.Primary("CONTRIBUTE", () =>
            {
                if (!FundUi.TryMoney(amount.value, out decimal v)) { Say("Enter an amount.", true); return; }
                Act(Fund.Contribute(v), $"{FundUi.Money(v)} moved from your bank into {Fund.Name}.");
            }, buttons, "owner-contribute");
            FundUi.Secondary("WITHDRAW TO MY BANK", () =>
            {
                if (!FundUi.TryMoney(amount.value, out decimal v)) { Say("Enter an amount.", true); return; }
                Act(Fund.Withdraw(v), $"{FundUi.Money(v)} moved from {Fund.Name} to your bank.");
            }, buttons, "owner-withdraw");

            VisualElement ledger = FundUi.Card(_page, "Ledger (operating cash)");
            FundUi.Row(ledger, "fund-row-head", ("TIME", 150), ("TYPE", 170), ("DESCRIPTION", 0), ("AMOUNT", 120), ("BALANCE", 120));
            var entries = Fund.Ledger.Entries;
            for (int i = entries.Count - 1, n = 0; i >= 0 && n < 80; i--, n++)
            {
                CashEntry x = entries[i];
                var cells = FundUi.Row(ledger, "", (new DateTime(x.Time).ToString("MMM d h:mm tt", FundUi.C), 150), (CompanyLedger.KindName(x.Kind), 170), (x.Memo, 0), ("", 120), (FundUi.Money(x.BalanceAfter), 120));
                FundUi.SetSigned(cells[3], x.Amount);
            }
            _signature = () => Fund.Ledger.Entries.Count.ToString();
        }

        // ------------------------------------------------------------------ settings

        private void Settings()
        {
            Title("Company settings");
            var cols = Ui.Box("fund-cols", _page);
            VisualElement id = FundUi.Card(cols, "Identity", "fund-grow");
            FundUi.Line(id, "Registered name", Fund.Name);
            FundUi.Line(id, "Registered", Fund.Founded.ToString("MMMM d, yyyy", FundUi.C));
            FundUi.Line(id, "Reputation", $"{Fund.ReputationTier} ({Fund.Reputation:0}/100)");
            Ui.Label("fund-note", id, "Reputation builds slowly from sustained results, paying people on time, treating staff well and staying inside risk limits. One lucky day barely moves it.");
            Ui.Label("fund-label", id, "Logo");
            var logos = Ui.Box("fund-pickers", id);
            for (int i = 0; i < FundBrand.Logos.Length; i++)
            {
                int k = i;
                Button b = Ui.Button("", () => { Fund.Rebrand(k, Fund.Colour); Build(); }, "fund-pick", logos, "settings-logo-" + i);
                b.Add(new FundLogo(i, Fund.Colour, 30f));
                b.EnableInClassList("selected", i == Fund.Logo);
            }
            Ui.Label("fund-label", id, "Colour");
            var colours = Ui.Box("fund-pickers", id);
            for (int i = 0; i < FundBrand.Colours.Length; i++)
            {
                int k = i;
                Button b = Ui.Button("", () => { Fund.Rebrand(Fund.Logo, k); Build(); }, "fund-swatch", colours, "settings-colour-" + i);
                b.style.backgroundColor = FundUi.BrandColour(i);
                b.EnableInClassList("selected", i == Fund.Colour);
            }

            VisualElement defaults = FundUi.Card(cols, "Defaults for new hires", "fund-side");
            Ui.Label("fund-label", defaults, "Max contracts per position");
            TextField mc = FundUi.Field(defaults, Fund.Config.DefaultMaxContracts.ToString(FundUi.C), "default-contracts");
            Ui.Label("fund-label", defaults, "Max risk per trade ($)");
            TextField mr = FundUi.Field(defaults, Fund.Config.DefaultRiskPerTrade.ToString("0", FundUi.C), "default-risk", ".,$");
            Ui.Label("fund-label", defaults, "Max daily loss ($)");
            TextField md = FundUi.Field(defaults, Fund.Config.DefaultDailyLoss.ToString("0", FundUi.C), "default-daily", ".,$");
            FundUi.Primary("SAVE", () =>
            {
                if (int.TryParse(mc.value, out int a) && a > 0) Fund.Config.DefaultMaxContracts = a;
                if (FundUi.TryMoney(mr.value, out decimal b) && b > 0m) Fund.Config.DefaultRiskPerTrade = b;
                if (FundUi.TryMoney(md.value, out decimal c) && c > 0m) Fund.Config.DefaultDailyLoss = c;
                Act(null, "Defaults saved.");
            }, defaults, "defaults-save");

            VisualElement danger = FundUi.Card(_page, "Wind down the fund", "fund-card-danger");
            Ui.Label("fund-muted fund-wrap", danger, "Closing stops all trading, closes positions through the market, recalls every desk's capital, pays what's owed (your bank covers any shortfall) and returns the rest to you. Staff leave. You can't register another fund for " + Fund.Config.ReformCooldownDays + " days.");
            FundUi.Armed("WIND DOWN " + Fund.Name.ToUpperInvariant(), "CONFIRM: CLOSE THE FUND", () =>
            {
                Fund.WindUp("Wound down by the owner.");
                Act(null, "Winding down: positions are closing and accounts will settle.");
            }, danger, "fund-btn-danger", "settings-winddown");
            _signature = () => Fund.Logo + "|" + Fund.Colour;
        }
    }
}
