using System;
using System.Collections.Generic;

namespace OpeningBell.Economy
{
    // Serialized by value in saves: append only, never reorder.
    public enum TransactionKind
    {
        Deposit,
        TransferIn,
        TransferOut,
        Rent,
        Bill,
        Subscription,
        Living,
        Purchase,
        Fee,
    }

    public sealed class BankTransaction
    {
        public long Id { get; }
        public DateTime Time { get; }
        public TransactionKind Kind { get; }

        /// <summary>Signed: positive in, negative out.</summary>
        public decimal Amount { get; }

        public decimal BalanceAfter { get; }
        public string Description { get; }

        internal BankTransaction(long id, DateTime time, TransactionKind kind, decimal amount, decimal balanceAfter, string description)
        {
            Id = id;
            Time = time;
            Kind = kind;
            Amount = amount;
            BalanceAfter = balanceAfter;
            Description = description;
        }

        /// <summary>Bills and fees the player should be told about (daily living costs are routine).</summary>
        public bool IsNotable => Kind == TransactionKind.Rent || Kind == TransactionKind.Bill ||
                                 Kind == TransactionKind.Subscription || Kind == TransactionKind.Fee;
    }

    /// <summary>The player's checking account: pays bills and purchases. Can go negative (overdraft).</summary>
    public sealed class BankAccount
    {
        private readonly List<BankTransaction> _transactions = new List<BankTransaction>();
        private long _nextId = 1;

        public decimal Balance { get; private set; }
        public bool IsOverdrawn => Balance < 0m;
        public IReadOnlyList<BankTransaction> Transactions => _transactions;

        internal long NextId => _nextId;

        internal BankTransaction Post(DateTime time, TransactionKind kind, decimal amount, string description)
        {
            Balance += amount;
            var tx = new BankTransaction(_nextId++, time, kind, amount, Balance, description);
            _transactions.Add(tx);
            return tx;
        }

        internal void Restore(decimal balance, long nextId, List<BankTransaction> transactions)
        {
            Balance = balance;
            _nextId = nextId;
            _transactions.Clear();
            _transactions.AddRange(transactions);
        }
    }
}
