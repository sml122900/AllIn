using System.Linq;
using AllIn.Core;
using AllIn.Core.Actions;
using NUnit.Framework;

namespace AllIn.Core.Tests
{
    /// <summary>Hunger notices (2026-09-30): exchange balance shown on the log line, and a single popup on the third hungry day in a row.</summary>
    public class HungerTests
    {
        static Loop Broke(long seed, long exchange)
        {
            var loop = Loop.NewRun(seed);
            loop.PopEvent(); loop.PopEvent();
            var d = loop.Data;
            d.bankCash = 0; d.domesticCash = exchange; d.overseasCash = 0;
            return loop;
        }

        static void PassThroughEvenings(Loop loop, int days)
        {
            int guard = 0;
            int target = loop.Data.stats.starved + days;
            while (loop.Data.stats.starved < target && guard++ < 200) { if (loop.Finished) loop.ContinueLife(); else loop.Pass(); }
        }

        [Test]
        public void HungerLine_MentionsTheExchange_OnlyWhenItCouldPayForFood()
        {
            var rich = Broke(1, 500_000);
            PassThroughEvenings(rich, 1);
            Assert.IsTrue(rich.Data.log.Any(l => l.Contains(Texts.Starved) && l.Contains(Texts.StarvedExchange(500_000))));
            var poor = Broke(2, Rules.FoodPerDay - 1);
            PassThroughEvenings(poor, 1);
            Assert.IsTrue(poor.Data.log.Any(l => l.Contains(Texts.Starved)));
            Assert.IsFalse(poor.Data.log.Any(l => l.Contains("거래소엔")));
        }

        [Test]
        public void ThirdHungryDayInARow_RaisesOnePopup_InsteadOfTheLogLine()
        {
            var loop = Broke(3, 300_000);
            PassThroughEvenings(loop, 2);
            Assert.IsFalse(loop.Data.pendingEvents.Any(e => e.id == EventIds.Hunger));
            PassThroughEvenings(loop, 1);
            var hunger = loop.Data.pendingEvents.Where(e => e.id == EventIds.Hunger).ToList();
            Assert.AreEqual(1, hunger.Count);
            Assert.AreEqual(3, hunger[0].count);
            Assert.AreEqual(300_000, hunger[0].amount);
            PassThroughEvenings(loop, 2);
            Assert.AreEqual(1, loop.Data.pendingEvents.Count(e => e.id == EventIds.Hunger), "days 4 and 5 add no second popup");
        }

        [Test]
        public void AFedDay_BreaksTheStreak()
        {
            var loop = Broke(4, 0);
            PassThroughEvenings(loop, 2);
            loop.Data.bankCash = Rules.FoodPerDay * 2;
            int guard = 0;
            while (loop.Data.stats.foodPaid < 2 && guard++ < 100) loop.Pass();   // the day is fed
            PassThroughEvenings(loop, 2);
            Assert.AreEqual(2, loop.Data.starveStreak);
            Assert.IsFalse(loop.Data.pendingEvents.Any(e => e.id == EventIds.Hunger));
        }
    }
}
