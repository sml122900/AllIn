using System;
using System.Collections.Generic;

namespace AllIn.Core
{
    // v0.5 Part B (DESIGN.md E5–E10): where the protagonist is, what he wears, what he drives, who stands beside him.
    // Pure data + catalogs; the rules that touch money live in LoopStyle.cs. Nothing here has an effect on the simulation's numbers.

    /// <summary>E6: the buildings on the street. Walking between them costs no slot.</summary>
    public enum Place { Home = 0, Restaurant, LottoShop, Pub, Bank, ClothShop, CarDealer }

    public enum ClothPart { Top = 0, Bottom = 1, Shoes = 2, Accessory = 3 }

    public enum ClothTier { Basic = 0, Brand = 1, Luxury = 2 }

    /// <summary>The player's look: two body layers (skin tone, hair) and four worn item ids (Wardrobe.Id). accessory -1 = none.</summary>
    [Serializable]
    public class Appearance
    {
        public int skin;       // 0..2
        public int hair;       // 0..2
        public int top = Wardrobe.StarterTop;
        public int bottom = Wardrobe.StarterBottom;
        public int shoes = Wardrobe.StarterShoes;
        public int accessory = -1;

        public Appearance Clone() => (Appearance)MemberwiseClone();

        public int Worn(ClothPart part)
        {
            switch (part)
            {
                case ClothPart.Top: return top;
                case ClothPart.Bottom: return bottom;
                case ClothPart.Shoes: return shoes;
                default: return accessory;
            }
        }

        internal void Wear(ClothPart part, int id)
        {
            switch (part)
            {
                case ClothPart.Top: top = id; break;
                case ClothPart.Bottom: bottom = id; break;
                case ClothPart.Shoes: shoes = id; break;
                default: accessory = id; break;
            }
        }
    }

    public static class CompanionKind
    {
        public const int Spouse = 0, Child = 1;
    }

    /// <summary>E9: a picture slot only. Nothing in the game can add one yet (roadmap ④); a debug menu does, to check the rendering.</summary>
    [Serializable]
    public class Companion
    {
        public int kind;
        public Appearance look = new Appearance();

        public static Companion Make(int kind)
        {
            var c = new Companion { kind = kind };
            c.look.skin = 0;
            c.look.hair = kind == CompanionKind.Spouse ? 2 : 1;
            c.look.top = Wardrobe.Id(ClothPart.Top, ClothTier.Brand, kind == CompanionKind.Spouse ? 2 : 1);
            c.look.bottom = Wardrobe.Id(ClothPart.Bottom, ClothTier.Basic, kind == CompanionKind.Spouse ? 2 : 1);
            c.look.shoes = Wardrobe.Id(ClothPart.Shoes, ClothTier.Basic, 1);
            return c;
        }
    }

    /// <summary>E7: the clothes shop catalog. An item id is part*100 + tier*10 + variant; the variant is the art (shape), the tier its fabric
    /// (palette) and price. The starter set (basic, variant 0, price 0) is owned from the start.</summary>
    public static class Wardrobe
    {
        public static readonly int[] VariantCount = { 5, 3, 2, 2 };   // top, bottom, shoes, accessory (E5)
        public const int StarterTop = 0, StarterBottom = 100, StarterShoes = 200;
        public static readonly int[] StarterIds = { StarterTop, StarterBottom, StarterShoes };

        static readonly long[] TierBase = { 10_000, 100_000, 1_000_000 };
        static readonly long[] TierCap = { 50_000, 500_000, 5_000_000 };          // E7: 1만~5만 / 10만~50만 / 100만~500만
        static readonly double[] PartFactor = { 1.5, 1.0, 3.0, 5.0 };

        public static int Id(ClothPart part, ClothTier tier, int variant) => (int)part * 100 + (int)tier * 10 + variant;
        public static ClothPart PartOf(int id) => (ClothPart)(id / 100);
        public static ClothTier TierOf(int id) => (ClothTier)(id / 10 % 10);
        public static int VariantOf(int id) => id % 10;

        public static bool IsValid(int id)
        {
            if (id < 0) return false;
            int part = id / 100, tier = id / 10 % 10, variant = id % 10;
            return part < VariantCount.Length && tier <= (int)ClothTier.Luxury && variant < VariantCount[part];
        }

        public static bool IsStarter(int id) => Array.IndexOf(StarterIds, id) >= 0;

        public static long Price(int id)
        {
            if (!IsValid(id)) throw new ArgumentOutOfRangeException(nameof(id));
            if (IsStarter(id)) return 0;
            int part = id / 100, tier = id / 10 % 10, variant = id % 10;
            long raw = (long)Math.Round(TierBase[tier] * PartFactor[part] * (1 + 0.1 * variant) / 1000.0) * 1000;
            return Math.Min(raw, TierCap[tier]);
        }

        public static List<int> Items(ClothTier tier, ClothPart part)
        {
            var l = new List<int>();
            for (int v = 0; v < VariantCount[(int)part]; v++) l.Add(Id(part, tier, v));
            return l;
        }

        public static string Name(int id) => Texts.ClothName(PartOf(id), TierOf(id), VariantOf(id));
    }

    /// <summary>E8: the car lot. Tier 0 = no car.</summary>
    public static class Cars
    {
        public const int None = 0, Count = 4;
        public static readonly long[] Price = { 0, 5_000_000, 30_000_000, 80_000_000, 300_000_000 };
        public const double UpkeepRate = 0.005;    // per month, of the car's price
        public const double SellRate = 0.60;

        public static bool IsValid(int tier) => tier >= 1 && tier <= Count;
        public static long UpkeepOf(int tier) => IsValid(tier) ? (long)Math.Round(Price[tier] * UpkeepRate) : 0;
        public static long SellPriceOf(int tier) => IsValid(tier) ? (long)Math.Round(Price[tier] * SellRate) : 0;
    }
}
