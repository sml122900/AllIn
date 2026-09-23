namespace AllIn.Core
{
    public sealed partial class Loop
    {
        public Position OpenPosition => Data.hasPosition ? Data.position : null;

        /// <summary>Margin + unrealized PnL of the open position, 0 if none.</summary>
        public long PositionValue => Data.hasPosition ? CoinRules.Value(Data.position, Data.price) : 0;

        public long UnrealizedPnl => Data.hasPosition ? (long)System.Math.Round(CoinRules.UnrealizedPnl(Data.position, Data.price)) : 0;

        public int SuggestedLeverage => CoinRules.SuggestedLeverage(Data.lastLeverage, Data.mental);

        public long MinCommit => CoinRules.MinCommit(Data.cash, Data.mental);

        partial void OnTick()
        {
            if (!Data.hasPosition || !CoinRules.IsLiquidated(Data.position, Data.price)) return;

            var p = Data.position;
            Data.hasPosition = false;
            Data.stats.liquidations++;
            Data.stats.coinRealized -= p.committed;
            AddMental(-Rules.LiquidationMental);
            // The tick that just happened moved us into Data.slot; night-side ticks wake you at 3am.
            string id = Calendar.IsNightSide(Data.slot) ? EventIds.LiquidationNight : EventIds.LiquidationDay;
            Emit(new GameEvent { id = id, amount = p.committed, value = Data.price, count = p.leverage });
            Log(Texts.E1Title + " " + Fmt.Won(-p.committed));
            Data.e3AtSlot = AbsSlot + 1;
        }

        /// <summary>Called when the player opens the coin screen: may fire the hope circuit (E4), once a day.</summary>
        public GameEvent OnCoinScreenOpened()
        {
            if (Data.mental < Rules.HopeCircuitAt || !Data.hasLastWin || Data.hopeShownDay == Data.day || Data.finished) return null;
            return EmitHope();
        }

        internal GameEvent TryHopeAfterWin()
        {
            if (Data.mental < Rules.HopeCircuitAt || Data.hopeShownDay == Data.day) return null;
            return EmitHope();
        }

        GameEvent EmitHope()
        {
            Data.hopeShownDay = Data.day;
            Data.stats.hopeCircuits++;
            long total = TotalAssets;
            var e = new GameEvent
            {
                id = EventIds.HopeCircuit,
                amount = total,
                value = CoinRules.HopeDailyReturn(Data.lastWin),
                value2 = CoinRules.HopeProjection(total, Data.lastWin, DaysLeft),
                count = DaysLeft,
            };
            Emit(e);
            Snapshot();
            return e;
        }
    }
}
