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
    /// Average-cost position. Tracks total cost basis rather than an average price so money stays exact:
    /// partial exits remove a pro-rata share of cost (rounded to cents), and a full exit removes whatever
    /// remains. Realized + unrealized therefore always reconciles to cash flows exactly.
    /// Quantity is signed (negative = short) so shorting can plug in later without new math.
    /// </summary>
    public sealed class Position
    {
        public string Ticker { get; }
        public long Quantity { get; private set; }

        /// <summary>Signed: positive for longs (cash paid), negative for shorts (cash received).</summary>
        public decimal CostBasis { get; private set; }

        public decimal RealizedPnL { get; private set; }

        public PositionDirection Direction =>
            Quantity > 0 ? PositionDirection.Long : Quantity < 0 ? PositionDirection.Short : PositionDirection.Flat;

        public bool IsOpen => Quantity != 0;
        public decimal AveragePrice => Quantity == 0 ? 0m : CostBasis / Quantity;

        public decimal MarketValue(decimal markPrice) => Quantity * markPrice;
        public decimal UnrealizedPnL(decimal markPrice) => Quantity * markPrice - CostBasis;

        internal Position(string ticker)
        {
            Ticker = ticker;
        }

        /// <summary>Applies a fill (positive quantity buys, negative sells) and returns the realized P&L.</summary>
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
            decimal removedCost = closing == held ? CostBasis : Money.RoundCents(CostBasis * closing / held);
            decimal realized = Math.Sign(Quantity) * closing * price - removedCost;

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
