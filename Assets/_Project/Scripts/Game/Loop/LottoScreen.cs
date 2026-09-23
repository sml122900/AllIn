using System.Text;
using AllIn.Core;
using AllIn.Core.Actions;
using AllIn.Game.UI;
using TMPro;
using UnityEngine;

namespace AllIn.Game.LoopScene
{
    /// <summary>Lotto counter: buy 5-game tickets for the next Saturday draw, see what you hold.</summary>
    public sealed class LottoScreen
    {
        LoopApp _app;
        GameObject _root;
        TextMeshProUGUI _info, _tickets, _reason;
        Ui.Btn _buy;

        public static LottoScreen Create(LoopApp app)
        {
            var s = new LottoScreen { _app = app };
            s.Build();
            return s;
        }

        void Build()
        {
            var canvas = Ui.Canvas("LottoScreen", 10);
            _root = canvas.gameObject;
            Ui.Image(canvas.transform, "Scrim", Palette.Scrim, raycast: true);
            var panel = Ui.Image(canvas.transform, "Panel", Palette.Panel, raycast: true).rectTransform.Box(0.5f, 0.5f, 820, 600);

            var title = Ui.Text(panel, Texts.LottoTitle, 30, bold: true);
            title.rectTransform.Place(0, 1, 1, 1, 30, -64, 30, 16);
            var close = Ui.Button(panel, Texts.Close, Hide, 22);
            close.Rect.Place(1, 1, 1, 1, -130, -60, 20, 16);

            _info = Ui.Text(panel, "", 22, Palette.Dim, TextAlignmentOptions.TopLeft);
            _info.rectTransform.Place(0, 1, 1, 1, 30, -150, 400, 76);
            _buy = Ui.Button(panel, Texts.LottoBuy, OnBuy, 22);
            _buy.Rect.Place(1, 1, 1, 1, -390, -150, 30, 84);

            _tickets = Ui.Text(panel, "", 22, align: TextAlignmentOptions.TopLeft);
            _tickets.rectTransform.Place(0, 0, 1, 1, 30, 50, 30, 170);
            _tickets.overflowMode = TextOverflowModes.Truncate;

            _reason = Ui.Text(panel, "", 20, Palette.Accent, TextAlignmentOptions.Center);
            _reason.rectTransform.Place(0, 0, 1, 0, 30, 10, 30, -44);
            _root.SetActive(false);
        }

        public void Show()
        {
            if (_app.Events.Busy) return;
            _root.SetActive(true);
            Refresh();
        }

        public void Hide() => _root.SetActive(false);

        void OnBuy()
        {
            var r = _app.Perform(LottoBuyAction.Instance);
            if (!r.Ok) _reason.text = r.Message;
        }

        public void Refresh()
        {
            if (_root == null || !_root.activeSelf) return;
            var loop = _app.Loop;
            var d = loop.Data;
            int draw = Calendar.NextSaturday(d.day);
            _info.text = "오늘 " + d.lottoBoughtToday + " / " + Rules.LottoMaxPerDay + "회\n" +
                         Texts.LottoTicketsFor(draw) + " " + Texts.SlotNames[(int)Slot.Evening] + " · 보유 " + loop.TicketsForNextDraw + "장" +
                         (draw > Rules.Days ? "  <color=" + Ui.Hex(Palette.Down) + ">(30일 안에 추첨 없음)</color>" : "");
            var can = loop.Check(LottoBuyAction.Instance);
            _buy.Interactable = can.Ok;
            _reason.text = can.Ok ? "" : can.Reason;

            // newest tickets first, 5 games each
            var sb = new StringBuilder();
            int shown = 0;
            for (int i = d.tickets.Count - 1; i >= 0 && shown < 3; i--, shown++)
            {
                var t = d.tickets[i];
                sb.Append("<color=" + Ui.Hex(Palette.Dim) + ">#" + t.serial + " · " + Texts.LottoTicketsFor(t.drawDay) + "</color>\n");
                for (int g = 0; g < Rules.LottoGamesPerTicket; g++)
                {
                    sb.Append((char)('A' + g)).Append("  ");
                    for (int k = 0; k < Rules.LottoPicks; k++) sb.Append(t.numbers[g * Rules.LottoPicks + k].ToString("00")).Append("  ");
                    sb.Append('\n');
                }
            }
            if (d.tickets.Count > 3) sb.Append("<color=" + Ui.Hex(Palette.Dim) + ">… 외 " + (d.tickets.Count - 3) + "장</color>");
            _tickets.text = sb.ToString();
        }
    }
}
