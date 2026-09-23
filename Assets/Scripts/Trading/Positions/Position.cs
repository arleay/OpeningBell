using System;

namespace OpeningBell.Trading
{
    public enum PositionDirection
    {
        Flat,
        Long,
        Short,
    }

    /// <summary>
    /// Average-price futures-style position in contracts. Cost basis is kept in price points (Σ contracts × price)
    /// so the average stays exact; money is points × the contract's point value. Partial exits remove a pro-rata
    /// share of cost, a full exit whatever remains. Quantity is signed (negative = short).
    /// </summary>
    public sealed class Position
    {
        public string Ticker { get; }
        public long Quantity { get; private set; }

        /// <summary>Signed Σ contracts × entry price, in price points (not dollars).</summary>
        public decimal CostBasis { get; private set; }

        /// <summary>Dollars per 1.00 price move per contract.</summary>
        public decimal PointValue { get; }

        public decimal RealizedPnL { get; private set; }

        public PositionDirection Direction =>
            Quantity > 0 ? PositionDirection.Long : Quantity < 0 ? PositionDirection.Short : PositionDirection.Flat;

        public bool IsOpen => Quantity != 0;
        public decimal AveragePrice => Quantity == 0 ? 0m : CostBasis / Quantity;

        /// <summary>Notional exposure (contracts × price × point value); futures post margin, not this.</summary>
        public decimal MarketValue(decimal markPrice) => Quantity * markPrice * PointValue;
        public decimal UnrealizedPnL(decimal markPrice) => Money.RoundCents((Quantity * markPrice - CostBasis) * PointValue);

        internal Position(string ticker, decimal pointValue)
        {
            Ticker = ticker;
            PointValue = pointValue;
        }

        internal void Restore(long quantity, decimal costBasis, decimal realizedPnL)
        {
            Quantity = quantity;
            CostBasis = costBasis;
            RealizedPnL = realizedPnL;
        }

        /// <summary>Applies a fill (positive quantity buys, negative sells) and returns the realized P&L in dollars.</summary>
        internal decimal ApplyFill(long signedQuantity, decimal price)
        {
            if (signedQuantity == 0) throw new ArgumentException("Fill quantity cannot be zero.", nameof(signedQuantity));

            if (Quantity == 0 || Math.Sign(Quantity) == Math.Sign(signedQuantity))
            {
                Quantity += signedQuantity;
                CostBasis += signedQuantity * price;
                return 0m;
            }

            long held = Math.Abs(Quantity);
            long closing = Math.Min(Math.Abs(signedQuantity), held);
            decimal removedCost = closing == held ? CostBasis : Math.Round(CostBasis * closing / held, 8, MidpointRounding.AwayFromZero);
            decimal realized = Money.RoundCents((Math.Sign(Quantity) * closing * price - removedCost) * PointValue);

            Quantity += Math.Sign(signedQuantity) * closing;
            CostBasis -= removedCost;
            RealizedPnL += realized;

            // Fill larger than the position: the excess opens a new position on the other side.
            long excess = Math.Abs(signedQuantity) - closing;
            if (excess > 0)
            {
                Quantity = Math.Sign(signedQuantity) * excess;
                CostBasis = Quantity * price;
            }

            return realized;
        }
    }
}
