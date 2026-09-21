using System;

namespace OpeningBell.Trading
{
    /// <summary>Broker-specific costs and execution behavior. Better brokers later = different values.</summary>
    [Serializable]
    public sealed class BrokerRules
    {
        public string Name = "PennyBridge";

        public double CommissionPerShare = 0.005;
        public double MinimumCommission = 1.0;

        /// <summary>Cap as a percentage of order value, so tiny orders are not charged the full minimum.</summary>
        public double MaximumCommissionPercent = 1.0;

        public bool AllowMarketOrdersOutsideRegularHours = false;

        /// <summary>Price levels a marketable order may consume per tick; the rest keeps working next tick.</summary>
        public int BookLevelsPerTick = 5;

        /// <summary>Each deeper level shows this much more size than the inside (0.5 → 1×, 1.5×, 2×…).</summary>
        public double BookLevelSizeGrowth = 0.5;

        /// <summary>Extra cash held for market buys so slippage cannot overdraw the account.</summary>
        public double MarketBuyReservePercent = 5;

        /// <summary>Total commission for an order that has filled this many shares for this notional.</summary>
        public decimal CommissionFor(long quantity, decimal notional)
        {
            if (quantity <= 0) return 0m;
            decimal fee = Math.Max(quantity * (decimal)CommissionPerShare, (decimal)MinimumCommission);
            decimal cap = notional * (decimal)MaximumCommissionPercent / 100m;
            return Money.RoundCents(Math.Min(fee, cap));
        }
    }
}
