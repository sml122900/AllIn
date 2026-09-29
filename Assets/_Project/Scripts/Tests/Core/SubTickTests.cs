using AllIn.Core;
using AllIn.Core.Actions;
using NUnit.Framework;

namespace AllIn.Core.Tests
{
    /// <summary>Fully scripted sub-tick path, ignoring old/new price and the RNG — for isolating SubTickWalk/liquidation logic.</summary>
    sealed class ScriptedSubTicks : ISubTickPath
    {
        readonly long[] _prices;
        public ScriptedSubTicks(params long[] prices) { _prices = prices; }
        public long[] Build(long oldPrice, long newPrice, IRng subTickRng) => _prices;
    }

    public class SubTickTests
    {
        const long P0 = Rules.CoinStartPrice;

        [Test]
        public void SubTickPath_HasTwelveSteps_AndLastIsExactlyTheSlotClose()
        {
            var rng = new Xoshiro128(77);
            long newPrice = 103_000_000;
            var prices = StudentTSubTickPath.Instance.Build(P0, newPrice, rng);
            Assert.AreEqual(Rules.SubTicksPerSlot, prices.Length, "so the path sums to the slot's r exactly");
            Assert.AreEqual(newPrice, prices[prices.Length - 1]);
            foreach (var p in prices) Assert.Greater(p, 0);
        }

        [Test]
        public void TailLiquidation_FiresMidPath_EvenWhenTheCloseAlone_WouldNotHave()
        {
            var loop = Loop.NewRun(400, new ScriptedPrices(P0, P0));
            loop.Data.overseasCash = Rules.StartingCash;
            loop.SubTickModel = LinearSubTickPath.Instance;
            Assert.IsTrue(loop.Do(new CoinOpenAction(Direction.Long, 25, 10_000_000)).Ok);
            var p = loop.Data.position;
            long liq = (long)System.Math.Floor(CoinRules.LiquidationPrice(p));

            var path = new long[Rules.SubTicksPerSlot];
            for (int i = 0; i < path.Length; i++) path[i] = P0;
            path[5] = liq - 1_000_000; // deep tail dip, well past the threshold
            loop.SubTickModel = new ScriptedSubTicks(path);

            loop.Pass(); // close (path[11]) is back at P0 — the close alone would never have liquidated
            Assert.IsFalse(loop.Data.hasPosition);
            Assert.AreEqual(1, loop.Data.stats.liquidations);
            Assert.AreEqual(1, loop.Data.stats.tailLiquidations);
        }

        /// <summary>Not a tail liquidation: the close sub-tick is ALSO past the liquidation price, so this isn't
        /// "liquidated mid-path but would have survived to the close" — it's just an ordinary liquidation that
        /// happened to be detected before we ever looked at sub-tick 12.</summary>
        [Test]
        public void Liquidation_NotTail_WhenTheCloseSubTickAlone_AlsoBreaches()
        {
            var loop = Loop.NewRun(410, new ScriptedPrices(P0, P0));
            loop.Data.overseasCash = Rules.StartingCash;
            loop.SubTickModel = LinearSubTickPath.Instance;
            Assert.IsTrue(loop.Do(new CoinOpenAction(Direction.Long, 25, 10_000_000)).Ok);
            var p = loop.Data.position;
            long liq = (long)System.Math.Floor(CoinRules.LiquidationPrice(p));

            var path = new long[Rules.SubTicksPerSlot];
            for (int i = 0; i < path.Length; i++) path[i] = P0;
            path[5] = liq - 1_000_000;      // breaches mid-path first
            path[path.Length - 1] = liq - 500_000; // the close is ALSO past the liquidation price
            loop.SubTickModel = new ScriptedSubTicks(path);

            loop.Pass();
            Assert.IsFalse(loop.Data.hasPosition);
            Assert.AreEqual(1, loop.Data.stats.liquidations);
            Assert.AreEqual(0, loop.Data.stats.tailLiquidations, "the close alone would also have liquidated — not a tail event");
        }

        /// <summary>Not a tail liquidation: nothing breaches before the close sub-tick (12th) — the close itself is the trigger.</summary>
        [Test]
        public void Liquidation_NotTail_WhenOnlyTheFinalSubTick_Breaches()
        {
            var loop = Loop.NewRun(411, new ScriptedPrices(P0, P0));
            loop.Data.overseasCash = Rules.StartingCash;
            loop.SubTickModel = LinearSubTickPath.Instance;
            Assert.IsTrue(loop.Do(new CoinOpenAction(Direction.Long, 25, 10_000_000)).Ok);
            var p = loop.Data.position;
            long liq = (long)System.Math.Floor(CoinRules.LiquidationPrice(p));

            var path = new long[Rules.SubTicksPerSlot];
            for (int i = 0; i < path.Length; i++) path[i] = P0; // safe all the way through sub-tick 11
            path[path.Length - 1] = liq - 500_000; // only the 12th (close) sub-tick breaches
            loop.SubTickModel = new ScriptedSubTicks(path);

            loop.Pass();
            Assert.IsFalse(loop.Data.hasPosition);
            Assert.AreEqual(1, loop.Data.stats.liquidations);
            Assert.AreEqual(0, loop.Data.stats.tailLiquidations, "liquidated exactly at the close — not a tail event");
        }

        [Test]
        public void AddMargin_MidWatch_PushesLiquidationPriceAway_SavingTheRemainingPath()
        {
            var loop = Loop.NewRun(401, new ScriptedPrices(P0));
            loop.Data.overseasCash = Rules.StartingCash;
            loop.SubTickModel = LinearSubTickPath.Instance;
            Assert.IsTrue(loop.Do(new CoinOpenAction(Direction.Long, 25, 5_000_000)).Ok);
            var p = loop.Data.position;

            var path = new long[Rules.SubTicksPerSlot];
            for (int i = 0; i < 5; i++) path[i] = 97_000_000;      // past the 70% watch threshold, not yet liquidated
            for (int i = 5; i < path.Length; i++) path[i] = 96_400_000; // past the ORIGINAL liquidation price
            loop.SubTickModel = new ScriptedSubTicks(path);

            var w = loop.BeginWatchableAction(RestAction.Instance);
            Assert.IsTrue(w.Walk.WatchTriggered);
            for (int i = 0; i < 5; i++) w.Walk.Step();
            Assert.IsFalse(w.Walk.Done, "not liquidated yet — watch has a chance to act");

            w.Walk.AddMargin(2_000_000); // enough to push the liquidation price below 96,400,000
            w.Walk.RunToEnd();
            loop.FinishWatch(w.Walk);

            Assert.IsTrue(loop.Data.hasPosition, "the added margin should have saved the position");
            Assert.AreEqual(2_000_000, p.addedMargin);
            Assert.AreEqual(0, loop.Data.stats.liquidations);
        }

        [Test]
        public void WithoutAddingMargin_TheSamePathLiquidates()
        {
            var loop = Loop.NewRun(401, new ScriptedPrices(P0));
            loop.Data.overseasCash = Rules.StartingCash;
            loop.SubTickModel = LinearSubTickPath.Instance;
            Assert.IsTrue(loop.Do(new CoinOpenAction(Direction.Long, 25, 5_000_000)).Ok);

            var path = new long[Rules.SubTicksPerSlot];
            for (int i = 0; i < 5; i++) path[i] = 97_000_000;
            for (int i = 5; i < path.Length; i++) path[i] = 96_400_000;
            loop.SubTickModel = new ScriptedSubTicks(path);

            loop.Pass();
            Assert.IsFalse(loop.Data.hasPosition);
            Assert.AreEqual(1, loop.Data.stats.liquidations);
        }

        [Test]
        public void StopLoss_ClosesImmediatelyAtTheCurrentSubTickPrice_NotALiquidation()
        {
            var loop = Loop.NewRun(402, new ScriptedPrices(P0));
            loop.Data.overseasCash = Rules.StartingCash;
            loop.SubTickModel = LinearSubTickPath.Instance;
            Assert.IsTrue(loop.Do(new CoinOpenAction(Direction.Long, 25, 5_000_000)).Ok);

            var path = new long[Rules.SubTicksPerSlot];
            for (int i = 0; i < path.Length; i++) path[i] = 97_000_000; // past watch threshold, short of liquidation
            loop.SubTickModel = new ScriptedSubTicks(path);

            var w = loop.BeginWatchableAction(RestAction.Instance);
            Assert.IsTrue(w.Walk.WatchTriggered);
            w.Walk.Step();
            w.Walk.StopLoss();
            loop.FinishWatch(w.Walk);

            Assert.IsFalse(loop.Data.hasPosition);
            Assert.AreEqual(0, loop.Data.stats.liquidations, "a voluntary stop-loss isn't a liquidation");
            Assert.AreEqual(97_000_000, loop.Data.price);
        }

        [Test]
        public void Prayer_HasZeroMechanicalEffect_SameSeedSameResultRegardlessOfPrayerCount()
        {
            Loop RunWithPrayers(int prayerCount)
            {
                var loop = Loop.NewRun(500);
                loop.Data.overseasCash = Rules.StartingCash;
                Assert.IsTrue(loop.Do(new CoinOpenAction(Direction.Long, 100, 10_000_000)).Ok);
                int guard = 0;
                while (loop.Data.hasPosition && guard++ < 200)
                {
                    var w = loop.BeginWatchableAction(RestAction.Instance);
                    if (w.Walk == null) continue;
                    if (w.Walk.WatchTriggered) for (int i = 0; i < prayerCount; i++) w.Walk.Pray();
                    loop.FinishWatch(w.Walk);
                }
                return loop;
            }

            var a = RunWithPrayers(0);
            var b = RunWithPrayers(50);

            Assert.AreEqual(a.Data.hasPosition, b.Data.hasPosition);
            Assert.AreEqual(a.Data.price, b.Data.price);
            Assert.AreEqual(a.Data.bankCash, b.Data.bankCash);
            Assert.AreEqual(a.Data.overseasCash, b.Data.overseasCash);
            Assert.AreEqual(a.Data.stats.liquidations, b.Data.stats.liquidations);
            Assert.AreEqual(a.Data.mental, b.Data.mental);
            Assert.AreNotEqual(a.Data.stats.prayers, b.Data.stats.prayers);
        }
    }
}
