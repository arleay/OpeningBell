using System;
using System.Collections.Generic;
using System.Linq;
using OpeningBell.Fund;
using OpeningBell.Market;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// Ledgerline (ledgerline.com), the fund administration portal: the company's dashboard (FUND_SPEC §5). A side menu of
    /// pages (overview, recruitment, employees, performance, capital and risk, office, training, finances, settings);
    /// each page lays itself out once and then keeps its numbers live, rebuilding only when its structure changes (and
    /// never while you're typing in one of its fields).
    /// </summary>
    internal sealed partial class LedgerlineSite : BrowserPage
    {
        public const string Host = "ledgerline.com";

        private static readonly (string Path, string Title)[] Pages =
        {
            ("", "Overview"), ("recruiting", "Recruitment"), ("employees", "Employees"), ("performance", "Trading performance"),
            ("capital", "Capital and risk"), ("office", "Office and workstations"), ("training", "Training"), ("finances", "Finances"),
            ("settings", "Company settings"),
        };

        private readonly VisualElement _header, _nav, _page;
        private readonly Label _status, _name, _sub;
        private readonly FundLogo _logo;
        private readonly Dictionary<string, Button> _navButtons = new Dictionary<string, Button>();
        private readonly List<Action> _live = new List<Action>();
        private Func<string> _signature = () => "";
        private string _known = "";
        private string _path = "";

        public LedgerlineSite(BrowserApp browser) : base(browser, "fund-site ledgerline")
        {
            _header = Ui.Box("site-header fund-header", Root);
            _logo = new FundLogo(0, 0, 34f) { OnDark = false };
            _header.Add(_logo);
            var titles = Ui.Box("site-titles fund-header-titles", _header);
            _name = Ui.Label("site-name", titles, "Ledgerline");
            _sub = Ui.Label("site-tagline", titles, "Fund administration");
            Ui.Box("spacer", _header);
            Ui.Label("fund-header-brand", _header, "LEDGERLINE");

            _status = Ui.Label("firm-status", Root);
            _status.name = "ledgerline-status";
            Ui.Show(_status, false);

            var body = Ui.Box("fund-shell app-grow", Root);
            _nav = Ui.Box("fund-nav", body);
            foreach (var (path, title) in Pages)
            {
                string p = path;
                Button b = Ui.Button(title, () => Browser.Navigate(Host + (p.Length > 0 ? "/" + p : "")), "fund-nav-item", _nav, "nav-" + (p.Length > 0 ? p : "overview"));
                _navButtons[p] = b;
            }
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("fund-main");
            body.Add(scroll);
            _page = scroll.contentContainer;
        }

        private GameBootstrap Game => Context.Game;
        private HedgeFund Fund => Game.Fund;
        private DateTime Now => Game.Market.Now;

        public override string Show(string path)
        {
            _path = path ?? "";
            Say("", false);
            Build();
            string top = _path.Split('/')[0];
            foreach (var kv in _navButtons) kv.Value.EnableInClassList("active", kv.Key == top || (kv.Key == "employees" && top == "employee") || (kv.Key == "recruiting" && top == "applicant"));
            string title = Pages.FirstOrDefault(p => p.Path == top).Title ?? "Overview";
            return Fund.Exists ? $"{title} · {Fund.Name} · Ledgerline" : "Ledgerline · Fund administration";
        }

        public override void Refresh()
        {
            UpdateHeader();
            string sig = Fund.Exists + "|" + _signature();
            if (sig != _known && !Typing())
            {
                Build();
                return;
            }
            foreach (Action a in _live) a();
        }

        /// <summary>A text field has focus: a rebuild now would throw away what's being typed.</summary>
        private bool Typing() => Root.panel?.focusController?.focusedElement is TextField || Root.panel?.focusController?.focusedElement is TextElement te && te.parent is TextField;

        private void UpdateHeader()
        {
            if (Fund.Exists)
            {
                Ui.SetText(_name, Fund.Name);
                Ui.SetText(_sub, $"{Fund.ReputationTier} · {HedgeFund.OfficeName}");
                _header.style.backgroundColor = FundUi.BrandColour(Fund.Colour);
                if (_logo.Mark != Fund.Logo || _logo.Colour != Fund.Colour) _logo.Set(Fund.Logo, Fund.Colour);
                _logo.OnDark = true;
            }
            else
            {
                Ui.SetText(_name, "Ledgerline");
                Ui.SetText(_sub, "Fund administration");
                _header.style.backgroundColor = new UnityEngine.Color(0.06f, 0.09f, 0.16f);
            }
            Ui.Show(_logo, Fund.Exists);
            Ui.Show(_nav, Fund.Exists);
        }

        private void Say(string text, bool error)
        {
            Ui.SetText(_status, text);
            Ui.Show(_status, text.Length > 0);
            _status.EnableInClassList("error", error);
        }

        /// <summary>After an action: show the result and rebuild.</summary>
        private void Act(string error, string success)
        {
            if (error != null) Say(error, true);
            else Say(success ?? "", false);
            Build();
        }

        private void Build()
        {
            _page.Clear();
            _live.Clear();
            _signature = () => "";
            UpdateHeader();
            if (!Fund.Exists)
            {
                Landing();
            }
            else
            {
                string[] parts = _path.Split('/');
                switch (parts[0])
                {
                    case "recruiting": Recruiting(); break;
                    case "applicant": ApplicantPage(parts.Length > 1 && long.TryParse(parts[1], out long a) ? a : 0); break;
                    case "employees": EmployeesPage(); break;
                    case "employee": EmployeePage(parts.Length > 1 && long.TryParse(parts[1], out long e) ? e : 0); break;
                    case "performance": Performance(); break;
                    case "capital": Capital(); break;
                    case "office": Office(); break;
                    case "training": TrainingPage(); break;
                    case "finances": Finances(); break;
                    case "settings": Settings(); break;
                    default: Overview(); break;
                }
            }
            _known = Fund.Exists + "|" + _signature();
            foreach (Action a in _live) a();
        }

        private void Title(string title, string lede = null)
        {
            Ui.Label("fund-h1", _page, title);
            if (lede != null) Ui.Label("fund-lede", _page, lede);
        }

        private void Landing()
        {
            Title("Fund administration for Kell Valley managers", "Ledgerline runs the back office of your fund: people, desks, capital, risk and the books. Register your fund at the Kell Valley Business Registry to get started.");
            FundUi.Primary("GO TO THE BUSINESS REGISTRY", () => Browser.Navigate(RegistrySite.Host), _page, "ledgerline-to-registry");
        }

        // ------------------------------------------------------------------ overview

        private void Overview()
        {
            Title("Overview");
            var kpis = Ui.Box("fund-kpis", _page);
            var cash = FundUi.Kpi(kpis, "Operating cash");
            var equity = FundUi.Kpi(kpis, "Company equity");
            var allocated = FundUi.Kpi(kpis, "Allocated to traders");
            var reserved = FundUi.Kpi(kpis, "Reserved for obligations");
            var realized = FundUi.Kpi(kpis, "Realized P&L today");
            var open = FundUi.Kpi(kpis, "Unrealized P&L");
            var expenses = FundUi.Kpi(kpis, "Operating expenses today");
            var net = FundUi.Kpi(kpis, "Net result today");

            var row = Ui.Box("fund-cols", _page);
            VisualElement chartCard = FundUi.Card(row, "Company equity", "fund-grow");
            var chart = new LineChart(170f);
            chartCard.Add(chart);
            Label chartNote = Ui.Label("fund-muted", chartCard);
            VisualElement side = FundUi.Card(row, "Team", "fund-side");
            Label level = FundUi.Line(side, "Reputation");
            Label active = FundUi.Line(side, "Active traders");
            Label waiting = FundUi.Line(side, "Waiting for workstations");
            Label training = FundUi.Line(side, "Training in progress");
            Label collectible = FundUi.Line(side, "Profit to collect");
            Button collectAll = FundUi.Collect("COLLECT ALL", () =>
            {
                decimal got = Fund.CollectAll();
                Act(null, got > 0m ? $"Collected {FundUi.Money(got)} into operating cash." : "Nothing to collect.");
            }, side, "overview-collect-all");

            VisualElement alerts = FundUi.Card(_page, "Alerts");
            var alertList = Ui.Box("fund-alerts", alerts);
            int alertVersion = -1;

            _signature = () => Fund.Notices.Count.ToString();
            _live.Add(() =>
            {
                decimal dayRealized = 0m, dayExpenses = Fund.Ledger.ExpensesBetween(Now.Date, Now.Date.AddDays(1));
                foreach (Employee e in Fund.Employees) if (e.Today != null && new DateTime(e.Today.Date) == Now.Date) dayRealized += e.Today.Realized - e.Today.Fees;
                Ui.SetText(cash.Value, FundUi.Money(Fund.Ledger.Cash));
                Ui.SetText(cash.Sub, $"{FundUi.Money(Math.Max(0m, Fund.FreeCash))} free");
                Ui.SetText(equity.Value, FundUi.Money(Fund.Equity));
                decimal change = Fund.Equity - Fund.Ledger.NetContributions;
                FundUi.SetSigned(equity.Sub, change, FundUi.Signed(change) + " on capital in");
                Ui.SetText(allocated.Value, FundUi.Money(Fund.Allocated));
                Ui.SetText(allocated.Sub, $"{FundUi.Money(Fund.DeskEquity)} desk equity");
                Ui.SetText(reserved.Value, FundUi.Money(Fund.Liabilities));
                Ui.SetText(reserved.Sub, "wages, commissions, bills owed");
                FundUi.SetSigned(realized.Value, dayRealized);
                Ui.SetText(realized.Sub, "after execution costs");
                FundUi.SetSigned(open.Value, Fund.Unrealized);
                Ui.SetText(open.Sub, "open positions, not collectible");
                Ui.SetText(expenses.Value, FundUi.Money(dayExpenses));
                Ui.SetText(expenses.Sub, "wages, commissions, bills, training");
                FundUi.SetSigned(net.Value, dayRealized + Fund.Unrealized - dayExpenses);
                Ui.SetText(net.Sub, "trading less expenses");

                Ui.SetText(level, $"{Fund.ReputationTier} ({Fund.Reputation:0})");
                int activeN = 0, waitingN = 0, trainingN = 0;
                foreach (Employee e in Fund.Staff)
                {
                    if (e.Activity == Activity.Trading) activeN++;
                    if (e.Activity == Activity.WaitingForWorkstation) waitingN++;
                    if (e.CurrentTraining != null) trainingN++;
                }
                Ui.SetText(active, activeN.ToString(FundUi.C));
                Ui.SetText(waiting, waitingN.ToString(FundUi.C));
                waiting.EnableInClassList("fund-down", waitingN > 0);
                Ui.SetText(training, trainingN.ToString(FundUi.C));
                decimal c = Fund.CollectibleTotal;
                Ui.SetText(collectible, FundUi.Money(c));
                Ui.SetText(collectAll, c > 0m ? $"COLLECT ALL {FundUi.Money(c)}" : "NOTHING TO COLLECT");
                collectAll.SetEnabled(c > 0m);

                var series = new List<double>();
                foreach (CompanyDay d in Fund.History.Skip(Math.Max(0, Fund.History.Count - 60))) series.Add((double)d.EquityValue);
                series.Add((double)Fund.Equity);
                chart.Set(series);
                Ui.SetText(chartNote, Fund.History.Count == 0 ? "The curve fills in day by day." : $"Last {Math.Min(60, Fund.History.Count)} business days and now.");

                if (alertVersion != Fund.Notices.Count)
                {
                    alertVersion = Fund.Notices.Count;
                    alertList.Clear();
                    Alerts(alertList);
                }
            });
        }

        private void Alerts(VisualElement list)
        {
            decimal runway = RunwayDays();
            if (runway < Fund.Config.WarnRunwayDays)
                AlertRow(list, "bad", "Low operating cash", runway <= 0 ? "Cash doesn't cover what's owed. Contribute, collect profit or recall capital." : $"About {runway:0} business days of costs left.");
            if (Fund.DelinquentDays > 0) AlertRow(list, "bad", "Payments overdue", $"{Fund.DelinquentDays} business day(s). At {Fund.Config.DefaultDays} the fund is wound up.");
            if (!Fund.NetworkUp) AlertRow(list, "bad", "Office offline", "Connectivity bill unpaid: nobody can trade.");
            int shown = 0;
            for (int i = Fund.Notices.Count - 1; i >= 0 && shown < 10; i--, shown++)
            {
                FundNotice n = Fund.Notices[i];
                string kind = n.Level == NoticeLevel.Urgent ? "bad" : n.Level == NoticeLevel.Important ? "warn" : "muted";
                AlertRow(list, kind, n.Count > 1 ? $"{n.Title} ({n.Count})" : n.Title, n.Body, new DateTime(n.Time));
            }
            if (list.childCount == 0) Ui.Label("fund-muted", list, "Nothing needs your attention.");
        }

        private static void AlertRow(VisualElement list, string kind, string title, string body, DateTime? when = null)
        {
            var row = Ui.Box("fund-alert", list);
            FundUi.Pill(row, kind == "bad" ? "URGENT" : kind == "warn" ? "NOTE" : "INFO", kind);
            var text = Ui.Box("fund-alert-text", row);
            Ui.Label("fund-alert-title", text, title);
            Ui.Label("fund-alert-body", text, body);
            if (when.HasValue) Ui.Label("fund-muted fund-alert-time", row, when.Value.ToString("ddd h:mm tt", FundUi.C));
        }

        /// <summary>Business days operating cash covers at the recent daily cost (accrual basis, last five days).</summary>
        private decimal RunwayDays()
        {
            decimal recent = Fund.Ledger.ExpensesBetween(Now.Date.AddDays(-7), Now.Date);
            decimal perDay = recent / 5m;
            decimal payroll = 0m;
            foreach (Employee e in Fund.Staff) if (e.Contract.PaysHourly) payroll += e.Contract.Hourly * 8m;
            perDay = Math.Max(perDay, payroll + (Fund.Config.OfficeUtilities + Fund.Config.OfficeConnectivity + (Fund.Tenure == OfficeTenure.Leased ? Fund.Config.OfficeMonthlyRent : 0m)) / 21m);
            return perDay <= 0m ? 999m : Math.Max(0m, Fund.FreeCash) / perDay;
        }
    }
}
