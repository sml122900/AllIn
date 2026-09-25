using System;
using System.Linq;
using AllIn.Core;
using AllIn.Holdem;
using NUnit.Framework;

namespace AllIn.Holdem.Tests
{
    public class HandEvaluatorTests
    {
        static int E(string cards) => HandEvaluator.Evaluate(Card.ParseMany(cards));

        static void Beats(string a, string b) => Assert.Greater(E(a), E(b), a + " should beat " + b);
        static void Ties(string a, string b) => Assert.AreEqual(E(a), E(b), a + " should tie " + b);

        [TestCase("As Ks Qs Js Ts 2d 3c", HandCategory.StraightFlush)]
        [TestCase("5h 4h 3h 2h Ah Kc Kd", HandCategory.StraightFlush)] // steel wheel
        [TestCase("9c 9d 9h 9s 2d 3c 4h", HandCategory.Quads)]
        [TestCase("Kc Kd Kh 2s 2d 7c 8h", HandCategory.FullHouse)]
        [TestCase("Kc Kd Kh 2s 2d 2c 8h", HandCategory.FullHouse)] // two trips
        [TestCase("Ac 9c 7c 4c 2c Kd Qh", HandCategory.Flush)]
        [TestCase("5d 4c 3h 2s Ah Kc 9d", HandCategory.Straight)] // wheel
        [TestCase("Tc Jd Qh Ks Ah 2c 3d", HandCategory.Straight)]
        [TestCase("7c 7d 7h Ks 2d 4c 9h", HandCategory.Trips)]
        [TestCase("7c 7d 5h 5s 2d 2c 9h", HandCategory.TwoPair)]
        [TestCase("7c 7d 5h 4s 2d Jc 9h", HandCategory.Pair)]
        [TestCase("7c 3d 5h 4s Jd Kc 9h", HandCategory.HighCard)]
        public void Categories(string cards, HandCategory expected) => Assert.AreEqual(expected, HandEvaluator.Category(E(cards)));

        [Test] public void Wheel_LosesToSixHighStraight() => Beats("6d 5c 4h 3s 2h Kc Kd", "5d 4c 3h 2s Ah Kc Kd");
        [Test] public void SteelWheel_BeatsQuads() => Beats("5h 4h 3h 2h Ah Kc Kd", "9c 9d 9h 9s Ah 3c 4d");
        [Test] public void SteelWheel_LosesToSixHighStraightFlush() => Beats("6h 5h 4h 3h 2h Kc Kd", "5h 4h 3h 2h Ah Kc Kd");
        [Test] public void FullHouse_HigherTripsWins() => Beats("Kc Kd Kh 2s 2d 7c 8h", "Qc Qd Qh As Ad 7c 8h");
        [Test] public void FullHouse_SameTrips_HigherPairWins() => Beats("Kc Kd Kh As Ad 7c 8h", "Kc Kd Kh Qs Qd 7c 8h");
        [Test] public void FullHouse_TwoTrips_UsesSecondTripsAsPair() => Beats("Kc Kd Kh 9s 9d 9c 8h", "Kc Kd Kh 8s 8d 2c 3h");
        [Test] public void Flush_KickerDecides() => Beats("Ac Kc 9c 7c 4c 2d 3h", "Ac Qc 9c 7c 4c 2d 3h");
        [Test] public void Flush_FifthCardKicker() => Beats("Ac Kc 9c 7c 5c 2d 3h", "Ac Kc 9c 7c 4c 2d 3h");
        [Test] public void Flush_OnlyBestFiveCount() => Ties("Ac Kc 9c 7c 5c 3c 2d", "Ac Kc 9c 7c 5c 4c 2d");
        [Test] public void BoardPlays_IsASplit() => Ties("2c 3d As Ks Qs Js Ts", "4c 5d As Ks Qs Js Ts");
        [Test] public void BoardStraight_Split() => Ties("2c 2d 9h Ts Jd Qc Kh", "3c 3d 9h Ts Jd Qc Kh");
        [Test] public void Straight_HigherTopWins() => Beats("Tc Jd Qh Ks 9h 2c 3d", "Tc Jd Qh 9s 8h 2c 3d");
        [Test] public void Straight_SixCardRunUsesTop() => Ties("4c 5d 6h 7s 8h 9c 2d", "Kc 5d 6h 7s 8h 9c 2d");
        [Test] public void TwoPair_ThirdPairCanBeKicker() => Beats("Ac Ad Kh Ks Qd Qc 2h", "Ac Ad Kh Ks Jd Tc 2h");
        [Test] public void TwoPair_KickerDecides() => Beats("Ac Ad Kh Ks Qd 3c 2h", "Ac Ad Kh Ks Jd 3c 2h");
        [Test] public void Pair_ThirdKickerDecides() => Beats("9c 9d Ah Ks Qd 3c 2h", "9c 9d Ah Ks Jd 3c 2h");
        [Test] public void Quads_KickerDecides() => Beats("9c 9d 9h 9s Ad 3c 2h", "9c 9d 9h 9s Kd 3c 2h");
        [Test] public void Quads_BoardQuads_KickerSplit() => Ties("9c 9d 9h 9s Ad 3c 2h", "9c 9d 9h 9s Ad 4c 2h");
        [Test] public void HighCard_SixthCardIgnored() => Ties("Ac Kd 9h 7s 5d 3c 2h", "Ac Kd 9h 7s 5d 4c 2h");

        [Test]
        public void MatchesBruteForceReference_OnRandomSevenCardHands()
        {
            var rng = new Xoshiro128(31337);
            var deck = Enumerable.Range(0, 52).ToArray();
            var seven = new int[7];
            for (int n = 0; n < 20000; n++)
            {
                rng.Shuffle(deck);
                Array.Copy(deck, seven, 7);
                int fast = HandEvaluator.Evaluate(seven);
                int slow = Reference.Best5Of7(seven);
                Assert.AreEqual(slow, fast, string.Join(" ", seven.Select(Card.ToText)));
            }
        }

        /// <summary>Deliberately naive: every 5-of-7 combination, each scored by sorting ranks.</summary>
        static class Reference
        {
            public static int Best5Of7(int[] c)
            {
                int best = -1;
                var five = new int[5];
                for (int a = 0; a < 7; a++)
                for (int b = a + 1; b < 7; b++)
                {
                    int k = 0;
                    for (int i = 0; i < 7; i++) if (i != a && i != b) five[k++] = c[i];
                    best = Math.Max(best, Score5(five));
                }
                return best;
            }

            static int Score5(int[] f)
            {
                var ranks = f.Select(Card.Rank).OrderByDescending(r => r).ToArray();
                bool flush = f.All(x => Card.Suit(x) == Card.Suit(f[0]));
                bool distinct = ranks.Distinct().Count() == 5;
                int straightTop = -1;
                if (distinct && ranks[0] - ranks[4] == 4) straightTop = ranks[0];
                if (distinct && ranks.SequenceEqual(new[] { 12, 3, 2, 1, 0 })) straightTop = 3;
                var groups = ranks.GroupBy(r => r).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).ToArray();
                var order = groups.SelectMany(g => Enumerable.Repeat(g.Key, g.Count())).Distinct().ToArray();
                HandCategory cat;
                if (flush && straightTop >= 0) return Pack(HandCategory.StraightFlush, straightTop);
                if (groups[0].Count() == 4) cat = HandCategory.Quads;
                else if (groups[0].Count() == 3 && groups[1].Count() == 2) cat = HandCategory.FullHouse;
                else if (flush) return Pack(HandCategory.Flush, ranks);
                else if (straightTop >= 0) return Pack(HandCategory.Straight, straightTop);
                else if (groups[0].Count() == 3) cat = HandCategory.Trips;
                else if (groups[0].Count() == 2 && groups[1].Count() == 2) cat = HandCategory.TwoPair;
                else if (groups[0].Count() == 2) cat = HandCategory.Pair;
                else return Pack(HandCategory.HighCard, ranks);
                return Pack(cat, order);
            }

            static int Pack(HandCategory c, params int[] r)
            {
                int s = (int)c << 20, shift = 16;
                foreach (var x in r) { s |= x << shift; shift -= 4; }
                return s;
            }
        }
    }
}
