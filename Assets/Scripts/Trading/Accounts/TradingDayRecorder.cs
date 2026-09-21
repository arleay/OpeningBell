using System;
using System.Collections.Generic;
using OpeningBell.Market;

namespace OpeningBell.Trading
{
    /// <summary>One trading day's results. Live while the day runs; final once the market closes (after-hours included).</summary>
    public sealed class TradingDayReport
    {
        public DateTime Date { get; internal set; }
        public int DayNumber { get; internal set; }
        public decimal StartEquity { get; internal set; }
        public decimal EndEquity { get; internal set; }
        public bool IsComplete { get; internal set; }

        /// <summary>Gross realized P&L from today's closing fills.</summary>
        public decimal RealizedPnL { get; internal set; }

        public decimal Commissions { get; internal set; }
        public int Fills { get; internal set; }

        /// <summary>Closing fills with a gain / loss (a partial exit counts as one).</summary>
        public int Winners { get; internal set; }
        public int Losers { get; internal set; }
        public decimal BestTrade { get; internal set; }
        public decimal WorstTrade { get; internal set; }

        /// <summary>Money moved in (+) or out (−) of the brokerage during the day.</summary>
        public decimal Transfers { get; internal set; }

        /// <summary>Trading result for the day: equity change excluding transfers. Valid once complete.</summary>
        public decimal NetPnL => EndEquity - StartEquity - Transfers;

        internal decimal StartNetDeposits;
    }

    /// <summary>
    /// Opens a report when a trading day's first session starts and closes it when the market closes.
    /// Keeps the full history for later statistics and the trading journal.
    /// </summary>
    public sealed class TradingDayRecorder
    {
        private readonly IMarketData _market;
        private readonly Account _account;
        private readonly List<TradingDayReport> _completed = new List<TradingDayReport>();

        public TradingDayReport Current { get; private set; }
        public IReadOnlyList<TradingDayReport> Completed => _completed;

        /// <summary>Day number of the current (or next) trading day, starting at 1.</summary>
        public int DayNumber => Current?.DayNumber ?? _completed.Count + 1;

        public event Action<TradingDayReport> DayCompleted;

        public TradingDayRecorder(IMarketData market, Account account, OrderManager orders)
        {
            _market = market;
            _account = account;
            _market.SessionChanged += OnSessionChanged;
            orders.OrderFilled += OnFill;
            if (market.Session != MarketSession.Closed) Begin();
        }

        internal void Restore(List<TradingDayReport> completed, TradingDayReport current)
        {
            _completed.Clear();
            _completed.AddRange(completed);
            Current = current;
        }

        private void OnSessionChanged(MarketSession previous, MarketSession current)
        {
            if (previous == MarketSession.Closed && current != MarketSession.Closed) Begin();
            else if (current == MarketSession.Closed && Current != null) Finish();
        }

        private void Begin()
        {
            Current = new TradingDayReport
            {
                Date = _market.Now.Date,
                DayNumber = _completed.Count + 1,
                StartEquity = _account.Equity,
                StartNetDeposits = _account.NetDeposits,
            };
        }

        private void Finish()
        {
            TradingDayReport report = Current;
            report.EndEquity = _account.Equity;
            report.Transfers = _account.NetDeposits - report.StartNetDeposits;
            report.IsComplete = true;
            _completed.Add(report);
            Current = null;
            DayCompleted?.Invoke(report);
        }

        private void OnFill(Fill fill)
        {
            if (Current == null) return;
            Current.Fills++;
            Current.Commissions += fill.Commission;
            if (fill.RealizedPnL == 0m) return;

            Current.RealizedPnL += fill.RealizedPnL;
            if (fill.RealizedPnL > 0m)
            {
                Current.Winners++;
                Current.BestTrade = Math.Max(Current.BestTrade, fill.RealizedPnL);
            }
            else
            {
                Current.Losers++;
                Current.WorstTrade = Math.Min(Current.WorstTrade, fill.RealizedPnL);
            }
        }
    }
}
