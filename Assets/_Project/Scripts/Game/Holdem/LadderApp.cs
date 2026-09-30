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
    /// Scene entry for Holdem.unity (v2: all-in or fold + win ladder; standalone, not hooked into the 30-day loop).
    /// Boot menu → hands replayed from <see cref="LadderHand"/> records → result. Rules live in AllIn.Holdem.Ladder;
    /// this only draws and asks the hero for [올인] / [폴드]. (The old 9-seat HoldemApp is no longer launched.)
    /// </summary>
    public sealed class LadderApp : MonoBehaviour
    {
        public const string SceneName = "Holdem";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() => SceneManager.sceneLoaded += (scene, _) =>
        {
            if (scene.name == SceneName && FindAnyObjectByType<LadderApp>() == null) new GameObject("LadderApp").AddComponent<LadderApp>();
        };

        /// <summary>Dev capture: when set, this answers for the hero.</summary>
        public ILadderPlayer AutoHero;
        public Func<string, IEnumerator> OnMoment;
        /// <summary>Dev capture only: forces the "무조건 승리" chance (the rule default is HoldemRules.LuckQHoldem = 0).</summary>
        public double? LuckQOverride;

        public Ladder Ladder { get; private set; }
        public bool AtResult { get; private set; }

        Canvas _canvas;
        RectTransform _table, _menu, _controls, _overlay, _advicePanel;
        TextMeshProUGUI _info, _banner, _adviceText, _log, _subBanner;
        readonly SeatView[] _seats = new SeatView[HoldemRules.Seats];
        readonly CardView[] _board = new CardView[5];
        Image _blackout;
        TournamentKind _kind = TournamentKind.Pub;
        int _study;
        long _seed;
        Ui.Btn[] _kindButtons, _studyButtons;
        Ui.Btn _allIn, _fold, _quickButton;
        TextMeshProUGUI _menuSeed;
        bool? _choice;
        bool _quick;
        readonly List<string> _logLines = new List<string>();

        const float TableCx = 0, TableCy = 50, Rx = 520, Ry = 210;

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

            _canvas = Ui.Canvas("LadderUI", 0);
            BuildTable();
            BuildControls();
            BuildMenu();
            _seed = LoopScene.LoopApp.NewSeed() % 1_000_000;
            ShowMenu();
        }

        // ================= menu =================

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
            var cfg = LadderConfig.For(kind, study);
            if (LuckQOverride.HasValue) cfg.LuckQ = LuckQOverride.Value;
            Ladder = new Ladder(cfg, new Xoshiro128((ulong)seed)) { LuckRng = new Xoshiro128((ulong)seed ^ 0x1AC1AC1AC1ACUL) };
            _menu.gameObject.SetActive(false);
            ClearOverlay();
            AtResult = false;
            _logLines.Clear();
            _log.text = "";
            for (int i = 0; i < _seats.Length; i++)
                _seats[i].SetOccupant(i == 0 ? Texts.HoldemHeroName : Ladder.SeatName[i]);
            StartCoroutine(Run());
        }

        // ================= table =================

        void BuildTable()
        {
            _table = Ui.Rect(_canvas.transform, "Table");
            Ui.Image(_table, "Felt", new Color(0.10f, 0.32f, 0.20f)).rectTransform.Box(0.5f, 0.5f, 2 * Rx - 120, 2 * Ry - 40, TableCx, TableCy);
            _info = Ui.Text(_table, "", 16, Palette.Text, TextAlignmentOptions.TopLeft);
            _info.rectTransform.Box(0f, 1f, 344, 92, 188, -52);
            _info.textWrappingMode = TextWrappingModes.NoWrap;
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
                float bx = TableCx + (float)(Math.Cos(a) * Rx * 0.6f), by = TableCy + (float)(Math.Sin(a) * Ry * 0.55f);
                _seats[i] = SeatView.Create(_table, i == 0, new Vector2(x, y), new Vector2(bx, by));
            }
            _advicePanel = Ui.Image(_table, "Advice", new Color(0, 0, 0, 0.6f)).rectTransform;
            _advicePanel.Place(1, 0, 1, 0, -300, 112, 10, -252);
            Ui.Text(_advicePanel, Texts.HoldemPushFold, 20, Palette.Accent, TextAlignmentOptions.TopLeft, true).rectTransform.Place(0, 1, 1, 1, 12, -34, 12, 8);
            _adviceText = Ui.Text(_advicePanel, "", 17, align: TextAlignmentOptions.TopLeft);
            _adviceText.rectTransform.Place(0, 0, 1, 1, 12, 8, 12, 36);

            _banner = Ui.Text(_table, "", 40, Palette.Accent, TextAlignmentOptions.Center, true);
            _banner.rectTransform.Box(0.5f, 0.5f, 900, 90, 0, TableCy + 150);
            _subBanner = Ui.Text(_table, "", 26, Palette.Text, TextAlignmentOptions.Center);
            _subBanner.rectTransform.Box(0.5f, 0.5f, 900, 50, 0, TableCy + 90);
            _log = Ui.Text(_table, "", 15, Palette.Dim, TextAlignmentOptions.BottomLeft);
            _log.rectTransform.Place(0, 0, 0, 0, 12, 112, -430, -300);
            _log.overflowMode = TextOverflowModes.Truncate;
            _quickButton = Ui.Button(_table, "", () => { _quick = !_quick; RefreshQuick(); }, 18);
            _quickButton.Rect.Place(1, 1, 1, 1, -170, -52, 12, 12);
            RefreshQuick();
            _overlay = Ui.Rect(_canvas.transform, "Overlay");
            _blackout = Ui.Image(_canvas.transform, "Blackout", new Color(0, 0, 0, 0));
        }

        void RefreshQuick() => _quickButton.Label.text = Texts.HoldemQuick + (_quick ? " ON" : " OFF");

        void BuildControls()
        {
            _controls = Ui.Image(_canvas.transform, "Controls", Palette.Panel, raycast: true).rectTransform;
            _controls.Place(0, 0, 1, 0, 0, 0, 0, -104);
            _fold = Ui.Button(_controls, Texts.LadderFold, () => _choice = false, 30);
            _fold.Rect.Place(0.08f, 0, 0.42f, 1, 0, 14, 0, 14);
            _allIn = Ui.Button(_controls, Texts.LadderAllIn, () => _choice = true, 34);
            _allIn.Rect.Place(0.58f, 0, 0.92f, 1, 0, 14, 0, 14);
            _controls.gameObject.SetActive(false);
        }

        // ================= run =================

        IEnumerator Run()
        {
            var l = Ladder;
            AtResult = false;
            ResetBoard();
            while (!l.Finished)
            {
                if (l.AwaitingReentry) { yield return ReentryPrompt(); continue; }
                var h = l.BeginHand();
                yield return ShowDeal(h);
                bool allIn;
                if (h.Kind == HandKind.Decision || h.Kind == HandKind.Facing)
                {
                    _allIn.Label.text = h.Kind == HandKind.Facing ? Texts.LadderCall : Texts.LadderAllIn;
                    RefreshAdvice(h);
                    _controls.gameObject.SetActive(true);
                    _seats[0].SetActive(true);
                    if (OnMoment != null) yield return OnMoment(h.Kind == HandKind.Facing ? "bb_facing_shove" : "deal");
                    _choice = null;
                    if (AutoHero != null) _choice = AutoHero.AllIn(h);
                    while (_choice == null) yield return null;
                    allIn = _choice.Value;
                    _controls.gameObject.SetActive(false);
                    _seats[0].SetActive(false);
                }
                else allIn = true; // free blind hand (ignored) or forced all-in
                _advicePanel.gameObject.SetActive(false);
                l.Resolve(allIn);
                yield return PlayOutcome(h);
            }
            yield return ShowResult();
        }

        IEnumerator ReentryPrompt()
        {
            int answer = -1;
            var panel = Popup(Texts.HoldemReentryPrompt(Ladder.Config.Reentry.Cost), null);
            Ui.Button(panel, Texts.Yes, () => answer = 1, 26).Rect.Place(0.1f, 0.1f, 0.45f, 0.4f);
            Ui.Button(panel, Texts.No, () => answer = 0, 26).Rect.Place(0.55f, 0.1f, 0.9f, 0.4f);
            if (OnMoment != null) yield return OnMoment("reentry");
            if (AutoHero != null) answer = 1;
            while (answer < 0) yield return null;
            ClearOverlay();
            Ladder.Reenter(answer == 1);
            ResetBoard();
        }

        // ================= a hand =================

        IEnumerator ShowDeal(LadderHand h)
        {
            ResetBoard();
            for (int i = 0; i < _seats.Length; i++)
            {
                _seats[i].ResetForHand();
                _seats[i].SetOccupant(i == 0 ? Texts.HoldemHeroName : Ladder.SeatName[i]);
                _seats[i].SetPosition("");
                _seats[i].SetStack("");
                _seats[i].SetAction("");
                if (i == 0) continue;
                if (h.Kind == HandKind.Facing) { if (i == h.ShoverSeat) _seats[i].ShowBacks(); else _seats[i].Fold(); }
                else if (i <= h.Behind) _seats[i].ShowBacks(); else _seats[i].Fold();
            }
            _seats[0].ShowCards(h.Hole0, h.Hole1);
            RefreshInfo(h);
            if (h.Kind == HandKind.Facing)
            {
                _seats[h.ShoverSeat].SetAction("<color=" + Ui.Hex(Palette.Down) + ">" + Texts.LadderAllIn + " " + h.ShoverStackBb.ToString("F1") + "bb</color>");
                _banner.text = "";
                _subBanner.text = Texts.LadderFacing(Ladder.SeatName[h.ShoverSeat], h.ShoverPos.ToString(), h.ShoverStackBb);
            }
            else _subBanner.text = "";
            if (h.Kind != HandKind.Facing) _banner.text = h.Kind == HandKind.FreeBlind ? Texts.LadderFreeBlind : h.Kind == HandKind.ForcedAllIn ? Texts.LadderForced : "";
            yield return Wait(h.Kind == HandKind.Decision ? 0.25f : 0.7f);
        }

        IEnumerator PlayOutcome(LadderHand h)
        {
            _banner.text = "";
            string handText = Card.RankText(h.Hole0) + Card.SuitSymbols[Card.Suit(h.Hole0)] + Card.RankText(h.Hole1) + Card.SuitSymbols[Card.Suit(h.Hole1)];
            switch (h.Outcome)
            {
                case HandOutcome.Folded:
                    _seats[0].Fold();
                    AddLog(Texts.LadderFoldedLine(h.Pos.ToString(), handText) + (h.Delta < 0 ? "  " + Bb(h.Delta, h.Bb) : ""));
                    yield return Wait(0.3f);
                    break;
                case HandOutcome.FreeBlind:
                    AddLog(Texts.LadderFreeBlind);
                    yield return Wait(0.3f);
                    break;
                case HandOutcome.Stole:
                    for (int i = 1; i <= h.Behind; i++) _seats[i].Fold();
                    _banner.text = Texts.LadderStole(h.Delta / (double)h.Bb);
                    AddLog(h.Pos + " " + handText + " " + Texts.LadderAllIn + " → " + Texts.LadderStole(h.Delta / (double)h.Bb));
                    if (OnMoment != null) yield return OnMoment("steal");
                    yield return Wait(0.9f);
                    break;
                default:
                    yield return PlayShowdown(h, handText);
                    break;
            }
            RefreshInfo(h, after: true);
            if (h.StageReached != LadderStage.None)
            {
                yield return Banner(Texts.LadderStageReached(Texts.LadderStageNames[(int)h.StageReached]), 1.4f);
                if (h.StageReached == LadderStage.FinalTable && OnMoment != null) yield return OnMoment("final_table");
                if (OnMoment != null) yield return OnMoment("stage_" + h.StageReached);
            }
            if (h.BubbleNow)
            {
                if (OnMoment != null) yield return OnMoment("bubble");
                yield return Banner(Texts.HoldemBubble, 1.3f);
            }
            if (h.LevelUp) yield return Banner(Texts.HoldemLevel(Ladder.Level + 1, Ladder.Bb / 2, Ladder.Bb), 0.8f);
            _banner.text = "";
            _subBanner.text = "";
        }

        IEnumerator PlayShowdown(LadderHand h, string handText)
        {
            for (int i = 1; i < h.CallerSeat; i++) _seats[i].Fold();
            for (int i = h.CallerSeat + 1; i <= h.Behind; i++) _seats[i].Fold();
            var caller = _seats[h.CallerSeat];
            caller.SetAction(Texts.LadderCalledBy(Ladder.SeatName[h.CallerSeat]));
            caller.ShowCards(h.Opp0, h.Opp1);
            caller.SetStack(Texts.LadderStack(h.OppStack / (double)h.Bb).Replace("스택 ", ""));
            if (_study >= 3 && h.HeroEquity >= 0)
            {
                _seats[0].SetAction(Texts.HoldemEquity(h.HeroEquity));
                caller.SetAction(Texts.LadderCalledBy(Ladder.SeatName[h.CallerSeat]) + " · " + Texts.HoldemEquity(1 - h.HeroEquity));
            }
            if (OnMoment != null) yield return OnMoment("called_reveal");
            yield return Wait(0.9f);
            if (h.LuckTriggered)
            {
                // E2: the call is made, the cards are still face down — the button must be pressed to turn them
                yield return LuckOffer.Show(_table, 0.5f, 0.5f, 0, TableCy + 100, 440).Run(Texts.LuckHoldemLine);
            }
            for (int i = 0; i < 3; i++) _board[i].SetCard(h.Board[i]);
            yield return Wait(0.55f);
            yield return Wait(HoldemRules.TurnDelayAllIn);
            _board[3].SetCard(h.Board[3]);
            yield return Shake(HoldemRules.RiverDelayAllIn);
            _board[4].SetCard(h.Board[4]);

            string result;
            switch (h.Outcome)
            {
                case HandOutcome.Won: result = "<color=" + Ui.Hex(Palette.Up) + ">" + Texts.LadderWon + "</color>"; break;
                case HandOutcome.Split: result = Texts.LadderSplit; break;
                case HandOutcome.LostSurvived: result = "<color=" + Ui.Hex(Palette.Down) + ">" + Texts.LadderSurvived + "</color>"; break;
                default: result = "<color=" + Ui.Hex(Palette.Down) + ">" + Texts.LadderOut + "</color>"; break;
            }
            _banner.text = result;
            _subBanner.text = Bb(h.Delta, h.Bb);
            AddLog(h.Pos + " " + handText + " vs " + Ladder.SeatName[h.CallerSeat] + " → " + StripTags(result) + "  " + Bb(h.Delta, h.Bb));
            if (OnMoment != null) yield return OnMoment(h.Outcome == HandOutcome.Won ? "call_won" : h.Outcome == HandOutcome.LostSurvived ? "call_survived" : h.Outcome == HandOutcome.LostOut ? "call_out" : "call_split");
            if (h.BadBeat) yield return BadBeat(h);
            yield return Wait(1.1f);
        }

        // ================= HUD =================

        string LadderText()
        {
            var cfg = Ladder.Config;
            var parts = new List<string>();
            foreach (var s in cfg.Steps) parts.Add(Texts.LadderRung(Texts.LadderStageNames[(int)s.Stage], s.Wins));
            return string.Join(" · ", parts);
        }

        string WinDots()
        {
            int goal = Ladder.Config.Steps[0].Wins;
            if (Ladder.Config.Kind == TournamentKind.Main) goal = Ladder.Config.Steps[Ladder.Config.Steps.Length - 1].Wins;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < goal; i++) sb.Append(i < Ladder.Wins ? "●" : "○");
            return sb.ToString();
        }

        void RefreshInfo(LadderHand h, bool after = false)
        {
            var l = Ladder;
            long bb = l.Bb;
            var lines = new List<string>
            {
                Texts.LadderHand(l.HandsPlayed + (after ? 0 : 1)) + "  " + Texts.HoldemKindNames[(int)l.Config.Kind] + "  ·  " + Texts.LadderRemaining(l.Remaining),
                WinDots() + "  " + Texts.LadderWins(l.Wins, LadderText()),
            };
            double ratio = l.Stack / (double)(l.Config.StartBb * bb);
            string pile = Texts.HoldemChipPiles[ratio >= 1.3 ? 2 : ratio >= 0.7 ? 1 : 0];
            lines.Add(_study >= 1 ? Texts.LadderStack(l.Stack / (double)bb) + "  " + Texts.LadderBlindIn(l.HandsToNextLevel, bb) : pile);
            _info.text = string.Join("\n", lines);
            _seats[0].SetStack(_study >= 1 ? (l.Stack / (double)bb).ToString("F1") + "bb" : pile);
            if (_study >= 2) _seats[0].SetPosition(h.Pos + " ·뒤" + h.Behind);
        }

        void RefreshAdvice(LadderHand h)
        {
            bool on = _study >= 4;
            _advicePanel.gameObject.SetActive(on);
            if (!on) return;
            double top = Ladder.AdvicePercent(h);
            double pct = Ladder.HandPercentile(h);
            string cls = HandRanks.ClassOf(h.Hole0, h.Hole1);
            bool inRange = pct <= top;
            string adv = h.Kind == HandKind.Facing ? Texts.LadderCallAdvice(top, Math.Min(h.StackBbBefore, h.ShoverStackBb), h.ShoverPos.ToString()) : Texts.LadderAdvice(top, h.StackBbBefore, h.Pos.ToString());
            _adviceText.text = adv + "\n" + Texts.HoldemMyHand(cls, pct, inRange);
        }

        void AddLog(string line)
        {
            _logLines.Add(line);
            if (_logLines.Count > 6) _logLines.RemoveAt(0);
            _log.text = string.Join("\n", _logLines);
        }

        static string Bb(long delta, long bb) => (delta >= 0 ? "+" : "") + (delta / (double)bb).ToString("F1") + "bb";
        static string StripTags(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");

        void ResetBoard()
        {
            for (int i = 0; i < 5; i++) _board[i].Hide();
            _banner.text = "";
            _subBanner.text = "";
        }

        // ================= effects =================

        IEnumerator Wait(float s) { if (!_quick && s > 0) yield return new WaitForSeconds(s); }

        IEnumerator Banner(string text, float seconds)
        {
            _banner.text = text;
            yield return new WaitForSeconds(seconds);
            _banner.text = "";
        }

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

        IEnumerator BadBeat(LadderHand h)
        {
            _blackout.transform.SetAsLastSibling();
            _blackout.color = Color.black;
            yield return new WaitForSeconds(HoldemRules.BadBeatBlackout);
            string line = Texts.HoldemBadBeatLines[Ladder.Result.BadBeats % Texts.HoldemBadBeatLines.Length];
            var text = Ui.Text(_blackout.transform, line + "\n<size=22><color=#8c8c94>" + (int)Math.Round(h.HeroEquity * 100) + "%</color></size>", 40, align: TextAlignmentOptions.Center);
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

        string PrizeText(Prize p)
        {
            switch (p.Type)
            {
                case PrizeType.Seat: return Ladder.Config.Kind == TournamentKind.Pub ? Texts.HoldemPrizeSatSeat : Texts.HoldemPrizeMainSeat;
                case PrizeType.Cash: return Texts.HoldemPrizeCash(p.Amount, p.AfterTax);
                default: return Texts.HoldemPrizeNone;
            }
        }

        IEnumerator ShowResult()
        {
            AtResult = true;
            var r = Ladder.Result;
            ClearOverlay();
            _overlay.SetAsLastSibling();
            Ui.Image(_overlay, "Scrim", new Color(0, 0, 0, 0.8f), raycast: true);
            var panel = Ui.Image(_overlay, "Panel", Palette.Panel, raycast: true).rectTransform.Box(0.5f, 0.5f, 760, 570);
            Ui.Text(panel, Texts.HoldemResultTitle + " · " + Texts.HoldemKindNames[(int)Ladder.Config.Kind], 30, Palette.Accent, TextAlignmentOptions.Center, true)
              .rectTransform.Place(0, 1, 1, 1, 20, -70, 20, 20);
            string Row(string k, string v) => "<color=" + Ui.Hex(Palette.Dim) + ">" + k + "</color>  " + v;
            string stage = Texts.LadderResultStage(Texts.LadderStageNames[(int)r.Stage]);
            var lines = new List<string>
            {
                "<size=44>" + (r.Stage > LadderStage.None ? "<color=" + Ui.Hex(Palette.Up) + ">" + stage + "</color>" : stage) + "</size>",
                PrizeText(r.Prize),
                "",
                Row(Texts.LadderWinsResult, r.Wins.ToString()),
                Row(Texts.HoldemHandsPlayed, r.HandsPlayed.ToString()),
                Row(Texts.LadderCalledAllIns, r.CalledAllIns + " (승 " + r.CalledWins + ")"),
                Row(Texts.LadderSteals, r.Steals.ToString()),
                Row(Texts.E7Luck, r.LuckWins + "회"),
                Row(Texts.HoldemReentries, r.Reentries + "회"),
                Row(Texts.HoldemBadBeats, r.BadBeats + "회"),
                Row(Texts.HoldemBubbleReached, r.BubbleReached ? "O" : "X"),
            };
            Ui.Text(panel, string.Join("\n", lines), 22, align: TextAlignmentOptions.Top).rectTransform.Place(0, 0, 1, 1, 30, 100, 30, 80);
            bool back = false;
            Ui.Button(panel, Texts.HoldemBackToMenu, () => back = true, 26).Rect.Place(0.5f, 0, 0.5f, 0, -140, 24, -140, -84);
            if (OnMoment != null) yield return OnMoment("result");
            while (!back && AutoHero == null) yield return null;
            if (AutoHero != null) yield break;
            ShowMenu();
        }
    }
}
