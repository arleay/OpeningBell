using System;
using OpeningBell.Trading;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// Top strip of the trading app: the active account (click for the account menu), its metrics, and for prop
    /// accounts the firm's numbers (drawdown threshold, room left, target or payout, contract limit). The menu lists
    /// every account; click one to trade it, tick others to copy its trades (PROP_SPEC §3). Clock, session and speed
    /// live in the taskbar.
    /// </summary>
    public sealed class AccountBarPanel : TerminalPanel
    {
        private readonly Label _selId, _selSub, _copying;
        private readonly Label _equity, _cash, _buyingPower, _dayPnl, _realized, _unrealized, _bank;
        private readonly VisualElement _propGroup;
        private readonly Label _threshold, _room, _progress, _contracts;
        private readonly Label _progressCaption;
        private readonly VisualElement _menuList;
        private readonly Label _trader;
        private bool _menuDirty = true;

        /// <summary>The account menu. An overlay: the terminal adds it after the panels so it draws on top.</summary>
        public VisualElement Menu { get; }

        public AccountBarPanel(TerminalContext context) : base(context, "account-bar")
        {
            var left = Ui.Box("bar-group", Root);
            var select = new Button(ToggleMenu) { name = "account-select" };
            select.AddToClassList("account-select");
            var names = Ui.Box("account-select-text", select);
            _selId = Ui.Label("account-id", names);
            _selId.name = "account-id";
            _selSub = Ui.Label("account-sub", names);
            Ui.Label("account-caret", select, "▾");
            left.Add(select);
            _copying = Ui.Label("copy-badge", left);
            _copying.name = "copy-badge";

            Ui.Box("spacer", Root);

            var metrics = Ui.Box("bar-group", Root);
            _cash = Ui.Stat("BALANCE", metrics);
            _equity = Ui.Stat("EQUITY", metrics);
            _buyingPower = Ui.Stat("BUYING POWER", metrics);
            _dayPnl = Ui.Stat("DAY P&L", metrics);
            _realized = Ui.Stat("REALIZED (NET)", metrics);
            _unrealized = Ui.Stat("UNREALIZED", metrics);

            _propGroup = Ui.Box("bar-group prop-stats", Root);
            _threshold = Ui.Stat("DRAWDOWN LIMIT", _propGroup);
            _room = Ui.Stat("ROOM", _propGroup);
            _room.name = "prop-room";
            var progress = Ui.Box("stat", _propGroup);
            _progressCaption = Ui.Label("stat-caption", progress, "TARGET");
            _progress = Ui.Label("stat-value", progress);
            _progress.name = "prop-progress";
            _contracts = Ui.Stat("CONTRACTS", _propGroup);

            var bank = Ui.Box("bar-group", Root);
            _bank = Ui.Stat("BANK", bank, "stat bank-stat");

            Menu = Ui.Box("account-menu");
            Menu.name = "account-menu";
            _trader = Ui.Label("account-menu-trader", Menu);
            Ui.Label("muted account-menu-help", Menu, "Click an account to make it the leader (the one you trade). Tick others to copy every trade the leader places. Your personal account only trades when it leads or is ticked.");
            _menuList = Ui.Box("account-menu-list", Menu);
            Ui.Show(Menu, false);

            context.AccountsChanged += () => _menuDirty = true;
        }

        private void ToggleMenu()
        {
            bool open = Menu.style.display == DisplayStyle.None;
            Ui.Show(Menu, open);
            _menuDirty = true;
            Refresh();
        }

        public override void Refresh()
        {
            TradingAccount active = Context.Active;
            PropAccount prop = active.Prop;
            Ui.SetText(_selId, prop == null ? $"{active.Firm.ToUpperInvariant()} PERSONAL" : active.Id);
            Ui.SetText(_selSub, prop == null ? "Your own money" : $"{active.Firm} · {active.Kind} · {active.Status}");
            _selSub.EnableInClassList("down", prop != null && prop.Status == PropStatus.Failed);
            _selSub.EnableInClassList("up", prop != null && prop.Status == PropStatus.Passed);
            int followers = Context.FollowerCount;
            Ui.SetText(_copying, followers > 0 ? $"COPYING TO {followers}" : "");
            Ui.Show(_copying, followers > 0);

            var account = Context.Account;
            Ui.SetText(_cash, Fmt.Money(account.Cash));
            Ui.SetText(_equity, Fmt.Money(account.Equity));
            Ui.SetText(_buyingPower, Fmt.Money(account.BuyingPower));
            SetSigned(_dayPnl, account.DailyPnL);
            SetSigned(_realized, account.RealizedPnL - account.TotalCommissions);
            SetSigned(_unrealized, account.UnrealizedPnL);
            decimal bank = Context.Economy.Bank.Balance;
            Ui.SetText(_bank, Fmt.Money(bank));
            _bank.EnableInClassList("down", bank < 0m);

            Ui.Show(_propGroup, prop != null);
            if (prop != null) RefreshProp(prop);

            if (Menu.style.display != DisplayStyle.None) RefreshMenu();
        }

        private void RefreshProp(PropAccount p)
        {
            Ui.SetText(_threshold, Fmt.Money(p.Threshold));
            decimal room = p.RoomToThreshold;
            Ui.SetText(_room, p.IsTradeable ? Fmt.Money(room) : "—");
            // Amber inside the last quarter of the drawdown, red inside the last tenth.
            _room.EnableInClassList("warn", p.IsTradeable && room < p.Plan.Drawdown * 0.25m && room >= p.Plan.Drawdown * 0.1m);
            _room.EnableInClassList("down", p.IsTradeable && room < p.Plan.Drawdown * 0.1m);

            if (p.Phase == PropPhase.Evaluation)
            {
                Ui.SetText(_progressCaption, "TARGET");
                Ui.SetText(_progress, $"{Fmt.Money(Math.Max(0m, p.Profit))} / {Fmt.Money(p.Plan.ProfitTarget)}  ·  {p.TradingDays}/{p.Firm.MinTradingDays}d");
                Ui.SetSign(_progress, p.Profit >= p.Plan.ProfitTarget ? 1m : 0m);
            }
            else
            {
                PayoutQuote q = Context.Game.Prop.PayoutAvailable(p);
                Ui.SetText(_progressCaption, "PAYOUT");
                Ui.SetText(_progress, q.Eligible || q.Max > 0m ? $"{Fmt.Money(q.Max)} available"
                    : p.Firm.Payouts == PayoutStyle.WinningDays ? $"{p.WinningDaysSincePayout}/{p.Firm.WinningDaysForPayout} winning days"
                    : $"buffer {Fmt.Money(p.StartBalance + p.Plan.Drawdown)}");
                Ui.SetSign(_progress, q.Eligible ? 1m : 0m);
            }
            Ui.SetText(_contracts, $"{p.OpenContracts} / {p.ContractLimit}");
        }

        private void RefreshMenu()
        {
            var accounts = Context.Accounts;
            if (_menuDirty || _menuList.childCount != accounts.Count)
            {
                _menuDirty = false;
                string name = Context.Game.Look != null && !string.IsNullOrEmpty(Context.Game.Look.Name) ? Context.Game.Look.Name : "Trader";
                Ui.SetText(_trader, $"{name.ToUpperInvariant()} · {accounts.Count} ACCOUNT{(accounts.Count == 1 ? "" : "S")}");
                _menuList.Clear();
                foreach (TradingAccount a in accounts) MakeRow(a);
            }
            for (int i = 0; i < accounts.Count && i < _menuList.childCount; i++)
            {
                TradingAccount a = accounts[i];
                var balance = _menuList[i].Q<Label>(className: "account-row-balance");
                Ui.SetText(balance, Fmt.Money(a.Account.Equity));
            }
        }

        private void MakeRow(TradingAccount a)
        {
            var row = Ui.Box("account-row", _menuList);
            row.name = "account-row-" + a.Id;
            row.EnableInClassList("active", a == Context.Active);

            var copy = new Toggle { name = "copy-" + a.Id, value = Context.IsFollower(a), tooltip = "Copy trades from the active account" };
            copy.AddToClassList("account-copy");
            copy.SetEnabled(a != Context.Active && a.Tradeable);
            copy.RegisterValueChangedCallback(e => Context.SetFollower(a, e.newValue));
            row.Add(copy);

            var text = Ui.Box("account-row-text", row);
            Ui.Label("account-row-id", text, a.Prop == null ? $"{a.Firm.ToUpperInvariant()} PERSONAL" : a.Id);
            Ui.Label("account-row-sub muted", text, a.Prop == null ? "Your own money" : $"{a.Firm} · {a.Kind}");
            text.RegisterCallback<ClickEvent>(_ =>
            {
                Context.SetActive(a);
                Ui.Show(Menu, false);
            });

            if (a == Context.Active) Ui.Label("account-row-lead", row, "LEADER").tooltip = "The account you trade; ticked accounts copy it";
            else if (a.Tradeable) Ui.Button("LEAD", () => { Context.SetActive(a); Ui.Show(Menu, false); }, "account-row-make-lead", row, "lead-" + a.Id)
                .tooltip = "Trade this account instead; the ticked accounts copy it";
            Ui.Label("account-row-balance", row);
            if (a.Prop != null)
            {
                var status = Ui.Label("account-row-status " + a.Status.ToLowerInvariant(), row, a.Status);
                status.tooltip = a.Prop.StatusReason;
            }
        }

        private static void SetSigned(Label label, decimal value)
        {
            Ui.SetText(label, Fmt.SignedMoney(value));
            Ui.SetSign(label, value);
        }
    }
}
