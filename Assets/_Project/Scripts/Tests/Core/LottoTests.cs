using System.Linq;
using AllIn.Core;
using AllIn.Core.Actions;
using NUnit.Framework;

namespace AllIn.Core.Tests
{
    public class LottoTests
    {
        [Test]
        public void OnePurchase_Is5000Won_For5Games()
        {
            var loop = Loop.NewRun(21);
            long cash = loop.Data.cash;
            Assert.IsTrue(loop.Do(LottoBuyAction.Instance).Ok);
            Assert.AreEqual(cash - 5_000, loop.Data.cash);
            Assert.AreEqual(1, loop.Data.tickets.Count);
            var nums = loop.Data.tickets[0].numbers;
            Assert.AreEqual(30, nums.Length);
            for (int g = 0; g < 5; g++)
            {
                var game = nums.Skip(g * 6).Take(6).ToArray();
                Assert.AreEqual(6, game.Distinct().Count());
                Assert.That(game.All(n => n >= 1 && n <= 45));
                CollectionAssert.IsOrdered(game);
            }
        }

        [Test]
        public void DailyLimit_Is20Purchases_ResetsNextDay()
        {
            var loop = Loop.NewRun(22);
            for (int i = 0; i < 20; i++) Assert.IsTrue(loop.Do(LottoBuyAction.Instance).Ok);
            Assert.IsFalse(loop.Do(LottoBuyAction.Instance).Ok);
            while (loop.Data.slot != Slot.Night) loop.Pass();
            Assert.IsFalse(loop.Do(LottoBuyAction.Instance).Ok, "no sales at night");
            loop.Pass();
            Assert.IsTrue(loop.Do(LottoBuyAction.Instance).Ok);
        }

        [Test]
        public void TheoreticalOdds_EmergeFromRealDraws_OverAMillionGames()
        {
            var rng = new Xoshiro128(777);
            var counts = new int[6];
            const int n = 1_000_000;
            for (int i = 0; i < n; i++) counts[LottoRules.Rank(LottoRules.PickGame(rng), LottoRules.Draw(rng))]++;
            double fifth = counts[5] / (double)n, fourth = counts[4] / (double)n;
            Assert.That(fifth, Is.InRange(1.0 / 45 * 0.9, 1.0 / 45 * 1.1), "5th prize ≈ 1/45");
            Assert.That(fourth, Is.InRange(1.0 / 733 * 0.8, 1.0 / 733 * 1.2), "4th prize ≈ 1/733");
        }

        [Test]
        public void Rank_ClassifiesMatches()
        {
            int[] drawn = { 1, 2, 3, 4, 5, 6, 7 };
            Assert.AreEqual(1, LottoRules.Rank(new[] { 1, 2, 3, 4, 5, 6 }, drawn));
            Assert.AreEqual(2, LottoRules.Rank(new[] { 1, 2, 3, 4, 5, 7 }, drawn));
            Assert.AreEqual(3, LottoRules.Rank(new[] { 1, 2, 3, 4, 5, 40 }, drawn));
            Assert.AreEqual(4, LottoRules.Rank(new[] { 1, 2, 3, 4, 7, 40 }, drawn), "bonus only matters for 5 matches");
            Assert.AreEqual(5, LottoRules.Rank(new[] { 1, 2, 3, 30, 31, 32 }, drawn));
            Assert.AreEqual(0, LottoRules.Rank(new[] { 1, 2, 30, 31, 32, 7 }, drawn));
        }

        [TestCase(2_000_000, 0)]
        [TestCase(2_000_001, 440_000)]
        [TestCase(300_000_000, 66_000_000)]
        [TestCase(300_000_001, 99_000_000)]
        public void Tax_BracketBoundaries(long gross, long expectedTax) => Assert.AreEqual(expectedTax, LottoRules.Tax(gross));

        [Test]
        public void FirstPrize_NetIs1_34Billion()
        {
            Assert.AreEqual(1_340_000_000, LottoRules.NetPrize(1));
            Assert.AreEqual(39_000_000, LottoRules.NetPrize(2));
            Assert.AreEqual(1_500_000, LottoRules.NetPrize(3));
        }

        [Test]
        public void Draws_HappenFourTimes_OnSaturdayEvenings()
        {
            var loop = Loop.NewRun(23);
            var drawDays = new System.Collections.Generic.List<int>();
            while (!loop.Finished)
            {
                if (loop.Data.slot == Slot.Dawn) loop.Do(LottoBuyAction.Instance);
                int before = loop.Data.stats.lottoDraws;
                int day = loop.Data.day;
                Slot slot = loop.Data.slot;
                loop.Pass();
                if (loop.Data.stats.lottoDraws > before)
                {
                    Assert.AreEqual(Slot.Evening, slot);
                    drawDays.Add(day);
                }
            }
            CollectionAssert.AreEqual(new[] { 6, 13, 20, 27 }, drawDays);
            Assert.AreEqual(4, loop.Data.pendingEvents.Count(e => e.id == EventIds.LottoDraw));
        }

        [Test]
        public void SaturdayEveningPurchase_MakesThatDraw_SundayPurchaseWaitsAWeek()
        {
            Assert.AreEqual(13, Calendar.NextSaturday(7));
            var loop = Loop.NewRun(24);
            while (!(loop.Data.day == 6 && loop.Data.slot == Slot.Evening)) loop.Pass();
            loop.Do(LottoBuyAction.Instance);
            Assert.AreEqual(6, loop.Data.tickets[0].drawDay, "Saturday evening purchase still makes the draw");
        }

        [Test]
        public void LosingPurchase_CostsOneMental()
        {
            var loop = Loop.NewRun(25);
            while (!(loop.Data.day == 6 && loop.Data.slot == Slot.Evening)) loop.Pass();
            loop.Do(LottoBuyAction.Instance);
            loop.Data.tickets[0].numbers = Enumerable.Repeat(0, 30).ToArray(); // rigged: 0 is never drawn
            int mental = loop.Data.mental;
            loop.Pass();
            Assert.AreEqual(mental - 1, loop.Data.mental);
        }
    }
}
