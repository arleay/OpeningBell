using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Casino;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.City
{
    /// <summary>
    /// The cashier's cage (CASINO_SPEC C2): chips are bought here from the bank and cashed back into it, the only
    /// way money moves in or out of the casino. First-timers sign up for a players' card (free) before buying.
    /// </summary>
    public sealed class CasinoCage : Interactable
    {
        public static readonly decimal[] QuickBuys = { 100m, 500m, 1_000m, 5_000m };

        private GameBootstrap _game;
        private InteractionHud _hud;
        private StaffNpc _cashier;
        private CasinoControls _controls;

        private VisualElement _panel, _join, _main, _rewardsPage, _budgetPage, _accountPage;
        private Label _bank, _chips, _status, _history, _rewards, _budget, _account;
        private string _page = "chips";
        private bool _hardLimit;
        private TextField _amount;
        private readonly List<CasinoUi.Pill> _pills = new List<CasinoUi.Pill>();

        public bool IsOpen { get; private set; }
        public string Status => _status?.text;

        public void Configure(CityContext c, StaffNpc cashier)
        {
            _game = c.Game;
            _hud = c.Hud;
            _cashier = cashier;
            _controls = new CasinoControls(c.Player);
        }

        private CasinoFloor Floor => _game.Casino;

        public override string Prompt => _cashier == null || _cashier.AtStation ? "Cashier · buy or cash out chips" : "Cage closed";
        public override string Details => $"Chips {CasinoUi.Money(Floor.Account.Chips)} · Bank {CasinoUi.Money(_game.Economy.Bank.Balance)}";
        public override bool CanInteract => base.CanInteract && !IsOpen && (_cashier == null || _cashier.AtStation);

        public override void Interact() => Open();

        public void Open()
        {
            if (IsOpen) return;
            if (_panel == null)
            {
                if (_hud.Root == null) return;
                Build(_hud.Root);
            }
            IsOpen = true;
            _controls.Take();
            _panel.style.display = DisplayStyle.Flex;
            SetStatus(Floor.Member ? "What can I do for you?" : "");
            Say(Floor.Member ? "Welcome back." : "Welcome to The Meridian. First time? I can set you up with a players' card.");
            Refresh();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _amount?.Blur();
            _panel.style.display = DisplayStyle.None;
            _controls.Release();
        }

        public void Join()
        {
            if (Floor.Member) return;
            Floor.Member = true;
            Say("You're all set. Your card tracks your play; chips are good at any table.");
            SetStatus("Meridian Rewards card issued.");
        }

        /// <summary>Buys chips with bank money. Null on success, else why not.</summary>
        public string Buy(decimal amount)
        {
            if (!Floor.Member) return Fail("Sign up for a players' card first (it's free).");
            string error = Floor.Account.BuyChips(amount, _game.Clock.Now, (a, what) => _game.Economy.Spend(a, what, _game.Clock.Now));
            if (error != null) return Fail(error);
            Say($"{CasinoUi.Money(amount)} in chips. Good luck.");
            SetStatus($"Bought {CasinoUi.Money(amount)} in chips.");
            return null;
        }

        /// <summary>Cashes chips back into the bank.</summary>
        public string CashOut(decimal amount)
        {
            string table = Floor.TableInPlay;
            if (table != null) return Fail("You've got a hand in play. Finish it first.");
            string error = Floor.Account.CashOut(amount, _game.Clock.Now, (a, what) => _game.Economy.Receive(a, what, _game.Clock.Now));
            if (error != null) return Fail(error);
            Say($"{CasinoUi.Money(amount)}, straight to your bank.");
            SetStatus($"Cashed out {CasinoUi.Money(amount)} to the bank.");
            return null;
        }

        public string CashOutAll() => Floor.Account.Chips > 0m ? CashOut(Floor.Account.Chips) : Fail("You don't have any chips.");

        private string Fail(string error)
        {
            SetStatus(error);
            return error;
        }

        private void Say(string line)
        {
            if (_cashier != null) _cashier.Say(line);
        }

        private void SetStatus(string text)
        {
            if (_status != null) _status.text = text;
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (_controls.BackPressed) Close();
            else Refresh();
        }

        private void Refresh()
        {
            _join.style.display = Floor.Member ? DisplayStyle.None : DisplayStyle.Flex;
            _main.style.display = Floor.Member ? DisplayStyle.Flex : DisplayStyle.None;
            _bank.text = "Bank " + CasinoUi.Money(_game.Economy.Bank.Balance);
            _chips.text = "Chips " + CasinoUi.Money(Floor.Account.Chips);
            var lines = new List<string>();
            IReadOnlyList<CasinoEntry> h = Floor.Account.History;
            for (int i = h.Count - 1; i >= 0 && lines.Count < 4; i--)
                if (h[i].Kind == CasinoEntryKind.BuyChips || h[i].Kind == CasinoEntryKind.CashOut)
                    lines.Add($"{h[i].Time.ToString("ddd h:mm tt", CultureInfo.InvariantCulture)}  {(h[i].Kind == CasinoEntryKind.BuyChips ? "Bought" : "Cashed out")} {CasinoUi.Money(h[i].Amount)}");
            decimal net = Floor.Net;
            _history.text = (lines.Count > 0 ? string.Join("\n", lines) + "\n" : "") +
                            $"Overall: {(net >= 0m ? "+" : "")}{CasinoUi.Money(net)}";

            _main.style.display = Floor.Member && _page == "chips" ? DisplayStyle.Flex : DisplayStyle.None;
            _rewardsPage.style.display = Floor.Member && _page == "rewards" ? DisplayStyle.Flex : DisplayStyle.None;
            _budgetPage.style.display = Floor.Member && _page == "budget" ? DisplayStyle.Flex : DisplayStyle.None;
            _accountPage.style.display = Floor.Member && _page == "account" ? DisplayStyle.Flex : DisplayStyle.None;
            Rewards r = Floor.Rewards;
            _rewards.text = $"Tier: {r.Tier} · {r.Points:N0} points ({CasinoUi.Money(r.PointsValue)} to spend on food, drinks and rooms)\n" +
                            $"Perks: {Rewards.PerksOf(r.Tier)}\n" +
                            (r.Tier == RewardTier.Diamond ? "Top tier." : $"{r.ToNextTier:N0} tier credits to {(RewardTier)((int)r.Tier + 1)} ({Rewards.PerksOf((RewardTier)((int)r.Tier + 1))})") +
                            "\nEarn 1 point per $10 at the tables, $5 on slots, $1 spent in the resort. Points never change the odds.";
            DateTime now = _game.Clock.Now;
            GamblingBudget b = Floor.Budget;
            _budget.text = b.ActiveOn(now)
                ? $"Tonight's limit {CasinoUi.Money(b.Limit)}{(b.Hard ? " (hard: bets stop at the limit)" : " (a reminder)")} · lost so far {CasinoUi.Money(b.Lost(Floor.Net))}"
                : "No limit set tonight. Set one and you'll be told at 80% and at the limit; tick hard to stop bets there.";
            CasinoAccount a = Floor.Account;
            _account.text =
                $"Bought {CasinoUi.Money(a.TotalBought)} · cashed out {CasinoUi.Money(a.TotalCashedOut)}\n" +
                $"Wagered {CasinoUi.Money(a.TotalWagered)} · won {CasinoUi.Money(a.TotalWon)} · lost {CasinoUi.Money(a.TotalLost)}\n" +
                $"Biggest win {CasinoUi.Money(a.BiggestWin)} · biggest loss {CasinoUi.Money(a.BiggestLoss)}\n" +
                $"Blackjack {a.Count(CasinoGame.Blackjack)} hands · roulette {a.Count(CasinoGame.Roulette)} spins · slots {a.Count(CasinoGame.Slots)} spins · baccarat {a.Count(CasinoGame.Baccarat)} · poker {a.Count(CasinoGame.Poker)} sessions ({(a.PokerNet >= 0 ? "+" : "")}{CasinoUi.Money(a.PokerNet)})\n" +
                $"Resort: food {CasinoUi.Money(Floor.SpentOn(SpendCategory.Food))} · drinks {CasinoUi.Money(Floor.SpentOn(SpendCategory.Drinks))} · hotel {CasinoUi.Money(Floor.SpentOn(SpendCategory.Hotel))} · valet {CasinoUi.Money(Floor.SpentOn(SpendCategory.Valet))}\n" +
                "House games are built to keep a share over time: the casino is a night out, not an income.";
            _pills.RefreshAll();
        }

        private void Build(VisualElement root)
        {
            _panel = CasinoUi.Frame(root, "cage-panel", 460f);
            PhoneKit.Absolute(_panel, top: 90f);
            _panel.style.left = new Length(50f, LengthUnit.Percent);
            _panel.style.translate = new Translate(new Length(-50f, LengthUnit.Percent), 0f);

            VisualElement head = PhoneKit.Row(_panel);
            CasinoUi.Heading(head, "THE MERIDIAN · CASHIER");
            new CasinoUi.Pill(head, "cage-close", "Close (Esc)", Close);
            _bank = PhoneKit.Label(_panel, "", 20f, PhoneKit.Text, true);
            _bank.name = "cage-bank";
            _bank.style.marginTop = 8f;
            _chips = PhoneKit.Label(_panel, "", 20f, CasinoUi.Gold, true);
            _chips.name = "cage-chips";

            _join = PhoneKit.Box(_panel, "cage-join-card");
            _join.style.marginTop = 10f;
            PhoneKit.Label(_join, "MERIDIAN REWARDS", 13f, CasinoUi.Gold, true);
            PhoneKit.Label(_join, "A free players' card: it holds your chips' history and your record at the tables. Chips can only be spent in the casino and cashed back here.", 14f, PhoneKit.Muted);
            new CasinoUi.Pill(_join, "cage-join", "Sign me up", Join, primary: true);

            VisualElement tabs = CasinoUi.Wrap(_panel);
            foreach ((string id, string label) in new[] { ("chips", "Chips"), ("rewards", "Rewards"), ("budget", "Budget"), ("account", "Account") })
            {
                string page = id;
                _pills.Add(new CasinoUi.Pill(tabs, "cage-tab-" + id, label, () => _page = page, () => Floor.Member, size: 13f));
            }

            _rewardsPage = PhoneKit.Box(_panel, "cage-rewards");
            _rewardsPage.style.marginTop = 10f;
            _rewards = PhoneKit.Label(_rewardsPage, "", 13f, PhoneKit.Text);
            _rewards.name = "cage-rewards-text";

            _budgetPage = PhoneKit.Box(_panel, "cage-budget");
            _budgetPage.style.marginTop = 10f;
            _budget = PhoneKit.Label(_budgetPage, "", 13f, PhoneKit.Text);
            VisualElement limits = CasinoUi.Wrap(_budgetPage);
            foreach (decimal v in new[] { 250m, 500m, 1_000m, 2_500m })
            {
                decimal limit = v;
                _pills.Add(new CasinoUi.Pill(limits, $"cage-limit-{v}", CasinoUi.Money(v), () => SetBudget(limit)));
            }
            _pills.Add(new CasinoUi.Pill(limits, "cage-limit-clear", "Clear", () => Floor.Budget.Clear(), () => Floor.Budget.Set));
            var hard = new Toggle("Hard limit (stop my bets there)") { name = "cage-limit-hard" };
            hard.labelElement.style.color = PhoneKit.Text;
            hard.RegisterValueChangedCallback(e => _hardLimit = e.newValue);
            _budgetPage.Add(hard);

            _accountPage = PhoneKit.Box(_panel, "cage-account");
            _accountPage.style.marginTop = 10f;
            _account = PhoneKit.Label(_accountPage, "", 12f, PhoneKit.Text);
            _account.name = "cage-account-text";

            _main = PhoneKit.Box(_panel, "cage-main");
            _main.style.marginTop = 10f;
            PhoneKit.Label(_main, "BUY CHIPS (from your bank)", 12f, PhoneKit.Muted, true);
            VisualElement quick = CasinoUi.Wrap(_main);
            foreach (decimal v in QuickBuys)
            {
                decimal amount = v;
                _pills.Add(new CasinoUi.Pill(quick, $"cage-buy-{v}", CasinoUi.Money(v), () => Buy(amount), () => _game.Economy.Bank.Balance >= amount));
            }
            VisualElement custom = PhoneKit.Row(_main, Justify.FlexStart);
            custom.style.marginTop = 8f;
            _amount = PhoneKit.Field(custom, "", "cage-amount");
            _amount.style.maxWidth = 150f;
            _amount.style.marginRight = 6f;
            _pills.Add(new CasinoUi.Pill(custom, "cage-buy", "Buy", () => { if (Parse(out decimal a)) Buy(a); }, primary: true));
            _pills.Add(new CasinoUi.Pill(custom, "cage-cashout", "Cash out", () => { if (Parse(out decimal a)) CashOut(a); }, () => Floor.Account.Chips > 0m));
            _pills.Add(new CasinoUi.Pill(custom, "cage-cashout-all", "Cash out all", () => CashOutAll(), () => Floor.Account.Chips > 0m));

            _status = PhoneKit.Label(_panel, "", 14f, Color.white);
            _status.name = "cage-status";
            _status.style.marginTop = 10f;
            _history = PhoneKit.Label(_panel, "", 12f, PhoneKit.Muted);
            _history.name = "cage-history";
            _history.style.marginTop = 8f;
        }

        /// <summary>Tonight's gambling limit (§61): losses from now until 6 AM count toward it.</summary>
        public void SetBudget(decimal limit)
        {
            Floor.Budget.Start(limit, _hardLimit, _game.Clock.Now, Floor.Net);
            SetStatus($"Tonight's limit: {CasinoUi.Money(limit)}{(_hardLimit ? ", hard" : "")}.");
        }

        public void ShowPage(string page) => _page = page;

        private bool Parse(out decimal amount)
        {
            string text = (_amount.value ?? "").Replace("$", "").Replace(",", "").Trim();
            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount)) return true;
            SetStatus("Enter an amount, like 250.");
            return false;
        }
    }
}
