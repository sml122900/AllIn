using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AllIn.Core;
using AllIn.Core.Actions;
using AllIn.Game.LoopScene;
using UnityEngine;
using UnityEngine.UI;

namespace AllIn.Game.Dev
{
    /// <summary>
    /// Dev-only screenshot driver. Inert unless the player is launched with <c>-allinShots &lt;dir&gt;</c>:
    /// then it saves into that dir (never the real save), plays a scripted 30 days, screenshots every
    /// screen and event, and quits. Not reachable from any in-game UI.
    /// </summary>
    public sealed class DevCapture : MonoBehaviour
    {
        string _dir;
        int _shot;
        readonly HashSet<string> _captured = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Arm()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-allinShots");
            if (i < 0 || i + 1 >= args.Length) return;
            string dir = args[i + 1];
            Directory.CreateDirectory(dir);
            SaveIO.DirectoryOverride = dir;
            Application.runInBackground = true; // keep playing when the window has no focus
            SaveIO.Delete();
            var go = new GameObject("DevCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<DevCapture>()._dir = dir;
        }

        IEnumerator Start()
        {
            LoopApp app = null;
            while ((app = FindAnyObjectByType<LoopApp>()) == null) yield return null;

            yield return Wait(3.3f);
            yield return Shot("opening");
            yield return DrainEvents(app);
            yield return Shot("hud");

            // B5 v0.3 "돈의 세 칸": move everything bank -> domestic (starts the 24h lock) -> shot both tabs.
            app.Exchange.Show();
            yield return Wait(0.2f);
            yield return Shot("exchange_bank");
            app.Loop.TransferBankToDomestic(app.Loop.Data.bankCash);
            app.Save();
            app.Exchange.SelectTab(1); // 국내 거래소 — show the lock countdown this deposit just started
            yield return Wait(0.6f); // first time these glyphs (약/시간/남음) are requested — give the dynamic TMP atlas a beat
            yield return Shot("exchange_domestic_locked");
            app.Exchange.Hide();

            app.Coin.Show();
            yield return Wait(0.3f);
            yield return Shot("coin_form");
            app.Coin.Hide();

            app.Lotto.Show();
            for (int i = 0; i < 3; i++) app.Perform(LottoBuyAction.Instance);
            yield return Wait(0.3f);
            yield return Shot("lotto");
            app.Lotto.Hide();

            int guard = 0;
            while (!app.Loop.Finished && guard++ < 400)
            {
                var loop = app.Loop;
                var d = loop.Data;

                // keep sweeping the chain forward: domestic -> overseas once unlocked, re-fund from the bank
                // once both are dry (mirrors sim E's "청산 체인" strategy — inputs are scripted, outcomes aren't).
                if (!loop.DomesticLocked && d.domesticCash > 0) loop.TransferDomesticToOverseas(d.domesticCash);
                else if (!loop.DomesticLocked && d.domesticCash <= 0 && d.overseasCash <= 0 && d.bankCash > 0)
                    loop.TransferBankToDomestic(d.bankCash / 2);

                // a crude "player": lotto on dawns (instant), then exactly one slot action — close in the
                // afternoon, a reckless 100x every other night, the job in the evening, otherwise rest
                if (d.slot == Slot.Dawn) app.Perform(LottoBuyAction.Instance);
                if (d.day == 12 && d.slot == Slot.Morning && !_captured.Contains("hope_forced"))
                {
                    _captured.Add("hope_forced");
                    d.mental = 90; d.hasLastWin = true; d.lastWin = new LastWin { realized = 300_000, committed = 1_000_000, heldTicks = 2 };
                    app.Coin.Show();
                    yield return Wait(0.2f);
                    yield return DrainEvents(app);
                    app.Coin.Hide();
                }
                if (d.hasPosition && d.slot == Slot.Afternoon) app.Perform(CoinCloseAction.Instance);
                else if (d.slot == Slot.Night && !d.hasPosition && d.day % 2 == 0 && d.overseasCash > 100_000)
                    app.Perform(new CoinOpenAction(Direction.Long, 100, d.overseasCash));
                else if (d.slot == Slot.Evening && app.Loop.Check(WorkRestaurantAction.Instance).Ok) app.Perform(WorkRestaurantAction.Instance);
                else app.Perform(RestAction.Instance);

                yield return WatchIfShowing(app);
                yield return null;
                yield return DrainEvents(app); // drain E1/E2/E3 before shooting the tilt screen so it isn't hidden behind a popup

                if (d.tilted && !_captured.Contains("tilt"))
                {
                    _captured.Add("tilt");
                    app.Coin.Show();
                    yield return Wait(0.3f);
                    yield return Shot("tilt");
                    app.Coin.Hide();
                }
            }
            yield return Shot("end_hud");

            // D (v0.4): [계속 살기] -> go broke on purpose -> E9 -> the ship (with a position left to decide about) -> the return.
            ClickButtonByLabel(app, Texts.E7Continue);
            yield return Wait(0.8f);
            yield return DrainEvents(app);
            yield return Shot("continued_hud");
            {
                var loop = app.Loop;
                var d = loop.Data;
                // 1) rent falls due tomorrow with an empty bank (money left on the exchange only) -> E9 -> hold on
                d.bankCash = 0; d.domesticCash = 0; d.overseasCash = 300_000; d.pendingTransfers.Clear();
                d.healthDebt = 0;
                d.nextRentDay = d.day + 1;
                app.Save();
                app.Refresh();
                int g2 = 0;
                while (g2++ < 40 && !(loop.HasEvent && d.pendingEvents.Any(e => e.id == EventIds.Bankrupt)))
                {
                    app.Perform(RestAction.Instance);
                    yield return WatchIfShowing(app);
                    yield return null;
                    yield return DrainUntilBankrupt(app);
                }
                yield return DrainUntilBankrupt(app);
                yield return Wait(1.0f);
                _captured.Add(EventIds.Bankrupt);
                yield return Shot("event_E9");
                ClickButtonByLabel(app, Texts.E9Hold);
                yield return Wait(0.5f);
                yield return Shot("arrears_countdown_hud");
                // 2) two hungry days (the bank is empty, so the evening meal can't be paid)
                for (int i = 0; i < 12; i++)
                {
                    app.Perform(RestAction.Instance);
                    yield return WatchIfShowing(app);
                    yield return null;
                    if (d.pendingEvents.Any(e => e.id == EventIds.Hunger) && !_captured.Contains("hunger"))
                    {
                        _captured.Add("hunger");
                        yield return Wait(0.8f);
                        yield return Shot("hunger_popup");
                    }
                    yield return DrainUntilBankrupt(app);
                }
                yield return Shot("starved_hud");
                // 3) the arrears get paid: released
                d.bankCash = d.rentOwed + 3 * Rules.FoodPerDay;
                app.Refresh();
                int g3 = 0;
                while (g3++ < 12 && loop.IsBankrupt)
                {
                    app.Perform(RestAction.Instance);
                    yield return null;
                    yield return DrainUntilBankrupt(app);
                }
                yield return Wait(0.4f);
                yield return Shot("arrears_released_hud");
                // 4) broke again -> E9 -> the ship
                d.bankCash = 0; d.domesticCash = 0; d.overseasCash = 300_000; d.pendingTransfers.Clear();
                d.nextRentDay = d.day + 1;
                d.hasPosition = true;
                d.position = CoinRules.Open(Direction.Long, 5, 200_000, d.price, loop.AbsSlot);
                app.Save();
                app.Refresh();
                g2 = 0;
                while (g2++ < 40 && !(loop.HasEvent && d.pendingEvents.Any(e => e.id == EventIds.Bankrupt)))
                {
                    app.Perform(RestAction.Instance);
                    yield return WatchIfShowing(app);
                    yield return null;
                    yield return DrainUntilBankrupt(app);
                }
                yield return DrainUntilBankrupt(app);
                yield return Wait(0.6f);
                ClickButtonByLabel(app, Texts.E9Board);
                yield return Wait(0.5f);
                if (d.hasPosition)
                {
                    yield return Shot("voyage_confirm");
                    ClickButtonByLabel(app, Texts.VoyageLeave);
                }
                yield return Wait(2.4f);
                yield return Shot("voyage_board");
                yield return Wait(4.6f);
                yield return Shot("voyage_skip");
                yield return DrainEvents(app); // Confirm, then the return scene (shot on its own), then Confirm
                yield return Wait(0.5f);
                yield return Shot("after_voyage_hud");
                UnityEngine.Debug.Log("[DevCapture] voyage result: day " + d.day + " age " + loop.Age + " bank " + d.bankCash + " bodyMax " + d.bodyMax + " debt " + d.healthDebt + " voyages " + d.stats.voyages);
            }
            Application.Quit();
        }

        /// <summary>Drains popups but stops in front of E9, whose buttons a blind click would answer with "버틴다".</summary>
        IEnumerator DrainUntilBankrupt(LoopApp app)
        {
            int guard = 0;
            while (guard++ < 200)
            {
                yield return null;
                if (!app.Events.Busy && !app.Loop.HasEvent) yield break;
                if (app.Loop.HasEvent && app.Loop.Data.pendingEvents[0].id == EventIds.Bankrupt) yield break;
                yield return Wait(0.3f);
                ClickTopButton(app);
                yield return Wait(0.1f);
            }
        }

        static void ClickButtonByLabel(LoopApp app, string label)
        {
            foreach (var b in app.Events.GetComponentsInChildren<Button>(false))
            {
                var t = b.GetComponentInChildren<TMPro.TMP_Text>();
                if (t != null && t.text == label && b.interactable) { b.onClick.Invoke(); return; }
            }
            UnityEngine.Debug.LogWarning("[DevCapture] button not found: " + label);
        }

        /// <summary>B5a: LoopApp.Perform starts the watch scene as a background coroutine when it triggers. Catch
        /// it here for a screenshot, then wait for it to resolve before scripting the next slot.</summary>
        IEnumerator WatchIfShowing(LoopApp app)
        {
            if (!app.Watch.Busy) yield break;
            if (!_captured.Contains("watch"))
            {
                _captured.Add("watch");
                yield return Wait(0.4f);
                yield return Shot("watch");
            }
            while (app.Watch.Busy) yield return null;
        }

        IEnumerator DrainEvents(LoopApp app)
        {
            int guard = 0;
            while (guard++ < 200)
            {
                yield return null;
                if (!app.Events.Busy && !app.Loop.HasEvent) yield break;
                string id = app.Loop.HasEvent ? app.Loop.Data.pendingEvents[0].id : "event";
                bool first = _captured.Add(id);
                if (first)
                {
                    yield return Wait(id == EventIds.LottoDraw ? 8.5f : id == EventIds.HopeCircuit ? 1.8f : 2.0f);
                    yield return Shot("event_" + id);
                }
                else yield return Wait(id == EventIds.LottoDraw ? 7.5f : 0.1f);
                ClickTopButton(app);
                yield return Wait(0.1f);
                if (id == EventIds.Settlement) yield break; // leave the settlement screen up for the final shot
            }
        }

        static void ClickTopButton(LoopApp app)
        {
            var buttons = app.Events.GetComponentsInChildren<Button>(false).Where(b => b.interactable).ToList();
            if (buttons.Count == 0) return;
            var label = buttons.Last().GetComponentInChildren<TMPro.TMP_Text>();
            if (label != null && label.text == Texts.E7Again) return;
            buttons.Last().onClick.Invoke();
        }

        static IEnumerator Wait(float s) => new WaitForSecondsRealtime(s);

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, (++_shot).ToString("00") + "_" + name + ".png"));
            yield return null;
        }
    }
}
