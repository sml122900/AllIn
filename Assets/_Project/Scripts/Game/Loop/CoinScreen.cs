using System;
using AllIn.Core;
using AllIn.Core.Actions;
using AllIn.Game.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AllIn.Game.LoopScene
{
    /// <summary>BTC/USDT perpetual screen: price, mini chart, position, and the open form (direction · leverage · commit slider).</summary>
    public sealed class CoinScreen
    {
        LoopApp _app;
        GameObject _root;
        TextMeshProUGUI _price, _change, _positionInfo, _commitInfo, _reason;
        Image[] _bars;
        RectTransform _openForm, _positionPanel;
        Ui.Btn[] _dirButtons, _levButtons;
        Ui.Btn _openButton, _closeButton;
        Slider _slider;
        Direction _dir = Direction.Long;
        int _leverage = Rules.DefaultLeverage;
        bool _silentSlider;

        public bool Visible => _root.activeSelf;

        public static CoinScreen Create(LoopApp app)
        {
            var s = new CoinScreen { _app = app };
            s.Build();
            return s;
        }

        void Build()
        {
            var canvas = Ui.Canvas("CoinScreen", 10);
            _root = canvas.gameObject;
            Ui.Image(canvas.transform, "Scrim", Palette.Scrim, raycast: true);
            var panel = Ui.Image(canvas.transform, "Panel", Palette.Panel, raycast: true).rectTransform.Box(0.5f, 0.5f, 1000, 640);

            var title = Ui.Text(panel, Texts.CoinTitle, 26, Palette.Dim, bold: true);
            title.rectTransform.Place(0, 1, 1, 1, 30, -60, 30, 16);
            _price = Ui.Text(panel, "", 40, bold: true);
            _price.rectTransform.Place(0, 1, 1, 1, 30, -110, 30, 56);
            _change = Ui.Text(panel, "", 22, align: TextAlignmentOptions.MidlineRight);
            _change.rectTransform.Place(0, 1, 1, 1, 30, -110, 30, 56);
            var close = Ui.Button(panel, Texts.Close, Hide, 22);
            close.Rect.Place(1, 1, 1, 1, -130, -60, 20, 16);

            // chart
            var chart = Ui.Image(panel, "Chart", Palette.PanelLight).rectTransform.Place(0, 1, 1, 1, 30, -290, 30, 120);
            _bars = new Image[Rules.PriceHistoryLength];
            for (int i = 0; i < _bars.Length; i++)
            {
                _bars[i] = Ui.Image(chart, "bar" + i, Palette.Up);
                float x0 = i / (float)_bars.Length, x1 = (i + 1) / (float)_bars.Length;
                _bars[i].rectTransform.Place(x0, 0, x1, 0.5f, 1, 0, 1, 0);
            }

            // open position panel
            _positionPanel = Ui.Rect(panel, "Position").Place(0, 0, 1, 1, 30, 30, 30, 310);
            _positionInfo = Ui.Text(_positionPanel, "", 24, align: TextAlignmentOptions.TopLeft);
            _positionInfo.rectTransform.Place(0, 0, 0.7f, 1, 0, 0, 0, 0);
            _closeButton = Ui.Button(_positionPanel, Texts.CoinClose + " →", OnClose, 30);
            _closeButton.Rect.Place(0.72f, 0.35f, 1, 0.75f);

            // open form
            _openForm = Ui.Rect(panel, "OpenForm").Place(0, 0, 1, 1, 30, 30, 30, 310);
            _dirButtons = new[]
            {
                Ui.Button(_openForm, Texts.CoinLong, () => { _dir = Direction.Long; Refresh(); }, 26),
                Ui.Button(_openForm, Texts.CoinShort, () => { _dir = Direction.Short; Refresh(); }, 26),
            };
            _dirButtons[0].Rect.Place(0, 1, 0, 1, 0, -60, -150, 0);
            _dirButtons[1].Rect.Place(0, 1, 0, 1, 160, -60, -310, 0);
            var levLabel = Ui.Text(_openForm, Texts.CoinLeverage, 22, Palette.Dim);
            levLabel.rectTransform.Place(0, 1, 0, 1, 340, -60, -470, 0);
            _levButtons = new Ui.Btn[Rules.Leverages.Length];
            for (int i = 0; i < _levButtons.Length; i++)
            {
                int lev = Rules.Leverages[i];
                _levButtons[i] = Ui.Button(_openForm, "x" + lev, () => { _leverage = lev; Refresh(); }, 24);
                float x = 470 + i * 94;
                _levButtons[i].Rect.Place(0, 1, 0, 1, x, -60, -(x + 86), 0);
            }
            _slider = Ui.Slider(_openForm, _ => { if (!_silentSlider) RefreshCommitInfo(); });
            ((RectTransform)_slider.transform).Place(0, 1, 1, 1, 0, -130, 0, 80);
            _commitInfo = Ui.Text(_openForm, "", 22, align: TextAlignmentOptions.TopLeft);
            _commitInfo.rectTransform.Place(0, 0, 0.7f, 1, 0, 0, 0, 140);
            _openButton = Ui.Button(_openForm, Texts.CoinOpen + " →", OnOpen, 30);
            _openButton.Rect.Place(0.72f, 0.05f, 1, 0.45f);

            _reason = Ui.Text(panel, "", 20, Palette.Accent, TextAlignmentOptions.Center);
            _reason.rectTransform.Place(0, 0, 1, 0, 30, 4, 30, -30);

            _root.SetActive(false);
        }

        public void Show()
        {
            if (_app.Events.Busy) return;
            _root.SetActive(true);
            _leverage = _app.Loop.SuggestedLeverage; // hope circuit nudges the default one step up
            _silentSlider = true;
            _slider.value = 0.5f;
            _silentSlider = false;
            if (_app.Loop.OnCoinScreenOpened() != null) _app.Save(); // E4 queued; the presenter shows it on top
            Refresh();
        }

        public void Hide() => _root.SetActive(false);

        void OnOpen()
        {
            long commit = Commit();
            var r = _app.Perform(new CoinOpenAction(_dir, _leverage, commit));
            if (r.Ok) Hide(); // the slot is over; back to the room
            else _reason.text = r.Message;
        }

        void OnClose()
        {
            var r = _app.Perform(CoinCloseAction.Instance);
            if (r.Ok) Hide();
            else _reason.text = r.Message;
        }

        long Commit()
        {
            var loop = _app.Loop;
            long cash = loop.Data.cash;
            if (cash <= 0) return 0;
            double minFrac = Math.Min(1.0, loop.MinCommit / (double)cash);
            double frac = minFrac + (1 - minFrac) * _slider.value;
            return Math.Max(loop.MinCommit, Math.Min(cash, (long)Math.Round(cash * frac)));
        }

        public void Refresh()
        {
            if (_root == null || !_root.activeSelf) return;
            var loop = _app.Loop;
            var d = loop.Data;

            _price.text = Fmt.Won(d.price);
            int n = d.priceHistory.Count;
            if (n >= 2)
            {
                double prev = d.priceHistory[n - 2];
                double ch = d.price / prev - 1;
                _change.text = "<color=" + Ui.Hex(ch >= 0 ? Palette.Up : Palette.Down) + ">" + (ch >= 0 ? "+" : "") + Fmt.Pct(ch, 2) + "</color>";
            }
            DrawChart(d);

            bool has = d.hasPosition;
            _positionPanel.gameObject.SetActive(has);
            _openForm.gameObject.SetActive(!has);
            _reason.text = Texts.CoinTakesSlot + (d.slot == Slot.Night ? " " + Texts.AllNighterWarning : "");

            if (has)
            {
                var p = d.position;
                long u = loop.UnrealizedPnl;
                _positionInfo.text =
                    (p.direction == Direction.Long ? Texts.CoinLong : Texts.CoinShort) + " x" + p.leverage + "\n" +
                    Texts.CoinMargin + " " + Fmt.Won(p.margin) + "  (투입 " + Fmt.Won(p.committed) + ")\n" +
                    "진입가 " + Fmt.Won(p.entryPrice) + "\n" +
                    Texts.CoinLiqPrice + " <color=" + Ui.Hex(Palette.Down) + ">" + Fmt.Won(CoinRules.LiquidationPrice(p)) + "</color>\n" +
                    Texts.CoinUnrealized + " <color=" + Ui.Hex(u >= 0 ? Palette.Up : Palette.Down) + ">" + Fmt.SignedWon(u) + "</color>";
                var can = loop.Check(CoinCloseAction.Instance);
                _closeButton.Interactable = can.Ok;
                if (!can.Ok && !string.IsNullOrEmpty(can.Reason)) _reason.text = can.Reason;
            }
            else
            {
                _dirButtons[0].SetOn(_dir == Direction.Long);
                _dirButtons[1].SetOn(_dir == Direction.Short);
                for (int i = 0; i < _levButtons.Length; i++) _levButtons[i].SetOn(Rules.Leverages[i] == _leverage);
                RefreshCommitInfo();
            }
        }

        void RefreshCommitInfo()
        {
            var loop = _app.Loop;
            long commit = Commit();
            if (commit <= 0)
            {
                _commitInfo.text = Texts.CoinNoCash;
                _openButton.Interactable = false;
                return;
            }
            var preview = CoinRules.Open(_dir, _leverage, commit, loop.Data.price, loop.AbsSlot);
            string chasing = loop.Data.mental < Rules.ChasingLossesBelow ? "\n<color=" + Ui.Hex(Palette.Accent) + ">" + Texts.CoinBelowMin + "</color>" : "";
            _commitInfo.text =
                "투입 " + Fmt.Won(commit) + "  (현금의 " + Fmt.Pct(commit / (double)loop.Data.cash, 0) + ")\n" +
                "명목 " + Fmt.Won(CoinRules.Notional(preview)) + " · 진입 수수료 " + Fmt.Won((long)CoinRules.EntryFee(preview)) + "\n" +
                Texts.CoinLiqPrice + " " + Fmt.Won(CoinRules.LiquidationPrice(preview)) + chasing;
            _openButton.Interactable = loop.Check(new CoinOpenAction(_dir, _leverage, commit)).Ok;
        }

        void DrawChart(SaveData d)
        {
            var h = d.priceHistory;
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var v in h) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
            double span = Math.Max(hi - lo, hi * 0.002);
            int offset = _bars.Length - h.Count;
            for (int i = 0; i < _bars.Length; i++)
            {
                int k = i - offset;
                if (k < 0) { _bars[i].enabled = false; continue; }
                _bars[i].enabled = true;
                float y = (float)((h[k] - lo) / span);
                _bars[i].rectTransform.Place((float)i / _bars.Length, 0, (float)(i + 1) / _bars.Length, 0.04f + 0.94f * y, 1, 0, 1, 0);
                bool up = k == 0 || h[k] >= h[k - 1];
                _bars[i].color = up ? Palette.Up : Palette.Down;
            }
        }
    }
}
