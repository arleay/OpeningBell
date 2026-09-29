using System;
using OpeningBell.Fund;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// Kell Valley Business Registry (kvregistry.gov): where the player forms their fund (FUND_SPEC §4). Checks the career
    /// milestone and the money, takes a name, logo and colour, confirms the office on Level 26, and spells out exactly
    /// where each dollar goes before a confirming second click registers it.
    /// </summary>
    internal sealed class RegistrySite : BrowserPage
    {
        public const string Host = "kvregistry.gov";

        private readonly VisualElement _content;
        private readonly Label _status;
        private string _name = "";
        private int _logo, _colour;
        private OfficeTenure _office = OfficeTenure.Leased;
        private decimal _extra;
        private string _signature = "";
        private Action _live;

        public RegistrySite(BrowserApp browser) : base(browser, "fund-site registry-site")
        {
            VisualElement header = Header(Root, "site-registry", GlyphKind.Bank, "Kell Valley Business Registry", "Form a company · Filings · Licences");
            Ui.Box("spacer", header);
            _status = Ui.Label("firm-status", Root);
            _status.name = "registry-status";
            Ui.Show(_status, false);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("site-body");
            scroll.AddToClassList("app-grow");
            Root.Add(scroll);
            _content = scroll.contentContainer;
        }

        private GameBootstrap Game => Context.Game;
        private HedgeFund Fund => Game.Fund;

        public override string Show(string path)
        {
            Build();
            return "Kell Valley Business Registry · Form a fund";
        }

        private string Signature() => Fund.Exists + Fund.Name;

        public override void Refresh()
        {
            if (Signature() != _signature) Build();
            _live?.Invoke();
        }

        private void Say(string text, bool error)
        {
            Ui.SetText(_status, text);
            Ui.Show(_status, text.Length > 0);
            _status.EnableInClassList("error", error);
        }

        private void Build()
        {
            _content.Clear();
            _live = null;
            _signature = Signature();
            if (Fund.Exists) BuildRegistered();
            else BuildForm();
        }

        private void BuildRegistered()
        {
            Ui.Label("tp-title", _content, "Your company");
            VisualElement card = FundUi.Card(_content, "Registered investment fund");
            var head = Ui.Box("fund-hrow", card);
            head.Add(new FundLogo(Fund.Logo, Fund.Colour, 44f));
            var titles = Ui.Box("fund-titles", head);
            Ui.Label("fund-h2", titles, Fund.Name);
            Ui.Label("fund-muted", titles, $"Registered {Fund.Founded:MMMM d, yyyy} · {HedgeFund.OfficeName}");
            FundUi.Line(card, "Status", Fund.WindingUp ? "Winding up" : "Active");
            FundUi.Line(card, "Owner", Game.Look != null && Game.Look.Name.Length > 0 ? Game.Look.Name : "You");
            FundUi.Line(card, "Office", Fund.Tenure == OfficeTenure.Owned ? "Owned" : "Leased");
            FundUi.Primary("OPEN THE LEDGERLINE DASHBOARD", () => Browser.Navigate(LedgerlineSite.Host), card, "registry-open-dashboard");
        }

        private void BuildForm()
        {
            Ui.Label("tp-title", _content, "Register an investment fund");
            Ui.Label("firm-lede", _content, "Trade the company's money through traders you hire. The fund is a separate company: its cash and yours stay apart, and money moves between them only when you move it.");
            if (Fund.ClosedOn != default)
                Ui.Label("firm-lede", _content, $"Your previous fund closed on {Fund.ClosedOn:MMM d}: {Fund.ClosedReason}");

            var cols = Ui.Box("fund-cols", _content);
            var left = Ui.Box("fund-col-main", cols);
            var right = Ui.Box("fund-col-side", cols);

            // Eligibility.
            VisualElement elig = FundUi.Card(left, "Eligibility");
            Label days = FundUi.Line(elig, "Trading record");
            Label profit = FundUi.Line(elig, "Proven trading profit");
            Label bank = FundUi.Line(elig, "Your bank balance");
            Label verdict = Ui.Label("fund-note", elig);

            // The company.
            VisualElement form = FundUi.Card(left, "The company");
            Ui.Label("fund-label", form, "Fund name");
            TextField name = FundUi.Field(form, _name, "registry-name", width: 360f);
            name.maxLength = 36;
            name.RegisterValueChangedCallback(e => _name = e.newValue);
            Ui.Label("fund-label", form, "Logo");
            var logos = Ui.Box("fund-pickers", form);
            var logoViews = new System.Collections.Generic.List<(Button, FundLogo)>();
            var swatches = new System.Collections.Generic.List<Button>();
            for (int i = 0; i < FundBrand.Logos.Length; i++)
            {
                int k = i;
                Button b = Ui.Button("", null, "fund-pick", logos, "registry-logo-" + i);
                var mark = new FundLogo(i, _colour, 34f);
                b.Add(mark);
                b.tooltip = FundBrand.Logos[i];
                b.clicked += () => { _logo = k; Sync(); };
                logoViews.Add((b, mark));
            }
            Ui.Label("fund-label", form, "Colour");
            var colours = Ui.Box("fund-pickers", form);
            for (int i = 0; i < FundBrand.Colours.Length; i++)
            {
                int k = i;
                Button b = Ui.Button("", null, "fund-swatch", colours, "registry-colour-" + i);
                b.style.backgroundColor = FundUi.BrandColour(i);
                b.tooltip = FundBrand.Colours[i].Name;
                b.clicked += () => { _colour = k; Sync(); };
                swatches.Add(b);
            }
            void Sync()
            {
                for (int i = 0; i < logoViews.Count; i++)
                {
                    logoViews[i].Item1.EnableInClassList("selected", i == _logo);
                    logoViews[i].Item2.Set(i, _colour);
                }
                for (int i = 0; i < swatches.Count; i++) swatches[i].EnableInClassList("selected", i == _colour);
            }
            Sync();

            // The office.
            FundConfig cfg = Fund.Config;
            VisualElement office = FundUi.Card(left, "Headquarters: " + HedgeFund.OfficeName);
            Ui.Label("fund-muted fund-wrap", office, "The full floor directly below the Harborview penthouse: reception, open trading floor, a private office, a meeting room, a coffee point and restrooms. Finished and lit, with power and data points; you buy the desks, chairs and screens. Staff reach it by the residents' lift from the lobby and the P1 garage.");
            var choices = Ui.Box("fund-choices", office);
            Button lease = Ui.Button("", () => { _office = OfficeTenure.Leased; }, "fund-choice", choices, "registry-lease");
            Ui.Label("fund-choice-title", lease, "Lease");
            Ui.Label("fund-choice-sub", lease, $"{FundUi.Money0(cfg.OfficeMonthlyRent)}/month + {FundUi.Money0(cfg.OfficeDeposit)} refundable deposit, paid by the company");
            Button buy = Ui.Button("", () => { _office = OfficeTenure.Owned; }, "fund-choice", choices, "registry-buy");
            Ui.Label("fund-choice-title", buy, "Buy");
            Ui.Label("fund-choice-sub", buy, $"{FundUi.Money0(cfg.OfficePurchasePrice)}, paid by the company (contribute the extra capital below)");

            Ui.Label("fund-label", form, "Extra starting capital (optional)");
            TextField extra = FundUi.Field(form, "", "registry-extra", ".,$", 200f);
            extra.RegisterValueChangedCallback(e => _extra = FundUi.TryMoney(e.newValue, out decimal v) ? Math.Max(0m, v) : 0m);

            // What it costs, line by line.
            VisualElement costs = FundUi.Card(right, "Cost breakdown");
            Label fee = FundUi.Line(costs, "Formation, legal and setup");
            Ui.Label("fund-note", costs, "Paid to the registry and advisers. Spent, not kept.");
            Label capital = FundUi.Line(costs, "Starting capital to the company");
            Ui.Label("fund-note", costs, "Still yours: it becomes the company's operating cash.");
            Label total = FundUi.Line(costs, "Leaving your bank today", "", "fund-line-total");
            Ui.Box("fund-sep", costs);
            Label officeCost = FundUi.Line(costs, "Office (paid by the company)");
            Label dayOne = FundUi.Line(costs, "Company cash on day one", "", "fund-line-total");
            Ui.Box("fund-sep", costs);
            Ui.Label("fund-muted fund-wrap", costs, string.Format(FundUi.C, "Then monthly, from the company: {0}utilities {1}, connectivity {2}, market data {3} per trader. Furniture, equipment, wages, commissions and training are separate.",
                _office == OfficeTenure.Leased ? "rent, " : "", FundUi.Money0(cfg.OfficeUtilities), FundUi.Money0(cfg.OfficeConnectivity), FundUi.Money0(cfg.MarketDataPerSeat)));
            Button register = FundUi.Armed("REGISTER THE FUND", "CONFIRM: CHARGE MY BANK", Register, right, "fund-btn-primary fund-btn-wide", "registry-submit");

            _live = () =>
            {
                (int tradingDays, decimal proven) = Game.Career;
                decimal balance = Game.Economy.Bank.Balance;
                decimal needed = cfg.FormationFee + cfg.StartingCapital + _extra;
                Ui.SetText(days, $"{tradingDays} of {cfg.RequiredTradingDays} trading days");
                days.EnableInClassList("fund-down", tradingDays < cfg.RequiredTradingDays);
                days.EnableInClassList("fund-up", tradingDays >= cfg.RequiredTradingDays);
                Ui.SetText(profit, $"{FundUi.Money0(proven)} of {FundUi.Money0(cfg.RequiredProvenProfit)}");
                profit.EnableInClassList("fund-down", proven < cfg.RequiredProvenProfit);
                profit.EnableInClassList("fund-up", proven >= cfg.RequiredProvenProfit);
                Ui.SetText(bank, $"{FundUi.Money0(balance)} of {FundUi.Money0(needed)}");
                bank.EnableInClassList("fund-down", balance < needed);
                bank.EnableInClassList("fund-up", balance >= needed);
                string blocker = Fund.RegistrationBlocker(tradingDays, proven, balance - _extra);
                Ui.SetText(verdict, blocker ?? "You're eligible to register a fund.");
                verdict.EnableInClassList("fund-down", blocker != null);

                lease.EnableInClassList("selected", _office == OfficeTenure.Leased);
                buy.EnableInClassList("selected", _office == OfficeTenure.Owned);
                decimal cap = cfg.StartingCapital + _extra;
                decimal officeNow = _office == OfficeTenure.Leased ? cfg.OfficeMonthlyRent + cfg.OfficeDeposit : cfg.OfficePurchasePrice;
                Ui.SetText(fee, FundUi.Money0(cfg.FormationFee));
                Ui.SetText(capital, FundUi.Money0(cap));
                Ui.SetText(total, FundUi.Money0(cfg.FormationFee + cap));
                Ui.SetText(officeCost, _office == OfficeTenure.Leased
                    ? $"{FundUi.Money0(cfg.OfficeMonthlyRent)} rent + {FundUi.Money0(cfg.OfficeDeposit)} deposit"
                    : FundUi.Money0(cfg.OfficePurchasePrice));
                Ui.SetText(dayOne, FundUi.Money0(cap - officeNow));
                dayOne.EnableInClassList("fund-down", cap < officeNow);
                register.SetEnabled(blocker == null && cap >= officeNow);
            };
            _live();
        }

        private void Register()
        {
            (int tradingDays, decimal proven) = Game.Career;
            string error = Fund.Register(_name, _logo, _colour, _office, _extra, tradingDays, proven, Game.Economy.Bank.Balance);
            if (error != null)
            {
                Say(error, true);
                return;
            }
            Say($"{Fund.Name} is registered. Next: furnish {HedgeFund.OfficeName}, post a job ad and hire.", false);
            Build();
        }
    }
}
