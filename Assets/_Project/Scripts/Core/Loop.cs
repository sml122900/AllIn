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

        public IPriceModel PriceModel { get; set; } = StudentTPriceModel.Instance;

        readonly Xoshiro128 _market;
        readonly Xoshiro128 _rng;

        Loop(SaveData data, Xoshiro128 market, Xoshiro128 rng)
        {
            Data = data;
            _market = market;
            _rng = rng;
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
            var loop = new Loop(d, new Xoshiro128((ulong)seed), new Xoshiro128((ulong)seed ^ 0x5EED5EED5EEDUL));
            if (priceModel != null) loop.PriceModel = priceModel;
            d.priceHistory.Add(d.price);

            loop.Emit(new GameEvent { id = EventIds.Opening });
            d.cash = Rules.StartingCash;
            loop.PayFixed(Rules.Rent);
            d.stats.rentPaid++;
            loop.Log(Texts.RentPaid);
            d.room = Calendar.RoomFor(d.cash);
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
            var loop = new Loop(data, new Xoshiro128(ToUInts(data.marketRngState)), new Xoshiro128(ToUInts(data.rngState)));
            if (priceModel != null) loop.PriceModel = priceModel;
            return loop;
        }

        /// <summary>Writes the RNG states into Data and returns it, ready to serialize.</summary>
        public SaveData Snapshot()
        {
            Data.marketRngState = ToInts(_market.GetState());
            Data.rngState = ToInts(_rng.GetState());
            return Data;
        }

        static uint[] ToUInts(int[] a) => new[] { unchecked((uint)a[0]), unchecked((uint)a[1]), unchecked((uint)a[2]), unchecked((uint)a[3]) };
        static int[] ToInts(uint[] a) => new[] { unchecked((int)a[0]), unchecked((int)a[1]), unchecked((int)a[2]), unchecked((int)a[3]) };

        // ---- derived ----

        public int AbsSlot => Calendar.AbsoluteSlot(Data.day, Data.slot);
        public bool Finished => Data.finished;
        public int DaysLeft => Math.Max(0, Rules.Days - Data.day);
        public long TotalAssets => Data.cash + PositionValue;

        // ---- actions ----

        public Availability Check(IAction action)
        {
            if (Data.finished) return Availability.No("");
            if (!action.Slots.Has(Data.slot)) return Availability.No("");
            if (action.EndsSlot && !(action is RestAction) && Data.body <= 0) return Availability.No(Texts.BodyZero);
            return action.Check(this);
        }

        public ActionResult Do(IAction action)
        {
            var ok = Check(action);
            if (!ok.Ok) return ActionResult.Refused(ok.Reason);
            var result = action.Execute(this);
            if (!result.Ok) return result;
            if (!string.IsNullOrEmpty(result.Message)) Log(result.Message);
            if (action.EndsSlot)
            {
                if (Data.slot == Slot.Night && !(action is RestAction))
                {
                    AddMental(-Rules.AllNighterMental); // all-nighter: no recovery, mental hit
                    Log(Texts.AllNighter);
                }
                EndSlotAndAdvance();
            }
            Snapshot();
            return result;
        }

        /// <summary>End the slot doing nothing (rest by day, sleep at night).</summary>
        public ActionResult Pass() => Do(RestAction.Instance);

        // ---- time ----

        void EndSlotAndAdvance()
        {
            while (true)
            {
                Data.stats.slotsPlayed++;
                OnSlotEnd();
                if (Data.day == Rules.Days && Data.slot == Slot.Night)
                {
                    Finish();
                    return;
                }
                MoveToNextSlot();
                TrackAssets();
                if (AbsSlot < Data.lostUntilSlot)
                {
                    // "can't get up": the slot passes as rest.
                    RestAction.Instance.Execute(this);
                    continue;
                }
                if (Data.e3AtSlot >= 0 && AbsSlot >= Data.e3AtSlot)
                {
                    Data.e3AtSlot = -1;
                    Emit(new GameEvent { id = EventIds.SelfPity, count = Rng.NextInt(Texts.E3Monologues.Length) });
                }
                return;
            }
        }

        void MoveToNextSlot()
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
            TickPrice();
            OnSlotStart();
        }

        void TickPrice()
        {
            Data.price = PriceModel.Next(Data.price, _market);
            Data.priceHistory.Add(Data.price);
            if (Data.priceHistory.Count > Rules.PriceHistoryLength) Data.priceHistory.RemoveAt(0);
            OnTick();
        }

        void OnSlotStart()
        {
            switch (Data.slot)
            {
                case Slot.Dawn:
                    OnNewDay();
                    var room = Calendar.RoomFor(Data.cash);
                    if (room != Data.room)
                    {
                        Emit(new GameEvent { id = EventIds.MoveRoom, ints = new[] { (int)Data.room, (int)room } });
                        Log(Texts.E6Move(Data.room, room));
                        Data.room = room;
                    }
                    if (Data.mental < Rules.CantGetUpBelow)
                    {
                        Data.lostUntilSlot = Calendar.AbsoluteSlot(Data.day, Slot.Afternoon);
                        Emit(new GameEvent { id = EventIds.CantGetUp });
                        Log(Texts.CantGetUp);
                    }
                    break;
                case Slot.Evening:
                    PayFixed(Rules.FoodPerDay);
                    Data.stats.foodPaid++;
                    break;
            }
        }

        void Finish()
        {
            Data.finished = true;
            TrackAssets();
            Emit(new GameEvent { id = EventIds.Settlement, amount = TotalAssets });
        }

        void TrackAssets()
        {
            long total = TotalAssets;
            if (total > Data.stats.maxAssets) Data.stats.maxAssets = total;
            if (total < Data.stats.minAssets) Data.stats.minAssets = total;
        }

        // Hooks filled in by the coin / lotto parts of this partial class.
        partial void OnTick();
        partial void OnNewDay();
        partial void OnSlotEnd();

        // ---- mutation helpers used by actions ----

        public void AddBody(int delta) => Data.body = ClampGauge(Data.body + delta);
        public void AddMental(int delta) => Data.mental = ClampGauge(Data.mental + delta);
        static int ClampGauge(int v) => v < Rules.GaugeMin ? Rules.GaugeMin : v > Rules.GaugeMax ? Rules.GaugeMax : v;

        public void AddCash(long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Data.cash += amount;
        }

        /// <summary>Takes exactly this much; callers check affordability first.</summary>
        public void SpendCash(long amount)
        {
            if (amount < 0 || amount > Data.cash) throw new InvalidOperationException("can't spend " + amount + " of " + Data.cash);
            Data.cash -= amount;
        }

        /// <summary>Fixed costs take what's there — cash never goes below 0 (no debt in this prototype).</summary>
        void PayFixed(long amount) => Data.cash -= Math.Min(Data.cash, amount);

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
