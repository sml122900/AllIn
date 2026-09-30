using System;
using System.Collections;
using System.Collections.Generic;
using AllIn.Core;
using AllIn.Game.LoopScene;
using AllIn.Game.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AllIn.Game.World
{
    /// <summary>
    /// v0.5 E5–E9, presentation only: the pixel-art street (day/night tint), the protagonist walking (or driving) between buildings,
    /// the room view with lie/sit poses, the parked car, companions. It never decides anything: where he stands is Loop.Place, what he
    /// wears is Loop.Data.appearance. Walking costs no slot and can be skipped with a click.
    /// </summary>
    public sealed class WorldView
    {
        // street lives far to the right of the room so one camera can visit both
        public const float StreetX0 = 1000f, BuildingSlot = 120f;
        const float StreetW = 7 * BuildingSlot, FeetY = -22f, RoadY = -40f, GroundTop = -15f;
        const float RoomHeroX = -15f, RoomFeetY = -34f, BedX = 28f, BedTopY = -41f, DeskSitX = -46f;
        const float CarScale = 2f, CarBeside = 64f;
        const int OrderBuilding = 2, OrderDetail = 3, OrderCar = 6, OrderHero = 8, OrderFade = 100;

        static readonly Place[] Order = { Place.Home, Place.Restaurant, Place.LottoShop, Place.Pub, Place.Bank, Place.ClothShop, Place.CarDealer };
        static readonly Color[] SkyBySlot =
        {
            new Color(0.32f, 0.34f, 0.55f), new Color(0.55f, 0.78f, 0.96f), new Color(0.62f, 0.82f, 0.96f), new Color(0.92f, 0.55f, 0.38f), new Color(0.04f, 0.05f, 0.14f),
        };
        static readonly Color[] BuildingColor =
        {
            new Color(0.62f, 0.60f, 0.56f), new Color(0.80f, 0.42f, 0.34f), new Color(0.42f, 0.66f, 0.44f), new Color(0.40f, 0.30f, 0.52f),
            new Color(0.48f, 0.58f, 0.70f), new Color(0.88f, 0.55f, 0.66f), new Color(0.50f, 0.62f, 0.66f),
        };
        static readonly float[] BuildingHeight = { 50, 52, 46, 54, 64, 48, 44 };

        sealed class Building
        {
            public Place Place;
            public Transform Root;
            public SpriteRenderer Body, Roof, Door, Sign;
            public readonly List<SpriteRenderer> Windows = new List<SpriteRenderer>();
            public TextMeshProUGUI Label;
            public float Height;
        }

        readonly struct Tint
        {
            public readonly SpriteRenderer Sr; public readonly Color Base; public readonly bool Window; public readonly bool Sky;
            public Tint(SpriteRenderer sr, Color c, bool window, bool sky) { Sr = sr; Base = c; Window = window; Sky = sky; }
        }

        LoopApp _app;
        Transform _cam;
        Transform _streetRoot;
        Canvas _labels;
        readonly Building[] _buildings = new Building[7];
        readonly List<Tint> _tints = new List<Tint>();
        SpriteRenderer _hero, _car, _fade;
        readonly List<SpriteRenderer> _companions = new List<SpriteRenderer>();
        Room _homeRoom = (Room)(-1);
        Slot _tintSlot = (Slot)(-1);
        int _tintCompanionCount = -1;

        // presentation state
        float _x = StreetX0 + 60;            // hero (and, when there is one, the car) along the street
        float _carX = StreetX0 + 60 + 44;
        float _facing = 1;
        bool _walking, _driving;
        DollPose _pose = DollPose.Idle;
        float _animT;
        float _insideUntil;
        bool _poseOverride;
        float _poseX, _poseY;

        public bool Busy { get; private set; }
        public bool InStreet { get; private set; }
        /// <summary>Set by LoopApp every frame: a shop / exchange / lotto / event screen is up, so the hero is "inside".</summary>
        public bool ScreenOpen { get; set; }
        /// <summary>The voyage scene: no street, no hero (E6).</summary>
        public bool AtSea { get; set; }
        public float HeroX => _x;

        public static WorldView Create(LoopApp app, Transform camera)
        {
            var v = new WorldView { _app = app, _cam = camera };
            v.BuildStreet();
            v.BuildActors();
            return v;
        }

        static float DoorX(Place p) => StreetX0 + BuildingSlot * (Array.IndexOf(Order, p) + 0.5f);

        // ================= building the street =================

        SpriteRenderer Block(Transform parent, string name, float cx, float cy, float w, float h, Color c, int order, bool window = false, bool sky = false, bool tint = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(cx, cy, 0);
            go.transform.localScale = new Vector3(w, h, 1);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteCache.Pixel;
            sr.sortingOrder = order;
            sr.color = c;
            if (tint) _tints.Add(new Tint(sr, c, window, sky));
            return sr;
        }

        void BuildStreet()
        {
            _streetRoot = new GameObject("Street").transform;
            float cx = StreetX0 + StreetW / 2;
            Block(_streetRoot, "Sky", cx, 0, StreetW + 400, 180, Color.white, 0, sky: true);
            // distant skyline (flat silhouettes, purely decorative)
            for (int i = 0; i < 14; i++)
            {
                float w = 30 + (i * 37) % 26, h = 30 + (i * 53) % 40;
                Block(_streetRoot, "Skyline" + i, StreetX0 - 100 + i * 66 + w / 2, GroundTop + h / 2, w, h, new Color(0.45f, 0.5f, 0.6f), 1);
            }
            Block(_streetRoot, "Sidewalk", cx, (GroundTop - 10 + GroundTop) / 2 - 5, StreetW + 400, 10, new Color(0.62f, 0.62f, 0.64f), 1);
            Block(_streetRoot, "Road", cx, -60, StreetW + 400, 70, new Color(0.24f, 0.25f, 0.28f), 1);
            for (float x = StreetX0 - 100; x < StreetX0 + StreetW + 100; x += 30)
                Block(_streetRoot, "Lane", x, RoadY - 6, 14, 2, new Color(0.85f, 0.85f, 0.8f), 1);

            _labels = Ui.Canvas("StreetLabels", -1);
            for (int i = 0; i < 7; i++) _buildings[i] = BuildBuilding(Order[i], i);
            SetVisibleStreet(false);
        }

        Building BuildBuilding(Place place, int i)
        {
            var b = new Building { Place = place, Height = BuildingHeight[i] };
            b.Root = new GameObject("Building:" + place).transform;
            b.Root.SetParent(_streetRoot, false);
            float cx = StreetX0 + BuildingSlot * (i + 0.5f);
            var col = BuildingColor[i];
            b.Body = Block(b.Root, "Body", cx, GroundTop + b.Height / 2, 100, b.Height, col, OrderBuilding);
            b.Roof = Block(b.Root, "Roof", cx, GroundTop + b.Height - 3, 104, 6, col * 0.65f, OrderDetail);
            b.Door = Block(b.Root, "Door", cx, GroundTop + 14, 16, 28, new Color(0.30f, 0.20f, 0.14f), OrderDetail);
            b.Sign = Block(b.Root, "Sign", cx, GroundTop + b.Height - 12, 86, 14, new Color(0.1f, 0.1f, 0.12f), OrderDetail);
            float wy = GroundTop + b.Height * 0.55f;
            b.Windows.Add(Block(b.Root, "Window0", cx - 32, wy, 18, 14, Color.white, OrderDetail, window: true));
            b.Windows.Add(Block(b.Root, "Window1", cx + 32, wy, 18, 14, Color.white, OrderDetail, window: true));
            b.Label = Ui.Text(_labels.transform, i == 0 ? Texts.PlaceNames[0] : Texts.PlaceNames[(int)place], 20, Palette.Accent, TextAlignmentOptions.Center, true);
            b.Label.rectTransform.anchorMin = b.Label.rectTransform.anchorMax = Vector2.zero;
            b.Label.rectTransform.sizeDelta = new Vector2(360, 40);
            if (place == Place.Bank)
            {
                for (int k = -1; k <= 1; k++) Block(b.Root, "Column" + k, cx + k * 34, GroundTop + 22, 6, 34, new Color(0.85f, 0.85f, 0.88f), OrderDetail);
            }
            if (place == Place.CarDealer) b.Windows[0].transform.localScale = new Vector3(60, 22, 1);
            return b;
        }

        /// <summary>The home building follows the room tier (three looks).</summary>
        void ShapeHome(Room room)
        {
            if (room == _homeRoom) return;
            _homeRoom = room;
            var b = _buildings[0];
            float cx = DoorX(Place.Home);
            float h = room == Room.Goshiwon ? 44 : room == Room.OneRoom ? 56 : 84;
            float w = room == Room.Goshiwon ? 76 : room == Room.OneRoom ? 92 : 100;
            Color col = room == Room.Goshiwon ? new Color(0.55f, 0.53f, 0.50f) : room == Room.OneRoom ? new Color(0.78f, 0.70f, 0.58f) : new Color(0.55f, 0.66f, 0.78f);
            b.Height = h;
            SetBlock(b.Body, cx, GroundTop + h / 2, w, h, col);
            SetBlock(b.Roof, cx, GroundTop + h - 3, w + 4, 6, col * 0.65f);
            SetBlock(b.Sign, cx, GroundTop + Mathf.Min(h - 12, 40), 60, 12, new Color(0.1f, 0.1f, 0.12f));
            b.Sign.gameObject.SetActive(true);
            // windows: 1 (goshiwon) / 2 with a balcony strip (one-room) / 4 (officetel)
            while (b.Windows.Count < 6) b.Windows.Add(Block(b.Root, "WindowX", cx, 0, 14, 12, Color.white, OrderDetail, window: true));
            int count = room == Room.Goshiwon ? 1 : room == Room.OneRoom ? 2 : 6;
            for (int i = 0; i < b.Windows.Count; i++)
            {
                var wsr = b.Windows[i];
                wsr.gameObject.SetActive(i < count);
                if (i >= count) continue;
                float wx = room == Room.Officetel ? cx - 30 + (i % 3) * 30 : room == Room.OneRoom ? cx - 26 + i * 52 : cx + 22;
                float wy = room == Room.Officetel ? GroundTop + 40 + (i / 3) * 22 : GroundTop + h * 0.6f;
                SetBlock(wsr, wx, wy, 14, 12, Color.white);
            }
            RebaseTints();
        }

        static void SetBlock(SpriteRenderer sr, float cx, float cy, float w, float h, Color c)
        {
            sr.transform.position = new Vector3(cx, cy, 0);
            sr.transform.localScale = new Vector3(w, h, 1);
            sr.color = c;
        }

        void RebaseTints()
        {
            // re-read the base colours of everything that ShapeHome recoloured
            for (int i = 0; i < _tints.Count; i++)
            {
                var t = _tints[i];
                if (t.Sr == null) continue;
                var home = _buildings[0];
                if (t.Sr == home.Body || t.Sr == home.Roof || t.Sr == home.Sign) _tints[i] = new Tint(t.Sr, t.Sr.color, false, false);
            }
            _tintSlot = (Slot)(-1);
        }

        void BuildActors()
        {
            _hero = new GameObject("Hero").AddComponent<SpriteRenderer>();
            _hero.sortingOrder = OrderHero;
            _car = new GameObject("Car").AddComponent<SpriteRenderer>();
            _car.sortingOrder = OrderCar;
            _car.gameObject.SetActive(false);
            for (int i = 0; i < 4; i++)
            {
                var c = new GameObject("Companion" + i).AddComponent<SpriteRenderer>();
                c.sortingOrder = OrderHero - 1;
                c.gameObject.SetActive(false);
                _companions.Add(c);
            }
            _fade = new GameObject("Fade").AddComponent<SpriteRenderer>();
            _fade.sprite = SpriteCache.Pixel;
            _fade.sortingOrder = OrderFade;
            _fade.transform.localScale = new Vector3(340, 200, 1);
            _fade.color = new Color(0, 0, 0, 0);
        }

        void SetVisibleStreet(bool on) => _streetRoot.gameObject.SetActive(on);

        // ================= sync & per-frame =================

        /// <summary>Called after every state change: puts everything where the Loop says it is (unless a walk is in progress).</summary>
        public void Sync()
        {
            if (_app.Loop == null) return;
            var d = _app.Loop.Data;
            ShapeHome(d.room);
            if (Busy) return;
            InStreet = d.place != Place.Home;
            _x = DoorX(d.place);
            _carX = _x + CarBeside;
            _facing = 1;
            if (d.place == Place.Home) _carX = DoorX(Place.Home) + CarBeside;
            ApplyView();
        }

        void ApplyView()
        {
            SetVisibleStreet(InStreet);
            if (!InStreet) { _cam.position = new Vector3(0, 0, -10); _facing = 1; }
            _tintSlot = (Slot)(-1);
        }

        /// <summary>Per-frame: animation timers, sprite frames, positions, the day/night tint. Driven by LoopApp.Update.</summary>
        public void Tick()
        {
            if (_app.Loop == null) return;
            var d = _app.Loop.Data;
            _animT += Time.unscaledDeltaTime;
            bool hidden = AtSea || ScreenOpen || Time.unscaledTime < _insideUntil;
            var look = d.appearance;

            // hero
            DollPose pose = _poseOverride ? _pose : (_walking ? DollPose.Walk : DollPose.Idle);
            int frame = pose == DollPose.Walk ? (int)(_animT * 8f) : pose == DollPose.Idle ? (int)(_animT * 2f) : 0;
            _hero.sprite = SpriteCache.Person(look, pose, frame);
            _hero.gameObject.SetActive(!hidden && !_driving);
            float hx, hy;
            if (_poseOverride) { hx = _poseX; hy = _poseY; }
            else if (InStreet) { hx = _x; hy = FeetY; }
            else { hx = RoomHeroX; hy = RoomFeetY; }
            _hero.transform.position = new Vector3(hx, hy, 0);
            _hero.transform.localScale = new Vector3(_facing >= 0 ? 1 : -1, 1, 1);

            // car
            bool showCar = d.carTier > 0 && InStreet && !AtSea;
            if (showCar)
            {
                _car.sprite = SpriteCache.Car(d.carTier);
                _car.transform.position = new Vector3(_carX, RoadY, 0);
                _car.transform.localScale = new Vector3((_driving && _facing < 0 ? -1 : 1) * CarScale, CarScale, 1);   // drawn small; 2x reads as a real car next to him
            }
            _car.gameObject.SetActive(showCar);

            // companions: they stand with him (a picture slot only, E9)
            int n = Mathf.Min(d.companions.Count, _companions.Count);
            for (int i = 0; i < _companions.Count; i++)
            {
                var sr = _companions[i];
                bool on = i < n && !AtSea && !hidden && !(InStreet && _driving);
                sr.gameObject.SetActive(on);
                if (!on) continue;
                var c = d.companions[i];
                sr.sprite = SpriteCache.Person(c.look, _walking && InStreet ? DollPose.Walk : DollPose.Idle, _walking && InStreet ? (int)(_animT * 8f) + i : (int)(_animT * 2f) + i);
                float s = c.kind == CompanionKind.Child ? 0.75f : 1f;
                float dx = InStreet ? -_facing * (18 + 16 * i) : 24 + 22 * i;
                sr.transform.position = new Vector3((InStreet ? _x : RoomHeroX) + dx, InStreet ? FeetY : RoomFeetY, 0);
                sr.transform.localScale = new Vector3((_facing >= 0 || !InStreet ? 1 : -1) * s, s, 1);
            }

            // camera + labels
            if (!InStreet) _cam.position = new Vector3(0, 0, -10);
            else
            {
                float target = _driving ? _carX : _x;
                float camX = Mathf.Clamp(target, StreetX0 + 160, StreetX0 + StreetW - 160);
                _cam.position = new Vector3(camX, 0, -10);
                PlaceLabels(camX);
            }
            _labels.gameObject.SetActive(InStreet && !AtSea);

            if (_tintSlot != d.slot) ApplyTint(d.slot);
        }

        void PlaceLabels(float camX)
        {
            foreach (var b in _buildings)
            {
                float wx = DoorX(b.Place), wy = GroundTop + Mathf.Min(b.Height - 12, b.Place == Place.Home ? 40 : 200);
                float nx = (wx - camX) / 320f + 0.5f, ny = (wy - 0) / 180f + 0.5f;
                b.Label.rectTransform.anchoredPosition = new Vector2(nx * Ui.Reference.x, ny * Ui.Reference.y);
                b.Label.gameObject.SetActive(nx > -0.2f && nx < 1.2f);
            }
        }

        void ApplyTint(Slot slot)
        {
            _tintSlot = slot;
            var tint = LoopApp.SlotTint[(int)slot];
            bool lit = slot == Slot.Night || slot == Slot.Dawn || slot == Slot.Evening;
            foreach (var t in _tints)
            {
                if (t.Sr == null) continue;
                if (t.Sky) t.Sr.color = SkyBySlot[(int)slot];
                else if (t.Window) t.Sr.color = lit ? new Color(1f, 0.88f, 0.45f) : new Color(0.62f, 0.82f, 0.95f) * tint;
                else t.Sr.color = t.Base * tint;
            }
            var actorTint = Color.Lerp(Color.white, tint, 0.65f);
            _hero.color = actorTint;
            _car.color = actorTint;
            foreach (var c in _companions) c.color = actorTint;
        }

        // ================= movement =================

        static bool SkipPressed()
        {
            var m = Mouse.current;
            var k = Keyboard.current;
            return (m != null && m.leftButton.wasPressedThisFrame) || (k != null && k.anyKey.wasPressedThisFrame);
        }

        IEnumerator Wipe(Action mid)
        {
            for (float t = 0; t < 0.18f; t += Time.unscaledDeltaTime) { _fade.color = new Color(0, 0, 0, t / 0.18f); yield return null; }
            _fade.color = Color.black;
            mid?.Invoke();
            yield return null;
            for (float t = 0; t < 0.18f; t += Time.unscaledDeltaTime) { _fade.color = new Color(0, 0, 0, 1 - t / 0.18f); yield return null; }
            _fade.color = new Color(0, 0, 0, 0);
        }

        IEnumerator Travel(float toX)
        {
            bool car = _app.Loop.HasCar;
            float from = car ? _carX : _x;
            float dist = Mathf.Abs(toX - from);
            if (dist < 1f) yield break;
            float dur = car ? Mathf.Clamp(dist / 260f, 0.6f, 1.4f) : Mathf.Clamp(dist / 140f, 1f, 2f);
            _facing = toX >= from ? 1 : -1;
            if (car) _driving = true; else _walking = true;
            int startFrame = Time.frameCount;
            for (float t = 0; t < dur; t += Time.unscaledDeltaTime)
            {
                if (Time.frameCount > startFrame + 1 && SkipPressed()) break;    // a click skips the walk
                float u = t / dur;
                float x = Mathf.Lerp(from, toX, car ? u * u * (3 - 2 * u) : u);
                if (car) _carX = x; else _x = x;
                yield return null;
            }
            if (car) _carX = toX; else _x = toX;
            if (car)
            {
                _x = toX - 40 * _facing;    // steps out beside the parked car
                _driving = false;
            }
            _walking = false;
        }

        /// <summary>Walks (or drives) to the building's door and goes in. No slot is spent; the Loop only learns where he is.</summary>
        public IEnumerator GoTo(Place dest)
        {
            var loop = _app.Loop;
            Busy = true;
            _insideUntil = 0;
            if (dest == Place.Home)
            {
                if (InStreet)
                {
                    yield return Travel(DoorX(Place.Home) + (loop.HasCar ? CarBeside : 0));
                    loop.MoveTo(Place.Home);
                    yield return Wipe(() => { InStreet = false; ApplyView(); });
                }
                else loop.MoveTo(Place.Home);
            }
            else
            {
                if (!InStreet)
                {
                    // out the front door: the street, with the hero at his own door
                    yield return Wipe(() =>
                    {
                        InStreet = true;
                        _x = DoorX(Place.Home);
                        _carX = DoorX(Place.Home) + CarBeside;
                        _facing = 1;
                        ApplyView();
                    });
                }
                float doorX = DoorX(dest);
                float targetX = loop.HasCar ? doorX + CarBeside : doorX;
                // with a car the hero stands next to it at the door; without, he stands at the door itself
                if (Mathf.Abs((loop.HasCar ? _carX : _x) - targetX) > 1f) yield return Travel(targetX);
                _x = doorX;
                loop.MoveTo(dest);
            }
            if (dest != Place.Home) _insideUntil = Time.unscaledTime + 0.45f;   // steps through the door; the screen / action takes over
            Busy = false;
        }

        /// <summary>Room view only: lie on the bed (night sleep) or sit at the desk (day rest) for a beat.</summary>
        public IEnumerator PlayPose(DollPose pose, float seconds)
        {
            if (InStreet || AtSea) yield break;
            Busy = true;
            _pose = pose;
            _poseOverride = true;
            _poseX = pose == DollPose.Lie ? BedX : DeskSitX;
            _poseY = pose == DollPose.Lie ? BedTopY : RoomFeetY;
            _facing = 1;
            yield return new WaitForSecondsRealtime(seconds);
            _poseOverride = false;
            Busy = false;
        }

        /// <summary>The hero stays out of sight for a moment (an action that runs instantly, like the shift at the restaurant).</summary>
        public void StepInside(float seconds) => _insideUntil = Time.unscaledTime + seconds;

        public IEnumerator FadeBlack(float seconds)
        {
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime) { _fade.color = new Color(0, 0, 0, t / seconds); yield return null; }
            _fade.color = Color.black;
        }

        public void ClearFade() => _fade.color = new Color(0, 0, 0, 0);
    }
}
