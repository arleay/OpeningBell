using System;

namespace OpeningBell.Trading
{
    /// <summary>
    /// All money is System.Decimal. Trade notionals (tick price × whole shares) are exact; only derived
    /// allocations such as pro-rata cost basis are rounded, to cents, away from zero.
    /// </summary>
    public static class Money
    {
        public static decimal RoundCents(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
