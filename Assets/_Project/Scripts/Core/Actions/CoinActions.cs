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
            if (d.overseasCash <= 0 || Commit > d.overseasCash || Commit <= 0) return Availability.No(Texts.CoinNoCash);
            if (Commit < loop.MinCommit) return Availability.No(Texts.CoinBelowMin);
            return Availability.Yes;
        }

        public ActionResult Execute(Loop loop)
        {
            var d = loop.Data;
            loop.SpendOverseas(Commit);
            d.position = CoinRules.Open(Direction, Leverage, Commit, d.price, loop.AbsSlot);
            d.hasPosition = true;
            d.lastLeverage = Leverage;
            if (d.tilted) d.stats.rageBets++;
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
            long realized = loop.ClosePositionAt(loop.Data.price);
            return ActionResult.Done(Texts.CoinClose + " " + Fmt.SignedWon(realized));
        }
    }
}
