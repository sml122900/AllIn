using System.Linq;
using AllIn.Core;
using AllIn.Core.Actions;
using NUnit.Framework;

namespace AllIn.Core.Tests
{
    /// <summary>DESIGN.md Part D: age, bankruptcy (E9), the six-month ship, health debt, illness, check-ups, continuing past Day 30.</summary>
    public class LifeTests
    {
        static Loop Fresh(long seed)
        {
            var loop = Loop.NewRun(seed);
            loop.PopEvent(); loop.PopEvent();
            return loop;
        }

        /// <summary>Passes until the clock reaches (or, when a lost-days cascade jumps over it, first passes) the given moment.</summary>
        static void PassUntil(Loop loop, int day, Slot slot)
        {
            int target = Calendar.AbsoluteSlot(day, slot);
            int guard = 0;
            while (loop.AbsSlot < target)
            {
                if (guard++ > 5000) Assert.Fail("never reached day " + day + " " + slot);
                if (loop.Finished) loop.ContinueLife();
                else Assert.IsTrue(loop.Pass().Ok);
            }
        }

        static void Broke(Loop loop)
        {
            loop.Data.bankCash = 0;
            loop.Data.domesticCash = 0;
            loop.Data.overseasCash = 0;
            loop.Data.pendingTransfers.Clear();
        }

        static bool HasEvent(Loop loop, string id) => loop.Data.pendingEvents.Any(e => e.id == id);

        /// <summary>Bankrupt and at the dawn where the ship can be taken: the state E9 leaves behind.</summary>
        static Loop BankruptAtDawn(long seed, int day = 2)
        {
            var loop = Fresh(seed);
            Broke(loop);
            PassUntil(loop, day, Slot.Dawn);
            Assert.IsTrue(loop.IsBankrupt);
            return loop;
        }

        // ---- age ----

        [Test]
        public void Age_Is21OnDay1_AndRisesEvery365Days()
        {
            Assert.AreEqual(21, Loop.AgeAtDay(1));
            Assert.AreEqual(21, Loop.AgeAtDay(365));
            Assert.AreEqual(22, Loop.AgeAtDay(366));
            Assert.AreEqual(23, Loop.AgeAtDay(731));
            Assert.AreEqual(21, Fresh(1).Age);
        }

        // ---- continue past the month-end settlement ----

        [Test]
        public void ContinueLife_ResumesOnDay31Dawn_ThenSettlesAgainOnDay60()
        {
            var loop = Fresh(11);
            PassUntil(loop, 30, Slot.Night);
            loop.Pass();
            Assert.IsTrue(loop.Finished);
            Assert.IsTrue(loop.Data.pendingEvents.Any(e => e.id == EventIds.Settlement));
            Assert.IsTrue(loop.ContinueLife().Ok);
            Assert.IsFalse(loop.Finished);
            Assert.AreEqual(31, loop.Data.day);
            Assert.AreEqual(Slot.Dawn, loop.Data.slot);
            PassUntil(loop, 60, Slot.Night);
            loop.Pass();
            Assert.IsTrue(loop.Finished, "the next monthly settlement");
            Assert.AreEqual(60, loop.Data.day);
        }

        [Test]
        public void ContinueLife_IsRefusedWhenTheMonthIsNotOver()
        {
            var loop = Fresh(12);
            Assert.IsFalse(loop.ContinueLife().Ok);
            Assert.AreEqual(1, loop.Data.day);
        }

        [Test]
        public void DaysLeft_IsUnchangedInMonthOne_AndRestartsEachMonth()
        {
            var loop = Fresh(13);
            Assert.AreEqual(29, loop.DaysLeft);
            loop.Data.day = 30;
            Assert.AreEqual(0, loop.DaysLeft);
            loop.Data.day = 31;
            Assert.AreEqual(29, loop.DaysLeft);
        }

        [Test]
        public void Rent_IsChargedAgainEvery30Days_NotBefore()
        {
            var loop = Fresh(14);
            PassUntil(loop, 31, Slot.Night); // through Day 31's dawn
            Assert.AreEqual(2, loop.Data.stats.rentPaid);
            long expected = Rules.StartingCash - 2 * Rules.Rent - 31 * Rules.FoodPerDay; // food through Day 31's evening
            Assert.AreEqual(expected, loop.Data.bankCash);
            PassUntil(loop, 61, Slot.Dawn);
            Assert.AreEqual(3, loop.Data.stats.rentPaid);
        }

        [Test]
        public void AJobOnlyRun_IsUnchangedByTheLifeRules()
        {
            var loop = Fresh(15);
            int guard = 0;
            while (!loop.Finished && guard++ < 1000)
            {
                if (loop.Data.slot == Slot.Evening && loop.Do(WorkRestaurantAction.Instance).Ok) continue;
                loop.Pass();
            }
            long expected = Rules.StartingCash - Rules.Rent - Rules.Days * Rules.FoodPerDay + Rules.Days * Rules.RestaurantWage;
            Assert.AreEqual(expected, loop.TotalAssets);
            Assert.AreEqual(0, loop.Data.stats.voyages);
            Assert.IsFalse(loop.IsBankrupt);
        }

        // ---- bankruptcy judgment ----

        [Test]
        public void Bankruptcy_BoundaryIsRentPlusSevenDaysOfFood()
        {
            Assert.AreEqual(534_000, Rules.BankruptBelowAssets);
            foreach (var (assets, bankrupt) in new[] { (534_000L - 1, true), (534_000L, false) })
            {
                var loop = Fresh(20);
                PassUntil(loop, 1, Slot.Night);
                Broke(loop);
                loop.Data.bankCash = assets;
                loop.Data.pendingEvents.Clear();
                loop.Pass(); // sleep -> Day 2 dawn, judged on entry
                Assert.AreEqual(bankrupt, loop.IsBankrupt, "assets " + assets);
                Assert.AreEqual(bankrupt, HasEvent(loop, EventIds.Bankrupt), "assets " + assets);
            }
        }

        [Test]
        public void Bankruptcy_CountsEverySpotOfMoney_NotJustTheBank()
        {
            var loop = Fresh(21);
            PassUntil(loop, 1, Slot.Night);
            Broke(loop);
            loop.Data.overseasCash = 600_000; // not bank money, still above the line
            loop.Pass();
            Assert.IsFalse(loop.IsBankrupt);
        }

        [Test]
        public void Bankruptcy_IsOnlyJudgedAtDawn()
        {
            var loop = Fresh(22);
            Broke(loop);
            loop.Pass(); // morning of Day 1
            loop.Pass();
            Assert.IsFalse(loop.IsBankrupt);
            PassUntil(loop, 2, Slot.Dawn);
            Assert.IsTrue(loop.IsBankrupt);
            Assert.AreEqual(2, loop.Data.bankruptSinceDay);
        }

        static int E9Count(Loop loop) => loop.Data.pendingEvents.Count(e => e.id == EventIds.Bankrupt);

        [Test]
        public void E9_PopsUpOnDeclaration_OnDay7_AndForcedOnDay14_NotOnTheDaysBetween()
        {
            var loop = Fresh(23);
            Broke(loop);
            PassUntil(loop, 2, Slot.Dawn);
            int start = loop.Data.bankruptSinceDay;
            Assert.AreEqual(2, start);
            Assert.AreEqual(1, E9Count(loop));
            Assert.AreEqual(0, loop.Data.pendingEvents.Last(e => e.id == EventIds.Bankrupt).ints[0], "first dawn: a choice");
            Assert.AreEqual(0, loop.BankruptDaysHeld);
            var seen = new System.Collections.Generic.List<int>();
            for (int held = 1; held < Rules.BankruptForcedAfterDays; held++)
            {
                loop.Data.pendingEvents.Clear();
                PassUntil(loop, start + held, Slot.Dawn);
                Assert.AreEqual(held, loop.BankruptDaysHeld);
                if (E9Count(loop) > 0) seen.Add(held);
                Assert.IsFalse(loop.ForcedVoyageDue, "held " + held);
            }
            CollectionAssert.AreEqual(new[] { 7 }, seen, "one more choice on day 7, silence otherwise");
            Assert.AreEqual(0, loop.Data.stats.voyages);
        }

        [Test]
        public void Day7Popup_IsAChoiceNotForced()
        {
            var loop = Fresh(27);
            Broke(loop);
            PassUntil(loop, 2, Slot.Dawn);
            loop.Data.pendingEvents.Clear();
            PassUntil(loop, 2 + Rules.BankruptReconfirmAfterDays, Slot.Dawn);
            var e9 = loop.Data.pendingEvents.Last(e => e.id == EventIds.Bankrupt);
            Assert.AreEqual(0, e9.ints[0]);
            Assert.AreEqual(7, e9.count);
        }

        [Test]
        public void HoldingOn_WhenMoneyComesBack_EndsTheBankruptState()
        {
            var loop = BankruptAtDawn(24);
            loop.Data.bankCash = 5_000_000;
            PassUntil(loop, 3, Slot.Dawn);
            Assert.IsFalse(loop.IsBankrupt);
            Assert.AreEqual(0, loop.Data.bankruptSinceDay);
        }

        [Test]
        public void Bankruptcy_Off_NeverDeclares()
        {
            var loop = Fresh(25);
            loop.BankruptcyEnabled = false;
            Broke(loop);
            PassUntil(loop, 12, Slot.Dawn);
            Assert.IsFalse(loop.IsBankrupt);
            Assert.AreEqual(0, loop.Data.stats.voyages);
            Assert.IsFalse(HasEvent(loop, EventIds.Bankrupt));
        }

        [Test]
        public void FourteenthHeldDay_ForcesTheShip_WithNoChoice()
        {
            var loop = Fresh(26);
            Broke(loop);
            PassUntil(loop, 15, Slot.Night);
            Assert.AreEqual(0, loop.Data.stats.voyages, "13 days held: still a choice");
            loop.Pass(); // Day 16 dawn = 14 days after the state began on Day 2
            Assert.AreEqual(1, loop.Data.stats.voyages, "forced");
            Assert.AreEqual(16 + Rules.VoyageDays, loop.Data.day);
            Assert.AreEqual(Slot.Dawn, loop.Data.slot);
            Assert.IsFalse(loop.IsBankrupt);
        }

        [Test]
        public void HoldingOn_CrossingTheLineMidway_ClearsTheStateWithAOneLineNotice()
        {
            var loop = Fresh(28);
            Broke(loop);
            PassUntil(loop, 2 + 9, Slot.Dawn);
            Assert.IsTrue(loop.IsBankrupt);
            loop.Data.bankCash = Rules.BankruptBelowAssets + 10 * Rules.FoodPerDay;
            PassUntil(loop, 2 + 10, Slot.Dawn);
            Assert.IsFalse(loop.IsBankrupt);
            Assert.AreEqual(-1, loop.BankruptDaysHeld);
            Assert.IsTrue(loop.Data.log.Any(l => l.Contains(Texts.BankruptCleared)));
            PassUntil(loop, 2 + 20, Slot.Dawn);
            Assert.AreEqual(0, loop.Data.stats.voyages, "no forced ship after recovering");
        }

        /// <summary>Constant check plus a real run: from ~0 assets, a restaurant shift every day clears 534,000 within the 14 days.</summary>
        [Test]
        public void JobOnly_CanClearTheBankruptcyLineWithin14Days()
        {
            long constant = Rules.BankruptForcedAfterDays * (Rules.RestaurantWage - Rules.FoodPerDay);
            Assert.GreaterOrEqual(constant, Rules.BankruptBelowAssets, "arithmetic path exists");

            var loop = Fresh(29);
            Broke(loop);
            PassUntil(loop, 2, Slot.Dawn);
            int guard = 0;
            while (loop.IsBankrupt && !loop.ForcedVoyageDue && guard++ < 200)
            {
                if (loop.Data.slot == Slot.Evening && loop.Do(WorkRestaurantAction.Instance).Ok) continue;
                loop.Pass();
            }
            Assert.IsFalse(loop.IsBankrupt, "released by working alone");
            Assert.AreEqual(0, loop.Data.stats.voyages);
            Assert.LessOrEqual(loop.Data.day - 2, Rules.BankruptForcedAfterDays, "cleared at the latest on the forced dawn itself");
            System.Console.WriteLine("job-only clear on held day " + (loop.Data.day - 2));
        }

        // ---- the ship ----

        [Test]
        public void Depart_IsRefusedUnlessBankrupt()
        {
            var loop = Fresh(30);
            Assert.IsFalse(loop.CanDepart().Ok);
            Assert.IsFalse(loop.Depart(false).Ok);
            Assert.AreEqual(0, loop.Data.stats.voyages);
        }

        [Test]
        public void EveryAction_IsRefusedWhileAtSea()
        {
            var loop = Fresh(31);
            loop.Data.overseasCash = 1_000_000;
            loop.Data.atSea = true;
            Assert.IsFalse(loop.Check(WorkRestaurantAction.Instance).Ok);
            Assert.IsFalse(loop.Check(RestAction.Instance).Ok);
            Assert.IsFalse(loop.Check(LottoBuyAction.Instance).Ok);
            Assert.IsFalse(loop.Check(CheckupAction.Instance).Ok);
            Assert.IsFalse(loop.Check(new CoinOpenAction(Direction.Long, 5, 100_000)).Ok);
            Assert.IsFalse(loop.Check(CoinCloseAction.Instance).Ok);
            Assert.IsFalse(loop.TransferBankToDomestic(1000).Ok);
            Assert.IsFalse(loop.TransferDomesticToOverseas(1000).Ok);
            Assert.IsFalse(loop.TransferOverseasToDomestic(1000).Ok);
            Assert.IsFalse(loop.TransferDomesticToBank(1000).Ok);
            Assert.IsFalse(loop.AddMarginToPosition(1000).Ok);
            Assert.IsFalse(loop.Pass().Ok);
            Assert.AreEqual(1, loop.Data.day, "no time passed");
        }

        [Test]
        public void Return_PaysAgesShrinksBodyMaxAndAddsHealthDebt()
        {
            var loop = BankruptAtDawn(32);
            loop.Data.body = 95;
            loop.Data.mental = 5;
            int dayBefore = loop.Data.day;
            long assetsBefore = loop.TotalAssets;
            Assert.IsTrue(loop.Depart(false).Ok);
            var d = loop.Data;
            Assert.AreEqual(assetsBefore + Rules.VoyagePay, loop.TotalAssets, "rent and food are not charged at sea");
            Assert.AreEqual(15_000_000, d.bankCash);
            Assert.AreEqual(dayBefore + 180, d.day);
            Assert.AreEqual(Slot.Dawn, d.slot);
            Assert.AreEqual(85, d.bodyMax);
            Assert.AreEqual(85, d.body, "body is clamped to the new maximum");
            Assert.AreEqual(1, d.healthDebt);
            Assert.AreEqual(50, d.mental);
            Assert.AreEqual(Room.Goshiwon, d.room);
            Assert.AreEqual(1, d.stats.voyages);
            Assert.IsFalse(loop.IsBankrupt);
            Assert.IsFalse(d.atSea);
            Assert.AreEqual(Rules.StartAge + (d.day - 1) / 365, loop.Age);
            Assert.AreEqual(0.5, Loop.AgeYears(d.day) - Loop.AgeYears(dayBefore), 0.01, "a voyage is about half a year");
            var ret = d.pendingEvents.Last();
            Assert.AreEqual(EventIds.VoyageReturn, ret.id);
            Assert.AreEqual(15_000_000, ret.amount);
            Assert.AreEqual(85, ret.ints[1]);
        }

        [Test]
        public void BodyMax_FallsBy15PerVoyage_AndNeverBelow20()
        {
            var loop = Fresh(33);
            int[] expected = { 85, 70, 55, 40, 25, 20, 20 };
            foreach (int want in expected)
            {
                loop.Data.bankruptSinceDay = loop.Data.day;
                Assert.IsTrue(loop.Depart(false).Ok);
                Assert.AreEqual(want, loop.Data.bodyMax);
            }
            Assert.AreEqual(7, loop.Data.healthDebt);
            Assert.Less(Rules.BodyMaxFloor, Rules.RestaurantMinBody, "the floor makes the job impossible");
            loop.AddBody(500);
            Assert.AreEqual(20, loop.Data.body, "healing stops at the new maximum");
        }

        [Test]
        public void Return_MovesBackToAGoshiwon_AndDoesNotUpgradeItFor30Days()
        {
            var loop = BankruptAtDawn(34);
            loop.Depart(false);
            Assert.AreEqual(15_000_000, loop.Data.bankCash);
            Assert.AreEqual(Room.OneRoom, Calendar.RoomFor(loop.TotalCashLikeForRoom), "the money alone would say one-room");
            int back = loop.Data.day;
            PassUntil(loop, back + 1, Slot.Dawn);
            Assert.AreEqual(Room.Goshiwon, loop.Data.room);
            PassUntil(loop, back + 30, Slot.Dawn);
            Assert.AreEqual(Room.OneRoom, loop.Data.room, "re-judged once the new lease period is over");
        }

        [Test]
        public void Return_RentComesDue30DaysAfterReturn_NotAtOnce()
        {
            var loop = BankruptAtDawn(35);
            loop.Depart(false);
            int back = loop.Data.day;
            int rentBefore = loop.Data.stats.rentPaid;
            PassUntil(loop, back + 29, Slot.Night);
            Assert.AreEqual(rentBefore, loop.Data.stats.rentPaid);
            PassUntil(loop, back + 30, Slot.Morning);
            Assert.AreEqual(rentBefore + 1, loop.Data.stats.rentPaid);
        }

        /// <summary>A loop with `leverage`x long open on all the overseas money, for the first seed whose opening slot doesn't liquidate it.</summary>
        static Loop WithOpenPosition(int leverage, long commit)
        {
            for (int seed = 100; seed < 200; seed++)
            {
                var loop = Fresh(seed);
                loop.Data.bankCash = 0;
                loop.Data.domesticCash = 0;
                loop.Data.overseasCash = commit;
                Assert.IsTrue(loop.Do(new CoinOpenAction(Direction.Long, leverage, commit)).Ok);
                if (!loop.Data.hasPosition) continue;
                loop.Data.pendingEvents.Clear();
                loop.Data.bankruptSinceDay = loop.Data.day;
                return loop;
            }
            Assert.Fail("no seed kept the position alive through its first slot");
            return null;
        }

        [Test]
        public void ThePositionCanBeClosedBeforeBoarding_ProceedsGoToTheOverseasWallet()
        {
            var loop = WithOpenPosition(5, 200_000);
            long total = loop.TotalAssets;
            Assert.IsTrue(loop.Depart(true).Ok);
            Assert.IsFalse(loop.Data.hasPosition);
            Assert.AreEqual(0, loop.Data.stats.liquidations, "closed by hand — never liquidated");
            Assert.Greater(loop.Data.overseasCash, 0);
            Assert.AreEqual(0, loop.PositionValue);
            var ret = loop.Data.pendingEvents.Last();
            Assert.AreEqual(0, ret.ints[4], "no position to report");
            Assert.AreEqual(Rules.VoyagePay, loop.Data.bankCash);
            Assert.Less(Rules.VoyagePay + loop.Data.overseasCash, total + Rules.VoyagePay + 200_000, "sanity: nothing appeared from nowhere");
        }

        [Test]
        public void APositionLeftOpen_IsLiquidatedAtSea_WithoutPopups()
        {
            var loop = WithOpenPosition(100, 300_000);
            Assert.IsTrue(loop.Depart(false).Ok);
            Assert.IsFalse(loop.Data.hasPosition, "a 100x long does not survive 900 slots");
            Assert.AreEqual(1, loop.Data.stats.liquidations);
            Assert.AreEqual(1, loop.Data.stats.voyageLiquidations);
            var ids = loop.Data.pendingEvents.Select(e => e.id).ToList();
            CollectionAssert.DoesNotContain(ids, EventIds.LiquidationDay);
            CollectionAssert.DoesNotContain(ids, EventIds.LiquidationNight);
            CollectionAssert.DoesNotContain(ids, EventIds.SelfPity);
            CollectionAssert.DoesNotContain(ids, EventIds.Ghost);
            Assert.AreEqual(-1, loop.Data.e3AtSlot);
            var ret = loop.Data.pendingEvents.Last();
            Assert.AreEqual(EventIds.VoyageReturn, ret.id);
            Assert.AreEqual(2, ret.ints[4], "reported as liquidated at sea");
            Assert.Greater(ret.amount2, 0);
            Assert.AreEqual(50, loop.Data.mental, "the liquidation's mental hit is overwritten by the return");
            Assert.IsFalse(loop.Data.tilted);
        }

        [Test]
        public void ThePriceKeepsMoving_AtSea()
        {
            var loop = BankruptAtDawn(38);
            long priceBefore = loop.Data.price;
            loop.Depart(false);
            Assert.AreNotEqual(priceBefore, loop.Data.price);
            Assert.AreEqual(Rules.PriceHistoryLength, loop.Data.priceHistory.Count, "900 slots of history fill the window");
        }

        [Test]
        public void ALottoTicketBoughtBeforeSailing_IsDrawnAtSea_WithoutAPopup()
        {
            var loop = Fresh(39);
            Assert.IsTrue(loop.Do(LottoBuyAction.Instance).Ok);
            loop.Data.pendingEvents.Clear();
            Broke(loop);
            loop.Data.bankruptSinceDay = loop.Data.day;
            loop.Depart(false);
            Assert.GreaterOrEqual(loop.Data.stats.lottoDraws, 1);
            Assert.IsFalse(HasEvent(loop, EventIds.LottoDraw));
            Assert.AreEqual(0, loop.Data.tickets.Count);
        }

        [Test]
        public void TheShipSkipsTheSettlementsItSailsPast()
        {
            var loop = Fresh(40);
            PassUntil(loop, 25, Slot.Dawn);
            Broke(loop);
            loop.Data.bankruptSinceDay = loop.Data.day;
            loop.Depart(false);
            Assert.AreEqual(205, loop.Data.day);
            Assert.IsFalse(loop.Finished);
            PassUntil(loop, 210, Slot.Night);
            loop.Pass();
            Assert.IsTrue(loop.Finished, "the next settlement is Day 210");
        }

        [Test]
        public void SaveAndRestore_AfterAVoyage_ContinuesIdentically()
        {
            var a = BankruptAtDawn(41);
            a.Depart(false);
            var restored = Loop.FromSave(ScheduleTests.Clone(a.Snapshot()));
            for (int i = 0; i < 200; i++) { a.Pass(); restored.Pass(); }
            Assert.AreEqual(a.Data.day, restored.Data.day);
            Assert.AreEqual(a.Data.price, restored.Data.price);
            Assert.AreEqual(a.Data.bankCash, restored.Data.bankCash);
            Assert.AreEqual(a.Data.bodyMax, restored.Data.bodyMax);
            Assert.AreEqual(a.Data.healthDebt, restored.Data.healthDebt);
            Assert.AreEqual(a.Data.stats.illnesses, restored.Data.stats.illnesses);
            Assert.AreEqual(a.Data.nextHealthCheckDay, restored.Data.nextHealthCheckDay);
        }

        // ---- illness ----

        [Test]
        public void IllnessRoll_IsSkippedWithNoDebt_SoTheRngStreamIsUntouched()
        {
            var healthy = Fresh(50);
            var control = Fresh(50);
            control.Data.nextHealthCheckDay = 100_000; // never rolls
            PassUntil(healthy, 70, Slot.Dawn);
            PassUntil(control, 70, Slot.Dawn);
            CollectionAssert.AreEqual(control.Data.rngState, healthy.Data.rngState);
            Assert.AreEqual(0, healthy.Data.stats.illnesses);
        }

        [TestCase(1, 0.10)]
        [TestCase(3, 0.30)]
        public void IllnessChance_IsTenPercentPerDebt_OverManySeeds(int debt, double expected)
        {
            const int runs = 1500;
            int sick = 0;
            for (int seed = 1; seed <= runs; seed++)
            {
                var loop = Fresh(seed);
                loop.Data.healthDebt = debt;
                PassUntil(loop, Rules.HealthCheckIntervalDays + 1, Slot.Dawn);
                if (loop.Data.stats.illnesses > 0) sick++;
            }
            double rate = sick / (double)runs;
            Assert.AreEqual(expected, rate, 0.035, "observed " + rate);
        }

        [Test]
        public void TenDebt_MeansCertainIllness_HospitalBillInRange_AndThreeDaysLost()
        {
            var loop = Fresh(51);
            loop.Data.healthDebt = 10;
            PassUntil(loop, 30, Slot.Night);
            long bankBefore = loop.Data.bankCash;
            loop.Pass();          // Day 30 night: the monthly settlement
            loop.ContinueLife();  // -> Day 31 dawn: rent, illness, then three lost days
            var d = loop.Data;
            Assert.AreEqual(1, d.stats.illnesses);
            Assert.That(d.stats.hospitalPaid, Is.InRange(Rules.HospitalMin, Rules.HospitalMax));
            var sick = d.pendingEvents.Single(e => e.id == EventIds.Sick);
            Assert.AreEqual(d.stats.hospitalPaid, sick.amount);
            Assert.AreEqual(bankBefore - Rules.Rent - d.stats.hospitalPaid - 3 * Rules.FoodPerDay, d.bankCash, "rent, the bill, and food for the three bed days");
            Assert.AreEqual(34, d.day, "Days 31, 32, 33 pass as forced rest");
            Assert.AreEqual(Slot.Dawn, d.slot);
        }

        [Test]
        public void IllnessPaysOnlyWhatIsInTheBank_NoDebt()
        {
            var loop = Fresh(52);
            loop.Data.healthDebt = 10;
            PassUntil(loop, 30, Slot.Night);
            loop.Data.bankCash = 300_000;
            loop.Pass();
            loop.ContinueLife();
            Assert.AreEqual(0, loop.Data.bankCash);
            Assert.LessOrEqual(loop.Data.stats.hospitalPaid, 300_000);
        }

        // ---- check-ups ----

        /// <summary>A checkup on the next day slot (sleeps through the night first — checkups are day-only).</summary>
        static bool Checkup(Loop loop)
        {
            if (loop.Data.slot == Slot.Night) loop.Pass();
            return loop.Do(CheckupAction.Instance).Ok;
        }

        [Test]
        public void TenCheckups_CancelOneHealthDebt()
        {
            var loop = Fresh(60);
            loop.Data.healthDebt = 2;
            for (int i = 0; i < 9; i++) Assert.IsTrue(Checkup(loop), "checkup " + i);
            Assert.AreEqual(2, loop.Data.healthDebt, "9 is not enough");
            Assert.IsTrue(Checkup(loop));
            Assert.AreEqual(1, loop.Data.healthDebt);
            Assert.AreEqual(0, loop.Data.checkupProgress);
            Assert.AreEqual(10, loop.Data.stats.checkups);
        }

        [Test]
        public void Checkup_CostsMoneyAndTheSlot_DaySlotsOnly()
        {
            var loop = Fresh(61);
            long bank = loop.Data.bankCash;
            var slot = loop.Data.slot;
            Assert.IsTrue(loop.Do(CheckupAction.Instance).Ok);
            Assert.AreEqual(bank - Rules.CheckupCost, loop.Data.bankCash);
            Assert.AreEqual(slot + 1, loop.Data.slot, "it takes the slot");
            PassUntil(loop, 1, Slot.Night);
            Assert.IsFalse(loop.Check(CheckupAction.Instance).Ok, "not at night");
            loop.Data.bankCash = Rules.CheckupCost - 1;
            PassUntil(loop, 2, Slot.Morning);
            Assert.IsFalse(loop.Check(CheckupAction.Instance).Ok, "not without the money");
        }

        [Test]
        public void Checkups_WithNoDebt_DoNotBankProgress()
        {
            var loop = Fresh(62);
            for (int i = 0; i < 10; i++) Assert.IsTrue(Checkup(loop));
            Assert.AreEqual(0, loop.Data.healthDebt);
            Assert.AreEqual(0, loop.Data.checkupProgress);
            loop.Data.healthDebt = 1;
            for (int i = 0; i < 9; i++) Assert.IsTrue(Checkup(loop));
            Assert.AreEqual(1, loop.Data.healthDebt, "the earlier ones did not count");
        }
    
        // ---- v0.4 reply: the ship has a price ----

        [Test]
        public void VoyagePay_Is15M_ThenDropsTenPercentOfBaseFromTheThird_FloorNineMillion()
        {
            long[] expected = { 15_000_000, 15_000_000, 13_500_000, 12_000_000, 10_500_000, 9_000_000, 9_000_000 };
            for (int n = 1; n <= expected.Length; n++) Assert.AreEqual(expected[n - 1], Rules.VoyagePayFor(n), "voyage " + n);
            var loop = Fresh(40);
            for (int n = 1; n <= 3; n++) { loop.Data.bankruptSinceDay = loop.Data.day; loop.Data.bankCash = 0; loop.Depart(false); }
            Assert.AreEqual(13_500_000, loop.Data.pendingEvents.Last(e => e.id == EventIds.VoyageReturn).amount);
        }

        [Test]
        public void Return_RollsIllnessOnceAtOnce_CertainAtTenDebt()
        {
            var loop = Fresh(41);
            loop.Data.healthDebt = 9; // +1 on return = 10 -> 100%
            loop.Data.bankruptSinceDay = loop.Data.day;
            loop.Depart(false);
            Assert.AreEqual(1, loop.Data.stats.illnesses, "the immediate roll");
            Assert.IsTrue(HasEvent(loop, EventIds.Sick));
            Assert.Greater(loop.Data.lostUntilSlot, loop.AbsSlot);
        }

        [Test]
        public void IllnessClock_StartsAtTheFirstReturn_AndKeepsTheCalendarAcrossLaterVoyages()
        {
            var loop = Fresh(42);
            loop.Data.bankruptSinceDay = loop.Data.day;
            loop.Depart(false);
            int firstReturn = loop.Data.day;
            Assert.AreEqual(firstReturn + 30, loop.Data.nextHealthCheckDay);
            PassUntil(loop, firstReturn + 10, Slot.Dawn);
            int before = loop.Data.nextHealthCheckDay;
            loop.Data.bankruptSinceDay = loop.Data.day;
            loop.Depart(false);
            Assert.Greater(loop.Data.nextHealthCheckDay, loop.Data.day, "checks that fell due at sea are collapsed into one");
            Assert.AreEqual(0, (loop.Data.nextHealthCheckDay - before) % 30, "same 30-day calendar, not reset to return + 30");
            Assert.LessOrEqual(loop.Data.nextHealthCheckDay - loop.Data.day, 30);
        }
}
}
