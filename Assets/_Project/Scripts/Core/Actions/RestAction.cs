namespace AllIn.Core.Actions
{
    /// <summary>Doing nothing with the slot: rest by day (body +5), sleep at night (body +30, mental +3).</summary>
    public sealed class RestAction : IAction
    {
        public static readonly RestAction Instance = new RestAction();

        public string Id => "rest";
        public bool EndsSlot => true;
        public SlotMask Slots => SlotMask.All;
        public long CashCost(Loop loop) => 0;
        public Availability Check(Loop loop) => Availability.Yes;

        public ActionResult Execute(Loop loop)
        {
            if (loop.Data.slot == Slot.Night)
            {
                loop.AddBody(Rules.SleepBody);
                loop.AddMental(Rules.SleepMental);
                return ActionResult.Done(Texts.Slept);
            }
            loop.AddBody(Rules.RestBody);
            return ActionResult.Done(Texts.Rested);
        }
    }
}
