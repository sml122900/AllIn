using System.Collections.Generic;
using AllIn.Core;
using AllIn.Holdem;
using AllIn.Holdem.Brain;
using NUnit.Framework;

namespace AllIn.Holdem.Tests
{
    /// <summary>DESIGN.md E1/E2: "무조건 승리" on a called all-in.</summary>
    public class LuckLadderTests
    {
        const string Trash = "7c 2d";
        const string Aces = "As Ad";
        const string DryBoard = "9c Tc 4h Kd 2s";

        sealed class CountingRng : IRng
        {
            readonly Xoshiro128 _inner;
            public int Draws;
            public CountingRng(ulong seed) { _inner = new Xoshiro128(seed); }
            public uint NextUInt() { Draws++; return _inner.NextUInt(); }
        }

        static Ladder New(double q, long seed = 1, TournamentKind kind = TournamentKind.Pub)
        {
            var cfg = LadderConfig.For(kind);
            cfg.LuckQ = q;
            return new Ladder(cfg, new Xoshiro128((ulong)seed)) { LuckRng = new Xoshiro128((ulong)seed ^ 0x77) };
        }

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

        [Test]
        public void QZero_NeverDrawsTheLuckStream()
        {
            var luck = new CountingRng(5);
            var l = new Ladder(LadderConfig.For(TournamentKind.Pub), new Xoshiro128(1)) { LuckRng = luck };
            var h = Deal(l, "7h 3d", Aces, DryBoard);
            l.Resolve(true);
            Assert.AreNotEqual(HandOutcome.Won, h.Outcome, "aces call and win on their own");
            Assert.AreEqual(0, luck.Draws);
            Assert.IsFalse(h.LuckTriggered);
        }

        [Test]
        public void RuleDefaults_AreTheUsersChoice_ThreePercentHoldem_TwoPercentCoin()
        {
            Assert.AreEqual(0.03, HoldemRules.LuckQHoldem);
            Assert.AreEqual(0.02, Rules.LuckQCoin);
        }

        [Test]
        public void CalledAllIn_WithLuck_IsAGuaranteedWin_OnARebuiltBoard()
        {
            var l = New(1);
            var h = Deal(l, "7h 3d", Aces, DryBoard);
            var original = (int[])h.Board.Clone();
            long before = l.Stack;
            l.Resolve(true);
            Assert.IsTrue(h.LuckTriggered);
            Assert.IsTrue(h.LuckFlipped);
            Assert.AreEqual(HandOutcome.Won, h.Outcome);
            Assert.AreEqual(1, l.Wins);
            Assert.AreEqual(1, l.Result.LuckWins);
            Assert.IsFalse(h.BadBeat, "a rescued hand is not a bad beat");
            Assert.Greater(l.Stack, before);
            CollectionAssert.AreNotEqual(original, h.Board);
            Assert.Greater(HandEvaluator.Evaluate(h.Hole0, h.Hole1, h.Board, 5), HandEvaluator.Evaluate(h.Opp0, h.Opp1, h.Board, 5));
            var dealt = new HashSet<int> { h.Hole0, h.Hole1, h.Opp0, h.Opp1 };
            foreach (int c in h.Board) Assert.IsTrue(dealt.Add(c), "no duplicated card: " + Card.ToText(c));
            Assert.AreEqual(9, dealt.Count);
        }

        [Test]
        public void Chips_SettleLikeAnyOrdinaryWin()
        {
            var lucky = New(1);
            var h1 = Deal(lucky, "7h 3d", Aces, DryBoard, 0.5);
            lucky.Resolve(true);

            var plain = New(0);
            var h2 = Deal(plain, "Ah Ad", "Ks Kh", "9c Tc 4h 3d 2s", 0.5);
            plain.Resolve(true);
            Assert.AreEqual(HandOutcome.Won, h2.Outcome);
            Assert.AreEqual(plain.Stack, lucky.Stack, "min(stacks) is won either way");
        }

        [Test]
        public void HeroAlreadyWinning_KeepsTheBoard_ButStillCountsAsLucky()
        {
            var l = New(1);
            var h = Deal(l, "Ah Ad", "Ks Kh", "9c Tc 4h 3d 2s");
            var original = (int[])h.Board.Clone();
            l.Resolve(true);
            Assert.IsTrue(h.LuckTriggered);
            Assert.IsFalse(h.LuckFlipped);
            CollectionAssert.AreEqual(original, h.Board);
            Assert.AreEqual(HandOutcome.Won, h.Outcome);
        }

        [Test]
        public void ASplitPot_BecomesAWin()
        {
            var l = New(1);
            var h = Deal(l, "Ah Kd", "As Kc", "2c 5d 9h Js 3d");
            l.Resolve(true);
            Assert.IsTrue(h.LuckTriggered);
            Assert.AreEqual(HandOutcome.Won, h.Outcome);
        }

        [Test]
        public void NotOnASteal_NothingWasCalled_SoNothingIsRolled()
        {
            var luck = new CountingRng(5);
            var cfg = LadderConfig.For(TournamentKind.Pub);
            cfg.LuckQ = 1;
            var l = new Ladder(cfg, new Xoshiro128(1)) { LuckRng = luck };
            var h = Deal(l, "Ah Kd", Trash, DryBoard);
            l.Resolve(true);
            Assert.AreEqual(HandOutcome.Stole, h.Outcome);
            Assert.AreEqual(0, luck.Draws);
            Assert.IsFalse(h.LuckTriggered);
        }

        [Test]
        public void NotOnAFold_OrTheFreeBlind()
        {
            var luck = new CountingRng(5);
            var cfg = LadderConfig.For(TournamentKind.Pub);
            cfg.LuckQ = 1;
            var l = new Ladder(cfg, new Xoshiro128(1)) { LuckRng = luck };
            for (int i = 0; i < 9; i++) { var h = l.BeginHand(); l.Resolve(false); Assert.IsFalse(h.LuckTriggered); }
            Assert.AreEqual(0, luck.Draws);
        }

        [Test]
        public void BigBlindCall_FacingAShove_CanBeRescuedToo()
        {
            var l = New(1);
            for (int i = 0; i < 8; i++) { l.BeginHand(); l.Resolve(false); }
            var h = l.BeginHand();
            Assert.AreEqual(TablePosition.BB, h.Pos);
            var hc = Card.ParseMany("7h 3d");
            h.Hole0 = hc[0]; h.Hole1 = hc[1];
            var aces = Card.ParseMany(Aces);
            var trash = Card.ParseMany(Trash);
            for (int i = 0; i < h.OppCount; i++)
            {
                var c = i == 0 ? aces : trash;
                h.OppCards[2 * i] = c[0]; h.OppCards[2 * i + 1] = c[1]; h.OppStackRatio[i] = 1.0;
            }
            h.Board = Card.ParseMany(DryBoard);
            h.Kind = HandKind.FreeBlind; h.ShoverSeat = 0;
            l.FindShover(h);
            Assert.AreEqual(HandKind.Facing, h.Kind);
            l.Resolve(true);
            Assert.IsTrue(h.LuckTriggered);
            Assert.AreEqual(HandOutcome.Won, h.Outcome);
        }

        [Test]
        public void TheFacingHandThatIsFolded_RollsNothing()
        {
            var luck = new CountingRng(5);
            var cfg = LadderConfig.For(TournamentKind.Pub);
            cfg.LuckQ = 1;
            var l = new Ladder(cfg, new Xoshiro128(1)) { LuckRng = luck };
            for (int i = 0; i < 8; i++) { l.BeginHand(); l.Resolve(false); }
            var h = l.BeginHand();
            var aces = Card.ParseMany(Aces);
            h.OppCards[0] = aces[0]; h.OppCards[1] = aces[1]; h.OppStackRatio[0] = 1.0;
            h.Kind = HandKind.FreeBlind; h.ShoverSeat = 0;
            l.FindShover(h);
            if (h.Kind != HandKind.Facing) Assert.Inconclusive("nobody shoved");
            l.Resolve(false);
            Assert.AreEqual(0, luck.Draws);
        }

        [Test]
        public void TheRoll_ComesFromTheLuckStream_NotFromTheDealStream()
        {
            var luck = new CountingRng(9);
            var cfg = LadderConfig.For(TournamentKind.Pub);
            cfg.LuckQ = 1;
            var l = new Ladder(cfg, new Xoshiro128(3)) { LuckRng = luck };
            Deal(l, "7h 3d", Aces, DryBoard);
            l.Resolve(true);
            Assert.Greater(luck.Draws, 0);
        }

        [Test]
        public void RunToEnd_WithLuck_ReachesTheLadderTopMoreOftenThanWithout()
        {
            int Champs(double q)
            {
                int n = 0;
                for (int s = 1; s <= 60; s++)
                {
                    var cfg = LadderConfig.For(TournamentKind.Pub);
                    cfg.LuckQ = q;
                    var l = new Ladder(cfg, new Xoshiro128((ulong)s)) { LuckRng = new Xoshiro128((ulong)s + 999) };
                    l.RunToEnd(new AlwaysPush());
                    if (l.Result.Stage == LadderStage.Seat) n++;
                }
                return n;
            }
            Assert.Greater(Champs(0.5), Champs(0));
        }

        sealed class AlwaysPush : ILadderPlayer { public bool AllIn(LadderHand h) => true; }
    }
}
