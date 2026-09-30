using System;
using AllIn.Core.Actions;

namespace AllIn.Core
{
    // v0.5 Part B (DESIGN.md E6–E10): street position, clothes, car. None of it feeds back into the simulation's numbers.
    public sealed partial class Loop
    {
        // ---- E6: places ----

        public Place Place => Data.place;

        /// <summary>Walking to a building. Never consumes a slot and never touches the clock.</summary>
        public Availability MoveTo(Place place)
        {
            if (Data.finished) return Availability.No("");
            if (Data.atSea) return Availability.No(Texts.AtSea);
            Data.place = place;
            return Availability.Yes;
        }

        // ---- E7: clothes ----

        public bool Owns(int clothId) => Data.ownedClothes.Contains(clothId);

        /// <summary>Paid from the bank (E7). One copy of each item; buying what you own is refused.</summary>
        public Availability BuyClothes(int clothId)
        {
            if (Data.atSea) return Availability.No(Texts.AtSea);
            if (!Wardrobe.IsValid(clothId)) return Availability.No("");
            if (Owns(clothId)) return Availability.No(Texts.Owned);
            long price = Wardrobe.Price(clothId);
            if (Data.bankCash < price) return Availability.No(Texts.NoCash);
            SpendBank(price);
            Data.ownedClothes.Add(clothId);
            Log(Texts.BoughtCloth + " " + Wardrobe.Name(clothId) + " " + Fmt.Won(-price));
            Snapshot();
            return Availability.Yes;
        }

        /// <summary>Changes what is worn (free, no slot). Only owned items.</summary>
        public Availability Equip(int clothId)
        {
            if (!Wardrobe.IsValid(clothId) || !Owns(clothId)) return Availability.No("");
            Data.appearance.Wear(Wardrobe.PartOf(clothId), clothId);
            Snapshot();
            return Availability.Yes;
        }

        /// <summary>Takes the accessory off (the other parts always keep something on).</summary>
        public void RemoveAccessory()
        {
            Data.appearance.accessory = -1;
            Snapshot();
        }

        /// <summary>The mirror at home: skin tone and hair are free to change.</summary>
        public void SetBody(int skin, int hair)
        {
            Data.appearance.skin = Math.Max(0, Math.Min(2, skin));
            Data.appearance.hair = Math.Max(0, Math.Min(2, hair));
            Snapshot();
        }

        // ---- E8: car ----

        public bool HasCar => Data.carTier > 0;
        public long CarUpkeep => Cars.UpkeepOf(Data.carTier);

        public Availability BuyCar(int tier)
        {
            if (Data.atSea) return Availability.No(Texts.AtSea);
            if (!Cars.IsValid(tier)) return Availability.No("");
            if (HasCar) return Availability.No(Texts.CarOnlyOne);
            long price = Cars.Price[tier];
            if (Data.bankCash < price) return Availability.No(Texts.NoCash);
            SpendBank(price);
            Data.carTier = tier;
            Log(Texts.CarBought + " " + Texts.CarNames[tier] + " " + Fmt.Won(-price));
            Snapshot();
            return Availability.Yes;
        }

        /// <summary>Immediate sale at 60% of the car's price, into the bank.</summary>
        public Availability SellCar()
        {
            if (Data.atSea) return Availability.No(Texts.AtSea);
            if (!HasCar) return Availability.No(Texts.CarNone);
            long proceeds = Cars.SellPriceOf(Data.carTier);
            AddBank(proceeds);
            Log(Texts.CarSold + " " + Fmt.Won(proceeds));
            Data.carTier = 0;
            Snapshot();
            return Availability.Yes;
        }

        /// <summary>E8: charged on the rent day, after the rent, from the bank as it stands. Not part of the bankruptcy arithmetic: if the bank
        /// can't cover it nothing is taken and one line says so.</summary>
        void ChargeCarUpkeep()
        {
            if (!HasCar) return;
            long upkeep = CarUpkeep;
            if (Data.bankCash >= upkeep)
            {
                Data.bankCash -= upkeep;
                Log(Texts.CarUpkeepPaid(upkeep));
            }
            else Log(Texts.CarUpkeepUnpaid(upkeep));
        }

        // ---- E10: v1 -> v2 defaults ----

        static partial void MigrateStyle(SaveData d)
        {
            if (d.appearance == null) d.appearance = new Appearance();
            if (d.ownedClothes == null) d.ownedClothes = new System.Collections.Generic.List<int>();
            foreach (int id in Wardrobe.StarterIds) if (!d.ownedClothes.Contains(id)) d.ownedClothes.Add(id);
            if (d.companions == null) d.companions = new System.Collections.Generic.List<Companion>();
            if (!Wardrobe.IsValid(d.appearance.top)) d.appearance.top = Wardrobe.StarterTop;
            if (!Wardrobe.IsValid(d.appearance.bottom)) d.appearance.bottom = Wardrobe.StarterBottom;
            if (!Wardrobe.IsValid(d.appearance.shoes)) d.appearance.shoes = Wardrobe.StarterShoes;
            if (d.carTier < 0 || d.carTier > Cars.Count) d.carTier = 0;
        }
    }
}
