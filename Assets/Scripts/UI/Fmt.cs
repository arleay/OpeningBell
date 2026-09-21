using System;
using System.Globalization;

namespace OpeningBell.UI
{
    /// <summary>Display formatting. Invariant culture so numbers never change shape with the OS locale.</summary>
    public static class Fmt
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        public static string Price(decimal price) =>
            price >= 1m || price == 0m ? price.ToString("N2", C) : price.ToString("N4", C);

        public static string Money(decimal value) => (value < 0m ? "-$" : "$") + Math.Abs(value).ToString("N2", C);

        public static string SignedMoney(decimal value) =>
            (value > 0m ? "+$" : value < 0m ? "-$" : "$") + Math.Abs(value).ToString("N2", C);

        /// <summary>A price difference (change, spread), shown with the precision of the price it relates to.</summary>
        public static string PriceDelta(decimal delta, decimal referencePrice, bool signed = true) =>
            (signed ? Sign(delta) : "") + Math.Abs(delta).ToString(referencePrice >= 1m ? "N2" : "N4", C);

        /// <summary>Input is already in percent (1.5 → "+1.50%").</summary>
        public static string Percent(decimal percent) => Sign(percent) + Math.Abs(percent).ToString("N2", C) + "%";

        public static string Volume(long shares)
        {
            if (shares >= 1_000_000_000) return (shares / 1e9).ToString("0.##", C) + "B";
            if (shares >= 1_000_000) return (shares / 1e6).ToString("0.0#", C) + "M";
            if (shares >= 10_000) return (shares / 1e3).ToString("0.#", C) + "K";
            return shares.ToString("N0", C);
        }

        public static string Shares(long quantity) => quantity.ToString("N0", C);

        public static string Clock(DateTime time) => time.ToString("HH:mm:ss", C);

        public static string Date(DateTime time) => time.ToString("ddd MMM d", C);

        private static string Sign(decimal value) => value > 0m ? "+" : value < 0m ? "-" : "";
    }
}
