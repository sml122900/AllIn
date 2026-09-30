using System.Collections.Generic;

namespace AllIn.Core
{
    /// <summary>
    /// B5a "관전" (watch): a slot's pre-generated sub-tick price path, steppable so the Game layer can animate it
    /// in real time and let the player intervene ([증거금 추가] / [손절]) between steps. Headless callers (Do(),
    /// sims, tests) never step it themselves — Loop.FinishWatch calls RunToEnd(), which is the zero-intervention
    /// ("기도"-only) baseline: same seed, same result, regardless of how many times Pray() was called.
    ///
    /// `position` is the SaveData.position reference this walk is tracking, or null when no position is open (in
    /// which case the walk is a trivial single-step pass-through — nothing to liquidate or watch).
    /// </summary>
    public sealed class SubTickWalk
    {
        readonly Loop _loop;
        readonly long[] _prices;
        readonly Position _position;

        int _index;
        bool _liquidated;
        bool _tailLiquidation;
        bool _closedEarly;
        readonly bool _watchTriggered;
        readonly int _watchTriggerIndex;
        readonly bool _luckTriggered;   // E1/E3: rolled once, when the watch scene starts (never while asleep / at sea / q = 0)
        bool _luckOffered, _luckUsed;

        internal SubTickWalk(Loop loop, long[] prices, Position position, bool asleep = false)
        {
            _loop = loop;
            _prices = prices;
            _position = position;

            _watchTriggerIndex = -1;
            if (_position != null)
            {
                for (int i = 0; i < _prices.Length; i++)
                {
                    if (!CoinRules.IsAtWatchThreshold(_position, _prices[i])) continue;
                    _watchTriggered = true;
                    _watchTriggerIndex = i;
                    break;
                }
            }
            // The roll depends on nothing but the luck stream: not on prayers, mental, the price path or the position's size.
            if (_watchTriggered && !asleep && !loop.Data.atSea && loop.LuckQCoin > 0)
                _luckTriggered = loop.LuckRng.Chance(loop.LuckQCoin);
            if (_luckTriggered) _prices = (long[])prices.Clone();   // UseLuck rewrites the tail; a shared (scripted) array must not change
        }

        public IReadOnlyList<long> Prices => _prices;
        public int Index => _index;
        public bool Done => !LuckPending && (_index >= _prices.Length || _liquidated || _closedEarly);

        /// <summary>E3: "무조건 승리" was rolled for this watch scene (decided when the walk was created).</summary>
        public bool LuckTriggered => _luckTriggered;
        public bool LuckUsed => _luckUsed;
        /// <summary>The button is up: the walk reached the trigger sub-tick and waits for UseLuck() (Step does nothing until then).</summary>
        public bool LuckPending => _luckOffered && !_luckUsed && !_liquidated && !_closedEarly;
        public bool WatchTriggered => _watchTriggered;
        public int WatchTriggerIndex => _watchTriggerIndex;

        internal bool WasLiquidated => _liquidated;
        internal bool WasTailLiquidation => _tailLiquidation;
        internal bool WasClosedEarly => _closedEarly;

        /// <summary>Current liquidation price given the position's live (possibly margin-added) state. Null with no position.</summary>
        public long? CurrentLiquidationPrice => _position == null ? (long?)null : (long)System.Math.Round(CoinRules.LiquidationPrice(_position));

        /// <summary>Applies the next sub-tick's price. No-op once Done.</summary>
        public void Step()
        {
            if (Done || LuckPending) return;
            long price = _prices[_index];
            _loop.Data.price = price;
            // The luck offer stands at the watch trigger sub-tick; liquidation on that very sub-tick waits for the player's press.
            bool offerHere = _luckTriggered && !_luckOffered && _index == _watchTriggerIndex;
            if (offerHere) _luckOffered = true;
            else if (!_luckUsed && _position != null && CoinRules.IsLiquidated(_position, price))
            {
                _liquidated = true;
                // "꼬리 청산": liquidated mid-path, but the slot's close sub-tick (at the CURRENT — possibly
                // margin-added — liquidation price) would not have triggered it on its own. Not just "before index 11".
                long closePrice = _prices[_prices.Length - 1];
                _tailLiquidation = !CoinRules.IsLiquidated(_position, closePrice);
            }
            _index++;
            // the rebound path ends the position by itself at its last sub-tick
            if (_luckUsed && _index >= _prices.Length && !_closedEarly && _position != null)
            {
                _loop.ClosePositionAt(price);
                _closedEarly = true;
            }
        }

        /// <summary>Finishes every remaining sub-tick with no further intervention — the deterministic default.
        /// A pending luck offer counts as pressed (headless callers; the UI gates it before calling this).</summary>
        public void RunToEnd()
        {
            while (!Done)
            {
                if (LuckPending) UseLuck(); else Step();
            }
        }

        /// <summary>E3 [무조건 승리]: swaps the remaining sub-ticks for a monotone rebound to the price where the position's
        /// profit equals its total margin (ROE +100%), and closes there at the end of the path (exit fee applies).
        /// No liquidation is judged afterwards. Only callable while LuckPending.</summary>
        public bool UseLuck()
        {
            if (!LuckPending || _position == null) return false;
            var p = _position;
            double move = CoinRules.TotalMargin(p) * Rules.LuckRoeTarget / CoinRules.Quantity(p);
            long target = p.direction == Direction.Long
                ? (long)System.Math.Ceiling(p.entryPrice + move)
                : (long)System.Math.Floor(p.entryPrice - move);
            long start = _loop.Data.price;
            int n = _prices.Length - _index;
            _luckUsed = true;
            _loop.Data.stats.luckUses++;
            if (n <= 0)
            {
                _prices[_prices.Length - 1] = target;   // the trigger was the slot's last sub-tick: it becomes the close
                _loop.Data.price = target;
                _loop.ClosePositionAt(target);
                _closedEarly = true;
                return true;
            }
            for (int k = 0; k < n; k++) _prices[_index + k] = start + (target - start) * (k + 1) / n;
            return true;
        }

        /// <summary>[기도] — zero mechanical effect, ever. Only moves the "겪었다" counter.</summary>
        public void Pray() => _loop.Data.stats.prayers++;

        /// <summary>[증거금 추가]: adds to the position's margin cushion from the overseas wallet. Notional/quantity
        /// stay fixed — only the liquidation price (checked on the remaining sub-ticks) moves.</summary>
        public void AddMargin(long amount)
        {
            if (Done || _position == null || amount <= 0) return;
            var d = _loop.Data;
            amount = System.Math.Min(amount, d.overseasCash);
            if (amount <= 0) return;
            d.overseasCash -= amount;
            _position.addedMargin += amount;
        }

        /// <summary>[손절]: closes the position at the current sub-tick's price, right now.</summary>
        public void StopLoss()
        {
            if (Done || _position == null) return;
            _loop.ClosePositionAt(_loop.Data.price);
            _closedEarly = true;
        }
    }
}
