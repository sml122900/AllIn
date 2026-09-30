using AllIn.Core;
using AllIn.Core.Actions;
using AllIn.Game;
using NUnit.Framework;

namespace AllIn.Game.Tests
{
    public class SaveRoundTripTests
    {
        static Loop PlayABit(long seed)
        {
            var loop = Loop.NewRun(seed);
            loop.Data.overseasCash = 3_000_000; // coin margin only comes from the overseas wallet (B5 v0.3)
            loop.Do(LottoBuyAction.Instance);
            loop.Do(new CoinOpenAction(Direction.Short, 25, 3_000_000));
            for (int i = 0; i < 13; i++) loop.Pass();
            return loop;
        }

        [Test]
        public void SaveThenLoad_JsonRestoresIdenticalState()
        {
            var loop = PlayABit(4242);
            string json = SaveIO.ToJson(loop.Snapshot());
            var restored = SaveIO.FromJson(json);
            Assert.AreEqual(json, SaveIO.ToJson(restored));
            Assert.AreEqual(loop.Data.price, restored.price, "doubles survive JSON bit-exact");
            Assert.AreEqual(loop.Data.hasPosition, restored.hasPosition);
            Assert.AreEqual(loop.Data.tickets.Count, restored.tickets.Count);
        }

        [Test]
        public void StyleFields_SurviveJson_LookClothesCarCompanionsPlaceAndLuckStream()
        {
            var loop = Loop.NewRun(31);
            loop.Data.bankCash = 400_000_000;
            int top = Wardrobe.Id(ClothPart.Top, ClothTier.Luxury, 4), bag = Wardrobe.Id(ClothPart.Accessory, ClothTier.Brand, 1);
            loop.BuyClothes(top); loop.Equip(top); loop.BuyClothes(bag); loop.Equip(bag);
            loop.SetBody(2, 1);
            loop.BuyCar(3);
            loop.MoveTo(Place.CarDealer);
            loop.Data.companions.Add(Companion.Make(CompanionKind.Spouse));
            loop.Data.companions.Add(Companion.Make(CompanionKind.Child));
            for (int i = 0; i < 5; i++) loop.LuckRng.NextUInt();
            var restored = SaveIO.FromJson(SaveIO.ToJson(loop.Snapshot()));
            Assert.AreEqual(SaveData.CurrentVersion, restored.version);
            Assert.AreEqual(top, restored.appearance.top);
            Assert.AreEqual(bag, restored.appearance.accessory);
            Assert.AreEqual(2, restored.appearance.skin);
            Assert.AreEqual(1, restored.appearance.hair);
            CollectionAssert.AreEquivalent(loop.Data.ownedClothes, restored.ownedClothes);
            Assert.AreEqual(3, restored.carTier);
            Assert.AreEqual(Place.CarDealer, restored.place);
            Assert.AreEqual(2, restored.companions.Count);
            Assert.AreEqual(CompanionKind.Child, restored.companions[1].kind);
            CollectionAssert.AreEqual(loop.Snapshot().luckRngState, restored.luckRngState);
            var again = Loop.FromSave(restored);
            Assert.AreEqual(loop.LuckRng.NextUInt(), again.LuckRng.NextUInt());
        }

        [Test]
        public void AVersion1Save_WithoutTheNewFields_LoadsAsTheDefaultLook()
        {
            var fresh = Loop.NewRun(32).Snapshot();
            string json = SaveIO.ToJson(fresh);
            // strip what version 1 never had, the way an old file on disk would look
            foreach (string key in new[] { "\"appearance\"", "\"ownedClothes\"", "\"carTier\"", "\"companions\"", "\"place\"", "\"luckRngState\"" })
                Assert.IsTrue(json.Contains(key), key);
            var old = UnityEngine.JsonUtility.FromJson<SaveData>("{\"version\":1,\"seed\":32,\"day\":1,\"body\":80,\"mental\":60,\"price\":100000000,\"bankCash\":20000000,\"marketRngState\":[1,2,3,4],\"rngState\":[5,6,7,8],\"subTickRngState\":[9,10,11,12]}");
            var loop = Loop.FromSave(old);
            Assert.AreEqual(2, loop.Data.version);
            Assert.AreEqual(Wardrobe.StarterTop, loop.Data.appearance.top);
            CollectionAssert.AreEquivalent(Wardrobe.StarterIds, loop.Data.ownedClothes);
            Assert.AreEqual(0, loop.Data.carTier);
            Assert.AreEqual(Place.Home, loop.Data.place);
            Assert.AreNotEqual(0, loop.Data.luckRngState[0] | loop.Data.luckRngState[1] | loop.Data.luckRngState[2] | loop.Data.luckRngState[3], "the luck stream is derived from the seed");
        }

        [Test]
        public void RestoredRun_ContinuesExactlyLikeTheOriginal()
        {
            var a = PlayABit(99);
            var b = Loop.FromSave(SaveIO.FromJson(SaveIO.ToJson(a.Snapshot())));
            for (int i = 0; i < 60 && !a.Finished; i++)
            {
                if (a.Data.slot == Slot.Evening) { a.Do(WorkRestaurantAction.Instance); b.Do(WorkRestaurantAction.Instance); }
                else { a.Do(LottoBuyAction.Instance); b.Do(LottoBuyAction.Instance); a.Pass(); b.Pass(); }
            }
            Assert.AreEqual(SaveIO.ToJson(a.Snapshot()), SaveIO.ToJson(b.Snapshot()));
        }
    }
}
