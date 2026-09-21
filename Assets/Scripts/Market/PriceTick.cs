using System;

namespace OpeningBell.Market
{
    /// <summary>Minimum price increments: $0.01 at or above $1, $0.0001 below.</summary>
    public static class PriceTick
    {
        public const decimal Standard = 0.01m;
        public const decimal SubDollar = 0.0001m;

        public static decimal For(decimal price) => price >= 1m ? Standard : SubDollar;

        public static decimal RoundDown(decimal price, decimal tick) => Math.Floor(price / tick) * tick;

        public static decimal RoundUp(decimal price, decimal tick) => Math.Ceiling(price / tick) * tick;

        public static decimal RoundNearest(decimal price)
        {
            decimal tick = For(price);
            return Math.Round(price / tick, MidpointRounding.AwayFromZero) * tick;
        }

        public static bool IsOnGrid(decimal price) => price % For(price) == 0m;
    }
}
