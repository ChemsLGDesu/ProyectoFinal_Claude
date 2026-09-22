using NUnit.Framework;
using TTTXO.Core;

namespace TTTXO.Core.Tests
{
    public class RewardCalculatorTests
    {
        // (boardSize, difficulty, expectedWin, expectedDraw, expectedLoss) per design-doc.md
        // section 4: base reward by size/result, floored after the difficulty multiplier.
        [TestCase(3, AiDifficulty.Easy, 5, 2, 1)]
        [TestCase(3, AiDifficulty.Medium, 10, 5, 2)]
        [TestCase(3, AiDifficulty.Hard, 15, 7, 3)]
        [TestCase(3, AiDifficulty.Adaptive, 10, 5, 2)]
        [TestCase(6, AiDifficulty.Easy, 10, 5, 2)]
        [TestCase(6, AiDifficulty.Medium, 20, 10, 4)]
        [TestCase(6, AiDifficulty.Hard, 30, 15, 6)]
        [TestCase(6, AiDifficulty.Adaptive, 20, 10, 4)]
        [TestCase(9, AiDifficulty.Easy, 15, 7, 3)]
        [TestCase(9, AiDifficulty.Medium, 30, 15, 6)]
        [TestCase(9, AiDifficulty.Hard, 45, 22, 9)]
        [TestCase(9, AiDifficulty.Adaptive, 30, 15, 6)]
        [TestCase(11, AiDifficulty.Easy, 20, 10, 4)]
        [TestCase(11, AiDifficulty.Medium, 40, 20, 8)]
        [TestCase(11, AiDifficulty.Hard, 60, 30, 12)]
        [TestCase(11, AiDifficulty.Adaptive, 40, 20, 8)]
        public void SoftCurrencyFor_SinglePlayer_MatchesDesignDocTable(
            int boardSize, AiDifficulty difficulty, int expectedWin, int expectedDraw, int expectedLoss)
        {
            int win = RewardCalculator.SoftCurrencyFor(
                GameMode.SinglePlayer, difficulty, boardSize, MatchStatus.XWon, CellOwner.X);
            int draw = RewardCalculator.SoftCurrencyFor(
                GameMode.SinglePlayer, difficulty, boardSize, MatchStatus.Draw, CellOwner.X);
            int loss = RewardCalculator.SoftCurrencyFor(
                GameMode.SinglePlayer, difficulty, boardSize, MatchStatus.OWon, CellOwner.X);

            Assert.AreEqual(expectedWin, win, "win reward");
            Assert.AreEqual(expectedDraw, draw, "draw reward");
            Assert.AreEqual(expectedLoss, loss, "loss reward");
        }

        [Test]
        public void SoftCurrencyFor_SinglePlayer_LocalPlayerIdentityDeterminesWinVsLoss()
        {
            // Same match result (O won); reward differs depending on which symbol the local
            // player controlled.
            int rewardForWinner = RewardCalculator.SoftCurrencyFor(
                GameMode.SinglePlayer, AiDifficulty.Medium, 3, MatchStatus.OWon, CellOwner.O);
            int rewardForLoser = RewardCalculator.SoftCurrencyFor(
                GameMode.SinglePlayer, AiDifficulty.Medium, 3, MatchStatus.OWon, CellOwner.X);

            Assert.AreEqual(10, rewardForWinner);
            Assert.AreEqual(2, rewardForLoser);
        }

        [TestCase(MatchStatus.XWon)]
        [TestCase(MatchStatus.OWon)]
        [TestCase(MatchStatus.Draw)]
        public void SoftCurrencyFor_LocalMultiplayer_AlwaysPaysFlat2(MatchStatus status)
        {
            int rewardX = RewardCalculator.SoftCurrencyFor(
                GameMode.LocalMultiplayer, null, 3, status, CellOwner.X);
            int rewardO = RewardCalculator.SoftCurrencyFor(
                GameMode.LocalMultiplayer, null, 3, status, CellOwner.O);

            Assert.AreEqual(2, rewardX);
            Assert.AreEqual(2, rewardO);
        }

        [Test]
        public void SoftCurrencyFor_UnfinishedMatch_PaysZero()
        {
            int reward = RewardCalculator.SoftCurrencyFor(
                GameMode.SinglePlayer, AiDifficulty.Hard, 3, MatchStatus.InProgress, CellOwner.X);

            Assert.AreEqual(0, reward);
        }

        [Test]
        public void SoftCurrencyFor_SinglePlayerWithoutDifficulty_Throws()
        {
            Assert.Throws<System.ArgumentException>(() =>
                RewardCalculator.SoftCurrencyFor(
                    GameMode.SinglePlayer, null, 3, MatchStatus.XWon, CellOwner.X));
        }
    }
}
