using System;
using System.Collections;
using System.Collections.Generic;
using AllIn.Core;
using AllIn.Game.UI;
using AllIn.Holdem;
using AllIn.Holdem.Brain;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AllIn.Game.HoldemScene
{
    /// <summary>
    /// Scene entry for Holdem.unity (runs standalone, no 30-day loop). Boot menu → a tournament replayed from the engine's
    /// event stream with delays → result. All rules live in AllIn.Holdem; this only draws and asks the hero.
    /// </summary>
    public sealed class HoldemApp : MonoBehaviour
    {
        public const string SceneName = "Holdem";

        // v1 (9-seat full table): no longer launched — LadderApp owns the Holdem scene. Kept compiling for reference.

        /// <summary>Dev capture: when set, a bot answers for the hero and this is called whenever the hero would be asked.</summary>
        public IDecider AutoHero;
        public Func<string, IEnumerator> OnMoment;

        public Tournament Tournament { get; private set; }
        public bool AtResult { get; private set; }

        Canvas _canvas;
        RectTransform _table, _menu, _controls, _pushFoldPanel, _overlay;
        TextMeshProUGUI _info, _potText, _banner, _pushFoldText, _menuSeed;
        readonly SeatView[] _seats = new SeatView[HoldemRules.Seats];
        readonly CardView[] _board = new CardView[5];
        Image _blackout;
        TournamentKind _kind = TournamentKind.Pub;
        int _study;
        long _seed;
        Ui.Btn[] _kindButtons, _studyButtons;

        // controls
        Ui.Btn _fold, _call, _raise;
        Ui.Btn[] _presets;
        Slider _slider;
        PlayerAction? _heroChoice;
        long _raiseTo;

        // replay model (what the player has seen so far)
        readonly long[] _stack = new long[HoldemRules.Seats];
        readonly long[] _bet = new long[HoldemRules.Seats];
        long _pot;
        bool _runout;
        int _eventCursor;

        // tempo (decision C): hands the hero is out of play instantly, except all-in showcases unless "quick" is on
        bool _heroInHand, _showcase, _quick;
        Ui.Btn _quickButton;
        TextMeshProUGUI _log;
        readonly List<string> _logLines = new List<string>();
        readonly List<int> _boardCards = new List<int>();
        readonly List<int> _revealed = new List<int>();
        readonly Xoshiro128 _uiRng = new Xoshiro128(0xE0E0); // presentation-only equity; never touches the tournament's stream

        void Awake()
        {
            Fonts.Ensure();
            Ui.EnsureEventSystem();
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.07f, 0.06f);

            _canvas = Ui.Canvas("HoldemUI", 0);
            BuildTable();
            BuildControls();
            BuildMenu();
            _seed = LoopScene.LoopApp.NewSeed() % 1_000_000;
            ShowMenu();
        }

        // ================= menu (C7 boot) =================

        void BuildMenu()
        {
            _menu = Ui.Image(_canvas.transform, "Menu", new Color(0.04f, 0.05f, 0.05f, 1f), raycast: true).rectTransform;
            Ui.Text(_menu, Texts.HoldemMenuTitle, 40, Palette.Accent, TextAlignmentOptions.Center, true).rectTransform.Place(0, 1, 1, 1, 0, -110, 0, 40);

            _kindButtons = new Ui.Btn[3];
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                _kindButtons[i] = Ui.Button(_menu, Texts.HoldemKindNames[i], () => { _kind = (TournamentKind)k; RefreshMenu(); }, 26);
                _kindButtons[i].Rect.Box(0.5f, 0.5f, 280, 70, -300 + i * 300, 150);
            }
            Ui.Text(_menu, Texts.HoldemStudy, 24, Palette.Dim, TextAlignmentOptions.Center).rectTransform.Box(0.5f, 0.5f, 600, 40, 0, 70);
            _studyButtons = new Ui.Btn[5];
            for (int i = 0; i < 5; i++)
            {
                int s = i;
                _studyButtons[i] = Ui.Button(_menu, Texts.HoldemStudyNames[i], () => { _study = s; RefreshMenu(); }, 20);
                _studyButtons[i].Rect.Box(0.5f, 0.5f, 220, 56, -480 + i * 240, 15);
            }
            _menuSeed = Ui.Text(_menu, "", 26, align: TextAlignmentOptions.Center);
            _menuSeed.rectTransform.Box(0.5f, 0.5f, 400, 50, 0, -70);
            Ui.Button(_menu, "−", () => { _seed = Math.Max(0, _seed - 1); RefreshMenu(); }, 30).Rect.Box(0.5f, 0.5f, 60, 50, -260, -70);
            Ui.Button(_menu, "+", () => { _seed++; RefreshMenu(); }, 30).Rect.Box(0.5f, 0.5f, 60, 50, 260, -70);
            Ui.Button(_menu, Texts.HoldemRandomSeed, () => { _seed = LoopScene.LoopApp.NewSeed() % 1_000_000; RefreshMenu(); }, 22).Rect.Box(0.5f, 0.5f, 120, 50, 380, -70);
            Ui.Button(_menu, Texts.HoldemStart, () => StartTournament(_kind, _study, _seed), 32).Rect.Box(0.5f, 0.5f, 320, 80, 0, -190);
        }

        void RefreshMenu()
        {
            for (int i = 0; i < 3; i++) _kindButtons[i].SetOn((int)_kind == i);
            for (int i = 0; i < 5; i++) _studyButtons[i].SetOn(_study == i);
            _menuSeed.text = Texts.HoldemSeed + " " + _seed;
        }

        void ShowMenu()
        {
            StopAllCoroutines();
            AtResult = false;
            ClearOverlay();
            _menu.gameObject.SetActive(true);
            _menu.SetAsLastSibling();
            RefreshMenu();
        }

        /// <summary>Also the dev-capture entry point.</summary>
        public void StartTournament(TournamentKind kind, int study, long seed)
        {
            _kind = kind;
            _study = study;
            _seed = seed;
            var cfg = TournamentConfig.For(kind, study);
            Tournament = new Tournament(cfg, new Xoshiro128((ulong)seed), BotFactory.Instance) { StopWhenHeroOut = true };
            _menu.gameObject.SetActive(false);
            ClearOverlay();
            AtResult = false;
            _eventCursor = 0;
            _logLines.Clear();
            _log.text = "";
            for (int i = 0; i < _seats.Length; i++) { _stack[i] = cfg.StartingChips; _bet[i] = 0; }
            _pot = 0;
            StartCoroutine(Run());
        }

        // ================= table =================

        const float TableCx = 0, TableCy = 50, Rx = 520, Ry = 210;

        void BuildTable()
        {
            _table = Ui.Rect(_canvas.transform, "Table");
            var felt = Ui.Image(_table, "Felt", new Color(0.10f, 0.32f, 0.20f)).rectTransform.Box(0.5f, 0.5f, 2 * Rx - 120, 2 * Ry - 40, TableCx, TableCy);
            Ui.Image(felt, "Rail", new Color(0, 0, 0, 0)).rectTransform.Place(0, 0, 1, 1);

            _info = Ui.Text(_table, "", 17, Palette.Text, TextAlignmentOptions.TopLeft);
            _info.rectTransform.Place(0, 1, 0, 1, 16, -80, -420, 10);

            _potText = Ui.Text(_table, "", 26, Palette.Accent, TextAlignmentOptions.Center, true);
            _potText.rectTransform.Box(0.5f, 0.5f, 400, 40, TableCx, TableCy + 70);
            for (int i = 0; i < 5; i++)
            {
                _board[i] = CardView.Create(_table, 60, 84);
                _board[i].Root.Box(0.5f, 0.5f, 60, 84, TableCx - 136 + i * 68, TableCy + 10);
                _board[i].Hide();
            }

            for (int i = 0; i < _seats.Length; i++)
            {
                double a = (-90 - i * 40) * Math.PI / 180;
                float x = TableCx + (float)(Math.Cos(a) * Rx), y = TableCy + (float)(Math.Sin(a) * Ry);
                // bets sit between the seat and the pot; the hero's go above their (larger) cards
                float k = i == HoldemRules.HeroSeat ? 0.3f : 0.6f;
                float bx = TableCx + (float)(Math.Cos(a) * Rx * k), by = TableCy + (float)(Math.Sin(a) * Ry * (i == HoldemRules.HeroSeat ? 0.3f : 0.55f));
                _seats[i] = SeatView.Create(_table, i == HoldemRules.HeroSeat, new Vector2(x, y), new Vector2(bx, by));
            }

            _pushFoldPanel = Ui.Image(_table, "PushFold", new Color(0, 0, 0, 0.6f)).rectTransform;
            _pushFoldPanel.Place(1, 0, 1, 0, -210, 112, 10, -252);
            Ui.Text(_pushFoldPanel, Texts.HoldemPushFold, 20, Palette.Accent, TextAlignmentOptions.TopLeft, true).rectTransform.Place(0, 1, 1, 1, 12, -34, 12, 8);
            _pushFoldText = Ui.Text(_pushFoldPanel, "", 16, align: TextAlignmentOptions.TopLeft);
            _pushFoldText.rectTransform.Place(0, 0, 1, 1, 12, 8, 12, 36);

            _banner = Ui.Text(_table, "", 40, Palette.Accent, TextAlignmentOptions.Center, true);
            _banner.rectTransform.Box(0.5f, 0.5f, 900, 120, 0, TableCy + 150);

            _log = Ui.Text(_table, "", 15, Palette.Dim, TextAlignmentOptions.BottomLeft);
            _log.rectTransform.Place(0, 0, 0, 0, 12, 112, -430, -300);
            _log.overflowMode = TextOverflowModes.Truncate;
            _quickButton = Ui.Button(_table, "", () => { _quick = !_quick; RefreshQuick(); }, 18);
            _quickButton.Rect.Place(1, 1, 1, 1, -170, -52, 12, 12);
            RefreshQuick();

            _overlay = Ui.Rect(_canvas.transform, "Overlay");
            _blackout = Ui.Image(_canvas.transform, "Blackout", new Color(0, 0, 0, 0));
        }

        void BuildControls()
        {
            _controls = Ui.Image(_canvas.transform, "Controls", Palette.Panel, raycast: true).rectTransform;
            _controls.Place(0, 0, 1, 0, 0, 0, 0, -104);
            _fold = Ui.Button(_controls, Texts.HoldemFold, () => _heroChoice = PlayerAction.Fold, 26);
            _fold.Rect.Place(0, 0, 0, 1, 14, 14, -174, 14);
            _call = Ui.Button(_controls, Texts.HoldemCall, () => _heroChoice = PlayerAction.Call, 24);
            _call.Rect.Place(0, 0, 0, 1, 188, 14, -438, 14);
            _presets = new Ui.Btn[4];
            float[] fr = { 0.5f, 2f / 3, 1f, -1f };
            for (int i = 0; i < 4; i++)
            {
                float f = fr[i];
                _presets[i] = Ui.Button(_controls, Texts.HoldemPresets[i], () => SetPreset(f), 20);
                float x = 452 + i * 104;
                _presets[i].Rect.Place(0, 1, 0, 1, x, -48, -(x + 96), 10);
            }
            _slider = Ui.Slider(_controls, v => OnSlider(v));
            ((RectTransform)_slider.transform).Place(0, 0, 0, 0, 452, 12, -860, -44);
            _raise = Ui.Button(_controls, Texts.HoldemRaise, () => _heroChoice = RaiseChoice(), 24);
            _raise.Rect.Place(1, 0, 1, 1, -400, 14, 14, 14);
            _controls.gameObject.SetActive(false);
        }

        // ================= replay loop =================

        IEnumerator Run()
        {
            var t = Tournament;
            AtResult = false;
            ResetTableVisuals();
            while (true)
            {
                while (_eventCursor < t.Events.Count)
                    yield return PlayEvent(t.Events[_eventCursor++]);
                if (t.Finished) break;

                var step = t.Step();
                if (step == StepResult.NeedHeroAction)
                {
                    while (_eventCursor < t.Events.Count) yield return PlayEvent(t.Events[_eventCursor++]);
                    yield return HeroTurn();
                }
                else if (step == StepResult.NeedHeroReentry)
                {
                    while (_eventCursor < t.Events.Count) yield return PlayEvent(t.Events[_eventCursor++]);
                    yield return ReentryPrompt();
                }
            }
            yield return ShowResult();
        }

        IEnumerator HeroTurn()
        {
            var t = Tournament;
            var h = t.Hand;
            var legal = h.LegalFor(HoldemRules.HeroSeat);
            SetupControls(h, legal);
            RefreshPushFold();
            _seats[HoldemRules.HeroSeat].SetActive(true);
            if (OnMoment != null) yield return OnMoment("hero_" + h.Street);
            _heroChoice = null;
            if (AutoHero != null) _heroChoice = AutoHero.Decide(t, HoldemRules.HeroSeat);
            while (_heroChoice == null) yield return null;
            _controls.gameObject.SetActive(false);
            _seats[HoldemRules.HeroSeat].SetActive(false);
            t.HeroAct(_heroChoice.Value);
        }

        IEnumerator ReentryPrompt()
        {
            int answer = -1;
            var panel = Popup(Texts.HoldemReentryPrompt(Tournament.Config.Reentry.Cost), null);
            Ui.Button(panel, Texts.Yes, () => answer = 1, 26).Rect.Place(0.1f, 0.1f, 0.45f, 0.4f);
            Ui.Button(panel, Texts.No, () => answer = 0, 26).Rect.Place(0.55f, 0.1f, 0.9f, 0.4f);
            if (OnMoment != null) yield return OnMoment("reentry");
            if (AutoHero != null) answer = 1;
            while (answer < 0) yield return null;
            ClearOverlay();
            Tournament.HeroReentry(answer == 1);
        }

        // ================= events =================

        /// <summary>Plays an event with its pauses, or applies it at once when the hero has no stake in the hand.</summary>
        IEnumerator PlayEvent(HoldemEvent e)
        {
            if (e.Kind == EventKind.Reveal) _showcase = true;
            var it = Play(e);
            if (!Instant(e))
            {
                yield return it;
                yield break;
            }
            Drain(it);
        }

        bool Instant(HoldemEvent e)
        {
            if (_heroInHand) return false;
            switch (e.Kind)
            {
                case EventKind.Reveal:
                case EventKind.Board:
                case EventKind.Show:
                case EventKind.PotWon:
                    return !_showcase || _quick; // an all-in is worth watching, unless the player asked for speed
                case EventKind.PostBlind:
                case EventKind.HoleCards:
                case EventKind.Action:
                case EventKind.Runout:
                case EventKind.HandEnd:
                    return true;
                default:
                    return false; // level-ups, busts, bubble, merges, bad beats always play
            }
        }

        /// <summary>Runs a presentation coroutine to completion in one go, skipping its waits.</summary>
        static void Drain(IEnumerator it)
        {
            while (it.MoveNext())
                if (it.Current is IEnumerator inner) Drain(inner);
        }

        void RefreshQuick() => _quickButton.Label.text = Texts.HoldemQuick + (_quick ? " ON" : " OFF");

        void Log(string line)
        {
            _logLines.Add(line);
            if (_logLines.Count > 6) _logLines.RemoveAt(0);
            _log.text = string.Join("\n", _logLines);
        }

        /// <summary>One line per hand: who won how much from whom.</summary>
        void LogHandSummary()
        {
            var t = Tournament;
            var h = t.Hand;
            var winners = new List<string>();
            var losers = new List<string>();
            for (int s = 0; s < HandState.Seats; s++)
            {
                if (!h.InHand[s]) continue;
                long net = h.Won[s] - h.Committed[s];
                if (net > 0) winners.Add(t.Seats[s].Name + " +" + net.ToString("N0"));
                else if (net < 0) losers.Add(t.Seats[s].Name);
            }
            if (winners.Count > 0) Log("#" + t.HandNumber + " " + string.Join(", ", winners) + (losers.Count > 0 ? " ← " + string.Join("·", losers) : ""));
        }

        /// <summary>Study level ≥ 3: each revealed hand's equity on the current board (exact on the turn, Monte Carlo before).</summary>
        void ShowEquities()
        {
            if (_study < 3 || _revealed.Count < 2 || _boardCards.Count >= 5) return;
            var h = Tournament.Hand;
            var hands = new int[_revealed.Count][];
            for (int i = 0; i < hands.Length; i++) hands[i] = new[] { h.Hole0[_revealed[i]], h.Hole1[_revealed[i]] };
            var board = new int[5];
            for (int i = 0; i < _boardCards.Count; i++) board[i] = _boardCards[i];
            for (int i = 0; i < hands.Length; i++)
            {
                double eq = Equity.VsKnown(hands, i, board, _boardCards.Count, _uiRng, 1000);
                _seats[_revealed[i]].SetAction(Texts.HoldemEquity(eq));
            }
        }

        IEnumerator Play(HoldemEvent e)
        {
            var t = Tournament;
            switch (e.Kind)
            {
                case EventKind.HandStart:
                    ResetTableVisuals();
                    var h = t.Hand;
                    for (int s = 0; s < _seats.Length; s++)
                    {
                        var who = t.Seats[s];
                        _stack[s] = h.InHand[s] ? h.StartStack[s] : 0;
                        _seats[s].SetOccupant(who != null && (who.Alive || h.InHand[s]) ? who.Name : null);
                    }
                    PaintPositions(h);
                    RefreshInfo();
                    _heroInHand = h.InHand[HoldemRules.HeroSeat];
                    _showcase = false;
                    _boardCards.Clear();
                    _revealed.Clear();
                    yield return Wait(0.35f);
                    break;
                case EventKind.PostBlind:
                    Put(e.Seat, e.Amount);
                    RefreshSeat(e.Seat);
                    break;
                case EventKind.HoleCards:
                    if (e.Seat == HoldemRules.HeroSeat) _seats[e.Seat].ShowCards(e.Cards[0], e.Cards[1]);
                    else _seats[e.Seat].ShowBacks();
                    if (e.Seat == LastHoleSeat()) yield return Wait(0.3f);
                    break;
                case EventKind.Action:
                    Put(e.Seat, e.Amount2);
                    _seats[e.Seat].SetAction(ActionLabel(e));
                    if (e.Action == ActionType.Fold) _seats[e.Seat].Fold();
                    if (e.Action == ActionType.Fold && e.Seat == HoldemRules.HeroSeat) _heroInHand = false;
                    RefreshSeat(e.Seat);
                    RefreshPot();
                    yield return Wait(e.Seat == HoldemRules.HeroSeat ? 0.15f : 0.5f);
                    break;
                case EventKind.Runout:
                    _runout = true;
                    break;
                case EventKind.Reveal:
                    CollectBets();
                    _revealed.Add(e.Seat);
                    _seats[e.Seat].ShowCards(e.Cards[0], e.Cards[1]);
                    bool lastReveal = _eventCursor >= t.Events.Count || t.Events[_eventCursor].Kind != EventKind.Reveal;
                    if (lastReveal)
                    {
                        ShowEquities();
                        if (OnMoment != null) yield return OnMoment(_boardCards.Count < 5 ? "allin_reveal_before_river" : "allin_reveal_on_river");
                        yield return Wait(0.8f);
                    }
                    break;
                case EventKind.Board:
                    CollectBets();
                    bool river = e.Street == Street.River;
                    float delay = river ? (_runout ? HoldemRules.RiverDelayAllIn : HoldemRules.RiverDelay)
                                : e.Street == Street.Turn && _runout ? HoldemRules.TurnDelayAllIn : 0.55f;
                    if (river && _runout) yield return Shake(delay);
                    else yield return Wait(delay);
                    int first = e.Street == Street.Flop ? 0 : e.Street == Street.Turn ? 3 : 4;
                    for (int i = 0; i < e.Cards.Length; i++) _board[first + i].SetCard(e.Cards[i]);
                    _boardCards.AddRange(e.Cards);
                    for (int s = 0; s < _seats.Length; s++) _seats[s].SetAction("");
                    if (_runout) ShowEquities();
                    if (OnMoment != null && river) yield return OnMoment(_runout ? "river_allin" : "river");
                    break;
                case EventKind.Show:
                    CollectBets();
                    _seats[e.Seat].ShowCards(e.Cards[0], e.Cards[1]);
                    _seats[e.Seat].SetAction(Texts.HoldemHandNames[(int)HandEvaluator.Category(e.Score)]);
                    yield return Wait(0.6f);
                    break;
                case EventKind.PotWon:
                    CollectBets();
                    _stack[e.Seat] += e.Amount;
                    _pot = Math.Max(0, _pot - e.Amount);
                    string handName = e.Score > 0 && _showcase ? " · " + Texts.HoldemHandNames[(int)HandEvaluator.Category(e.Score)] : "";
                    _seats[e.Seat].SetAction("<color=" + Ui.Hex(Palette.Up) + ">+" + e.Amount.ToString("N0") + "</color>" + handName);
                    _seats[e.Seat].Highlight(true);
                    RefreshSeat(e.Seat);
                    RefreshPot();
                    if (OnMoment != null) yield return OnMoment("showdown");
                    yield return Wait(1.1f);
                    break;
                case EventKind.HandEnd:
                    LogHandSummary();
                    _runout = false;
                    yield return Wait(0.4f);
                    break;
                case EventKind.LevelUp:
                    yield return Banner(Texts.HoldemLevel(e.Seat, e.Amount, e.Amount2), 1.2f);
                    break;
                case EventKind.Busted:
                    _seats[e.Seat].Bust(Texts.HoldemBusted((int)e.Amount));
                    Log(Tournament.Seats[e.Seat].Name + " " + Texts.HoldemBusted((int)e.Amount));
                    RefreshInfo();
                    yield return Wait(0.6f);
                    break;
                case EventKind.Reentry:
                    _stack[e.Seat] = Tournament.Config.StartingChips;
                    _seats[e.Seat].SetAction(Texts.HoldemReentered);
                    RefreshSeat(e.Seat);
                    yield return Wait(0.5f);
                    break;
                case EventKind.Refill:
                    _stack[e.Seat] = e.Amount;
                    _seats[e.Seat].SetOccupant(e.Text);
                    RefreshSeat(e.Seat);
                    yield return Banner(Texts.HoldemTablesMerged, 0.8f);
                    break;
                case EventKind.FieldCount:
                    RefreshInfo();
                    break;
                case EventKind.FinalTable:
                    yield return Banner(Texts.HoldemFinalTable, 1.5f);
                    break;
                case EventKind.Bubble:
                    if (OnMoment != null) { _banner.text = Texts.HoldemBubble + "\n<size=24>" + Texts.HoldemBubbleSub + "</size>"; yield return OnMoment("bubble"); }
                    yield return Banner(Texts.HoldemBubble + "\n<size=24>" + Texts.HoldemBubbleSub + "</size>", 1.8f);
                    break;
                case EventKind.BadBeat:
                    yield return BadBeat(e);
                    break;
            }
        }

        int LastHoleSeat()
        {
            int last = -1;
            for (int s = 0; s < HoldemRules.Seats; s++) if (Tournament.Hand.InHand[s]) last = s;
            return last;
        }

        static string ActionLabel(HoldemEvent e)
        {
            switch (e.Action)
            {
                case ActionType.Fold: return Texts.HoldemFold;
                case ActionType.Check: return Texts.HoldemCheck;
                case ActionType.Call: return Texts.HoldemCall + " " + e.Amount.ToString("N0");
                case ActionType.Bet: return Texts.HoldemBet + " " + e.Amount.ToString("N0");
                case ActionType.Raise: return Texts.HoldemRaise + " " + e.Amount.ToString("N0");
                default: return "<color=" + Ui.Hex(Palette.Down) + ">" + Texts.HoldemAllIn + " " + e.Amount.ToString("N0") + "</color>";
            }
        }

        void Put(int seat, long amount)
        {
            _stack[seat] -= amount;
            _bet[seat] += amount;
            _pot += amount;
        }

        void CollectBets()
        {
            for (int s = 0; s < _seats.Length; s++)
            {
                _bet[s] = 0;
                _seats[s].SetBet(0);
            }
            RefreshPot();
        }

        void ResetTableVisuals()
        {
            for (int s = 0; s < _seats.Length; s++)
            {
                _bet[s] = 0;
                _seats[s].ResetForHand();
            }
            foreach (var c in _board) c.Hide();
            _pot = 0;
            _runout = false;
            _banner.text = "";
            _pushFoldPanel.gameObject.SetActive(false);
            RefreshPot();
            RefreshAllSeats();
        }

        void RefreshPot() => _potText.text = _pot > 0 ? Texts.HoldemPot + " " + _pot.ToString("N0") : "";

        void RefreshAllSeats()
        {
            for (int s = 0; s < _seats.Length; s++) RefreshSeat(s);
        }

        /// <summary>Stack text depends on the study level: 0 = own chips only (others as a pile size), 1+ = everyone in bb.</summary>
        void RefreshSeat(int s)
        {
            var t = Tournament;
            if (t == null) return;
            long bb = t.Blinds.Big;
            string stackText;
            if (_study >= 1) stackText = _stack[s].ToString("N0") + " <size=16>(" + (_stack[s] / (double)bb).ToString("F1") + "bb)</size>";
            else if (s == HoldemRules.HeroSeat) stackText = _stack[s].ToString("N0");
            else stackText = PileLabel(_stack[s]);
            _seats[s].SetStack(stackText);
            _seats[s].SetBet(_bet[s]);
        }

        string PileLabel(long stack)
        {
            long total = 0;
            int n = 0;
            for (int s = 0; s < _stack.Length; s++) if (_stack[s] > 0) { total += _stack[s]; n++; }
            double avg = n > 0 ? total / (double)n : 1;
            int idx = stack >= avg * HoldemRules.ChipPileBigAtAvg ? 2 : stack >= avg * HoldemRules.ChipPileMidAtAvg ? 1 : 0;
            return Texts.HoldemChipPiles[idx];
        }

        void PaintPositions(HandState h)
        {
            var pos = Positions.Assign(h);
            for (int s = 0; s < _seats.Length; s++)
            {
                bool inHand = h.InHand[s];
                _seats[s].SetPosition(inHand && _study >= 2 ? pos[s].ToString() : "");
                _seats[s].SetDealer(s == h.Button, _study >= 2);
            }
        }

        void RefreshInfo()
        {
            var t = Tournament;
            int handsLeft = t.Config.HandsPerLevel - ((Math.Max(1, t.HandNumber) - 1) % t.Config.HandsPerLevel);
            bool last = t.LevelIndex == t.Config.BlindSchedule.Length - 1;
            _info.text = Texts.HoldemKindNames[(int)t.Config.Kind] + " · " + Texts.HoldemLevel(t.LevelIndex + 1, t.Blinds.Small, t.Blinds.Big) + "\n" +
                         Texts.HoldemHandNo(t.HandNumber) + (last ? "" : " · " + Texts.HoldemNextLevel(handsLeft)) + "\n" +
                         Texts.HoldemRemaining(t.Remaining);
        }

        // ================= hero controls =================

        void SetupControls(HandState h, Legal legal)
        {
            _controls.gameObject.SetActive(true);
            _controls.SetAsLastSibling();
            _fold.Interactable = !legal.CanCheck;
            if (legal.CanCheck) _call.Label.text = Texts.HoldemCheck;
            else
            {
                string odds = _study >= 3
                    ? "\n<size=17>" + Texts.HoldemPotOdds + " " + (100.0 * legal.ToCall / (h.PotTotal + legal.ToCall)).ToString("F0") + "%</size>"
                    : "";
                _call.Label.text = (legal.ToCall >= h.Stack[HoldemRules.HeroSeat] ? Texts.HoldemAllIn : Texts.HoldemCall) + " " + legal.ToCall.ToString("N0") + odds;
            }
            bool canRaise = legal.CanRaise && legal.MaxTo > h.CurrentBet;
            _raise.Button.gameObject.SetActive(canRaise);
            _slider.gameObject.SetActive(canRaise && legal.MinRaiseTo < legal.MaxTo);
            foreach (var p in _presets) p.Button.gameObject.SetActive(canRaise);
            _raiseTo = legal.MinRaiseTo;
            _slider.SetValueWithoutNotify(0);
            UpdateRaiseLabel();
        }

        void SetPreset(float fraction)
        {
            var h = Tournament.Hand;
            var legal = h.LegalFor(HoldemRules.HeroSeat);
            if (fraction < 0) _raiseTo = legal.MaxTo;
            else
            {
                long pot = h.PotTotal + legal.ToCall;
                _raiseTo = h.CurrentBet + (long)Math.Round(pot * fraction);
            }
            _raiseTo = Math.Max(legal.MinRaiseTo, Math.Min(legal.MaxTo, _raiseTo));
            _slider.SetValueWithoutNotify(legal.MaxTo > legal.MinRaiseTo ? (float)(_raiseTo - legal.MinRaiseTo) / (legal.MaxTo - legal.MinRaiseTo) : 1);
            UpdateRaiseLabel();
        }

        void OnSlider(float v)
        {
            var legal = Tournament.Hand.LegalFor(HoldemRules.HeroSeat);
            long bb = Tournament.Blinds.Big;
            long raw = legal.MinRaiseTo + (long)Math.Round((legal.MaxTo - legal.MinRaiseTo) * v);
            _raiseTo = v >= 0.999f ? legal.MaxTo : Math.Max(legal.MinRaiseTo, raw / Math.Max(1, bb / 2) * Math.Max(1, bb / 2));
            UpdateRaiseLabel();
        }

        void UpdateRaiseLabel()
        {
            var h = Tournament.Hand;
            var legal = h.LegalFor(HoldemRules.HeroSeat);
            string verb = _raiseTo >= legal.MaxTo ? Texts.HoldemAllIn : legal.IsBet ? Texts.HoldemBet : Texts.HoldemRaise;
            _raise.Label.text = verb + " " + _raiseTo.ToString("N0");
        }

        PlayerAction RaiseChoice()
        {
            var legal = Tournament.Hand.LegalFor(HoldemRules.HeroSeat);
            if (_raiseTo >= legal.MaxTo) return PlayerAction.AllIn;
            return legal.IsBet ? PlayerAction.BetTo(_raiseTo) : PlayerAction.RaiseTo(_raiseTo);
        }

        /// <summary>Study level 4: the push/fold chart for this exact spot.</summary>
        void RefreshPushFold()
        {
            if (_study < 4) { _pushFoldPanel.gameObject.SetActive(false); return; }
            _pushFoldPanel.gameObject.SetActive(true);
            var h = Tournament.Hand;
            int me = HoldemRules.HeroSeat;
            double eff = Bot.EffectiveBb(h, me);
            string cls = HandRanks.ClassOf(h.Hole0[me], h.Hole1[me]);
            double pct = HandRanks.Percentile(h.Hole0[me], h.Hole1[me]);
            if (h.Street != Street.Preflop || eff > Bot.PushFoldAtBb)
            {
                _pushFoldText.text = Texts.HoldemTooDeep + "\n" + Texts.HoldemMyHand(cls, pct, false).Split('→')[0];
                return;
            }
            var pos = Positions.Assign(h);
            double top;
            string advice;
            if (h.RaisesThisStreet == 0)
            {
                top = PushFoldTable.PushPercent(pos[me], eff);
                advice = Texts.HoldemPushAdvice(top, eff, pos[me].ToString());
            }
            else
            {
                var shover = h.LastRaiser >= 0 ? pos[h.LastRaiser] : TablePosition.SB;
                top = PushFoldTable.CallPercent(shover, eff);
                advice = Texts.HoldemCallAdvice(top, eff, shover.ToString());
            }
            _pushFoldText.text = advice + "\n" + Texts.HoldemMyHand(cls, pct, pct <= top);
        }

        // ================= effects =================

        IEnumerator Wait(float s) { yield return new WaitForSeconds(s); }

        IEnumerator Banner(string text, float seconds)
        {
            _banner.text = text;
            yield return new WaitForSeconds(seconds);
            _banner.text = "";
        }

        /// <summary>All-in river: longer wait and a gentle shake of the board.</summary>
        IEnumerator Shake(float seconds)
        {
            var home = new Vector2[5];
            for (int i = 0; i < 5; i++) home[i] = _board[i].Root.anchoredPosition;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                float k = 3f * (t / seconds);
                for (int i = 0; i < 5; i++)
                    _board[i].Root.anchoredPosition = home[i] + new Vector2(Mathf.Sin(Time.time * 55f + i) * k, Mathf.Cos(Time.time * 47f + i) * k);
                yield return null;
            }
            for (int i = 0; i < 5; i++) _board[i].Root.anchoredPosition = home[i];
        }

        IEnumerator BadBeat(HoldemEvent e)
        {
            _blackout.transform.SetAsLastSibling();
            _blackout.color = Color.black;
            yield return new WaitForSeconds(HoldemRules.BadBeatBlackout);
            string line = Texts.HoldemBadBeatLines[Tournament.Result.BadBeats % Texts.HoldemBadBeatLines.Length];
            var text = Ui.Text(_blackout.transform, line + "\n<size=22><color=#8c8c94>" + e.Amount + "%</color></size>", 40, align: TextAlignmentOptions.Center);
            if (OnMoment != null) yield return OnMoment("badbeat");
            yield return new WaitForSeconds(1.4f);
            Destroy(text.gameObject);
            _blackout.color = new Color(0, 0, 0, 0);
        }

        RectTransform Popup(string title, string body)
        {
            ClearOverlay();
            _overlay.SetAsLastSibling();
            Ui.Image(_overlay, "Scrim", Palette.Scrim, raycast: true);
            var panel = Ui.Image(_overlay, "Panel", Palette.Panel, raycast: true).rectTransform.Box(0.5f, 0.5f, 620, 260);
            Ui.Text(panel, title, 30, Palette.Accent, TextAlignmentOptions.Center, true).rectTransform.Place(0, 0.55f, 1, 1, 20, 0, 20, 20);
            if (body != null) Ui.Text(panel, body, 22, align: TextAlignmentOptions.Center).rectTransform.Place(0, 0.4f, 1, 0.6f);
            return panel;
        }

        void ClearOverlay()
        {
            if (_overlay != null) Ui.Clear(_overlay);
        }

        IEnumerator ShowResult()
        {
            AtResult = true;
            var r = Tournament.Result;
            ClearOverlay();
            _overlay.SetAsLastSibling();
            Ui.Image(_overlay, "Scrim", new Color(0, 0, 0, 0.8f), raycast: true);
            var panel = Ui.Image(_overlay, "Panel", Palette.Panel, raycast: true).rectTransform.Box(0.5f, 0.5f, 760, 520);
            Ui.Text(panel, Texts.HoldemResultTitle + " · " + Texts.HoldemKindNames[(int)Tournament.Config.Kind], 30, Palette.Accent, TextAlignmentOptions.Center, true)
              .rectTransform.Place(0, 1, 1, 1, 20, -70, 20, 20);
            string finish = r.Finish == 1 ? "<color=" + Ui.Hex(Palette.Up) + ">" + Texts.HoldemFinish(1) + " · " + Texts.HoldemWin + "</color>" : Texts.HoldemFinish(r.Finish);
            string Row(string k, string v) => "<color=" + Ui.Hex(Palette.Dim) + ">" + k + "</color>  " + v;
            var lines = new List<string>
            {
                "<size=44>" + finish + "</size>",
                PrizeText(r.Prize),
                "",
                Row(Texts.HoldemHandsPlayed, r.HandsPlayed.ToString()),
                Row(Texts.HoldemReentries, r.Reentries + "회"),
                Row(Texts.HoldemBadBeats, r.BadBeats + "회"),
                Row(Texts.HoldemBubbleReached, r.BubbleReached ? "O" : "X"),
            };
            Ui.Text(panel, string.Join("\n", lines), 24, align: TextAlignmentOptions.Top).rectTransform.Place(0, 0, 1, 1, 30, 100, 30, 80);
            bool back = false;
            Ui.Button(panel, Texts.HoldemBackToMenu, () => back = true, 26).Rect.Place(0.5f, 0, 0.5f, 0, -140, 24, -140, -84);
            if (OnMoment != null) yield return OnMoment("result");
            while (!back && AutoHero == null) yield return null;
            if (AutoHero != null) yield break;
            ShowMenu();
        }

        string PrizeText(Prize p)
        {
            switch (p.Type)
            {
                case PrizeType.Seat: return Tournament.Config.Kind == TournamentKind.Pub ? Texts.HoldemPrizeSatSeat : Texts.HoldemPrizeMainSeat;
                case PrizeType.Point: return Texts.HoldemPrizePoints(p.Amount);
                case PrizeType.Cash: return Texts.HoldemPrizeCash(p.Amount, p.AfterTax);
                default: return "<color=" + Ui.Hex(Palette.Dim) + ">" + Texts.HoldemPrizeNone + "</color>";
            }
        }
    }
}
