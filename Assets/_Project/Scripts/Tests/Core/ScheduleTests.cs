using AllIn.Core;
using NUnit.Framework;

namespace AllIn.Core.Tests
{
    public class ScheduleTests
    {
        static Loop PassToEnd(Loop loop)
        {
            int guard = 0;
            while (!loop.Finished && guard++ < 1000) Assert.IsTrue(loop.Pass().Ok);
            return loop;
        }

        [Test]
        public void ThirtyDays_Are150Slots_ThenTheRunEnds()
        {
            var loop = PassToEnd(Loop.NewRun(1));
            Assert.AreEqual(Rules.TotalSlots, loop.Data.stats.slotsPlayed);
            Assert.AreEqual(150, loop.Data.stats.slotsPlayed);
            Assert.AreEqual(30, loop.Data.day);
            Assert.AreEqual(Slot.Night, loop.Data.slot);
            Assert.IsFalse(loop.Pass().Ok, "no slots after the end");
        }

        [Test]
        public void FixedCosts_RentOnce_Food30Times()
        {
            var loop = PassToEnd(Loop.NewRun(2));
            Assert.AreEqual(1, loop.Data.stats.rentPaid);
            Assert.AreEqual(30, loop.Data.stats.foodPaid);
            Assert.AreEqual(Rules.StartingCash - Rules.Rent - 30 * Rules.FoodPerDay, loop.Data.bankCash);
        }

        [Test]
        public void Food_IsChargedWhenEveningStarts_NotBefore()
        {
            var loop = Loop.NewRun(3);
            long afterRent = Rules.StartingCash - Rules.Rent;
            loop.Pass(); loop.Pass(); // dawn → morning → afternoon
            Assert.AreEqual(Slot.Afternoon, loop.Data.slot);
            Assert.AreEqual(afterRent, loop.Data.bankCash);
            loop.Pass();
            Assert.AreEqual(Slot.Evening, loop.Data.slot);
            Assert.AreEqual(afterRent - Rules.FoodPerDay, loop.Data.bankCash);
        }

        [Test]
        public void Day1_IsMonday_SaturdaysAre6_13_20_27()
        {
            Assert.AreEqual(0, Calendar.Weekday(1));
            int[] saturdays = { 6, 13, 20, 27 };
            for (int d = 1; d <= 30; d++) Assert.AreEqual(System.Array.IndexOf(saturdays, d) >= 0, Calendar.IsSaturday(d), "day " + d);
            Assert.AreEqual(6, Calendar.NextSaturday(1));
            Assert.AreEqual(6, Calendar.NextSaturday(6));
            Assert.AreEqual(13, Calendar.NextSaturday(7));
        }

        [Test]
        public void Opening_EmitsOpeningThenFirstPopup_AndStartsWith20MMinusRent()
        {
            var loop = Loop.NewRun(4);
            Assert.AreEqual(EventIds.Opening, loop.PopEvent().id);
            Assert.AreEqual(EventIds.FirstPopup, loop.PopEvent().id);
            Assert.IsNull(loop.PopEvent());
            Assert.AreEqual(19_550_000, loop.Data.bankCash);
            Assert.AreEqual(Room.OneRoom, loop.Data.room);
        }

        [Test]
        public void RestByDay_Body5_SleepAtNight_Body30Mental3()
        {
            var loop = Loop.NewRun(5);
            loop.Data.body = 50;
            loop.Pass(); // dawn rest
            Assert.AreEqual(55, loop.Data.body);
            loop.Pass(); loop.Pass(); loop.Pass(); // morning, afternoon, evening
            Assert.AreEqual(Slot.Night, loop.Data.slot);
            int mental = loop.Data.mental;
            int body = loop.Data.body;
            loop.Pass(); // sleep
            Assert.AreEqual(body + 30, loop.Data.body);
            Assert.AreEqual(mental + Rules.SleepMental, loop.Data.mental);
        }

        [Test]
        public void Gauges_ClampTo0And100()
        {
            var loop = Loop.NewRun(6);
            loop.AddBody(500);
            loop.AddMental(-500);
            Assert.AreEqual(100, loop.Data.body);
            Assert.AreEqual(0, loop.Data.mental);
        }

        [Test]
        public void Room_IsRejudgedAtDawnOnly_AndEmitsE6()
        {
            var loop = Loop.NewRun(7);
            loop.PopEvent(); loop.PopEvent();
            loop.Data.bankCash = 2_000_000;
            loop.Pass();
            Assert.AreEqual(Room.OneRoom, loop.Data.room, "not re-judged mid-day");
            while (loop.Data.slot != Slot.Dawn) loop.Pass();
            Assert.AreEqual(Room.Goshiwon, loop.Data.room);
            Assert.Contains(EventIds.MoveRoom, loop.Data.pendingEvents.ConvertAll(e => e.id));
        }

        [TestCase(0, Room.Goshiwon)]
        [TestCase(2_999_999, Room.Goshiwon)]
        [TestCase(3_000_000, Room.OneRoom)]
        [TestCase(99_999_999, Room.OneRoom)]
        [TestCase(100_000_000, Room.Officetel)]
        public void RoomFor_Thresholds(long cash, Room expected) => Assert.AreEqual(expected, Calendar.RoomFor(cash));

        [Test]
        public void SnapshotAndRestore_ContinuesIdentically()
        {
            var a = Loop.NewRun(8);
            for (int i = 0; i < 17; i++) a.Pass();
            var restored = Loop.FromSave(Clone(a.Snapshot()));
            for (int i = 0; i < 40; i++) { a.Pass(); restored.Pass(); }
            Assert.AreEqual(a.Data.price, restored.Data.price);
            Assert.AreEqual(a.Data.bankCash, restored.Data.bankCash);
            Assert.AreEqual(a.Data.day, restored.Data.day);
            Assert.AreEqual(a.Data.slot, restored.Data.slot);
        }

        [Test]
        public void SameSeed_SameInputs_SamePricePath()
        {
            var a = PassToEnd(Loop.NewRun(9));
            var b = PassToEnd(Loop.NewRun(9));
            Assert.AreEqual(a.Data.priceHistory, b.Data.priceHistory);
        }

        /// <summary>Shallow-enough copy for the fields a plain pass-through run touches.</summary>
        internal static SaveData Clone(SaveData s)
        {
            var c = (SaveData)typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(s, null);
            c.priceHistory = new System.Collections.Generic.List<long>(s.priceHistory);
            c.pendingEvents = new System.Collections.Generic.List<GameEvent>(s.pendingEvents);
            c.log = new System.Collections.Generic.List<string>(s.log);
            c.tickets = new System.Collections.Generic.List<LottoTicket>(s.tickets);
            c.pendingTransfers = new System.Collections.Generic.List<PendingTransfer>(s.pendingTransfers);
            c.ghosts = new System.Collections.Generic.List<Ghost>(s.ghosts);
            c.rngState = (int[])s.rngState.Clone();
            c.marketRngState = (int[])s.marketRngState.Clone();
            c.subTickRngState = (int[])s.subTickRngState.Clone();
            c.stats = (Stats)typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(s.stats, null);
            c.stats.ghostResolutionSlots = new System.Collections.Generic.List<int>(s.stats.ghostResolutionSlots);
            return c;
        }
    }
}
