using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpeningBell.Trading
{
    // Save models (JsonUtility-friendly). Money is a decimal string, so any scale round-trips exactly. Times are ticks.

    [Serializable]
    public sealed class TradingSaveData
    {
        public string NetDeposits, TotalCommissions, DayStartEquity;
        public List<LedgerEntrySaveData> Ledger = new List<LedgerEntrySaveData>();
        public List<PositionSaveData> Positions = new List<PositionSaveData>();
        public long NextOrderId, NextFillId;
        public List<OrderSaveData> Orders = new List<OrderSaveData>();
        public List<FillSaveData> Fills = new List<FillSaveData>();
        public List<DayReportSaveData> CompletedDays = new List<DayReportSaveData>();
        public bool HasCurrentDay;
        public DayReportSaveData CurrentDay = new DayReportSaveData();
    }

    [Serializable]
    public sealed class LedgerEntrySaveData
    {
        public long Id, Time, OrderId, FillId;
        public int Type;
        public string Amount, BalanceAfter, Ticker, Memo;
    }

    [Serializable]
    public sealed class PositionSaveData
    {
        public string Ticker;
        public long Quantity;
        public string CostBasis, RealizedPnL;
    }

    [Serializable]
    public sealed class OrderSaveData
    {
        public long Id, Quantity, FilledQuantity, SubmittedAt, UpdatedAt;
        public string Ticker, LimitPrice, StatusReason, FilledNotional, Commission, ReservePrice;
        public int Side, Type, Status;
        public bool IsResting;
    }

    [Serializable]
    public sealed class FillSaveData
    {
        public long Id, OrderId, Quantity, Time;
        public string Ticker, Price, Commission, RealizedPnL;
        public int Side;
    }

    [Serializable]
    public sealed class DayReportSaveData
    {
        public long Date;
        public int DayNumber, Fills, Winners, Losers;
        public bool IsComplete;
        public string StartEquity, EndEquity, RealizedPnL, Commissions, BestTrade, WorstTrade;
    }

    /// <summary>Captures and restores account, positions, orders, fills and day reports.</summary>
    public static class TradingState
    {
        public static TradingSaveData Capture(Account account, OrderManager orders, TradingDayRecorder days,
            int maxOrders = 200, int maxFills = 500)
        {
            var data = new TradingSaveData
            {
                NetDeposits = S(account.NetDeposits),
                TotalCommissions = S(account.TotalCommissions),
                DayStartEquity = S(account.DayStartEquity),
                NextOrderId = orders.NextOrderId,
                NextFillId = orders.NextFillId,
            };

            foreach (LedgerEntry e in account.Ledger.Entries)
                data.Ledger.Add(new LedgerEntrySaveData
                {
                    Id = e.Id, Time = e.Time.Ticks, OrderId = e.OrderId, FillId = e.FillId, Type = (int)e.Type,
                    Amount = S(e.Amount), BalanceAfter = S(e.BalanceAfter), Ticker = e.Ticker ?? "", Memo = e.Memo ?? "",
                });

            foreach (Position p in account.Portfolio.Positions)
                data.Positions.Add(new PositionSaveData
                {
                    Ticker = p.Ticker, Quantity = p.Quantity, CostBasis = S(p.CostBasis), RealizedPnL = S(p.RealizedPnL),
                });

            // Recent history plus every open order (open orders are always recent: all orders are day orders).
            var all = orders.Orders;
            for (int i = 0; i < all.Count; i++)
            {
                Order o = all[i];
                if (i < all.Count - maxOrders && !o.IsOpen) continue;
                data.Orders.Add(new OrderSaveData
                {
                    Id = o.Id, Ticker = o.Ticker, Side = (int)o.Side, Type = (int)o.Type, Status = (int)o.Status,
                    Quantity = o.Quantity, LimitPrice = S(o.LimitPrice), StatusReason = o.StatusReason ?? "",
                    SubmittedAt = o.SubmittedAt.Ticks, UpdatedAt = o.UpdatedAt.Ticks, FilledQuantity = o.FilledQuantity,
                    FilledNotional = S(o.FilledNotional), Commission = S(o.Commission), ReservePrice = S(o.ReservePrice),
                    IsResting = o.IsResting,
                });
            }

            var fills = orders.Fills;
            for (int i = Math.Max(0, fills.Count - maxFills); i < fills.Count; i++)
            {
                Fill f = fills[i];
                data.Fills.Add(new FillSaveData
                {
                    Id = f.Id, OrderId = f.OrderId, Ticker = f.Ticker, Side = (int)f.Side, Quantity = f.Quantity,
                    Price = S(f.Price), Commission = S(f.Commission), RealizedPnL = S(f.RealizedPnL), Time = f.Time.Ticks,
                });
            }

            foreach (TradingDayReport r in days.Completed) data.CompletedDays.Add(Capture(r));
            if (days.Current != null)
            {
                data.HasCurrentDay = true;
                data.CurrentDay = Capture(days.Current);
            }
            return data;
        }

        /// <summary>Restores into freshly constructed objects (no deposits, orders or fills yet).</summary>
        public static void Restore(TradingSaveData data, Account account, OrderManager orders, TradingDayRecorder days)
        {
            var entries = new List<LedgerEntry>();
            foreach (LedgerEntrySaveData e in data.Ledger)
                entries.Add(new LedgerEntry(e.Id, new DateTime(e.Time), (LedgerEntryType)e.Type, D(e.Amount), D(e.BalanceAfter),
                    Nullable(e.Ticker), e.OrderId, e.FillId, Nullable(e.Memo)));
            account.Ledger.Restore(entries);
            account.RestoreTotals(D(data.NetDeposits), D(data.TotalCommissions), D(data.DayStartEquity));

            foreach (PositionSaveData p in data.Positions)
                account.Portfolio.GetOrCreate(p.Ticker).Restore(p.Quantity, D(p.CostBasis), D(p.RealizedPnL));

            var fills = new List<Fill>();
            foreach (FillSaveData f in data.Fills)
                fills.Add(new Fill(f.Id, f.OrderId, f.Ticker, (OrderSide)f.Side, f.Quantity, D(f.Price), D(f.Commission), new DateTime(f.Time))
                    { RealizedPnL = D(f.RealizedPnL) });

            var restoredOrders = new List<Order>();
            foreach (OrderSaveData o in data.Orders)
            {
                var order = new Order(o.Id, o.Ticker, (OrderSide)o.Side, (OrderType)o.Type, o.Quantity, D(o.LimitPrice), new DateTime(o.SubmittedAt));
                order.Restore((OrderStatus)o.Status, Nullable(o.StatusReason), new DateTime(o.UpdatedAt), o.FilledQuantity,
                    D(o.FilledNotional), D(o.Commission), o.IsResting, D(o.ReservePrice));
                foreach (Fill f in fills)
                    if (f.OrderId == order.Id) order.AttachFill(f);
                restoredOrders.Add(order);
            }
            orders.Restore(data.NextOrderId, data.NextFillId, restoredOrders, fills);

            var completed = new List<TradingDayReport>();
            foreach (DayReportSaveData r in data.CompletedDays) completed.Add(Restore(r));
            days.Restore(completed, data.HasCurrentDay ? Restore(data.CurrentDay) : null);
        }

        private static DayReportSaveData Capture(TradingDayReport r) => new DayReportSaveData
        {
            Date = r.Date.Ticks, DayNumber = r.DayNumber, Fills = r.Fills, Winners = r.Winners, Losers = r.Losers,
            IsComplete = r.IsComplete, StartEquity = S(r.StartEquity), EndEquity = S(r.EndEquity), RealizedPnL = S(r.RealizedPnL),
            Commissions = S(r.Commissions), BestTrade = S(r.BestTrade), WorstTrade = S(r.WorstTrade),
        };

        private static TradingDayReport Restore(DayReportSaveData r) => new TradingDayReport
        {
            Date = new DateTime(r.Date), DayNumber = r.DayNumber, Fills = r.Fills, Winners = r.Winners, Losers = r.Losers,
            IsComplete = r.IsComplete, StartEquity = D(r.StartEquity), EndEquity = D(r.EndEquity), RealizedPnL = D(r.RealizedPnL),
            Commissions = D(r.Commissions), BestTrade = D(r.BestTrade), WorstTrade = D(r.WorstTrade),
        };

        private static string S(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        private static decimal D(string value) =>
            string.IsNullOrEmpty(value) ? 0m : decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

        private static string Nullable(string value) => string.IsNullOrEmpty(value) ? null : value;
    }
}
