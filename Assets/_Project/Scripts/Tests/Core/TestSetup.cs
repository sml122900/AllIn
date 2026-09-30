using AllIn.Core;
using NUnit.Framework;

namespace AllIn.Core.Tests
{
    /// <summary>The rules now carry a real luck chance (2% coin / 3% hold'em). Every test that is not about luck pins the deterministic world.</summary>
    [SetUpFixture]
    public class NoLuckByDefault
    {
        [OneTimeSetUp]
        public void Zero() { Loop.DefaultLuckQCoin = 0; }
    }
}
