namespace AllIn.Core.Actions
{
    /// <summary>D5 "검진·운동": takes a day slot and 50,000 won; every 10th one (while health debt exists) cancels 1 debt. Ends the slot.</summary>
    public sealed class CheckupAction : IAction
    {
        public static readonly CheckupAction Instance = new CheckupAction();

        public string Id => "health.checkup";
        public bool EndsSlot => true;
        public SlotMask Slots => SlotMask.Day;
        public long CashCost(Loop loop) => Rules.CheckupCost;

        public Availability Check(Loop loop)
        {
            if (loop.Data.bankCash < Rules.CheckupCost) return Availability.No(Texts.CheckupNoCash);
            return Availability.Yes;
        }

        public ActionResult Execute(Loop loop)
        {
            var d = loop.Data;
            loop.SpendBank(Rules.CheckupCost);
            d.stats.checkups++;
            if (d.healthDebt > 0 && ++d.checkupProgress >= Rules.CheckupsPerDebtRelief)
            {
                d.checkupProgress = 0;
                d.healthDebt--;
            }
            return ActionResult.Done(Texts.CheckupDone);
        }
    }
}
