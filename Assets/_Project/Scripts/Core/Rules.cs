namespace AllIn.Core
{
    /// <summary>
    /// Every loop number lives here and mirrors md/DESIGN.md (Part B). The doc wins on conflict:
    /// change DESIGN.md first, then this file, then log the reason in md/STATUS.md.
    /// </summary>
    public static class Rules
    {
        // B1 time
        public const int SlotsPerDay = 5;
        public const int Days = 30;
        public const int TotalSlots = SlotsPerDay * Days;

        // B2 state
        public const int BodyStart = 80;
        public const int MentalStart = 60;
        public const int GaugeMin = 0;
        public const int GaugeMax = 100;
        public const long RoomOneRoomAt = 3_000_000;
        public const long RoomOfficetelAt = 100_000_000;

        // B3 start / fixed costs
        public const long StartingCash = 20_000_000;
        public const long Rent = 450_000;
        public const long FoodPerDay = 12_000;

        // B4 restaurant job
        public const long RestaurantWage = 51_600; // 10,320 x 5h
        public const int RestaurantHours = 5;
        public const int RestaurantBodyCost = 12;
        public const int RestaurantMentalCost = 4;
        public const int RestaurantMinBody = 25;
        public const int MistakeMentalBelow = 30;
        public const double MistakeChance = 0.30;
        public const int FiredDays = 3;
        public const int FiredMental = 10;

        // B5 coin futures
        public const long CoinStartPrice = 100_000_000; // whole won: every saved number is integral so JSON saves round-trip exactly
        public const double CoinTickScale = 0.010;
        public const double CoinTickCap = 0.25;
        public static readonly int[] Leverages = { 5, 10, 25, 50, 100 };
        public const int DefaultLeverage = 10;
        public const double FeeRate = 0.0005;
        public const double LiquidationRatio = 0.9;
        public const int PriceHistoryLength = 60;

        // B6 lotto
        public const long LottoPrice = 5_000;
        public const int LottoGamesPerTicket = 5;
        public const int LottoMaxPerDay = 20;
        public const int LottoMaxNumber = 45;
        public const int LottoPicks = 6;
        public static readonly long[] LottoPrize = { 0, 2_000_000_000, 50_000_000, 1_500_000, 50_000, 5_000 }; // index = rank
        public const long LottoTaxFreeUpTo = 2_000_000;
        public const long LottoMidTaxUpTo = 300_000_000;
        public const double LottoMidTax = 0.22;
        public const double LottoHighTax = 0.33;
        public const int LottoCrowdWinnersBase = 12;
        public const int LottoCrowdWinnersSpread = 5;

        // B7 rest / sleep
        public const int RestBody = 5;
        public const int SleepBody = 30;
        public const int SleepMental = 3;
        public const int AllNighterMental = 5;

        // B8 gauges
        public const int LiquidationMental = 40;
        public const int CloseLossMentalMin = 5, CloseLossMentalMax = 25;
        public const int CloseGainMentalMin = 5, CloseGainMentalMax = 30;
        public const int LottoMissMental = 1;
        public static readonly int[] LottoRankMental = { 0, 30, 30, 30, 5, 1 }; // index = rank
        public const int ChasingLossesBelow = 30;     // "본전 생각"
        public const double ChasingMinMarginOfCash = 0.25;
        public const int CantGetUpBelow = 15;          // lose dawn + morning
        public const int HopeCircuitAt = 80;           // "희망회로"

        // B10 end
        public const long BankruptBelow = 5_000;

        // B5 v0.3 — liquidation chain: wallets, sub-tick path, watch/tilt/lock/ghost
        public const int SubTicksPerSlot = 12;
        public static readonly double SubTickScale = CoinTickScale / System.Math.Sqrt(SubTicksPerSlot);
        public const double WatchLossRatio = 0.7;         // loss / (margin+addedMargin) that triggers the watch scene
        public const int DomesticLockSlots = 5;            // 24h, re-set (not extended) on every bank->domestic deposit
        public const int TransferDelaySlots = 1;            // overseas->domestic, domestic->bank
        public const int GhostTrackSlots = 50;              // 10 days
        public const int GhostEnhancedWindowSlots = 5;      // "강화 변형": E8 within this many slots of a domestic->overseas arrival
        public const int GhostMental = 15;
        public const int GhostMentalEnhanced = 25;
        public const int TiltDefaultLeverage = 100;
        public const int BadSleepBody = 15;                 // half of SleepBody; mental stays +0
    }
}
