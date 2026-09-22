using NUnit.Framework;
using TTTXO.Game.Services;

namespace TTTXO.Game.Tests
{
    /// <summary>
    /// The offline fallback for a Ranked ticket's MMR attribute. It only ever stores a value the
    /// server itself reported, and it is only ever read when the live <c>GetRankedProfile</c> call
    /// fails - so the two things worth pinning are that an unseen board returns the caller's
    /// fallback verbatim (a brand-new player is not "0 MMR") and that board sizes stay isolated,
    /// since MMR is tracked per board.
    /// </summary>
    [TestFixture]
    public class RankedProfileCacheTests
    {
        private const int InitialMmr = 1000;

        private PlayerPrefsSandbox _prefs;

        [SetUp]
        public void SetUp()
        {
            _prefs = PlayerPrefsSandbox.CaptureAndClear();
        }

        [TearDown]
        public void TearDown()
        {
            _prefs.Restore();
        }

        [Test]
        public void GetKnownMmr_ReturnsTheFallback_WhenTheServerNeverReportedOne()
        {
            Assert.AreEqual(InitialMmr, RankedProfileCache.GetKnownMmr(3, InitialMmr));
        }

        [Test]
        public void GetKnownMmr_ReturnsTheLastValueTheServerReported()
        {
            RankedProfileCache.SetKnownMmr(3, 1240);

            Assert.AreEqual(1240, RankedProfileCache.GetKnownMmr(3, InitialMmr));
        }

        [Test]
        public void KnownMmr_IsTrackedPerBoardSize()
        {
            RankedProfileCache.SetKnownMmr(3, 1240);

            Assert.AreEqual(1240, RankedProfileCache.GetKnownMmr(3, InitialMmr));
            Assert.AreEqual(InitialMmr, RankedProfileCache.GetKnownMmr(6, InitialMmr), "6x6 has its own MMR");
        }

        [Test]
        public void SetKnownMmr_OverwritesTheEarlierValue()
        {
            RankedProfileCache.SetKnownMmr(6, 1100);
            RankedProfileCache.SetKnownMmr(6, 980);

            Assert.AreEqual(980, RankedProfileCache.GetKnownMmr(6, InitialMmr), "decay and soft reset move MMR down too");
        }
    }
}
