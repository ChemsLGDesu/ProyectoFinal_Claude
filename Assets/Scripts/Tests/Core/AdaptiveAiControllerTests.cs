using NUnit.Framework;
using TTTXO.Core;

namespace TTTXO.Core.Tests
{
    public class AdaptiveAiControllerTests
    {
        [Test]
        public void CurrentLevel_DefaultsTo50()
        {
            var controller = new AdaptiveAiController();
            Assert.AreEqual(50, controller.CurrentLevel);
        }

        [Test]
        public void RecordResult_FewerThan3Samples_DoesNotAdjustLevel()
        {
            var controller = new AdaptiveAiController();
            controller.RecordResult(humanWon: true, draw: false);
            controller.RecordResult(humanWon: true, draw: false);

            // Only 2 games recorded: below the 3-game minimum for the band rule, and below
            // the 3-game streak length too.
            Assert.AreEqual(50, controller.CurrentLevel);
        }

        [Test]
        public void RecordResult_ThreeConsecutiveWins_AppliesBandPlusStreakBonus()
        {
            var controller = new AdaptiveAiController();
            controller.RecordResult(humanWon: true, draw: false);
            controller.RecordResult(humanWon: true, draw: false);
            controller.RecordResult(humanWon: true, draw: false);

            // Window win rate = 1.0 (> 0.60) -> +10; 3-win streak -> +15 additional. 50+10+15=75.
            Assert.AreEqual(75, controller.CurrentLevel);
        }

        [Test]
        public void RecordResult_ThreeConsecutiveLosses_AppliesBandMinusStreakPenalty()
        {
            var controller = new AdaptiveAiController();
            controller.RecordResult(humanWon: false, draw: false);
            controller.RecordResult(humanWon: false, draw: false);
            controller.RecordResult(humanWon: false, draw: false);

            // Window win rate = 0.0 (< 0.40) -> -10; 3-loss streak -> -15 additional. 50-10-15=25.
            Assert.AreEqual(25, controller.CurrentLevel);
        }

        [Test]
        public void RecordResult_BandAdjustmentWithoutStreak_OnlyAppliesBandStep()
        {
            var controller = new AdaptiveAiController();
            // Win, win, draw: win rate = (1 + 1 + 0.5) / 3 = 0.833 > 0.60 -> +10.
            // The draw resets both streak counters, so no streak bonus applies.
            controller.RecordResult(humanWon: true, draw: false);
            controller.RecordResult(humanWon: true, draw: false);
            controller.RecordResult(humanWon: false, draw: true);

            Assert.AreEqual(60, controller.CurrentLevel);
        }

        [Test]
        public void RecordResult_WinRateInsideDeadBand_DoesNotAdjustLevel()
        {
            var controller = new AdaptiveAiController();
            // Win, loss, draw: win rate = (1 + 0 + 0.5) / 3 = 0.5, inside [0.40, 0.60] band.
            controller.RecordResult(humanWon: true, draw: false);
            controller.RecordResult(humanWon: false, draw: false);
            controller.RecordResult(humanWon: false, draw: true);

            Assert.AreEqual(50, controller.CurrentLevel);
        }

        [Test]
        public void RecordResult_RepeatedWinStreaks_ClampsAt100()
        {
            var controller = new AdaptiveAiController();
            for (int i = 0; i < 12; i++)
                controller.RecordResult(humanWon: true, draw: false);

            Assert.AreEqual(100, controller.CurrentLevel);
        }

        [Test]
        public void RecordResult_RepeatedLossStreaks_ClampsAt0()
        {
            var controller = new AdaptiveAiController();
            for (int i = 0; i < 12; i++)
                controller.RecordResult(humanWon: false, draw: false);

            Assert.AreEqual(0, controller.CurrentLevel);
        }

        [Test]
        public void GetParams_InterpolatesLinearlyAtLevel50()
        {
            var controller = new AdaptiveAiController(50);

            var params3X3 = controller.GetParams(3);
            Assert.AreEqual(0.85, params3X3.WinProbability, 1e-9);
            Assert.AreEqual(0.75, params3X3.BlockProbability, 1e-9);
            Assert.AreEqual(0.20, params3X3.BlunderChance, 1e-9);
            Assert.Greater(params3X3.SearchDepth, 0); // L=50 >= 35 -> full search on 3x3.

            var params6X6 = controller.GetParams(6);
            Assert.AreEqual(2, params6X6.SearchDepth); // 35 <= L=50 < 70 -> depth 2.
            Assert.AreEqual(0.20, params6X6.BlunderChance, 1e-9);
        }

        [Test]
        public void GetParams_BelowSearchThreshold_ForcesNoSearchAndNoBlunder()
        {
            var controller = new AdaptiveAiController(20); // L < 35 on all board sizes.

            var params3X3 = controller.GetParams(3);
            Assert.AreEqual(0, params3X3.SearchDepth);
            Assert.AreEqual(0.0, params3X3.BlunderChance);

            var params6X6 = controller.GetParams(6);
            Assert.AreEqual(0, params6X6.SearchDepth);
            Assert.AreEqual(0.0, params6X6.BlunderChance);
        }

        [Test]
        public void GetParams_HighLevelOn6X6_UsesDepth4()
        {
            var controller = new AdaptiveAiController(80); // L >= 70
            var aiParams = controller.GetParams(6);
            Assert.AreEqual(4, aiParams.SearchDepth);
        }
    }
}
