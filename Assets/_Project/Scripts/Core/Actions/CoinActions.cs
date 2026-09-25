namespace AllIn.Core.Actions
{
    /// <summary>Open the single coin-futures position. Takes the slot (at night: an all-nighter).</summary>
    public sealed class CoinOpenAction : IAction
    {
        public readonly Direction Direction;
        public readonly int Leverage;
        public readonly long Commit;

        public CoinOpenAction(Direction direction, int leverage, long commit)
        {
            Direction = direction;
            Leverage = leverage;
            Commit = commit;
        }

        public string Id => "coin.open";
        public bool EndsSlot => true;
        public SlotMask Slots => SlotMask.All;
        public long CashCost(Loop loop) => Commit;

        public Availability Check(Loop loop)
        {
            var d = loop.Data;
            if (d.hasPosition) return Availability.No(Texts.CoinAlreadyOpen);
            if (!CoinRules.IsValidLeverage(Leverage)) return Availability.No(Texts.CoinBadLeverage);
            if (d.cash <= 0 || Commit > d.cash || Commit <= 0) return Availability.No(Texts.CoinNoCash);
            if (Commit < loop.MinCommit) return Availability.No(Texts.CoinBelowMin);
            return Availability.Yes;
        }

        public ActionResult Execute(Loop loop)
        {
            var d = loop.Data;
            loop.SpendCash(Commit);
            d.position = CoinRules.Open(Direction, Leverage, Commit, d.price, loop.AbsSlot);
            d.hasPosition = true;
            d.lastLeverage = Leverage;
            return ActionResult.Done(Texts.CoinOpen + " " + (Direction == Direction.Long ? Texts.CoinLong : Texts.CoinShort) +
                                     " x" + Leverage + " " + Fmt.Won(Commit));
        }
    }

    /// <summary>Close the position at the current price. Takes the slot — so it can never happen in the slot it was opened.</summary>
    public sealed class CoinCloseAction : IAction
    {
        public static readonly CoinCloseAction Instance = new CoinCloseAction();

        public string Id => "coin.close";
        public bool EndsSlot => true;
        public SlotMask Slots => SlotMask.All;
        public long CashCost(Loop loop) => 0;

        public Availability Check(Loop loop)
        {
            if (!loop.Data.hasPosition) return Availability.No(Texts.CoinNoPosition);
            return Availability.Yes;
        }

        public ActionResult Execute(Loop loop)
        {
            var d = loop.Data;
            var p = d.position;
            long proceeds = CoinRules.CloseProceeds(p, d.price);
            long realized = proceeds - p.committed;
            d.hasPosition = false;
            loop.AddCash(proceeds);
            d.stats.coinRealized += realized;
            loop.AddMental(CoinRules.CloseMentalDelta(realized, loop.TotalAssets));
            if (realized > 0)
            {
                d.hasLastWin = true;
                d.lastWin = new LastWin { realized = realized, committed = p.committed, heldTicks = loop.AbsSlot - p.openedAtSlot };
                loop.TryHopeAfterWin();
            }
            return ActionResult.Done(Texts.CoinClose + " " + Fmt.SignedWon(realized));
        }
    }
}
