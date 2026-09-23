using System;
using System.Linq;
using AllIn.Core;
using NUnit.Framework;

namespace AllIn.Core.Tests
{
    public class RngTests
    {
        [Test]
        public void SameSeed_ProducesSameSequence()
        {
            var a = new Xoshiro128(42);
            var b = new Xoshiro128(42);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(a.NextUInt(), b.NextUInt());
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentSequences()
        {
            var a = new Xoshiro128(1);
            var b = new Xoshiro128(2);
            Assert.AreNotEqual(Enumerable.Range(0, 8).Select(_ => a.NextUInt()).ToArray(),
                               Enumerable.Range(0, 8).Select(_ => b.NextUInt()).ToArray());
        }

        [Test]
        public void KnownSeed_MatchesPinnedOutput_SoHarnessAndEditorAgree()
        {
            // Pinned values: if this ever changes, every saved seed and sim report changes meaning.
            var r = new Xoshiro128(12345);
            uint[] got = { r.NextUInt(), r.NextUInt(), r.NextUInt() };
            var again = new Xoshiro128(12345);
            Assert.AreEqual(got, new[] { again.NextUInt(), again.NextUInt(), again.NextUInt() });
            Assert.AreEqual(Pinned.Seed12345, got, "got " + string.Join(",", got));
        }

        [Test]
        public void StateRoundTrip_ContinuesTheSameStream()
        {
            var a = new Xoshiro128(7);
            for (int i = 0; i < 100; i++) a.NextUInt();
            var b = new Xoshiro128(a.GetState());
            for (int i = 0; i < 100; i++) Assert.AreEqual(a.NextUInt(), b.NextUInt());
        }

        [Test]
        public void NextInt_StaysInRange_AndCoversAllValues()
        {
            var r = new Xoshiro128(3);
            var seen = new int[45];
            for (int i = 0; i < 45000; i++) seen[r.NextInt(45)]++;
            Assert.That(seen.Min(), Is.GreaterThan(800));
            Assert.That(seen.Max(), Is.LessThan(1200));
        }

        [Test]
        public void NextDouble_IsInUnitInterval()
        {
            var r = new Xoshiro128(4);
            for (int i = 0; i < 100000; i++)
            {
                double d = r.NextDouble();
                Assert.That(d >= 0.0 && d < 1.0);
            }
        }

        [Test]
        public void StudentT3_QuantilesMatchTheory()
        {
            // t(3) quantiles: q0.5 = 0, q0.75 = 0.7649, q0.95 = 2.3534, q0.99 = 4.5407. Var = 3 (fat tails vs normal's 1).
            var r = new Xoshiro128(2024);
            const int n = 200000;
            var xs = new double[n];
            for (int i = 0; i < n; i++) xs[i] = r.NextStudentT3();
            Array.Sort(xs);
            double Q(double p) => xs[(int)(p * (n - 1))];
            Assert.That(Q(0.5), Is.InRange(-0.02, 0.02));
            Assert.That(Q(0.75), Is.InRange(0.74, 0.79));
            Assert.That(Q(0.25), Is.InRange(-0.79, -0.74));
            Assert.That(Q(0.95), Is.InRange(2.25, 2.46));
            Assert.That(Q(0.99), Is.InRange(4.2, 4.9));
            Assert.That(Q(0.01), Is.InRange(-4.9, -4.2));
        }

        [Test]
        public void Gaussian_HasUnitVariance()
        {
            var r = new Xoshiro128(99);
            const int n = 100000;
            double sum = 0, sq = 0;
            for (int i = 0; i < n; i++) { double g = r.NextGaussian(); sum += g; sq += g * g; }
            double mean = sum / n;
            Assert.That(mean, Is.InRange(-0.02, 0.02));
            Assert.That(sq / n - mean * mean, Is.InRange(0.97, 1.03));
        }

        static class Pinned
        {
            public static readonly uint[] Seed12345 = { 2314518269u, 2498321016u, 2055377852u };
        }
    }
}
