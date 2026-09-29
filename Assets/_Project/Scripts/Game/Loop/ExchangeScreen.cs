using AllIn.Core;
using AllIn.Core.Actions;
using AllIn.Game.UI;
using TMPro;
using UnityEngine;

namespace AllIn.Game.LoopScene
{
    /// <summary>B5 v0.3 "돈의 세 칸": bank / domestic exchange / overseas wallet tabs, with free (no-slot) transfers.</summary>
    public sealed class ExchangeScreen
    {
        LoopApp _app;
        GameObject _root;
        TextMeshProUGUI _balanceText, _subText, _lockText, _reason;
        RectTransform[] _tabPanels;
        Ui.Btn[] _tabButtons;
        int _tab; // 0 bank, 1 domestic, 2 overseas

        public bool Visible => _root.activeSelf;

        public static ExchangeScreen Create(LoopApp app)
        {
            var s = new ExchangeScreen { _app = app };
            s.Build();
            return s;
        }

        void Build()
        {
            var canvas = Ui.Canvas("ExchangeScreen", 11);
            _root = canvas.gameObject;
            Ui.Image(canvas.transform, "Scrim", Palette.Scrim, raycast: true);
            var panel = Ui.Image(canvas.transform, "Panel", Palette.Panel, raycast: true).rectTransform.Box(0.5f, 0.5f, 900, 560);

            var close = Ui.Button(panel, Texts.Close, Hide, 22);
            close.Rect.Place(1, 1, 1, 1, -130, -60, 20, 16);

            string[] labels = { Texts.WalletBank, Texts.WalletDomestic, Texts.WalletOverseas };
            _tabButtons = new Ui.Btn[3];
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                _tabButtons[i] = Ui.Button(panel, labels[i], () => { _tab = idx; Refresh(); }, 24);
                _tabButtons[i].Rect.Place(0, 1, 0, 1, 30 + i * 280, -60, -(30 + (i + 1) * 280 - 20), 0);
            }

            _balanceText = Ui.Text(panel, "", 44, bold: true);
            _balanceText.rectTransform.Place(0, 1, 1, 1, 30, -170, 30, 116);
            _subText = Ui.Text(panel, "", 22, Palette.Dim);
            _subText.rectTransform.Place(0, 1, 1, 1, 30, -210, 30, 176);
            _lockText = Ui.Text(panel, "", 24, Palette.Accent);
            _lockText.rectTransform.Place(0, 1, 1, 1, 30, -250, 30, 216);

            _tabPanels = new RectTransform[3];
            _tabPanels[0] = BuildBankTab(panel);
            _tabPanels[1] = BuildDomesticTab(panel);
            _tabPanels[2] = BuildOverseasTab(panel);

            _reason = Ui.Text(panel, "", 20, Palette.Accent, TextAlignmentOptions.Center);
            _reason.rectTransform.Place(0, 0, 1, 0, 30, 4, 30, -30);

            _root.SetActive(false);
        }

        RectTransform BuildBankTab(Transform parent)
        {
            var t = Ui.Rect(parent, "BankTab").Place(0, 0, 1, 1, 30, 30, 30, 280);
            Ui.Text(t, Texts.WalletDeposit + " → " + Texts.WalletDomestic, 22, Palette.Dim).rectTransform.Place(0, 1, 1, 1, 0, -30, 0, 0);
            AmountButtons(t, 60, frac => Transfer(() => _app.Loop.TransferBankToDomestic(Portion(_app.Loop.Data.bankCash, frac))));
            return t;
        }

        RectTransform BuildDomesticTab(Transform parent)
        {
            var t = Ui.Rect(parent, "DomesticTab").Place(0, 0, 1, 1, 30, 30, 30, 280);
            Ui.Text(t, Texts.WalletTransferSend + " → " + Texts.WalletOverseas, 22, Palette.Dim).rectTransform.Place(0, 1, 1, 1, 0, -30, 0, 0);
            AmountButtons(t, 60, frac => Transfer(() => _app.Loop.TransferDomesticToOverseas(Portion(_app.Loop.Data.domesticCash, frac))));
            return t;
        }

        RectTransform BuildOverseasTab(Transform parent)
        {
            var t = Ui.Rect(parent, "OverseasTab").Place(0, 0, 1, 1, 30, 30, 30, 280);
            Ui.Text(t, Texts.WalletWithdraw + " → " + Texts.WalletDomestic, 22, Palette.Dim).rectTransform.Place(0, 1, 1, 1, 0, -30, 0, 0);
            AmountButtons(t, 60, frac => Transfer(() => _app.Loop.TransferOverseasToDomestic(Portion(_app.Loop.Data.overseasCash, frac))));
            return t;
        }

        static long Portion(long total, double frac) => (long)System.Math.Round(total * frac);

        void AmountButtons(Transform parent, float y, System.Action<double> onPick)
        {
            var fracs = new[] { 0.25, 0.5, 1.0 };
            var names = new[] { "25%", "50%", "전액" };
            for (int i = 0; i < fracs.Length; i++)
            {
                double f = fracs[i];
                var b = Ui.Button(parent, names[i], () => onPick(f), 24);
                b.Rect.Box(0.16f + i * 0.34f, 0.55f, 230, 90);
            }
        }

        void Transfer(System.Func<Availability> doTransfer)
        {
            var r = doTransfer();
            _reason.text = r.Ok ? "" : r.Reason;
            _app.Save();
            Refresh();
        }

        public void Show()
        {
            if (_app.Events.Busy || _app.Watch.Busy) return;
            _root.SetActive(true);
            _app.Loop.NoteExchangeScreenOpened();
            Refresh();
        }

        public void Hide() => _root.SetActive(false);

        public void SelectTab(int tab)
        {
            _tab = Mathf.Clamp(tab, 0, 2);
            Refresh();
        }

        public void Refresh()
        {
            if (_root == null || !_root.activeSelf) return;
            var loop = _app.Loop;
            var d = loop.Data;

            for (int i = 0; i < 3; i++)
            {
                _tabButtons[i].SetOn(_tab == i);
                _tabPanels[i].gameObject.SetActive(_tab == i);
            }

            long overseasPnl = d.hasPosition ? loop.UnrealizedPnl : 0;
            switch (_tab)
            {
                case 0:
                    _balanceText.text = Fmt.Won(d.bankCash);
                    _subText.text = Texts.WalletBank;
                    _lockText.text = "";
                    break;
                case 1:
                    _balanceText.text = Fmt.Won(d.domesticCash);
                    _subText.text = Texts.WalletDomestic + (d.hasPosition ? "   " + Texts.CoinUnrealized + " " + Fmt.SignedWon(overseasPnl) : "");
                    _lockText.text = loop.DomesticLocked ? Texts.WalletLockRemaining(HoursRemaining(loop)) : Texts.WalletUnlocked;
                    break;
                default:
                    _balanceText.text = Fmt.Won(d.overseasCash);
                    _subText.text = Texts.WalletOverseas + PendingSuffix(d);
                    _lockText.text = "";
                    break;
            }
        }

        static int HoursRemaining(Core.Loop loop) => Mathf.CeilToInt(loop.DomesticLockRemainingSlots * 24f / Rules.SlotsPerDay);

        static string PendingSuffix(SaveData d)
        {
            long sum = 0;
            foreach (var t in d.pendingTransfers) sum += t.amount;
            return sum > 0 ? "   " + Texts.WalletInTransit + " " + Fmt.Won(sum) : "";
        }
    }
}
