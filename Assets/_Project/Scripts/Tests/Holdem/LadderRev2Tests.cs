using AllIn.Core;
using AllIn.Holdem;
using AllIn.Holdem.Brain;
using NUnit.Framework;

namespace AllIn.Holdem.Tests
{
    /// <summary>DESIGN.md C-v2 rev.2: BB ante + facing a steal shove, opponents' stacks from the tournament average.</summary>
    public class LadderRev2Tests
    {
        const string Trash = "7c 2d";
        const string Aces = "As Ad";
        const string DryBoard = "9c Tc 4h Kd 2s";
        const string HeroWinsBoard = "9c Tc 4h 3d 2s";

        static Ladder Modern(TournamentKind kind = TournamentKind.Pub, long seed = 1) => new Ladder(LadderConfig.For(kind), new Xoshiro128((ulong)seed));

        static LadderHand Deal(Ladder l, string hero, string opp, string board, double mult = 1.0)
        {
            var h = l.BeginHand();
            var hc = Card.ParseMany(hero);
            h.Hole0 = hc[0]; h.Hole1 = hc[1];
            var oc = Card.ParseMany(opp);
            for (int i = 0; i < h.OppCount; i++) { h.OppCards[2 * i] = oc[0]; h.OppCards[2 * i + 1] = oc[1]; h.OppStackRatio[i] = mult; }
            h.Board = Card.ParseMany(board);
            return h;
        }

        static void FoldOrbitUpToBb(Ladder l, int hands)
        {
            for (int i = 0; i < hands; i++) { l.BeginHand(); l.Resolve(false); }
        }

        /// <summary>Rigs the BB hand: everybody ahead holds trash except opponent #shover (1-based, 0 = nobody) who holds aces.</summary>
        static void RigBb(Ladder l, LadderHand h, int shover, string board = DryBoard, double mult = 1.0)
        {
            var trash = Card.ParseMany(Trash);
            var aces = Card.ParseMany(Aces);
            for (int i = 0; i < h.OppCount; i++)
            {
                var c = i + 1 == shover ? aces : trash;
                h.OppCards[2 * i] = c[0]; h.OppCards[2 * i + 1] = c[1];
                h.OppStackRatio[i] = mult;
            }
            var hc = Card.ParseMany("7h 3d");
            h.Hole0 = hc[0]; h.Hole1 = hc[1];
            h.Board = Card.ParseMany(board);
            h.Kind = HandKind.FreeBlind; h.ShoverSeat = 0;
            l.FindShover(h);
        }

        [Test]
        public void LadderEconomy_PubThreeWinsOf8_SatelliteSixWinsOf64_LogTwoOfTheField()
        {
            var pub = LadderConfig.For(TournamentKind.Pub);
            var sat = LadderConfig.For(TournamentKind.Satellite);
            Assert.AreEqual(8, pub.Field); Assert.AreEqual(3, pub.Steps[0].Wins); Assert.AreEqual(25_000, pub.EntryFee);
            Assert.AreEqual(64, sat.Field); Assert.AreEqual(6, sat.Steps[0].Wins); Assert.AreEqual(HoldemRules.SatelliteFee, sat.EntryFee);
            Assert.AreEqual(3, (int)System.Math.Log(pub.Field, 2));
            Assert.AreEqual(6, (int)System.Math.Log(sat.Field, 2));
            var l = new Ladder(sat, new Xoshiro128(1));
            Assert.AreEqual(64, l.RemainingField(0));
            Assert.AreEqual(2, l.RemainingField(5), "the bubble: the win before the seat");
            Assert.AreEqual(1, l.RemainingField(6));
        }

        [Test]
        public void Steal_AwardsBothBlindsAndTheAnte_2p5Bb()
        {
            var l = Modern();
            var h = Deal(l, "Ah Kd", Trash, DryBoard);
            long s = l.Stack;
            l.Resolve(true);
            Assert.AreEqual(HandOutcome.Stole, h.Outcome);
            Assert.AreEqual(s + (long)(2.5 * h.Bb), l.Stack);
        }

        [Test]
        public void Steal_AsSmallBlind_NetsTwoBigBlinds()
        {
            var l = Modern();
            FoldOrbitUpToBb(l, 7);
            var h = Deal(l, "Ah Kd", Trash, DryBoard);
            Assert.AreEqual(TablePosition.SB, h.Pos);
            long s = l.Stack;
            l.Resolve(true);
            Assert.AreEqual(s + (long)(2.5 * h.Bb) - h.Bb / 2, l.Stack);
        }

        [Test]
        public void BbHand_NobodyShoves_CollectsTheSmallBlind()
        {
            var l = Modern();
            FoldOrbitUpToBb(l, 8);
            var h = l.BeginHand();
            RigBb(l, h, 0);
            Assert.AreEqual(HandKind.FreeBlind, h.Kind);
            long s = l.Stack;
            l.Resolve(true);
            Assert.AreEqual(s + h.Bb / 2, l.Stack);
            Assert.AreEqual(HandOutcome.FreeBlind, h.Outcome);
        }

        [Test]
        public void BbHand_FacingAShove_FoldingCostsTheBlindAndTheAnte()
        {
            var l = Modern();
            FoldOrbitUpToBb(l, 8);
            var h = l.BeginHand();
            RigBb(l, h, 3);
            Assert.AreEqual(HandKind.Facing, h.Kind);
            Assert.AreEqual(3, h.ShoverSeat);
            Assert.AreEqual(TablePosition.UTG, h.ShoverPos);
            long s = l.Stack;
            l.Resolve(false);
            Assert.AreEqual(HandOutcome.Folded, h.Outcome);
            Assert.AreEqual(s - 2 * h.Bb, l.Stack);
            Assert.AreEqual(0, l.Wins);
            Assert.AreEqual(1, l.Result.BbFacing);
            Assert.AreEqual(0, l.Result.BbCalls);
        }

        [Test]
        public void BbHand_FacingAShove_CallPlaysLikeAnyCalledAllIn()
        {
            var l = Modern();
            FoldOrbitUpToBb(l, 8);
            var h = l.BeginHand();
            RigBb(l, h, 5, DryBoard, 0.5);       // aces at half the average stack, hero holds 7-3
            l.Resolve(true);
            Assert.AreEqual(5, h.CallerSeat);
            Assert.AreEqual(HandOutcome.LostSurvived, h.Outcome, "covering: lose their stack, keep playing");
            Assert.AreEqual(1, l.Result.BbCalls);

            var l2 = Modern(TournamentKind.Main);
            FoldOrbitUpToBb(l2, 8);
            var h2 = l2.BeginHand();
            RigBb(l2, h2, 5, DryBoard, 1.5);
            l2.Resolve(true);
            Assert.AreEqual(HandOutcome.LostOut, h2.Outcome, "covered: eliminated");
            Assert.IsTrue(l2.Finished);
            Assert.AreEqual(1, l2.Result.BustBb.Count);
        }

        [Test]
        public void AnOrbitOfFolds_CostsTwoAndAHalfBigBlinds_WhenTheBbHandIsShovedAt()
        {
            var l = Modern();
            FoldOrbitUpToBb(l, 7);
            long s0 = l.Stack;
            var sb = l.BeginHand();
            l.Resolve(false);
            double sbCost = (s0 - l.Stack) / (double)sb.Bb;
            var bbHand = l.BeginHand();
            RigBb(l, bbHand, 2);
            long s1 = l.Stack;
            l.Resolve(false);
            double bbCost = (s1 - l.Stack) / (double)bbHand.Bb;
            Assert.AreEqual(0.5, sbCost, 1e-9);
            Assert.AreEqual(2.0, bbCost, 1e-9);
            Assert.AreEqual(2.5, sbCost + bbCost, 1e-9);
        }

        [Test]
        public void AverageStack_IsStartingChipsTimesFieldOverRemaining()
        {
            Assert.AreEqual(1500.0, Modern(TournamentKind.Main).AverageStack, 1e-9);
            var pub = Modern();
            Deal(pub, "Ah Ad", "Ks Kh", HeroWinsBoard);
            pub.Resolve(true);
            Assert.AreEqual(1, pub.Wins);
            Assert.AreEqual(1500.0 * 8 / 4, pub.AverageStack, 1e-9);
        }

        [Test]
        public void OpponentStack_FollowsTheAverage_NotTheHero()
        {
            var l = Modern();
            l.Stack = 300;          // the hero is short; the opponent sits at the average (1500)
            var h = Deal(l, "Ah Ad", "Ks Kh", HeroWinsBoard);
            l.Resolve(true);
            Assert.AreEqual(1500, h.OppStack);
            Assert.AreEqual(HandOutcome.Won, h.Outcome);
            Assert.AreEqual(600, l.Stack, "wins min(mine, theirs) = 300");
        }

        [Test]
        public void Reentry_RestartsAtTheStartingChips_NotAtTheCurrentBlinds()
        {
            var l = Modern();
            FoldOrbitUpToBb(l, 7);
            Assert.AreEqual(1, l.Level);
            var h = l.BeginHand();                 // SB
            var hc = Card.ParseMany("7h 3d");
            h.Hole0 = hc[0]; h.Hole1 = hc[1];
            var ac = Card.ParseMany(Aces);
            for (int i = 0; i < h.OppCount; i++) { h.OppCards[2 * i] = ac[0]; h.OppCards[2 * i + 1] = ac[1]; h.OppStackRatio[i] = 2.0; }
            h.Board = Card.ParseMany(DryBoard);
            l.Resolve(true);
            Assert.IsTrue(l.AwaitingReentry);
            l.Reenter(true);
            Assert.AreEqual(1500, l.Stack);
        }

        [Test]
        public void OpponentStackMultiplier_IsLognormalWithMedianOne()
        {
            var mults = new System.Collections.Generic.List<double>();
            for (int seed = 1; seed <= 60; seed++)
            {
                var l = Modern(TournamentKind.Main, seed);
                for (int i = 0; i < 9; i++) { var h = l.BeginHand(); mults.AddRange(h.OppStackRatio); l.Resolve(false); }
            }
            mults.Sort();
            Assert.AreEqual(1.0, mults[mults.Count / 2], 0.08);
            Assert.Greater(mults[mults.Count - 1], 1.5);
            Assert.Less(mults[0], 0.67);
        }
    }
}
