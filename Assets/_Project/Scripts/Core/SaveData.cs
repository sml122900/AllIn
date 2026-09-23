using System;
using System.Collections.Generic;

namespace AllIn.Core
{
    // Plain data, JsonUtility-friendly: public fields only, no null objects (JsonUtility can't express them —
    // hence hasPosition + position), no dictionaries, no jagged arrays.

    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public long seed;
        public int[] marketRngState = new int[4]; // coin price path only, so strategies see the same market
        public int[] rngState = new int[4];       // everything else (lotto numbers, job mistakes, flavor)

        public long cash;
        public int body;
        public int mental;
        public int day;
        public Slot slot;
        public Room room;
        public bool finished;

        // coin
        public double price;
        public List<double> priceHistory = new List<double>();
        public bool hasPosition;
        public Position position = new Position();
        public int lastLeverage = Rules.DefaultLeverage;
        public bool hasLastWin;
        public LastWin lastWin = new LastWin();
        public int hopeShownDay;

        // lotto
        public List<LottoTicket> tickets = new List<LottoTicket>();
        public int lottoBoughtToday;
        public int lottoTicketSerial;

        // job
        public int firedUntilDay;

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
        public double entryPrice;
        public double quantity;     // notional / entryPrice
        public int openedAtSlot;    // absolute slot
    }

    [Serializable]
    public class LastWin
    {
        public double returnOnCommitted;
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

        // schedule bookkeeping (tests / sims)
        public int rentPaid;
        public int foodPaid;
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
        public double value;
        public double value2;
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
        public const string Fired = "FIRED";
        public const string CantGetUp = "CANT_GET_UP";

        /// <summary>Scenes that count for the end-of-run "scenes witnessed" checklist.</summary>
        public static readonly string[] Codex = { LiquidationDay, LiquidationNight, SelfPity, HopeCircuit, LottoDraw, MoveRoom };
    }
}
