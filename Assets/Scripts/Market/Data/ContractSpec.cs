using System;

namespace OpeningBell.Market
{
    /// <summary>
    /// How a symbol trades as a futures-style contract: dollars per 1.00 price move per contract (NQ: $20 a point)
    /// and the day margin posted per contract. Fixed for the game, like a real contract's specs.
    /// </summary>
    public readonly struct ContractSpec
    {
        /// <summary>A typical day's move (one daily volatility) is worth about this much per contract.</summary>
        public const double DollarsPerDailyMove = 500;

        /// <summary>Day margin covers about this many typical daily moves per contract.</summary>
        public const double MarginDailyMoves = 2;

        private static readonly double[] NiceValues = { 1, 2, 2.5, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 2500, 5000, 10000 };

        public readonly decimal PointValue;
        public readonly decimal Margin;

        public ContractSpec(decimal pointValue, decimal margin)
        {
            PointValue = pointValue;
            Margin = margin;
        }

        /// <summary>Dollar value of one tick-size move ($0.01 above $1) per contract.</summary>
        public decimal TickValue(decimal price) => PriceTick.For(price) * PointValue;

        /// <summary>
        /// The spec's own values when set; otherwise sized from its base price and volatility so every symbol's
        /// contract moves about the same dollars on a normal day, rounded to a clean point value.
        /// </summary>
        public static ContractSpec For(SecuritySpec spec)
        {
            double dailyMove = Math.Max(1e-6, spec.BasePrice * Math.Max(0.001, spec.DailyVolatility));
            double pointValue = spec.PointValue > 0 ? spec.PointValue : Nice(DollarsPerDailyMove / dailyMove);
            double margin = spec.DayMargin > 0
                ? spec.DayMargin
                : Math.Max(100, Math.Round(MarginDailyMoves * dailyMove * pointValue / 100) * 100);
            return new ContractSpec((decimal)pointValue, (decimal)margin);
        }

        /// <summary>Nearest value on a 1-2-2.5-5 ladder (in log terms).</summary>
        private static double Nice(double raw)
        {
            double best = NiceValues[0];
            foreach (double v in NiceValues)
                if (Math.Abs(Math.Log(v / raw)) < Math.Abs(Math.Log(best / raw))) best = v;
            return best;
        }
    }
}
