using System;
using System.Collections.Generic;
using OpeningBell.Market;

namespace OpeningBell.Trading
{
    /// <summary>
    /// Cash brokerage account (no margin yet). Positions are marked at the last trade price.
    /// Invariant: Equity − NetDeposits == RealizedPnL + UnrealizedPnL − TotalCommissions.
    /// </summary>
    public sealed class Account
    {
        private readonly IMarketData _market;
        private readonly Dictionary<long, decimal> _reservations = new Dictionary<long, decimal>();

        public AccountLedger Ledger { get; } = new AccountLedger();
        public Portfolio Portfolio { get; } = new Portfolio();

        public decimal Cash => Ledger.Balance;
        public decimal NetDeposits { get; private set; }
        public decimal TotalCommissions { get; private set; }

        /// <summary>Cash held back for open buy orders.</summary>
        public decimal ReservedCash { get; private set; }

        public decimal BuyingPower => Cash - ReservedCash;

        public decimal LongMarketValue => SumOverPositions(longOnly: true, unrealized: false);
        public decimal Equity => Cash + SumOverPositions(longOnly: false, unrealized: false);
        public decimal RealizedPnL => Portfolio.RealizedPnL;
        public decimal UnrealizedPnL => SumOverPositions(longOnly: false, unrealized: true);

        public decimal DayStartEquity { get; private set; }
        public decimal DailyPnL => Equity - DayStartEquity;
        public decimal TotalReturnPercent => NetDeposits == 0m ? 0m : (Equity - NetDeposits) / NetDeposits * 100m;

        public Account(IMarketData market)
        {
            _market = market;
            _market.SessionChanged += OnSessionChanged;
        }

        public decimal MarkPrice(string ticker)
        {
            if (!_market.TryGetQuote(ticker, out var quote)) return 0m;
            return quote.Last > 0m ? quote.Last : quote.Mid;
        }

        public void Deposit(decimal amount, string memo = null)
        {
            if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount), "Deposit must be positive.");
            Ledger.Post(_market.Now, LedgerEntryType.Deposit, amount, memo: memo);
            NetDeposits += amount;
            DayStartEquity += amount; // deposits are not trading P&L
        }

        internal void RestoreTotals(decimal netDeposits, decimal totalCommissions, decimal dayStartEquity)
        {
            NetDeposits = netDeposits;
            TotalCommissions = totalCommissions;
            DayStartEquity = dayStartEquity;
        }

        internal decimal ApplyFill(Fill fill)
        {
            bool buy = fill.Side == OrderSide.Buy;
            Ledger.Post(fill.Time, buy ? LedgerEntryType.TradeBuy : LedgerEntryType.TradeSell,
                buy ? -fill.Notional : fill.Notional, fill.Ticker, fill.OrderId, fill.Id);

            if (fill.Commission != 0m)
            {
                Ledger.Post(fill.Time, LedgerEntryType.Commission, -fill.Commission, fill.Ticker, fill.OrderId, fill.Id);
                TotalCommissions += fill.Commission;
            }

            return Portfolio.GetOrCreate(fill.Ticker).ApplyFill(buy ? fill.Quantity : -fill.Quantity, fill.Price);
        }

        internal decimal ReservationFor(long orderId) => _reservations.TryGetValue(orderId, out var amount) ? amount : 0m;

        internal void SetReservation(long orderId, decimal amount)
        {
            ReservedCash -= ReservationFor(orderId);
            if (amount > 0m)
            {
                _reservations[orderId] = amount;
                ReservedCash += amount;
            }
            else
            {
                _reservations.Remove(orderId);
            }
        }

        private decimal SumOverPositions(bool longOnly, bool unrealized)
        {
            decimal total = 0m;
            foreach (var p in Portfolio.Positions)
            {
                if (!p.IsOpen || (longOnly && p.Quantity < 0)) continue;
                decimal mark = MarkPrice(p.Ticker);
                total += unrealized ? p.UnrealizedPnL(mark) : p.MarketValue(mark);
            }
            return total;
        }

        private void OnSessionChanged(MarketSession previous, MarketSession current)
        {
            if (previous == MarketSession.Closed && current != MarketSession.Closed)
                DayStartEquity = Equity;
        }
    }
}
