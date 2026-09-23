namespace AllIn.Core.Actions
{
    /// <summary>
    /// Everything the player can do goes through this. Slot actions (job, rest) end the current slot;
    /// instant actions (coin, lotto) happen inside it. Extension point: stocks, warehouse loading (night slot
    /// action → all-nighter), construction, "study" for hold'em, hold'em tournaments plug in here.
    /// </summary>
    public interface IAction
    {
        string Id { get; }

        /// <summary>True → performing it ends the slot and advances time.</summary>
        bool EndsSlot { get; }

        /// <summary>Slots in which the action exists at all (bit i = Slot i).</summary>
        SlotMask Slots { get; }

        /// <summary>Cash it takes up front (0 if none).</summary>
        long CashCost(Loop loop);

        /// <summary>Whether it can be done right now, and why not.</summary>
        Availability Check(Loop loop);

        /// <summary>Apply the effects. Only called after Check succeeded. Time advance is the Loop's job.</summary>
        ActionResult Execute(Loop loop);
    }

    [System.Flags]
    public enum SlotMask
    {
        None = 0,
        Dawn = 1 << Slot.Dawn,
        Morning = 1 << Slot.Morning,
        Afternoon = 1 << Slot.Afternoon,
        Evening = 1 << Slot.Evening,
        Night = 1 << Slot.Night,
        Day = Dawn | Morning | Afternoon | Evening,
        All = Day | Night,
    }

    public static class SlotMaskExtensions
    {
        public static bool Has(this SlotMask mask, Slot slot) => (mask & (SlotMask)(1 << (int)slot)) != 0;
    }

    public readonly struct Availability
    {
        public readonly bool Ok;
        public readonly string Reason;
        Availability(bool ok, string reason) { Ok = ok; Reason = reason; }
        public static Availability Yes => new Availability(true, null);
        public static Availability No(string reason) => new Availability(false, reason);
    }

    public readonly struct ActionResult
    {
        public readonly bool Ok;
        public readonly string Message;
        public ActionResult(bool ok, string message) { Ok = ok; Message = message; }
        public static ActionResult Done(string message = null) => new ActionResult(true, message);
        public static ActionResult Refused(string reason) => new ActionResult(false, reason);
    }
}
