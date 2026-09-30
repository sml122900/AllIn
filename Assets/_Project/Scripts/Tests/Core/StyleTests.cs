using System.Linq;
using AllIn.Core;
using AllIn.Core.Actions;
using NUnit.Framework;

namespace AllIn.Core.Tests
{
    /// <summary>DESIGN.md E5–E10: paper doll, places, clothes, car, companions, save migration.</summary>
    public class StyleTests
    {
        static readonly ICharacterLayerSource Src = CodeLayerSource.Instance;

        static Appearance Look(int skin = 0, int hair = 0, int top = 0, int bottom = 100, int shoes = 200, int acc = -1) =>
            new Appearance { skin = skin, hair = hair, top = top, bottom = bottom, shoes = shoes, accessory = acc };

        static int Opaque(uint[] px) => px.Count(p => (p >> 24) != 0);

        // ---------- paper doll ----------

        [Test]
        public void Compose_SameSelection_GivesTheSamePixels_EveryPoseAndFrame()
        {
            var a = Look(1, 2, Wardrobe.Id(ClothPart.Top, ClothTier.Brand, 3), Wardrobe.Id(ClothPart.Bottom, ClothTier.Luxury, 2), Wardrobe.Id(ClothPart.Shoes, ClothTier.Basic, 1), Wardrobe.Id(ClothPart.Accessory, ClothTier.Luxury, 1));
            var b = a.Clone();
            foreach (DollPose pose in new[] { DollPose.Idle, DollPose.Walk, DollPose.Lie, DollPose.Sit })
                for (int f = 0; f < PaperDoll.FrameCount(pose); f++)
                    CollectionAssert.AreEqual(PaperDoll.Compose(Src, a, pose, f), PaperDoll.Compose(new CodeLayerSource(), b, pose, f), pose + " frame " + f);
        }

        [Test]
        public void Compose_EachLayerChoice_ChangesThePixels()
        {
            var basePx = PaperDoll.Compose(Src, Look(), DollPose.Idle, 0);
            void Differs(Appearance a, string what) => CollectionAssert.AreNotEqual(basePx, PaperDoll.Compose(Src, a, DollPose.Idle, 0), what);
            Differs(Look(skin: 1), "skin");
            Differs(Look(hair: 2), "hair");
            Differs(Look(top: Wardrobe.Id(ClothPart.Top, ClothTier.Basic, 1)), "top");
            Differs(Look(bottom: Wardrobe.Id(ClothPart.Bottom, ClothTier.Basic, 1)), "bottom");
            Differs(Look(shoes: Wardrobe.Id(ClothPart.Shoes, ClothTier.Basic, 1)), "shoes");
            Differs(Look(acc: Wardrobe.Id(ClothPart.Accessory, ClothTier.Basic, 0)), "accessory");
            Differs(Look(top: Wardrobe.Id(ClothPart.Top, ClothTier.Luxury, 0)), "the same shape in another tier is another fabric");
        }

        [Test]
        public void Compose_LayersStackInOrder_HairOverTopOverBody()
        {
            var bodyOnly = Src.GetLayer(LayerKind.Body, 0, 0, DollPose.Idle, 0);
            var full = PaperDoll.Compose(Src, Look(hair: 2), DollPose.Idle, 0);
            Assert.Greater(Opaque(full), Opaque(bodyOnly), "clothes and long hair add pixels beyond the bare body");
            var hair = Src.GetLayer(LayerKind.Hair, 2, 0, DollPose.Idle, 0);
            for (int i = 0; i < hair.Length; i++) if ((hair[i] >> 24) != 0) Assert.AreEqual(hair[i], full[i], "hair is drawn on top");
        }

        [Test]
        public void HairVariants_ReachFurther_ShortToLong()
        {
            int Count(int v) => Opaque(Src.GetLayer(LayerKind.Hair, v, 0, DollPose.Idle, 0));
            Assert.Less(Count(0), Count(1));
            Assert.Less(Count(1), Count(2));
        }

        [Test]
        public void TopVariants_TeeShowsBareForearms_CoatCoversTheHips()
        {
            int Count(int v) => Opaque(Src.GetLayer(LayerKind.Top, v, 0, DollPose.Idle, 0));
            Assert.Less(Count(1), Count(2), "tee < hoodie (sleeves)");
            Assert.Less(Count(2), Count(3), "hoodie < jacket (hem)");
            Assert.Less(Count(3), Count(4), "jacket < coat (longer)");
        }

        [Test]
        public void EveryPose_DrawsSomething_InsideTheCanvas_AndTheFeetOrBackRestOnTheFloor()
        {
            foreach (DollPose pose in new[] { DollPose.Idle, DollPose.Walk, DollPose.Lie, DollPose.Sit })
                for (int f = 0; f < PaperDoll.FrameCount(pose); f++)
                {
                    var px = PaperDoll.Compose(Src, Look(acc: 1), pose, f);
                    Assert.Greater(Opaque(px), 60, pose + " " + f);
                    Assert.IsTrue(Enumerable.Range(0, 32).Any(x => (px[31 * 32 + x] >> 24) != 0) || pose == DollPose.Sit, pose + " " + f + ": something touches the bottom row");
                }
        }

        [Test]
        public void FrameCounts_MatchTheSpec_IdleTwoWalkFourLieSitOne()
        {
            Assert.AreEqual(2, PaperDoll.FrameCount(DollPose.Idle));
            Assert.AreEqual(4, PaperDoll.FrameCount(DollPose.Walk));
            Assert.AreEqual(1, PaperDoll.FrameCount(DollPose.Lie));
            Assert.AreEqual(1, PaperDoll.FrameCount(DollPose.Sit));
        }

        [Test]
        public void Animation_FramesDiffer_IdleBreathesWalkMovesItsLegs()
        {
            var a = Look();
            CollectionAssert.AreNotEqual(PaperDoll.Compose(Src, a, DollPose.Idle, 0), PaperDoll.Compose(Src, a, DollPose.Idle, 1));
            for (int f = 1; f < 4; f++)
                if (f != 2) CollectionAssert.AreNotEqual(PaperDoll.Compose(Src, a, DollPose.Walk, 0), PaperDoll.Compose(Src, a, DollPose.Walk, f), "walk frame " + f);
        }

        [Test]
        public void ComposeWrapsFrameNumbers_SoAnyCounterCanDriveTheAnimation()
        {
            var a = Look();
            CollectionAssert.AreEqual(PaperDoll.Compose(Src, a, DollPose.Walk, 1), PaperDoll.Compose(Src, a, DollPose.Walk, 5));
            CollectionAssert.AreEqual(PaperDoll.Compose(Src, a, DollPose.Lie, 0), PaperDoll.Compose(Src, a, DollPose.Lie, 3));
        }

        [Test]
        public void ASwappedLayerSource_IsUsedAsIs_TheSeamForPngSheets()
        {
            var solid = new SolidSource();
            var px = PaperDoll.Compose(solid, Look(), DollPose.Idle, 0);
            Assert.AreEqual(0xFF112233u, px[0]);
        }

        sealed class SolidSource : ICharacterLayerSource
        {
            public uint[] GetLayer(LayerKind kind, int variant, int tier, DollPose pose, int frame)
            {
                var p = new uint[PaperDoll.Size * PaperDoll.Size];
                if (kind == LayerKind.Body) for (int i = 0; i < p.Length; i++) p[i] = 0xFF112233u;
                return p;
            }
        }

        // ---------- catalog ----------

        [Test]
        public void Catalog_VariantCountsMatchTheSpec()
        {
            CollectionAssert.AreEqual(new[] { 5, 3, 2, 2 }, Wardrobe.VariantCount);
        }

        [Test]
        public void Prices_StayInsideTheTierBands_AndStarterIsFree()
        {
            long[] lo = { 10_000, 100_000, 1_000_000 }, hi = { 50_000, 500_000, 5_000_000 };
            foreach (ClothTier tier in new[] { ClothTier.Basic, ClothTier.Brand, ClothTier.Luxury })
                foreach (ClothPart part in new[] { ClothPart.Top, ClothPart.Bottom, ClothPart.Shoes, ClothPart.Accessory })
                    foreach (int id in Wardrobe.Items(tier, part))
                    {
                        if (Wardrobe.IsStarter(id)) { Assert.AreEqual(0, Wardrobe.Price(id)); continue; }
                        Assert.GreaterOrEqual(Wardrobe.Price(id), lo[(int)tier], id.ToString());
                        Assert.LessOrEqual(Wardrobe.Price(id), hi[(int)tier], id.ToString());
                    }
        }

        [Test]
        public void ItemIds_RoundTripPartTierVariant()
        {
            int id = Wardrobe.Id(ClothPart.Shoes, ClothTier.Luxury, 1);
            Assert.AreEqual(ClothPart.Shoes, Wardrobe.PartOf(id));
            Assert.AreEqual(ClothTier.Luxury, Wardrobe.TierOf(id));
            Assert.AreEqual(1, Wardrobe.VariantOf(id));
            Assert.IsFalse(Wardrobe.IsValid(Wardrobe.Id(ClothPart.Shoes, ClothTier.Basic, 2)), "shoes have two shapes");
        }

        // ---------- buying and wearing ----------

        [Test]
        public void NewRun_StartsWithTheStarterSetWorn_AndNothingElse()
        {
            var loop = Loop.NewRun(1);
            CollectionAssert.AreEquivalent(Wardrobe.StarterIds, loop.Data.ownedClothes);
            Assert.AreEqual(Wardrobe.StarterTop, loop.Data.appearance.top);
            Assert.AreEqual(-1, loop.Data.appearance.accessory);
            Assert.AreEqual(0, loop.Data.carTier);
            Assert.AreEqual(0, loop.Data.companions.Count);
        }

        [Test]
        public void BuyClothes_PaysFromTheBank_AndAddsItToTheWardrobe()
        {
            var loop = Loop.NewRun(2);
            int id = Wardrobe.Id(ClothPart.Top, ClothTier.Brand, 2);
            long before = loop.Data.bankCash, price = Wardrobe.Price(id);
            Assert.IsTrue(loop.BuyClothes(id).Ok);
            Assert.AreEqual(before - price, loop.Data.bankCash);
            Assert.IsTrue(loop.Owns(id));
        }

        [Test]
        public void BuyClothes_RefusesWhatYouOwn_AndWhatYouCannotPay_AndChangesNothing()
        {
            var loop = Loop.NewRun(3);
            int id = Wardrobe.Id(ClothPart.Top, ClothTier.Luxury, 4);
            loop.Data.bankCash = Wardrobe.Price(id) - 1;
            var no = loop.BuyClothes(id);
            Assert.IsFalse(no.Ok);
            Assert.AreEqual(Wardrobe.Price(id) - 1, loop.Data.bankCash);
            Assert.IsFalse(loop.Owns(id));
            loop.Data.bankCash = Wardrobe.Price(id) * 2;
            Assert.IsTrue(loop.BuyClothes(id).Ok);
            Assert.IsFalse(loop.BuyClothes(id).Ok, "one copy each");
            Assert.AreEqual(Wardrobe.Price(id), loop.Data.bankCash);
        }

        [Test]
        public void Money_ComesOnlyFromTheBank_NotTheExchangeWallets()
        {
            var loop = Loop.NewRun(4);
            int id = Wardrobe.Id(ClothPart.Shoes, ClothTier.Luxury, 0);
            loop.Data.bankCash = 0;
            loop.Data.domesticCash = 10_000_000; loop.Data.overseasCash = 10_000_000;
            Assert.IsFalse(loop.BuyClothes(id).Ok);
        }

        [Test]
        public void Equip_OnlyOwnedItems_AndItSticksInTheSave()
        {
            var loop = Loop.NewRun(5);
            int id = Wardrobe.Id(ClothPart.Top, ClothTier.Basic, 1);
            Assert.IsFalse(loop.Equip(id).Ok, "not bought yet");
            Assert.IsTrue(loop.BuyClothes(id).Ok);
            Assert.IsTrue(loop.Equip(id).Ok);
            Assert.AreEqual(id, loop.Data.appearance.top);
            var restored = Loop.FromSave(loop.Snapshot());
            Assert.AreEqual(id, restored.Data.appearance.top);
            Assert.IsTrue(restored.Owns(id));
        }

        [Test]
        public void Equip_PutsEachItemInItsOwnSlot()
        {
            var loop = Loop.NewRun(6);
            loop.Data.bankCash = 50_000_000;
            var ids = new[]
            {
                Wardrobe.Id(ClothPart.Top, ClothTier.Brand, 1), Wardrobe.Id(ClothPart.Bottom, ClothTier.Brand, 2),
                Wardrobe.Id(ClothPart.Shoes, ClothTier.Basic, 1), Wardrobe.Id(ClothPart.Accessory, ClothTier.Luxury, 0),
            };
            foreach (int id in ids) { Assert.IsTrue(loop.BuyClothes(id).Ok); Assert.IsTrue(loop.Equip(id).Ok); }
            var a = loop.Data.appearance;
            CollectionAssert.AreEqual(ids, new[] { a.top, a.bottom, a.shoes, a.accessory });
            loop.RemoveAccessory();
            Assert.AreEqual(-1, loop.Data.appearance.accessory);
        }

        [Test]
        public void Wardrobe_ChangesTakeNoSlot()
        {
            var loop = Loop.NewRun(7);
            loop.Data.bankCash = 5_000_000;
            int abs = loop.AbsSlot;
            int id = Wardrobe.Id(ClothPart.Top, ClothTier.Basic, 1);
            loop.BuyClothes(id); loop.Equip(id); loop.SetBody(2, 1);
            Assert.AreEqual(abs, loop.AbsSlot);
            Assert.AreEqual(2, loop.Data.appearance.skin);
            Assert.AreEqual(1, loop.Data.appearance.hair);
        }

        [Test]
        public void SetBody_ClampsToTheAvailableLayers()
        {
            var loop = Loop.NewRun(8);
            loop.SetBody(99, -4);
            Assert.AreEqual(2, loop.Data.appearance.skin);
            Assert.AreEqual(0, loop.Data.appearance.hair);
        }

        // ---------- places ----------

        [Test]
        public void MoveTo_ChangesThePlace_ButNeverConsumesASlotOrTouchesTheClock()
        {
            var loop = Loop.NewRun(9);
            int abs = loop.AbsSlot, day = loop.Data.day;
            long assets = loop.TotalAssets;
            int body = loop.Data.body, mental = loop.Data.mental;
            foreach (Place p in new[] { Place.Restaurant, Place.LottoShop, Place.Pub, Place.Bank, Place.ClothShop, Place.CarDealer, Place.Home })
            {
                Assert.IsTrue(loop.MoveTo(p).Ok);
                Assert.AreEqual(p, loop.Place);
            }
            Assert.AreEqual(abs, loop.AbsSlot);
            Assert.AreEqual(day, loop.Data.day);
            Assert.AreEqual(assets, loop.TotalAssets);
            Assert.AreEqual(body, loop.Data.body);
            Assert.AreEqual(mental, loop.Data.mental);
            Assert.AreEqual(0, loop.Data.stats.slotsPlayed);
        }

        [Test]
        public void MoveTo_HasNoEffectOnTheRandomStreams()
        {
            var a = Loop.NewRun(10);
            var b = Loop.NewRun(10);
            a.MoveTo(Place.Bank); a.MoveTo(Place.Restaurant);
            for (int i = 0; i < 25; i++) { a.Pass(); b.Pass(); }
            Assert.AreEqual(a.Data.price, b.Data.price);
            Assert.AreEqual(a.Data.bankCash, b.Data.bankCash);
            CollectionAssert.AreEqual(a.Snapshot().rngState, b.Snapshot().rngState);
        }

        // ---------- car ----------

        [Test]
        public void BuyCar_PaysFromTheBank_OneCarOnly()
        {
            var loop = Loop.NewRun(11);
            long before = loop.Data.bankCash;
            Assert.IsTrue(loop.BuyCar(1).Ok);
            Assert.AreEqual(before - 5_000_000, loop.Data.bankCash);
            Assert.AreEqual(1, loop.Data.carTier);
            Assert.IsFalse(loop.BuyCar(2).Ok, "one car at a time");
            Assert.AreEqual(1, loop.Data.carTier);
        }

        [Test]
        public void BuyCar_RefusesWhenTheBankIsShort()
        {
            var loop = Loop.NewRun(12);
            loop.Data.bankCash = 29_999_999;
            Assert.IsFalse(loop.BuyCar(2).Ok);
            Assert.AreEqual(0, loop.Data.carTier);
            Assert.AreEqual(29_999_999, loop.Data.bankCash);
        }

        [Test]
        public void CarPrices_AreTheSpecifiedFour()
        {
            CollectionAssert.AreEqual(new long[] { 0, 5_000_000, 30_000_000, 80_000_000, 300_000_000 }, Cars.Price);
        }

        [Test]
        public void SellCar_PaysSixtyPercent_AndFreesTheDriveway()
        {
            var loop = Loop.NewRun(13);
            loop.Data.bankCash = 100_000_000;
            loop.BuyCar(3);
            long before = loop.Data.bankCash;
            Assert.IsTrue(loop.SellCar().Ok);
            Assert.AreEqual(before + 48_000_000, loop.Data.bankCash);
            Assert.AreEqual(0, loop.Data.carTier);
            Assert.IsFalse(loop.SellCar().Ok, "nothing left to sell");
        }

        [Test]
        public void Upkeep_IsHalfAPercentOfThePrice_ChargedWithTheRentOnTheRentDay()
        {
            var loop = Loop.NewRun(14);
            loop.Data.bankCash = 40_000_000;
            Assert.IsTrue(loop.BuyCar(2).Ok);          // 30,000,000 -> upkeep 150,000
            Assert.AreEqual(150_000, loop.CarUpkeep);
            long bankBefore = loop.Data.bankCash;
            int rentDay = loop.Data.nextRentDay;
            loop.BankruptcyEnabled = true;
            int guard = 0;
            while (loop.Data.day < rentDay && guard++ < 1000) { loop.Pass(); loop.Data.pendingEvents.Clear(); if (loop.Finished) loop.BeginContinueLife(); }
            // Day (rentDay) dawn: the rent and the upkeep both left the bank on this dawn
            long food = loop.Data.stats.foodPaid * Rules.FoodPerDay;
            long expected = bankBefore - food - Rules.Rent - 150_000;
            Assert.AreEqual(expected, loop.Data.bankCash);
            Assert.IsTrue(loop.Data.log.Any(l => l.Contains(Texts.CarUpkeepPaid(150_000))), "the upkeep line is in the log");
        }

        [Test]
        public void Upkeep_ThatTheBankCannotCover_IsSkipped_WithOneLine_AndNeverAddedToTheArrears()
        {
            var loop = Loop.NewRun(15);
            loop.Data.bankCash = 300_000_000;
            loop.BuyCar(4);                            // 300,000,000 -> upkeep 1,500,000
            loop.Data.bankCash = 0;
            loop.Data.nextRentDay = loop.Data.day + 1;
            loop.BankruptcyEnabled = true;
            int guard = 0;
            while (loop.Data.rentOwed == 0 && guard++ < 20) { loop.Pass(); loop.Data.pendingEvents.Clear(); }
            Assert.AreEqual(Rules.Rent, loop.Data.rentOwed, "the arrears are the rent alone (D3): the upkeep is not part of the bankruptcy arithmetic");
            Assert.AreEqual(0, loop.Data.bankCash);
            Assert.IsTrue(loop.Data.log.Any(l => l.Contains(Texts.CarUpkeepUnpaid(1_500_000))));
            Assert.AreEqual(4, loop.Data.carTier, "he keeps the car");
        }

        [Test]
        public void NoCar_NoUpkeep_TheOldRentDayIsUnchanged()
        {
            var a = Loop.NewRun(16);
            var b = Loop.NewRun(16);
            a.Data.bankCash = b.Data.bankCash = 5_000_000;
            for (int i = 0; i < 200; i++) { a.Pass(); a.Data.pendingEvents.Clear(); if (a.Finished) a.BeginContinueLife(); }
            for (int i = 0; i < 200; i++) { b.Pass(); b.Data.pendingEvents.Clear(); if (b.Finished) b.BeginContinueLife(); }
            Assert.AreEqual(a.Data.bankCash, b.Data.bankCash);
            Assert.IsFalse(a.Data.log.Any(l => l.Contains("차 유지비")));
        }

        [Test]
        public void ACar_IsNotPartOfTheAssets_ItIsConsumption()
        {
            var loop = Loop.NewRun(17);
            loop.Data.bankCash = 100_000_000;
            long total = loop.TotalAssets;
            loop.BuyCar(3);
            Assert.AreEqual(total - 80_000_000, loop.TotalAssets);
        }

        // ---------- companions & migration ----------

        [Test]
        public void Companions_HaveNoWayIn_ExceptWritingTheListDirectly()
        {
            var loop = Loop.NewRun(18);
            for (int i = 0; i < 100; i++) { loop.Pass(); loop.Data.pendingEvents.Clear(); if (loop.Finished) loop.BeginContinueLife(); }
            Assert.AreEqual(0, loop.Data.companions.Count);
            loop.Data.companions.Add(Companion.Make(CompanionKind.Spouse));
            loop.Data.companions.Add(Companion.Make(CompanionKind.Child));
            var restored = Loop.FromSave(loop.Snapshot());
            Assert.AreEqual(2, restored.Data.companions.Count);
            Assert.AreEqual(CompanionKind.Child, restored.Data.companions[1].kind);
        }

        [Test]
        public void Companions_RenderThroughTheSameDoll()
        {
            foreach (int kind in new[] { CompanionKind.Spouse, CompanionKind.Child })
            {
                var c = Companion.Make(kind);
                Assert.Greater(Opaque(PaperDoll.Compose(Src, c.look, DollPose.Idle, 0)), 100);
            }
        }

        [Test]
        public void OldSave_Version1_GetsTheDefaultLookOneUniformNoCar_AndTheNewVersion()
        {
            var old = Loop.NewRun(19).Snapshot();
            old.version = 1;
            old.appearance = null;
            old.ownedClothes = null;
            old.companions = null;
            old.luckRngState = new int[4];
            var loop = Loop.FromSave(old);
            Assert.AreEqual(SaveData.CurrentVersion, loop.Data.version);
            Assert.AreEqual(2, SaveData.CurrentVersion);
            var a = loop.Data.appearance;
            CollectionAssert.AreEqual(new[] { 0, 0, Wardrobe.StarterTop, Wardrobe.StarterBottom, Wardrobe.StarterShoes, -1 }, new[] { a.skin, a.hair, a.top, a.bottom, a.shoes, a.accessory });
            CollectionAssert.AreEquivalent(Wardrobe.StarterIds, loop.Data.ownedClothes);
            Assert.AreEqual(0, loop.Data.carTier);
            Assert.AreEqual(0, loop.Data.companions.Count);
            Assert.AreEqual(Place.Home, loop.Data.place);
        }

        [Test]
        public void MigrationRepairsABrokenLook_SoTheDollAlwaysDraws()
        {
            var old = Loop.NewRun(20).Snapshot();
            old.version = 1;
            old.appearance.top = 999;
            old.appearance.bottom = -5;
            var loop = Loop.FromSave(old);
            Assert.AreEqual(Wardrobe.StarterTop, loop.Data.appearance.top);
            Assert.AreEqual(Wardrobe.StarterBottom, loop.Data.appearance.bottom);
        }

        [Test]
        public void StyleSettings_DoNotDisturbTheSimulation_SameSeedSamePricesAndCash()
        {
            var plain = Loop.NewRun(21);
            var dressed = Loop.NewRun(21);
            dressed.Data.bankCash += 10_000_000; plain.Data.bankCash += 10_000_000;
            dressed.BuyClothes(Wardrobe.Id(ClothPart.Top, ClothTier.Luxury, 2)); dressed.MoveTo(Place.Bank);
            long bonus = Wardrobe.Price(Wardrobe.Id(ClothPart.Top, ClothTier.Luxury, 2));
            for (int i = 0; i < 60; i++) { plain.Pass(); dressed.Pass(); plain.Data.pendingEvents.Clear(); dressed.Data.pendingEvents.Clear(); }
            Assert.AreEqual(plain.Data.price, dressed.Data.price);
            Assert.AreEqual(plain.Data.bankCash - bonus, dressed.Data.bankCash);
        }
    }
}
