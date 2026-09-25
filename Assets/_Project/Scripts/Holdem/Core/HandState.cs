using System;
using System.Collections.Generic;
using AllIn.Core;

namespace AllIn.Holdem
{
    /// <summary>
    /// One no-limit hand at a 9-seat table (DESIGN.md C1): blinds, action order, min-raise, the
    /// "short all-in doesn't reopen" rule, side pots, showdown order. Seats with a 0 stack are empty.
    /// Deterministic: the only randomness is the deck shuffle from the injected RNG.
    /// </summary>
    public sealed class HandState
    {
        public const int Seats = 9;

        public readonly bool[] InHand = new bool[Seats];
        public readonly long[] Stack = new long[Seats];      // chips behind
        public readonly long[] StartStack = new long[Seats];
        public readonly long[] StreetBet = new long[Seats];
        public readonly long[] Committed = new long[Seats];  // whole hand
        public readonly bool[] Folded = new bool[Seats];
        public readonly bool[] IsAllIn = new bool[Seats];
        public readonly bool[] HasActed = new bool[Seats];
        public readonly int[] Hole0 = new int[Seats];
        public readonly int[] Hole1 = new int[Seats];
        public readonly int[] Board = new int[5];
        public readonly long[] Won = new long[Seats];
        public readonly int[] Scores = new int[Seats];
        public readonly List<Pot> Pots = new List<Pot>();
        public readonly List<int> ShowOrder = new List<int>();
        public readonly List<HoldemEvent> Events = new List<HoldemEvent>();

        readonly int[] _actedAtRaise = new int[Seats];
        readonly Deck _deck;
        int _fullRaiseId;

        public int BoardCount { get; private set; }
        public Street Street { get; private set; }
        public int Button { get; }
        public int SmallBlindSeat { get; }
        public int BigBlindSeat { get; }
        public long SmallBlind { get; }
        public long BigBlind { get; }
        public long CurrentBet { get; private set; }
        public long LastRaiseSize { get; private set; }
        public int ToAct { get; private set; } = -1;
        public int LastAggressor { get; private set; } = -1;   // this street
        public int RaisesThisStreet { get; private set; }       // bets + raises (blinds don't count)
        public int LastRaiser { get; private set; } = -1;       // this street, full or short
        public bool IsOver { get; private set; }
        public bool WentToShowdown { get; private set; }
        /// <summary>Board size when betting closed with several players left and at most one able to act (all-in runout).</summary>
        public int RunoutFromBoardCount { get; private set; } = -1;

        public sealed class Pot
        {
            public long Amount;
            public readonly List<int> Eligible = new List<int>();
            public readonly List<int> Winners = new List<int>();
        }

        /// <param name="stacks">Chips per seat; 0 = empty seat.</param>
        public HandState(long[] stacks, int button, long smallBlind, long bigBlind, IRng rng)
        {
            if (stacks.Length != Seats) throw new ArgumentException("9 seats");
            int players = 0;
            for (int i = 0; i < Seats; i++)
            {
                Stack[i] = StartStack[i] = stacks[i];
                InHand[i] = stacks[i] > 0;
                if (InHand[i]) players++;
            }
            if (players < 2) throw new InvalidOperationException("need two players");
            if (!InHand[button]) throw new ArgumentException("button seat is empty");

            Button = button;
            SmallBlind = smallBlind;
            BigBlind = bigBlind;
            // Heads-up: the button posts the small blind and acts first preflop.
            SmallBlindSeat = players == 2 ? button : NextInHand(button);
            BigBlindSeat = NextInHand(SmallBlindSeat);

            _deck = new Deck(rng);
            Post(SmallBlindSeat, smallBlind);
            Post(BigBlindSeat, bigBlind);
            CurrentBet = bigBlind;
            LastRaiseSize = bigBlind;

            // deal two rounds starting left of the button
            for (int round = 0; round < 2; round++)
            for (int k = 1; k <= Seats; k++)
            {
                int s = (button + k) % Seats;
                if (!InHand[s]) continue;
                if (round == 0) Hole0[s] = _deck.Deal();
                else Hole1[s] = _deck.Deal();
            }
            for (int s = 0; s < Seats; s++)
                if (InHand[s]) Events.Add(new HoldemEvent { Kind = EventKind.HoleCards, Seat = s, Cards = new[] { Hole0[s], Hole1[s] } });

            ToAct = FindNextToAct(BigBlindSeat);
            if (ToAct < 0) CloseStreet();
        }

        void Post(int seat, long blind)
        {
            long amount = Math.Min(blind, Stack[seat]);
            Put(seat, amount);
            Events.Add(new HoldemEvent { Kind = EventKind.PostBlind, Seat = seat, Amount = amount });
        }

        // ---------- test rigging ----------

        /// <summary>Gives a seat specific hole cards (e.g. "As Kd"), swapping them out of wherever they were.</summary>
        internal void RigHole(int seat, string cards)
        {
            var c = Card.ParseMany(cards);
            RigCard(ref Hole0[seat], c[0]);
            RigCard(ref Hole1[seat], c[1]);
        }

        void RigCard(ref int slot, int wanted)
        {
            int old = slot;
            if (old == wanted) return;
            for (int s = 0; s < Seats; s++)
            {
                if (Hole0[s] == wanted && InHand[s]) Hole0[s] = old;
                if (Hole1[s] == wanted && InHand[s]) Hole1[s] = old;
            }
            _deck.Swap(old, wanted);
            slot = wanted;
        }

        /// <summary>Fixes the coming board cards (e.g. "2c 7d 9h Ts Js").</summary>
        internal void RigBoard(string cards) => _deck.PlaceNext(Card.ParseMany(cards));

        // ---------- queries ----------

        public int NextInHand(int seat)
        {
            for (int k = 1; k <= Seats; k++)
            {
                int s = (seat + k) % Seats;
                if (InHand[s]) return s;
            }
            return seat;
        }

        public bool CanAct(int s) => InHand[s] && !Folded[s] && !IsAllIn[s];

        bool NeedsToAct(int s) => CanAct(s) && (!HasActed[s] || StreetBet[s] < CurrentBet);

        public int PlayersInHand
        {
            get { int n = 0; for (int s = 0; s < Seats; s++) if (InHand[s] && !Folded[s]) n++; return n; }
        }

        public int PlayersAbleToAct
        {
            get { int n = 0; for (int s = 0; s < Seats; s++) if (CanAct(s)) n++; return n; }
        }

        public long PotTotal
        {
            get { long t = 0; for (int s = 0; s < Seats; s++) t += Committed[s]; return t; }
        }

        public int[] BoardCards
        {
            get { var b = new int[BoardCount]; Array.Copy(Board, b, BoardCount); return b; }
        }

        public Legal LegalFor(int s)
        {
            long toCall = Math.Min(CurrentBet - StreetBet[s], Stack[s]);
            bool reopened = !HasActed[s] || _actedAtRaise[s] < _fullRaiseId;
            bool canRaise = reopened && Stack[s] > CurrentBet - StreetBet[s];
            long minTo = CurrentBet == 0 ? BigBlind : CurrentBet + LastRaiseSize;
            long maxTo = StreetBet[s] + Stack[s];
            return new Legal(toCall, StreetBet[s] == CurrentBet, canRaise, Math.Min(minTo, maxTo), maxTo, CurrentBet == 0);
        }

        /// <summary>Clamps a wish into something legal (used by bots and the UI's sizing controls).</summary>
        public PlayerAction Normalize(int s, PlayerAction a)
        {
            var l = LegalFor(s);
            switch (a.Type)
            {
                case ActionType.Fold:
                    return l.CanCheck ? PlayerAction.Check : PlayerAction.Fold;
                case ActionType.Check:
                    return l.CanCheck ? PlayerAction.Check : PlayerAction.Fold;
                case ActionType.Call:
                    return l.CanCheck ? PlayerAction.Check : PlayerAction.Call;
                case ActionType.AllIn:
                    if (!l.CanRaise) return l.CanCheck ? PlayerAction.Check : PlayerAction.Call;
                    return PlayerAction.AllIn;
                default:
                    if (!l.CanRaise) return l.CanCheck ? PlayerAction.Check : PlayerAction.Call;
                    long to = Math.Max(a.To, l.MinRaiseTo);
                    if (to >= l.MaxTo) return PlayerAction.AllIn;
                    return l.IsBet ? PlayerAction.BetTo(to) : PlayerAction.RaiseTo(to);
            }
        }

        // ---------- acting ----------

        public void Apply(PlayerAction a)
        {
            if (IsOver) throw new InvalidOperationException("hand is over");
            int s = ToAct;
            var l = LegalFor(s);
            long before = StreetBet[s];
            switch (a.Type)
            {
                case ActionType.Fold:
                    Folded[s] = true;
                    break;
                case ActionType.Check:
                    if (!l.CanCheck) throw new InvalidOperationException("can't check facing " + CurrentBet);
                    break;
                case ActionType.Call:
                    if (l.ToCall <= 0) throw new InvalidOperationException("nothing to call");
                    Put(s, l.ToCall);
                    break;
                case ActionType.Bet:
                case ActionType.Raise:
                    if (!l.CanRaise) throw new InvalidOperationException("action not reopened for seat " + s);
                    if (a.To < l.MinRaiseTo || a.To > l.MaxTo) throw new InvalidOperationException("raise to " + a.To + " outside " + l.MinRaiseTo + ".." + l.MaxTo);
                    RaiseTo(s, a.To);
                    break;
                case ActionType.AllIn:
                    long to = l.MaxTo;
                    if (to > CurrentBet)
                    {
                        if (!l.CanRaise) throw new InvalidOperationException("action not reopened for seat " + s);
                        RaiseTo(s, to);
                    }
                    else Put(s, Stack[s]); // all-in call for less
                    break;
            }
            HasActed[s] = true;
            _actedAtRaise[s] = _fullRaiseId;
            Events.Add(new HoldemEvent { Kind = EventKind.Action, Seat = s, Action = IsAllIn[s] && a.Type != ActionType.Fold ? ActionType.AllIn : a.Type, Amount = StreetBet[s], Amount2 = StreetBet[s] - before, Street = Street });

            if (PlayersInHand == 1)
            {
                Finish(false);
                return;
            }
            ToAct = FindNextToAct(s);
            if (ToAct < 0) CloseStreet();
        }

        void RaiseTo(int s, long to)
        {
            long size = to - CurrentBet;
            if (size >= LastRaiseSize)
            {
                LastRaiseSize = size;
                _fullRaiseId++;
            }
            Put(s, to - StreetBet[s]);
            CurrentBet = to;
            LastAggressor = s;
            LastRaiser = s;
            RaisesThisStreet++;
        }

        void Put(int s, long amount)
        {
            Stack[s] -= amount;
            StreetBet[s] += amount;
            Committed[s] += amount;
            if (Stack[s] == 0) IsAllIn[s] = true;
        }

        int FindNextToAct(int from)
        {
            for (int k = 1; k <= Seats; k++)
            {
                int s = (from + k) % Seats;
                if (NeedsToAct(s)) return s;
            }
            return -1;
        }

        void CloseStreet()
        {
            while (true)
            {
                if (Street == Street.River)
                {
                    Finish(true);
                    return;
                }
                if (PlayersAbleToAct <= 1 && RunoutFromBoardCount < 0)
                {
                    RunoutFromBoardCount = BoardCount;
                    Events.Add(new HoldemEvent { Kind = EventKind.Runout, Street = Street });
                    RevealAll(); // all-in and no betting left: everyone tables their cards before the board comes
                }
                Street++;
                int n = Street == Street.Flop ? 3 : 1;
                var dealt = new int[n];
                for (int i = 0; i < n; i++) dealt[i] = Board[BoardCount++] = _deck.Deal();
                Events.Add(new HoldemEvent { Kind = EventKind.Board, Street = Street, Cards = dealt });

                for (int s = 0; s < Seats; s++)
                {
                    StreetBet[s] = 0;
                    HasActed[s] = false;
                }
                CurrentBet = 0;
                LastRaiseSize = BigBlind;
                LastAggressor = -1;
                LastRaiser = -1;
                RaisesThisStreet = 0;

                if (RunoutFromBoardCount >= 0) continue; // nobody left to bet: deal on
                ToAct = FindNextToAct(Button);
                if (ToAct >= 0) return;
            }
        }

        // ---------- pots ----------

        void Finish(bool showdown)
        {
            IsOver = true;
            ToAct = -1;
            WentToShowdown = showdown && PlayersInHand > 1;
            if (WentToShowdown)
            {
                Street = Street.Showdown;
                for (int s = 0; s < Seats; s++)
                    if (InHand[s] && !Folded[s]) Scores[s] = HandEvaluator.Evaluate(Hole0[s], Hole1[s], Board, 5);
                if (!RevealedAtAllIn && AnyAllIn()) RevealAll(); // all-in called on the river: same, cards up at once
                if (!RevealedAtAllIn)
                {
                    // no all-in: last river aggressor shows first
                    BuildShowOrder();
                    foreach (int s in ShowOrder)
                        Events.Add(new HoldemEvent { Kind = EventKind.Show, Seat = s, Cards = new[] { Hole0[s], Hole1[s] }, Score = Scores[s] });
                }
            }
            BuildPots();
            AwardPots();
            Events.Add(new HoldemEvent { Kind = EventKind.HandEnd });
        }

        /// <summary>True when an all-in closed the betting and every live hand was turned face up at once.</summary>
        public bool RevealedAtAllIn { get; private set; }

        bool AnyAllIn()
        {
            for (int s = 0; s < Seats; s++) if (InHand[s] && !Folded[s] && IsAllIn[s]) return true;
            return false;
        }

        /// <summary>Tournament all-in rule: all live hands face up, clockwise from the button's left.</summary>
        void RevealAll()
        {
            RevealedAtAllIn = true;
            ShowOrder.Clear();
            for (int k = 1; k <= Seats; k++)
            {
                int s = (Button + k) % Seats;
                if (!InHand[s] || Folded[s]) continue;
                ShowOrder.Add(s);
                Events.Add(new HoldemEvent { Kind = EventKind.Reveal, Seat = s, Cards = new[] { Hole0[s], Hole1[s] }, Street = Street });
            }
        }

        /// <summary>Last aggressor on the river shows first, then clockwise; with no river bet, first player left of the button.</summary>
        void BuildShowOrder()
        {
            int first = LastAggressor >= 0 && !Folded[LastAggressor] ? LastAggressor : -1;
            if (first < 0)
                for (int k = 1; k <= Seats && first < 0; k++)
                {
                    int s = (Button + k) % Seats;
                    if (InHand[s] && !Folded[s]) first = s;
                }
            for (int k = 0; k < Seats; k++)
            {
                int s = (first + k) % Seats;
                if (InHand[s] && !Folded[s]) ShowOrder.Add(s);
            }
        }

        void BuildPots()
        {
            var levels = new SortedSet<long>();
            for (int s = 0; s < Seats; s++) if (Committed[s] > 0) levels.Add(Committed[s]);
            long prev = 0, carry = 0;
            foreach (long level in levels)
            {
                long amount = carry;
                for (int s = 0; s < Seats; s++) amount += Math.Min(Committed[s], level) - Math.Min(Committed[s], prev);
                prev = level;
                var pot = new Pot { Amount = amount };
                for (int s = 0; s < Seats; s++)
                    if (InHand[s] && !Folded[s] && Committed[s] >= level) pot.Eligible.Add(s);
                if (pot.Eligible.Count == 0)
                {
                    carry = amount; // only folded money at this level: roll it into the next
                    continue;
                }
                carry = 0;
                // same contenders as the previous pot → same pot
                var last = Pots.Count > 0 ? Pots[Pots.Count - 1] : null;
                if (last != null && SameSet(last.Eligible, pot.Eligible)) last.Amount += amount;
                else Pots.Add(pot);
            }
            if (carry > 0 && Pots.Count > 0) Pots[Pots.Count - 1].Amount += carry;
        }

        static bool SameSet(List<int> a, List<int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        void AwardPots()
        {
            for (int p = 0; p < Pots.Count; p++)
            {
                var pot = Pots[p];
                if (pot.Eligible.Count == 1 || !WentToShowdown)
                {
                    pot.Winners.Add(pot.Eligible[0]);
                }
                else
                {
                    int best = int.MinValue;
                    foreach (int s in pot.Eligible) best = Math.Max(best, Scores[s]);
                    // odd chips go to the winner closest to the button's left
                    for (int k = 1; k <= Seats; k++)
                    {
                        int s = (Button + k) % Seats;
                        if (pot.Eligible.Contains(s) && Scores[s] == best) pot.Winners.Add(s);
                    }
                }
                long share = pot.Amount / pot.Winners.Count, odd = pot.Amount % pot.Winners.Count;
                for (int w = 0; w < pot.Winners.Count; w++)
                {
                    long amount = share + (w < odd ? 1 : 0);
                    int s = pot.Winners[w];
                    Won[s] += amount;
                    Stack[s] += amount;
                    Events.Add(new HoldemEvent { Kind = EventKind.PotWon, Seat = s, Amount = amount, Amount2 = p, Score = Scores[s] });
                }
            }
        }
    }
}
