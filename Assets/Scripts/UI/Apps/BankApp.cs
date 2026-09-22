using System.Collections.Generic;
using OpeningBell.Economy;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>Checking account: balance, transfers with the brokerage, monthly costs, upcoming bills, history.</summary>
    public sealed class BankApp : TerminalPanel
    {
        private const int UpcomingDays = 30;
        private const int DueSoonDays = 3;

        private readonly Label _balance, _overdrawn, _brokerageFree, _transferStatus, _monthly;
        private readonly TextField _amount;
        private readonly VisualElement _bills, _upcoming;
        private readonly ListView _history;
        private readonly List<BankTransaction> _items = new List<BankTransaction>();
        private int _knownTransactions = -1;
        private long _knownDay = -1;

        public BankApp(TerminalContext context) : base(context, "bank-app")
        {
            var left = Ui.Box("app-column", Root);
            var card = Ui.Box("app-card", left);
            Ui.Label("panel-title", card, "CHECKING ACCOUNT");
            _balance = Ui.Label("bank-balance", card);
            _balance.name = "bank-balance";
            _overdrawn = Ui.Label("down app-note", card, "Overdrawn: each bill that bounces costs a fee. Move money over from your brokerage.");
            _brokerageFree = Ui.Label("muted app-note", card);

            var transfer = Ui.Box("app-card", left);
            Ui.Label("panel-title", transfer, "TRANSFER");
            _amount = new TextField { name = "transfer-amount", value = "500" };
            _amount.AddToClassList("ticket-field");
            TicketInput.Restrict(_amount, ".,$");
            transfer.Add(_amount);
            var buttons = Ui.Box("quick-row", transfer);
            Ui.Button("FROM BROKERAGE", () => Transfer(fromBrokerage: true), "", buttons, "transfer-from-broker");
            Ui.Button("TO BROKERAGE", () => Transfer(fromBrokerage: false), "", buttons, "transfer-to-broker");
            _transferStatus = Ui.Label("ticket-status", transfer);
            _transferStatus.name = "transfer-status";

            var costs = Ui.Box("app-card", left);
            Ui.Label("panel-title", costs, "MONTHLY COSTS");
            _bills = Ui.Box("", costs);
            _monthly = Ui.Label("app-total", costs);

            var right = Ui.Box("app-column app-wide", Root);
            var upcomingCard = Ui.Box("app-card", right);
            Ui.Label("panel-title", upcomingCard, $"UPCOMING ({UpcomingDays} DAYS)");
            _upcoming = Ui.Box("", upcomingCard);

            var historyCard = Ui.Box("app-card app-grow", right);
            Ui.Label("panel-title", historyCard, "HISTORY");
            _history = new ListView(_items, 24, MakeRow, BindRow) { selectionType = SelectionType.None, name = "bank-history" };
            _history.AddToClassList("activity-list");
            historyCard.Add(_history);
        }

        public override void Refresh()
        {
            EconomySystem economy = Context.Economy;
            decimal balance = economy.Bank.Balance;
            Ui.SetText(_balance, Fmt.Money(balance));
            Ui.SetSign(_balance, balance < 0m ? -1m : 0m);
            Ui.Show(_overdrawn, economy.Bank.IsOverdrawn);
            Ui.SetText(_brokerageFree, $"Brokerage cash free to transfer: {Fmt.Money(System.Math.Max(0m, Context.Account.BuyingPower))}");

            var transactions = economy.Bank.Transactions;
            long day = Context.Clock.Now.Date.Ticks;
            if (transactions.Count != _knownTransactions || day != _knownDay)
            {
                _knownTransactions = transactions.Count;
                _knownDay = day;
                _items.Clear();
                for (int i = transactions.Count - 1; i >= 0; i--) _items.Add(transactions[i]);
                _history.RefreshItems();
                RebuildBills(economy);
                RebuildUpcoming(economy);
            }
        }

        private void Transfer(bool fromBrokerage)
        {
            if (!TicketInput.TryParsePrice(_amount.value, out decimal amount))
            {
                ShowStatus("Enter a dollar amount.", error: true);
                return;
            }
            string error = fromBrokerage
                ? Context.Economy.TransferFromBrokerage(amount, Context.Clock.Now)
                : Context.Economy.TransferToBrokerage(amount, Context.Clock.Now);
            ShowStatus(error ?? $"Moved {Fmt.Money(amount)} {(fromBrokerage ? "to the bank" : "to the brokerage")}.", error != null);
            Refresh();
        }

        private void ShowStatus(string text, bool error)
        {
            Ui.SetText(_transferStatus, text);
            _transferStatus.EnableInClassList("error", error);
            _transferStatus.EnableInClassList("ok", !error);
        }

        private void RebuildBills(EconomySystem economy)
        {
            _bills.Clear();
            foreach (ActiveBill bill in economy.ActiveBills)
                Row(_bills, bill.Name, $"monthly · day {bill.DayOfMonth}", Fmt.Money(bill.Amount));
            Row(_bills, "Food & living", "daily", Fmt.Money(economy.DailyLivingCost));
            decimal perMonth = economy.MonthlyBills + economy.DailyLivingCost * 30m;
            Ui.SetText(_monthly, $"About {Fmt.Money(perMonth)} per month");
        }

        private void RebuildUpcoming(EconomySystem economy)
        {
            _upcoming.Clear();
            List<UpcomingBill> upcoming = economy.Upcoming(Context.Clock.Now, UpcomingDays);
            if (upcoming.Count == 0) Ui.Label("muted", _upcoming, "Nothing due.");
            foreach (UpcomingBill u in upcoming)
            {
                int inDays = (u.Date - Context.Clock.Now.Date).Days;
                var row = Row(_upcoming, u.Bill.Name, $"{Fmt.Date(u.Date)} · in {inDays} day{(inDays == 1 ? "" : "s")}", Fmt.Money(u.Bill.Amount));
                row.EnableInClassList("due-soon", inDays <= DueSoonDays);
            }
        }

        private static VisualElement Row(VisualElement parent, string name, string detail, string amount)
        {
            var row = Ui.Box("table-row", parent);
            Ui.Label("c c-left", row, name);
            Ui.Label("c c-left muted", row, detail);
            Ui.Label("c", row, amount);
            return row;
        }

        private VisualElement MakeRow()
        {
            var row = Ui.Box("table-row data-row");
            Ui.Label("c c-left muted", row);
            Ui.Label("c c-left", row);
            Ui.Label("c", row);
            Ui.Label("c muted", row);
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            BankTransaction t = _items[index];
            Ui.SetText((Label)row[0], $"{Fmt.Date(t.Time)} {Fmt.Minutes(t.Time)}");
            Ui.SetText((Label)row[1], t.Description);
            Ui.SetText((Label)row[2], Fmt.SignedMoney(t.Amount));
            Ui.SetSign(row[2], t.Amount);
            Ui.SetText((Label)row[3], Fmt.Money(t.BalanceAfter));
        }
    }
}
