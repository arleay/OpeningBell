using System;
using System.Collections.Generic;

namespace OpeningBell.Trading
{
    public enum OrderSide
    {
        Buy,
        Sell,
    }

    public enum OrderType
    {
        Market,
        Limit,
    }

    public enum OrderStatus
    {
        Pending,
        Working,
        PartiallyFilled,
        Filled,
        Cancelled,
        Rejected,
    }

    public sealed class Order
    {
        private readonly List<Fill> _fills = new List<Fill>();

        public long Id { get; }
        public string Ticker { get; }
        public OrderSide Side { get; }
        public OrderType Type { get; }
        public long Quantity { get; }

        /// <summary>0 for market orders.</summary>
        public decimal LimitPrice { get; }

        public OrderStatus Status { get; internal set; } = OrderStatus.Pending;
        public string StatusReason { get; internal set; }
        public DateTime SubmittedAt { get; }
        public DateTime UpdatedAt { get; internal set; }

        public long FilledQuantity { get; private set; }
        public long RemainingQuantity => Quantity - FilledQuantity;
        public decimal FilledNotional { get; private set; }
        public decimal AverageFillPrice => FilledQuantity == 0 ? 0m : FilledNotional / FilledQuantity;
        public decimal Commission { get; private set; }
        public IReadOnlyList<Fill> Fills => _fills;
        public bool IsOpen => Status == OrderStatus.Working || Status == OrderStatus.PartiallyFilled;

        /// <summary>Set once the order has survived its first evaluation (see ExecutionEngine).</summary>
        internal bool IsResting;

        /// <summary>Per-share cash held back for an open buy (limit price, or ask plus a slippage cushion).</summary>
        internal decimal ReservePrice;

        internal Order(long id, string ticker, OrderSide side, OrderType type, long quantity, decimal limitPrice, DateTime submittedAt)
        {
            Id = id;
            Ticker = ticker;
            Side = side;
            Type = type;
            Quantity = quantity;
            LimitPrice = limitPrice;
            SubmittedAt = submittedAt;
            UpdatedAt = submittedAt;
        }

        internal void Restore(OrderStatus status, string reason, DateTime updatedAt, long filledQuantity,
            decimal filledNotional, decimal commission, bool isResting, decimal reservePrice)
        {
            Status = status;
            StatusReason = reason;
            UpdatedAt = updatedAt;
            FilledQuantity = filledQuantity;
            FilledNotional = filledNotional;
            Commission = commission;
            IsResting = isResting;
            ReservePrice = reservePrice;
        }

        /// <summary>Re-links a saved fill for display; totals were restored separately.</summary>
        internal void AttachFill(Fill fill) => _fills.Add(fill);

        internal void AddFill(Fill fill)
        {
            _fills.Add(fill);
            FilledQuantity += fill.Quantity;
            FilledNotional += fill.Notional;
            Commission += fill.Commission;
            UpdatedAt = fill.Time;
            Status = RemainingQuantity == 0 ? OrderStatus.Filled : OrderStatus.PartiallyFilled;
        }
    }

    public sealed class Fill
    {
        public long Id { get; }
        public long OrderId { get; }
        public string Ticker { get; }
        public OrderSide Side { get; }
        public long Quantity { get; }
        public decimal Price { get; }
        public decimal Commission { get; }
        public DateTime Time { get; }

        /// <summary>Gross realized P&L this fill produced (commission excluded).</summary>
        public decimal RealizedPnL { get; internal set; }

        public decimal Notional => Quantity * Price;

        internal Fill(long id, long orderId, string ticker, OrderSide side, long quantity, decimal price, decimal commission, DateTime time)
        {
            Id = id;
            OrderId = orderId;
            Ticker = ticker;
            Side = side;
            Quantity = quantity;
            Price = price;
            Commission = commission;
            Time = time;
        }
    }
}
