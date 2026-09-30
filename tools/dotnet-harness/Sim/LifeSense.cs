using System;
using AllIn.Core;

namespace AllIn.Harness.Sim
{
    /// <summary>
    /// Sim-bot "life sense" (reply to 742fe1d): the human habits the raw bots lack. Off by default — the C/D acceptance sims and the
    /// original E/F numbers run with it off and are unchanged. It only ever moves money the normal transfer rules allow (each hop
    /// keeps its own delay) and never touches an open position; the wallet's free cash is all it uses.
    ///  - bank buffer: never move the bank below (next rent + 30 days of food) into the exchange
    ///  - hunger: before the evening meal, with the bank short of it and money on the exchange, start a withdrawal
    ///  - E9: when the exchange's free money can cover the arrears, start the withdrawal at once
    /// </summary>
    public static class LifeSense
    {
        public static bool On;
        public const long Buffer = Rules.Rent + 30 * Rules.FoodPerDay;

        /// <summary>Caps how much of the bank a bot may push out to the exchange.</summary>
        public static long CapMove(Loop l, long want) => !On ? want : Math.Max(0, Math.Min(want, l.Data.bankCash - Buffer));

        static long Inbound(SaveData d)
        {
            long sum = 0;
            foreach (var t in d.pendingTransfers)
                if (t.kind == TransferKind.DomesticToBank || t.kind == TransferKind.OverseasToDomestic) sum += t.amount;
            return sum;
        }

        /// <summary>Call once per slot before the bot acts.</summary>
        public static void Tick(Loop l)
        {
            if (!On) return;
            var d = l.Data;
            if (d.atSea) return;
            long inbound = Inbound(d);
            long free = d.domesticCash + d.overseasCash;

            if (l.IsBankrupt)
            {
                long need = d.rentOwed + 3 * Rules.FoodPerDay;
                if (d.bankCash + inbound + free >= d.rentOwed) Withdraw(l, need - d.bankCash - inbound);
                return;
            }
            bool beforeEvening = d.slot == Slot.Dawn || d.slot == Slot.Morning || d.slot == Slot.Afternoon;
            if (beforeEvening && d.bankCash + inbound < Rules.FoodPerDay && free > 0)
                Withdraw(l, 30 * Rules.FoodPerDay - d.bankCash - inbound);
        }

        static void Withdraw(Loop l, long need)
        {
            if (need <= 0) return;
            var d = l.Data;
            long fromDomestic = Math.Min(d.domesticCash, need);
            if (fromDomestic > 0 && !l.DomesticLocked && l.TransferDomesticToBank(fromDomestic).Ok) need -= fromDomestic;
            long fromOverseas = Math.Min(d.overseasCash, need);
            if (fromOverseas > 0) l.TransferOverseasToDomestic(fromOverseas);   // the second hop follows next slot
        }
    }
}
