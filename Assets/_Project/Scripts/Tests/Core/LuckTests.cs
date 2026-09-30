using AllIn.Core;
using AllIn.Core.Actions;
using NUnit.Framework;

namespace AllIn.Core.Tests
{
    /// <summary>DESIGN.md E1/E3: "무조건 승리" on the coin watch scene.</summary>
    public class LuckTests
    {
        const long P0 = Rules.CoinStartPrice;

        /// <summary>A 25x long of 5,000,000 with a scripted sub-tick path; returns the loop with the position open.</summary>
        static Loop WithPosition(long seed, double q, long[] path, int leverage = 25)
        {
            var loop = Loop.NewRun(seed, new ScriptedPrices(P0, P0, P0));
            loop.LuckQCoin = q;
            loop.Data.overseasCash = Rules.StartingCash;
            loop.SubTickModel = LinearSubTickPath.Instance;
            Assert.IsTrue(loop.Do(new CoinOpenAction(Direction.Long, leverage, 5_000_000)).Ok);
            loop.SubTickModel = new ScriptedSubTicks(path);
            return loop;
        }

        static long[] Flat(long price)
        {
            var path = new long[Rules.SubTicksPerSlot];
            for (int i = 0; i < path.Length; i++) path[i] = price;
            return path;
        }

        /// <summary>A price inside the watch band (past 70% of the margin lost, short of liquidation) for the open position.</summary>
        static long WatchPriceOf(Position p) =>
            (long)System.Math.Floor(p.entryPrice - CoinRules.TotalMargin(p) * Rules.WatchLossRatio / CoinRules.Quantity(p)) - 1;

        static long LiqOf(Loop loop) => (long)System.Math.Floor(CoinRules.LiquidationPrice(loop.Data.position));

        [Test]
        public void QZero_NeverDrawsTheLuckStream_EvenThroughAWatchScene()
        {
            var loop = WithPosition(1, 0, Flat(97_000_000));
            var before = (int[])loop.Snapshot().luckRngState.Clone();
            var w = loop.BeginWatchableAction(RestAction.Instance);
            Assert.IsTrue(w.Walk.WatchTriggered);
            Assert.IsFalse(w.Walk.LuckTriggered);
            loop.FinishWatch(w.Walk);
            CollectionAssert.AreEqual(before, loop.Snapshot().luckRngState);
            Assert.AreEqual(0, loop.Data.stats.luckUses);
        }

        [Test]
        public void NoOffer_WithoutAnOpenPosition_ThereIsNothingToBetOn()
        {
            var loop = Loop.NewRun(2);
            loop.LuckQCoin = 1;
            var before = (int[])loop.Snapshot().luckRngState.Clone();
            for (int i = 0; i < 20; i++) loop.Pass();
            CollectionAssert.AreEqual(before, loop.Snapshot().luckRngState, "not even rolled while no bet stands");
            Assert.AreEqual(0, loop.Data.stats.luckUses);
        }

        [Test]
        public void NoOffer_WhenThePositionIsNotInTrouble_NoWatchSceneMeansNoRoll()
        {
            var loop = WithPosition(3, 1, Flat(P0));
            var before = (int[])loop.Snapshot().luckRngState.Clone();
            var w = loop.BeginWatchableAction(RestAction.Instance);
            Assert.IsNull(w.Walk, "no watch scene, so no walk is handed out");
            CollectionAssert.AreEqual(before, loop.Snapshot().luckRngState);
        }

        [Test]
        public void Offer_PausesTheWalkUntilPressed_ThenReboundsToRoeOneHundredPercentAndCloses()
        {
            var loop = WithPosition(4, 1, Flat(97_000_000));
            var p = loop.Data.position;
            long overseasBefore = loop.Data.overseasCash;
            var w = loop.BeginWatchableAction(RestAction.Instance);
            var walk = w.Walk;
            Assert.IsTrue(walk.LuckTriggered);
            Assert.IsFalse(walk.LuckPending, "the button comes up at the trigger sub-tick, not before");
            walk.Step();
            Assert.IsTrue(walk.LuckPending);
            int at = walk.Index;
            walk.Step(); walk.Step();
            Assert.AreEqual(at, walk.Index, "nothing advances until it is pressed");
            Assert.IsFalse(walk.Done);

            Assert.IsTrue(walk.UseLuck());
            Assert.IsFalse(walk.LuckPending);
            long last = 0;
            while (!walk.Done) { walk.Step(); Assert.GreaterOrEqual(loop.Data.price, last, "the path only rebounds"); last = loop.Data.price; }
            loop.FinishWatch(walk);

            Assert.IsFalse(loop.Data.hasPosition);
            Assert.AreEqual(0, loop.Data.stats.liquidations);
            Assert.AreEqual(1, loop.Data.stats.luckUses);
            long total = CoinRules.TotalMargin(p);
            long proceeds = loop.Data.overseasCash - overseasBefore;
            Assert.AreEqual(2 * total, proceeds + CoinRules.ExitFee(p, loop.Data.price), 5, "margin doubled, exit fee deducted");
            Assert.Greater(proceeds, total);
        }

        [Test]
        public void Reward_IsDoubleTheMargin_RegardlessOfLeverage()
        {
            foreach (int lev in new[] { 5, 25, 100 })
            {
                var loop = WithPosition(10 + lev, 1, Flat(P0), lev);
                var p = loop.Data.position;
                long liq = LiqOf(loop);
                double lossToWatch = CoinRules.TotalMargin(p) * Rules.WatchLossRatio;
                long watchPrice = (long)System.Math.Floor(p.entryPrice - lossToWatch / CoinRules.Quantity(p)) - 1;
                Assert.Greater(watchPrice, liq, "lev " + lev + ": the scripted price is in the watch band");
                loop.SubTickModel = new ScriptedSubTicks(Flat(watchPrice));
                long before = loop.Data.overseasCash;
                loop.Pass();
                Assert.AreEqual(1, loop.Data.stats.luckUses, "lev " + lev);
                long proceeds = loop.Data.overseasCash - before;
                Assert.AreEqual(2 * CoinRules.TotalMargin(p), proceeds + CoinRules.ExitFee(p, loop.Data.price), 8, "lev " + lev);
            }
        }

        [Test]
        public void AGapStraightPastTheLiquidationPrice_IsStillRescued_AndNoLiquidationIsCounted()
        {
            var loop = WithPosition(5, 1, Flat(P0));
            long liq = LiqOf(loop);
            loop.SubTickModel = new ScriptedSubTicks(Flat(liq - 2_000_000));
            loop.Pass();   // headless: the offer counts as pressed
            Assert.IsFalse(loop.Data.hasPosition);
            Assert.AreEqual(0, loop.Data.stats.liquidations);
            Assert.AreEqual(0, loop.Data.ghosts.Count, "not a liquidation: no ghost");
            Assert.IsFalse(loop.Data.tilted);
            Assert.AreEqual(1, loop.Data.stats.luckUses);
        }

        [Test]
        public void SamePath_WithoutLuck_Liquidates()
        {
            var loop = WithPosition(5, 0, Flat(P0));
            long liq = LiqOf(loop);
            loop.SubTickModel = new ScriptedSubTicks(Flat(liq - 2_000_000));
            loop.Pass();
            Assert.AreEqual(1, loop.Data.stats.liquidations);
        }

        [Test]
        public void TriggerOnTheLastSubTick_IsStillOffered_AndBecomesTheClose()
        {
            var loop = WithPosition(6, 1, Flat(P0));
            var path = Flat(P0);
            path[Rules.SubTicksPerSlot - 1] = 97_000_000;
            loop.SubTickModel = new ScriptedSubTicks(path);
            var w = loop.BeginWatchableAction(RestAction.Instance);
            for (int i = 0; i < Rules.SubTicksPerSlot; i++) w.Walk.Step();
            Assert.IsTrue(w.Walk.LuckPending, "the walk ran out of sub-ticks but the button is up");
            Assert.IsFalse(w.Walk.Done);
            Assert.IsTrue(w.Walk.UseLuck());
            Assert.IsTrue(w.Walk.Done);
            loop.FinishWatch(w.Walk);
            Assert.IsFalse(loop.Data.hasPosition);
            Assert.AreEqual(1, loop.Data.stats.luckUses);
        }

        [Test]
        public void NothingStacks_TheNextWatchSceneRollsAgain_OnItsOwn()
        {
            var loop = WithPosition(7, 1, Flat(97_000_000));
            loop.Pass();
            Assert.AreEqual(1, loop.Data.stats.luckUses);
            Assert.IsFalse(loop.Data.hasPosition);
            loop.Data.overseasCash = Rules.StartingCash;
            loop.SubTickModel = LinearSubTickPath.Instance;
            loop.LuckQCoin = 0;
            loop.PriceModel = new ScriptedPrices();   // the market stands still from here
            Assert.IsTrue(loop.Do(new CoinOpenAction(Direction.Long, 25, 5_000_000)).Ok);
            loop.SubTickModel = new ScriptedSubTicks(Flat(WatchPriceOf(loop.Data.position)));
            var w = loop.BeginWatchableAction(RestAction.Instance);
            Assert.IsTrue(w.Walk.WatchTriggered);
            Assert.IsFalse(w.Walk.LuckTriggered, "no coupon was kept from the last time");
        }

        [Test]
        public void NotWhileAsleep_TheNightRestHasNoWatchScene()
        {
            var loop = WithPosition(8, 1, Flat(P0));
            while (loop.Data.slot != Slot.Night) { loop.SubTickModel = new ScriptedSubTicks(Flat(P0)); loop.Pass(); }
            loop.SubTickModel = new ScriptedSubTicks(Flat(97_000_000));
            var before = (int[])loop.Snapshot().luckRngState.Clone();
            var w = loop.BeginWatchableAction(RestAction.Instance);
            Assert.IsFalse(w.Walk.LuckTriggered);
            CollectionAssert.AreEqual(before, loop.Snapshot().luckRngState);
        }

        [Test]
        public void WhileWorking_TheOfferStillComes_ThePlayerCanOnlyPressIt()
        {
            var loop = WithPosition(9, 1, Flat(P0));
            while (loop.Data.slot != Slot.Evening) { loop.SubTickModel = new ScriptedSubTicks(Flat(P0)); loop.Pass(); }
            loop.SubTickModel = new ScriptedSubTicks(Flat(97_000_000));
            var w = loop.BeginWatchableAction(WorkRestaurantAction.Instance);
            Assert.IsTrue(w.Walk.LuckTriggered);
        }

        [Test]
        public void Independent_OfPrayers_SameSeedSameLuckAndSameOutcome()
        {
            Loop Run(int prayers)
            {
                var loop = Loop.NewRun(600);
                loop.LuckQCoin = 0.5;
                loop.Data.overseasCash = Rules.StartingCash;
                Assert.IsTrue(loop.Do(new CoinOpenAction(Direction.Long, 100, 10_000_000)).Ok);
                int guard = 0;
                while (loop.Data.hasPosition && guard++ < 200)
                {
                    var w = loop.BeginWatchableAction(RestAction.Instance);
                    if (w.Walk == null) continue;
                    if (w.Walk.WatchTriggered) for (int i = 0; i < prayers; i++) w.Walk.Pray();
                    if (w.Walk.LuckTriggered)
                    {
                        while (!w.Walk.LuckPending && !w.Walk.Done) w.Walk.Step();
                        if (w.Walk.LuckPending) w.Walk.UseLuck();
                    }
                    loop.FinishWatch(w.Walk);
                }
                return loop;
            }

            var a = Run(0);
            var b = Run(50);
            Assert.AreEqual(a.Data.stats.luckUses, b.Data.stats.luckUses);
            Assert.AreEqual(a.Data.price, b.Data.price);
            Assert.AreEqual(a.Data.overseasCash, b.Data.overseasCash);
            Assert.AreEqual(a.Data.stats.liquidations, b.Data.stats.liquidations);
            Assert.AreEqual(a.Data.mental, b.Data.mental);
            CollectionAssert.AreEqual(a.Snapshot().luckRngState, b.Snapshot().luckRngState);
            Assert.AreNotEqual(a.Data.stats.prayers, b.Data.stats.prayers);
        }

        [Test]
        public void Independent_OfMentalAndWealth_TheRollSequenceIsTheStreamAlone()
        {
            var a = Loop.NewRun(700);
            var b = Loop.NewRun(700);
            b.Data.mental = 0; b.Data.bankCash = 1; b.Data.stats.prayers = 9999;
            for (int i = 0; i < 50; i++) Assert.AreEqual(a.LuckRng.NextUInt(), b.LuckRng.NextUInt());
        }

        [Test]
        public void Lotto_IsUntouched_ByTheLuckSetting()
        {
            Loop Run(double q)
            {
                var loop = Loop.NewRun(800);
                loop.LuckQCoin = q;
                for (int i = 0; i < 40; i++)
                {
                    if (loop.Data.slot != Slot.Night) loop.Do(LottoBuyAction.Instance);
                    loop.Pass();
                }
                return loop;
            }
            var a = Run(0);
            var b = Run(1);
            Assert.AreEqual(a.Data.bankCash, b.Data.bankCash);
            Assert.AreEqual(a.Data.stats.lottoWon, b.Data.stats.lottoWon);
            Assert.AreEqual(a.Data.stats.lottoSpent, b.Data.stats.lottoSpent);
            Assert.AreEqual(0, b.Data.stats.luckUses);
        }

        [Test]
        public void SaveVersion1_MigratesTo2_WithTheLuckStreamDerivedFromTheSeed()
        {
            var fresh = Loop.NewRun(900);
            var old = fresh.Snapshot();
            old.version = 1;
            old.luckRngState = new int[4];
            var loaded = Loop.FromSave(old);
            Assert.AreEqual(SaveData.CurrentVersion, loaded.Data.version);
            var again = Loop.NewRun(900);
            for (int i = 0; i < 10; i++) Assert.AreEqual(again.LuckRng.NextUInt(), loaded.LuckRng.NextUInt());
        }

        [Test]
        public void LuckStream_RoundTripsThroughASnapshot()
        {
            var loop = Loop.NewRun(901);
            for (int i = 0; i < 7; i++) loop.LuckRng.NextUInt();
            var copy = Loop.FromSave(loop.Snapshot());
            for (int i = 0; i < 10; i++) Assert.AreEqual(loop.LuckRng.NextUInt(), copy.LuckRng.NextUInt());
        }
    }
}
