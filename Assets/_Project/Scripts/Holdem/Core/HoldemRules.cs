namespace AllIn.Holdem
{
    /// <summary>
    /// Every hold'em number, mirroring md/DESIGN.md Part C. The doc wins on conflict:
    /// change DESIGN.md first, then this file, then log the reason in md/STATUS.md.
    /// </summary>
    public static class HoldemRules
    {
        public const int Seats = 9;
        public const int HeroSeat = 0;

        // C2 blinds (hand-count based; the last level holds)
        public static readonly BlindLevel[] Schedule =
        {
            new BlindLevel(50, 100), new BlindLevel(100, 200), new BlindLevel(200, 400), new BlindLevel(400, 800),
            new BlindLevel(800, 1600), new BlindLevel(1500, 3000), new BlindLevel(3000, 6000), new BlindLevel(6000, 12000),
            new BlindLevel(12000, 24000),
        };

        public const long PubStartingChips = 3000;
        public const int PubHandsPerLevel = 6;
        public const long MainStartingChips = 5000;
        public const int MainHandsPerLevel = 8;

        // C3 prizes / re-entry
        public const long PubFee = 30_000;
        /// <summary>E2 "무조건 승리": chance per called all-in the hero is part of. 3% (user decision 2026-09-30).</summary>
        public const double LuckQHoldem = 0.03;
        public const long SatelliteFee = 100_000;
        public const int ReentryUntilLevel = 3;       // bust during levels 1..3 → may re-enter
        public const double AiReentryChance = 0.5;     // provisional (DESIGN.md C3)
        public const int PubPoints2nd = 2, PubPoints3rd = 1;
        public static readonly long[] MainPrizes = { 100_000_000, 50_000_000, 30_000_000, 5_000_000, 5_000_000, 5_000_000, 5_000_000, 5_000_000, 5_000_000 };
        public const double MainTaxRate = 0.22;
        public const int MainFieldSize = 200;
        public const double OffTableEliminationRate = 0.05; // × players not at our table, per hand
        public const double MainRefillProShare = 0.60;       // rest split evenly across the other three

        // C6 presentation
        public const double BadBeatEquity = 0.70;
        public const float RiverDelay = 0.9f;
        public const float RiverDelayAllIn = 1.5f;
        public const float TurnDelayAllIn = 0.9f;
        public const float BadBeatBlackout = 0.5f;

        // C5 HUD
        public const double ChipPileBigAtAvg = 1.3, ChipPileMidAtAvg = 0.7;
    }

    public readonly struct BlindLevel
    {
        public readonly long Small, Big;
        public BlindLevel(long small, long big) { Small = small; Big = big; }
    }

    public enum TournamentKind { Pub, Satellite, Main }

    public enum PersonalityId { Tight, Station, Maniac, Pro }

    public enum PrizeType { None, Seat, Point, Cash }

    public readonly struct Prize
    {
        public readonly PrizeType Type;
        public readonly long Amount;
        public readonly long AfterTax; // == Amount unless taxed

        public Prize(PrizeType type, long amount, long afterTax)
        {
            Type = type;
            Amount = amount;
            AfterTax = afterTax;
        }

        public static Prize None => new Prize(PrizeType.None, 0, 0);
        public bool IsPaid => Type != PrizeType.None;
    }

    public sealed class ReentryRule
    {
        public bool Allowed;
        public int UntilLevel;   // 1-based: bust while the level is ≤ this → may re-enter
        public long Cost;
    }

    public sealed class TournamentConfig
    {
        public TournamentKind Kind;
        public int Seats = HoldemRules.Seats;
        public long StartingChips;
        public BlindLevel[] BlindSchedule = HoldemRules.Schedule;
        public int HandsPerLevel;
        public Prize[] Payouts;               // index 0 = 1st place
        public ReentryRule Reentry = new ReentryRule();
        public int StudyLevel;                // 0..4, fixed for the tournament
        public PersonalityId[] Roster;        // the 8 AI opponents
        public int FieldSize;                 // 0 = just this table; Main simulates a bigger field
        public long EntryFee;

        public int PaidPlaces
        {
            get { int n = 0; foreach (var p in Payouts) if (p.IsPaid) n++; return n; }
        }

        public static TournamentConfig For(TournamentKind kind, int studyLevel = 0)
        {
            switch (kind)
            {
                case TournamentKind.Pub:
                    return new TournamentConfig
                    {
                        Kind = kind, StartingChips = HoldemRules.PubStartingChips, HandsPerLevel = HoldemRules.PubHandsPerLevel,
                        Payouts = new[]
                        {
                            new Prize(PrizeType.Seat, 1, 1),
                            new Prize(PrizeType.Point, HoldemRules.PubPoints2nd, HoldemRules.PubPoints2nd),
                            new Prize(PrizeType.Point, HoldemRules.PubPoints3rd, HoldemRules.PubPoints3rd),
                        },
                        Reentry = new ReentryRule { Allowed = true, UntilLevel = HoldemRules.ReentryUntilLevel, Cost = HoldemRules.PubFee },
                        StudyLevel = studyLevel,
                        Roster = MakeRoster(2, 3, 2, 1),
                        EntryFee = HoldemRules.PubFee,
                    };
                case TournamentKind.Satellite:
                    return new TournamentConfig
                    {
                        Kind = kind, StartingChips = HoldemRules.PubStartingChips, HandsPerLevel = HoldemRules.PubHandsPerLevel,
                        Payouts = new[] { new Prize(PrizeType.Seat, 1, 1) },
                        Reentry = new ReentryRule { Allowed = true, UntilLevel = HoldemRules.ReentryUntilLevel, Cost = HoldemRules.SatelliteFee },
                        StudyLevel = studyLevel,
                        Roster = MakeRoster(2, 2, 2, 2),
                        EntryFee = HoldemRules.SatelliteFee,
                    };
                default:
                    var prizes = new Prize[HoldemRules.MainPrizes.Length];
                    for (int i = 0; i < prizes.Length; i++)
                    {
                        long gross = HoldemRules.MainPrizes[i];
                        prizes[i] = new Prize(PrizeType.Cash, gross, gross - (long)System.Math.Round(gross * HoldemRules.MainTaxRate));
                    }
                    return new TournamentConfig
                    {
                        Kind = kind, StartingChips = HoldemRules.MainStartingChips, HandsPerLevel = HoldemRules.MainHandsPerLevel,
                        Payouts = prizes,
                        Reentry = new ReentryRule { Allowed = false },
                        StudyLevel = studyLevel,
                        Roster = MakeRoster(2, 1, 1, 4),
                        FieldSize = HoldemRules.MainFieldSize,
                    };
            }
        }

        static PersonalityId[] MakeRoster(int tight, int station, int maniac, int pro)
        {
            var list = new System.Collections.Generic.List<PersonalityId>();
            for (int i = 0; i < tight; i++) list.Add(PersonalityId.Tight);
            for (int i = 0; i < station; i++) list.Add(PersonalityId.Station);
            for (int i = 0; i < maniac; i++) list.Add(PersonalityId.Maniac);
            for (int i = 0; i < pro; i++) list.Add(PersonalityId.Pro);
            return list.ToArray();
        }
    }

    public sealed class TournamentResult
    {
        public int Finish;
        public Prize Prize;
        public int HandsPlayed;
        public int Reentries;
        public int BustedAtLevel;   // 1-based; 0 if not busted (won)
        public int BadBeats;
        public bool BubbleReached;
        // v2 (DESIGN.md C-v2): the ladder model fills these; Finish is 1 (champion) / 2-3 (podium draw) / 0 otherwise
        public LadderStage Stage;
        public int Wins;
        public int Steals, FreeBlinds, Folds, CalledAllIns, CalledWins;
        public int LuckWins;                          // E2: called all-ins turned into wins by "무조건 승리"
        public int BbFacing, BbCalls;                 // BB hands that faced a steal shove / that the hero called
        public System.Collections.Generic.List<double> BustBb = new System.Collections.Generic.List<double>(); // stack (bb) before each fatal hand
        public System.Collections.Generic.List<string> Log = new System.Collections.Generic.List<string>();
    }
}
