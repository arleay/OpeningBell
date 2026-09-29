using System;
using System.Collections.Generic;

namespace OpeningBell.Fund
{
    /// <summary>Why operating cash moved. Saved as ints: append only.</summary>
    public enum CashKind
    {
        OwnerContribution,
        OwnerWithdrawal,
        /// <summary>Operating cash → an employee's desk.</summary>
        Allocation,
        /// <summary>An employee's desk → operating cash (capital taken back).</summary>
        Recall,
        /// <summary>Realized desk profit moved to operating cash.</summary>
        Collection,
        Wages,
        Commission,
        Rent,
        Deposit,
        DepositReturned,
        Utilities,
        Connectivity,
        MarketData,
        Training,
        Equipment,
        EquipmentSale,
        Recruiting,
        FitOut,
        OfficePurchase,
        OfficeSale,
        /// <summary>Winding up: what the owner's bank paid in to settle debts.</summary>
        ClosureShortfall,
    }

    /// <summary>What an expense was for (the P&L's lines). Saved as ints: append only.</summary>
    public enum ExpenseKind
    {
        Wages,
        Commission,
        Rent,
        Utilities,
        Connectivity,
        MarketData,
        Training,
        Equipment,
        Recruiting,
        FitOut,
    }

    /// <summary>One movement of operating cash. Append-only: cash only changes through entries.</summary>
    [Serializable]
    public sealed class CashEntry
    {
        public long Id, Time;
        public CashKind Kind;
        public decimal Amount, BalanceAfter;
        public string Memo = "";
        /// <summary>The employee it concerns (0 = none).</summary>
        public long Employee;
    }

    /// <summary>An expense recognised when it was incurred (accrual basis), whether or not it's paid yet.</summary>
    [Serializable]
    public sealed class ExpenseEntry
    {
        public long Time;
        public ExpenseKind Kind;
        public decimal Amount;
        public long Employee;
        public string Memo = "";
    }

    /// <summary>
    /// The company's operating cash (cash basis) and its expense journal (accrual basis). Expenses are recognised
    /// once, when incurred; paying them later only settles a liability, so payroll is never subtracted twice.
    /// </summary>
    public sealed class CompanyLedger
    {
        private readonly List<CashEntry> _cash = new List<CashEntry>();
        private readonly List<ExpenseEntry> _expenses = new List<ExpenseEntry>();
        private long _nextId = 1;

        public decimal Cash { get; private set; }
        public IReadOnlyList<CashEntry> Entries => _cash;
        public IReadOnlyList<ExpenseEntry> Expenses => _expenses;
        /// <summary>Net money the owner has put in (contributions less withdrawals).</summary>
        public decimal NetContributions { get; private set; }
        public decimal TotalExpenses { get; private set; }
        public decimal OtherIncome { get; private set; }

        public event Action<CashEntry> Posted;

        public CashEntry Post(DateTime time, CashKind kind, decimal amount, string memo, long employee = 0)
        {
            Cash += amount;
            var e = new CashEntry { Id = _nextId++, Time = time.Ticks, Kind = kind, Amount = amount, BalanceAfter = Cash, Memo = memo ?? "", Employee = employee };
            _cash.Add(e);
            if (kind == CashKind.OwnerContribution || kind == CashKind.OwnerWithdrawal || kind == CashKind.ClosureShortfall) NetContributions += amount;
            if (kind == CashKind.EquipmentSale) OtherIncome += amount;
            Posted?.Invoke(e);
            return e;
        }

        public void Recognise(DateTime time, ExpenseKind kind, decimal amount, string memo, long employee = 0)
        {
            if (amount == 0m) return;
            // Wages accrue every minute: fold into today's line for that person rather than one entry a minute.
            if (kind == ExpenseKind.Wages && _expenses.Count > 0)
            {
                for (int i = _expenses.Count - 1; i >= 0 && i >= _expenses.Count - 64; i--)
                {
                    ExpenseEntry last = _expenses[i];
                    if (last.Kind == ExpenseKind.Wages && last.Employee == employee && new DateTime(last.Time).Date == time.Date)
                    {
                        last.Amount += amount;
                        TotalExpenses += amount;
                        return;
                    }
                }
            }
            _expenses.Add(new ExpenseEntry { Time = time.Ticks, Kind = kind, Amount = amount, Employee = employee, Memo = memo ?? "" });
            TotalExpenses += amount;
        }

        /// <summary>Expenses of a kind recognised in [from, to).</summary>
        public decimal ExpensesBetween(DateTime from, DateTime to, ExpenseKind? kind = null)
        {
            decimal sum = 0m;
            foreach (ExpenseEntry e in _expenses)
                if (e.Time >= from.Ticks && e.Time < to.Ticks && (kind == null || e.Kind == kind)) sum += e.Amount;
            return sum;
        }

        internal void Restore(List<CashEntry> cash, List<ExpenseEntry> expenses, decimal netContributions, decimal totalExpenses, decimal otherIncome)
        {
            _cash.Clear();
            _cash.AddRange(cash);
            _expenses.Clear();
            _expenses.AddRange(expenses);
            Cash = _cash.Count > 0 ? _cash[_cash.Count - 1].BalanceAfter : 0m;
            _nextId = _cash.Count > 0 ? _cash[_cash.Count - 1].Id + 1 : 1;
            NetContributions = netContributions;
            TotalExpenses = totalExpenses;
            OtherIncome = otherIncome;
        }

        public static string KindName(CashKind k) => k switch
        {
            CashKind.OwnerContribution => "Owner contribution",
            CashKind.OwnerWithdrawal => "Owner withdrawal",
            CashKind.Allocation => "Capital allocated",
            CashKind.Recall => "Capital recalled",
            CashKind.Collection => "Profit collected",
            CashKind.Wages => "Payroll",
            CashKind.Commission => "Commission paid",
            CashKind.Rent => "Office rent",
            CashKind.Deposit => "Lease deposit",
            CashKind.DepositReturned => "Deposit returned",
            CashKind.Utilities => "Utilities",
            CashKind.Connectivity => "Connectivity",
            CashKind.MarketData => "Market data",
            CashKind.Training => "Training",
            CashKind.Equipment => "Equipment",
            CashKind.EquipmentSale => "Equipment sold",
            CashKind.Recruiting => "Recruiting",
            CashKind.FitOut => "Office fit-out",
            CashKind.OfficePurchase => "Office purchase",
            CashKind.OfficeSale => "Office sale",
            CashKind.ClosureShortfall => "Owner settled debts",
            _ => k.ToString(),
        };

        public static string ExpenseName(ExpenseKind k) => k switch
        {
            ExpenseKind.Wages => "Hourly wages",
            ExpenseKind.Commission => "Profit commissions",
            ExpenseKind.Rent => "Office rent",
            ExpenseKind.Utilities => "Utilities",
            ExpenseKind.Connectivity => "Connectivity",
            ExpenseKind.MarketData => "Market data and platform",
            ExpenseKind.Training => "Training",
            ExpenseKind.Equipment => "Furniture and equipment",
            ExpenseKind.Recruiting => "Recruiting",
            ExpenseKind.FitOut => "Office fit-out",
            _ => k.ToString(),
        };
    }
}
