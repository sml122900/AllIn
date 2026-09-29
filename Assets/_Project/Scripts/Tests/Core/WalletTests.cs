using AllIn.Core;
using NUnit.Framework;

namespace AllIn.Core.Tests
{
    public class WalletTests
    {
        static Loop Run(long seed = 600)
        {
            var loop = Loop.NewRun(seed);
            loop.Data.pendingEvents.Clear();
            return loop;
        }

        [Test]
        public void BankToDomestic_IsInstant_AndStartsA5SlotLock()
        {
            var loop = Run();
            long bank = loop.Data.bankCash;
            Assert.IsTrue(loop.TransferBankToDomestic(1_000_000).Ok);
            Assert.AreEqual(bank - 1_000_000, loop.Data.bankCash);
            Assert.AreEqual(1_000_000, loop.Data.domesticCash);
            Assert.IsTrue(loop.DomesticLocked);
            Assert.AreEqual(Rules.DomesticLockSlots, loop.DomesticLockRemainingSlots);
        }

        [Test]
        public void DomesticToOverseas_BlockedWhileLocked_AllowedOnceUnlocked()
        {
            var loop = Run();
            Assert.IsTrue(loop.TransferBankToDomestic(1_000_000).Ok);
            Assert.IsFalse(loop.TransferDomesticToOverseas(500_000).Ok, "still locked");

            for (int i = 0; i < Rules.DomesticLockSlots; i++) loop.Pass();
            Assert.IsFalse(loop.DomesticLocked, "lock should have elapsed");
            Assert.IsTrue(loop.TransferDomesticToOverseas(500_000).Ok);
        }

        [Test]
        public void RedepositWhileLocked_ResetsTheLock_FromTheNewDeposit()
        {
            var loop = Run();
            Assert.IsTrue(loop.TransferBankToDomestic(1_000_000).Ok);
            int firstUnlockAbsSlot = loop.Data.domesticLockUntilAbsSlot;
            loop.Pass(); loop.Pass(); loop.Pass(); // 3 of the 5 slots elapse; still locked
            Assert.IsTrue(loop.DomesticLocked);

            Assert.IsTrue(loop.TransferBankToDomestic(500_000).Ok); // re-deposit
            Assert.AreEqual(loop.AbsSlot + Rules.DomesticLockSlots, loop.Data.domesticLockUntilAbsSlot);
            Assert.Greater(loop.Data.domesticLockUntilAbsSlot, firstUnlockAbsSlot, "reset from now, not stacked on the old deposit");
        }

        [Test]
        public void TransfersLandOneSlotLater()
        {
            var loop = Run();
            Assert.IsTrue(loop.TransferBankToDomestic(2_000_000).Ok);
            for (int i = 0; i < Rules.DomesticLockSlots; i++) loop.Pass();

            long overseasBefore = loop.Data.overseasCash;
            Assert.IsTrue(loop.TransferDomesticToOverseas(1_000_000).Ok);
            Assert.AreEqual(overseasBefore, loop.Data.overseasCash, "not there yet");
            Assert.AreEqual(1_000_000, SumPending(loop));
            loop.Pass();
            Assert.AreEqual(overseasBefore + 1_000_000, loop.Data.overseasCash);
            Assert.AreEqual(0, SumPending(loop));

            long domesticBefore = loop.Data.domesticCash;
            Assert.IsTrue(loop.TransferOverseasToDomestic(500_000).Ok);
            Assert.AreEqual(domesticBefore, loop.Data.domesticCash);
            loop.Pass();
            Assert.AreEqual(domesticBefore + 500_000, loop.Data.domesticCash);

            long bankBefore = loop.Data.bankCash;
            int foodBefore = loop.Data.stats.foodPaid;
            Assert.IsTrue(loop.TransferDomesticToBank(200_000).Ok);
            Assert.AreEqual(bankBefore, loop.Data.bankCash);
            loop.Pass();
            long foodCost = (loop.Data.stats.foodPaid - foodBefore) * Rules.FoodPerDay; // the pass may cross the evening food deduction
            Assert.AreEqual(bankBefore + 200_000 - foodCost, loop.Data.bankCash);
        }

        static long SumPending(Loop loop)
        {
            long sum = 0;
            foreach (var t in loop.Data.pendingTransfers) sum += t.amount;
            return sum;
        }

        [Test]
        public void TotalAssets_IncludesInTransitMoney()
        {
            var loop = Run();
            long total = loop.TotalAssets;
            Assert.IsTrue(loop.TransferBankToDomestic(1_000_000).Ok); // instant: bank -1M, domestic +1M, total unchanged
            Assert.AreEqual(total, loop.TotalAssets);

            for (int i = 0; i < Rules.DomesticLockSlots; i++) loop.Pass();
            total = loop.TotalAssets;
            Assert.IsTrue(loop.TransferDomesticToOverseas(1_000_000).Ok); // now in transit, still counted
            Assert.AreEqual(total, loop.TotalAssets, "in-transit money still counts toward total assets");
        }

        [Test]
        public void WagesRentFoodLotto_AllGoThroughTheBank()
        {
            var loop = Run();
            Assert.AreEqual(Rules.StartingCash - Rules.Rent, loop.Data.bankCash);
            Assert.AreEqual(0, loop.Data.domesticCash);
            Assert.AreEqual(0, loop.Data.overseasCash);
        }
    }
}
