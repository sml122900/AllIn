using System.Collections.Generic;
using System.Linq;
using AllIn.Core;
using AllIn.Core.Actions;
using NUnit.Framework;

namespace AllIn.Core.Tests
{
    /// <summary>Feeds a fixed price sequence, then holds the last price.</summary>
    public sealed class ScriptedPrices : IPriceModel
    {
        readonly Queue<long> _next;
        public ScriptedPrices(params long[] prices) { _next = new Queue<long>(prices); }
        public long Next(long price, IRng rng) => _next.Count > 0 ? _next.Dequeue() : price;
    }

    public class CoinTests
    {
        const long P0 = Rules.CoinStartPrice;

        static Loop Run(params long[] prices)
        {
            var loop = Loop.NewRun(100, new ScriptedPrices(prices));
            loop.Data.pendingEvents.Clear();
            return loop;
        }

        static Position Open(Loop loop, Direction dir, int lev, long commit)
        {
            Assert.IsTrue(loop.Do(new CoinOpenAction(dir, lev, commit)).Ok);
            return loop.Data.position;
        }

        [Test]
        public void Fees_EntryAndExit_AreEachNotionalTimes0_05Percent()
        {
            var loop = Run(P0);
            var p = Open(loop, Direction.Long, 100, 1_000_000);
            Assert.AreEqual(50_000, CoinRules.EntryFee(p), "100M notional × 0.05%");
            Assert.AreEqual(950_000, p.margin);
            Assert.AreEqual(50_000, CoinRules.ExitFee(p, P0));
            loop.Pass(); // price unchanged
            Assert.IsTrue(loop.Do(CoinCloseAction.Instance).Ok);
            Assert.AreEqual(-100_000, loop.Data.stats.coinRealized, "100x round trip at a flat price costs 10% of the commit");
        }

        [Test]
        public void Fees_ExitUsesNotionalAtTheExitPrice()
        {
            var loop = Run(P0 * 102 / 100);
            var p = Open(loop, Direction.Long, 10, 1_000_000);
            loop.Pass();
            Assert.AreEqual(5_100, CoinRules.ExitFee(p, loop.Data.price)); // 10M × 1.02 × 0.05%
        }

        [Test]
        public void Liquidation_FiresAtTheTickLossReachesMargin_NotBefore()
        {
            var probe = CoinRules.Open(Direction.Long, 25, 10_000_000, P0, 0);
            long liq = (long)System.Math.Floor(CoinRules.LiquidationPrice(probe)); // first whole-won price at/below the threshold
            long justAbove = liq + 1; // 1 won above: loss still short of margin × 0.9
            var loop = Run(justAbove, liq);
            Open(loop, Direction.Long, 25, 10_000_000);

            loop.Pass();
            Assert.IsTrue(loop.Data.hasPosition, "one won short of the threshold must not liquidate");
            Assert.AreEqual(0, loop.Data.stats.liquidations);

            loop.Pass();
            Assert.IsFalse(loop.Data.hasPosition);
            Assert.AreEqual(1, loop.Data.stats.liquidations);
            Assert.AreEqual(-10_000_000, loop.Data.stats.coinRealized, "whole commit gone, nothing returned");
        }

        [Test]
        public void Liquidation_ShortSide_MirrorsLong()
        {
            var probe = CoinRules.Open(Direction.Short, 50, 1_000_000, P0, 0);
            long liq = (long)System.Math.Ceiling(CoinRules.LiquidationPrice(probe));
            Assert.Greater(liq, P0);
            Assert.IsFalse(CoinRules.IsLiquidated(probe, liq - 1));
            Assert.IsTrue(CoinRules.IsLiquidated(probe, liq));
        }

        [Test]
        public void Liquidation_FiresWhileAsleep_AsE2_ThenE3NextSlot()
        {
            var loop = Run();
            while (loop.Data.slot != Slot.Night) loop.Pass();
            var p = Open(loop, Direction.Long, 100, 5_000_000);
            loop.PriceModel = new ScriptedPrices((long)(CoinRules.LiquidationPrice(p) * 0.99));
            loop.Data.pendingEvents.Clear();
            loop.Pass(); // sleep → the night→dawn tick liquidates
            Assert.AreEqual(Slot.Dawn, loop.Data.slot);
            Assert.AreEqual(1, loop.Data.stats.liquidations);
            var ids = loop.Data.pendingEvents.Select(e => e.id).ToList();
            Assert.Contains(EventIds.LiquidationNight, ids);
            Assert.IsFalse(ids.Contains(EventIds.SelfPity), "E3 waits for the next slot");
            loop.Data.pendingEvents.Clear();
            loop.Pass();
            Assert.AreEqual(EventIds.SelfPity, loop.PopEvent().id);
        }

        [Test]
        public void Liquidation_DuringDaytimeTick_IsE1_AndCostsMental40()
        {
            var loop = Run();
            var p = Open(loop, Direction.Long, 100, 5_000_000);
            loop.PriceModel = new ScriptedPrices((long)(CoinRules.LiquidationPrice(p) * 0.99));
            int mental = loop.Data.mental;
            loop.Pass(); // dawn → morning
            Assert.AreEqual(EventIds.LiquidationDay, loop.PopEvent().id);
            Assert.AreEqual(mental - Rules.LiquidationMental, loop.Data.mental);
        }

        [Test]
        public void ManualClose_InTheSameSlot_IsRefused()
        {
            var loop = Run(P0);
            Open(loop, Direction.Long, 10, 1_000_000);
            Assert.IsFalse(loop.Do(CoinCloseAction.Instance).Ok);
            loop.Pass();
            Assert.IsTrue(loop.Do(CoinCloseAction.Instance).Ok);
        }

        [Test]
        public void OnlyOnePosition()
        {
            var loop = Run();
            Open(loop, Direction.Long, 10, 1_000_000);
            Assert.IsFalse(loop.Do(new CoinOpenAction(Direction.Short, 10, 1_000_000)).Ok);
        }

        [Test]
        public void OpeningDoesNotEndTheSlot()
        {
            var loop = Run();
            Open(loop, Direction.Long, 10, 1_000_000);
            Assert.AreEqual(Slot.Dawn, loop.Data.slot);
        }

        [Test]
        public void TotalAssets_IsCashPlusMarginPlusUnrealized()
        {
            var loop = Run(P0 * 101 / 100);
            long before = loop.Data.cash;
            var p = Open(loop, Direction.Long, 10, 1_000_000);
            loop.Pass();
            long expected = before - 1_000_000 + p.margin + 100_000; // 10M notional × 1%
            Assert.AreEqual(expected, loop.TotalAssets);
        }

        [Test]
        public void ChasingLosses_Mental29_ForcesMinCommitQuarterOfCash_Mental30DoesNot()
        {
            var loop = Run();
            loop.Data.cash = 1_000_000;
            loop.Data.mental = 29;
            Assert.AreEqual(250_000, loop.MinCommit);
            Assert.IsFalse(loop.Do(new CoinOpenAction(Direction.Long, 10, 249_999)).Ok);
            loop.Data.mental = 30;
            Assert.AreEqual(1, loop.MinCommit);
            Assert.IsTrue(loop.Do(new CoinOpenAction(Direction.Long, 10, 249_999)).Ok);
        }

        [TestCase(-1_000, 100_000, -5)]      // 1% loss → clamped up to 5
        [TestCase(-10_000, 100_000, -10)]
        [TestCase(-90_000, 100_000, -25)]    // 90% → clamped to 25
        [TestCase(1_000, 100_000, 5)]
        [TestCase(20_000, 100_000, 20)]
        [TestCase(500_000, 100_000, 30)]     // clamped to 30
        public void CloseMental_IsShareOfTotalAssets_Clamped(long realized, long total, int expected) =>
            Assert.AreEqual(expected, CoinRules.CloseMentalDelta(realized, total));

        [Test]
        public void HopeCircuit_Mental79_NoE4_Mental80_E4_OncePerDay()
        {
            var loop = Run();
            loop.Data.hasLastWin = true;
            loop.Data.lastWin = new LastWin { realized = 10, committed = 100, heldTicks = 5 };
            loop.Data.mental = 79;
            Assert.IsNull(loop.OnCoinScreenOpened());
            loop.Data.mental = 80;
            var e = loop.OnCoinScreenOpened();
            Assert.IsNotNull(e);
            Assert.AreEqual(EventIds.HopeCircuit, e.id);
            Assert.IsNull(loop.OnCoinScreenOpened(), "once a day");
            Assert.AreEqual(1, loop.Data.stats.hopeCircuits);
        }

        [Test]
        public void HopeCircuit_ProjectionIsRealCompounding()
        {
            var w = new LastWin { realized = 20, committed = 100, heldTicks = 10 }; // 2 days → 10%/day
            Assert.AreEqual(0.10, CoinRules.HopeDailyReturn(w), 1e-12);
            Assert.AreEqual(1_000_000 * System.Math.Pow(1.1, 20), CoinRules.HopeProjection(1_000_000, w, 20), 1e-3);
            var quick = new LastWin { realized = 5, committed = 100, heldTicks = 1 }; // under a day counts as 1 day
            Assert.AreEqual(0.05, CoinRules.HopeDailyReturn(quick), 1e-12);
        }

        [Test]
        public void HopeCircuit_FiresRightAfterAProfitableClose_WhenMentalHigh()
        {
            var loop = Run(P0 * 105 / 100);
            loop.Data.mental = 85;
            Open(loop, Direction.Long, 10, 5_000_000);
            loop.Pass();
            loop.Data.pendingEvents.Clear();
            loop.Do(CoinCloseAction.Instance);
            Assert.AreEqual(EventIds.HopeCircuit, loop.PopEvent().id);
        }

        [Test]
        public void SuggestedLeverage_StepsUpOnlyInHopeCircuit()
        {
            Assert.AreEqual(25, CoinRules.SuggestedLeverage(25, 79));
            Assert.AreEqual(50, CoinRules.SuggestedLeverage(25, 80));
            Assert.AreEqual(100, CoinRules.SuggestedLeverage(100, 90));
        }

        [Test]
        public void TickReturns_AreCappedAt25Percent()
        {
            var r = new Xoshiro128(5);
            for (int i = 0; i < 200000; i++)
            {
                double x = StudentTPriceModel.LogReturn(r);
                Assert.That(x >= -0.25 && x <= 0.25);
            }
        }
    }
}
