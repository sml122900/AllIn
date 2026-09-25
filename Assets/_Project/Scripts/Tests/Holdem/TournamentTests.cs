using System.Linq;
using AllIn.Core;
using AllIn.Holdem;
using NUnit.Framework;

namespace AllIn.Holdem.Tests
{
    /// <summary>Scripted deciders so the tournament machinery is tested without the AI.</summary>
    sealed class Scripted : IDecider, IDeciderFactory
    {
        readonly PlayerAction _action;
        public Scripted(PlayerAction action) { _action = action; }
        public PlayerAction Decide(Tournament t, int seat) => _action;
        public IDecider Create(PersonalityId p) => this;
    }

    /// <summary>Random but seeded (draws from the tournament's own stream).</summary>
    sealed class Wild : IDecider, IDeciderFactory
    {
        public PlayerAction Decide(Tournament t, int seat)
        {
            switch (t.Rng.NextInt(5))
            {
                case 0: return PlayerAction.Fold;
                case 1: return PlayerAction.AllIn;
                case 2: return PlayerAction.RaiseTo(t.Hand.CurrentBet * 3);
                default: return PlayerAction.Call;
            }
        }
        public IDecider Create(PersonalityId p) => this;
    }

    public class TournamentTests
    {
        static TournamentConfig Pub(long chips = 0)
        {
            var c = TournamentConfig.For(TournamentKind.Pub);
            if (chips > 0) c.StartingChips = chips;
            return c;
        }

        [Test]
        public void Blinds_GoUpEvery6Hands_AndHoldAfterTheLastLevel()
        {
            var calls = new Scripted(PlayerAction.Call); // nobody bets: blinds only move chips slowly
            var t = new Tournament(Pub(10_000_000), new Xoshiro128(1), calls, calls);
            var bigBlindAtHand = new long[80];
            while (t.HandNumber < 79)
            {
                t.Step();
                bigBlindAtHand[t.HandNumber] = t.Blinds.Big;
            }
            Assert.AreEqual(100, bigBlindAtHand[1]);
            Assert.AreEqual(100, bigBlindAtHand[6]);
            Assert.AreEqual(200, bigBlindAtHand[7]);
            Assert.AreEqual(400, bigBlindAtHand[13]);
            Assert.AreEqual(24_000, bigBlindAtHand[49], "level 9 starts at hand 49");
            Assert.AreEqual(24_000, bigBlindAtHand[79], "and holds");
        }

        [Test]
        public void MainEvent_Uses8HandsPerLevel()
        {
            var calls = new Scripted(PlayerAction.Call);
            var c = TournamentConfig.For(TournamentKind.Main);
            c.StartingChips = 10_000_000;
            var t = new Tournament(c, new Xoshiro128(2), calls, calls);
            while (t.HandNumber < 9) t.Step();
            Assert.AreEqual(200, t.Blinds.Big);
        }

        [Test]
        public void SameSeed_SameTournament_LogsIdentical()
        {
            var wild = new Wild();
            var a = new Tournament(Pub(), new Xoshiro128(99), wild, wild, PersonalityId.Maniac);
            var b = new Tournament(Pub(), new Xoshiro128(99), wild, wild, PersonalityId.Maniac);
            a.RunToEnd();
            b.RunToEnd();
            CollectionAssert.AreEqual(a.Result.Log, b.Result.Log);
            Assert.Greater(a.Result.Log.Count, 50);
        }

        [Test]
        public void Reentry_IsOfferedOnlyWhileLevelIsAtMost3()
        {
            var wild = new Wild();
            var t = new Tournament(Pub(), new Xoshiro128(3), wild) { StopWhenHeroOut = true };
            int offers = 0, guard = 0;
            while (!t.Finished && guard++ < 100_000)
            {
                switch (t.Step())
                {
                    case StepResult.NeedHeroAction: t.HeroAct(PlayerAction.AllIn); break;
                    case StepResult.NeedHeroReentry:
                        offers++;
                        Assert.LessOrEqual(t.LevelIndex + 1, 3);
                        t.HeroReentry(true);
                        break;
                }
            }
            Assert.IsTrue(t.Finished);
            Assert.AreEqual(offers, t.Result.Reentries);
            if (t.Result.Finish != 1) Assert.Greater(t.Result.BustedAtLevel, 3, "a final bust after the re-entry window");
        }

        [Test]
        public void DecliningReentry_EndsTheHerosTournament()
        {
            var wild = new Wild();
            for (ulong seed = 1; seed < 40; seed++)
            {
                var t = new Tournament(Pub(), new Xoshiro128(seed), wild) { StopWhenHeroOut = true };
                bool asked = false;
                int guard = 0;
                while (!t.Finished && guard++ < 100_000)
                {
                    var r = t.Step();
                    if (r == StepResult.NeedHeroAction) t.HeroAct(PlayerAction.AllIn);
                    if (r == StepResult.NeedHeroReentry) { asked = true; t.HeroReentry(false); }
                }
                if (!asked) continue;
                Assert.IsTrue(t.HeroOut);
                Assert.AreEqual(0, t.Result.Reentries);
                Assert.That(t.Result.Finish, Is.InRange(2, 9));
                return;
            }
            Assert.Fail("no seed produced an early bust");
        }

        [Test]
        public void EveryTournament_EndsWithOneWinner_AndDistinctFinishes()
        {
            var wild = new Wild();
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var t = new Tournament(Pub(), new Xoshiro128(seed), wild, wild);
                t.HeroBotReenters = false;
                t.RunToEnd();
                Assert.IsNotNull(t.Winner);
                var finishes = t.AllEntrants.Where(e => !e.Alive || e == t.Winner).Select(e => e.Finish).OrderBy(f => f).ToArray();
                CollectionAssert.AreEqual(Enumerable.Range(1, 9).ToArray(), finishes, "seed " + seed);
                Assert.AreEqual(9 * 3000 + t.AllEntrants.Sum(e => e.Reentries) * 3000, t.Winner.Stack, "chips conserved (re-entries add a stack)");
            }
        }

        [Test]
        public void Bubble_FiresOnceWhenFourRemainInThePub()
        {
            var wild = new Wild();
            var t = new Tournament(Pub(), new Xoshiro128(11), wild, wild);
            t.RunToEnd();
            var bubbles = t.Events.Where(e => e.Kind == EventKind.Bubble).ToList();
            Assert.AreEqual(1, bubbles.Count);
            Assert.AreEqual(4, bubbles[0].Amount);
        }

        [Test]
        public void MainEvent_FieldShrinks_TableRefills_UntilTheFinalTable()
        {
            var wild = new Wild();
            var t = new Tournament(TournamentConfig.For(TournamentKind.Main), new Xoshiro128(21), wild, wild);
            t.RunToEnd();
            Assert.IsTrue(t.Events.Any(e => e.Kind == EventKind.Refill), "busted seats get refilled while the field is big");
            Assert.Greater(t.HandsToFinalTable, 0);
            Assert.IsTrue(t.Events.Count(e => e.Kind == EventKind.FinalTable) == 1);
            Assert.IsFalse(t.Events.SkipWhile(e => e.Kind != EventKind.FinalTable).Any(e => e.Kind == EventKind.Refill), "no refills at the final table");
            Assert.AreEqual(1, t.Remaining);
            Assert.IsNotNull(t.Winner);
        }

        [Test]
        public void MainEvent_HeroPrize_IsTaxedCash()
        {
            var c = TournamentConfig.For(TournamentKind.Main);
            Assert.AreEqual(100_000_000, c.Payouts[0].Amount);
            Assert.AreEqual(78_000_000, c.Payouts[0].AfterTax);
            Assert.AreEqual(9, c.PaidPlaces);
            Assert.IsFalse(c.Reentry.Allowed);
        }

        [Test]
        public void HeadsUp_ButtonPostsSmallBlind()
        {
            var wild = new Wild();
            var t = new Tournament(Pub(), new Xoshiro128(5), wild, wild);
            int guard = 0;
            while (!t.Finished && guard++ < 100_000)
            {
                t.Step();
                if (t.Hand != null && t.Hand.PlayersInHand == 2 && t.Hand.Street == Street.Preflop && t.Hand.InHand.Count(x => x) == 2)
                {
                    Assert.AreEqual(t.Hand.Button, t.Hand.SmallBlindSeat);
                    return;
                }
            }
            Assert.Fail("never reached heads-up");
        }
    }
}
