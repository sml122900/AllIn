using System.Linq;
using AllIn.Core;
using AllIn.Holdem;
using AllIn.Holdem.Brain;
using NUnit.Framework;

namespace AllIn.Holdem.Tests
{
    /// <summary>DESIGN.md C-v2: all-in-or-fold hand loop and the win ladder.</summary>
    public class LadderTests
    {
        static Ladder New(TournamentKind kind = TournamentKind.Pub, long seed = 1) => new Ladder(LadderConfig.For(kind), new Xoshiro128((ulong)seed));

        /// <summary>Rigs the deal of the current hand: hero cards, every opponent's cards, board, and every opponent's stack ratio.</summary>
        static LadderHand Deal(Ladder l, string hero, string opp, string board, double ratio = 1.0)
        {
            var h = l.BeginHand();
            var hc = Card.ParseMany(hero);
            h.Hole0 = hc[0]; h.Hole1 = hc[1];
            var oc = Card.ParseMany(opp);
            for (int i = 0; i < h.Behind; i++) { h.OppCards[2 * i] = oc[0]; h.OppCards[2 * i + 1] = oc[1]; h.OppStackRatio[i] = ratio; }
            h.Board = Card.ParseMany(board);
            return h;
        }

        const string Trash = "7c 2d";      // never calls a shove
        const string Aces = "As Ad";       // always calls
        const string DryBoard = "9c Tc 4h Kd 2s";

        static void Fold(Ladder l) { l.BeginHand(); l.Resolve(false); }

        [Test]
        public void NobodyCalls_IsASteal_1p5BbAndNoWin()
        {
            var l = New();
            long bb = l.Bb;
            var h = Deal(l, "Ah Kd", Trash, DryBoard);
            l.Resolve(true);
            Assert.AreEqual(HandOutcome.Stole, h.Outcome);
            Assert.AreEqual(1500 + (long)(1.5 * bb), l.Stack);
            Assert.AreEqual(0, l.Wins);
            Assert.AreEqual(1, l.Result.Steals);
        }

        [Test]
        public void OnlyTheFirstCallerPlays()
        {
            var l = New();
            var h = Deal(l, "7h 3d", Aces, DryBoard);
            l.Resolve(true);
            Assert.AreEqual(1, h.CallerSeat, "seat 1 acts first and calls; the rest fold");
            Assert.AreEqual(1, l.Result.CalledAllIns);
        }

        [Test]
        public void Win_AddsOneWin_AndTheSmallerStack()
        {
            var l = New();
            var h = Deal(l, "Ah Ad", "7c 2d", DryBoard);
            // make the opponent call anyway: rig their cards to something inside every range but weaker than aces
            h.OppCards[0] = Card.Parse("Ks"); h.OppCards[1] = Card.Parse("Kh");
            h.Board = Card.ParseMany("9c Tc 4h 3d 2s");
            h.OppStackRatio[0] = 0.5;
            long before = l.Stack;
            l.Resolve(true);
            Assert.AreEqual(HandOutcome.Won, h.Outcome);
            Assert.AreEqual(1, l.Wins);
            Assert.AreEqual(before + before / 2, l.Stack, "min(my stack, theirs) = theirs = half");
        }

        [Test]
        public void Win_ReplacesTheBeatenSeat_OthersKeepTheirPersonality()
        {
            var l = New(TournamentKind.Satellite, 5);
            var before = (PersonalityId[])l.SeatPersonality.Clone();
            var h = Deal(l, "Ah Ad", "Ks Kh", "9c Tc 4h 3d 2s", 1.0);
            l.Resolve(true);
            Assert.AreEqual(HandOutcome.Won, h.Outcome);
            for (int s = 2; s < 9; s++) Assert.AreEqual(before[s], l.SeatPersonality[s], "seat " + s);
        }

        [Test]
        public void Loss_WhenIAmCovering_LosesTheirStackAndKeepsPlaying()
        {
            var l = New();
            var h = Deal(l, "7h 3d", Aces, DryBoard, 0.5);
            long stack = l.Stack;
            l.Resolve(true);
            Assert.AreEqual(HandOutcome.LostSurvived, h.Outcome);
            Assert.AreEqual(stack - stack / 2, l.Stack);
            Assert.IsFalse(l.Finished);
            Assert.IsFalse(l.AwaitingReentry);
            Assert.AreEqual(0, l.Wins, "the win count is kept (it was 0)");
        }

        [Test]
        public void Loss_KeepsTheWinCount_WhenSurviving()
        {
            var l = New();
            var w = Deal(l, "Ah Ad", "Ks Kh", "9c Tc 4h 3d 2s", 0.5);
            l.Resolve(true);
            Assert.AreEqual(1, l.Wins);
            var h = Deal(l, "7h 3d", Aces, DryBoard, 0.5);
            l.Resolve(true);
            Assert.AreEqual(HandOutcome.LostSurvived, h.Outcome);
            Assert.AreEqual(1, l.Wins);
        }

        [Test]
        public void Loss_WhenCoveredOrEqual_IsElimination()
        {
            foreach (double ratio in new[] { 1.0, 1.5 })
            {
                var l = New(TournamentKind.Main);
                var h = Deal(l, "7h 3d", Aces, DryBoard, ratio);
                l.Resolve(true);
                Assert.AreEqual(HandOutcome.LostOut, h.Outcome, "ratio " + ratio);
                Assert.IsTrue(l.Finished, "Main has no re-entry");
                Assert.AreEqual(0, l.Stack);
            }
        }

        [Test]
        public void Split_ChangesNothing()
        {
            var l = New();
            long stack = l.Stack;
            var h = Deal(l, "Ah Kd", "As Kc", "2c 5d 9h Jd Qs");
            l.Resolve(true);
            Assert.AreEqual(HandOutcome.Split, h.Outcome);
            Assert.AreEqual(stack, l.Stack);
            Assert.AreEqual(0, l.Wins);
        }

        [Test]
        public void BlindsRiseEverySixHands_HalvingTheStackInBigBlinds()
        {
            var l = New();
            Assert.AreEqual(100, l.Bb);
            for (int i = 0; i < 5; i++) Fold(l);
            Assert.AreEqual(100, l.Bb, "5 hands: still level 1");
            Fold(l);
            Assert.AreEqual(200, l.Bb, "6 hands played: level 2");
            Assert.AreEqual(1, l.Level);
            var h = l.BeginHand();
            Assert.AreEqual(1500 / 200.0, h.StackBbBefore, 0.01, "chips unchanged, bb count halved... minus the SB fold");
        }

        [Test]
        public void UnderOneBigBlind_TheNextHandIsAnAutomaticAllIn()
        {
            var l = New();
            l.Stack = 50;
            var h = l.BeginHand();
            Assert.AreEqual(HandKind.ForcedAllIn, h.Kind);
            l.Resolve(false); // the "fold" is ignored
            Assert.IsTrue(h.ChoseAllIn);
        }

        [Test]
        public void TheBigBlindHand_IsFree_HalfABigBlind()
        {
            var l = New();
            for (int i = 0; i < 7; i++) Fold(l); // hands 1..7, the 7th is BTN; the SB fold on hand 8 costs 0.5bb
            var sb = l.BeginHand();
            Assert.AreEqual(TablePosition.SB, sb.Pos);
            long s0 = l.Stack;
            l.Resolve(false);
            Assert.AreEqual(s0 - sb.Bb / 2, l.Stack, "folding the small blind loses half a big blind");
            var bbHand = l.BeginHand();
            Assert.AreEqual(TablePosition.BB, bbHand.Pos);
            Assert.AreEqual(HandKind.FreeBlind, bbHand.Kind);
            long s1 = l.Stack;
            l.Resolve(true);
            Assert.AreEqual(s1 + bbHand.Bb / 2, l.Stack);
            Assert.AreEqual(0, l.Wins);
        }

        [Test]
        public void ThePositionCycles_AndOpponentsBehindShrinkToTheBlinds()
        {
            var l = New();
            var behind = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 9; i++) { var h = l.BeginHand(); behind.Add(h.Behind); l.Resolve(false); }
            CollectionAssert.AreEqual(new[] { 8, 7, 6, 5, 4, 3, 2, 1, 0 }, behind);
        }

        [Test]
        public void Steal_AsTheSmallBlind_NetsOneBigBlind()
        {
            var l = New();
            for (int i = 0; i < 7; i++) Fold(l);
            var h = Deal(l, "Ah Kd", Trash, DryBoard);
            Assert.AreEqual(TablePosition.SB, h.Pos);
            long s = l.Stack;
            l.Resolve(true);
            Assert.AreEqual(HandOutcome.Stole, h.Outcome);
            Assert.AreEqual(s + (long)(1.5 * h.Bb) - h.Bb / 2, l.Stack);
        }

        [Test]
        public void Pub_ThreeWinsEndTheTournament_WithASeat()
        {
            var l = New();
            for (int w = 0; w < 3; w++)
            {
                if (l.Wins != w) break;
                var h = Deal(l, "Ah Ad", "Ks Kh", "9c Tc 4h 3d 2s", 1.0);
                if (h.Kind == HandKind.FreeBlind) { l.Resolve(true); w--; continue; }
                l.Resolve(true);
            }
            Assert.IsTrue(l.Finished);
            Assert.AreEqual(LadderStage.Seat, l.Result.Stage);
            Assert.AreEqual(PrizeType.Seat, l.Result.Prize.Type);
            Assert.AreEqual(3, l.Result.Wins);
        }

        [Test]
        public void Bubble_FiresWhenTheNextWinPaysForTheFirstTime()
        {
            var l = New();
            var a = Deal(l, "Ah Ad", "Ks Kh", "9c Tc 4h 3d 2s", 1.0);
            l.Resolve(true);
            Assert.IsFalse(a.BubbleNow);
            var b = Deal(l, "Ah Ad", "Ks Kh", "9c Tc 4h 3d 2s", 1.0);
            l.Resolve(true);
            Assert.AreEqual(2, l.Wins);
            Assert.IsTrue(b.BubbleNow);
            Assert.IsTrue(l.Result.BubbleReached);
        }

        [Test]
        public void RemainingField_IsFieldOver2ToTheWins_RoundedUp()
        {
            var main = New(TournamentKind.Main);
            Assert.AreEqual(200, main.RemainingField(0));
            Assert.AreEqual(100, main.RemainingField(1));
            Assert.AreEqual(7, main.RemainingField(5));
            Assert.AreEqual(1, main.RemainingField(8));
            Assert.AreEqual(1, main.RemainingField(12));
        }

        [Test]
        public void Main_LadderStagesPayTheirPrizes_AndTheBestStageWins()
        {
            var expected = new (int wins, LadderStage stage)[] { (5, LadderStage.MoneyIn), (6, LadderStage.FinalTable), (7, LadderStage.Podium), (8, LadderStage.Champion) };
            var l = New(TournamentKind.Main);
            int guard = 0;
            while (!l.Finished && guard++ < 200)
            {
                var h = Deal(l, "Ah Ad", "Ks Kh", "9c Tc 4h 3d 2s", 1.0);
                if (h.Kind == HandKind.FreeBlind) { l.Resolve(true); continue; }
                l.Resolve(true);
                var e = expected.FirstOrDefault(x => x.wins == l.Wins);
                if (e.stage != LadderStage.None && h.Outcome == HandOutcome.Won) Assert.AreEqual(e.stage, h.StageReached, "wins " + l.Wins);
            }
            Assert.IsTrue(l.Finished);
            Assert.AreEqual(LadderStage.Champion, l.Result.Stage);
            Assert.AreEqual(1, l.Result.Finish);
            Assert.AreEqual(100_000_000, l.Result.Prize.Amount);
        }

        [Test]
        public void Main_BustAfterMoneyIn_PaysTheBestStageReached()
        {
            var l = New(TournamentKind.Main);
            int guard = 0;
            while (l.Wins < 5 && guard++ < 200)
            {
                var h = Deal(l, "Ah Ad", "Ks Kh", "9c Tc 4h 3d 2s", 1.0);
                l.Resolve(true);
            }
            Assert.AreEqual(5, l.Wins);
            Deal(l, "7h 3d", Aces, DryBoard, 1.0);
            l.Resolve(true);
            Assert.IsTrue(l.Finished);
            Assert.AreEqual(LadderStage.MoneyIn, l.Result.Stage);
            Assert.AreEqual(5_000_000, l.Result.Prize.Amount);
        }

        [Test]
        public void Reentry_IsOfferedOnlyInTheFirstThreeLevels_AndRestartsAt15Bb()
        {
            var l = New();
            var h = Deal(l, "7h 3d", Aces, DryBoard, 1.5);
            l.Resolve(true);
            Assert.IsTrue(l.AwaitingReentry);
            l.Reenter(true);
            Assert.AreEqual(1, l.Result.Reentries);
            Assert.AreEqual(15 * l.Bb, l.Stack);
            Assert.AreEqual(0, l.Wins);

            var late = New();
            for (int i = 0; i < 18; i++) Fold(late); // 3 levels of folds
            Assert.AreEqual(3, late.Level);
            late.Stack = 1000 * late.Bb;
            Deal(late, "7h 3d", Aces, DryBoard, 1.5);
            late.Resolve(true);
            Assert.IsTrue(late.Finished, "level 4: the window has closed");
        }

        [Test]
        public void SeatPersonalities_AreFixedAcrossFolds()
        {
            var l = New();
            var before = (PersonalityId[])l.SeatPersonality.Clone();
            for (int i = 0; i < 20; i++) Fold(l);
            CollectionAssert.AreEqual(before, l.SeatPersonality);
        }

        [Test]
        public void SameSeed_SameTournament()
        {
            string Run()
            {
                var l = New(TournamentKind.Satellite, 77);
                l.RunToEnd(ChartPlayer.Instance);
                return string.Join("|", l.Result.Log);
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void TheDeal_DoesNotDependOnTheHeroChoice()
        {
            var a = New(TournamentKind.Pub, 9);
            var b = New(TournamentKind.Pub, 9);
            var ha = a.BeginHand(); a.Resolve(false);
            var hb = b.BeginHand(); b.Resolve(true);
            Assert.AreEqual(ha.Hole0, hb.Hole0);
            Assert.AreEqual(ha.Hole1, hb.Hole1);
        }

        [Test]
        public void BadBeat_IsAHeavyFavouriteLosing()
        {
            var l = New();
            var h = Deal(l, "As Ad", "Ks Kh", "Kd 4c 9h 2s 7d", 0.5);
            l.Resolve(true);
            Assert.AreEqual(HandOutcome.LostSurvived, h.Outcome);
            Assert.Greater(h.HeroEquity, 0.7);
            Assert.IsTrue(h.BadBeat);
            Assert.AreEqual(1, l.Result.BadBeats);
        }
    }
}
