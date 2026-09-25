using AllIn.Game.UI;
using AllIn.Holdem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AllIn.Game.HoldemScene
{
    /// <summary>A playing card drawn with an Image and a TMP label (no card art yet).</summary>
    public sealed class CardView
    {
        public RectTransform Root;
        Image _face;
        TextMeshProUGUI _label;
        static readonly Color Back = new Color(0.16f, 0.22f, 0.45f);
        static readonly Color Red = new Color(0.85f, 0.12f, 0.15f);
        static readonly Color Black = new Color(0.08f, 0.08f, 0.10f);

        public static CardView Create(Transform parent, float w, float h)
        {
            var v = new CardView();
            v._face = Ui.Image(parent, "Card", Color.white);
            v.Root = v._face.rectTransform;
            v.Root.sizeDelta = new Vector2(w, h);
            v._label = Ui.Text(v.Root, "", h * 0.36f, Black, TextAlignmentOptions.Center, true);
            v._label.textWrappingMode = TextWrappingModes.NoWrap;
            return v;
        }

        public void SetCard(int card)
        {
            Root.gameObject.SetActive(true);
            _face.color = Color.white;
            _label.color = Card.IsRed(card) ? Red : Black;
            _label.text = Card.RankText(card) + "\n" + Card.SuitSymbols[Card.Suit(card)];
        }

        public void SetBack()
        {
            Root.gameObject.SetActive(true);
            _face.color = Back;
            _label.text = "";
        }

        public void Hide() => Root.gameObject.SetActive(false);
    }

    /// <summary>One seat: name, position, stack, last action, bet in front, two cards, dealer button.</summary>
    public sealed class SeatView
    {
        RectTransform _root, _box;
        Image _boxImage, _dealer;
        TextMeshProUGUI _name, _pos, _stack, _action, _bet, _dealerLabel;
        CardView _c0, _c1;
        bool _hero, _occupied;
        static readonly Color BoxColor = new Color(0.08f, 0.09f, 0.11f, 0.92f);
        static readonly Color HeroColor = new Color(0.12f, 0.12f, 0.16f, 0.95f);

        public static SeatView Create(Transform table, bool hero, Vector2 center, Vector2 betAt)
        {
            var v = new SeatView { _hero = hero };
            v._root = Ui.Rect(table, hero ? "Seat:hero" : "Seat");
            float cw = hero ? 56 : 40, ch = hero ? 78 : 56;
            v._c0 = CardView.Create(v._root, cw, ch);
            v._c0.Root.Box(0.5f, 0.5f, cw, ch, center.x - cw / 2 - 2, center.y + 48 + ch / 2 - 10);
            v._c1 = CardView.Create(v._root, cw, ch);
            v._c1.Root.Box(0.5f, 0.5f, cw, ch, center.x + cw / 2 + 2, center.y + 48 + ch / 2 - 10);

            v._boxImage = Ui.Image(v._root, "Box", hero ? HeroColor : BoxColor);
            v._box = v._boxImage.rectTransform.Box(0.5f, 0.5f, 196, 92, center.x, center.y);
            v._name = Ui.Text(v._box, "", 19, Palette.Text, TextAlignmentOptions.TopLeft, true);
            v._name.rectTransform.Place(0, 1, 1, 1, 10, -30, 50, 4);
            v._name.textWrappingMode = TextWrappingModes.NoWrap;
            v._pos = Ui.Text(v._box, "", 16, Palette.Accent, TextAlignmentOptions.TopRight, true);
            v._pos.rectTransform.Place(0, 1, 1, 1, 10, -30, 10, 6);
            v._stack = Ui.Text(v._box, "", 19, Palette.Text, TextAlignmentOptions.MidlineLeft);
            v._stack.rectTransform.Place(0, 0.33f, 1, 0.66f, 10, 0, 10, 0);
            v._stack.textWrappingMode = TextWrappingModes.NoWrap;
            v._action = Ui.Text(v._box, "", 17, Palette.Dim, TextAlignmentOptions.BottomLeft);
            v._action.rectTransform.Place(0, 0, 1, 0.33f, 10, 4, 10, 0);
            v._action.textWrappingMode = TextWrappingModes.NoWrap;

            v._bet = Ui.Text(v._root, "", 18, Palette.Accent, TextAlignmentOptions.Center, true);
            v._bet.rectTransform.Box(0.5f, 0.5f, 140, 26, betAt.x, betAt.y);

            v._dealer = Ui.Image(v._root, "Dealer", Color.white);
            v._dealer.rectTransform.Box(0.5f, 0.5f, 26, 26, center.x + 108, center.y + 30);
            v._dealerLabel = Ui.Text(v._dealer.transform, "D", 16, Color.black, TextAlignmentOptions.Center, true);
            v._dealer.gameObject.SetActive(false);
            v._c0.Hide();
            v._c1.Hide();
            v._root.gameObject.SetActive(false); // empty until someone sits down
            return v;
        }

        public void SetOccupant(string name)
        {
            _occupied = name != null;
            _root.gameObject.SetActive(_occupied);
            if (_occupied) _name.text = name;
        }

        public void ResetForHand()
        {
            _c0.Hide();
            _c1.Hide();
            _action.text = "";
            _bet.text = "";
            Highlight(false);
            SetAlpha(1);
        }

        public void ShowCards(int a, int b)
        {
            _c0.SetCard(a);
            _c1.SetCard(b);
        }

        public void ShowBacks()
        {
            _c0.SetBack();
            _c1.SetBack();
        }

        public void Fold()
        {
            if (!_hero) { _c0.Hide(); _c1.Hide(); }
            SetAlpha(0.45f);
        }

        public void Bust(string text)
        {
            _action.text = "<color=" + Ui.Hex(Palette.Down) + ">" + text + "</color>";
            _c0.Hide();
            _c1.Hide();
            SetAlpha(0.35f);
        }

        void SetAlpha(float a)
        {
            var c = _boxImage.color;
            c.a = (_hero ? HeroColor.a : BoxColor.a) * a;
            _boxImage.color = c;
            _name.alpha = _stack.alpha = a;
        }

        public void SetAction(string text) => _action.text = text;
        public void SetStack(string text) => _stack.text = text;
        public void SetBet(long amount) => _bet.text = amount > 0 ? amount.ToString("N0") : "";
        public void SetPosition(string label) => _pos.text = label;

        public void SetDealer(bool on, bool emphasize)
        {
            _dealer.gameObject.SetActive(on);
            _dealer.color = emphasize ? Palette.Accent : new Color(0.8f, 0.8f, 0.8f);
        }

        public void Highlight(bool on) => _boxImage.color = on ? new Color(0.20f, 0.35f, 0.18f, 0.95f) : (_hero ? HeroColor : BoxColor);

        /// <summary>Outline the hero seat while it's their turn.</summary>
        public void SetActive(bool on) => _boxImage.color = on ? new Color(0.30f, 0.26f, 0.10f, 0.95f) : (_hero ? HeroColor : BoxColor);
    }
}
