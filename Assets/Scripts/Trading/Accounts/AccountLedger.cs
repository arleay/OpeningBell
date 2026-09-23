using System;
using System.Collections.Generic;

namespace OpeningBell.Trading
{
    public enum LedgerEntryType
    {
        Deposit,
        Withdrawal,
        TradeBuy,
        TradeSell,
        Commission,
        /// <summary>Realized result of closing contracts (futures-style: no cash for the contracts themselves).</summary>
        TradePnL,
    }

    public sealed class LedgerEntry
    {
        public long Id { get; }
        public DateTime Time { get; }
        public LedgerEntryType Type { get; }

        /// <summary>Signed cash change.</summary>
        public decimal Amount { get; }

        public decimal BalanceAfter { get; }
        public string Ticker { get; }
        public long OrderId { get; }
        public long FillId { get; }
        public string Memo { get; }

        internal LedgerEntry(long id, DateTime time, LedgerEntryType type, decimal amount, decimal balanceAfter,
            string ticker, long orderId, long fillId, string memo)
        {
            Id = id;
            Time = time;
            Type = type;
            Amount = amount;
            BalanceAfter = balanceAfter;
            Ticker = ticker;
            OrderId = orderId;
            FillId = fillId;
            Memo = memo;
        }
    }

    /// <summary>Append-only cash journal. Cash is only ever changed by posting an entry.</summary>
    public sealed class AccountLedger
    {
        private readonly List<LedgerEntry> _entries = new List<LedgerEntry>();

        public IReadOnlyList<LedgerEntry> Entries => _entries;
        public decimal Balance { get; private set; }

        internal void Restore(List<LedgerEntry> entries)
        {
            _entries.Clear();
            _entries.AddRange(entries);
            Balance = entries.Count > 0 ? entries[entries.Count - 1].BalanceAfter : 0m;
        }

        internal LedgerEntry Post(DateTime time, LedgerEntryType type, decimal amount,
            string ticker = null, long orderId = 0, long fillId = 0, string memo = null)
        {
            Balance += amount;
            var entry = new LedgerEntry(_entries.Count + 1, time, type, amount, Balance, ticker, orderId, fillId, memo);
            _entries.Add(entry);
            return entry;
        }
    }
}
