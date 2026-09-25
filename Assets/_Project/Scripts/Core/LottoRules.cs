using System;

namespace AllIn.Core
{
    /// <summary>
    /// Real 6/45 draws — no probability table anywhere. Odds fall out of the draw itself;
    /// the theoretical table in DESIGN.md B6 exists only as a test reference.
    /// </summary>
    public static class LottoRules
    {
        /// <summary>6 distinct numbers from 1..45, sorted.</summary>
        public static int[] PickGame(IRng rng) => Pick(rng, Rules.LottoPicks, sortAll: true);

        /// <summary>7 distinct numbers: [0..5] the main six (sorted), [6] the bonus.</summary>
        public static int[] Draw(IRng rng) => Pick(rng, Rules.LottoPicks + 1, sortAll: false);

        static int[] Pick(IRng rng, int count, bool sortAll)
        {
            var pool = new int[Rules.LottoMaxNumber];
            for (int i = 0; i < pool.Length; i++) pool[i] = i + 1;
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                int j = i + rng.NextInt(pool.Length - i); // partial Fisher–Yates
                (pool[i], pool[j]) = (pool[j], pool[i]);
                result[i] = pool[i];
            }
            Array.Sort(result, 0, sortAll ? count : Rules.LottoPicks);
            return result;
        }

        /// <summary>0 = no prize, 1..5 = rank. <paramref name="game"/> starts at <paramref name="offset"/> (6 numbers).</summary>
        public static int Rank(int[] game, int offset, int[] drawn)
        {
            int match = 0;
            bool bonus = false;
            for (int i = 0; i < Rules.LottoPicks; i++)
            {
                int n = game[offset + i];
                if (Array.IndexOf(drawn, n, 0, Rules.LottoPicks) >= 0) match++;
                else if (n == drawn[Rules.LottoPicks]) bonus = true;
            }
            switch (match)
            {
                case 6: return 1;
                case 5: return bonus ? 2 : 3;
                case 4: return 4;
                case 3: return 5;
                default: return 0;
            }
        }

        public static int Rank(int[] game, int[] drawn) => Rank(game, 0, drawn);

        /// <summary>≤ 2M tax-free; otherwise 22% up to 300M, plus 33% on the part above 300M (progressive).</summary>
        public static long Tax(long gross)
        {
            if (gross <= Rules.LottoTaxFreeUpTo) return 0;
            double tax = Math.Min(gross, Rules.LottoMidTaxUpTo) * Rules.LottoMidTax;
            if (gross > Rules.LottoMidTaxUpTo) tax += (gross - Rules.LottoMidTaxUpTo) * Rules.LottoHighTax;
            return (long)Math.Round(tax);
        }

        public static long NetPrize(int rank) => rank <= 0 ? 0 : Rules.LottoPrize[rank] - Tax(Rules.LottoPrize[rank]);
    }
}
