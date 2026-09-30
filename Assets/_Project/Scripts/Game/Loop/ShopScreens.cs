using System;
using System.Collections.Generic;
using AllIn.Core;
using AllIn.Game.UI;
using AllIn.Game.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AllIn.Game.LoopScene
{
    /// <summary>Shared pieces of the three v0.5 screens (clothes shop, wardrobe, car lot): a panel, a title, a close button, a doll preview.</summary>
    public abstract class StyleScreen
    {
        protected LoopApp App;
        protected GameObject Root;
        protected RectTransform Panel;
        protected TextMeshProUGUI Info, Notice;
        protected Image Doll;

        public bool Visible => Root != null && Root.activeSelf;

        protected void BuildFrame(string name, string title, float w, float h)
        {
            var canvas = Ui.Canvas(name, 10);
            Root = canvas.gameObject;
            Ui.Image(canvas.transform, "Scrim", Palette.Scrim, raycast: true);
            Panel = Ui.Image(canvas.transform, "Panel", Palette.Panel, raycast: true).rectTransform.Box(0.5f, 0.5f, w, h);
            Ui.Text(Panel, title, 30, bold: true).rectTransform.Place(0, 1, 1, 1, 30, -64, 30, 16);
            Ui.Button(Panel, Texts.Close, Hide, 22).Rect.Place(1, 1, 1, 1, -130, -60, 20, 16);
            Info = Ui.Text(Panel, "", 22, Palette.Dim, TextAlignmentOptions.MidlineRight);
            Info.rectTransform.Place(0.35f, 1, 1, 1, 0, -64, 160, 16);
            Notice = Ui.Text(Panel, "", 20, Palette.Accent, TextAlignmentOptions.Center);
            Notice.rectTransform.Place(0, 0, 1, 0, 30, 10, 30, -44);
            Root.SetActive(false);
        }

        protected Image AddDoll(float x, float y, float size)
        {
            var img = Ui.Image(Panel, "Doll", new Color(0.16f, 0.17f, 0.21f, 1f));
            img.rectTransform.Box(0, 0, size + 24, size + 24, x, y);
            var inner = Ui.Image(img.transform, "Sprite", Color.white);
            inner.rectTransform.Place(0, 0, 1, 1, 12, 12, 12, 12);
            inner.preserveAspect = true;
            return inner;
        }

        public virtual void Show()
        {
            if (App.Events.Busy) return;
            Root.SetActive(true);
            Notice.text = "";
            Refresh();
        }

        public void Hide() => Root.SetActive(false);

        public abstract void Refresh();

        protected void Say(string text) => Notice.text = text;

        protected static string Won(long v) => Fmt.Won(v);
    }

    /// <summary>E7: three tiers x four parts. Payment comes from the bank; a click on an item tries it on.</summary>
    public sealed class ClothShopScreen : StyleScreen
    {
        int _tier, _part, _selected = -1;
        Ui.Btn[] _tierBtn, _partBtn, _card;
        TextMeshProUGUI[] _cardText;
        Ui.Btn _buy;

        public static ClothShopScreen Create(LoopApp app)
        {
            var s = new ClothShopScreen { App = app };
            s.Build();
            return s;
        }

        void Build()
        {
            BuildFrame("ClothShopScreen", Texts.ShopClothTitle, 1040, 640);
            _tierBtn = new Ui.Btn[3];
            for (int i = 0; i < 3; i++)
            {
                int t = i;
                _tierBtn[i] = Ui.Button(Panel, Texts.ClothTierNames[i], () => { _tier = t; _selected = -1; Refresh(); }, 24);
                _tierBtn[i].Rect.Place(0, 1, 0, 1, 30 + i * 150, -140, -(30 + i * 150 + 140), 84);
            }
            _partBtn = new Ui.Btn[4];
            for (int i = 0; i < 4; i++)
            {
                int p = i;
                _partBtn[i] = Ui.Button(Panel, Texts.ClothPartNames[i], () => { _part = p; _selected = -1; Refresh(); }, 22);
                _partBtn[i].Rect.Place(0, 1, 0, 1, 30 + i * 120, -206, -(30 + i * 120 + 110), 156);
            }
            _card = new Ui.Btn[5];
            _cardText = new TextMeshProUGUI[5];
            for (int i = 0; i < 5; i++)
            {
                int c = i;
                _card[i] = Ui.Button(Panel, "", () => { _selected = CardItem(c); Refresh(); }, 20);
                _card[i].Rect.Place(0, 1, 0, 1, 30 + i * 118, -400, -(30 + i * 118 + 108), 230);
                _card[i].Label.gameObject.SetActive(false);
                _cardText[i] = Ui.Text(_card[i].Rect, "", 18, align: TextAlignmentOptions.Center);
                _cardText[i].rectTransform.Place(0, 0, 1, 1, 4, 4, 4, 4);
            }
            _buy = Ui.Button(Panel, Texts.Buy, OnBuy, 26);
            _buy.Rect.Place(0, 0, 0, 0, 30, 60, -290, -130);
            Doll = AddDoll(810, 250, 340);
        }

        int CardItem(int card)
        {
            var items = Wardrobe.Items((ClothTier)_tier, (ClothPart)_part);
            return card < items.Count ? items[card] : -1;
        }

        void OnBuy()
        {
            if (_selected < 0) return;
            var r = App.Loop.BuyClothes(_selected);
            if (r.Ok) { Say(Texts.BoughtCloth); App.Save(); App.Refresh(); }
            else Say(r.Reason);
            Refresh();
        }

        public override void Refresh()
        {
            if (Root == null || !Root.activeSelf) return;
            var loop = App.Loop;
            Info.text = Texts.ShopClothPay + "  " + Texts.WalletBank + " " + Won(loop.Data.bankCash);
            for (int i = 0; i < 3; i++) _tierBtn[i].SetOn(i == _tier);
            for (int i = 0; i < 4; i++) _partBtn[i].SetOn(i == _part);
            for (int i = 0; i < 5; i++)
            {
                int id = CardItem(i);
                _card[i].Button.gameObject.SetActive(id >= 0);
                if (id < 0) continue;
                bool owned = loop.Owns(id);
                _card[i].SetOn(id == _selected);
                _cardText[i].text = Wardrobe.Name(id) + "\n\n" + (owned ? "<color=" + Ui.Hex(Palette.Up) + ">" + Texts.Owned + "</color>" : Won(Wardrobe.Price(id)));
            }
            var look = loop.Data.appearance.Clone();
            if (_selected >= 0) Wear(look, _selected);
            Doll.sprite = SpriteCache.Person(look, DollPose.Idle, 0);
            bool can = _selected >= 0 && !loop.Owns(_selected) && loop.Data.bankCash >= Wardrobe.Price(_selected);
            _buy.Interactable = can;
            _buy.Label.text = _selected >= 0 && !loop.Owns(_selected) ? Texts.Buy + "  " + Won(Wardrobe.Price(_selected)) : Texts.Buy;
        }

        static void Wear(Appearance a, int id)
        {
            switch (Wardrobe.PartOf(id))
            {
                case ClothPart.Top: a.top = id; break;
                case ClothPart.Bottom: a.bottom = id; break;
                case ClothPart.Shoes: a.shoes = id; break;
                default: a.accessory = id; break;
            }
        }

        public override void Show()
        {
            _selected = -1;
            base.Show();
        }
    }

    /// <summary>E7: the wardrobe at home — put on what you own; the mirror changes skin tone and hair. No slot.</summary>
    public sealed class WardrobeScreen : StyleScreen
    {
        int _part;
        Ui.Btn[] _partBtn, _item, _skin, _hair;
        Ui.Btn _off;
        readonly List<int> _listed = new List<int>();

        public static WardrobeScreen Create(LoopApp app)
        {
            var s = new WardrobeScreen { App = app };
            s.Build();
            return s;
        }

        void Build()
        {
            BuildFrame("WardrobeScreen", Texts.WardrobeTitle, 1040, 640);
            _partBtn = new Ui.Btn[4];
            for (int i = 0; i < 4; i++)
            {
                int p = i;
                _partBtn[i] = Ui.Button(Panel, Texts.ClothPartNames[i], () => { _part = p; Refresh(); }, 22);
                _partBtn[i].Rect.Place(0, 1, 0, 1, 30 + i * 120, -140, -(30 + i * 120 + 110), 84);
            }
            _item = new Ui.Btn[8];
            for (int i = 0; i < 8; i++)
            {
                int k = i;
                _item[i] = Ui.Button(Panel, "", () => OnWear(k), 20);
                _item[i].Rect.Place(0, 1, 0, 1, 30 + (i % 2) * 250, -206 - (i / 2) * 54, -(30 + (i % 2) * 250 + 240), 156 + (i / 2) * 54);
            }
            _off = Ui.Button(Panel, Texts.Unequip, () => { App.Loop.RemoveAccessory(); App.Save(); App.Refresh(); Refresh(); }, 20);
            _off.Rect.Place(0, 0, 0, 0, 30, 152, -240, -206);
            Ui.Text(Panel, Texts.WardrobeMirror, 22, Palette.Dim, bold: true).rectTransform.Box(0, 0, 200, 34, 130, 96);
            _skin = new Ui.Btn[3];
            _hair = new Ui.Btn[3];
            for (int i = 0; i < 3; i++)
            {
                int s = i;
                _skin[i] = Ui.Button(Panel, Texts.SkinNames[i], () => { App.Loop.SetBody(s, App.Loop.Data.appearance.hair); App.Save(); App.Refresh(); Refresh(); }, 18);
                _skin[i].Rect.Place(0, 0, 0, 0, 260 + i * 120, 100, -(260 + i * 120 + 112), -142);
                _hair[i] = Ui.Button(Panel, Texts.HairNames[i], () => { App.Loop.SetBody(App.Loop.Data.appearance.skin, s); App.Save(); App.Refresh(); Refresh(); }, 18);
                _hair[i].Rect.Place(0, 0, 0, 0, 260 + i * 120, 52, -(260 + i * 120 + 112), -94);
            }
            Doll = AddDoll(810, 250, 340);
        }

        void OnWear(int slot)
        {
            if (slot >= _listed.Count) return;
            if (App.Loop.Equip(_listed[slot]).Ok) { App.Save(); App.Refresh(); }
            Refresh();
        }

        public override void Refresh()
        {
            if (Root == null || !Root.activeSelf) return;
            var loop = App.Loop;
            var a = loop.Data.appearance;
            Info.text = "";
            for (int i = 0; i < 4; i++) _partBtn[i].SetOn(i == _part);
            _listed.Clear();
            foreach (int id in loop.Data.ownedClothes)
                if ((int)Wardrobe.PartOf(id) == _part) _listed.Add(id);
            _listed.Sort();
            for (int i = 0; i < _item.Length; i++)
            {
                bool has = i < _listed.Count;
                _item[i].Button.gameObject.SetActive(has);
                if (!has) continue;
                int id = _listed[i];
                bool worn = a.Worn((ClothPart)_part) == id;
                _item[i].SetOn(worn);
                _item[i].Label.text = Wardrobe.Name(id) + (worn ? "  · " + Texts.Worn : "");
            }
            _off.Button.gameObject.SetActive(_part == (int)ClothPart.Accessory);
            for (int i = 0; i < 3; i++) { _skin[i].SetOn(a.skin == i); _hair[i].SetOn(a.hair == i); }
            Doll.sprite = SpriteCache.Person(a, DollPose.Idle, 0);
        }
    }

    /// <summary>E8: four cars, one at a time; the running cost and the sale price are shown before you commit.</summary>
    public sealed class CarShopScreen : StyleScreen
    {
        Ui.Btn[] _buy;
        TextMeshProUGUI[] _row;
        Image[] _pic;
        Ui.Btn _sell;

        public static CarShopScreen Create(LoopApp app)
        {
            var s = new CarShopScreen { App = app };
            s.Build();
            return s;
        }

        void Build()
        {
            BuildFrame("CarShopScreen", Texts.CarShopTitle, 1040, 640);
            _buy = new Ui.Btn[Cars.Count + 1];
            _row = new TextMeshProUGUI[Cars.Count + 1];
            _pic = new Image[Cars.Count + 1];
            for (int t = 1; t <= Cars.Count; t++)
            {
                int tier = t;
                float top = 140 + (t - 1) * 104;
                var bg = Ui.Image(Panel, "Row" + t, new Color(0.16f, 0.17f, 0.21f, 1f));
                bg.rectTransform.Place(0, 1, 1, 1, 30, -(top + 94), 30, top);
                _pic[t] = Ui.Image(bg.transform, "Car", Color.white);
                _pic[t].rectTransform.Place(0, 0, 0, 1, 10, 8, -160, 8);
                _pic[t].preserveAspect = true;
                _pic[t].sprite = SpriteCache.Car(tier);
                _row[t] = Ui.Text(bg.transform, "", 22, align: TextAlignmentOptions.MidlineLeft);
                _row[t].rectTransform.Place(0, 0, 1, 1, 180, 4, 210, 4);
                _buy[t] = Ui.Button(bg.transform, Texts.CarBuy, () => OnBuy(tier), 22);
                _buy[t].Rect.Place(1, 0, 1, 1, -190, 14, 10, 14);
            }
            _sell = Ui.Button(Panel, Texts.CarSell, OnSell, 24);
            _sell.Rect.Place(0, 1, 0, 1, 30, -130, -300, 84);
        }

        void OnBuy(int tier)
        {
            var r = App.Loop.BuyCar(tier);
            if (r.Ok) { Say(Texts.CarBought); App.Save(); App.Refresh(); }
            else Say(r.Reason);
            Refresh();
        }

        void OnSell()
        {
            var r = App.Loop.SellCar();
            if (r.Ok) { Say(Texts.CarSold); App.Save(); App.Refresh(); }
            else Say(r.Reason);
            Refresh();
        }

        public override void Refresh()
        {
            if (Root == null || !Root.activeSelf) return;
            var loop = App.Loop;
            int have = loop.Data.carTier;
            Info.text = Texts.WalletBank + " " + Won(loop.Data.bankCash);
            for (int t = 1; t <= Cars.Count; t++)
            {
                _row[t].text = Texts.CarNames[t] + (have == t ? "  <color=" + Ui.Hex(Palette.Up) + ">" + Texts.CarHave + "</color>" : "") +
                               "\n<size=18><color=" + Ui.Hex(Palette.Dim) + ">" + Won(Cars.Price[t]) + "  ·  " + Texts.CarUpkeepLine(Cars.UpkeepOf(t)) + "  ·  " + Texts.CarSellLine(Cars.SellPriceOf(t)) + "</color></size>";
                _buy[t].Interactable = have == 0 && loop.Data.bankCash >= Cars.Price[t];
                _buy[t].Button.gameObject.SetActive(have != t);
            }
            _sell.Button.gameObject.SetActive(have > 0);
            if (have > 0) _sell.Label.text = Texts.CarSell + "  " + Won(Cars.SellPriceOf(have));
        }
    }
}
