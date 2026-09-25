using System;
using System.Collections.Generic;
using AllIn.Core;

namespace AllIn.Holdem
{
    /// <summary>Whoever decides for a seat: a bot, or (for the hero) the UI.</summary>
    public interface IDecider
    {
        PlayerAction Decide(Tournament t, int seat);
    }

    public interface IDeciderFactory
    {
        IDecider Create(PersonalityId personality);
    }

    public sealed class Entrant
    {
        public int Id;
        public string Name;
        public bool IsHero;
        public PersonalityId Personality;   // for the hero: only meaningful when a bot plays the seat (sims)
        public IDecider Decider;            // null for a human hero
        public int Seat;
        public long Stack;
        public bool Alive;
        public int Reentries;
        public int Finish;
        public bool IsRefill;
    }

    public enum StepResult { Progressed, NeedHeroAction, NeedHeroReentry, Finished }

    /// <summary>
    /// A single-table NLHE tournament (DESIGN.md C2–C3) as a step machine: the UI calls Step() until it needs the hero,
    /// headless code runs it to the end. Everything random comes from the injected RNG → same seed + same hero inputs =
    /// same tournament.
    /// </summary>
    public sealed class Tournament
    {
        public const int HeroSeat = HoldemRules.HeroSeat;

        public TournamentConfig Config { get; }
        public readonly Entrant[] Seats = new Entrant[HoldemRules.Seats];
        public readonly List<Entrant> AllEntrants = new List<Entrant>();
        public readonly List<HoldemEvent> Events = new List<HoldemEvent>();
        /// <summary>Median effective stack (bb) among live players, at the start of each hand (index = hand number − 1).</summary>
        public readonly List<double> MedianEffectiveBb = new List<double>();

        public HandState Hand { get; private set; }
        public int HandNumber { get; private set; }
        public int LevelIndex { get; private set; }
        public int Remaining { get; private set; }
        public bool Finished { get; private set; }
        public bool HeroOut { get; private set; }
        public int HandsToFinalTable { get; private set; }
        public Entrant Winner { get; private set; }
        public TournamentResult Result { get; } = new TournamentResult();
        public bool StopWhenHeroOut { get; set; }

        public BlindLevel Blinds => Config.BlindSchedule[LevelIndex];
        /// <summary>The tournament's single random stream (bots draw from it too, so a seed fixes everything).</summary>
        public IRng Rng => _rng;
        public Entrant Hero => Seats[HeroSeat];
        public bool AwaitingHeroReentry => _pendingHeroReentry;

        readonly IRng _rng;
        readonly IDeciderFactory _bots;
        readonly Queue<int> _busts = new Queue<int>();
        readonly string[] _names;
        int _nameCursor;
        int _button = -1;
        int _nextEntrantId;
        bool _pendingHeroReentry;
        bool _handOpen;
        bool _bubbleShown, _finalTableShown;
        int _eventsFromHand;

        /// <param name="heroBot">Null when a human plays the hero seat (the UI answers NeedHeroAction).</param>
        public Tournament(TournamentConfig config, IRng rng, IDeciderFactory bots, IDecider heroBot = null, PersonalityId heroPersonality = PersonalityId.Pro)
        {
            if (config.Roster == null || config.Roster.Length != HoldemRules.Seats - 1) throw new ArgumentException("roster must name 8 opponents");
            Config = config;
            _rng = rng;
            _bots = bots;
            _names = (string[])Texts.HoldemNames.Clone();
            rng.Shuffle(_names);

            Seats[HeroSeat] = NewEntrant(Texts.HoldemHeroName, true, heroPersonality, heroBot, HeroSeat, config.StartingChips, false);
            for (int i = 0; i < config.Roster.Length; i++)
                Seats[i + 1] = NewEntrant(NextName(), false, config.Roster[i], bots.Create(config.Roster[i]), i + 1, config.StartingChips, false);
            Remaining = config.FieldSize > 0 ? config.FieldSize : HoldemRules.Seats;
            if (config.FieldSize > 0) Emit(new HoldemEvent { Kind = EventKind.FieldCount, Amount = Remaining });
        }

        Entrant NewEntrant(string name, bool hero, PersonalityId p, IDecider d, int seat, long stack, bool refill)
        {
            var e = new Entrant { Id = _nextEntrantId++, Name = name, IsHero = hero, Personality = p, Decider = d, Seat = seat, Stack = stack, Alive = true, IsRefill = refill };
            AllEntrants.Add(e);
            return e;
        }

        string NextName() => _names[_nameCursor++ % _names.Length] + (_nameCursor > _names.Length ? " " + (_nameCursor / _names.Length + 1) : "");

        int SeatedAlive
        {
            get { int n = 0; foreach (var e in Seats) if (e != null && e.Alive) n++; return n; }
        }

        void Emit(HoldemEvent e)
        {
            Events.Add(e);
            Result.Log.Add(HandNumber + ": " + e);
        }

        // ---------- stepping ----------

        public StepResult Step()
        {
            if (Finished) return StepResult.Finished;
            if (_pendingHeroReentry) return StepResult.NeedHeroReentry;
            if (!_handOpen)
            {
                StartHand();
                return StepResult.Progressed;
            }
            int s = Hand.ToAct;
            var who = Seats[s];
            if (who.Decider == null) return StepResult.NeedHeroAction;
            Act(who.Decider.Decide(this, s));
            return Finished ? StepResult.Finished : StepResult.Progressed;
        }

        public void HeroAct(PlayerAction a)
        {
            if (!_handOpen || Hand.ToAct != HeroSeat) throw new InvalidOperationException("not the hero's turn");
            Act(a);
        }

        public void HeroReentry(bool yes)
        {
            if (!_pendingHeroReentry) throw new InvalidOperationException("no re-entry pending");
            _pendingHeroReentry = false;
            if (yes) Reenter(Hero);
            else Eliminate(Hero);
            ProcessBusts();
        }

        /// <summary>Runs to the end with bots (and the hero bot, or <paramref name="heroReentry"/> for re-entry questions).</summary>
        public void RunToEnd(Func<long, bool> heroReentry = null)
        {
            int guard = 0;
            while (!Finished)
            {
                if (guard++ > 200_000) throw new InvalidOperationException("tournament does not end");
                switch (Step())
                {
                    case StepResult.NeedHeroAction: throw new InvalidOperationException("hero has no bot");
                    case StepResult.NeedHeroReentry: HeroReentry(heroReentry != null && heroReentry(Config.Reentry.Cost)); break;
                }
            }
        }

        void Act(PlayerAction wish)
        {
            int s = Hand.ToAct;
            Hand.Apply(Hand.Normalize(s, wish));
            FlushHandEvents();
            if (Hand.IsOver) EndHand();
        }

        void FlushHandEvents()
        {
            for (; _eventsFromHand < Hand.Events.Count; _eventsFromHand++) Emit(Hand.Events[_eventsFromHand]);
        }

        void StartHand()
        {
            HandNumber++;
            int level = Math.Min((HandNumber - 1) / Config.HandsPerLevel, Config.BlindSchedule.Length - 1);
            if (level != LevelIndex)
            {
                LevelIndex = level;
                Emit(new HoldemEvent { Kind = EventKind.LevelUp, Amount = Blinds.Small, Amount2 = Blinds.Big, Seat = level + 1 });
            }

            _button = _button < 0 ? FirstButton() : NextAliveSeat(_button);
            var stacks = new long[HoldemRules.Seats];
            for (int i = 0; i < stacks.Length; i++) stacks[i] = Seats[i] != null && Seats[i].Alive ? Seats[i].Stack : 0;
            MedianEffectiveBb.Add(MedianEffective(stacks, Blinds.Big));
            if (Hero.Alive) Result.HandsPlayed++;

            Emit(new HoldemEvent { Kind = EventKind.HandStart, Seat = _button, Amount = Blinds.Small, Amount2 = Blinds.Big, Text = "L" + (LevelIndex + 1) });
            Hand = new HandState(stacks, _button, Blinds.Small, Blinds.Big, _rng);
            _eventsFromHand = 0;
            _handOpen = true;
            FlushHandEvents();
            if (Hand.IsOver) EndHand(); // everyone all-in from the blinds
        }

        int FirstButton()
        {
            var alive = new List<int>();
            for (int i = 0; i < Seats.Length; i++) if (Seats[i] != null && Seats[i].Alive) alive.Add(i);
            return alive[_rng.NextInt(alive.Count)];
        }

        int NextAliveSeat(int from)
        {
            for (int k = 1; k <= Seats.Length; k++)
            {
                int s = (from + k) % Seats.Length;
                if (Seats[s] != null && Seats[s].Alive) return s;
            }
            return from;
        }

        static double MedianEffective(long[] stacks, long bb)
        {
            var eff = new List<double>();
            for (int i = 0; i < stacks.Length; i++)
            {
                if (stacks[i] <= 0) continue;
                long maxOther = 0;
                for (int j = 0; j < stacks.Length; j++) if (j != i) maxOther = Math.Max(maxOther, stacks[j]);
                eff.Add(Math.Min(stacks[i], maxOther) / (double)bb);
            }
            eff.Sort();
            int n = eff.Count;
            return n % 2 == 1 ? eff[n / 2] : (eff[n / 2 - 1] + eff[n / 2]) / 2;
        }

        void EndHand()
        {
            _handOpen = false;
            CheckBadBeat();
            for (int i = 0; i < Seats.Length; i++)
                if (Seats[i] != null && Seats[i].Alive) Seats[i].Stack = Hand.Stack[i];

            // simultaneous busts: the bigger starting stack finishes higher
            var busted = new List<int>();
            for (int i = 0; i < Seats.Length; i++)
                if (Seats[i] != null && Seats[i].Alive && Seats[i].Stack == 0) busted.Add(i);
            busted.Sort((a, b) => Hand.StartStack[b] != Hand.StartStack[a] ? Hand.StartStack[b].CompareTo(Hand.StartStack[a]) : a.CompareTo(b));
            foreach (int s in busted) _busts.Enqueue(s);
            ProcessBusts();
        }

        void ProcessBusts()
        {
            while (_busts.Count > 0)
            {
                int s = _busts.Peek();
                var e = Seats[s];
                bool mayReenter = Config.Reentry.Allowed && LevelIndex + 1 <= Config.Reentry.UntilLevel;
                if (mayReenter && e.IsHero && e.Decider == null && !_pendingHeroReentry)
                {
                    _pendingHeroReentry = true; // ask the UI; HeroReentry() resumes here
                    _busts.Dequeue();
                    return;
                }
                _busts.Dequeue();
                bool again = mayReenter && (e.IsHero ? HeroBotReentry(Config.Reentry.Cost) : _rng.Chance(HoldemRules.AiReentryChance));
                if (again) Reenter(e);
                else Eliminate(e);
            }
            AfterBusts();
        }

        /// <summary>When a bot plays the hero seat: whether to re-enter (given the cost). Default: always.</summary>
        public Func<long, bool> HeroBotReentry { get; set; } = _ => true;

        void Reenter(Entrant e)
        {
            e.Stack = Config.StartingChips;
            e.Reentries++;
            if (e.IsHero) Result.Reentries++;
            Emit(new HoldemEvent { Kind = EventKind.Reentry, Seat = e.Seat, Amount = Config.Reentry.Cost });
        }

        void Eliminate(Entrant e)
        {
            e.Alive = false;
            e.Finish = Remaining;
            Remaining--;
            Emit(new HoldemEvent { Kind = EventKind.Busted, Seat = e.Seat, Amount = e.Finish });
            if (e.IsHero)
            {
                HeroOut = true;
                Result.Finish = e.Finish;
                Result.Prize = PrizeFor(e.Finish);
                Result.BustedAtLevel = LevelIndex + 1;
            }
        }

        Prize PrizeFor(int finish) => finish >= 1 && finish <= Config.Payouts.Length ? Config.Payouts[finish - 1] : Prize.None;

        void AfterBusts()
        {
            if (Config.FieldSize > 0)
            {
                // players outside our table bust too
                int offTable = Remaining - SeatedAlive;
                if (offTable > 0)
                {
                    double expected = offTable * HoldemRules.OffTableEliminationRate;
                    int k = (int)expected + (_rng.Chance(expected - (int)expected) ? 1 : 0);
                    k = Math.Min(k, offTable);
                    if (k > 0)
                    {
                        Remaining -= k;
                        Emit(new HoldemEvent { Kind = EventKind.FieldCount, Amount = Remaining });
                    }
                }
                // tables merge: fill our empty seats while the field still has players elsewhere
                for (int s = 0; s < Seats.Length && Remaining > SeatedAlive; s++)
                {
                    if (Seats[s] != null && Seats[s].Alive) continue;
                    var p = RefillPersonality();
                    long avg = Config.StartingChips * Config.FieldSize / Math.Max(1, Remaining);
                    Seats[s] = NewEntrant(NextName(), false, p, _bots.Create(p), s, avg, true);
                    Emit(new HoldemEvent { Kind = EventKind.Refill, Seat = s, Amount = avg, Text = Seats[s].Name });
                }
                if (!_finalTableShown && Remaining <= HoldemRules.Seats)
                {
                    _finalTableShown = true;
                    HandsToFinalTable = HandNumber;
                    Emit(new HoldemEvent { Kind = EventKind.FinalTable, Amount = Remaining });
                }
            }

            if (!_bubbleShown && Remaining == Config.PaidPlaces + 1)
            {
                _bubbleShown = true;
                if (Hero.Alive) Result.BubbleReached = true;
                Emit(new HoldemEvent { Kind = EventKind.Bubble, Amount = Remaining });
            }

            if (SeatedAlive <= 1 && Remaining <= 1)
            {
                foreach (var e in Seats)
                {
                    if (e == null || !e.Alive) continue;
                    e.Finish = 1;
                    Winner = e;
                    if (e.IsHero)
                    {
                        Result.Finish = 1;
                        Result.Prize = PrizeFor(1);
                    }
                }
                Finished = true;
                Emit(new HoldemEvent { Kind = EventKind.TournamentEnd, Seat = Winner?.Seat ?? -1 });
                return;
            }
            if (HeroOut && StopWhenHeroOut) Finished = true;
        }

        PersonalityId RefillPersonality()
        {
            if (_rng.Chance(HoldemRules.MainRefillProShare)) return PersonalityId.Pro;
            switch (_rng.NextInt(3))
            {
                case 0: return PersonalityId.Tight;
                case 1: return PersonalityId.Station;
                default: return PersonalityId.Maniac;
            }
        }

        // ---------- bad beats ----------

        /// <summary>Hero all-in with ≥ 70% equity at the moment betting closed, and lost chips on the hand.</summary>
        void CheckBadBeat()
        {
            var h = Hand;
            if (h.RunoutFromBoardCount < 0 || !h.InHand[HeroSeat] || h.Folded[HeroSeat]) return; // hero must be in the all-in runout
            var seats = new List<int>();
            for (int s = 0; s < HandState.Seats; s++) if (h.InHand[s] && !h.Folded[s]) seats.Add(s);
            if (seats.Count < 2) return;
            var hands = new int[seats.Count][];
            int hero = -1;
            for (int i = 0; i < seats.Count; i++)
            {
                hands[i] = new[] { h.Hole0[seats[i]], h.Hole1[seats[i]] };
                if (seats[i] == HeroSeat) hero = i;
            }
            double eq = Equity.VsKnown(hands, hero, h.Board, h.RunoutFromBoardCount, _rng, 1000);
            bool lost = h.Won[HeroSeat] < h.Committed[HeroSeat];
            if (eq >= HoldemRules.BadBeatEquity && lost)
            {
                Result.BadBeats++;
                Emit(new HoldemEvent { Kind = EventKind.BadBeat, Seat = HeroSeat, Amount = (long)Math.Round(eq * 100) });
            }
        }
    }
}
