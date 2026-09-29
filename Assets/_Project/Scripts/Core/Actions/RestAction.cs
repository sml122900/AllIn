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
            var d = loop.Data;
            if (d.slot == Slot.Night)
            {
                if (d.peekedExchangeAtNight)
                {
                    // B5c "잠 못 듦": opened the coin/exchange screen during a locked night — half recovery.
                    loop.AddBody(Rules.BadSleepBody);
                    d.peekedExchangeAtNight = false;
                }
                else
                {
                    loop.AddBody(Rules.SleepBody);
                    loop.AddMental(Rules.SleepMental);
                }
                d.tilted = false; // B5b: tilt ends at the next successful sleep, day-rest doesn't clear it
                return ActionResult.Done(Texts.Slept);
            }
            loop.AddBody(Rules.RestBody);
            return ActionResult.Done(Texts.Rested);
        }
    }
}
