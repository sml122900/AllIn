using System;

namespace AllIn.Core
{
    public interface IPriceModel
    {
        long Next(long price, IRng rng);
    }

    /// <summary>Per-tick log return r = t(3) × scale, |r| capped. Scale, not std-dev: the point is fat tails.</summary>
    public sealed class StudentTPriceModel : IPriceModel
    {
        public static readonly StudentTPriceModel Instance = new StudentTPriceModel();

        /// <summary>Rounded to whole won (a 100M-won price loses nothing meaningful; saves stay exact).</summary>
        public long Next(long price, IRng rng) => Math.Max(1L, (long)Math.Round(price * Math.Exp(LogReturn(rng))));

        public static double LogReturn(IRng rng)
        {
            double r = rng.NextStudentT3() * Rules.CoinTickScale;
            return Math.Max(-Rules.CoinTickCap, Math.Min(Rules.CoinTickCap, r));
        }
    }

    /// <summary>
    /// B5 v0.3: decomposes a slot's price move (oldPrice -&gt; newPrice, already decided by IPriceModel) into
    /// Rules.SubTicksPerSlot intermediate prices, for tail-risk liquidation checks. Doesn't change the slot's
    /// close-price distribution: the raw draws are linearly corrected (Brownian-bridge approx) so the path's
    /// cumulative log-return sums to exactly ln(newPrice/oldPrice), and the last sub-tick equals newPrice exactly.
    /// </summary>
    public interface ISubTickPath
    {
        long[] Build(long oldPrice, long newPrice, IRng subTickRng);
    }

    public sealed class StudentTSubTickPath : ISubTickPath
    {
        public static readonly StudentTSubTickPath Instance = new StudentTSubTickPath();

        public long[] Build(long oldPrice, long newPrice, IRng subTickRng)
        {
            int n = Rules.SubTicksPerSlot;
            double r = Math.Log((double)newPrice / oldPrice);
            var raw = new double[n];
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                raw[i] = subTickRng.NextStudentT3() * Rules.SubTickScale;
                sum += raw[i];
            }
            double corr = (r - sum) / n;
            var prices = new long[n];
            double logOld = Math.Log(oldPrice);
            double cum = 0;
            for (int i = 0; i < n - 1; i++)
            {
                cum += raw[i] + corr;
                prices[i] = Math.Max(1L, (long)Math.Round(Math.Exp(logOld + cum)));
            }
            prices[n - 1] = newPrice; // exact: matches the unchanged close-price contract
            return prices;
        }
    }

    /// <summary>No tail risk: straight-line interpolation in log-space, doesn't touch the sub-tick RNG stream.
    /// Real gameplay never uses this — it's for tests that care about the close price, not sub-tick noise.</summary>
    public sealed class LinearSubTickPath : ISubTickPath
    {
        public static readonly LinearSubTickPath Instance = new LinearSubTickPath();

        public long[] Build(long oldPrice, long newPrice, IRng subTickRng)
        {
            int n = Rules.SubTicksPerSlot;
            double step = Math.Log((double)newPrice / oldPrice) / n;
            double logOld = Math.Log(oldPrice);
            var prices = new long[n];
            for (int i = 0; i < n - 1; i++) prices[i] = Math.Max(1L, (long)Math.Round(Math.Exp(logOld + step * (i + 1))));
            prices[n - 1] = newPrice;
            return prices;
        }
    }
}
