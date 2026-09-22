using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Trading;

namespace OpeningBell.Economy
{
    public readonly struct ActiveBill
    {
        public string Id { get; }
        public string Name { get; }
        public BillCategory Category { get; }
        public decimal Amount { get; }
        public int DayOfMonth { get; }

        public ActiveBill(string id, string name, BillCategory category, decimal amount, int dayOfMonth)
        {
            Id = id;
            Name = name;
            Category = category;
            Amount = amount;
            DayOfMonth = dayOfMonth;
        }
    }

    public readonly struct UpcomingBill
    {
        public DateTime Date { get; }
        public ActiveBill Bill { get; }

        public UpcomingBill(DateTime date, ActiveBill bill)
        {
            Date = date;
            Bill = bill;
        }
    }

    /// <summary>
    /// Life outside the brokerage: a bank account that pays bills, daily living costs and purchases, plus transfers
    /// to/from the brokerage. Charges are applied per calendar day crossed (at midnight), so results don't depend on
    /// how time was advanced. Overdraft instead of game over: a bill that bounces costs a fee.
    /// </summary>
    public sealed class EconomySystem
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        private readonly EconomyConfig _config;
        private readonly Account _brokerage;
        private readonly List<StoreItem> _catalog;
        private readonly Dictionary<string, StoreItem> _catalogById = new Dictionary<string, StoreItem>(StringComparer.Ordinal);
        private readonly HashSet<string> _owned = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<ActiveBill> _activeBills = new List<ActiveBill>();

        public BankAccount Bank { get; } = new BankAccount();
        public IReadOnlyList<StoreItem> Catalog => _catalog;
        public IReadOnlyCollection<string> OwnedItems => _owned;
        public DateTime LastProcessedDate { get; private set; }
        public decimal DailyLivingCost => (decimal)_config.DailyLivingCost;

        public event Action<BankTransaction> TransactionPosted;
        public event Action<StoreItem> ItemPurchased;

        public EconomySystem(EconomyConfig config, IReadOnlyList<StoreItem> catalog, Account brokerage, DateTime start, bool fundBank = true)
        {
            _config = config;
            _brokerage = brokerage;
            _catalog = new List<StoreItem>(catalog);
            var billIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (RecurringBill b in config.Bills)
                if (!billIds.Add(b.Id)) throw new ArgumentException($"Duplicate bill id {b.Id}.");
            foreach (StoreItem item in _catalog)
            {
                if (_catalogById.ContainsKey(item.Id)) throw new ArgumentException($"Duplicate store item id {item.Id}.");
                if (!string.IsNullOrEmpty(item.ReplacesBillId) && !billIds.Contains(item.ReplacesBillId))
                    throw new ArgumentException($"Store item {item.Id} replaces unknown bill {item.ReplacesBillId}.");
                _catalogById.Add(item.Id, item);
            }

            LastProcessedDate = start.Date;
            if (fundBank && config.StartingBankBalance > 0)
                Post(start, TransactionKind.Deposit, (decimal)config.StartingBankBalance, "Opening balance");
            RebuildBills();
        }

        public bool Owns(string itemId) => _owned.Contains(itemId);

        /// <summary>True if any owned equipment upgrades this apartment slot.</summary>
        public bool HasUpgrade(string slot)
        {
            foreach (string id in _owned)
                if (_catalogById.TryGetValue(id, out StoreItem item) && item.Slot == slot) return true;
            return false;
        }

        /// <summary>Base bills, minus any replaced by owned services, plus those services' own bills.</summary>
        public IReadOnlyList<ActiveBill> ActiveBills => _activeBills;

        public decimal MonthlyBills
        {
            get
            {
                decimal total = 0m;
                foreach (ActiveBill b in _activeBills) total += b.Amount;
                return total;
            }
        }

        /// <summary>Charges every calendar day between the last processed date and <paramref name="now"/>.</summary>
        public void AdvanceTo(DateTime now)
        {
            while (LastProcessedDate < now.Date)
            {
                DateTime day = LastProcessedDate.AddDays(1);
                Post(day, TransactionKind.Living, -DailyLivingCost, "Food & living");
                foreach (ActiveBill bill in _activeBills)
                    if (DueDay(bill, day) == day.Day) ChargeBill(day, bill);
                LastProcessedDate = day;
            }
        }

        public List<UpcomingBill> Upcoming(DateTime from, int days)
        {
            var list = new List<UpcomingBill>();
            for (DateTime d = from.Date.AddDays(1); d <= from.Date.AddDays(days); d = d.AddDays(1))
                foreach (ActiveBill bill in _activeBills)
                    if (DueDay(bill, d) == d.Day) list.Add(new UpcomingBill(d, bill));
            return list;
        }

        /// <summary>Returns an error message, or null on success.</summary>
        public string Buy(string itemId, DateTime now)
        {
            if (!_catalogById.TryGetValue(itemId, out StoreItem item)) return "Unknown item.";
            if (_owned.Contains(itemId)) return $"You already own the {item.Name}.";
            decimal price = Money.RoundCents((decimal)item.Price);
            if (Bank.Balance < price)
                return $"Not enough in the bank: {Dollars(price)} needed, {Dollars(Bank.Balance)} available. Transfer from your brokerage first.";

            if (price > 0m) Post(now, TransactionKind.Purchase, -price, item.Name);
            _owned.Add(itemId);
            RebuildBills();
            ItemPurchased?.Invoke(item);
            return null;
        }

        /// <summary>Everyday card purchase (coffee, snacks). Declined rather than overdrawn. Error message, or null.</summary>
        public string Spend(decimal amount, string description, DateTime now)
        {
            amount = Money.RoundCents(amount);
            if (amount <= 0m) return "Nothing to pay.";
            if (Bank.Balance < amount) return $"Card declined: {Dollars(Math.Max(0m, Bank.Balance))} in the bank.";
            Post(now, TransactionKind.Purchase, -amount, description);
            return null;
        }

        /// <summary>Returns an error message, or null on success.</summary>
        public string TransferToBrokerage(decimal amount, DateTime now)
        {
            amount = Money.RoundCents(amount);
            if (amount <= 0m) return "Enter an amount.";
            if (Bank.Balance < amount) return $"Only {Dollars(Math.Max(0m, Bank.Balance))} in the bank.";
            Post(now, TransactionKind.TransferOut, -amount, "Transfer to brokerage");
            _brokerage.Deposit(amount, "Transfer from bank");
            return null;
        }

        /// <summary>Returns an error message, or null on success. Cash reserved for open orders can't be moved.</summary>
        public string TransferFromBrokerage(decimal amount, DateTime now)
        {
            amount = Money.RoundCents(amount);
            if (amount <= 0m) return "Enter an amount.";
            if (amount > _brokerage.BuyingPower)
                return $"Only {Dollars(Math.Max(0m, _brokerage.BuyingPower))} of brokerage cash is free (the rest is in positions or reserved for open orders).";
            _brokerage.Withdraw(amount, "Transfer to bank");
            Post(now, TransactionKind.TransferIn, amount, "Transfer from brokerage");
            return null;
        }

        private void ChargeBill(DateTime day, ActiveBill bill)
        {
            TransactionKind kind = bill.Category == BillCategory.Housing || bill.Category == BillCategory.Lease ? TransactionKind.Rent
                : bill.Category == BillCategory.Subscription ? TransactionKind.Subscription
                : TransactionKind.Bill;
            Post(day, kind, -bill.Amount, bill.Name);
            if (Bank.IsOverdrawn) Post(day, TransactionKind.Fee, -Money.RoundCents((decimal)_config.OverdraftFee), $"Overdraft fee ({bill.Name})");
        }

        private void Post(DateTime time, TransactionKind kind, decimal amount, string description)
        {
            // Post first: `event?.Invoke(Post(...))` would skip the argument, and the money, when nobody listens.
            BankTransaction tx = Bank.Post(time, kind, amount, description);
            TransactionPosted?.Invoke(tx);
        }

        private void RebuildBills()
        {
            _activeBills.Clear();
            var replaced = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in _owned)
                if (_catalogById.TryGetValue(id, out StoreItem item) && !string.IsNullOrEmpty(item.ReplacesBillId))
                    replaced.Add(item.ReplacesBillId);

            foreach (RecurringBill b in _config.Bills)
                if (!replaced.Contains(b.Id))
                    _activeBills.Add(new ActiveBill(b.Id, b.Name, b.Category, Money.RoundCents((decimal)b.Amount), b.DayOfMonth));

            foreach (StoreItem item in _catalog)
                if (_owned.Contains(item.Id) && item.MonthlyCost > 0)
                    _activeBills.Add(new ActiveBill(item.Id, item.Name,
                        item.Category == StoreCategory.Lease ? BillCategory.Lease : BillCategory.Subscription,
                        Money.RoundCents((decimal)item.MonthlyCost), item.BillDayOfMonth));
        }

        private static int DueDay(ActiveBill bill, DateTime month) =>
            Math.Min(Math.Max(1, bill.DayOfMonth), DateTime.DaysInMonth(month.Year, month.Month));

        private static string Dollars(decimal value) => "$" + value.ToString("N2", C);

        // ---- save ----

        public EconomySaveData CaptureState(int maxTransactions = 500)
        {
            var data = new EconomySaveData
            {
                Balance = Bank.Balance.ToString(C),
                NextTransactionId = Bank.NextId,
                LastProcessedDate = LastProcessedDate.Ticks,
                Owned = new List<string>(_owned),
            };
            var all = Bank.Transactions;
            for (int i = Math.Max(0, all.Count - maxTransactions); i < all.Count; i++)
            {
                BankTransaction t = all[i];
                data.Transactions.Add(new BankTransactionSaveData
                {
                    Id = t.Id, Time = t.Time.Ticks, Kind = (int)t.Kind, Amount = t.Amount.ToString(C),
                    BalanceAfter = t.BalanceAfter.ToString(C), Description = t.Description,
                });
            }
            return data;
        }

        /// <summary>Restores into an economy built with fundBank: false. Unknown (removed) items are dropped.</summary>
        public void RestoreState(EconomySaveData data)
        {
            var transactions = new List<BankTransaction>();
            foreach (BankTransactionSaveData t in data.Transactions)
                transactions.Add(new BankTransaction(t.Id, new DateTime(t.Time), (TransactionKind)t.Kind,
                    decimal.Parse(t.Amount, C), decimal.Parse(t.BalanceAfter, C), t.Description));
            Bank.Restore(decimal.Parse(data.Balance, C), data.NextTransactionId, transactions);
            LastProcessedDate = new DateTime(data.LastProcessedDate);

            _owned.Clear();
            foreach (string id in data.Owned)
                if (_catalogById.ContainsKey(id)) _owned.Add(id);
            RebuildBills();
        }
    }

    [Serializable]
    public sealed class EconomySaveData
    {
        public string Balance;
        public long NextTransactionId;
        public long LastProcessedDate;
        public List<string> Owned = new List<string>();
        public List<BankTransactionSaveData> Transactions = new List<BankTransactionSaveData>();
    }

    [Serializable]
    public sealed class BankTransactionSaveData
    {
        public long Id, Time;
        public int Kind;
        public string Amount, BalanceAfter, Description;
    }
}
