using System;

namespace AllIn.Core
{
    public interface IPriceModel
    {
        double Next(double price, IRng rng);
    }

    /// <summary>Per-tick log return r = t(3) × scale, |r| capped. Scale, not std-dev: the point is fat tails.</summary>
    public sealed class StudentTPriceModel : IPriceModel
    {
        public static readonly StudentTPriceModel Instance = new StudentTPriceModel();

        public double Next(double price, IRng rng) => price * Math.Exp(LogReturn(rng));

        public static double LogReturn(IRng rng)
        {
            double r = rng.NextStudentT3() * Rules.CoinTickScale;
            return Math.Max(-Rules.CoinTickCap, Math.Min(Rules.CoinTickCap, r));
        }
    }
}
