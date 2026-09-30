using System;
using AllIn.Core.Actions;

namespace AllIn.Core
{
    /// <summary>
    /// The 30-day loop: owns the SaveData and applies every rule. Presentation calls Do(action), then drains
    /// events with PopEvent(). No undo: there is no way back to an earlier state short of a new run.
    /// </summary>
    public sealed partial class Loop
    {
        public SaveData Data { get; }

        /// <summary>Coin price path only — separate stream so every strategy faces the same market for a seed.</summary>
        public IRng MarketRng => _market;

        /// <summary>Everything else: lotto numbers, job mistakes, flavor picks.</summary>
        public IRng Rng => _rng;

        /// <summary>Sub-tick decomposition of the slot's price move (B5 v0.3), separate from the market stream.</summary>
        public IRng SubTickRng => _subTick;

        public IPriceModel PriceModel { get; set; } = StudentTPriceModel.Instance;
        public ISubTickPath SubTickModel { get; set; } = StudentTSubTickPath.Instance;

        readonly Xoshiro128 _market;
        readonly Xoshiro128 _rng;
        readonly Xoshiro128 _subTick;
        readonly Xoshiro128 _luck;

        /// <summary>E1 "무조건 승리" rolls only. Never drawn while q = 0, and never touched by prayers, mental or anything else.</summary>
        public IRng LuckRng => _luck;

        /// <summary>Chance a watch scene offers "무조건 승리" (DESIGN.md E3). Sim-tunable, not saved; the rule default is 0.</summary>
        public double LuckQCoin { get; set; } = DefaultLuckQCoin;

        /// <summary>Starting value of <see cref="LuckQCoin"/> for new Loops (sims / tests that must not see luck set it to 0; the game never touches it).</summary>
        public static double DefaultLuckQCoin = Rules.LuckQCoin;

        const ulong LuckSalt = 0x1AC1AC1AC1ACUL;

        Loop(SaveData data, Xoshiro128 market, Xoshiro128 rng, Xoshiro128 subTick, Xoshiro128 luck)
        {
            Data = data;
            _market = market;
            _rng = rng;
            _subTick = subTick;
            _luck = luck;
        }

        public static Loop NewRun(long seed, IPriceModel priceModel = null)
        {
            var d = new SaveData
            {
                seed = seed,
                day = 1,
                slot = Slot.Dawn,
                body = Rules.BodyStart,
                mental = Rules.MentalStart,
                price = Rules.CoinStartPrice,
            };
            var loop = new Loop(d, new Xoshiro128((ulong)seed), new Xoshiro128((ulong)seed ^ 0x5EED5EED5EEDUL), new Xoshiro128((ulong)seed ^ 0x5AB7A11C5AB7A11CUL), new Xoshiro128((ulong)seed ^ LuckSalt));
            if (priceModel != null) loop.PriceModel = priceModel;
            d.priceHistory.Add(d.price);

            loop.Emit(new GameEvent { id = EventIds.Opening });
            d.bankCash = Rules.StartingCash;
            loop.PayFixedFromBank(Rules.Rent);
            d.stats.rentPaid++;
            loop.Log(Texts.RentPaid);
            d.room = Calendar.RoomFor(loop.TotalCashLikeForRoom);
            loop.Emit(new GameEvent { id = EventIds.FirstPopup });

            long total = loop.TotalAssets;
            d.stats.maxAssets = total;
            d.stats.minAssets = total;
            loop.Snapshot();
            return loop;
        }

        public static Loop FromSave(SaveData data, IPriceModel priceModel = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Migrate(data);
            var loop = new Loop(data, new Xoshiro128(ToUInts(data.marketRngState)), new Xoshiro128(ToUInts(data.rngState)), new Xoshiro128(ToUInts(data.subTickRngState)), new Xoshiro128(ToUInts(data.luckRngState)));
            if (priceModel != null) loop.PriceModel = priceModel;
            return loop;
        }

        /// <summary>Writes the RNG states into Data and returns it, ready to serialize.</summary>
        public SaveData Snapshot()
        {
            Data.marketRngState = ToInts(_market.GetState());
            Data.rngState = ToInts(_rng.GetState());
            Data.subTickRngState = ToInts(_subTick.GetState());
            Data.luckRngState = ToInts(_luck.GetState());
            return Data;
        }

        /// <summary>v1 -> v2 (DESIGN.md E10): the luck stream comes from the seed; appearance/wardrobe defaults are Core/LoopStyle.</summary>
        static void Migrate(SaveData d)
        {
            if (d.version >= SaveData.CurrentVersion) return;
            if (d.luckRngState == null || d.luckRngState.Length != 4 || (d.luckRngState[0] | d.luckRngState[1] | d.luckRngState[2] | d.luckRngState[3]) == 0)
                d.luckRngState = ToInts(new Xoshiro128((ulong)d.seed ^ LuckSalt).GetState());
            MigrateStyle(d);
            d.version = SaveData.CurrentVersion;
        }

        static partial void MigrateStyle(SaveData d);

        static uint[] ToUInts(int[] a) => new[] { unchecked((uint)a[0]), unchecked((uint)a[1]), unchecked((uint)a[2]), unchecked((uint)a[3]) };
        static int[] ToInts(uint[] a) => new[] { unchecked((int)a[0]), unchecked((int)a[1]), unchecked((int)a[2]), unchecked((int)a[3]) };

        // ---- derived ----

        public int AbsSlot => Calendar.AbsoluteSlot(Data.day, Data.slot);
        public bool Finished => Data.finished;
        /// <summary>Days left in the current 30-day stretch (Day 1..30 identical to the old 30-day loop; then 60, 90...).</summary>
        public int DaysLeft => Rules.Days - ((Data.day - 1) % Rules.Days + 1);

        /// <summary>Month-end: the settlement pause (Day 30, 60, 90...) after the night slot.</summary>
        bool IsMonthEnd => Data.slot == Slot.Night && Data.day % Rules.Days == 0;

        long PendingTransferSum()
        {
            long sum = 0;
            foreach (var t in Data.pendingTransfers) sum += t.amount;
            return sum;
        }

        /// <summary>Bank + domestic + overseas + in-transit (excludes position — used for room tier, B5 §1).</summary>
        public long TotalCashLikeForRoom => Data.bankCash + Data.domesticCash + Data.overseasCash + PendingTransferSum();

        public long TotalAssets => TotalCashLikeForRoom + PositionValue;

        // ---- actions ----

        public Availability Check(IAction action)
        {
            if (Data.finished) return Availability.No("");
            if (Data.atSea) return Availability.No(Texts.AtSea);
            if (ForcedVoyageDue) return Availability.No(Texts.VoyageForced);
            if (!action.Slots.Has(Data.slot)) return Availability.No("");
            if (action.EndsSlot && !(action is RestAction) && Data.body <= 0) return Availability.No(Texts.BodyZero);
            return action.Check(this);
        }

        public ActionResult Do(IAction action)
        {
            var w = BeginWatchableAction(action);
            if (w.Walk != null) FinishWatch(w.Walk);
            AutoDepartIfForced();
            return w.Result;
        }

        /// <summary>End the slot doing nothing (rest by day, sleep at night).</summary>
        public ActionResult Pass() => Do(RestAction.Instance);

        // ---- time / watchable slot advance (B5a: the watch scene can pause between BeginWatchableAction and FinishWatch) ----

        public readonly struct WatchableAction
        {
            public readonly ActionResult Result;
            /// <summary>Non-null only when the action ended the slot and a position may be at risk this slot. Caller must call FinishWatch(Walk) exactly once.</summary>
            public readonly SubTickWalk Walk;
            internal WatchableAction(ActionResult result, SubTickWalk walk) { Result = result; Walk = walk; }
        }

        /// <summary>
        /// Runs Check/Execute for `action` (same order as Do). If it ends the slot, advances the clock and prices
        /// the slot's move, but stops short of applying the outcome — the caller (Game, for a live watch scene) may
        /// drive the returned walk's Step()/AddMargin()/StopLoss() before calling FinishWatch. Headless callers
        /// (Do(), sims, tests) never see a Walk: Do() always finishes it immediately with zero intervention.
        /// </summary>
        public WatchableAction BeginWatchableAction(IAction action)
        {
            var ok = Check(action);
            if (!ok.Ok) return new WatchableAction(ActionResult.Refused(ok.Reason), null);
            var result = action.Execute(this);
            if (!result.Ok) return new WatchableAction(result, null);
            if (!string.IsNullOrEmpty(result.Message)) Log(result.Message);
            if (!action.EndsSlot)
            {
                Snapshot();
                return new WatchableAction(result, null);
            }
            bool sleeping = action is RestAction && Data.slot == Slot.Night;   // E3: no watch scene, so no luck offer
            if (Data.slot == Slot.Night && !(action is RestAction))
            {
                AddMental(-Rules.AllNighterMental); // all-nighter: no recovery, mental hit
                Log(Texts.AllNighter);
            }

            Data.stats.slotsPlayed++;
            OnSlotEnd();
            if (IsMonthEnd)
            {
                Finish();
                Snapshot();
                return new WatchableAction(result, null);
            }
            return AdvanceAfterSlotEnd(result, sleeping);
        }

        /// <summary>The second half of a slot end: clock, price walk, next slot's start. Shared with ContinueLife (the month-end
        /// slot already ran its OnSlotEnd before the settlement pause).</summary>
        WatchableAction AdvanceAfterSlotEnd(ActionResult result, bool sleeping = false)
        {
            AdvanceClockOnly();
            var walk = BeginPriceWalk(sleeping);
            if (!walk.WatchTriggered)
            {
                walk.RunToEnd();
                FinishPriceWalk(walk);
                OnSlotStart();
                TrackAssets();
                ContinueCascadeAndDeferredEvents();
                Snapshot();
                return new WatchableAction(result, null);
            }
            return new WatchableAction(result, walk);
        }

        /// <summary>Completes a walk returned by BeginWatchableAction (auto-running any remaining sub-ticks) and finishes the slot advance.</summary>
        public void FinishWatch(SubTickWalk walk)
        {
            if (walk == null) return;
            if (!walk.Done) walk.RunToEnd();
            FinishPriceWalk(walk);
            OnSlotStart();
            TrackAssets();
            ContinueCascadeAndDeferredEvents();
            Snapshot();
        }

        void ContinueCascadeAndDeferredEvents()
        {
            while (AbsSlot < Data.lostUntilSlot && !ForcedVoyageDue)
            {
                // "can't get up": the slot passes as rest.
                RestAction.Instance.Execute(this);
                Data.stats.slotsPlayed++;
                OnSlotEnd();
                if (IsMonthEnd)
                {
                    Finish();
                    return;
                }
                MoveToNextSlot();
                TrackAssets();
            }
            if (Data.e3AtSlot >= 0 && AbsSlot >= Data.e3AtSlot)
            {
                Data.e3AtSlot = -1;
                Emit(new GameEvent { id = EventIds.SelfPity, count = Rng.NextInt(Texts.E3Monologues.Length) });
            }
        }

        void AdvanceClockOnly()
        {
            if (Data.slot == Slot.Night)
            {
                Data.day++;
                Data.slot = Slot.Dawn;
            }
            else
            {
                Data.slot = Data.slot + 1;
            }
        }

        void MoveToNextSlot()
        {
            AdvanceClockOnly();
            var walk = BeginPriceWalk(true);   // a lost ("can't get up") slot is asleep: no watch, no luck
            walk.RunToEnd();
            FinishPriceWalk(walk);
            OnSlotStart();
        }

        /// <summary>Draws the slot's close price (market stream, unchanged) and, if a position is open, decomposes
        /// it into Rules.SubTicksPerSlot sub-tick prices (sub-tick stream) for tail-risk liquidation. Doesn't commit
        /// anything to Data yet — see SubTickWalk / FinishPriceWalk.</summary>
        SubTickWalk BeginPriceWalk(bool asleep = false)
        {
            long oldPrice = Data.price;
            long newPrice = PriceModel.Next(oldPrice, _market);
            long[] prices = Data.hasPosition ? SubTickModel.Build(oldPrice, newPrice, _subTick) : new[] { newPrice };
            return new SubTickWalk(this, prices, Data.hasPosition ? Data.position : null, asleep);
        }

        /// <summary>Commits the walk's final price (always the slot's true close, regardless of how the position's
        /// story played out), then applies liquidation bookkeeping and always sweeps the ghost list (OnTick).</summary>
        void FinishPriceWalk(SubTickWalk walk)
        {
            var prices = walk.Prices;
            Data.price = prices[prices.Count - 1];
            Data.priceHistory.Add(Data.price);
            if (Data.priceHistory.Count > Rules.PriceHistoryLength) Data.priceHistory.RemoveAt(0);
            if (walk.WatchTriggered && !Data.atSea) Data.stats.watchesShown++;
            if (!walk.WasClosedEarly && walk.WasLiquidated) LiquidatePosition(walk.WasTailLiquidation);
            OnTick();
        }

        void OnSlotStart()
        {
            ResolvePendingTransfers();
            switch (Data.slot)
            {
                case Slot.Dawn:
                    OnNewDay();
                    OnDawnLife();
                    var room = Calendar.RoomFor(TotalCashLikeForRoom);
                    if (room != Data.room && Data.day >= Data.roomRecheckDay)
                    {
                        Emit(new GameEvent { id = EventIds.MoveRoom, ints = new[] { (int)Data.room, (int)room } });
                        Log(Texts.E6Move(Data.room, room));
                        Data.room = room;
                    }
                    if (Data.mental < Rules.CantGetUpBelow)
                    {
                        Data.lostUntilSlot = Math.Max(Data.lostUntilSlot, Calendar.AbsoluteSlot(Data.day, Slot.Afternoon));
                        Emit(new GameEvent { id = EventIds.CantGetUp });
                        Log(Texts.CantGetUp);
                    }
                    break;
                case Slot.Evening:
                    if (Data.bankCash >= Rules.FoodPerDay)
                    {
                        PayFixedFromBank(Rules.FoodPerDay);
                        Data.stats.foodPaid++;
                    }
                    else if (!StarvationEnabled)
                    {
                        PayFixedFromBank(Rules.FoodPerDay);   // measurement control only: the pre-hunger behaviour (pays what is there)
                        Data.stats.foodPaid++;
                    }
                    else // D3 (2026-09-30): can't pay for food -> go hungry (separate from bankruptcy)
                    {
                        AddBody(-Rules.StarveBody);
                        AddMental(-Rules.StarveMental);
                        Data.stats.starved++;
                        Data.starveStreak = Data.lastStarveDay == Data.day - 1 && Data.starveStreak > 0 ? Data.starveStreak + 1 : 1;
                        Data.lastStarveDay = Data.day;
                        long onExchange = Data.domesticCash + Data.overseasCash + PendingTransferSum();   // shown, never withdrawn for the player
                        if (Data.starveStreak == Rules.StarvePopupStreak)
                            Emit(new GameEvent { id = EventIds.Hunger, amount = onExchange, count = Data.starveStreak });
                        else
                            Log(onExchange >= Rules.FoodPerDay ? Texts.Starved + " " + Texts.StarvedExchange(onExchange) : Texts.Starved);
                    }
                    break;
            }
        }

        void Finish()
        {
            Data.finished = true;
            TrackAssets();
            Emit(new GameEvent { id = EventIds.Settlement, amount = TotalAssets, count = Data.day / Rules.Days });
        }

        void TrackAssets()
        {
            long total = TotalAssets;
            if (total > Data.stats.maxAssets) Data.stats.maxAssets = total;
            if (total < Data.stats.minAssets) Data.stats.minAssets = total;
        }

        // Hooks filled in by the coin / lotto parts of this partial class.
        partial void OnTick();
        /// <summary>Sim-only switch for the D-median control (report-only). Not saved; the game always has hunger on.</summary>
        public bool StarvationEnabled { get; set; } = true;

        partial void OnNewDay();
        partial void OnSlotEnd();

        // ---- mutation helpers used by actions ----

        public void AddBody(int delta) => Data.body = Math.Min(Data.bodyMax, ClampGauge(Data.body + delta));
        public void AddMental(int delta) => Data.mental = ClampGauge(Data.mental + delta);
        static int ClampGauge(int v) => v < Rules.GaugeMin ? Rules.GaugeMin : v > Rules.GaugeMax ? Rules.GaugeMax : v;

        public void AddBank(long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Data.bankCash += amount;
        }

        /// <summary>Takes exactly this much; callers check affordability first.</summary>
        public void SpendBank(long amount)
        {
            if (amount < 0 || amount > Data.bankCash) throw new InvalidOperationException("can't spend " + amount + " of " + Data.bankCash);
            Data.bankCash -= amount;
        }

        /// <summary>Fixed costs take what's there — cash never goes below 0 (no debt in this prototype).</summary>
        void PayFixedFromBank(long amount) => Data.bankCash -= Math.Min(Data.bankCash, amount);

        public void AddOverseas(long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Data.overseasCash += amount;
        }

        /// <summary>Takes exactly this much; callers check affordability first.</summary>
        public void SpendOverseas(long amount)
        {
            if (amount < 0 || amount > Data.overseasCash) throw new InvalidOperationException("can't spend " + amount + " of " + Data.overseasCash);
            Data.overseasCash -= amount;
        }

        public void Emit(GameEvent e)
        {
            Data.pendingEvents.Add(e);
            if (Array.IndexOf(EventIds.Codex, e.id) >= 0 && !Data.stats.eventsSeen.Contains(e.id)) Data.stats.eventsSeen.Add(e.id);
        }

        public bool HasEvent => Data.pendingEvents.Count > 0;

        public GameEvent PopEvent()
        {
            if (Data.pendingEvents.Count == 0) return null;
            var e = Data.pendingEvents[0];
            Data.pendingEvents.RemoveAt(0);
            return e;
        }

        public const int LogLimit = 200;

        public void Log(string line)
        {
            Data.log.Add(Texts.DayLabel(Data.day) + " " + Texts.SlotNames[(int)Data.slot] + " · " + line);
            if (Data.log.Count > LogLimit) Data.log.RemoveAt(0);
        }
    }
}
