using System.Linq;
using AllIn.Core;
using AllIn.Core.Actions;
using NUnit.Framework;

namespace AllIn.Core.Tests
{
    public class GhostTests
    {
        const long P0 = Rules.CoinStartPrice;

        static Loop Liquidate(long seed)
        {
            var loop = Loop.NewRun(seed, new ScriptedPrices(P0));
            loop.Data.overseasCash = Rules.StartingCash;
            loop.SubTickModel = LinearSubTickPath.Instance;
            Assert.IsTrue(loop.Do(new CoinOpenAction(Direction.Long, 25, 10_000_000)).Ok);
            var p = loop.Data.position;
            long liq = (long)System.Math.Floor(CoinRules.LiquidationPrice(p));
            loop.PriceModel = new ScriptedPrices(liq - 1);
            loop.Data.pendingEvents.Clear();
            loop.Pass();
            Assert.AreEqual(1, loop.Data.stats.liquidations);
            Assert.AreEqual(1, loop.Data.ghosts.Count);
            return loop;
        }

        [Test]
        public void Ghost_FiresOnce_WhenPriceRecoversToEntry()
        {
            var loop = Liquidate(700);
            int mental = loop.Data.mental;
            int liqAbsSlot = loop.AbsSlot;

            loop.PriceModel = new ScriptedPrices(P0); // recovers all the way back to entry
            loop.Data.pendingEvents.Clear();
            loop.Pass();

            Assert.AreEqual(1, loop.Data.stats.ghostResolutionSlots.Count);
            Assert.AreEqual(loop.AbsSlot - liqAbsSlot, loop.Data.stats.ghostResolutionSlots[0]);

            Assert.AreEqual(0, loop.Data.ghosts.Count, "resolved, no longer tracked");
            Assert.AreEqual(1, loop.Data.stats.ghostFired);
            Assert.AreEqual(0, loop.Data.stats.ghostFiredEnhanced);
            Assert.AreEqual(System.Math.Max(0, mental - Rules.GhostMental), loop.Data.mental);
            var ids = loop.Data.pendingEvents.Select(e => e.id).ToList();
            Assert.Contains(EventIds.Ghost, ids);

            // doesn't refire
            loop.Data.pendingEvents.Clear();
            loop.Pass();
            Assert.AreEqual(1, loop.Data.stats.ghostFired);
        }

        [Test]
        public void Ghost_Enhanced_WhenAnOverseasArrivalLandedWithinTheWindow()
        {
            var loop = Liquidate(701);
            loop.Data.lastOverseasArrivalAbsSlot = loop.AbsSlot + 1; // simulate a deposit that arrives right as the ghost is about to resolve (strictly after liquidation, within the window)

            loop.PriceModel = new ScriptedPrices(P0);
            loop.Data.pendingEvents.Clear();
            loop.Pass();

            Assert.AreEqual(1, loop.Data.stats.ghostFired);
            Assert.AreEqual(1, loop.Data.stats.ghostFiredEnhanced);
        }

        [Test]
        public void Ghost_ExpiresAfter50Slots_WithZeroMissedMoney_WhenItNeverTurnedProfitable()
        {
            var loop = Liquidate(702);
            loop.PriceModel = new ScriptedPrices(); // holds the post-liquidation price (still below entry) forever
            for (int i = 0; i < Rules.GhostTrackSlots + 2; i++) loop.Pass();

            Assert.AreEqual(0, loop.Data.ghosts.Count, "expired");
            Assert.AreEqual(0, loop.Data.stats.ghostFired, "never recovered to entry, so no E8");
            Assert.AreEqual(0, loop.Data.stats.ghostMissedMoney, "never profitable, so nothing was missed");
        }

        [Test]
        public void Ghost_Recovery_RecordsMissedMoney_WhenItOvershootsEntry()
        {
            var loop = Liquidate(703);
            loop.PriceModel = new ScriptedPrices(P0 * 110 / 100); // recovers past entry, not just to it
            loop.Data.pendingEvents.Clear();
            loop.Pass();

            Assert.AreEqual(1, loop.Data.stats.ghostFired);
            Assert.Greater(loop.Data.stats.ghostMissedMoney, 0);
        }
    }
}
