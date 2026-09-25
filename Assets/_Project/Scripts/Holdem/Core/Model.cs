namespace AllIn.Holdem
{
    public enum Street { Preflop = 0, Flop, Turn, River, Showdown }

    public enum ActionType { Fold, Check, Call, Bet, Raise, AllIn }

    /// <summary><see cref="To"/> is the player's total bet on this street after the action (bet / raise only).</summary>
    public readonly struct PlayerAction
    {
        public readonly ActionType Type;
        public readonly long To;

        public PlayerAction(ActionType type, long to = 0)
        {
            Type = type;
            To = to;
        }

        public static PlayerAction Fold => new PlayerAction(ActionType.Fold);
        public static PlayerAction Check => new PlayerAction(ActionType.Check);
        public static PlayerAction Call => new PlayerAction(ActionType.Call);
        public static PlayerAction AllIn => new PlayerAction(ActionType.AllIn);
        public static PlayerAction BetTo(long to) => new PlayerAction(ActionType.Bet, to);
        public static PlayerAction RaiseTo(long to) => new PlayerAction(ActionType.Raise, to);

        public override string ToString() => Type + (To > 0 ? " " + To : "");
    }

    /// <summary>What one player may do right now.</summary>
    public readonly struct Legal
    {
        public readonly long ToCall;       // chips needed to call (capped at stack)
        public readonly bool CanCheck;
        public readonly bool CanRaise;     // bet or raise; false when a short all-in didn't reopen the action
        public readonly long MinRaiseTo;   // smallest legal bet/raise total (all-in for less is always allowed)
        public readonly long MaxTo;        // street bet + stack
        public readonly bool IsBet;        // nobody has bet yet this street

        public Legal(long toCall, bool canCheck, bool canRaise, long minRaiseTo, long maxTo, bool isBet)
        {
            ToCall = toCall;
            CanCheck = canCheck;
            CanRaise = canRaise;
            MinRaiseTo = minRaiseTo;
            MaxTo = maxTo;
            IsBet = isBet;
        }
    }

    public enum EventKind
    {
        HandStart, PostBlind, HoleCards, Action, Board, Runout, Show, PotWon, HandEnd,
        LevelUp, Busted, Reentry, Refill, FieldCount, Bubble, BadBeat, FinalTable, TournamentEnd,
    }

    /// <summary>Everything the presentation replays, in order. Generic payload fields; see <see cref="Kind"/>.</summary>
    public sealed class HoldemEvent
    {
        public EventKind Kind;
        public int Seat = -1;
        public long Amount;
        public long Amount2;
        public ActionType Action;
        public Street Street;
        public int[] Cards;
        public int Score;
        public string Text;

        public override string ToString() =>
            Kind + (Seat >= 0 ? " s" + Seat : "") + (Kind == EventKind.Action ? " " + Action : "") +
            (Amount != 0 ? " " + Amount : "") + (Amount2 != 0 ? "/" + Amount2 : "") +
            (Cards != null ? " [" + string.Join(" ", System.Array.ConvertAll(Cards, Card.ToText)) + "]" : "") +
            (Text != null ? " " + Text : "");
    }
}
