using System;
using System.Text;
using AllIn.Core;
using AllIn.Core.Actions;
using AllIn.Game.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace AllIn.Game.LoopScene
{
    /// <summary>
    /// Scene entry for Loop.unity. The scene file is empty; this builds camera, room, HUD and screens in code.
    /// All rules are asked of AllIn.Core.Loop — nothing is re-derived here.
    /// </summary>
    public sealed class LoopApp : MonoBehaviour
    {
        public const string SceneName = "Loop";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (SceneManager.GetActiveScene().name != SceneName) return;
            if (FindAnyObjectByType<LoopApp>() != null) return;
            new GameObject("LoopApp").AddComponent<LoopApp>();
        }

        public Core.Loop Loop { get; private set; }

        SpriteRenderer _roomBack;
        SpriteRenderer[] _furniture;
        Canvas _hud;
        TextMeshProUGUI _dayText, _cashText, _assetText, _roomText, _positionText, _logText, _hintText;
        Ui.Gauge _body, _mental;
        Ui.Btn _work, _coin, _exchange, _lotto, _checkup, _rest;
        TextMeshProUGUI _lifeText;
        UnityEngine.UI.Image _roomShade;
        float _hintUntil;

        public CoinScreen Coin { get; private set; }
        public ExchangeScreen Exchange { get; private set; }
        public WatchScreen Watch { get; private set; }
        public LottoScreen Lotto { get; private set; }
        public EventPresenter Events { get; private set; }

        void Awake()
        {
            Application.targetFrameRate = 60;
            Fonts.Ensure();
            Ui.EnsureEventSystem();
            BuildWorld();
            BuildHud();
            Coin = CoinScreen.Create(this);
            Exchange = ExchangeScreen.Create(this);
            Watch = WatchScreen.Create(this);
            Lotto = LottoScreen.Create(this);
            Events = EventPresenter.Create(this);

            var saved = SaveIO.Load();
            Loop = saved != null ? Core.Loop.FromSave(saved) : Core.Loop.NewRun(NewSeed());
            Save();
            Refresh();
        }

        public static long NewSeed() => DateTime.UtcNow.Ticks & 0x7FFF_FFFF_FFFFL;

        public void StartNewLife()
        {
            SaveIO.Delete();
            Loop = Core.Loop.NewRun(NewSeed());
            Coin.Hide();
            Exchange.Hide();
            Lotto.Hide();
            Save();
            Refresh();
        }

        public void Save() => SaveIO.Save(Loop.Snapshot());

        /// <summary>[계속 살기] on the month-end settlement: same walk-then-watch shape as Perform, minus the busy guards
        /// (the settlement popup that calls this has already been dismissed).</summary>
        public void ContinueLife()
        {
            var w = Loop.BeginContinueLife();
            Refresh();
            if (w.Walk != null && w.Walk.WatchTriggered) StartCoroutine(Watch.Run(w.Walk, false));
            else
            {
                if (w.Walk != null)
                {
                    w.Walk.RunToEnd();
                    Loop.FinishWatch(w.Walk);
                }
                Save();
                Refresh();
            }
        }

        /// <summary>The one door every player action goes through. Slot-ending actions that could leave a position
        /// at risk this slot (B5a) route through the watch scene before the result is finalized.</summary>
        public ActionResult Perform(IAction action)
        {
            if (Events.Busy || Watch.Busy) return ActionResult.Refused("");
            bool sleeping = action is RestAction && Loop.Data.slot == Slot.Night;
            bool working = action is WorkRestaurantAction;

            var w = Loop.BeginWatchableAction(action);
            if (w.Walk == null)
            {
                if (!w.Result.Ok && !string.IsNullOrEmpty(w.Result.Message)) Hint(w.Result.Message);
                Save();
                Refresh();
                return w.Result;
            }

            Refresh();
            if (w.Walk.WatchTriggered && !sleeping) StartCoroutine(Watch.Run(w.Walk, working));
            else
            {
                w.Walk.RunToEnd();
                Loop.FinishWatch(w.Walk);
                Save();
                Refresh();
            }
            return w.Result;
        }

        public void Hint(string text)
        {
            _hintText.text = text;
            _hintUntil = Time.unscaledTime + 3f;
        }

        void Update()
        {
            if (_hintText.text.Length > 0 && Time.unscaledTime > _hintUntil) _hintText.text = "";
        }

        // ---------- world (pixel-perfect room backdrop) ----------

        void BuildWorld()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 90;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Bg;
            camGo.transform.position = new Vector3(0, 0, -10);
            var ppc = camGo.AddComponent<PixelPerfectCamera>();
            ppc.assetsPPU = 1;
            ppc.refResolutionX = 320;
            ppc.refResolutionY = 180;
            ppc.gridSnapping = PixelPerfectCamera.GridSnapping.UpscaleRenderTexture;
            ppc.cropFrame = PixelPerfectCamera.CropFrame.None;

            var lightGo = new GameObject("Global Light 2D");
            var light = lightGo.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;

            _roomBack = Block("Room", 0, 0, 320, 180, 0);
            _furniture = new[]
            {
                Block("Window", -70, 30, 60, 40, 1),
                Block("Bed", 70, -55, 120, 30, 1),
                Block("Desk", -80, -50, 70, 40, 1),
            };
        }

        static Sprite _pixel;

        static SpriteRenderer Block(string name, float x, float y, float w, float h, int order)
        {
            if (_pixel == null)
            {
                var tex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _pixel = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
            }
            var go = new GameObject(name);
            go.transform.position = new Vector3(x, y, 0);
            go.transform.localScale = new Vector3(w, h, 1);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _pixel;
            sr.sortingOrder = order;
            return sr;
        }

        static readonly Color[] RoomColors = { new Color(0.22f, 0.24f, 0.20f), new Color(0.45f, 0.40f, 0.33f), new Color(0.30f, 0.38f, 0.50f) };
        static readonly Color[] SlotTint = { new Color(0.55f, 0.6f, 0.8f), Color.white, new Color(1f, 0.97f, 0.9f), new Color(0.9f, 0.7f, 0.6f), new Color(0.3f, 0.3f, 0.45f) };

        void PaintRoom()
        {
            var d = Loop.Data;
            var tint = SlotTint[(int)d.slot];
            _roomBack.color = RoomColors[(int)d.room] * tint;
            var f = RoomColors[(int)d.room] * 0.6f;
            f.a = 1;
            foreach (var sr in _furniture) sr.color = f * tint;
            _furniture[0].color = (d.slot == Slot.Night || d.slot == Slot.Dawn ? new Color(0.1f, 0.12f, 0.25f) : new Color(0.6f, 0.8f, 0.95f)) * tint;
            _furniture[2].gameObject.SetActive(d.room != Room.Goshiwon);
        }

        // ---------- HUD ----------

        void BuildHud()
        {
            _hud = Ui.Canvas("HUD", 0);
            var root = _hud.transform;

            // top bar
            var top = Ui.Image(root, "TopBar", Palette.Panel).rectTransform.Place(0, 1, 1, 1, 0, -72, 0, 0);
            _dayText = Ui.Text(top, "", 28, bold: true);
            _dayText.rectTransform.Place(0, 0, 0.35f, 1, 20, 0, 0, 0);
            _cashText = Ui.Text(top, "", 24, align: TextAlignmentOptions.MidlineRight);
            _cashText.rectTransform.Place(0.35f, 0.5f, 0.68f, 1, 0, 0, 10, 0);
            _assetText = Ui.Text(top, "", 20, Palette.Dim, TextAlignmentOptions.MidlineRight);
            _assetText.rectTransform.Place(0.35f, 0, 0.68f, 0.5f, 0, 0, 10, 0);
            var newLife = Ui.Button(top, Texts.NewLife, () => Events.ConfirmNewLife(), 20);
            newLife.Rect.Place(1, 0, 1, 1, -150, 14, 16, 14);

            // gauges + status row
            var status = Ui.Rect(root, "Status").Place(0, 1, 1, 1, 0, -120, 0, 76);
            _body = Ui.Gauge.Create(status, Texts.Body, Palette.Body);
            _body.Root.Place(0, 0, 0, 1, 20, 4, -240, 4);
            _mental = Ui.Gauge.Create(status, Texts.Mental, Palette.Mental);
            _mental.Root.Place(0, 0, 0, 1, 260, 4, -480, 4);
            _roomText = Ui.Text(status, "", 22, Palette.Accent);
            _roomText.rectTransform.Place(0, 0.45f, 0, 1, 500, 0, -760, 0);
            _lifeText = Ui.Text(status, "", 18, Palette.Dim);
            _lifeText.rectTransform.Place(0, 0, 0, 0.5f, 500, 0, -760, 0);
            _positionText = Ui.Text(status, "", 22);
            _positionText.rectTransform.Place(0, 0, 1, 1, 770, 0, 20, 0);

            // room shade (E3 darkens the room)
            _roomShade = Ui.Image(root, "RoomShade", new Color(0, 0, 0, 0));
            _roomShade.rectTransform.Place(0, 0, 1, 1, 0, 120, 0, 120);

            // log
            var logPanel = Ui.Image(root, "Log", new Color(0, 0, 0, 0.55f)).rectTransform.Place(1, 0, 1, 1, -420, 130, 16, 132);
            _logText = Ui.Text(logPanel, "", 18, Palette.Dim, TextAlignmentOptions.BottomLeft);
            _logText.rectTransform.Place(0, 0, 1, 1, 12, 10, 12, 10);
            _logText.overflowMode = TextOverflowModes.Truncate;

            // bottom action bar
            var bottom = Ui.Image(root, "ActionBar", Palette.Panel).rectTransform.Place(0, 0, 1, 0, 0, 0, 0, -120);
            _hintText = Ui.Text(root, "", 22, Palette.Accent, TextAlignmentOptions.Center);
            _hintText.rectTransform.Place(0, 0, 1, 0, 0, 122, 0, -160);
            string[] labels = { Texts.ActWork, Texts.ActCoin, Texts.WalletBank, Texts.ActLotto, Texts.ActCheckup, Texts.ActRest };
            Action[] actions =
            {
                () => Perform(WorkRestaurantAction.Instance),
                () => Coin.Show(),
                () => Exchange.Show(),
                () => Lotto.Show(),
                () => Perform(CheckupAction.Instance),
                () => Perform(RestAction.Instance),
            };
            const int n = 6;
            var buttons = new Ui.Btn[n];
            for (int i = 0; i < n; i++)
            {
                float x0 = i / (float)n, x1 = (i + 1) / (float)n;
                buttons[i] = Ui.Button(bottom, labels[i], actions[i], 26);
                buttons[i].Rect.Place(x0, 0, x1, 1, 8, 18, 8, 18);
            }
            _work = buttons[0];
            _coin = buttons[1];
            _exchange = buttons[2];
            _lotto = buttons[3];
            _checkup = buttons[4];
            _rest = buttons[5];
        }

        public void SetRoomShade(float alpha) => _roomShade.color = new Color(0, 0, 0, alpha);

        public void Refresh()
        {
            var d = Loop.Data;
            PaintRoom();
            _dayText.text = Texts.DayLabel(d.day) + "  " + Texts.SlotNames[(int)d.slot] + " <size=20><color=#8c8c94>" + Texts.SlotHours[(int)d.slot] + "</color></size>";
            _cashText.text = Texts.WalletBank + " " + Fmt.Won(d.bankCash);
            _assetText.text = Texts.Assets + " " + Fmt.Won(Loop.TotalAssets);
            _body.Set(d.body, d.bodyMax);
            _mental.Set(d.mental);
            _roomText.text = Texts.RoomNames[(int)d.room] + " · " + Texts.AgeLabel(Loop.Age);
            var life = new StringBuilder();
            if (d.healthDebt > 0) life.Append(Texts.HealthDebt + " " + d.healthDebt + "  ");
            if (Loop.IsBankrupt) life.Append("<color=" + Ui.Hex(Palette.Down) + ">" + Texts.BankruptStatus + " " + Texts.BankruptCountdown(Loop.BankruptDaysHeld, Rules.BankruptForcedAfterDays) + "</color>");
            _lifeText.text = life.ToString();

            if (d.hasPosition)
            {
                var p = d.position;
                long u = Loop.UnrealizedPnl;
                _positionText.text = (p.direction == Direction.Long ? Texts.CoinLong : Texts.CoinShort) + " x" + p.leverage + "  " +
                                     Texts.CoinUnrealized + " <color=" + Ui.Hex(u >= 0 ? Palette.Up : Palette.Down) + ">" + Fmt.SignedWon(u) + "</color>";
            }
            else _positionText.text = "";

            var sb = new StringBuilder();
            int from = Math.Max(0, d.log.Count - 14);
            for (int i = from; i < d.log.Count; i++) sb.AppendLine(d.log[i]);
            _logText.text = sb.ToString();

            bool alive = !d.finished;
            _work.Interactable = alive && Loop.Check(WorkRestaurantAction.Instance).Ok;
            _work.Label.text = Texts.ActWork + (d.slot == Slot.Evening ? "" : "\n<size=18>(" + Texts.SlotNames[(int)Slot.Evening] + ")</size>");
            _coin.Interactable = alive;
            _exchange.Interactable = alive;
            _lotto.Interactable = alive && d.slot != Slot.Night;
            _rest.Interactable = alive && Loop.Check(RestAction.Instance).Ok;
            _checkup.Interactable = alive && Loop.Check(CheckupAction.Instance).Ok;
            _checkup.Label.text = Texts.ActCheckup + (d.healthDebt > 0 ? "\n<size=16>" + Texts.CheckupEffect(d.checkupProgress, Rules.CheckupsPerDebtRelief) + "</size>" : "");
            _rest.Label.text = (d.slot == Slot.Night ? Texts.ActSleep : Texts.ActRest) + " →";

            if (!_work.Interactable && alive && d.slot == Slot.Evening)
            {
                var why = Loop.Check(WorkRestaurantAction.Instance);
                if (!string.IsNullOrEmpty(why.Reason)) _work.Label.text = Texts.ActWork + "\n<size=18>" + why.Reason + "</size>";
            }

            Coin.Refresh();
            Exchange.Refresh();
            Lotto.Refresh();
        }
    }
}
