using System;
using AllIn.Core;

namespace AllIn.Holdem.Brain
{
    /// <summary>Knobs per personality (DESIGN.md C4). Personalities are never shown — players learn them by watching.</summary>
    public sealed class Personality
    {
        public PersonalityId Id;
        public double RangeMultiplier = 1.0;
        public double BluffChance = 0.10;
        public double CallMargin;           // call if equity ≥ pot odds + margin
        public double RaiseThresholdAdd;    // added to the +0.20 raise threshold
        public double BetSizeMultiplier = 1.0;
        public bool ExploitsStations;       // value-bets thinner (−0.05) when a Station is in the pot
        public bool ExactPushFold;          // plays the push/fold table unscaled

        public static Personality For(PersonalityId id)
        {
            switch (id)
            {
                case PersonalityId.Tight: return new Personality { Id = id, RangeMultiplier = 0.7, BluffChance = 0.03, CallMargin = 0.08 };
                case PersonalityId.Station: return new Personality { Id = id, RangeMultiplier = 1.2, BluffChance = 0.05, CallMargin = -0.12, RaiseThresholdAdd = 0.15 };
                case PersonalityId.Maniac: return new Personality { Id = id, RangeMultiplier = 1.6, BluffChance = 0.30, CallMargin = -0.05, BetSizeMultiplier = 1.2 };
                default: return new Personality { Id = PersonalityId.Pro, BluffChance = 0.10, ExploitsStations = true, ExactPushFold = true };
            }
        }
    }

    public sealed class BotFactory : IDeciderFactory
    {
        public static readonly BotFactory Instance = new BotFactory();
        public IDecider Create(PersonalityId personality) => new Bot(Personality.For(personality));
    }

    /// <summary>
    /// Table-driven AI (DESIGN.md C4). Preflop: push/fold at ≤ 15bb effective, otherwise open-raise / 3-bet / fold
    /// ranges. Postflop: Monte Carlo equity (400 runs, once per street) against pot odds.
    /// </summary>
    public sealed class Bot : IDecider
    {
        public const int EquitySamples = 400;
        public const double PushFoldAtBb = 15;
        public static readonly double[] OpenPercent = { 12, 15, 25, 42, 36, 0 }; // UTG, MP, CO, BTN, SB, (BB)
        public const double ThreeBetPercent = 5, FlatPercent = 8, FourBetPercent = 4;
        public const double OpenSizeBb = 2.5, ThreeBetSize = 3.0, IsoRaiseBb = 3.5, BbRaiseLimpersPercent = 15;
        public const double ValueBetEquity = 0.62, CheckEquity = 0.45, RaiseOverOdds = 0.20, ExploitValueShift = 0.05;
        public const double ValueBetPot = 2.0 / 3, BluffBetPot = 0.5, RaiseMultiple = 2.5;

        public readonly Personality P;
        int _cachedHand = -1;
        Street _cachedStreet;
        double _cachedEquity;

        public Bot(Personality p) { P = p; }

        public PlayerAction Decide(Tournament t, int seat)
        {
            var h = t.Hand;
            return h.Street == Street.Preflop ? Preflop(h, seat) : Postflop(t, h, seat);
        }

        // ---------- preflop ----------

        PlayerAction Preflop(HandState h, int s)
        {
            long bb = h.BigBlind;
            var legal = h.LegalFor(s);
            double pct = HandRanks.Percentile(h.Hole0[s], h.Hole1[s]);
            var positions = Positions.Assign(h);
            var pos = positions[s];
            double eff = EffectiveBb(h, s);
            double mult = P.RangeMultiplier;

            if (eff <= PushFoldAtBb)
            {
                double pfMult = P.ExactPushFold ? 1.0 : mult;
                if (h.RaisesThisStreet == 0)
                {
                    if (pos == TablePosition.BB) return legal.CanCheck ? PlayerAction.Check : PlayerAction.Fold;
                    return pct <= RangeMath.Scaled(PushFoldTable.PushPercent(pos, eff), pfMult) ? PlayerAction.AllIn : FoldOrCheck(legal);
                }
                var shover = h.LastRaiser >= 0 ? positions[h.LastRaiser] : TablePosition.SB;
                return pct <= RangeMath.Scaled(PushFoldTable.CallPercent(shover, eff), pfMult) ? PlayerAction.AllIn : FoldOrCheck(legal);
            }

            if (h.RaisesThisStreet == 0)
            {
                if (pos == TablePosition.BB) // limped to us
                    return pct <= RangeMath.Scaled(BbRaiseLimpersPercent, mult) ? Aggress(h, s, (long)(IsoRaiseBb * bb)) : FoldOrCheck(legal);
                return pct <= RangeMath.Scaled(OpenPercent[(int)pos], mult) ? Aggress(h, s, (long)(OpenSizeBb * bb)) : FoldOrCheck(legal);
            }
            if (h.RaisesThisStreet == 1)
            {
                if (pct <= RangeMath.Scaled(ThreeBetPercent, mult)) return Aggress(h, s, (long)(ThreeBetSize * h.CurrentBet));
                if (pct <= RangeMath.Scaled(ThreeBetPercent + FlatPercent, mult)) return PlayerAction.Call;
                return FoldOrCheck(legal);
            }
            return pct <= RangeMath.Scaled(FourBetPercent, mult) ? PlayerAction.AllIn : FoldOrCheck(legal);
        }

        /// <summary>min(my chips, biggest other stack in the hand) in big blinds, counting chips already bet this street.</summary>
        public static double EffectiveBb(HandState h, int s)
        {
            long mine = h.Stack[s] + h.StreetBet[s], maxOther = 0;
            for (int o = 0; o < HandState.Seats; o++)
                if (o != s && h.InHand[o] && !h.Folded[o]) maxOther = Math.Max(maxOther, h.Stack[o] + h.StreetBet[o]);
            return Math.Min(mine, maxOther) / (double)h.BigBlind;
        }

        static PlayerAction FoldOrCheck(Legal l) => l.CanCheck ? PlayerAction.Check : PlayerAction.Fold;

        /// <summary>Any bet or raise becomes a shove once the stack is at most twice the pot.</summary>
        static PlayerAction Aggress(HandState h, int s, long to)
        {
            if (h.Stack[s] <= 2 * h.PotTotal) return PlayerAction.AllIn;
            to = Math.Max(to, h.BigBlind);
            return h.CurrentBet == 0 ? PlayerAction.BetTo(to) : PlayerAction.RaiseTo(to);
        }

        // ---------- postflop ----------

        PlayerAction Postflop(Tournament t, HandState h, int s)
        {
            var legal = h.LegalFor(s);
            double eq = EquityFor(t, h, s);
            long pot = h.PotTotal;

            if (legal.ToCall == 0)
            {
                double valueAt = ValueBetEquity - (P.ExploitsStations && StationInPot(t, h, s) ? ExploitValueShift : 0);
                if (eq >= valueAt) return Aggress(h, s, (long)Math.Round(pot * ValueBetPot * P.BetSizeMultiplier));
                if (eq >= CheckEquity) return PlayerAction.Check;
                if (t.Rng.Chance(P.BluffChance)) return Aggress(h, s, (long)Math.Round(pot * BluffBetPot * P.BetSizeMultiplier));
                return PlayerAction.Check;
            }

            double potOdds = legal.ToCall / (double)(pot + legal.ToCall);
            if (eq >= potOdds + RaiseOverOdds + P.RaiseThresholdAdd && legal.CanRaise)
                return Aggress(h, s, (long)Math.Round(h.CurrentBet * RaiseMultiple));
            if (eq >= potOdds + P.CallMargin) return PlayerAction.Call;
            return PlayerAction.Fold;
        }

        double EquityFor(Tournament t, HandState h, int s)
        {
            if (_cachedHand == t.HandNumber && _cachedStreet == h.Street) return _cachedEquity;
            int opponents = h.PlayersInHand - 1;
            _cachedEquity = Equity.VsRandom(h.Hole0[s], h.Hole1[s], h.Board, h.BoardCount, opponents, t.Rng, EquitySamples);
            _cachedHand = t.HandNumber;
            _cachedStreet = h.Street;
            return _cachedEquity;
        }

        static bool StationInPot(Tournament t, HandState h, int s)
        {
            for (int o = 0; o < HandState.Seats; o++)
                if (o != s && h.InHand[o] && !h.Folded[o] && t.Seats[o] != null && t.Seats[o].Decider != null && t.Seats[o].Personality == PersonalityId.Station)
                    return true;
            return false;
        }
    }
}
