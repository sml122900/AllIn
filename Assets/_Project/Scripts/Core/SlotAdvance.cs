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

        internal SubTickWalk(Loop loop, long[] prices, Position position)
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
        }

        public IReadOnlyList<long> Prices => _prices;
        public int Index => _index;
        public bool Done => _index >= _prices.Length || _liquidated || _closedEarly;
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
            if (Done) return;
            long price = _prices[_index];
            _loop.Data.price = price;
            if (_position != null && CoinRules.IsLiquidated(_position, price))
            {
                _liquidated = true;
                _tailLiquidation = _index < _prices.Length - 1; // liquidated before the slot's close sub-tick
            }
            _index++;
        }

        /// <summary>Finishes every remaining sub-tick with no further intervention — the deterministic default.</summary>
        public void RunToEnd()
        {
            while (!Done) Step();
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
