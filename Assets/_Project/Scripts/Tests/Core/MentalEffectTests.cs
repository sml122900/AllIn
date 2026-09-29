using AllIn.Core;
using NUnit.Framework;

namespace AllIn.Core.Tests
{
    public class MentalEffectTests
    {
        static Loop AtNight(int mental)
        {
            var loop = Loop.NewRun(11);
            while (loop.Data.slot != Slot.Night) loop.Pass();
            loop.Data.mental = mental - Rules.SleepMental; // sleeping adds +3 before dawn is judged
            return loop;
        }

        [Test]
        public void Mental14AtDawn_LosesDawnAndMorning_DayStartsAfternoon()
        {
            var loop = AtNight(14);
            loop.Pass();
            Assert.AreEqual(2, loop.Data.day);
            Assert.AreEqual(Slot.Afternoon, loop.Data.slot);
        }

        [Test]
        public void Mental15AtDawn_GetsUpNormally()
        {
            var loop = AtNight(15);
            loop.Pass();
            Assert.AreEqual(2, loop.Data.day);
            Assert.AreEqual(Slot.Dawn, loop.Data.slot);
        }

        [Test]
        public void LostSlots_StillTickThePrice()
        {
            var loop = AtNight(10);
            int before = loop.Data.priceHistory.Count;
            loop.Pass();
            Assert.AreEqual(before + 3, loop.Data.priceHistory.Count, "night→dawn, dawn→morning, morning→afternoon");
        }

        [Test]
        public void BadSleep_HalvesBodyRecovery_NoMentalGain_WhenTheLockedExchangeScreenWasOpenedThatNight()
        {
            var loop = Loop.NewRun(900);
            Assert.IsTrue(loop.TransferBankToDomestic(1_000_000).Ok); // starts the 24h lock
            while (loop.Data.slot != Slot.Night) loop.Pass();
            Assert.IsTrue(loop.DomesticLocked);
            loop.NoteExchangeScreenOpened();
            loop.Data.body = 50;
            int body = loop.Data.body, mental = loop.Data.mental;
            loop.Pass(); // sleep
            Assert.AreEqual(body + Rules.BadSleepBody, loop.Data.body);
            Assert.AreEqual(mental, loop.Data.mental, "mental +0");
        }

        [Test]
        public void NormalSleep_WhenLockedButTheScreenWasNeverOpened_StillFullRecovery()
        {
            var loop = Loop.NewRun(901);
            Assert.IsTrue(loop.TransferBankToDomestic(1_000_000).Ok);
            while (loop.Data.slot != Slot.Night) loop.Pass();
            loop.Data.body = 50;
            int body = loop.Data.body, mental = loop.Data.mental;
            loop.Pass();
            Assert.AreEqual(body + Rules.SleepBody, loop.Data.body);
            Assert.AreEqual(mental + Rules.SleepMental, loop.Data.mental);
        }
    }
}
