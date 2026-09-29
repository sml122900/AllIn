using System;
using AllIn.Core.Actions;

namespace AllIn.Core
{
    public sealed partial class Loop
    {
        public Position OpenPosition => Data.hasPosition ? Data.position : null;

        /// <summary>B5a: margin add is free and available any time a position is open, not just mid-watch.</summary>
        public Availability AddMarginToPosition(long amount)
        {
            if (Data.atSea) return Availability.No(Texts.AtSea);
            if (!Data.hasPosition) return Availability.No(Texts.CoinNoPosition);
            if (amount <= 0 || amount > Data.overseasCash) return Availability.No(Texts.WalletBadAmount);
            Data.overseasCash -= amount;
            Data.position.addedMargin += amount;
            Snapshot();
            return Availability.Yes;
        }

        /// <summary>Margin + unrealized PnL of the open position, 0 if none.</summary>
        public long PositionValue => Data.hasPosition ? CoinRules.Value(Data.position, Data.price) : 0;

        public long UnrealizedPnl => Data.hasPosition ? (long)System.Math.Round(CoinRules.UnrealizedPnl(Data.position, Data.price)) : 0;

        public int SuggestedLeverage => CoinRules.SuggestedLeverage(Data.lastLeverage, Data.mental);

        /// <summary>Coin margin only ever comes from the overseas wallet (B5 v0.3).</summary>
        public long MinCommit => CoinRules.MinCommit(Data.overseasCash, Data.mental);

        /// <summary>Ghost sweep (B5d) — runs every slot regardless of whether a position is open.</summary>
        partial void OnTick()
        {
            if (Data.ghosts.Count == 0) return;
            for (int i = Data.ghosts.Count - 1; i >= 0; i--)
            {
                var g = Data.ghosts[i];
                long pnl = (long)Math.Round(CoinRules.GhostUnrealizedPnl(g, Data.price));
                if (pnl > g.peakFavorablePnl) g.peakFavorablePnl = pnl;

                bool recovered = g.direction == Direction.Long ? Data.price >= g.entryPrice : Data.price <= g.entryPrice;
                if (recovered)
                {
                    bool enhanced = Data.lastOverseasArrivalAbsSlot > g.liqAbsSlot &&
                                     AbsSlot - Data.lastOverseasArrivalAbsSlot <= Rules.GhostEnhancedWindowSlots;
                    Data.stats.ghostFired++;
                    if (enhanced) Data.stats.ghostFiredEnhanced++;
                    Data.stats.ghostResolutionSlots.Add(AbsSlot - g.liqAbsSlot);
                    Data.stats.ghostMissedMoney += Math.Max(0, g.peakFavorablePnl);
                    if (!Data.atSea) // at sea (D4) nothing pops up piecemeal; the return report summarizes
                    {
                        AddMental(enhanced ? -Rules.GhostMentalEnhanced : -Rules.GhostMental);
                        Emit(new GameEvent
                        {
                            id = EventIds.Ghost,
                            amount = g.committed + g.addedMargin,
                            amount2 = g.entryPrice,
                            amount3 = pnl,
                            ints = new[] { enhanced ? 1 : 0 },
                        });
                        Log(Texts.E8Title);
                    }
                    Data.ghosts.RemoveAt(i);
                    continue;
                }
                if (AbsSlot > g.expiryAbsSlot)
                {
                    Data.stats.ghostMissedMoney += Math.Max(0, g.peakFavorablePnl);
                    Data.ghosts.RemoveAt(i);
                }
            }
        }

        /// <summary>Liquidation (B5 v0.3): moves from OnTick into here since it now happens mid-walk, at whichever
        /// sub-tick's price crossed the threshold — not necessarily the slot's close price.</summary>
        void LiquidatePosition(bool tail)
        {
            var p = Data.position;
            long lost = p.committed + p.addedMargin;
            Data.hasPosition = false;
            Data.stats.liquidations++;
            if (tail) Data.stats.tailLiquidations++;
            if (DomesticLocked) Data.stats.liquidationsDuringLock++;
            Data.stats.coinRealized -= lost;
            AddMental(-Rules.LiquidationMental);
            Data.tilted = true;
            Data.lastLiquidationLoss = lost;
            Data.ghosts.Add(new Ghost
            {
                direction = p.direction,
                leverage = p.leverage,
                committed = p.committed,
                addedMargin = p.addedMargin,
                entryPrice = p.entryPrice,
                liqAbsSlot = AbsSlot,
                expiryAbsSlot = AbsSlot + Rules.GhostTrackSlots,
            });
            if (Data.atSea) // D4: liquidated while the phone is in a locker — counted, but no popup and no E3
            {
                Log(Texts.E1Title + " " + Fmt.Won(-lost));
                return;
            }
            // The tick that just happened moved us into Data.slot; night-side ticks wake you at 3am.
            string id = Calendar.IsNightSide(Data.slot) ? EventIds.LiquidationNight : EventIds.LiquidationDay;
            Emit(new GameEvent { id = id, amount = lost, amount2 = Data.price, count = p.leverage });
            Log(Texts.E1Title + " " + Fmt.Won(-lost));
            Data.e3AtSlot = AbsSlot + 1;
        }

        /// <summary>Shared close math for a manual close (CoinCloseAction) and a watch-scene stop-loss (SubTickWalk).</summary>
        internal long ClosePositionAt(long price, bool quiet = false)
        {
            var d = Data;
            var p = d.position;
            long proceeds = CoinRules.CloseProceeds(p, price);
            long realized = proceeds - (p.committed + p.addedMargin);
            d.hasPosition = false;
            AddOverseas(proceeds);
            d.stats.coinRealized += realized;
            if (quiet) return realized; // D4: closing before the voyage — no mental swing, no hope circuit
            AddMental(CoinRules.CloseMentalDelta(realized, TotalAssets));
            if (realized > 0)
            {
                d.hasLastWin = true;
                d.lastWin = new LastWin { realized = realized, committed = p.committed + p.addedMargin, heldTicks = AbsSlot - p.openedAtSlot };
                TryHopeAfterWin();
            }
            return realized;
        }

        /// <summary>Called when the player opens the coin or exchange screen: may fire the hope circuit (E4), once a
        /// day, and marks "잠 못 듦" (B5c) if it's a locked night.</summary>
        public GameEvent OnCoinScreenOpened()
        {
            NoteExchangeScreenOpened();
            if (Data.mental < Rules.HopeCircuitAt || !Data.hasLastWin || Data.hopeShownDay == Data.day || Data.finished) return null;
            return EmitHope();
        }

        internal GameEvent TryHopeAfterWin()
        {
            if (Data.mental < Rules.HopeCircuitAt || Data.hopeShownDay == Data.day) return null;
            return EmitHope();
        }

        /// <summary>E4 carries the daily rate as parts-per-million (integral, so it saves exactly).</summary>
        public const double HopeRateScale = 1_000_000;

        static long ClampToLong(double v) => double.IsNaN(v) ? 0 : v >= long.MaxValue ? long.MaxValue : v <= long.MinValue ? long.MinValue : (long)Math.Round(v);

        GameEvent EmitHope()
        {
            Data.hopeShownDay = Data.day;
            Data.stats.hopeCircuits++;
            long total = TotalAssets;
            var e = new GameEvent
            {
                id = EventIds.HopeCircuit,
                amount = total,
                amount2 = ClampToLong(CoinRules.HopeProjection(total, Data.lastWin, DaysLeft)),
                amount3 = (long)Math.Round(CoinRules.HopeDailyReturn(Data.lastWin) * HopeRateScale),
                count = DaysLeft,
            };
            Emit(e);
            Snapshot();
            return e;
        }
    }
}
