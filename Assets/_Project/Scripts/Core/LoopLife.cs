using System;
using AllIn.Core.Actions;

namespace AllIn.Core
{
    /// <summary>
    /// D (v0.4, DESIGN.md Part D): one continuous life. Age, the month-end settlement that can be continued, monthly
    /// rent, bankruptcy (E9), the six-month ocean-going ship, health debt / illness. Pure rules — the Game layer only
    /// asks (Age, IsBankrupt, CanDepart) and calls (Depart, ContinueLife).
    /// </summary>
    public sealed partial class Loop
    {
        /// <summary>Headless switch. The 30-day bots (sim A~E, DESIGN.md D7) never answer E9, and a forced voyage would turn a
        /// 30-day run into a 200-day one, so they turn this off to keep the original definition. Not saved; the game leaves it on.</summary>
        public bool BankruptcyEnabled { get; set; } = true;

        public int Age => AgeAtDay(Data.day);

        public static int AgeAtDay(int day) => Rules.StartAge + (day - 1) / Rules.DaysPerYear;

        /// <summary>Fractional age in years, for reports.</summary>
        public static double AgeYears(int day) => Rules.StartAge + (day - 1) / (double)Rules.DaysPerYear;

        public bool IsBankrupt => Data.bankruptSinceDay > 0;

        /// <summary>D3: still bankrupt on the 7th day after the state began -> no choice, the ship is forced.</summary>
        public bool ForcedVoyageDue => Data.bankruptSinceDay > 0 && !Data.atSea &&
                                       Data.day - Data.bankruptSinceDay >= Rules.BankruptForcedAfterDays;

        // ---- new dawn: rent, illness, bankruptcy (never while atSea — the voyage loop doesn't call this) ----

        void OnDawnLife()
        {
            var d = Data;

            if (d.day >= d.nextRentDay)
            {
                PayFixedFromBank(Rules.Rent);
                d.stats.rentPaid++;
                d.nextRentDay += Rules.RentIntervalDays;
                Log(Texts.RentPaid);
            }

            if (d.day >= d.nextHealthCheckDay)
            {
                d.nextHealthCheckDay += Rules.HealthCheckIntervalDays;
                // no debt -> no roll at all, so a healthy life never touches the RNG stream (sims A~E stay byte-identical)
                if (d.healthDebt > 0 && Rng.Chance(Math.Min(1.0, d.healthDebt * Rules.IllnessChancePerDebt))) FallIll();
            }

            if (!BankruptcyEnabled) return;
            if (TotalAssets < Rules.BankruptBelowAssets)
            {
                if (d.bankruptSinceDay == 0) d.bankruptSinceDay = d.day;
                Emit(new GameEvent
                {
                    id = EventIds.Bankrupt,
                    amount = TotalAssets,
                    ints = new[] { ForcedVoyageDue ? 1 : 0 },
                    count = d.day - d.bankruptSinceDay,
                });
                Log(Texts.E9Bankrupt);
            }
            else d.bankruptSinceDay = 0;
        }

        void FallIll()
        {
            var d = Data;
            long cost = Rules.HospitalMin + Rng.NextInt(0, (int)(Rules.HospitalMax - Rules.HospitalMin) + 1);
            long paid = Math.Min(cost, d.bankCash); // no debt: pays what is there
            d.bankCash -= paid;
            d.stats.illnesses++;
            d.stats.hospitalPaid += paid;
            d.lostUntilSlot = Math.Max(d.lostUntilSlot, Calendar.AbsoluteSlot(d.day + Rules.IllnessLostDays, Slot.Dawn));
            Emit(new GameEvent { id = EventIds.Sick, amount = paid, count = Rules.IllnessLostDays });
            Log(Texts.SickTitle + " " + Fmt.Won(-paid));
        }

        // ---- continue past the month-end settlement (D1) ----

        /// <summary>[계속 살기]: resumes the run from the slot after the month-end night. Same shape as BeginWatchableAction —
        /// the walk may want a watch scene if a position is open.</summary>
        public WatchableAction BeginContinueLife()
        {
            if (!Data.finished || Data.atSea) return new WatchableAction(ActionResult.Refused(""), null);
            Data.finished = false;
            return AdvanceAfterSlotEnd(ActionResult.Done());
        }

        /// <summary>Headless [계속 살기].</summary>
        public ActionResult ContinueLife()
        {
            var w = BeginContinueLife();
            if (w.Walk != null) FinishWatch(w.Walk);
            AutoDepartIfForced();
            return w.Result;
        }

        // ---- the ship (D4) ----

        public Availability CanDepart()
        {
            if (Data.atSea) return Availability.No(Texts.AtSea);
            if (Data.finished) return Availability.No("");
            if (Data.bankruptSinceDay == 0) return Availability.No(Texts.VoyageNotBankrupt);
            return Availability.Yes;
        }

        /// <summary>Headless callers (Do / ContinueLife) board at once when the ship is forced and leave the position as it is.
        /// The Game asks the player about the position first and calls Depart itself.</summary>
        void AutoDepartIfForced()
        {
            if (ForcedVoyageDue && !Data.finished) Depart(false);
        }

        /// <summary>
        /// Six months at sea. Optionally closes the open position first (fee applies, proceeds to the overseas wallet).
        /// Then simulates all 900 slots — market and sub-tick paths included, so a position left open can be liquidated and
        /// ghosts can fire — with no player actions, rent, food, recovery or popups. Returns with +15,000,000 in the bank,
        /// mental 50, body max -15 (floor 40), health debt +1, back in a goshiwon.
        /// </summary>
        public ActionResult Depart(bool closePosition)
        {
            var ok = CanDepart();
            if (!ok.Ok) return ActionResult.Refused(ok.Reason);
            var d = Data;

            if (closePosition && d.hasPosition) ClosePositionAt(d.price, quiet: true);
            bool leftOpen = d.hasPosition;
            long realizedBefore = d.stats.coinRealized;
            int liquidationsBefore = d.stats.liquidations;

            d.atSea = true;
            d.stats.voyages++;
            Log(Texts.VoyageBoardTitle);
            for (int i = 0; i < Rules.VoyageSlots; i++)
            {
                OnSlotEnd();       // Saturday-evening lotto draw (settled silently at sea)
                AdvanceClockOnly();
                var walk = BeginPriceWalk();
                walk.RunToEnd();
                FinishPriceWalk(walk);
                ResolvePendingTransfers();
                if (Data.slot == Slot.Dawn) OnNewDay();
                TrackAssets();
            }
            d.atSea = false;

            bool liquidated = d.stats.liquidations > liquidationsBefore;
            d.stats.voyageLiquidations += d.stats.liquidations - liquidationsBefore;
            long lostAtSea = liquidated ? realizedBefore - d.stats.coinRealized : 0;

            AddBank(Rules.VoyagePay);
            d.mental = Rules.VoyageMental;
            d.bodyMax = Math.Max(Rules.BodyMaxFloor, d.bodyMax - Rules.VoyageBodyMaxLoss);
            d.body = Math.Min(d.body, d.bodyMax);
            d.healthDebt++;
            d.tilted = false;
            d.peekedExchangeAtNight = false;
            d.lostUntilSlot = -1;
            d.e3AtSlot = -1;
            d.firedUntilDay = 0;
            d.bankruptSinceDay = 0;
            d.room = Room.Goshiwon;                                   // the one-room lease ended; a goshiwon for now
            d.roomRecheckDay = d.day + Rules.RentIntervalDays;
            d.nextRentDay = d.day + Rules.RentIntervalDays;
            d.nextHealthCheckDay = d.day + Rules.HealthCheckIntervalDays;

            // Nothing that happened at sea should pop up piecemeal: keep only the E9 that is currently being answered.
            d.pendingEvents.RemoveAll(e => e.id != EventIds.Bankrupt);
            Emit(new GameEvent
            {
                id = EventIds.VoyageReturn,
                amount = Rules.VoyagePay,
                amount2 = lostAtSea,
                amount3 = d.hasPosition ? PositionValue : 0,
                // age, body max, health debt, voyage count, position state (0 none / 1 still open / 2 liquidated at sea)
                ints = new[] { Age, d.bodyMax, d.healthDebt, d.stats.voyages, d.hasPosition ? 1 : (leftOpen && liquidated ? 2 : 0) },
            });
            Log(Texts.VoyageReturnTitle);
            TrackAssets();
            Snapshot();
            return ActionResult.Done();
        }
    }
}
