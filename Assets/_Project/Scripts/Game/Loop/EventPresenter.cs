using System;
using System.Collections;
using System.Collections.Generic;
using AllIn.Core;
using AllIn.Game.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AllIn.Game.LoopScene
{
    /// <summary>
    /// Plays Core's event queue one at a time, on top of everything. An event is popped (and the save updated)
    /// only after it has been dismissed — quit mid-popup and it plays again next launch.
    /// </summary>
    public sealed class EventPresenter : MonoBehaviour
    {
        LoopApp _app;
        RectTransform _layer;
        Image _flash;

        public bool Busy { get; private set; }

        /// <summary>Runs once the current event has been popped and saved (the settlement's [계속 살기] uses it so the
        /// resumed slot's own events queue up behind an already-empty queue instead of re-triggering the settlement).</summary>
        Action _afterPop;

        public static EventPresenter Create(LoopApp app)
        {
            var canvas = Ui.Canvas("Events", 50);
            var p = canvas.gameObject.AddComponent<EventPresenter>();
            p._app = app;
            p._layer = (RectTransform)canvas.transform;
            p._flash = Ui.Image(canvas.transform, "Flash", new Color(0, 0, 0, 0));
            return p;
        }

        void Update()
        {
            if (Busy || _app.Loop == null || !_app.Loop.HasEvent) return;
            StartCoroutine(Run(_app.Loop));
        }

        IEnumerator Run(Core.Loop loop)
        {
            Busy = true;
            yield return Present(loop.Data.pendingEvents[0]);
            if (_app.Loop == loop) // "다시 전역일로" swaps the loop mid-event; the new run keeps its own queue
            {
                loop.PopEvent();
                _app.Save();
            }
            _app.Refresh();
            var after = _afterPop;
            _afterPop = null;
            after?.Invoke();
            Busy = false;
        }

        IEnumerator Present(GameEvent e)
        {
            switch (e.id)
            {
                case EventIds.Opening: return Opening();
                case EventIds.FirstPopup: return Popup(null, new[] { Texts.FirstPopup }, Texts.Close);
                case EventIds.LiquidationDay: return LiquidationDay(e);
                case EventIds.LiquidationNight: return LiquidationNight(e);
                case EventIds.SelfPity: return SelfPity(e);
                case EventIds.HopeCircuit: return HopeCircuit(e);
                case EventIds.LottoDraw: return LottoDraw(e);
                case EventIds.MoveRoom: return Popup(null, new[] { Texts.E6Move((Room)e.ints[0], (Room)e.ints[1]) }, Texts.Confirm);
                case EventIds.Ghost: return Ghost(e);
                case EventIds.Bankrupt: return Bankrupt(e);
                case EventIds.Sick: return Popup(Texts.SickTitle, new[] { Texts.SickBody(e.amount, e.count) }, Texts.Confirm);
                case EventIds.Hunger: return Popup(Texts.HungerTitle, new[] { Texts.HungerBody(e.count, e.amount) }, Texts.Confirm);
                case EventIds.VoyageReturn: return VoyageReturn(e);
                case EventIds.Settlement: return Settlement(e);
                case EventIds.Fired: return Popup(null, new[] { Texts.WorkMistake }, Texts.Confirm);
                case EventIds.CantGetUp: return Popup(null, new[] { Texts.CantGetUp }, Texts.Confirm);
                default: return Popup(null, new[] { e.id }, Texts.Confirm);
            }
        }

        public void ConfirmNewLife()
        {
            if (Busy || _app.Watch.Busy) return;
            StartCoroutine(NewLifeRoutine());
        }

        IEnumerator NewLifeRoutine()
        {
            Busy = true;
            int choice = -1;
            var scrim = Scrim(Palette.Scrim);
            var panel = Panel(scrim, 640, 280);
            Ui.Text(panel, Texts.NewLifeConfirm, 24, align: TextAlignmentOptions.Center).rectTransform.Place(0, 0.4f, 1, 1, 30, 0, 30, 20);
            Ui.Button(panel, Texts.Yes, () => choice = 1, 26).Rect.Place(0.1f, 0.08f, 0.45f, 0.32f);
            Ui.Button(panel, Texts.No, () => choice = 0, 26).Rect.Place(0.55f, 0.08f, 0.9f, 0.32f);
            while (choice < 0) yield return null;
            Destroy(scrim.gameObject);
            if (choice == 1) _app.StartNewLife();
            Busy = false;
        }

        // ---------- building blocks ----------

        RectTransform Scrim(Color color)
        {
            var img = Ui.Image(_layer, "Scrim", color, raycast: true);
            img.transform.SetSiblingIndex(_flash.transform.GetSiblingIndex()); // keep the flash on top
            return img.rectTransform;
        }

        static RectTransform Panel(RectTransform parent, float w, float h, Color? color = null) =>
            Ui.Image(parent, "Panel", color ?? Palette.Panel, raycast: true).rectTransform.Box(0.5f, 0.5f, w, h);

        IEnumerator Popup(string title, string[] lines, string button)
        {
            var scrim = Scrim(Palette.Scrim);
            float h = 200 + lines.Length * 40 + (title != null ? 50 : 0);
            var panel = Panel(scrim, 700, h);
            float top = 30;
            if (title != null)
            {
                Ui.Text(panel, title, 32, Palette.Accent, TextAlignmentOptions.Center, true).rectTransform.Place(0, 1, 1, 1, 20, -top - 44, 20, top);
                top += 54;
            }
            Ui.Text(panel, string.Join("\n", lines), 26, align: TextAlignmentOptions.Center).rectTransform.Place(0, 0, 1, 1, 30, 100, 30, top);
            yield return WaitButton(panel, button);
            Destroy(scrim.gameObject);
        }

        static IEnumerator WaitButton(RectTransform parent, string label)
        {
            bool done = false;
            Ui.Button(parent, label, () => done = true, 26).Rect.Place(0.5f, 0, 0.5f, 0, -120, 24, -120, -84);
            while (!done) yield return null;
        }

        /// <summary>Invisible full-screen button: click anywhere to go on. Used where skipping must not exist.</summary>
        static IEnumerator WaitClickAnywhere(RectTransform parent)
        {
            bool done = false;
            var img = Ui.Image(parent, "ClickCatcher", new Color(0, 0, 0, 0), raycast: true);
            var b = img.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => done = true);
            while (!done) yield return null;
            Destroy(img.gameObject);
        }

        IEnumerator Flash(Color color, float seconds)
        {
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
            {
                _flash.color = new Color(color.r, color.g, color.b, color.a * (1 - t / seconds));
                yield return null;
            }
            _flash.color = new Color(0, 0, 0, 0);
        }

        static IEnumerator FadeText(TextMeshProUGUI t, float seconds)
        {
            for (float x = 0; x < seconds; x += Time.unscaledDeltaTime)
            {
                t.alpha = x / seconds;
                yield return null;
            }
            t.alpha = 1;
        }

        static void Shake(RectTransform rt, Vector2 home, float strength, float seed)
        {
            float tt = Time.unscaledTime * 40f;
            rt.anchoredPosition = home + new Vector2((Mathf.PerlinNoise(tt, seed) - 0.5f) * 2 * strength, (Mathf.PerlinNoise(seed, tt) - 0.5f) * 2 * strength);
        }

        // ---------- opening ----------

        IEnumerator Opening()
        {
            var scrim = Scrim(Color.black);
            var text = Ui.Text(scrim, "", 30, align: TextAlignmentOptions.Center);
            text.rectTransform.Place(0, 0.3f, 1, 0.8f, 60, 0, 60, 0);
            Ui.Text(scrim, Texts.OpeningTitle, 22, Palette.Dim, TextAlignmentOptions.Center).rectTransform.Place(0, 0.8f, 1, 0.9f);
            var lines = new List<string>();
            foreach (var line in Texts.OpeningLines)
            {
                lines.Add(line);
                text.text = string.Join("\n\n", lines);
                yield return new WaitForSecondsRealtime(0.9f);
            }
            yield return WaitButton(scrim, Texts.Confirm);
            Destroy(scrim.gameObject);
        }

        // ---------- E1: daytime liquidation ----------

        IEnumerator LiquidationDay(GameEvent e)
        {
            var scrim = Scrim(new Color(0.15f, 0, 0, 0.75f));
            StartCoroutine(Flash(new Color(1f, 0.05f, 0.05f, 0.85f), 0.3f));
            var panel = Panel(scrim, 700, 340);
            Ui.Text(panel, Texts.E1Title, 44, Palette.Down, TextAlignmentOptions.Center, true).rectTransform.Place(0, 1, 1, 1, 20, -90, 20, 24);
            Ui.Text(panel, Texts.E1Body + "  (x" + e.count + ")", 24, Palette.Dim, TextAlignmentOptions.Center).rectTransform.Place(0, 1, 1, 1, 20, -140, 20, 96);
            Ui.Text(panel, Texts.E1Lost + "  " + Fmt.Won(-e.amount), 34, Palette.Down, TextAlignmentOptions.Center, true).rectTransform.Place(0, 1, 1, 1, 20, -210, 20, 150);
            yield return WaitButton(panel, Texts.Confirm);
            Destroy(scrim.gameObject);
        }

        // ---------- E2: 3am notification ----------

        IEnumerator LiquidationNight(GameEvent e)
        {
            var scrim = Scrim(Color.black);
            var clock = Ui.Text(scrim, Texts.E2Time, 96, new Color(0.5f, 0.5f, 0.55f), TextAlignmentOptions.Center, true);
            clock.rectTransform.Place(0, 0.55f, 1, 0.85f);
            var buzz = Ui.Text(scrim, Texts.E2Vibrate, 34, Palette.Dim, TextAlignmentOptions.Center);
            buzz.rectTransform.Box(0.5f, 0.42f, 600, 60);
            var home = buzz.rectTransform.anchoredPosition;
            for (float t = 0; t < 1.6f; t += Time.unscaledDeltaTime)
            {
                bool on = (t % 0.8f) < 0.5f; // two buzzes
                buzz.alpha = on ? 1 : 0.2f;
                if (on) Shake(buzz.rectTransform, home, 6, 3.7f);
                yield return null;
            }
            buzz.rectTransform.anchoredPosition = home;

            // notification banner slides down from the top
            var banner = Ui.Image(scrim, "Banner", new Color(0.93f, 0.93f, 0.95f, 1f)).rectTransform;
            banner.anchorMin = new Vector2(0.5f, 1);
            banner.anchorMax = new Vector2(0.5f, 1);
            banner.pivot = new Vector2(0.5f, 1);
            banner.sizeDelta = new Vector2(760, 120);
            var bt = Ui.Text(banner, Texts.E2Banner + "\n" + Texts.E1Lost + " " + Fmt.Won(-e.amount), 24, new Color(0.1f, 0.1f, 0.12f));
            bt.rectTransform.Place(0, 0, 1, 1, 24, 10, 24, 10);
            for (float t = 0; t < 0.35f; t += Time.unscaledDeltaTime)
            {
                banner.anchoredPosition = new Vector2(0, Mathf.Lerp(130, -24, t / 0.35f));
                yield return null;
            }
            banner.anchoredPosition = new Vector2(0, -24);
            yield return WaitButton(scrim, Texts.Confirm);
            Destroy(scrim.gameObject);
        }

        // ---------- E3: self-pity (forced, unskippable) ----------

        IEnumerator SelfPity(GameEvent e)
        {
            var lines = Texts.E3Monologues[Mathf.Clamp(e.count, 0, Texts.E3Monologues.Length - 1)];
            var scrim = Scrim(new Color(0, 0, 0, 0.01f)); // blocks input to the action bar until the monologue ends
            for (float t = 0; t < 0.6f; t += Time.unscaledDeltaTime)
            {
                _app.SetRoomShade(0.8f * t / 0.6f);
                yield return null;
            }
            _app.SetRoomShade(0.8f);
            var text = Ui.Text(scrim, "", 30, align: TextAlignmentOptions.Center);
            text.rectTransform.Place(0.1f, 0.3f, 0.9f, 0.7f);
            var shown = new List<string>();
            foreach (var line in lines)
            {
                shown.Add(line);
                text.text = string.Join("\n\n", shown);
                yield return WaitClickAnywhere(scrim);
            }
            for (float t = 0; t < 0.6f; t += Time.unscaledDeltaTime)
            {
                _app.SetRoomShade(0.8f * (1 - t / 0.6f));
                yield return null;
            }
            _app.SetRoomShade(0);
            Destroy(scrim.gameObject);
        }

        // ---------- E4: hope circuit calculator ----------

        IEnumerator HopeCircuit(GameEvent e)
        {
            var scrim = Scrim(Palette.Scrim);
            var panel = Panel(scrim, 760, 460, new Color(0.10f, 0.12f, 0.10f, 0.98f));
            Ui.Text(panel, Texts.E4Title, 30, Palette.Up, TextAlignmentOptions.Center, true).rectTransform.Place(0, 1, 1, 1, 20, -70, 20, 24);
            Ui.Text(panel, Texts.E4Line1 + "\n" + Texts.E4Rate(e.amount3 / Core.Loop.HopeRateScale) + "\n" + Texts.E4Days(e.count), 26, align: TextAlignmentOptions.Center)
              .rectTransform.Place(0, 1, 1, 1, 20, -210, 20, 80);
            var big = Ui.Text(panel, Fmt.Won(e.amount), 44, Palette.Accent, TextAlignmentOptions.Center, true);
            big.rectTransform.Place(0, 1, 1, 1, 20, -290, 20, 220);
            // count up from today's total to the projection — the numbers are real, only the tempo is theater
            double from = e.amount, to = e.amount2;
            for (float t = 0; t < 1.4f; t += Time.unscaledDeltaTime)
            {
                double k = Math.Pow(t / 1.4f, 3);
                big.text = Fmt.Won(from + (to - from) * k);
                yield return null;
            }
            big.text = Fmt.Won(to);
            bool done = false;
            Ui.Button(panel, Texts.E4Yes, () => done = true, 26).Rect.Place(0.1f, 0, 0.45f, 0, 0, 30, 0, -100);
            Ui.Button(panel, Texts.E4Dots, () => done = true, 26).Rect.Place(0.55f, 0, 0.9f, 0, 0, 30, 0, -100);
            while (!done) yield return null;
            Destroy(scrim.gameObject);
        }

        // ---------- E5: lotto draw ----------

        IEnumerator LottoDraw(GameEvent e)
        {
            const int maxRows = 8;
            int[] drawn = e.ints;
            int[] mine = e.ints2;
            int games = mine.Length / Rules.LottoPicks;

            var scrim = Scrim(Palette.Scrim);
            var panel = Panel(scrim, 900, 680);
            Ui.Text(panel, Texts.LottoDrawTitle, 30, Palette.Accent, TextAlignmentOptions.Center, true).rectTransform.Place(0, 1, 1, 1, 20, -64, 20, 18);

            // balls: 6 + "+" + bonus
            var balls = new TextMeshProUGUI[7];
            var ballImgs = new Image[7];
            for (int i = 0; i < 7; i++)
            {
                float x = -330 + i * 100 + (i == 6 ? 40 : 0);
                var img = Ui.Image(panel, "ball" + i, Palette.PanelLight);
                img.rectTransform.Box(0.5f, 1, 80, 80, x, -130);
                ballImgs[i] = img;
                balls[i] = Ui.Text(img.transform, "?", 34, Palette.Dim, TextAlignmentOptions.Center, true);
            }
            var plus = Ui.Text(panel, "+", 34, Palette.Dim, TextAlignmentOptions.Center, true);
            plus.rectTransform.Box(0.5f, 1, 40, 80, -330 + 5 * 100 + 70, -130);

            // my games
            int rows = Math.Min(games, maxRows);
            var cells = new Image[rows, Rules.LottoPicks];
            for (int g = 0; g < rows; g++)
            {
                for (int k = 0; k < Rules.LottoPicks; k++)
                {
                    var c = Ui.Image(panel, "cell", Palette.PanelLight);
                    c.rectTransform.Box(0.5f, 1, 56, 32, -155 + k * 62, -200 - g * 36);
                    Ui.Text(c.transform, mine[g * Rules.LottoPicks + k].ToString("00"), 22, align: TextAlignmentOptions.Center);
                    cells[g, k] = c;
                }
            }
            if (games > maxRows)
                Ui.Text(panel, "… 외 " + (games - maxRows) + "게임", 20, Palette.Dim, TextAlignmentOptions.Center)
                  .rectTransform.Place(0, 0, 1, 0, 0, 158, 0, -186);

            // open one ball per second
            for (int i = 0; i < 7; i++)
            {
                yield return new WaitForSecondsRealtime(1f);
                bool bonus = i == 6;
                balls[i].text = drawn[i].ToString();
                balls[i].color = Color.black;
                ballImgs[i].color = bonus ? Palette.Body : Palette.Accent;
                for (int g = 0; g < rows; g++)
                for (int k = 0; k < Rules.LottoPicks; k++)
                    if (mine[g * Rules.LottoPicks + k] == drawn[i])
                        cells[g, k].color = bonus ? Palette.Body : Palette.Accent;
            }

            var counts = new int[6];
            for (int g = 0; g < games; g++) counts[LottoRules.Rank(mine, g * Rules.LottoPicks, drawn)]++;
            var summary = new List<string>();
            for (int r = 1; r <= 5; r++) if (counts[r] > 0) summary.Add(Texts.LottoRankNames[r] + " " + counts[r]);
            string result = (summary.Count == 0 ? Texts.LottoRankNames[0] : string.Join(" · ", summary)) + "   " + Fmt.Won(e.amount);
            Ui.Text(panel, result + "\n<color=" + Ui.Hex(Palette.Dim) + ">" + Texts.LottoCrowd(e.count) + "</color>", 24, align: TextAlignmentOptions.Center)
              .rectTransform.Place(0, 0, 1, 0, 20, 88, 20, -154);
            yield return WaitButton(panel, Texts.Confirm);
            Destroy(scrim.gameObject);
        }

        // ---------- E8: ghost (본절) ----------

        IEnumerator Ghost(GameEvent e)
        {
            bool enhanced = e.ints.Length > 0 && e.ints[0] != 0;
            var scrim = Scrim(new Color(0.05f, 0.05f, 0.08f, 0.85f));
            var panel = Panel(scrim, 700, 360);
            Ui.Text(panel, Texts.E8Title, 34, Palette.Dim, TextAlignmentOptions.Center, true).rectTransform.Place(0, 1, 1, 1, 20, -80, 20, 24);
            Ui.Text(panel, enhanced ? Texts.E8Enhanced : Texts.E8Body, 24, align: TextAlignmentOptions.Center).rectTransform.Place(0, 1, 1, 1, 20, -140, 20, 90);
            Ui.Text(panel, "진입가 " + Fmt.Won(e.amount2), 22, Palette.Dim, TextAlignmentOptions.Center).rectTransform.Place(0, 1, 1, 1, 20, -190, 20, 150);
            Ui.Text(panel, Texts.E8IfAlive(e.amount3), 30, e.amount3 >= 0 ? Palette.Up : Palette.Down, TextAlignmentOptions.Center, true)
              .rectTransform.Place(0, 1, 1, 1, 20, -250, 20, 190);
            yield return WaitButton(panel, Texts.Confirm);
            Destroy(scrim.gameObject);
        }

        // ---------- E9: bankruptcy, and the ship (D3/D4) ----------

        IEnumerator Bankrupt(GameEvent e)
        {
            var loop = _app.Loop;
            if (!loop.CanDepart().Ok) yield break; // stale: the ship already sailed, or money came back before this popup
            bool forced = e.ints.Length > 0 && e.ints[0] != 0;
            int choice = -1;

            var scrim = Scrim(new Color(0.06f, 0.02f, 0.02f, 0.92f));
            var panel = Panel(scrim, 760, 400);
            Ui.Text(panel, Texts.E9Title, 44, Palette.Down, TextAlignmentOptions.Center, true).rectTransform.Place(0, 1, 1, 1, 20, -90, 20, 24);
            var lines = new List<string> { Texts.E9Body, Texts.E9Owed(e.amount) };
            if (e.amount2 > 0) lines.Add(Texts.E9Exchange(e.amount2));
            if (forced) lines.Add(Texts.E9Forced);
            else if (e.count > 0) lines.Add(Texts.E9Held(e.count));
            Ui.Text(panel, string.Join("\n", lines), 26, align: TextAlignmentOptions.Center).rectTransform.Place(0, 1, 1, 1, 30, -260, 30, 110);
            if (forced) Ui.Button(panel, Texts.E9Board, () => choice = 1, 26).Rect.Place(0.2f, 0, 0.8f, 0, 0, 30, 0, -100);
            else
            {
                Ui.Button(panel, Texts.E9Board, () => choice = 1, 26).Rect.Place(0.05f, 0, 0.55f, 0, 0, 30, 0, -100);
                Ui.Button(panel, Texts.E9Hold, () => choice = 0, 26).Rect.Place(0.6f, 0, 0.95f, 0, 0, 30, 0, -100);
            }
            while (choice < 0) yield return null;
            Destroy(scrim.gameObject);
            if (choice == 0) yield break;

            // an open position: the last chance to deal with it
            bool close = false;
            if (loop.Data.hasPosition)
            {
                int pick = -1;
                var s2 = Scrim(Palette.Scrim);
                var p2 = Panel(s2, 760, 380);
                var pos = loop.Data.position;
                Ui.Text(p2, Texts.VoyageConfirmTitle, 34, Palette.Accent, TextAlignmentOptions.Center, true).rectTransform.Place(0, 1, 1, 1, 20, -80, 20, 24);
                Ui.Text(p2, Texts.VoyageConfirmBody + "\n\n" + (pos.direction == Direction.Long ? Texts.CoinLong : Texts.CoinShort) + " x" + pos.leverage + "   " +
                             Texts.CoinUnrealized + " " + Fmt.SignedWon(loop.UnrealizedPnl), 26, align: TextAlignmentOptions.Center)
                  .rectTransform.Place(0, 1, 1, 1, 30, -250, 30, 100);
                Ui.Button(p2, Texts.VoyageClose, () => pick = 1, 26).Rect.Place(0.05f, 0, 0.5f, 0, 0, 30, 0, -100);
                Ui.Button(p2, Texts.VoyageLeave, () => pick = 0, 26).Rect.Place(0.55f, 0, 0.95f, 0, 0, 30, 0, -100);
                while (pick < 0) yield return null;
                Destroy(s2.gameObject);
                close = pick == 1;
            }

            loop.Depart(close); // the six months are simulated in one go; what follows is the skip scene
            _app.Save();

            var black = Scrim(Color.black);
            _app.AtSeaScene = true;                      // E6: no street, no hero while the ship is out
            var ship = ShipScene(black);
            var text = Ui.Text(black, "", 30, align: TextAlignmentOptions.Center);
            text.rectTransform.Place(0, 0.2f, 1, 0.66f, 60, 0, 60, 0);
            Ui.Text(black, Texts.VoyageBoardTitle, 22, Palette.Dim, TextAlignmentOptions.Center).rectTransform.Place(0, 0.8f, 1, 0.9f);
            var shown = new List<string>();
            foreach (var line in Texts.VoyageBoardLines) { shown.Add(line); text.text = string.Join("\n\n", shown); yield return new WaitForSecondsRealtime(0.9f); }
            shown.Clear();
            foreach (var line in Texts.VoyageSkipLines) { shown.Add(line); text.text = string.Join("\n\n", shown); yield return new WaitForSecondsRealtime(0.9f); }
            yield return WaitButton(black, Texts.Confirm);
            _app.AtSeaScene = false;
            Destroy(black.gameObject);
        }

        /// <summary>The ship scene (placeholder pixel blocks): a hull with a cabin and stack riding two rows of waves.</summary>
        RectTransform ShipScene(RectTransform parent)
        {
            var root = Ui.Rect(parent, "Ship");
            root.Place(0.5f, 0.68f, 0.5f, 0.68f, -150, -60, -150, -60);
            Ui.Image(root, "Hull", new Color(0.32f, 0.20f, 0.14f)).rectTransform.Box(0.5f, 0.5f, 240, 44, 0, -20);
            Ui.Image(root, "Bow", new Color(0.32f, 0.20f, 0.14f)).rectTransform.Box(0.5f, 0.5f, 60, 30, 130, -13);
            Ui.Image(root, "Cabin", new Color(0.82f, 0.82f, 0.80f)).rectTransform.Box(0.5f, 0.5f, 80, 38, -40, 21);
            Ui.Image(root, "Window", new Color(0.55f, 0.75f, 0.9f)).rectTransform.Box(0.5f, 0.5f, 60, 10, -40, 24);
            Ui.Image(root, "Stack", new Color(0.7f, 0.25f, 0.2f)).rectTransform.Box(0.5f, 0.5f, 20, 30, -50, 55);
            Ui.Image(root, "Mast", new Color(0.6f, 0.6f, 0.6f)).rectTransform.Box(0.5f, 0.5f, 5, 60, 60, 40);
            for (int i = 0; i < 12; i++)
                Ui.Image(root, "Wave" + i, new Color(0.12f, 0.25f, 0.45f)).rectTransform.Box(0.5f, 0.5f, 46, 8, -260 + i * 46, -46 + (i % 2) * 8);
            return root;
        }

        IEnumerator VoyageReturn(GameEvent e)
        {
            // ints: age, body max, health debt, voyage count, position state (0 none / 1 open / 2 liquidated at sea)
            var black = Scrim(new Color(0, 0, 0, 0.94f));
            var panel = Panel(black, 780, 520, new Color(0.08f, 0.09f, 0.11f, 1f));
            Ui.Text(panel, Texts.VoyageReturnTitle, 40, Palette.Accent, TextAlignmentOptions.Center, true).rectTransform.Place(0, 1, 1, 1, 20, -84, 20, 24);
            var lines = new List<string>
            {
                Texts.VoyageReturnPay(e.amount),
                Texts.VoyageMirror,
                Texts.VoyageAge(e.ints[0]) + "  " + Texts.BodyMax + " " + e.ints[1] + "  " + Texts.HealthDebt + " " + e.ints[2],
                Texts.VoyageBankExpired,
            };
            switch (e.ints[4])
            {
                case 1: lines.Add(Texts.VoyagePositionOpen(e.amount3)); break;
                case 2: lines.Add(Texts.VoyagePositionLiquidated(e.amount2)); break;
            }
            Ui.Text(panel, string.Join("\n\n", lines), 24, align: TextAlignmentOptions.Center).rectTransform.Place(0, 0, 1, 1, 30, 110, 30, 110);
            yield return WaitButton(panel, Texts.Confirm);
            Destroy(black.gameObject);
        }

        // ---------- E7: settlement (monthly, D1) ----------

        IEnumerator Settlement(GameEvent e)
        {
            var loop = _app.Loop;
            var s = loop.Data.stats;
            var scrim = Scrim(new Color(0, 0, 0, 0.92f));
            var panel = Panel(scrim, 860, 700);
            Ui.Text(panel, Texts.E7TitleMonth(e.count), 34, Palette.Accent, TextAlignmentOptions.Center, true).rectTransform.Place(0, 1, 1, 1, 20, -70, 20, 20);

            long final = loop.TotalAssets;
            string Row(string label, string value) => "<color=" + Ui.Hex(Palette.Dim) + ">" + label + "</color>  " + value;
            var rows = new[]
            {
                Row(Texts.E7Start, Fmt.Won(Rules.StartingCash)) + "  →  " + Row(Texts.E7Final, "<color=" + Ui.Hex(final >= Rules.StartingCash ? Palette.Up : Palette.Down) + ">" + Fmt.Won(final) + "</color>"),
                Row(Texts.E7Labor, s.workedSlots * Rules.RestaurantHours + "시간 (" + s.workedSlots + "회)"),
                Row(Texts.E7CoinPnl, Fmt.SignedWon(s.coinRealized)) + "   " + Row(Texts.E7Liquidations, s.liquidations + "회"),
                Row(Texts.E7LottoSpent, Fmt.Won(s.lottoSpent)) + "   " + Row(Texts.E7LottoWon, Fmt.Won(s.lottoWon)),
                Row(Texts.E7Max, Fmt.Won(s.maxAssets)) + "   " + Row(Texts.E7Min, Fmt.Won(s.minAssets)),
                Row(Texts.E7Hope, s.hopeCircuits + "회"),
                Row(Texts.E7Prayers, s.prayers + "회") + "   " + Row(Texts.E7Luck, s.luckUses + "회"),
                Row(Texts.E7RageBets, s.rageBets + "회") + "   " + Row(Texts.E7GhostMissed, Fmt.Won(s.ghostMissedMoney)),
                Row(Texts.VoyageCount, s.voyages + "회") + "   " + Row(Texts.Age, Texts.AgeLabel(loop.Age)),
                Row(Texts.BodyMax, loop.Data.bodyMax.ToString()) + "   " + Row(Texts.HealthDebt, loop.Data.healthDebt.ToString()),
            };
            Ui.Text(panel, string.Join("\n", rows), 24, align: TextAlignmentOptions.TopLeft).rectTransform.Place(0, 1, 1, 1, 40, -400, 40, 90);

            var scenes = new List<string>();
            for (int i = 0; i < EventIds.Codex.Length; i++)
            {
                bool seen = s.eventsSeen.Contains(EventIds.Codex[i]);
                scenes.Add((seen ? "<color=" + Ui.Hex(Palette.Accent) + ">[O]</color> " : "<color=" + Ui.Hex(Palette.Dim) + ">[ ]</color> ") + Texts.SceneNames[i]);
            }
            Ui.Text(panel, Texts.E7Scenes, 24, Palette.Dim, bold: true).rectTransform.Place(0, 1, 1, 1, 40, -440, 40, 410);
            Ui.Text(panel, string.Join("\n", scenes.GetRange(0, 3)), 22, align: TextAlignmentOptions.TopLeft).rectTransform.Place(0, 1, 0.5f, 1, 40, -560, 0, 445);
            Ui.Text(panel, string.Join("\n", scenes.GetRange(3, 3)), 22, align: TextAlignmentOptions.TopLeft).rectTransform.Place(0.5f, 1, 1, 1, 0, -560, 40, 445);

            int pick = -1;
            Ui.Button(panel, Texts.E7Continue, () => pick = 1, 26).Rect.Place(0.05f, 0, 0.5f, 0, 0, 26, 0, -90);
            Ui.Button(panel, Texts.E7Again, () => pick = 0, 26).Rect.Place(0.55f, 0, 0.95f, 0, 0, 26, 0, -90);
            while (pick < 0) yield return null;
            Destroy(scrim.gameObject);
            if (pick == 0) _app.StartNewLife();
            else _afterPop = () => _app.ContinueLife();
        }
    }
}
