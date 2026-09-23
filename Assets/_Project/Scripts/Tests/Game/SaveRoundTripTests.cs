using AllIn.Core;
using AllIn.Core.Actions;
using AllIn.Game;
using NUnit.Framework;

namespace AllIn.Game.Tests
{
    public class SaveRoundTripTests
    {
        static Loop PlayABit(long seed)
        {
            var loop = Loop.NewRun(seed);
            loop.Do(LottoBuyAction.Instance);
            loop.Do(new CoinOpenAction(Direction.Short, 25, 3_000_000));
            for (int i = 0; i < 13; i++) loop.Pass();
            return loop;
        }

        [Test]
        public void SaveThenLoad_JsonRestoresIdenticalState()
        {
            var loop = PlayABit(4242);
            string json = SaveIO.ToJson(loop.Snapshot());
            var restored = SaveIO.FromJson(json);
            Assert.AreEqual(json, SaveIO.ToJson(restored));
            Assert.AreEqual(loop.Data.price, restored.price, "doubles survive JSON bit-exact");
            Assert.AreEqual(loop.Data.hasPosition, restored.hasPosition);
            Assert.AreEqual(loop.Data.tickets.Count, restored.tickets.Count);
        }

        [Test]
        public void RestoredRun_ContinuesExactlyLikeTheOriginal()
        {
            var a = PlayABit(99);
            var b = Loop.FromSave(SaveIO.FromJson(SaveIO.ToJson(a.Snapshot())));
            for (int i = 0; i < 60 && !a.Finished; i++)
            {
                if (a.Data.slot == Slot.Evening) { a.Do(WorkRestaurantAction.Instance); b.Do(WorkRestaurantAction.Instance); }
                else { a.Do(LottoBuyAction.Instance); b.Do(LottoBuyAction.Instance); a.Pass(); b.Pass(); }
            }
            Assert.AreEqual(SaveIO.ToJson(a.Snapshot()), SaveIO.ToJson(b.Snapshot()));
        }
    }
}
