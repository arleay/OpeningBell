using System;
using System.Collections.Generic;
using System.Linq;
using OpeningBell.Fund;
using OpeningBell.Market;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    internal sealed partial class LedgerlineSite
    {
        // ------------------------------------------------------------------ recruitment

        private void Recruiting()
        {
            Title("Recruitment", "Post a job ad and applications arrive over the next days. Better reputation brings more experienced people, who expect more.");
            var cols = Ui.Box("fund-cols", _page);
            VisualElement post = FundUi.Card(cols, "Post a job ad", "fund-side");
            var sectors = new List<string> { "Any sector" };
            sectors.AddRange(Enum.GetValues(typeof(Sector)).Cast<Sector>().Where(s => s != Sector.Financials).Select(Person.SectorName));
            var sectorDrop = new DropdownField("Specialty", sectors, 0) { name = "post-sector" };
            sectorDrop.AddToClassList("fund-drop");
            post.Add(sectorDrop);
            var strategies = new List<string> { "Any strategy" };
            strategies.AddRange(Enum.GetValues(typeof(Strategy)).Cast<Strategy>().Select(Person.StrategyName));
            var strategyDrop = new DropdownField("Strategy", strategies, 0) { name = "post-strategy" };
            strategyDrop.AddToClassList("fund-drop");
            post.Add(strategyDrop);
            foreach (Channel ch in new[] { Channel.LocalBoard, Channel.IndustryBoard, Channel.Headhunter })
            {
                Channel c = ch;
                var row = Ui.Box("fund-course", post);
                var text = Ui.Box("fund-course-text", row);
                Ui.Label("fund-strong", text, HedgeFund.ChannelName(c));
                Ui.Label("fund-muted fund-wrap", text, HedgeFund.ChannelBlurb(c));
                decimal price = HedgeFund.ListingPrice(c);
                FundUi.Primary(price > 0m ? $"POST {FundUi.Money0(price)}" : "POST FREE", () =>
                {
                    Sector? sector = sectorDrop.index <= 0 ? (Sector?)null : Enum.GetValues(typeof(Sector)).Cast<Sector>().Where(s => s != Sector.Financials).ElementAt(sectorDrop.index - 1);
                    Strategy? strategy = strategyDrop.index <= 0 ? (Strategy?)null : (Strategy)(strategyDrop.index - 1);
                    Act(Fund.Post(c, sector, strategy, out _), $"Posted on the {HedgeFund.ChannelName(c).ToLowerInvariant()} for {Fund.Config.ListingDays} days.");
                }, row, "post-" + c.ToString().ToLowerInvariant());
            }

            VisualElement listings = FundUi.Card(cols, "Your listings", "fund-grow");
            FundUi.Row(listings, "fund-row-head", ("CHANNEL", 170), ("LOOKING FOR", 0), ("POSTED", 90), ("ENDS", 90), ("APPLIED", 70), ("", 80));
            int live = 0;
            for (int i = Fund.Listings.Count - 1; i >= 0; i--)
            {
                Listing l = Fund.Listings[i];
                if (!l.Live(Now) && (Now - new DateTime(l.Expires)).TotalDays > 7) continue;
                string want = (l.AnySector ? "any sector" : Person.SectorName(l.Sector)) + " · " + (l.AnyStrategy ? "any strategy" : Person.StrategyName(l.Strategy));
                var cells = FundUi.Row(listings, "", (HedgeFund.ChannelName(l.Channel), 170), (want, 0), (new DateTime(l.Posted).ToString("MMM d", FundUi.C), 90),
                    (new DateTime(l.Expires).ToString("MMM d", FundUi.C), 90), (l.Received.ToString(FundUi.C), 70), (l.Live(Now) ? "" : "Closed", 80));
                if (l.Live(Now))
                {
                    live++;
                    Listing ll = l;
                    Button close = FundUi.Secondary("CLOSE", () => { Fund.CloseListing(ll); Act(null, "Listing closed."); }, cells[5].parent);
                    cells[5].RemoveFromHierarchy();
                    close.style.width = 80;
                }
            }
            if (Fund.Listings.Count == 0) Ui.Label("fund-muted", listings, "No listings yet.");

            // Support staff: through an agency, the going rate, one of each; they work from the reception desk.
            VisualElement support = FundUi.Card(_page, "Support staff");
            Ui.Label("fund-muted fund-wrap", support, "Hired through an agency on the going hourly rate and starting the next business day. They don't trade and need no workstation: they work from the reception desk.");
            foreach (Role role in new[] { Role.OfficeManager, Role.Receptionist })
            {
                Role r = role;
                var row = Ui.Box("fund-course", support);
                var text = Ui.Box("fund-course-text", row);
                decimal hourly = HedgeFund.AdminHourly(r);
                Ui.Label("fund-strong", text, $"{HedgeFund.RoleName(r)} · {FundUi.Money(hourly)}/h (about {FundUi.Money0(hourly * 40m)} a week)");
                Ui.Label("fund-muted fund-wrap", text, HedgeFund.RoleDuty(r));
                Employee holder = Fund.AdminOf(r);
                if (holder != null) Ui.Label("fund-muted", row, holder.Activity == Activity.AwaitingStart ? $"{holder.Name} starts {new DateTime(holder.StartsOn):ddd MMM d}" : $"{holder.Name} is on the team");
                else FundUi.Primary("HIRE", () => Act(Fund.HireAdmin(r), $"Hired a{(r == Role.OfficeManager ? "n" : "")} {HedgeFund.RoleName(r).ToLowerInvariant()}: they start next business day."),
                    row, "hire-" + r.ToString().ToLowerInvariant());
            }

            VisualElement apps = FundUi.Card(_page, "Applicants");
            var open = Fund.Applicants.Where(a => a.Status == ApplicantStatus.Open).OrderByDescending(a => a.Received).ToList();
            if (open.Count == 0) Ui.Label("fund-muted", apps, live > 0 ? "No applications yet: they arrive over the coming days." : "Post a job ad to receive applications.");
            foreach (Applicant a in open)
            {
                Person p = a.Person;
                var card = Ui.Box("fund-applicant", apps);
                card.name = "applicant-" + a.Id;
                FundUi.Avatar(card, p, 40f);
                var text = Ui.Box("fund-titles fund-grow", card);
                Ui.Label("fund-strong", text, $"{p.Name} · {p.Seniority}");
                Ui.Label("fund-muted", text, $"{Person.StrategyName(p.Strategy)} · {Person.SectorName(p.Specialty)} · {p.Years} yrs · {p.TradesPerDay:0.#} trades/day");
                Ui.Label("fund-muted", text, $"Est. win rate {FundUi.Pct(p.PastWinRate)} (historical) · R:R {p.PastRewardRisk:0.00} · self-control {p[Skill.SelfControl]:0} · risk mgmt {p[Skill.RiskManagement]:0}");
                var ask = Ui.Box("fund-titles", card);
                Ui.Label("fund-strong", ask, a.Asking.Describe());
                Ui.Label("fund-muted", ask, $"available {new DateTime(a.AvailableFrom):ddd MMM d}");
                long id = a.Id;
                FundUi.Primary("REVIEW", () => Browser.Navigate(Host + "/applicant/" + id), card, "review-" + id);
            }
            var past = Fund.Applicants.Where(a => a.Status != ApplicantStatus.Open).Reverse().Take(8).ToList();
            if (past.Count > 0)
            {
                VisualElement closed = FundUi.Card(_page, "Recently closed applications");
                foreach (Applicant a in past) FundUi.Line(closed, a.Person.Name, a.Status == ApplicantStatus.Hired ? "Hired" : a.Status == ApplicantStatus.Withdrawn ? "Withdrew" : a.Status == ApplicantStatus.Expired ? "Withdrew (no reply)" : "Declined");
            }
            _signature = () => Fund.Applicants.Count(a => a.Status == ApplicantStatus.Open) + "|" + Fund.Listings.Count + "|" + Fund.Listings.Count(l => l.Live(Now));
        }

        private void ApplicantPage(long id)
        {
            Applicant a = Fund.FindApplicant(id);
            if (a == null)
            {
                Title("Applicant not found");
                return;
            }
            Person p = a.Person;
            FundUi.Secondary("← ALL APPLICANTS", () => Browser.Navigate(Host + "/recruiting"), _page, "back-recruiting");
            PersonView.Header(_page, p, $"{p.Seniority} · applied {new DateTime(a.Received):MMM d} · available {new DateTime(a.AvailableFrom):ddd MMM d}");
            var cols = Ui.Box("fund-cols", _page);
            VisualElement facts = FundUi.Card(cols, "Profile", "fund-grow");
            PersonView.Facts(facts, p);
            VisualElement traits = FundUi.Card(facts, "Personality");
            PersonView.Traits(traits, p);
            VisualElement skills = FundUi.Card(facts, "Skills");
            PersonView.Skills(skills, p);

            VisualElement offer = FundUi.Card(cols, "Offer", "fund-side");
            FundUi.Line(offer, "They're asking", a.Asking.Describe());
            FundUi.Line(offer, "Interest in the job", a.Interest >= 0.66 ? "keen" : a.Interest >= 0.4 ? "open" : "lukewarm");
            if (a.LastCounter != null) FundUi.Line(offer, "Their counteroffer", a.LastCounter.Describe());
            if (a.LastLine.Length > 0) Ui.Label("fund-quote", offer, $"\"{a.LastLine}\"");
            if (a.Status == ApplicantStatus.Open)
            {
                EmployeePanel.NegotiationForm(offer, a.LastCounter ?? a.Asking, c =>
                {
                    Response r = Fund.Offer(a, c);
                    if (r.Answer == Answer.Accept)
                    {
                        Say($"{p.Name}: \"{r.Line}\" Hired on {c.Describe()}.", false);
                        Browser.Navigate(Host + "/employee/" + p.Id);
                        return;
                    }
                    Act(r.Answer == Answer.Withdraw ? $"{p.Name}: \"{r.Line}\"" : null, $"{p.Name}: \"{r.Line}\"");
                }, "SEND OFFER");
                if (a.LastCounter != null)
                {
                    Contract counter = a.LastCounter;
                    FundUi.Collect("ACCEPT THEIR COUNTER", () =>
                    {
                        Response r = Fund.Offer(a, counter);
                        if (r.Answer == Answer.Accept) Browser.Navigate(Host + "/employee/" + p.Id);
                        else Act(null, r.Line);
                    }, offer, "accept-counter");
                }
                FundUi.Danger("DECLINE APPLICATION", () => { Fund.Decline(a); Browser.Navigate(Host + "/recruiting"); }, offer, "decline");
                Ui.Label("fund-note", offer, "Offers below what they'll accept may get a counter; repeated low offers end the talks.");
            }
            else Ui.Label("fund-muted", offer, "This application is closed.");
            _signature = () => a.Status.ToString() + a.Rounds;
        }

        // ------------------------------------------------------------------ employees

        private void EmployeesPage()
        {
            Title("Employees");
            var staff = Fund.Staff.ToList();
            VisualElement table = FundUi.Card(_page, $"Team ({staff.Count})");
            if (staff.Count == 0) Ui.Label("fund-muted", table, "Nobody yet. Hire from Recruitment.");
            else FundUi.Row(table, "fund-row-head", ("NAME", 0), ("STATUS", 240), ("DESK", 110), ("CAPITAL", 110), ("TODAY", 100), ("TO COLLECT", 150), ("", 44));
            foreach (Employee e in staff) EmployeeRow(table, e);

            var former = Fund.Employees.Where(e => e.Former).ToList();
            if (former.Count > 0)
            {
                VisualElement past = FundUi.Card(_page, "Former employees");
                foreach (Employee e in former)
                    FundUi.Line(past, e.Name, $"{e.LeftReason} · {new DateTime(e.LeftOn):MMM d} · net {FundUi.Signed(e.NetRealized)}");
            }
            _signature = () => string.Join(",", Fund.Employees.Select(e => e.Id + (e.Former ? "f" : "")));
        }

        private void EmployeeRow(VisualElement table, Employee e)
        {
            var row = Ui.Box("fund-row fund-row-emp", table);
            row.name = "employee-row-" + e.Id;
            var who = Ui.Box("fund-cell fund-who", row);
            who.style.flexGrow = 1;
            FundUi.Avatar(who, e.Person, 28f);
            Button name = Ui.Button(e.Name, () => Browser.Navigate(Host + "/employee/" + e.Id), "fund-link", who, "open-" + e.Id);
            Label status = FundUi.Pill(Ui.Box("fund-cell", row), "", "muted");
            status.parent.style.width = 240;
            Label desk = Ui.Label("fund-cell", row); desk.style.width = 110;
            Label capital = Ui.Label("fund-cell", row); capital.style.width = 110;
            Label today = Ui.Label("fund-cell", row); today.style.width = 100;
            var collectCell = Ui.Box("fund-cell", row);
            collectCell.style.width = 150;
            Button collect = FundUi.Collect("", () =>
            {
                decimal got = Fund.Collect(e, out string error);
                Act(error, $"Collected {FundUi.Money(got)} from {e.Person.First} into operating cash.");
            }, collectCell, "collect-" + e.Id);
            var menuCell = Ui.Box("fund-cell", row);
            menuCell.style.width = 44;
            Button dots = Ui.Button("⋯", null, "fund-btn fund-dots", menuCell, "menu-" + e.Id);
            var menu = Ui.Box("fund-menu", table);
            Ui.Show(menu, false);
            dots.clicked += () => Ui.Show(menu, menu.style.display == DisplayStyle.None);
            void Item(string text, Action act, string id) => Ui.Button(text, () => { Ui.Show(menu, false); act(); }, "fund-menu-item", menu, id + "-" + e.Id);
            Item("View profile", () => Browser.Navigate(Host + "/employee/" + e.Id), "mi-profile");
            // Support staff don't trade: no workstation, capital, training or trading pause for them.
            if (!e.IsAdmin)
            {
                Item("Assign workstation", () => Act(Fund.AutoAssign(e), $"{e.Person.First} has a workstation."), "mi-assign");
                Item("Adjust trading allocation", () => Browser.Navigate(Host + "/employee/" + e.Id + "/permissions"), "mi-capital");
                Item("Start training", () => Browser.Navigate(Host + "/employee/" + e.Id + "/training"), "mi-training");
                Item("Negotiate compensation", () => Browser.Navigate(Host + "/employee/" + e.Id + "/contract"), "mi-contract");
                Item(e.Policy.Authorized ? "Pause trading" : "Resume trading", () =>
                {
                    e.Policy.Authorized = !e.Policy.Authorized;
                    Act(null, e.Policy.Authorized ? $"{e.Person.First} may trade again." : $"{e.Person.First}'s trading is paused.");
                }, "mi-pause");
            }
            if (e.IsAdmin) collect.style.display = DisplayStyle.None;
            var letGo = Ui.Box("fund-menu-danger", menu);
            Label exit = Ui.Label("fund-note fund-wrap", letGo, "");
            FundUi.Armed("Let go…", $"Confirm: let {e.Name} go", () =>
            {
                Fund.LetGo(e);
                Act(null, $"{e.Name} has been let go.");
            }, letGo, "fund-btn-danger", "mi-letgo-" + e.Id);

            _live.Add(() =>
            {
                var (text, kind) = FundUi.Status(Fund, e);
                if (e.Activity == Activity.WaitingForWorkstation) text += ": " + Fund.StationProblem(e);
                FundUi.SetPill(status, text, kind);
                Ui.SetText(desk, e.IsAdmin ? "Reception" : e.Desk == 0 ? "—" : "#" + e.Desk + (Fund.StationOf(e) == null ? " (incomplete)" : ""));
                Ui.SetText(capital, FundUi.Money0(e.Base));
                FundUi.SetSigned(today, e.Today != null ? e.Today.Realized - e.Today.Fees + e.Unrealized : e.Unrealized);
                decimal c = e.Collectible;
                Ui.SetText(collect, c > 0m ? $"Collect {FundUi.Money0(c)}" : "Collect $0");
                collect.SetEnabled(c > 0m);
                collect.tooltip = c > 0m ? "" : e.CollectBlocker;
                Ui.SetText(exit, Fund.ExitSummary(e));
            });
        }

        private EmployeePanel _panel;

        private void EmployeePage(long id)
        {
            Employee e = Fund.Find(id);
            if (e == null)
            {
                Title("Employee not found");
                return;
            }
            string[] parts = _path.Split('/');
            FundUi.Secondary("← ALL EMPLOYEES", () => Browser.Navigate(Host + "/employees"), _page, "back-employees");
            // A rebuild (new training, new contract) keeps the tab you were on.
            string tab = _panel != null && _panel.Employee == e ? _panel.Tab : parts.Length > 2 ? parts[2] : "overview";
            _panel = new EmployeePanel(Game, e, _page, Say, tab);
            _live.Add(() => _panel.Refresh());
            _signature = () => e.Former + "|" + e.Training.Count + "|" + e.Contract.Version + "|" + e.Desk;
        }
    }
}
