using AllIn.Core.Actions;

namespace AllIn.Core
{
    /// <summary>
    /// B5 v0.3 "돈의 세 칸": bank / domestic exchange / overseas futures wallet. All transfers are free actions
    /// (no slot cost). Bank -&gt; domestic is instant and starts (re-sets) the 24h domestic->overseas lock; every
    /// other route queues a PendingTransfer that lands Rules.TransferDelaySlots later, resolved in OnSlotStart.
    /// </summary>
    public sealed partial class Loop
    {
        public bool DomesticLocked => Data.domesticLockUntilAbsSlot > AbsSlot;

        public int DomesticLockRemainingSlots => DomesticLocked ? Data.domesticLockUntilAbsSlot - AbsSlot : 0;

        public Availability TransferBankToDomestic(long amount)
        {
            if (Data.atSea) return Availability.No(Texts.AtSea);
            if (amount <= 0 || amount > Data.bankCash) return Availability.No(Texts.WalletBadAmount);
            Data.bankCash -= amount;
            Data.domesticCash += amount;
            Data.domesticLockUntilAbsSlot = AbsSlot + Rules.DomesticLockSlots; // re-set, not extended
            Snapshot();
            return Availability.Yes;
        }

        public Availability TransferDomesticToOverseas(long amount)
        {
            if (Data.atSea) return Availability.No(Texts.AtSea);
            if (DomesticLocked) return Availability.No(Texts.WalletLocked);
            if (amount <= 0 || amount > Data.domesticCash) return Availability.No(Texts.WalletBadAmount);
            Data.domesticCash -= amount;
            Data.pendingTransfers.Add(new PendingTransfer { amount = amount, kind = TransferKind.DomesticToOverseas, arrivalAbsSlot = AbsSlot + Rules.TransferDelaySlots });
            Snapshot();
            return Availability.Yes;
        }

        public Availability TransferOverseasToDomestic(long amount)
        {
            if (Data.atSea) return Availability.No(Texts.AtSea);
            if (amount <= 0 || amount > Data.overseasCash) return Availability.No(Texts.WalletBadAmount);
            Data.overseasCash -= amount;
            Data.pendingTransfers.Add(new PendingTransfer { amount = amount, kind = TransferKind.OverseasToDomestic, arrivalAbsSlot = AbsSlot + Rules.TransferDelaySlots });
            Snapshot();
            return Availability.Yes;
        }

        public Availability TransferDomesticToBank(long amount)
        {
            if (Data.atSea) return Availability.No(Texts.AtSea);
            if (amount <= 0 || amount > Data.domesticCash) return Availability.No(Texts.WalletBadAmount);
            Data.domesticCash -= amount;
            Data.pendingTransfers.Add(new PendingTransfer { amount = amount, kind = TransferKind.DomesticToBank, arrivalAbsSlot = AbsSlot + Rules.TransferDelaySlots });
            Snapshot();
            return Availability.Yes;
        }

        void ResolvePendingTransfers()
        {
            for (int i = Data.pendingTransfers.Count - 1; i >= 0; i--)
            {
                var t = Data.pendingTransfers[i];
                if (AbsSlot < t.arrivalAbsSlot) continue;
                switch (t.kind)
                {
                    case TransferKind.DomesticToOverseas:
                        Data.overseasCash += t.amount;
                        Data.lastOverseasArrivalAbsSlot = AbsSlot;
                        break;
                    case TransferKind.OverseasToDomestic:
                        Data.domesticCash += t.amount;
                        break;
                    case TransferKind.DomesticToBank:
                        Data.bankCash += t.amount;
                        break;
                }
                Data.pendingTransfers.RemoveAt(i);
            }
        }

        /// <summary>B5c "잠 못 듦": opening the coin or exchange screen at night while locked costs the next sleep half its recovery.</summary>
        public void NoteExchangeScreenOpened()
        {
            if (Data.slot == Slot.Night && DomesticLocked) Data.peekedExchangeAtNight = true;
        }
    }
}
