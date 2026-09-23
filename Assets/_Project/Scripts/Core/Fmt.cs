using System;
using System.Globalization;

namespace AllIn.Core
{
    public static class Fmt
    {
        /// <summary>20000000 → "20,000,000원".</summary>
        public static string Won(long amount) => amount.ToString("N0", CultureInfo.InvariantCulture) + "원";

        public static string Won(double amount)
        {
            if (double.IsNaN(amount)) return "?원";
            if (amount >= 9.2e18) return "9,200,000,000,000,000,000+원";
            return Won((long)Math.Round(amount));
        }

        /// <summary>1234567 → "+1,234,567원" / "-1,234,567원".</summary>
        public static string SignedWon(long amount) => (amount > 0 ? "+" : "") + Won(amount);

        public static string Pct(double ratio, int decimals = 1) =>
            (ratio * 100).ToString("F" + decimals, CultureInfo.InvariantCulture) + "%";
    }
}
