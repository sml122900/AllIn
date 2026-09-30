using System;
using System.Collections.Generic;

namespace AllIn.Core
{
    // Plain data, JsonUtility-friendly: public fields only, no null objects (JsonUtility can't express them —
    // hence hasPosition + position), no dictionaries, no jagged arrays.

    [Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 2;
        public int version = SaveData.CurrentVersion;
        public long seed;
        public int[] marketRngState = new int[4]; // coin price path only, so strategies see the same market
        public int[] rngState = new int[4];       // everything else (lotto numbers, job mistakes, flavor)
        public int[] subTickRngState = new int[4]; // sub-tick decomposition of the slot's price move, separate from marketRngState
        public int[] luckRngState = new int[4];    // v2: "무조건 승리" rolls only (never drawn while q = 0); all zero in v1 saves -> derived from seed

        public int body;
        public int bodyMax = Rules.BodyMaxStart;   // D4: shrinks by 15 per voyage (floor 40); body is clamped to it
        public int mental;
        public int day;
        public Slot slot;
        public Room room;
        public bool finished;

        // money (B5 v0.3): three pools, no debt. Bank <- wages/rent/food/lotto. Overseas <- futures margin only.
        public long bankCash;
        public long domesticCash;
        public long overseasCash;
        public int domesticLockUntilAbsSlot = -1;              // absolute slot; -1 = unlocked. Re-set (not extended) on each deposit.
        public int lastOverseasArrivalAbsSlot = -1;             // last time a domestic->overseas transfer landed (B5d "강화 변형" window)
        public bool peekedExchangeAtNight;                      // opened coin/exchange screen at night while locked -> "잠 못 듦" this night
        public List<PendingTransfer> pendingTransfers = new List<PendingTransfer>();

        // coin
        public long price;                                  // whole won
        public List<long> priceHistory = new List<long>();
        public bool hasPosition;
        public Position position = new Position();
        public int lastLeverage = Rules.DefaultLeverage;
        public bool hasLastWin;
        public LastWin lastWin = new LastWin();
        public int hopeShownDay;
        public int workStreak;   // consecutive mistake-free worked days (MentalTuning streak bonus)
        public int lastWorkDay;
        public bool tilted;              // B5b: from liquidation until the next successful sleep
        public long lastLiquidationLoss; // committed + addedMargin lost, for the tilt recovery calculator
        public List<Ghost> ghosts = new List<Ghost>(); // B5d: tracked liquidated positions, up to 50 slots each

        // lotto
        public List<LottoTicket> tickets = new List<LottoTicket>();
        public int lottoBoughtToday;
        public int lottoTicketSerial;

        // job
        public int firedUntilDay;

        // life (D, v0.4)
        public int starveStreak, lastStarveDay;                     // consecutive hungry days (3 -> one popup)
        public long rentOwed;                                      // D3: unpaid rent from the day the bank could not cover it (0 = not bankrupt)
        public int nextRentDay = 1 + Rules.RentIntervalDays;      // D2: next Day on which rent is charged (at dawn)
        public int roomRecheckDay;                                  // D4: room is not re-judged before this Day (0 = always)
        public int bankruptSinceDay;                                // D3: 0 = not bankrupt; else the Day the state began
        public int healthDebt;                                      // D4/D5: +1 per voyage, -1 per 10 checkups
        public int checkupProgress;                                 // D5: checkups since the last debt relief (only counts while debt > 0)
        public int nextHealthCheckDay = 1 + Rules.HealthCheckIntervalDays; // D5: next illness roll (dawn)
        public bool atSea;                                          // D4: true only while a voyage is being simulated; every action refuses

        // v0.5 (DESIGN.md E5–E10): style, street position, car, companions. Field initializers are the v1-save defaults.
        public Place place;                                         // E6: last building visited (Home at the start); moving costs no slot
        public Appearance appearance = new Appearance();
        public List<int> ownedClothes = new List<int> { Wardrobe.StarterTop, Wardrobe.StarterBottom, Wardrobe.StarterShoes };
        public int carTier;                                         // E8: 0 = none, 1..4
        public List<Companion> companions = new List<Companion>();  // E9: picture slots only

        // scheduling
        public int lostUntilSlot = -1;   // absolute slot index; slots before it are lost ("can't get up")
        public int e3AtSlot = -1;        // absolute slot index at which E3 must play

        public List<GameEvent> pendingEvents = new List<GameEvent>();
        public List<string> log = new List<string>();
        public Stats stats = new Stats();
    }

    public enum Direction { Long = 1, Short = -1 }

    [Serializable]
    public class Position
    {
        public Direction direction = Direction.Long;
        public int leverage;
        public long committed;      // C: cash taken when opening
        public long margin;         // M = C - entry fee
        public long addedMargin;    // ΣA added mid-slot (B5a); notional/quantity stay fixed, only the liquidation price moves
        public long entryPrice;     // quantity is derived: CoinRules.Quantity
        public int openedAtSlot;    // absolute slot
        public int watchShownAtSlot = -1; // absolute slot the watch scene last fired for this position (once per slot)
    }

    public enum TransferKind { DomesticToOverseas, OverseasToDomestic, DomesticToBank }

    [Serializable]
    public class PendingTransfer
    {
        public long amount;
        public TransferKind kind;
        public int arrivalAbsSlot;
    }

    [Serializable]
    public class Ghost
    {
        public Direction direction;
        public int leverage;
        public long committed;
        public long addedMargin;
        public long entryPrice;
        public int liqAbsSlot;
        public int expiryAbsSlot;
        public bool enhanced;
        public long peakFavorablePnl;
    }

    [Serializable]
    public class LastWin
    {
        public long realized;
        public long committed;
        public int heldTicks;
    }

    [Serializable]
    public class LottoTicket
    {
        public int serial;
        public int drawDay;
        public int[] numbers = new int[Rules.LottoGamesPerTicket * Rules.LottoPicks]; // 5 games x 6, each game sorted
    }

    [Serializable]
    public class Stats
    {
        public int workedSlots;
        public int liquidations;
        public long coinRealized;
        public long lottoSpent;
        public long lottoWon;              // after tax
        public int[] lottoRanks = new int[6]; // index = rank (0 unused)
        public long maxAssets;
        public long minAssets;
        public int hopeCircuits;
        public List<string> eventsSeen = new List<string>();
        public int timesFired;

        // D (v0.4) life
        public int voyages;
        public int illnesses;
        public long hospitalPaid;
        public int checkups;
        public int voyageLiquidations;     // liquidated while the position was left open at sea

        // B5 v0.3 liquidation chain
        public int prayers;
        public int luckUses;                 // E1: "무조건 승리" pressed (coin)
        public int rageBets;
        public long ghostMissedMoney;
        public int watchesShown;
        public int tailLiquidations;         // liquidated mid-path though the slot's close price alone wouldn't have
        public int liquidationsDuringLock;    // liquidated while the domestic->overseas transfer was locked
        public int ghostFired;
        public int ghostFiredEnhanced;
        public List<int> ghostResolutionSlots = new List<int>(); // slots between liquidation and each E8 firing (report-only)

        // schedule bookkeeping (tests / sims)
        public int rentPaid;
        public int foodPaid;
        public int starved;
        public int lottoDraws;
        public int slotsPlayed;
    }

    /// <summary>A thing the presentation layer must show, in order. Generic payload so it serializes.</summary>
    [Serializable]
    public class GameEvent
    {
        public string id;            // EventIds.*
        public long amount;
        public long amount2;
        public long amount3;
        public int[] ints = new int[0];
        public int[] ints2 = new int[0];
        public int count;
    }

    public static class EventIds
    {
        public const string Opening = "OPENING";
        public const string FirstPopup = "FIRST";
        public const string LiquidationDay = "E1";
        public const string LiquidationNight = "E2";
        public const string SelfPity = "E3";
        public const string HopeCircuit = "E4";
        public const string LottoDraw = "E5";
        public const string MoveRoom = "E6";
        public const string Settlement = "E7";
        public const string Ghost = "E8";
        public const string Bankrupt = "E9";
        public const string Sick = "SICK";
        public const string Hunger = "HUNGER";
        public const string VoyageReturn = "VOYAGE_RETURN";
        public const string Fired = "FIRED";
        public const string CantGetUp = "CANT_GET_UP";

        /// <summary>Scenes that count for the end-of-run "scenes witnessed" checklist.</summary>
        public static readonly string[] Codex = { LiquidationDay, LiquidationNight, SelfPity, HopeCircuit, LottoDraw, MoveRoom };
    }
}
