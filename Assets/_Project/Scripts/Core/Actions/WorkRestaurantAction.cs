namespace AllIn.Core.Actions
{
    /// <summary>Restaurant shift: evenings only, paid on the spot. Ends the slot.</summary>
    public sealed class WorkRestaurantAction : IAction
    {
        public static readonly WorkRestaurantAction Instance = new WorkRestaurantAction();

        public string Id => "work.restaurant";
        public bool EndsSlot => true;
        public SlotMask Slots => SlotMask.Evening;
        public long CashCost(Loop loop) => 0;

        public Availability Check(Loop loop)
        {
            var d = loop.Data;
            if (d.slot != Slot.Evening) return Availability.No(Texts.WorkOnlyEvening);
            if (d.day <= d.firedUntilDay) return Availability.No(Texts.WorkFiredUntil);
            if (d.body < Rules.RestaurantMinBody) return Availability.No(Texts.WorkTooTired);
            return Availability.Yes;
        }

        public ActionResult Execute(Loop loop)
        {
            var d = loop.Data;
            bool mistake = d.mental < Rules.MistakeMentalBelow && loop.Rng.Chance(Rules.MistakeChance);
            loop.AddBank(Rules.RestaurantWage);
            loop.AddBody(-Rules.RestaurantBodyCost);
            loop.AddMental(-Rules.RestaurantMentalCost);
            d.stats.workedSlots++;
            loop.NoteShift(mistake);
            if (!mistake) return ActionResult.Done(Texts.WorkDone);

            d.firedUntilDay = d.day + Rules.FiredDays;
            d.stats.timesFired++;
            loop.AddMental(-Rules.FiredMental);
            loop.Emit(new GameEvent { id = EventIds.Fired });
            return ActionResult.Done(Texts.WorkMistake);
        }
    }
}
