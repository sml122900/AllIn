using AllIn.Core;
using AllIn.Core.Actions;
using NUnit.Framework;

namespace AllIn.Core.Tests
{
    public class JobTests
    {
        static Loop AtEvening(long seed = 31)
        {
            var loop = Loop.NewRun(seed);
            while (loop.Data.slot != Slot.Evening) loop.Pass();
            return loop;
        }

        [Test]
        public void Shift_Pays51600_CostsBody12Mental4_AndEndsTheSlot()
        {
            var loop = AtEvening();
            long cash = loop.Data.cash;
            int body = loop.Data.body, mental = loop.Data.mental;
            Assert.IsTrue(loop.Do(WorkRestaurantAction.Instance).Ok);
            Assert.AreEqual(cash + 51_600, loop.Data.cash);
            Assert.AreEqual(Slot.Night, loop.Data.slot);
            Assert.AreEqual(body - 12, loop.Data.body);
            Assert.AreEqual(mental - 4, loop.Data.mental);
            Assert.AreEqual(1, loop.Data.stats.workedSlots);
        }

        [Test]
        public void Shift_OnlyInTheEvening()
        {
            var loop = Loop.NewRun(32);
            Assert.IsFalse(loop.Do(WorkRestaurantAction.Instance).Ok);
        }

        [Test]
        public void Body24_CantWork_Body25_Can()
        {
            var loop = AtEvening();
            loop.Data.body = 24;
            Assert.IsFalse(loop.Do(WorkRestaurantAction.Instance).Ok);
            loop.Data.body = 25;
            Assert.IsTrue(loop.Do(WorkRestaurantAction.Instance).Ok);
        }

        [Test]
        public void Body0_OnlyRestEndsTheSlot()
        {
            var loop = AtEvening();
            loop.Data.body = 0;
            Assert.IsFalse(loop.Do(WorkRestaurantAction.Instance).Ok);
            Assert.IsTrue(loop.Pass().Ok);
        }

        [Test]
        public void Mental30_NeverMistakes()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var loop = AtEvening(seed);
                loop.Data.mental = 30;
                loop.Do(WorkRestaurantAction.Instance);
                Assert.AreEqual(0, loop.Data.stats.timesFired, "seed " + seed);
            }
        }

        [Test]
        public void Mental29_MistakesAbout30Percent_AndGetsFiredFor3Days()
        {
            int fired = 0;
            const int n = 2000;
            for (int seed = 0; seed < n; seed++)
            {
                var loop = AtEvening(seed);
                loop.Data.mental = 29;
                loop.Do(WorkRestaurantAction.Instance);
                if (loop.Data.stats.timesFired == 0) continue;
                fired++;
                Assert.AreEqual(29 - 4 - 10, loop.Data.mental);
                Assert.AreEqual(loop.Data.day + 3, loop.Data.firedUntilDay);
            }
            Assert.That(fired / (double)n, Is.InRange(0.26, 0.34));
        }

        [Test]
        public void Fired_CantWorkFor3Days_ThenCan()
        {
            var loop = AtEvening();
            loop.Data.firedUntilDay = 4; // fired on day 1
            for (int day = 1; day <= 4; day++)
            {
                Assert.IsFalse(loop.Do(WorkRestaurantAction.Instance).Ok, "day " + day);
                do loop.Pass(); while (loop.Data.slot != Slot.Evening);
            }
            Assert.AreEqual(5, loop.Data.day);
            Assert.IsTrue(loop.Do(WorkRestaurantAction.Instance).Ok);
        }

        [Test]
        public void JobOnly_30Days_IsFullyDeterministic()
        {
            var loop = Loop.NewRun(33);
            while (!loop.Finished)
            {
                if (loop.Data.slot == Slot.Evening) Assert.IsTrue(loop.Do(WorkRestaurantAction.Instance).Ok);
                else loop.Pass();
            }
            long expected = Rules.StartingCash - Rules.Rent - Rules.Days * Rules.FoodPerDay + Rules.Days * Rules.RestaurantWage;
            Assert.AreEqual(20_738_000, expected);
            Assert.AreEqual(expected, loop.TotalAssets);
        }
    }
}
