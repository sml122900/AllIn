using System.Linq;
using AllIn.Core;
using AllIn.Holdem;
using NUnit.Framework;

namespace AllIn.Holdem.Tests
{
    public class BettingTests
    {
        /// <summary>Stacks by seat (0 = empty). Blinds 50/100.</summary>
        static HandState Hand(int button, params long[] stacks)
        {
            var full = new long[9];
            for (int i = 0; i < stacks.Length; i++) full[i] = stacks[i];
            return new HandState(full, button, 50, 100, new Xoshiro128(7));
        }

        [Test]
        public void Blinds_AndFirstToAct_ThreeHanded()
        {
            var h = Hand(0, 1000, 1000, 1000);
            Assert.AreEqual(1, h.SmallBlindSeat);
            Assert.AreEqual(2, h.BigBlindSeat);
            Assert.AreEqual(0, h.ToAct, "UTG (here the button) acts first preflop");
            Assert.AreEqual(150, h.PotTotal);
        }

        [Test]
        public void HeadsUp_ButtonIsSmallBlind_ActsFirstPreflop_LastPostflop()
        {
            var h = Hand(3, 0, 0, 0, 1000, 1000);
            Assert.AreEqual(3, h.SmallBlindSeat);
            Assert.AreEqual(4, h.BigBlindSeat);
            Assert.AreEqual(3, h.ToAct);
            h.Apply(PlayerAction.Call);
            Assert.AreEqual(4, h.ToAct, "BB gets the option");
            h.Apply(PlayerAction.Check);
            Assert.AreEqual(Street.Flop, h.Street);
            Assert.AreEqual(4, h.ToAct, "BB (non-button) acts first after the flop");
        }

        [Test]
        public void MinRaise_IsTheLastRaiseSize()
        {
            var h = Hand(0, 5000, 5000, 5000, 5000);
            // seat 3 is UTG
            Assert.AreEqual(3, h.ToAct);
            Assert.AreEqual(200, h.LegalFor(3).MinRaiseTo, "min open = 2 × BB");
            h.Apply(PlayerAction.RaiseTo(350)); // raise of 250
            Assert.AreEqual(600, h.LegalFor(0).MinRaiseTo, "re-raise must be at least another 250");
            Assert.Throws<System.InvalidOperationException>(() => h.Apply(PlayerAction.RaiseTo(599)));
            h.Apply(PlayerAction.RaiseTo(600));
            Assert.AreEqual(850, h.LegalFor(1).MinRaiseTo);
        }

        [Test]
        public void ShortAllIn_DoesNotReopenBetting_ForPlayersWhoAlreadyActed()
        {
            // seat 3 UTG 5000, seat 0 (BTN) only 450 behind
            var h = Hand(0, 450, 5000, 5000, 5000);
            h.Apply(PlayerAction.RaiseTo(300));           // seat 3 raises 200 more
            Assert.AreEqual(0, h.ToAct);
            h.Apply(PlayerAction.AllIn);                   // 450: a raise of 150 < 200 → short
            Assert.AreEqual(1, h.ToAct);
            Assert.IsTrue(h.LegalFor(1).CanRaise, "SB hasn't acted: may raise");
            Assert.AreEqual(650, h.LegalFor(1).MinRaiseTo, "450 + the last full raise (200)");
            h.Apply(PlayerAction.Fold);
            h.Apply(PlayerAction.Fold);                    // BB
            Assert.AreEqual(3, h.ToAct);
            var l = h.LegalFor(3);
            Assert.IsFalse(l.CanRaise, "the original raiser may only call or fold");
            Assert.AreEqual(150, l.ToCall);
            Assert.AreEqual(PlayerAction.Call.Type, h.Normalize(3, PlayerAction.RaiseTo(2000)).Type);
        }

        [Test]
        public void FullRaiseAllIn_DoesReopen()
        {
            var h = Hand(0, 600, 5000, 5000, 5000);
            h.Apply(PlayerAction.RaiseTo(300));  // +200
            h.Apply(PlayerAction.AllIn);         // 600: +300 ≥ 200 → full raise
            h.Apply(PlayerAction.Fold);
            h.Apply(PlayerAction.Fold);
            Assert.IsTrue(h.LegalFor(3).CanRaise);
        }

        [Test]
        public void EveryoneFolds_ToTheBigBlind_WhoWinsTheBlinds()
        {
            var h = Hand(0, 1000, 1000, 1000);
            h.Apply(PlayerAction.Fold);
            h.Apply(PlayerAction.Fold);
            Assert.IsTrue(h.IsOver);
            Assert.IsFalse(h.WentToShowdown);
            Assert.AreEqual(1050, h.Stack[2]);
            Assert.AreEqual(950, h.Stack[1]);
        }

        [Test]
        public void UncalledBet_IsReturned()
        {
            var h = Hand(0, 1000, 1000, 1000);
            h.Apply(PlayerAction.RaiseTo(800));
            h.Apply(PlayerAction.Fold);
            h.Apply(PlayerAction.Fold);
            Assert.AreEqual(1150, h.Stack[0], "gets the 800 back plus the blinds");
        }

        [Test]
        public void SidePots_ThreeAllInsOfDifferentSizes()
        {
            // seat 0: 100 (best hand), seat 1: 300 (second), seat 2: 500 (worst)
            var h = Hand(2, 100, 300, 500);
            h.RigHole(0, "As Ad");
            h.RigHole(1, "Ks Kd");
            h.RigHole(2, "2c 7h");
            h.RigBoard("3c 8d 9h Jc 4s");
            h.Apply(PlayerAction.AllIn); // seat 2 (button acts first 3-handed): 500
            h.Apply(PlayerAction.AllIn); // seat 0: 100
            h.Apply(PlayerAction.AllIn); // seat 1: 300
            Assert.IsTrue(h.IsOver);
            Assert.AreEqual(3, h.Pots.Count);
            Assert.AreEqual(300, h.Pots[0].Amount); CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, h.Pots[0].Eligible);
            Assert.AreEqual(400, h.Pots[1].Amount); CollectionAssert.AreEquivalent(new[] { 1, 2 }, h.Pots[1].Eligible);
            Assert.AreEqual(200, h.Pots[2].Amount); CollectionAssert.AreEquivalent(new[] { 2 }, h.Pots[2].Eligible);
            Assert.AreEqual(300, h.Stack[0]);
            Assert.AreEqual(400, h.Stack[1]);
            Assert.AreEqual(200, h.Stack[2], "uncovered part comes back");
            Assert.AreEqual(900, h.Stack.Sum());
        }

        [Test]
        public void SidePots_FourPlayers_ShortStackWinsMain_BigStackWinsSides()
        {
            var h = Hand(3, 200, 400, 800, 1000);
            h.RigHole(0, "As Ad"); // 200 – best
            h.RigHole(1, "2c 7h"); // 400 – worst
            h.RigHole(2, "Qs Qd"); // 800 – second
            h.RigHole(3, "Ks Kd"); // 1000 – third... Kings beat Queens
            h.RigBoard("3c 8d 9h Jc 4s");
            // button 3; SB 0, BB 1, UTG = 2
            h.Apply(PlayerAction.AllIn); // seat 2: 800
            h.Apply(PlayerAction.Call);  // seat 3 calls 800
            h.Apply(PlayerAction.AllIn); // seat 0: 200
            h.Apply(PlayerAction.AllIn); // seat 1: 400
            Assert.IsTrue(h.IsOver);
            // main 800 (4×200) → AA; side 600 (3×200) → KK; side 800 (2×400) → KK
            Assert.AreEqual(800, h.Stack[0]);
            Assert.AreEqual(0, h.Stack[1]);
            Assert.AreEqual(0, h.Stack[2]);
            Assert.AreEqual(200 + 600 + 800, h.Stack[3]);
            Assert.AreEqual(2400, h.Stack.Sum());
        }

        [Test]
        public void SplitPot_OddChip_GoesToTheFirstWinnerLeftOfTheButton()
        {
            // SB is all-in for 33 → main pot 99 splits between BTN and BB; the odd chip goes to BB (closer to the button's left)
            var h = Hand(0, 1000, 33, 1000);
            h.RigHole(0, "Td 3c");
            h.RigHole(1, "2c 5h");
            h.RigHole(2, "Th 4c");
            h.RigBoard("Ah Ks Qd Jc 2d");
            h.Apply(PlayerAction.Call);  // BTN
            h.Apply(PlayerAction.Check); // BB
            for (int st = 0; st < 3; st++) { h.Apply(PlayerAction.Check); h.Apply(PlayerAction.Check); }
            Assert.IsTrue(h.WentToShowdown);
            Assert.AreEqual(1000 - 100 + 49 + 67, h.Stack[0]);
            Assert.AreEqual(1000 - 100 + 50 + 67, h.Stack[2]);
            Assert.AreEqual(0, h.Stack[1]);
        }

        [Test]
        public void SplitPot_EvenThreeWay_UncalledExtraReturned()
        {
            var h = Hand(0, 1000, 1001, 1000);
            h.RigHole(0, "2c 3d");
            h.RigHole(1, "2d 3c");
            h.RigHole(2, "4c 5h");
            h.RigBoard("As Ks Qs Js Ts");
            h.Apply(PlayerAction.AllIn); // BTN 1000
            h.Apply(PlayerAction.AllIn); // SB 1001
            h.Apply(PlayerAction.Call);  // BB 1000
            // pot of 3000 splits 1000 each; SB's extra 1 comes back uncalled
            Assert.AreEqual(1000, h.Stack[0]);
            Assert.AreEqual(1001, h.Stack[1]);
            Assert.AreEqual(1000, h.Stack[2]);
        }

        [Test]
        public void ShowOrder_RiverAggressorShowsFirst()
        {
            var h = Hand(0, 5000, 5000, 5000);
            h.RigBoard("2c 7d 9h Ts 3s");
            h.Apply(PlayerAction.Call); h.Apply(PlayerAction.Call); h.Apply(PlayerAction.Check); // pre
            for (int st = 0; st < 2; st++) { h.Apply(PlayerAction.Check); h.Apply(PlayerAction.Check); h.Apply(PlayerAction.Check); }
            Assert.AreEqual(Street.River, h.Street);
            Assert.AreEqual(1, h.ToAct);
            h.Apply(PlayerAction.Check);            // SB
            h.Apply(PlayerAction.BetTo(300));       // BB bets
            h.Apply(PlayerAction.Call);             // BTN
            h.Apply(PlayerAction.Call);             // SB
            Assert.IsTrue(h.WentToShowdown);
            CollectionAssert.AreEqual(new[] { 2, 0, 1 }, h.ShowOrder);
        }

        [Test]
        public void ShowOrder_NoRiverBet_StartsLeftOfButton()
        {
            var h = Hand(0, 5000, 5000, 5000);
            h.Apply(PlayerAction.Call); h.Apply(PlayerAction.Call); h.Apply(PlayerAction.Check);
            for (int st = 0; st < 3; st++) { h.Apply(PlayerAction.Check); h.Apply(PlayerAction.Check); h.Apply(PlayerAction.Check); }
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, h.ShowOrder);
        }

        [Test]
        public void AllInPreflop_RunsOutTheBoard_AndMarksTheRunout()
        {
            var h = Hand(0, 1000, 1000);
            h.Apply(PlayerAction.AllIn);
            h.Apply(PlayerAction.Call);
            Assert.IsTrue(h.IsOver);
            Assert.AreEqual(5, h.BoardCount);
            Assert.AreEqual(0, h.RunoutFromBoardCount);
            Assert.AreEqual(2000, h.Stack.Sum());
        }

        [Test]
        public void ChipsAreConserved_OverManyRandomHands()
        {
            var rng = new Xoshiro128(5);
            for (int n = 0; n < 2000; n++)
            {
                var stacks = new long[9];
                for (int s = 0; s < 9; s++) stacks[s] = rng.NextInt(3) == 0 ? 0 : 50 + rng.NextInt(3000);
                if (stacks.Count(x => x > 0) < 2) continue;
                int button = System.Array.FindIndex(stacks, x => x > 0);
                var h = new HandState(stacks, button, 50, 100, new Xoshiro128((ulong)n));
                int guard = 0;
                while (!h.IsOver && guard++ < 200)
                {
                    var l = h.LegalFor(h.ToAct);
                    int pick = rng.NextInt(6);
                    var want = pick == 0 ? PlayerAction.Fold : pick == 1 ? PlayerAction.Call : pick == 2 ? PlayerAction.AllIn
                             : PlayerAction.RaiseTo(l.MinRaiseTo + rng.NextInt(300));
                    h.Apply(h.Normalize(h.ToAct, want));
                }
                Assert.IsTrue(h.IsOver, "hand " + n + " never ended");
                Assert.AreEqual(stacks.Sum(), h.Stack.Sum(), "hand " + n);
            }
        }
    }
}
