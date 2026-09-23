using System;
using System.Collections.Generic;

namespace OpeningBell.Trading
{
    public enum OrderSide
    {
        Buy,
        Sell,
    }

    // Saved by value: append only.
    public enum OrderType
    {
        Market,
        Limit,
        /// <summary>Waits until price trades at or through StopPrice, then becomes a market order.</summary>
        Stop,
        /// <summary>Waits until price trades at or through StopPrice, then becomes a limit order at LimitPrice.</summary>
        StopLimit,
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
        public long Quantity { get; private set; }

        /// <summary>0 for market and stop orders.</summary>
        public decimal LimitPrice { get; internal set; }

        /// <summary>Trigger price for stop and stop-limit orders (0 otherwise).</summary>
        public decimal StopPrice { get; internal set; }

        /// <summary>A stop that has been triggered now works as a market (Stop) or limit (StopLimit) order.</summary>
        public bool Triggered { get; internal set; }

        /// <summary>One-cancels-other group (a take-profit and stop-loss pair); 0 = not linked.</summary>
        public long OcoGroup { get; internal set; }

        /// <summary>Good 'til cancelled: survives the close (brackets protecting a position overnight).</summary>
        public bool Gtc { get; internal set; }

        public bool IsStop => Type == OrderType.Stop || Type == OrderType.StopLimit;

        /// <summary>How the order executes right now: an untriggered stop doesn't; a triggered one is market or limit.</summary>
        public OrderType ActiveType => Type switch
        {
            OrderType.Stop => OrderType.Market,
            OrderType.StopLimit => OrderType.Limit,
            _ => Type,
        };

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

        /// <summary>Shrinks an order (the other leg of a bracket filled, or the position got smaller).</summary>
        internal void ReduceTo(long quantity) => Quantity = Math.Max(FilledQuantity, quantity);

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
