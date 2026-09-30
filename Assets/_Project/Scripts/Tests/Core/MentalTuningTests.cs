using AllIn.Core;
using AllIn.Core.Actions;
using NUnit.Framework;

namespace AllIn.Core.Tests
{
    /// <summary>Mental-recovery candidates V0~V3 (DESIGN.md D9). Default must stay V0 = the rules as they were.</summary>
    public class MentalTuningTests
    {
        static Loop Fresh(long seed)
        {
            var loop = Loop.NewRun(seed);
            loop.PopEvent(); loop.PopEvent();
            return loop;
        }

        static void SleepThroughDay(Loop loop, bool work)
        {
            int day = loop.Data.day;
            while (loop.Data.day == day && !loop.Finished)
            {
                if (work && loop.Data.slot == Slot.Evening && loop.Do(WorkRestaurantAction.Instance).Ok) continue;
                loop.Pass();
            }
        }

        [Test]
        public void DefaultTuning_IsV0_TheCurrentRules()
        {
            var t = Fresh(1).Tuning;
            Assert.AreEqual(Rules.SleepMental, t.SleepMental);
            Assert.AreEqual(0, t.StreakDays);
        }

        [Test]
        public void V0_WorkPlusSleep_IsMinusOneMentalPerDay()
        {
            var loop = Fresh(2);
            int before = loop.Data.mental;
            SleepThroughDay(loop, true);
            Assert.AreEqual(before - 1, loop.Data.mental);
        }

        [Test]
        public void V1_SleepGivesFive_SoWorkPlusSleepIsPlusOne()
        {
            var loop = Fresh(3);
            loop.Tuning = MentalTuning.V1();
            int before = loop.Data.mental;
            SleepThroughDay(loop, true);
            Assert.AreEqual(before + 1, loop.Data.mental);
        }

        [Test]
        public void V2_FiveMistakeFreeDaysInARow_PayEightMentalOnce_ThenRestart()
        {
            var loop = Fresh(4);
            loop.Tuning = MentalTuning.V2();
            loop.Data.mental = 60;
            for (int i = 0; i < 4; i++) SleepThroughDay(loop, true);
            Assert.AreEqual(60 - 4, loop.Data.mental, "4 days: no bonus yet (-1 each)");
            SleepThroughDay(loop, true);
            Assert.AreEqual(60 - 5 + 8, loop.Data.mental, "the 5th day pays +8");
            Assert.AreEqual(0, loop.Data.workStreak, "count restarts after paying");
        }

        [Test]
        public void V2_AMissedDay_ResetsTheStreak()
        {
            var loop = Fresh(5);
            loop.Tuning = MentalTuning.V2();
            loop.Data.mental = 60;
            for (int i = 0; i < 3; i++) SleepThroughDay(loop, true);
            SleepThroughDay(loop, false); // a day off
            SleepThroughDay(loop, true);
            SleepThroughDay(loop, true);
            Assert.AreEqual(2, loop.Data.workStreak);
        }

        [Test]
        public void V2_AMistake_ResetsTheStreak()
        {
            var loop = Fresh(6);
            loop.Tuning = MentalTuning.V2();
            loop.Data.workStreak = 4;
            loop.Data.lastWorkDay = loop.Data.day - 1;
            loop.Data.mental = 10; // below 30: 30% mistakes
            int guard = 0;
            while (loop.Data.stats.timesFired == 0 && guard++ < 2000)
            {
                loop.Data.workStreak = 4;
                loop.Data.lastWorkDay = loop.Data.day - 1;
                loop.Data.firedUntilDay = 0;
                loop.Data.mental = 10;
                if (loop.Data.slot == Slot.Evening) loop.Do(WorkRestaurantAction.Instance); else loop.Pass();
            }
            Assert.Greater(loop.Data.stats.timesFired, 0);
            Assert.AreEqual(0, loop.Data.workStreak);
        }

        [Test]
        public void V0_JobOnlyRun_IsUnchanged()
        {
            var loop = Fresh(7);
            int guard = 0;
            while (!loop.Finished && guard++ < 1000)
            {
                if (loop.Data.slot == Slot.Evening && loop.Do(WorkRestaurantAction.Instance).Ok) continue;
                loop.Pass();
            }
            long expected = Rules.StartingCash - Rules.Rent - Rules.Days * Rules.FoodPerDay + Rules.Days * Rules.RestaurantWage;
            Assert.AreEqual(expected, loop.TotalAssets);
        }
    }
}
