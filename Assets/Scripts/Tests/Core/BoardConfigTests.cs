using System;
using NUnit.Framework;
using TTTXO.Core;

namespace TTTXO.Core.Tests
{
    public class BoardConfigTests
    {
        [TestCase(3, 3)]
        [TestCase(6, 4)]
        [TestCase(9, 5)]
        [TestCase(11, 5)]
        public void ForSize_KnownSize_ReturnsExpectedWinLength(int size, int expectedWinLength)
        {
            var config = BoardConfig.ForSize(size);

            Assert.AreEqual(size, config.Size);
            Assert.AreEqual(expectedWinLength, config.WinLength);
        }

        [TestCase(0)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(7)]
        [TestCase(-3)]
        public void ForSize_UnknownSize_ThrowsArgumentException(int size)
        {
            Assert.Throws<ArgumentException>(() => BoardConfig.ForSize(size));
        }

        [Test]
        public void All_ContainsExactlyTheFourMilestone1Sizes()
        {
            var sizes = new System.Collections.Generic.List<int>();
            foreach (var config in BoardConfig.All)
                sizes.Add(config.Size);

            CollectionAssert.AreEquivalent(new[] { 3, 6, 9, 11 }, sizes);
        }
    }
}
