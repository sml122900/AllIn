using System.Linq;
using AllIn.Core;
using AllIn.Holdem;
using AllIn.Holdem.Brain;
using NUnit.Framework;

namespace AllIn.Holdem.Tests
{
    public class BrainTests
    {
        static double Eq(string a, string b, ulong seed = 42)
        {
            var hands = new[] { Card.ParseMany(a), Card.ParseMany(b) };
            return Equity.VsKnown(hands, 0, new int[5], 0, new Xoshiro128(seed), 2000);
        }

        [Test] public void Equity_AAvsKK_About82Percent() => Assert.That(Eq("As Ah", "Kd Kc"), Is.InRange(0.79, 0.85));

        [Test] public void Equity_AKsVs22_AboutACoinFlip() => Assert.That(Eq("As Ks", "2d 2c"), Is.InRange(0.46, 0.54));

        [Test]
        public void Equity_OnTheTurn_IsExact()
        {
            // AA vs KK on a blank turn: KK has 2 outs of 44 → AA wins 42/44
            var hands = new[] { Card.ParseMany("As Ah"), Card.ParseMany("Kd Kc") };
            var board = Card.ParseMany("2s 7h 9d 3c").Concat(new[] { 0 }).ToArray();
            Assert.AreEqual(42.0 / 44, Equity.VsKnown(hands, 0, board, 4, new Xoshiro128(1), 1), 1e-12);
        }

        [Test]
        public void HandRanks_Cover169Classes_1326Combos()
        {
            Assert.AreEqual(169, HandRanks.Order.Distinct().Count());
            Assert.AreEqual(1326, HandRanks.Order.Sum(HandRanks.Combos));
            Assert.AreEqual("AA", HandRanks.Order[0]);
            Assert.AreEqual("32o", HandRanks.Order[168]);
            Assert.AreEqual(1, HandRanks.Rank(Card.Parse("Ad"), Card.Parse("Ac")));
            Assert.AreEqual(100.0, HandRanks.Percentile(Card.Parse("3d"), Card.Parse("2c")), 1e-9);
            Assert.AreEqual("AKs", HandRanks.ClassOf(Card.Parse("Kh"), Card.Parse("Ah")));
        }

        [Test]
        public void PushFold_WiderInLaterPositions()
        {
            var order = new[] { TablePosition.UTG, TablePosition.MP, TablePosition.CO, TablePosition.BTN, TablePosition.SB };
            foreach (double bb in new[] { 3, 4, 5, 6, 7, 8.5, 10, 12, 15, 17, 20, 22.5, 25, 30, 35, 50 })
            for (int i = 1; i < order.Length; i++)
            {
                Assert.GreaterOrEqual(PushFoldTable.PushPercent(order[i], bb), PushFoldTable.PushPercent(order[i - 1], bb), order[i] + " push @" + bb);
                Assert.GreaterOrEqual(PushFoldTable.CallPercent(order[i], bb), PushFoldTable.CallPercent(order[i - 1], bb), "call vs " + order[i] + " @" + bb);
            }
        }

        [Test]
        public void PushFold_WiderWhenShorter()
        {
            foreach (TablePosition p in new[] { TablePosition.UTG, TablePosition.MP, TablePosition.CO, TablePosition.BTN, TablePosition.SB })
            {
                double prevPush = 101, prevCall = 101;
                for (double bb = 1; bb <= 45; bb += 0.5)
                {
                    double push = PushFoldTable.PushPercent(p, bb), call = PushFoldTable.CallPercent(p, bb);
                    Assert.LessOrEqual(push, prevPush, p + " push @" + bb);
                    Assert.LessOrEqual(call, prevCall, p + " call @" + bb);
                    prevPush = push;
                    prevCall = call;
                }
            }
        }

        [Test]
        public void Positions_NineHanded_And_HeadsUp()
        {
            var nine = new HandState(Enumerable.Repeat(1000L, 9).ToArray(), 0, 50, 100, new Xoshiro128(1));
            var p = Positions.Assign(nine);
            Assert.AreEqual(TablePosition.BTN, p[0]);
            Assert.AreEqual(TablePosition.SB, p[1]);
            Assert.AreEqual(TablePosition.BB, p[2]);
            Assert.AreEqual(TablePosition.UTG, p[3]);
            Assert.AreEqual(TablePosition.MP, p[6]);
            Assert.AreEqual(TablePosition.MP, p[7]);
            Assert.AreEqual(TablePosition.CO, p[8]);

            var hu = new HandState(new long[] { 1000, 0, 0, 1000, 0, 0, 0, 0, 0 }, 3, 50, 100, new Xoshiro128(1));
            var q = Positions.Assign(hu);
            Assert.AreEqual(TablePosition.SB, q[3]);
            Assert.AreEqual(TablePosition.BB, q[0]);
        }

        [Test]
        public void Bot_OpensAces_FoldsSevenDeuceUnderTheGun()
        {
            var t = new Tournament(TournamentConfig.For(TournamentKind.Main), new Xoshiro128(8), BotFactory.Instance, BotFactory.Instance.Create(PersonalityId.Pro));
            t.Step(); // deal hand 1
            var h = t.Hand;
            int s = h.ToAct;
            var pro = new Bot(Personality.For(PersonalityId.Pro));
            h.RigHole(s, "As Ah");
            Assert.AreEqual(ActionType.Raise, pro.Decide(t, s).Type, "50bb deep: open-raise");
            h.RigHole(s, "7d 2c");
            Assert.AreEqual(ActionType.Fold, pro.Decide(t, s).Type);
        }

        [Test]
        public void Bot_ShortStack_ShovesOrFolds()
        {
            var cfg = TournamentConfig.For(TournamentKind.Pub);
            cfg.StartingChips = 800; // 8bb
            var t = new Tournament(cfg, new Xoshiro128(9), BotFactory.Instance, BotFactory.Instance.Create(PersonalityId.Pro));
            t.Step();
            var h = t.Hand;
            int s = h.ToAct;
            var pro = new Bot(Personality.For(PersonalityId.Pro));
            h.RigHole(s, "Ks Kh");
            Assert.AreEqual(ActionType.AllIn, pro.Decide(t, s).Type);
        }

        [Test]
        public void Bots_AreDeterministic()
        {
            TournamentResult Run() => HoldemRunner.RunTournament(TournamentConfig.For(TournamentKind.Pub), new Xoshiro128(2024), new HeroBot());
            CollectionAssert.AreEqual(Run().Log, Run().Log);
        }

        sealed class HeroBot : ITournamentHooks
        {
            public IDecider Hero { get; } = BotFactory.Instance.Create(PersonalityId.Pro);
            public bool OfferReentry(long cost) => true;
        }
    }
}
