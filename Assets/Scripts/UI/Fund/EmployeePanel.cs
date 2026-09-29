using System;
using System.Collections.Generic;
using System.Linq;
using OpeningBell.Fund;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine.UIElements;
using Position = OpeningBell.Trading.Position;

namespace OpeningBell.UI
{
    /// <summary>A person's profile: who they are, what they're good at, their style (applicants and staff alike).</summary>
    internal static class PersonView
    {
        public static void Header(VisualElement parent, Person p, string subtitle)
        {
            var head = Ui.Box("fund-hrow fund-person-head", parent);
            FundUi.Avatar(head, p, 48f);
            var titles = Ui.Box("fund-titles", head);
            Ui.Label("fund-h2", titles, p.Name);
            Ui.Label("fund-muted", titles, subtitle);
        }

        public static void Facts(VisualElement parent, Person p)
        {
            FundUi.Line(parent, "Experience", $"{p.Years} year{(p.Years == 1 ? "" : "s")} · {p.Seniority}");
            FundUi.Line(parent, "Specialty", Person.SectorName(p.Specialty));
            FundUi.Line(parent, "Strategy", Person.StrategyName(p.Strategy));
            FundUi.Line(parent, "Typical trades a day", p.TradesPerDay.ToString("0.#", FundUi.C));
            FundUi.Line(parent, "Win rate (historical estimate)", FundUi.Pct(p.PastWinRate));
            FundUi.Line(parent, "Reward:risk (historical estimate)", p.PastRewardRisk.ToString("0.00", FundUi.C) + " : 1");
            FundUi.Line(parent, "Estimated win rate, reference conditions", FundUi.Pct(PeopleFactory.EstimatedWinRate(p)));
            Ui.Label("fund-note", parent, "Estimates describe past results and a model of their skill, not a promise: live results depend on the market, their strategy's fit and risk.");
        }

        /// <summary>Skill bars with their ceilings.</summary>
        public static void Skills(VisualElement parent, Person p, Func<double>[] live = null)
        {
            for (int i = 0; i < Person.SkillCount; i++)
            {
                var s = (Skill)i;
                var row = Ui.Box("fund-skill", parent);
                Ui.Label("fund-skill-name", row, Person.SkillName(s));
                var bar = Ui.Box("fund-bar", row);
                var cap = Ui.Box("fund-bar-cap", bar);
                var fill = Ui.Box("fund-bar-fill", bar);
                Label value = Ui.Label("fund-skill-value", row);
                int k = i;
                void Draw()
                {
                    cap.style.width = UnityEngine.UIElements.Length.Percent((float)p.Caps[k]);
                    fill.style.width = UnityEngine.UIElements.Length.Percent((float)p.Skills[k]);
                    Ui.SetText(value, p.Skills[k].ToString("0", FundUi.C));
                }
                Draw();
                bar.schedule.Execute(Draw).Every(1000);
            }
        }

        public static void Traits(VisualElement parent, Person p)
        {
            string Word(double v, string low, string high) => v < 35 ? low : v > 65 ? high : "balanced";
            FundUi.Line(parent, "Ambition", Word(p.Trait(Trait.Ambition), "content", "ambitious"));
            FundUi.Line(parent, "Loyalty", Word(p.Trait(Trait.Loyalty), "restless", "loyal"));
            FundUi.Line(parent, "Sociability", Word(p.Trait(Trait.Sociability), "private", "sociable"));
            FundUi.Line(parent, "Composure", Word(p.Trait(Trait.Composure), "excitable", "calm under pressure"));
            FundUi.Line(parent, "Greed", Word(p.Trait(Trait.Greed), "modest", "pushes for more"));
            FundUi.Line(parent, "Caution", Word(p.Trait(Trait.Caution), "bold", "cautious"));
        }
    }

    /// <summary>
    /// Everything about one employee in one place (FUND_SPEC §14): status and desk, capital and positions, today's and past
    /// results, skills and mood, training, contract and negotiation, permissions and limits, employment actions. Built
    /// into any container: the Ledgerline profile page and the in-world panel ([E] on the person) use the same one.
    /// Every upgrade bought here is for this employee only.
    /// </summary>
    internal sealed class EmployeePanel
    {
        private readonly GameBootstrap _game;
        private readonly Employee _e;
        private readonly VisualElement _root, _body;
        private readonly Action<string, bool> _say;
        private readonly List<Action> _live = new List<Action>();
        private readonly Dictionary<string, Button> _tabs = new Dictionary<string, Button>();
        private string _tab = "overview";

        private HedgeFund Fund => _game.Fund;

        public VisualElement Root => _root;
        public Employee Employee => _e;
        public string Tab => _tab;

        public EmployeePanel(GameBootstrap game, Employee e, VisualElement parent, Action<string, bool> say, string tab = "overview")
        {
            _game = game;
            _e = e;
            _say = say;
            _tab = tab;
            _root = Ui.Box("fund-employee", parent);
            _root.name = "employee-panel-" + e.Id;
            var head = Ui.Box("fund-emp-head", _root);
            FundUi.Avatar(head, e.Person, 52f);
            var titles = Ui.Box("fund-titles", head);
            Ui.Label("fund-h2", titles, e.Name);
            Ui.Label("fund-muted", titles, $"{e.Person.Seniority} · {Person.StrategyName(e.Person.Strategy)} · {Person.SectorName(e.Person.Specialty)}");
            Ui.Box("spacer", head);
            var status = FundUi.Pill(head, "", "muted");
            status.name = "employee-status";
            _live.Add(() =>
            {
                var (text, kind) = FundUi.Status(Fund, e);
                FundUi.SetPill(status, text, kind);
            });
            var tabs = Ui.Box("fund-tabs", _root);
            foreach (var (id, title) in new[] { ("overview", "Overview"), ("trading", "Trading"), ("skills", "Skills"), ("training", "Training"),
                         ("contract", "Contract"), ("permissions", "Permissions"), ("history", "History") })
            {
                string t = id;
                _tabs[id] = Ui.Button(title, () => { _tab = t; Build(); }, "fund-tab", tabs, "emp-tab-" + id);
            }
            _body = Ui.Box("fund-emp-body", _root);
            Build();
        }

        public void Refresh()
        {
            foreach (Action a in _live) a();
        }

        private void Act(string error, string success)
        {
            _say(error ?? success ?? "", error != null);
            Build();
        }

        private void Build()
        {
            _body.Clear();
            _live.RemoveRange(1, Math.Max(0, _live.Count - 1)); // keep the status pill
            foreach (var kv in _tabs) kv.Value.EnableInClassList("active", kv.Key == _tab);
            switch (_tab)
            {
                case "trading": Trading(); break;
                case "skills": SkillsTab(); break;
                case "training": TrainingTab(); break;
                case "contract": ContractTab(); break;
                case "permissions": Permissions(); break;
                case "history": History(); break;
                default: Overview(); break;
            }
            Refresh();
        }

        // ------------------------------------------------------------------ overview

        private void Overview()
        {
            Employee e = _e;
            var cols = Ui.Box("fund-cols", _body);
            VisualElement now = FundUi.Card(cols, "Right now", "fund-grow");
            Label activity = FundUi.Line(now, "Activity");
            Label desk = FundUi.Line(now, "Workstation");
            Label mood = FundUi.Line(now, "Satisfaction");
            Label fatigue = FundUi.Line(now, "Fatigue / stress");
            Label training = FundUi.Line(now, "Training");
            var deskRow = Ui.Box("fund-actions", now);
            FundUi.Secondary("AUTO-ASSIGN DESK", () => Act(Fund.AutoAssign(e), "Workstation assigned."), deskRow, "emp-autoassign");
            Button pause = FundUi.Secondary("", () =>
            {
                e.Policy.Authorized = !e.Policy.Authorized;
                Act(null, e.Policy.Authorized ? $"{e.Person.First} may trade again." : $"{e.Person.First}'s trading is paused. Open brackets keep working.");
            }, deskRow, "emp-pause");

            VisualElement money = FundUi.Card(cols, "Desk", "fund-side");
            Label capital = FundUi.Line(money, "Trading capital");
            Label realized = FundUi.Line(money, "Realized P&L (since hire)");
            Label unreal = FundUi.Line(money, "Unrealized P&L");
            Label accrued = FundUi.Line(money, "Commission accrued");
            Label todayLine = FundUi.Line(money, "Today (after costs)");
            Label collectNote = Ui.Label("fund-note", money);
            Button collect = FundUi.Collect("", () =>
            {
                decimal got = Fund.Collect(e, out string error);
                Act(error, $"Collected {FundUi.Money(got)} from {e.Person.First}'s desk into operating cash.");
            }, money, "emp-collect");

            VisualElement positions = FundUi.Card(_body, "Positions and working orders");
            var posList = Ui.Box("", positions);
            string posSig = "";

            _live.Add(() =>
            {
                var (text, _) = FundUi.Status(Fund, e);
                string reason = e.Activity == Activity.RiskLocked ? " · " + e.LockReason
                    : (e.Activity == Activity.Trading || e.Activity == Activity.Preparing) && e.Base <= 0m && e.DeskEquity <= 0m ? " · no capital allocated" : "";
                Ui.SetText(activity, text + reason);
                Workstation w = Fund.StationOf(e);
                Ui.SetText(desk, e.Desk == 0 ? "None assigned" : w != null ? $"Desk #{e.Desk} · quality {w.Quality * 100:0}%" : $"Desk #{e.Desk} · {Fund.StationProblem(e)}");
                Ui.SetText(mood, $"{e.Satisfaction:0}/100" + (e.ResignationNotice > 0 ? $" · resigning in {e.ResignationNotice} days" : e.RaiseRequested ? " · asked for a raise" : ""));
                mood.EnableInClassList("fund-down", e.Satisfaction < 40);
                Ui.SetText(fatigue, $"{e.Fatigue * 100:0}% / {e.Stress * 100:0}%");
                Ui.SetText(training, Fund.CurrentTrainingLabel(e) ?? "None");
                Ui.SetText(pause, e.Policy.Authorized ? "PAUSE TRADING" : "RESUME TRADING");
                Ui.SetText(capital, $"{FundUi.Money(e.Base)} allocated · {FundUi.Money(e.DeskEquity)} equity");
                FundUi.SetSigned(realized, e.NetRealized);
                FundUi.SetSigned(unreal, e.Unrealized);
                Ui.SetText(accrued, FundUi.Money(e.CommissionAccrued + e.CommissionOverdue));
                decimal day = e.Today != null ? e.Today.Realized - e.Today.Fees : 0m;
                FundUi.SetSigned(todayLine, day);
                decimal c = e.Collectible;
                Ui.SetText(collect, c > 0m ? $"COLLECT {FundUi.Money(c)}" : "COLLECT $0.00");
                collect.SetEnabled(c > 0m);
                Ui.SetText(collectNote, c > 0m ? "Moves realized profit above the allocated capital to operating cash. Equity doesn't change." : e.CollectBlocker ?? "");

                string sig = string.Join(",", e.Account.Portfolio.Positions.Where(p => p.IsOpen).Select(p => p.Ticker + p.Quantity)) + "|" + e.Orders.OpenOrders.Count;
                if (sig != posSig)
                {
                    posSig = sig;
                    posList.Clear();
                    FundUi.Row(posList, "fund-row-head", ("SYMBOL", 90), ("SIDE", 70), ("CONTRACTS", 90), ("AVG PRICE", 100), ("OPEN P&L", 110), ("ORDERS", 0));
                    foreach (Position p in e.Account.Portfolio.Positions)
                    {
                        if (!p.IsOpen) continue;
                        string orders = string.Join(", ", e.Orders.OpenOrders.Where(o => o.Ticker == p.Ticker).Select(o => (o.IsStop ? "stop " + o.StopPrice.ToString("0.00", FundUi.C) : "target " + o.LimitPrice.ToString("0.00", FundUi.C))));
                        var cells = FundUi.Row(posList, "", (p.Ticker, 90), (p.Quantity > 0 ? "Long" : "Short", 70), (Math.Abs(p.Quantity).ToString(FundUi.C), 90),
                            (p.AveragePrice.ToString("0.00", FundUi.C), 100), ("", 110), (orders.Length > 0 ? orders : "unprotected", 0));
                        Label pl = cells[4];
                        string ticker = p.Ticker;
                        _live.Add(() => FundUi.SetSigned(pl, e.Account.Portfolio.Find(ticker)?.UnrealizedPnL(e.Account.MarkPrice(ticker)) ?? 0m));
                    }
                    if (posList.childCount == 1) Ui.Label("fund-muted", posList, "Flat.");
                }
            });
        }

        // ------------------------------------------------------------------ trading record

        private void Trading()
        {
            Employee e = _e;
            List<TradeRecord> closed = e.Closed();
            var cols = Ui.Box("fund-cols", _body);
            VisualElement stats = FundUi.Card(cols, "Recorded performance", "fund-grow");
            Stats(stats, closed, "all trades since hire");
            Stats(stats, closed.Skip(Math.Max(0, closed.Count - 20)).ToList(), "last 20 trades");
            VisualElement est = FundUi.Card(cols, "Skill and estimates", "fund-side");
            FundUi.Line(est, "Estimated win rate, reference conditions", FundUi.Pct(PeopleFactory.EstimatedWinRate(e.Person)));
            FundUi.Line(est, "Strategy target reward:risk", TargetRr(e.Person.Strategy));
            Ui.Label("fund-note", est, "The estimate models their skill in average conditions. Recorded results are what actually happened: the market, fit and discipline all count. A high win rate can still lose money if the losses are bigger than the wins.");

            VisualElement notes = FundUi.Card(_body, "Behaviour and risk events");
            int n = 0;
            for (int i = e.Behavior.Count - 1; i >= 0 && n < 12; i--, n++)
            {
                BehaviorNote b = e.Behavior[i];
                var row = Ui.Box("fund-alert", notes);
                FundUi.Pill(row, b.Limit ? "LIMIT" : "BEHAVIOUR", b.Limit ? "bad" : "warn");
                Ui.Label("fund-alert-body", row, b.Text);
                Ui.Label("fund-muted fund-alert-time", row, new DateTime(b.Time).ToString("ddd h:mm tt", FundUi.C));
            }
            if (n == 0) Ui.Label("fund-muted", notes, "Nothing unusual yet.");

            VisualElement list = FundUi.Card(_body, "Trade history");
            FundUi.Row(list, "fund-row-head", ("CLOSED", 110), ("SYMBOL", 70), ("SIDE", 55), ("QTY", 45), ("ENTRY", 75), ("EXIT", 75), ("FEES", 70), ("NET", 95), ("R", 55), ("NOTES", 0));
            int shown = 0;
            for (int i = closed.Count - 1; i >= 0 && shown < 40; i--, shown++)
            {
                TradeRecord t = closed[i];
                var cells = FundUi.Row(list, "", (new DateTime(t.Closed).ToString("MMM d h:mm tt", FundUi.C), 110), (t.Ticker, 70), (t.Side > 0 ? "Long" : "Short", 55),
                    (t.Contracts.ToString(FundUi.C), 45), (t.Entry.ToString("0.00", FundUi.C), 75), (t.Exit.ToString("0.00", FundUi.C), 75), (FundUi.Money(t.Fees), 70),
                    ("", 95), (t.Risk > 0m ? t.R.ToString("+0.0;-0.0", FundUi.C) : "—", 55),
                    (Person.StrategyName(t.Strategy) + (t.FollowedPlan ? "" : " · off plan") + (t.Notes.Length > 0 ? " · " + t.Notes : ""), 0));
                FundUi.SetSigned(cells[7], t.Net);
            }
            if (shown == 0) Ui.Label("fund-muted", list, "No completed trades yet.");
        }

        private static string TargetRr(Strategy s) => s switch
        {
            Strategy.Reversion => "to VWAP (~1:1)",
            Strategy.Scalping => "1.3 : 1",
            Strategy.TrendPullback => "2.2 : 1",
            _ => "2 : 1",
        };

        public static void Stats(VisualElement parent, List<TradeRecord> trades, string sample)
        {
            Ui.Label("fund-label", parent, $"Sample: {sample} ({trades.Count})");
            if (trades.Count == 0)
            {
                Ui.Label("fund-muted", parent, "No completed trades in this sample.");
                return;
            }
            var wins = trades.Where(t => t.Net > 0m).ToList();
            var losses = trades.Where(t => t.Net < 0m).ToList();
            decimal net = trades.Sum(t => t.Net), fees = trades.Sum(t => t.Fees);
            decimal avgWin = wins.Count > 0 ? wins.Average(t => t.Net) : 0m, avgLoss = losses.Count > 0 ? losses.Average(t => t.Net) : 0m;
            var withRisk = trades.Where(t => t.Risk > 0m).ToList();
            double avgR = withRisk.Count > 0 ? withRisk.Average(t => t.R) : 0;
            // Maximum drawdown of the cumulative result through the sample.
            decimal peak = 0m, run = 0m, dd = 0m;
            foreach (TradeRecord t in trades)
            {
                run += t.Net;
                peak = Math.Max(peak, run);
                dd = Math.Max(dd, peak - run);
            }
            int followed = trades.Count(t => t.FollowedPlan);
            var grid = Ui.Box("fund-minis", parent);
            void Mini(string cap, string val, decimal sign = 0m)
            {
                var (v, _) = FundUi.Kpi(grid, cap, "fund-kpi-mini");
                if (sign != 0m) FundUi.SetSigned(v, sign, val); else Ui.SetText(v, val);
            }
            Mini("Win rate", FundUi.Pct(wins.Count / (double)trades.Count));
            Mini("Net result", FundUi.Signed(net), net);
            Mini("Average win", FundUi.Money(avgWin));
            Mini("Average loss", FundUi.Money(avgLoss));
            Mini("Win / loss size", avgLoss != 0m ? (avgWin / Math.Abs(avgLoss)).ToString("0.00", FundUi.C) : "—");
            Mini("Average R", avgR.ToString("+0.00;-0.00", FundUi.C));
            Mini("Max drawdown", FundUi.Money(dd));
            Mini("Fees and costs", FundUi.Money(fees));
            Mini("Followed their plan", FundUi.Pct(followed / (double)trades.Count));
        }

        // ------------------------------------------------------------------ skills

        private void SkillsTab()
        {
            var cols = Ui.Box("fund-cols", _body);
            VisualElement skills = FundUi.Card(cols, "Skills (bar) and ceiling (shade)", "fund-grow");
            PersonView.Skills(skills, _e.Person);
            VisualElement side = FundUi.Card(cols, "Personality", "fund-side");
            PersonView.Traits(side, _e.Person);
            VisualElement mentor = FundUi.Card(side, "Mentoring");
            Employee m = Fund.Find(_e.MentorOf);
            Ui.Label("fund-muted fund-wrap", mentor, m != null ? $"Mentored by {m.Name}: slow gains in analysis and self-control." : "No mentor. Seniors with leadership can mentor juniors.");
            var drop = new DropdownField();
            var seniors = Fund.Staff.Where(s => s != _e && s.Person.Years > _e.Person.Years).ToList();
            drop.choices = new List<string> { "No mentor" };
            drop.choices.AddRange(seniors.Select(s => $"{s.Name} ({Fund.MentorSlots(s)} slot{(Fund.MentorSlots(s) == 1 ? "" : "s")})"));
            drop.index = m != null ? 1 + seniors.IndexOf(m) : 0;
            drop.AddToClassList("fund-drop");
            drop.RegisterValueChangedCallback(ev =>
            {
                int i = drop.index;
                Act(Fund.SetMentor(_e, i <= 0 ? null : seniors[i - 1]), i <= 0 ? "Mentor removed." : $"{seniors[i - 1].Name} now mentors {_e.Person.First}.");
            });
            mentor.Add(drop);
        }

        // ------------------------------------------------------------------ training

        private void TrainingTab()
        {
            Employee e = _e;
            VisualElement now = FundUi.Card(_body, "Current training");
            Label current = Ui.Label("fund-strong", now);
            var bar = Ui.Box("fund-bar", now);
            var fill = Ui.Box("fund-bar-fill", bar);
            _live.Add(() =>
            {
                TrainingJob j = e.CurrentTraining;
                Ui.SetText(current, j == null ? "No course in progress." : $"{Person.SkillName(j.Skill)} level {j.Level}: {Fund.CurrentTrainingLabel(e)}");
                fill.style.width = UnityEngine.UIElements.Length.Percent(j == null ? 0 : (float)(j.MinutesDone / Math.Max(1, j.Minutes) * 100));
            });
            VisualElement list = FundUi.Card(_body, $"Courses for {e.Person.First} (charged to the company, for this employee only)");
            foreach (Course c in TrainingCatalog.Courses)
            {
                CourseOffer o = Fund.OfferCourse(e, c.Skill);
                var row = Ui.Box("fund-course", list);
                var text = Ui.Box("fund-course-text", row);
                Ui.Label("fund-strong", text, $"{Person.SkillName(c.Skill)} · level {Math.Min(o.Level, c.MaxLevel)} of {c.MaxLevel}");
                Ui.Label("fund-muted fund-wrap", text, c.Description);
                Ui.Label("fund-note", text, c.Effect);
                var buy = Ui.Box("fund-course-buy", row);
                if (o.Level <= c.MaxLevel)
                {
                    Ui.Label("fund-strong", buy, FundUi.Money0(o.Price));
                    Ui.Label("fund-muted", buy, $"{o.Minutes} in-game minutes");
                    Ui.Label("fund-muted", buy, $"+{o.Gain:0} {Person.SkillName(c.Skill).ToLowerInvariant()} (now {e.Person[c.Skill]:0})");
                    if (c.Skill == Skill.Analysis && o.Gain > 0)
                        Ui.Label("fund-muted", buy, $"≈ +{o.Gain * 0.33:0.0} pts estimated win rate");
                    Skill skill = c.Skill;
                    Button enroll = FundUi.Armed("ENROLL", $"CONFIRM {FundUi.Money0(o.Price)}", () => Act(Fund.BuyTraining(e, skill),
                        $"{e.Person.First} is enrolled in {Person.SkillName(skill).ToLowerInvariant()}. They'll study at their desk when they're free."), buy, "fund-btn-primary", "enroll-" + skill.ToString().ToLowerInvariant());
                    enroll.SetEnabled(o.Blocker == null);
                    if (o.Blocker != null) Ui.Label("fund-note fund-down", buy, o.Blocker);
                }
                else Ui.Label("fund-muted", buy, "Completed");
            }
            VisualElement past = FundUi.Card(_body, "Training history");
            foreach (TrainingJob j in e.Training)
                FundUi.Line(past, $"{Person.SkillName(j.Skill)} level {j.Level} ({FundUi.Money0(j.Price)})",
                    j.Done ? $"completed {new DateTime(j.Finished):MMM d}, +{j.Gain:0}" : j.Started != 0 ? "in progress" : "queued");
            if (e.Training.Count == 0) Ui.Label("fund-muted", past, "None yet.");
        }

        // ------------------------------------------------------------------ contract

        private void ContractTab()
        {
            Employee e = _e;
            var cols = Ui.Box("fund-cols", _body);
            VisualElement terms = FundUi.Card(cols, "Signed contract (locked)", "fund-grow");
            FundUi.Line(terms, "Pay", e.Contract.Describe());
            FundUi.Line(terms, "Signed", $"{new DateTime(e.Contract.Signed):MMM d, yyyy} · version {e.Contract.Version}");
            FundUi.Line(terms, "Employer", Fund.Name);
            FundUi.Line(terms, "Paid", $"weekly, {Fund.Config.PayDay}s after the close");
            if (e.Contract.PaysCommission)
            {
                Ui.Label("fund-label", terms, "How commission works");
                Ui.Label("fund-muted fund-wrap", terms, $"{e.Contract.CommissionRate * 100:0.#}% of new net trading profit: realized results after execution costs, counted only above the best level reached so far (the high-water mark). Losses must be earned back before any commission is due: losing $5,000 and making $5,000 back earns none.");
                Label hw = FundUi.Line(terms, "Net realized since hire");
                Label mark = FundUi.Line(terms, "High-water mark");
                Label due = FundUi.Line(terms, "Accrued, due on payday");
                _live.Add(() =>
                {
                    FundUi.SetSigned(hw, e.NetRealized);
                    Ui.SetText(mark, FundUi.Money(e.HighWater));
                    Ui.SetText(due, FundUi.Money(e.CommissionAccrued + e.CommissionOverdue));
                });
            }
            Label wages = FundUi.Line(terms, "Wages accrued this week");
            Label paid = FundUi.Line(terms, "Paid to date");
            _live.Add(() =>
            {
                Ui.SetText(wages, FundUi.Money(e.WagesAccrued + e.WagesOverdue) + (e.WagesOverdue > 0m ? $" ({FundUi.Money(e.WagesOverdue)} overdue)" : ""));
                Ui.SetText(paid, $"{FundUi.Money(e.TotalWagesPaid)} wages · {FundUi.Money(e.TotalCommissionPaid)} commission");
            });

            VisualElement nego = FundUi.Card(cols, "Propose new terms", "fund-side");
            NegotiationForm(nego, e.Contract, offer =>
            {
                Response r = Fund.Renegotiate(e, offer);
                Act(null, $"{e.Person.First}: \"{r.Line}\"" + (r.Answer == Answer.Accept ? " New terms signed." : " Terms unchanged."));
            }, "SEND PROPOSAL");
            Ui.Label("fund-note", nego, "Nothing changes unless they agree. Cutting pay usually fails, and asking costs goodwill.");
        }

        /// <summary>Structure, hourly and commission fields with a send button (applicants and staff).</summary>
        public static void NegotiationForm(VisualElement parent, Contract start, Action<Contract> send, string sendText)
        {
            var drop = new DropdownField(new List<string> { "Hourly", "Commission", "Hourly + commission" }, (int)start.Structure);
            drop.AddToClassList("fund-drop");
            drop.name = "offer-structure";
            parent.Add(drop);
            Ui.Label("fund-label", parent, "Hourly pay ($/h)");
            TextField hourly = FundUi.Field(parent, start.Hourly.ToString("0.00", FundUi.C), "offer-hourly", ".", 120f);
            Ui.Label("fund-label", parent, "Commission (% of new net profit)");
            TextField rate = FundUi.Field(parent, (start.CommissionRate * 100m).ToString("0.#", FundUi.C), "offer-rate", ".", 120f);
            Button go = FundUi.Primary(sendText, null, parent, "offer-send");
            go.clicked += () =>
            {
                FundUi.TryMoney(hourly.value, out decimal h);
                FundUi.TryMoney(rate.value, out decimal r);
                send(Contract.Of((PayStructure)Math.Max(0, drop.index), h, r / 100m));
            };
        }

        // ------------------------------------------------------------------ permissions

        private void Permissions()
        {
            Employee e = _e;
            RiskPolicy pol = e.Policy;
            var cols = Ui.Box("fund-cols", _body);
            VisualElement hard = FundUi.Card(cols, "Hard limits (enforced by the system)", "fund-grow");
            Ui.Label("fund-label", hard, "Trading capital ($)");
            TextField capital = FundUi.Field(hard, e.Base.ToString("0", FundUi.C), "perm-capital", ".,$");
            Ui.Label("fund-label", hard, "Max contracts per position");
            TextField contracts = FundUi.Field(hard, pol.MaxContracts.ToString(FundUi.C), "perm-contracts");
            Ui.Label("fund-label", hard, "Max risk per trade ($)");
            TextField risk = FundUi.Field(hard, pol.MaxRiskPerTrade.ToString("0", FundUi.C), "perm-risk", ".,$");
            Ui.Label("fund-label", hard, "Max daily loss ($, locks the desk for the day)");
            TextField daily = FundUi.Field(hard, pol.MaxDailyLoss.ToString("0", FundUi.C), "perm-daily", ".,$");
            Ui.Label("fund-label", hard, "Max open positions");
            TextField positions = FundUi.Field(hard, pol.MaxPositions.ToString(FundUi.C), "perm-positions");
            var auth = new Toggle("Authorized to trade") { value = pol.Authorized, name = "perm-authorized" };
            auth.AddToClassList("fund-toggle");
            hard.Add(auth);
            Ui.Label("fund-note", hard, "Overnight positions: not offered. Contracts settle flat at the 4:00 PM close.");
            FundUi.Primary("SAVE LIMITS", () =>
            {
                string error = null;
                if (FundUi.TryMoney(capital.value, out decimal cap) && cap != e.Base) error = Fund.Allocate(e, cap);
                if (int.TryParse(contracts.value, out int mc) && mc >= 0) pol.MaxContracts = mc;
                if (FundUi.TryMoney(risk.value, out decimal mr) && mr > 0m) pol.MaxRiskPerTrade = mr;
                if (FundUi.TryMoney(daily.value, out decimal md) && md > 0m) pol.MaxDailyLoss = md;
                if (int.TryParse(positions.value, out int mp) && mp >= 1) pol.MaxPositions = mp;
                pol.Authorized = auth.value;
                Act(error, "Limits saved.");
            }, hard, "perm-save");

            VisualElement soft = FundUi.Card(cols, "Instruments, strategies and preferences", "fund-side");
            Ui.Label("fund-label", soft, "Approved instruments (none ticked = all)");
            var syms = Ui.Box("fund-checks", soft);
            foreach (SecurityRuntimeState s in _game.Market.Securities)
            {
                string t = s.Ticker;
                var box = new Toggle(t) { value = pol.Instruments.Contains(t), name = "perm-sym-" + t };
                box.AddToClassList("fund-toggle");
                box.RegisterValueChangedCallback(ev => { if (ev.newValue) { if (!pol.Instruments.Contains(t)) pol.Instruments.Add(t); } else pol.Instruments.Remove(t); });
                syms.Add(box);
            }
            Ui.Label("fund-label", soft, "Approved strategies (their own is always allowed)");
            var strats = Ui.Box("fund-checks", soft);
            foreach (Strategy s in Enum.GetValues(typeof(Strategy)))
            {
                if (s == e.Person.Strategy) continue;
                Strategy st = s;
                var box = new Toggle(Person.StrategyName(s)) { value = pol.Strategies.Contains(s) };
                box.AddToClassList("fund-toggle");
                box.RegisterValueChangedCallback(ev => { if (ev.newValue) { if (!pol.Strategies.Contains(st)) pol.Strategies.Add(st); } else pol.Strategies.Remove(st); });
                strats.Add(box);
            }
            Ui.Label("fund-label", soft, "Soft preferences (followed as well as their self-control allows)");
            Ui.Label("fund-label", soft, "Preferred max trades a day (0 = none)");
            TextField pref = FundUi.Field(soft, pol.PreferredMaxTrades.ToString(FundUi.C), "perm-pref-trades");
            pref.RegisterValueChangedCallback(ev => { if (int.TryParse(ev.newValue, out int v) && v >= 0) pol.PreferredMaxTrades = v; });
            var quality = new Toggle("Only clearly good setups") { value = pol.PreferQuality };
            quality.AddToClassList("fund-toggle");
            quality.RegisterValueChangedCallback(ev => pol.PreferQuality = ev.newValue);
            soft.Add(quality);
        }

        // ------------------------------------------------------------------ history and employment

        private void History()
        {
            Employee e = _e;
            var cols = Ui.Box("fund-cols", _body);
            VisualElement mood = FundUi.Card(cols, "What's moving their satisfaction", "fund-grow");
            var sums = new Dictionary<string, double>();
            foreach (MoodReason r in e.Mood)
                if ((_game.Market.Now - new DateTime(r.Time)).TotalDays < 20) sums[r.Factor] = (sums.TryGetValue(r.Factor, out double v) ? v : 0) + r.Delta;
            foreach (var kv in sums.OrderBy(kv => kv.Value))
            {
                Label l = FundUi.Line(mood, kv.Key, kv.Value.ToString("+0.0;-0.0", FundUi.C));
                l.EnableInClassList("fund-down", kv.Value < 0);
                l.EnableInClassList("fund-up", kv.Value > 0);
            }
            if (sums.Count == 0) Ui.Label("fund-muted", mood, "Nothing yet (it builds up day by day).");

            VisualElement life = FundUi.Card(cols, "With the company", "fund-side");
            for (int i = e.Person.History.Count - 1, n = 0; i >= 0 && n < 20; i--, n++)
                FundUi.Line(life, new DateTime(e.Person.History[i].Time).ToString("MMM d", FundUi.C), e.Person.History[i].Text);
            FundUi.Line(life, "Drives", e.Person.Car.Length > 0 ? e.Person.Car : "No car (transit)");

            if (e.Former) return;
            VisualElement exit = FundUi.Card(_body, "Employment");
            Label summary = Ui.Label("fund-muted fund-wrap", exit);
            _live.Add(() => Ui.SetText(summary, $"Letting {e.Person.First} go: new trades stop at once. {Fund.ExitSummary(e)}"));
            FundUi.Armed($"LET {e.Person.First.ToUpperInvariant()} GO", $"CONFIRM: LET {e.Name.ToUpperInvariant()} GO", () =>
            {
                Fund.LetGo(e);
                Act(null, $"{e.Name} has been let go. They'll pack up and leave the building.");
            }, exit, "fund-btn-danger", "emp-letgo");
        }
    }
}
