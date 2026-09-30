using System;
using System.Collections;
using System.IO;
using System.Linq;
using AllIn.Core;
using AllIn.Core.Actions;
using AllIn.Game.LoopScene;
using AllIn.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace AllIn.Game.Dev
{
    /// <summary>
    /// Dev-only (v0.5): <c>-allinShotsV5 &lt;dir&gt;</c> plays a scripted tour of the street, walking, the car, the clothes shop, the wardrobe
    /// (before / after), the car lot, companions, the coin "무조건 승리" moment and the settlement row, saving screenshots into &lt;dir&gt;.
    /// The luck chance is forced to 100% here only; the rule default stays 0.
    /// </summary>
    public sealed class DevCaptureV5 : MonoBehaviour
    {
        string _dir;
        int _shot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Arm()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-allinShotsV5");
            if (i < 0 || i + 1 >= args.Length) return;
            string dir = args[i + 1];
            Directory.CreateDirectory(dir);
            SaveIO.DirectoryOverride = dir;
            Application.runInBackground = true;
            SaveIO.Delete();
            var go = new GameObject("DevCaptureV5");
            DontDestroyOnLoad(go);
            go.AddComponent<DevCaptureV5>()._dir = dir;
        }

        /// <summary>A price path that dips into the watch band (past 70% of the margin lost, short of liquidation) and stays there.</summary>
        sealed class DipPath : ISubTickPath
        {
            readonly Core.Loop _loop;
            public DipPath(Core.Loop loop) { _loop = loop; }
            public long[] Build(long oldPrice, long newPrice, IRng rng)
            {
                var p = _loop.Data.position;
                long price = (long)Math.Floor(p.entryPrice - CoinRules.TotalMargin(p) * 0.8 / CoinRules.Quantity(p));
                var path = new long[Rules.SubTicksPerSlot];
                for (int i = 0; i < path.Length; i++) path[i] = i < 2 ? oldPrice : price;
                return path;
            }
        }

        IEnumerator Start()
        {
            LoopApp app = null;
            while ((app = FindAnyObjectByType<LoopApp>()) == null) yield return null;
            yield return Wait(3.3f);
            yield return Drain(app);
            var loop = app.Loop;
            var d = loop.Data;

            // room, daytime: the hero standing in his room
            d.slot = Slot.Morning; app.Refresh();
            yield return Wait(0.5f);
            yield return Shot("room_day");

            // bed / desk poses: rest at night (lie) and by day (sit), shot mid-pose
            d.slot = Slot.Afternoon; app.Refresh();
            Click(Texts.ActRest);
            yield return Wait(0.35f);
            yield return Shot("pose_sit");
            yield return Wait(1.2f);
            yield return Drain(app);
            d.slot = Slot.Night; app.Refresh();
            Click(Texts.ActSleep);
            yield return Wait(0.35f);
            yield return Shot("pose_lie");
            yield return Wait(1.2f);
            yield return Drain(app);

            // go to the bank: walking shot, then the street in daylight
            d.slot = Slot.Morning; app.Refresh();
            Click(Texts.WalletBank);
            yield return Wait(0.5f);
            yield return Shot("walking");
            while (app.Stage.Busy) yield return null;
            yield return Wait(0.3f);
            app.Exchange.Hide();
            yield return Wait(0.3f);
            yield return Shot("street_day");

            // the same street at night
            d.slot = Slot.Night; app.Refresh();
            yield return Wait(0.3f);
            yield return Shot("street_night");
            d.slot = Slot.Evening; app.Refresh();
            yield return Wait(0.3f);
            yield return Shot("street_evening");
            d.slot = Slot.Morning; app.Refresh();

            // the home building in its three looks (room tiers), seen from the restaurant's doorstep
            foreach (var room in new[] { Room.Goshiwon, Room.Officetel, Room.OneRoom })
            {
                d.room = room; d.roomRecheckDay = 999;
                loop.MoveTo(Place.Restaurant);
                app.Refresh();
                yield return Wait(0.4f);
                yield return Shot("street_home_" + room);
            }
            loop.MoveTo(Place.Bank);
            app.Refresh();

            // clothes shop: brand tier, tops, try on the hoodie, buy it
            d.bankCash = 60_000_000;
            app.Refresh();
            Click(Texts.PlaceNames[(int)Place.ClothShop]);
            while (app.Stage.Busy) yield return null;
            yield return Wait(0.5f);
            yield return Shot("cloth_shop_start");
            Click(Texts.ClothTierNames[(int)ClothTier.Brand]);
            yield return Wait(0.2f);
            Click(Wardrobe.Name(Wardrobe.Id(ClothPart.Top, ClothTier.Brand, 2)));
            yield return Wait(0.3f);
            yield return Shot("cloth_shop_tryon");
            Click(Texts.Buy + "  ");
            yield return Wait(0.3f);
            Click(Texts.ClothTierNames[(int)ClothTier.Luxury]);
            Click(Wardrobe.Name(Wardrobe.Id(ClothPart.Top, ClothTier.Luxury, 4)));
            yield return Wait(0.2f);
            Click(Texts.Buy + "  ");
            yield return Wait(0.3f);
            yield return Shot("cloth_shop_bought");
            app.ClothShop.Hide();

            // wardrobe at home: before, then wear the hoodie, then the coat with a bag
            Click(Texts.WardrobeTitle);
            while (app.Stage.Busy) yield return null;
            yield return Wait(0.5f);
            yield return Shot("wardrobe_before");
            Click(Wardrobe.Name(Wardrobe.Id(ClothPart.Top, ClothTier.Brand, 2)));
            yield return Wait(0.3f);
            yield return Shot("wardrobe_after");
            app.Wardrobe.Hide();
            yield return Wait(0.3f);
            yield return Shot("room_new_clothes");

            // car lot: buy the domestic mid-size, then drive to the bank
            d.bankCash = 100_000_000;
            app.Refresh();
            Click(Texts.PlaceNames[(int)Place.CarDealer]);
            while (app.Stage.Busy) yield return null;
            yield return Wait(0.5f);
            yield return Shot("car_shop");
            loop.BuyCar(2);                    // the domestic mid-size (the on-screen buttons look the same; picking by tier is easier here)
            app.Save(); app.Refresh();
            yield return Wait(0.3f);
            yield return Shot("car_shop_bought");
            app.CarShop.Hide();
            yield return Wait(0.5f);
            yield return Shot("street_car_parked");
            Click(Texts.WalletBank);
            yield return Wait(0.45f);
            yield return Shot("car_drive");
            while (app.Stage.Busy) yield return null;
            app.Exchange.Hide();
            yield return Wait(0.3f);
            yield return Shot("street_car_at_bank");

            // companions (debug path): in the street, then in the room
            app.DebugSetCompanions(true, true);
            yield return Wait(0.5f);
            yield return Shot("companions_street");
            Click(Texts.ActRest);
            while (app.Stage.Busy) yield return null;
            yield return Wait(1.6f);
            yield return Drain(app);
            app.Refresh();
            yield return Wait(0.5f);
            yield return Shot("companions_room");
            app.DebugSetCompanions(false, false);

            // the coin watch scene with "무조건 승리": q forced to 100% for this capture only
            loop.LuckQCoin = 1.0;
            loop.SubTickModel = new DipPath(loop);
            d.slot = Slot.Morning; d.overseasCash = 5_000_000; d.hasPosition = false;
            app.Refresh();
            app.Perform(new CoinOpenAction(Direction.Long, 25, 2_000_000));
            float until = Time.realtimeSinceStartup + 10f;
            while (LuckOffer.Current == null && Time.realtimeSinceStartup < until) yield return null;
            yield return Wait(0.4f);
            yield return Shot("luck_coin_button");
            if (LuckOffer.Current != null) LuckOffer.Current.Press();
            yield return Wait(0.35f);
            yield return Shot("luck_coin_pressed");
            while (app.Watch.Busy) yield return null;
            yield return Wait(0.3f);
            yield return Drain(app);
            loop.LuckQCoin = 0;
            loop.SubTickModel = StudentTSubTickPath.Instance;
            UnityEngine.Debug.Log("[DevCaptureV5] luck uses " + d.stats.luckUses + ", position open " + d.hasPosition + ", overseas " + d.overseasCash);

            // the settlement table with "기도 N회 · 무조건 승리 M회"
            d.stats.prayers += 7;
            d.day = Rules.Days; d.slot = Slot.Night;
            app.Refresh();
            Click(Texts.ActSleep);
            yield return Wait(1.5f);
            float u2 = Time.realtimeSinceStartup + 6f;
            while (!loop.HasEvent && !app.Events.Busy && Time.realtimeSinceStartup < u2) yield return null;
            yield return Wait(2.0f);
            yield return Shot("settlement_luck");
            Application.Quit();
        }

        /// <summary>Presses every popup button until nothing is pending (never touches E9, which this tour never triggers).</summary>
        IEnumerator Drain(LoopApp app)
        {
            int guard = 0;
            while (guard++ < 100)
            {
                yield return null;
                if (!app.Events.Busy && !app.Loop.HasEvent && !app.Watch.Busy) yield break;
                if (app.Watch.Busy) { yield return Wait(0.3f); continue; }
                yield return Wait(0.35f);
                var buttons = app.Events.GetComponentsInChildren<Button>(false).Where(b => b.interactable).ToList();
                if (buttons.Count > 0) buttons.Last().onClick.Invoke();
                yield return Wait(0.1f);
            }
        }

        /// <summary>Invokes the first active, interactable button whose text (or a child's text) contains `label`.</summary>
        static bool Click(string label)
        {
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!b.interactable || !b.gameObject.activeInHierarchy) continue;
                foreach (var t in b.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    if (t.text != null && t.text.Contains(label)) { b.onClick.Invoke(); return true; }
                }
            }
            UnityEngine.Debug.LogWarning("[DevCaptureV5] button not found: " + label);
            return false;
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
