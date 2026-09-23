using System;

namespace OpeningBell.Trading
{
    /// <summary>Broker-specific costs and execution behavior. Better brokers later = different values.</summary>
    [Serializable]
    public sealed class BrokerRules
    {
        public string Name = "PennyBridge";

        /// <summary>Per contract, per side (entering and exiting are each charged).</summary>
        public double CommissionPerContract = 2.5;

        public bool AllowMarketOrdersOutsideRegularHours = false;

        /// <summary>Price levels a marketable order may consume per tick; the rest keeps working next tick.</summary>
        public int BookLevelsPerTick = 5;

        /// <summary>Each deeper level shows this much more size than the inside (0.5 → 1×, 1.5×, 2×…).</summary>
        public double BookLevelSizeGrowth = 0.5;

        /// <summary>Cushion on a market buy's price estimate (kept on the order; margin itself is per contract).</summary>
        public double MarketBuyReservePercent = 5;

        /// <summary>Total commission for this many contracts.</summary>
        public decimal CommissionFor(long contracts) =>
            contracts <= 0 ? 0m : Money.RoundCents(contracts * (decimal)CommissionPerContract);
    }
}
