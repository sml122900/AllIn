using System.Collections;
using AllIn.Core;
using AllIn.Game.UI;
using TMPro;
using UnityEngine;

namespace AllIn.Game.LoopScene
{
    /// <summary>
    /// B5a "관전": plays a SubTickWalk in real time (sub-ticks 1–10 @ 0.5s, 11–12 @ 1.5s per DESIGN.md) once the
    /// watch threshold has been crossed. Core already decided WHETHER to trigger (SubTickWalk.WatchTriggered) and
    /// precomputed the whole path — this only animates it and, unless asleep/working, lets the player call
    /// Step-adjacent Pray()/AddMargin()/StopLoss() on the same walk instance mid-animation.
    /// </summary>
    public sealed class WatchScreen
    {
        LoopApp _app;
        GameObject _root;
        RectTransform _panel;
        TextMeshProUGUI _priceText, _liqText, _pctText, _workLine;
        RectTransform _dangerFill;
        Ui.Btn _prayBtn, _marginBtn, _stopBtn;

        public bool Busy { get; private set; }

        public static WatchScreen Create(LoopApp app)
        {
            var s = new WatchScreen { _app = app };
            s.Build();
            return s;
        }

        void Build()
        {
            var canvas = Ui.Canvas("WatchScreen", 20);
            _root = canvas.gameObject;
            Ui.Image(canvas.transform, "Scrim", new Color(0.1f, 0, 0, 0.75f));
            var panel = Ui.Image(canvas.transform, "Panel", Palette.Panel).rectTransform.Box(0.5f, 0.5f, 820, 520);
            _panel = panel;

            Ui.Text(panel, Texts.WatchTitle, 30, Palette.Down, TextAlignmentOptions.Center, true).rectTransform.Place(0, 1, 1, 1, 20, -60, 20, 20);

            _priceText = Ui.Text(panel, "", 34, bold: true, align: TextAlignmentOptions.Center);
            _priceText.rectTransform.Place(0, 1, 1, 1, 20, -120, 20, 66);
            _liqText = Ui.Text(panel, "", 22, Palette.Down, TextAlignmentOptions.Center);
            _liqText.rectTransform.Place(0, 1, 1, 1, 20, -156, 20, 120);

            var dangerBg = Ui.Image(panel, "DangerBg", Palette.PanelLight).rectTransform.Place(0, 1, 1, 1, 40, -220, 40, 170);
            _dangerFill = Ui.Image(dangerBg, "DangerFill", Palette.Down).rectTransform;
            _dangerFill.Place(0, 0, 0, 1);
            _pctText = Ui.Text(panel, "", 22, Palette.Accent, TextAlignmentOptions.Center);
            _pctText.rectTransform.Place(0, 1, 1, 1, 20, -260, 20, 226);

            _workLine = Ui.Text(panel, Texts.WatchWorkLine, 24, Palette.Dim, TextAlignmentOptions.Center);
            _workLine.rectTransform.Place(0, 0, 1, 0, 20, 140, 20, -300);

            _marginBtn = Ui.Button(panel, Texts.WatchAddMargin, null, 24);
            _marginBtn.Rect.Box(0.22f, 0.22f, 320, 64);
            _stopBtn = Ui.Button(panel, Texts.WatchStopLoss, null, 24);
            _stopBtn.Rect.Box(0.78f, 0.22f, 320, 64);
            _prayBtn = Ui.Button(panel, Texts.WatchPray, null, 28);
            _prayBtn.Rect.Box(0.5f, 0.09f, 280, 60);

            _root.SetActive(false);
        }

        /// <summary>Drives `walk` to completion in real time, then hands off to Loop.FinishWatch. LoopApp starts this as a coroutine.</summary>
        public IEnumerator Run(SubTickWalk walk, bool workOnly)
        {
            Busy = true;
            _root.SetActive(true);
            _workLine.gameObject.SetActive(workOnly);
            _marginBtn.Button.gameObject.SetActive(!workOnly);
            _stopBtn.Button.gameObject.SetActive(!workOnly);

            var loop = _app.Loop;
            _prayBtn.Button.onClick.RemoveAllListeners();
            _prayBtn.Button.onClick.AddListener(() => { walk.Pray(); Refresh(loop, walk); });
            _marginBtn.Button.onClick.RemoveAllListeners();
            _marginBtn.Button.onClick.AddListener(() =>
            {
                long amount = (long)System.Math.Round(loop.Data.overseasCash * 0.25);
                walk.AddMargin(amount);
                Refresh(loop, walk);
            });
            _stopBtn.Button.onClick.RemoveAllListeners();
            _stopBtn.Button.onClick.AddListener(() => walk.StopLoss());

            Refresh(loop, walk);
            while (!walk.Done)
            {
                if (walk.LuckPending)
                {
                    // E3: the button comes up at the trigger sub-tick and nothing moves until it is pressed (even while working)
                    Refresh(loop, walk);
                    yield return LuckOffer.Show(_panel, 0.5f, 0.40f, 0, 0, 440).Run(Texts.LuckCoinLine, () => walk.UseLuck());
                    Refresh(loop, walk);
                    continue;
                }
                yield return new WaitForSecondsRealtime(walk.Index < 10 ? 0.5f : 1.5f);
                if (walk.Done) break; // StopLoss may have fired while waiting
                walk.Step();
                Refresh(loop, walk);
            }
            Refresh(loop, walk);
            yield return new WaitForSecondsRealtime(0.5f);

            _root.SetActive(false);
            Busy = false;
            loop.FinishWatch(walk);
            _app.Save();
            _app.Refresh();
        }

        void Refresh(Core.Loop loop, SubTickWalk walk)
        {
            long price = loop.Data.price;
            long? liq = walk.CurrentLiquidationPrice;
            _priceText.text = Fmt.Won(price);
            _liqText.text = liq.HasValue ? Texts.CoinLiqPrice + " " + Fmt.Won(liq.Value) : "";

            double pct = 0;
            if (liq.HasValue && loop.Data.hasPosition)
            {
                var p = loop.Data.position;
                double lossNow = -CoinRules.UnrealizedPnl(p, price);
                double lossAtLiq = CoinRules.LiquidationLoss(p);
                pct = lossAtLiq > 0 ? Mathf.Clamp01((float)(lossNow / lossAtLiq)) : 0;
            }
            _dangerFill.Place(0, 0, (float)pct, 1);
            _pctText.text = Texts.WatchPercentToLiq(1 - pct);

            bool canAddMargin = !walk.Done && loop.Data.overseasCash > 0 && !walk.LuckPending && !walk.LuckUsed;
            _marginBtn.Interactable = canAddMargin;
            _stopBtn.Interactable = !walk.Done && !walk.LuckPending && !walk.LuckUsed;
        }
    }
}
