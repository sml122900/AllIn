using System;

namespace AllIn.Core
{
    /// <summary>Pure futures math (DESIGN.md B5). No state, no randomness.</summary>
    public static class CoinRules
    {
        public static bool IsValidLeverage(int leverage) => Array.IndexOf(Rules.Leverages, leverage) >= 0;

        public static long Fee(double notional) => (long)Math.Round(notional * Rules.FeeRate);

        /// <summary>Builds the position for committing C won at this price. Entry fee comes out of C.</summary>
        public static Position Open(Direction dir, int leverage, long committed, long price, int absSlot)
        {
            double notional = (double)committed * leverage;
            long fee = Fee(notional);
            return new Position
            {
                direction = dir,
                leverage = leverage,
                committed = committed,
                margin = committed - fee,
                entryPrice = price,
                openedAtSlot = absSlot,
            };
        }

        public static double EntryFee(Position p) => p.committed - p.margin;

        public static double Notional(Position p) => (double)p.committed * p.leverage;

        /// <summary>Coins held: notional / entry price. Derived, never stored.</summary>
        public static double Quantity(Position p) => Notional(p) / p.entryPrice;

        public static double UnrealizedPnl(Position p, long price) => Quantity(p) * (price - p.entryPrice) * (int)p.direction;

        /// <summary>Margin + any added margin (B5a) — added margin moves the liquidation price but never the notional/quantity.</summary>
        public static long TotalMargin(Position p) => p.margin + p.addedMargin;

        public static double LiquidationLoss(Position p) => TotalMargin(p) * Rules.LiquidationRatio;

        /// <summary>Loss reaches margin × 0.9 → liquidated.</summary>
        public static bool IsLiquidated(Position p, long price) => -UnrealizedPnl(p, price) >= LiquidationLoss(p);

        /// <summary>Watch scene trigger (B5a): loss has passed Rules.WatchLossRatio of total margin.</summary>
        public static bool IsAtWatchThreshold(Position p, long price) => -UnrealizedPnl(p, price) >= TotalMargin(p) * Rules.WatchLossRatio;

        public static double LiquidationPrice(Position p) =>
            p.entryPrice - (int)p.direction * LiquidationLoss(p) / Quantity(p);

        /// <summary>Margin + unrealized, floored at 0 (what the position is worth before exit fee).</summary>
        public static long Value(Position p, long price) => Math.Max(0L, TotalMargin(p) + (long)Math.Round(UnrealizedPnl(p, price)));

        public static long ExitFee(Position p, long price) => Fee(Quantity(p) * price);

        /// <summary>Cash returned on a manual close: margin + PnL − exit fee, never negative.</summary>
        public static long CloseProceeds(Position p, long price) =>
            Math.Max(0L, TotalMargin(p) + (long)Math.Round(UnrealizedPnl(p, price)) - ExitFee(p, price));

        /// <summary>B5b recovery calculator: price-move fraction needed, betting `stake` at `leverage`, to recoup `lost`.</summary>
        public static double RecoveryRateNeeded(long lost, long stake, int leverage) =>
            stake <= 0 || leverage <= 0 ? double.PositiveInfinity : (double)lost / (stake * (double)leverage);

        /// <summary>B5d ghost: "살아 있었다면" PnL, as if the liquidated position (fixed notional = committed × leverage,
        /// addedMargin doesn't affect notional) had never closed.</summary>
        public static double GhostUnrealizedPnl(Ghost g, long price)
        {
            double notional = (double)g.committed * g.leverage;
            double qty = notional / g.entryPrice;
            return qty * (price - g.entryPrice) * (int)g.direction;
        }

        /// <summary>Mental change for a manual close relative to total assets after closing.</summary>
        public static int CloseMentalDelta(long realized, long totalAfter)
        {
            double denom = Math.Max(1L, totalAfter);
            if (realized < 0)
                return -Clamp((int)Math.Round(-realized / denom * 100), Rules.CloseLossMentalMin, Rules.CloseLossMentalMax);
            return Clamp((int)Math.Round(realized / denom * 100), Rules.CloseGainMentalMin, Rules.CloseGainMentalMax);
        }

        /// <summary>E4 calculator: daily return = realized return / held days (min 1); projection = total × (1+r)^daysLeft.</summary>
        public static double HopeDailyReturn(LastWin w) => w.realized / (double)Math.Max(1L, w.committed) / Math.Max(1.0, w.heldTicks / (double)Rules.SlotsPerDay);

        public static double HopeProjection(long total, LastWin w, int daysLeft) => total * Math.Pow(1 + HopeDailyReturn(w), daysLeft);

        /// <summary>Leverage the slider starts at: last used, or the next step up while the hope circuit runs.</summary>
        public static int SuggestedLeverage(int lastLeverage, int mental)
        {
            int i = Array.IndexOf(Rules.Leverages, lastLeverage);
            if (i < 0) return Rules.DefaultLeverage;
            if (mental >= Rules.HopeCircuitAt && i < Rules.Leverages.Length - 1) i++;
            return Rules.Leverages[i];
        }

        /// <summary>Minimum commit: 1 won normally, cash × 0.25 while chasing losses (mental &lt; 30).</summary>
        public static long MinCommit(long cash, int mental) =>
            mental < Rules.ChasingLossesBelow ? Math.Max(1L, (long)Math.Ceiling(cash * Rules.ChasingMinMarginOfCash)) : 1L;

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
