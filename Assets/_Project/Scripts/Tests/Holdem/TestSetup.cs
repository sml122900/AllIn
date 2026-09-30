using AllIn.Core;
using NUnit.Framework;

namespace AllIn.Holdem.Tests
{
    [SetUpFixture]
    public class NoLuckByDefault
    {
        [OneTimeSetUp]
        public void Zero() { Loop.DefaultLuckQCoin = 0; LadderConfig.DefaultLuckQ = 0; }
    }
}
