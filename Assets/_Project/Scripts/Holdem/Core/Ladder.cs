using System;
using System.Collections.Generic;
using AllIn.Core;
using AllIn.Holdem.Brain;

namespace AllIn.Holdem
{
    // Hold'em v2 "올인 or 폴드 + 승수 사다리" (DESIGN.md C-v2). The hero only ever pushes or folds; opponents behind decide
    // whether to call from the push/fold tables; N wins in a row climb the tournament's ladder. Pure rules — the presentation
    // replays the LadderHand records. The old full-table Tournament is untouched (not used by the game path).

    public enum LadderStage { None = 0, Seat, MoneyIn, FinalTable, Podium, Champion }

    public enum HandKind { Decision, ForcedAllIn, FreeBlind, Facing }

    public enum HandOutcome { Folded, FreeBlind, Stole, Won, LostSurvived, LostOut, Split }

    public sealed class LadderStep
    {
        public int Wins;
        public LadderStage Stage;
        public Prize Prize;
        public bool Ends;   // reaching it ends the tournament
        public LadderStep(int wins, LadderStage stage, Prize prize, bool ends) { Wins = wins; Stage = stage; Prize = prize; Ends = ends; }
    }

    public sealed class LadderConfig
    {
        public TournamentKind Kind;
        public int StartBb = 15;
        public int HandsPerLevel = 6;
        public int Field;
        public LadderStep[] Steps;              // ascending by wins
        public PersonalityId[] Roster;          // 8 opponents (seats 1..8)
        public ReentryRule Reentry = new ReentryRule();
        public long EntryFee;
        public int StudyLevel;
        /// <summary>C-v2 rev.2: the big-blind seat also pays a 1bb ante, and the hero at BB may face a steal shove (call / fold).</summary>
        public bool BbAnte = true;
        /// <summary>C-v2 rev.2: opponent stacks come from the tournament average (lognormal), not from the hero's stack; re-entry restarts at the starting chips.</summary>
        public bool AverageStacks = true;
        /// <summary>E2: chance a called all-in becomes a guaranteed win (DESIGN.md E1/E2). Sim-tunable; default 0.</summary>
        public double LuckQ = DefaultLuckQ;
        /// <summary>Starting value of <see cref="LuckQ"/> for new configs (tests / sims that must not see luck set it to 0; the game never touches it).</summary>
        public static double DefaultLuckQ = HoldemRules.LuckQHoldem;

        public const long BaseBb = 100;
        public const int MaxLevel = 20;

        public static LadderConfig For(TournamentKind kind, int studyLevel = 0)
        {
            Prize Cash(long gross) => new Prize(PrizeType.Cash, gross, gross - (long)Math.Round(gross * HoldemRules.MainTaxRate));
            switch (kind)
            {
                case TournamentKind.Pub:
                    return new LadderConfig
                    {
                        Kind = kind, Field = 8, StudyLevel = studyLevel, EntryFee = HoldemRules.PubFee,
                        Steps = new[] { new LadderStep(3, LadderStage.Seat, new Prize(PrizeType.Seat, 1, 1), true) },
                        Reentry = new ReentryRule { Allowed = true, UntilLevel = HoldemRules.ReentryUntilLevel, Cost = HoldemRules.PubFee },
                        Roster = MakeRoster(2, 3, 2, 1),
                    };
                case TournamentKind.Satellite:
                    return new LadderConfig
                    {
                        Kind = kind, Field = 64, StudyLevel = studyLevel, EntryFee = HoldemRules.SatelliteFee,   // required wins ~ log2(field)
                        Steps = new[] { new LadderStep(6, LadderStage.Seat, new Prize(PrizeType.Seat, 1, 1), true) },
                        Reentry = new ReentryRule { Allowed = true, UntilLevel = HoldemRules.ReentryUntilLevel, Cost = HoldemRules.SatelliteFee },
                        Roster = MakeRoster(2, 2, 2, 2),
                    };
                default:
                    return new LadderConfig
                    {
                        Kind = kind, Field = HoldemRules.MainFieldSize, StudyLevel = studyLevel,
                        Steps = new[]
                        {
                            new LadderStep(5, LadderStage.MoneyIn, Cash(HoldemRules.MainPrizes[3]), false),
                            new LadderStep(6, LadderStage.FinalTable, Cash(HoldemRules.MainPrizes[3]), false),
                            new LadderStep(7, LadderStage.Podium, Cash(HoldemRules.MainPrizes[1]), false), // 2nd or 3rd, drawn on arrival
                            new LadderStep(8, LadderStage.Champion, Cash(HoldemRules.MainPrizes[0]), true),
                        },
                        Reentry = new ReentryRule { Allowed = false },
                        Roster = MakeRoster(2, 1, 1, 4),
                    };
            }
        }

        static PersonalityId[] MakeRoster(int tight, int station, int maniac, int pro)
        {
            var l = new List<PersonalityId>();
            for (int i = 0; i < tight; i++) l.Add(PersonalityId.Tight);
            for (int i = 0; i < station; i++) l.Add(PersonalityId.Station);
            for (int i = 0; i < maniac; i++) l.Add(PersonalityId.Maniac);
            for (int i = 0; i < pro; i++) l.Add(PersonalityId.Pro);
            return l.ToArray();
        }

        public static long BbAt(int level) => BaseBb << Math.Min(level, MaxLevel);
    }

    /// <summary>One hand, from the deal to the outcome. The presentation replays this.</summary>
    public sealed class LadderHand
    {
        public int HandNo;
        public int Level;                    // 0-based
        public long Bb;
        public int PosIndex;                 // 0..8 in the cycle
        public TablePosition Pos;
        public int Behind;                   // opponents left to act after the hero (seats 1..Behind)
        public int OppCount;                 // opponents dealt (= Behind, or all 8 on the BB hand when BbAnte)
        public int ShoverSeat;               // BB hand only: the first opponent who shoved (0 = nobody)
        public TablePosition ShoverPos;
        public long ShoverStack;
        public double ShoverStackBb => ShoverSeat > 0 ? ShoverStack / (double)Bb : 0;
        public int Hole0, Hole1;
        public long StackBefore;
        public HandKind Kind;
        public int HandsToNextLevel;

        public int[] OppCards;               // 2 per opponent behind
        public double[] OppStackRatio;       // per opponent behind

        // resolution
        public bool ChoseAllIn;
        public HandOutcome Outcome;
        public int CallerSeat;               // 1..8, 0 = none
        public PersonalityId CallerPersonality;
        public int Opp0 = -1, Opp1 = -1;
        public long OppStack;
        public int[] Board;                  // 5 cards when a call happened
        public double HeroEquity = -1;       // vs the caller, at the moment of the call
        public long Delta;
        public long StackAfter;
        public int WinsAfter;
        public bool BadBeat;
        public LadderStage StageReached;     // new best stage this hand (None if no change)
        public bool BubbleNow;
        public bool LevelUp;                 // the NEXT hand starts a new level
        public bool BustedOut;               // eliminated (may re-enter, see Ladder.AwaitingReentry)
        public bool LuckTriggered;           // E2: "무조건 승리" came up for this called all-in (decided after the call, before the cards)
        public bool LuckFlipped;             // ... and the board had to be rebuilt to make the hero win

        public double StackBbBefore => StackBefore / (double)Bb;
    }

    /// <summary>Decides push/fold for a headless hero (sim, tests).</summary>
    public interface ILadderPlayer
    {
        bool AllIn(LadderHand hand);
    }

    public interface ILadderHooks : ITournamentHooks
    {
        ILadderPlayer LadderHero { get; }
    }

    public sealed class Ladder
    {
        public static readonly TablePosition[] Cycle =
        {
            TablePosition.UTG, TablePosition.UTG, TablePosition.UTG, TablePosition.MP, TablePosition.MP,
            TablePosition.CO, TablePosition.BTN, TablePosition.SB, TablePosition.BB,
        };

        public const double StealBb = 1.5, SmallBlindBb = 0.5, BadBeatEquity = 0.70;
        public const double OppStackMin = 0.5, OppStackMax = 1.5;   // legacy (AverageStacks = false)
        public const double StackSigma = 0.5;                        // lognormal sigma around the average stack
        public const double AnteBb = 1.0;
        public static int EquitySamples = 400;

        public LadderConfig Config { get; }
        public IRng Rng { get; }
        /// <summary>E1: the "무조건 승리" stream. Falls back to <see cref="Rng"/> when unset; never drawn while LuckQ = 0.</summary>
        public IRng LuckRng { get; set; }
        public long Stack { get; internal set; }
        public int Wins { get; private set; }
        public int HandsPlayed { get; private set; }
        public bool Finished { get; private set; }
        public bool AwaitingReentry { get; private set; }
        public LadderStage BestStage { get; private set; }
        public LadderHand Current { get; private set; }
        public TournamentResult Result { get; } = new TournamentResult();
        public readonly PersonalityId[] SeatPersonality = new PersonalityId[HoldemRules.Seats]; // index 1..8
        public readonly string[] SeatName = new string[HoldemRules.Seats];
        public readonly List<LadderHand> History = new List<LadderHand>();

        Prize _bestPrize = Prize.None;
        int _bestWins;
        bool _bubbleShown;
        readonly string[] _names;

        public Ladder(LadderConfig config, IRng rng)
        {
            Config = config;
            Rng = rng;
            for (int i = 1; i < HoldemRules.Seats; i++) SeatPersonality[i] = config.Roster[i - 1];
            _names = (string[])Texts.HoldemNames.Clone();
            rng.Shuffle(_names);
            for (int i = 1; i < HoldemRules.Seats; i++) SeatName[i] = _names[(i - 1) % _names.Length];
            Stack = config.StartBb * LadderConfig.BbAt(0);
            Result.Log.Add("ladder " + config.Kind + " start " + config.StartBb + "bb");
        }

        public int Level => Math.Min(LadderConfig.MaxLevel, HandsPlayed / Config.HandsPerLevel);
        public long Bb => LadderConfig.BbAt(Level);
        public int HandsToNextLevel => Config.HandsPerLevel - HandsPlayed % Config.HandsPerLevel;

        /// <summary>Field ÷ 2^wins, rounded up, at least 1 (DESIGN.md C-v2).</summary>
        public int RemainingField(int wins) => (int)Math.Max(1, Math.Ceiling(Config.Field / Math.Pow(2, wins)));
        public int Remaining => RemainingField(Wins);

        public LadderStep NextStep()
        {
            foreach (var s in Config.Steps) if (s.Wins > Wins) return s;
            return null;
        }

        // ---------- hand ----------

        /// <summary>Tournament average stack in chips: starting chips x field / remaining players.</summary>
        public double AverageStack => Config.StartBb * (double)LadderConfig.BbAt(0) * Config.Field / Remaining;

        long StackFor(double mult) => Math.Max(1, (long)Math.Round((Config.AverageStacks ? AverageStack : Stack) * mult));

        double DrawStackMult() => Config.AverageStacks
            ? Math.Exp(StackSigma * Rng.NextGaussian())
            : OppStackMin + (OppStackMax - OppStackMin) * Rng.NextDouble();

        /// <summary>Deals the next hand. Hero cards, position and (hidden) opponent cards / stack ratios are fixed here so the deal
        /// never depends on the hero's choice.</summary>
        public LadderHand BeginHand()
        {
            if (Finished || AwaitingReentry) throw new InvalidOperationException("no hand to deal");
            if (!Resolved) throw new InvalidOperationException("resolve the current hand first");
            int idx = HandsPlayed % Cycle.Length;
            var h = new LadderHand
            {
                HandNo = HandsPlayed + 1, Level = Level, Bb = Bb, PosIndex = idx, Pos = Cycle[idx],
                Behind = HoldemRules.Seats - 1 - idx, StackBefore = Stack, HandsToNextLevel = HandsToNextLevel,
            };
            var deck = new Deck(Rng);
            h.Hole0 = deck.Deal(); h.Hole1 = deck.Deal();
            bool bbSeat = idx == Cycle.Length - 1;
            h.OppCount = bbSeat && Config.BbAnte ? HoldemRules.Seats - 1 : h.Behind;
            h.OppCards = new int[h.OppCount * 2];
            h.OppStackRatio = new double[h.OppCount];
            for (int i = 0; i < h.OppCount; i++)
            {
                h.OppCards[2 * i] = deck.Deal(); h.OppCards[2 * i + 1] = deck.Deal();
                h.OppStackRatio[i] = DrawStackMult();
            }
            h.Board = new[] { deck.Deal(), deck.Deal(), deck.Deal(), deck.Deal(), deck.Deal() };
            h.Kind = bbSeat ? HandKind.FreeBlind : Stack < h.Bb ? HandKind.ForcedAllIn : HandKind.Decision;
            if (bbSeat && Config.BbAnte) FindShover(h);
            Current = h;
            Resolved = false;
            return h;
        }

        /// <summary>BB hand: the eight opponents ahead act in order; the first whose hand is inside their push range shoves.</summary>
        internal void FindShover(LadderHand h)
        {
            for (int i = 0; i < h.OppCount; i++)
            {
                var pos = Cycle[i];            // seats 1..8 act UTG, UTG, UTG, MP, MP, CO, BTN, SB
                long oppStack = StackFor(h.OppStackRatio[i]);
                double eff = Math.Min(Stack, oppStack) / (double)h.Bb;
                var pers = Personality.For(SeatPersonality[i + 1]);
                double range = RangeMath.Scaled(PushFoldTable.PushPercent(pos, eff), pers.RangeMultiplier);
                double pct = HandRanks.Percentile(h.OppCards[2 * i], h.OppCards[2 * i + 1]);
                if (pct > range) continue;
                h.ShoverSeat = i + 1; h.ShoverPos = pos; h.ShoverStack = oppStack;
                h.Kind = HandKind.Facing;
                return;
            }
        }

        public bool Resolved { get; private set; } = true;

        /// <summary>Applies the hero's choice (ignored for FreeBlind, forced for ForcedAllIn) and settles the hand.</summary>
        public LadderHand Resolve(bool allIn)
        {
            var h = Current;
            if (h == null || Resolved) throw new InvalidOperationException("no open hand");
            long bb = h.Bb;
            HandsPlayed++;
            Result.HandsPlayed = HandsPlayed;
            long before = Stack;

            if (h.Kind == HandKind.FreeBlind)
            {
                h.Outcome = HandOutcome.FreeBlind;
                Stack += bb / 2;
                Result.FreeBlinds++;
            }
            else if (h.Kind == HandKind.Facing)
            {
                Result.BbFacing++;
                bool call = allIn || Stack < bb;   // under one big blind the call is automatic
                h.ChoseAllIn = call;
                if (call) { Result.BbCalls++; Showdown(h, h.ShoverSeat - 1, h.ShoverStack); }
                else
                {
                    h.Outcome = HandOutcome.Folded;
                    Result.Folds++;
                    Stack = Math.Max(0, Stack - (long)Math.Round(bb * (1 + AnteBb)));   // BB + ante
                }
            }
            else
            {
                if (h.Kind == HandKind.ForcedAllIn) allIn = true;
                h.ChoseAllIn = allIn;
                if (!allIn)
                {
                    h.Outcome = HandOutcome.Folded;
                    Result.Folds++;
                    if (h.Pos == TablePosition.SB) Stack = Math.Max(0, Stack - bb / 2);
                }
                else ResolveAllIn(h);
            }

            h.Delta = Stack - before;
            h.StackAfter = Stack;
            h.WinsAfter = Wins;
            h.LevelUp = HandsPlayed % Config.HandsPerLevel == 0 && Level > h.Level;
            Resolved = true;
            History.Add(h);
            Result.Log.Add(h.HandNo + ": " + h.Pos + " " + Card.ToText(h.Hole0) + Card.ToText(h.Hole1) + " " + (h.ChoseAllIn ? "push" : "fold") + " " + h.Outcome + " " + h.Delta + " wins " + Wins);

            if (h.Outcome == HandOutcome.LostOut || Stack <= 0) OnBust(h);
            return h;
        }

        void ResolveAllIn(LadderHand h)
        {
            long bb = h.Bb;
            int caller = -1;
            long oppStack = 0;
            for (int i = 0; i < h.Behind && caller < 0; i++)
            {
                int seat = i + 1;
                oppStack = StackFor(h.OppStackRatio[i]);
                double eff = Math.Min(Stack, oppStack) / (double)bb;
                var pers = Personality.For(SeatPersonality[seat]);
                double range = RangeMath.Scaled(PushFoldTable.CallPercent(h.Pos, eff), pers.RangeMultiplier);
                double pct = HandRanks.Percentile(h.OppCards[2 * i], h.OppCards[2 * i + 1]);
                if (pct <= range) caller = i;
            }

            if (caller < 0)
            {
                h.Outcome = HandOutcome.Stole;
                double pot = Config.BbAnte ? 1.5 + AnteBb : StealBb;   // SB 0.5 + BB 1 (+ ante 1)
                long gain = (long)Math.Round(bb * pot) - (h.Pos == TablePosition.SB ? bb / 2 : 0);
                Stack += gain;
                Result.Steals++;
                return;
            }

            Showdown(h, caller, oppStack);
        }

        void Showdown(LadderHand h, int caller, long oppStack)
        {
            Result.CalledAllIns++;
            h.CallerSeat = caller + 1;
            h.CallerPersonality = SeatPersonality[h.CallerSeat];
            h.Opp0 = h.OppCards[2 * caller]; h.Opp1 = h.OppCards[2 * caller + 1];
            h.OppStack = oppStack;

            var hands = new[] { new[] { h.Hole0, h.Hole1 }, new[] { h.Opp0, h.Opp1 } };
            // presentation/measurement equity uses its own stream so the tournament's random sequence never depends on it
            var eqRng = new Xoshiro128(0xE0E0UL + (ulong)HandsPlayed * 7919UL);
            h.HeroEquity = Equity.VsKnown(hands, 0, new int[5], 0, eqRng, EquitySamples);

            int mine = HandEvaluator.Evaluate(h.Hole0, h.Hole1, h.Board, 5);
            int theirs = HandEvaluator.Evaluate(h.Opp0, h.Opp1, h.Board, 5);
            // E2: only here — after the hero's push was called (or the hero called) and before the cards are turned up
            if (Config.LuckQ > 0 && (LuckRng ?? Rng).Chance(Config.LuckQ))
            {
                h.LuckTriggered = true;
                Result.LuckWins++;
                if (mine <= theirs)
                {
                    h.Board = WinningBoard(h, LuckRng ?? Rng);
                    h.LuckFlipped = true;
                    mine = HandEvaluator.Evaluate(h.Hole0, h.Hole1, h.Board, 5);
                    theirs = HandEvaluator.Evaluate(h.Opp0, h.Opp1, h.Board, 5);
                }
            }
            if (mine > theirs)
            {
                h.Outcome = HandOutcome.Won;
                Stack += Math.Min(Stack, oppStack);
                Wins++;
                Result.CalledWins++;
                Result.Wins = Wins;
                ReplaceSeat(h.CallerSeat);
                OnWinsChanged(h);
            }
            else if (mine == theirs)
            {
                h.Outcome = HandOutcome.Split;
            }
            else
            {
                if (h.HeroEquity >= BadBeatEquity) { h.BadBeat = true; Result.BadBeats++; }
                if (Stack > oppStack)
                {
                    h.Outcome = HandOutcome.LostSurvived;
                    Stack -= oppStack;
                }
                else
                {
                    h.Outcome = HandOutcome.LostOut;
                    Stack = 0;
                }
            }
        }

        /// <summary>A 5-card runout from the undealt cards on which the hero beats the caller (conditional sampling, exhaustive fallback).</summary>
        static int[] WinningBoard(LadderHand h, IRng rng)
        {
            var used = new bool[52];
            used[h.Hole0] = used[h.Hole1] = true;
            foreach (int c in h.OppCards) used[c] = true;
            var pool = new List<int>();
            for (int c = 0; c < 52; c++) if (!used[c]) pool.Add(c);
            var board = new int[5];
            bool Wins()
            {
                return HandEvaluator.Evaluate(h.Hole0, h.Hole1, board, 5) > HandEvaluator.Evaluate(h.Opp0, h.Opp1, board, 5);
            }
            var idx = pool.ToArray();
            for (int attempt = 0; attempt < 5000; attempt++)
            {
                for (int k = 0; k < 5; k++)
                {
                    int j = k + rng.NextInt(idx.Length - k);
                    int t = idx[k]; idx[k] = idx[j]; idx[j] = t;
                    board[k] = idx[k];
                }
                if (Wins()) return (int[])board.Clone();
            }
            int n = pool.Count;
            for (int a = 0; a < n; a++) for (int b = a + 1; b < n; b++) for (int c = b + 1; c < n; c++)
            for (int d = c + 1; d < n; d++) for (int e = d + 1; e < n; e++)
            {
                board[0] = pool[a]; board[1] = pool[b]; board[2] = pool[c]; board[3] = pool[d]; board[4] = pool[e];
                if (Wins()) return (int[])board.Clone();
            }
            return h.Board;   // no runout beats this hand (identical ranks and no flush): keep the deal
        }

        void ReplaceSeat(int seat)
        {
            var roster = Config.Roster;
            if (Config.Kind == TournamentKind.Main)
            {
                double r = Rng.NextDouble();
                if (r < HoldemRules.MainRefillProShare) SeatPersonality[seat] = PersonalityId.Pro;
                else SeatPersonality[seat] = (PersonalityId)Rng.NextInt(3); // Tight, Station, Maniac (enum order)
            }
            else SeatPersonality[seat] = roster[Rng.NextInt(roster.Length)];
            SeatName[seat] = _names[Rng.NextInt(_names.Length)];
        }

        void OnWinsChanged(LadderHand h)
        {
            if (Wins > _bestWins) _bestWins = Wins;
            LadderStep reached = null;
            foreach (var s in Config.Steps) if (s.Wins == Wins) reached = s;
            if (reached != null && reached.Stage > BestStage)
            {
                BestStage = reached.Stage;
                _bestPrize = reached.Prize;
                if (reached.Stage == LadderStage.Podium)
                {
                    // 2nd or 3rd, drawn on arrival (DESIGN.md C-v2)
                    bool second = Rng.Chance(0.5);
                    long gross = HoldemRules.MainPrizes[second ? 1 : 2];
                    _bestPrize = new Prize(PrizeType.Cash, gross, gross - (long)Math.Round(gross * HoldemRules.MainTaxRate));
                    Result.Finish = second ? 2 : 3;
                }
                h.StageReached = reached.Stage;
                Result.Log.Add("stage " + reached.Stage);
                if (reached.Ends) { Finish(true); return; }
            }
            if (!_bubbleShown && Wins == Config.Steps[0].Wins - 1)
            {
                _bubbleShown = true;
                h.BubbleNow = true;
                Result.BubbleReached = true;
            }
        }

        void OnBust(LadderHand h)
        {
            h.BustedOut = true;
            Result.BustBb.Add(h.StackBbBefore);
            var re = Config.Reentry;
            // re-entry window: bust while the level is still within the first UntilLevel levels (level index 0-based)
            if (re.Allowed && h.Level < re.UntilLevel) { AwaitingReentry = true; return; }
            Finish(false);
        }

        /// <summary>The hero busted inside the window: pay and restart at 15bb with 0 wins, or walk away.</summary>
        public void Reenter(bool yes)
        {
            if (!AwaitingReentry) throw new InvalidOperationException("no re-entry offered");
            AwaitingReentry = false;
            if (!yes) { Finish(false); return; }
            Result.Reentries++;
            Wins = 0;
            _bubbleShown = false;
            Stack = Config.AverageStacks ? Config.StartBb * LadderConfig.BbAt(0) : Config.StartBb * Bb;
            Result.Log.Add("re-entry at level " + (Level + 1));
        }

        void Finish(bool endedByLadder)
        {
            Finished = true;
            AwaitingReentry = false;
            Result.Stage = BestStage;
            Result.Wins = _bestWins;
            Result.Prize = BestStage == LadderStage.None ? Prize.None : _bestPrize;
            if (BestStage == LadderStage.Champion) Result.Finish = 1;
            else if (BestStage != LadderStage.Podium) Result.Finish = 0;
            Result.Log.Add("finished " + BestStage);
        }

        // ---------- advice (HUD 4) ----------

        /// <summary>Push chart advice for this hand's position and the hero's stack: top N% (PushFoldTable). 0 for the free-blind hand.</summary>
        public static double PushAdvicePercent(LadderHand h)
        {
            if (h.Pos == TablePosition.BB) return 0;
            return PushFoldTable.PushPercent(h.Pos, h.StackBbBefore);
        }

        /// <summary>BB hand facing a shove: call range (top %) against the shover's position at the effective stack.</summary>
        public static double CallAdvicePercent(LadderHand h)
        {
            if (h.ShoverSeat == 0) return 0;
            double eff = Math.Min(h.StackBefore, h.ShoverStack) / (double)h.Bb;
            return PushFoldTable.CallPercent(h.ShoverPos, eff);
        }

        /// <summary>The chart's advice for this hand, whichever kind it is: push / call range (top %).</summary>
        public static double AdvicePercent(LadderHand h) => h.Kind == HandKind.Facing ? CallAdvicePercent(h) : PushAdvicePercent(h);

        public static double HandPercentile(LadderHand h) => HandRanks.Percentile(h.Hole0, h.Hole1);

        /// <summary>Runs a whole tournament with a headless hero (re-entering whenever offered).</summary>
        public void RunToEnd(ILadderPlayer hero, Func<long, bool> reentry = null)
        {
            int guard = 0;
            while (!Finished && guard++ < 100_000)
            {
                if (AwaitingReentry) { Reenter(reentry == null || reentry(Config.Reentry.Cost)); continue; }
                var h = BeginHand();
                Resolve(hero.AllIn(h));
            }
            if (!Finished) Finish(false);
        }
    }
}
