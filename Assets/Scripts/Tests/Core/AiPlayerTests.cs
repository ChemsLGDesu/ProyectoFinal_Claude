using System;
using System.Collections.Generic;
using NUnit.Framework;
using TTTXO.Core;

namespace TTTXO.Core.Tests
{
    public class AiPlayerTests
    {
        [Test]
        public void ChooseMove_HardOn3X3_NeverLosesAgainstRandomPlayerOver50Games()
        {
            var ai = new AiPlayer(randomSeed: 1234);
            var opponentRng = new Random(5678);
            var hardParams = AiParams.ForDifficulty(AiDifficulty.Hard, boardSize: 3);

            for (int game = 0; game < 50; game++)
            {
                // Alternate who starts so the AI is tested both as first and second mover.
                CellOwner aiSymbol = game % 2 == 0 ? CellOwner.X : CellOwner.O;
                CellOwner firstPlayer = CellOwner.X;
                var match = new MatchController(BoardConfig.ForSize(3), firstPlayer);

                while (match.Status == MatchStatus.InProgress)
                {
                    (int Row, int Col) move = match.CurrentPlayer == aiSymbol
                        ? ai.ChooseMove(match, hardParams)
                        : PickRandomMove(match, opponentRng);

                    var result = match.PlayMove(move.Row, move.Col);
                    Assert.IsTrue(result.IsValid, $"Game {game}: AI or random player made an invalid move ({move.Row},{move.Col}): {result.InvalidReason}");
                }

                bool aiLost = (aiSymbol == CellOwner.X && match.Status == MatchStatus.OWon)
                    || (aiSymbol == CellOwner.O && match.Status == MatchStatus.XWon);

                Assert.IsFalse(aiLost, $"Game {game}: Hard AI (as {aiSymbol}) lost, which should be impossible on 3x3.");
            }
        }

        [TestCase(3, AiDifficulty.Easy)]
        [TestCase(3, AiDifficulty.Medium)]
        [TestCase(3, AiDifficulty.Hard)]
        [TestCase(6, AiDifficulty.Easy)]
        [TestCase(6, AiDifficulty.Medium)]
        [TestCase(6, AiDifficulty.Hard)]
        [TestCase(9, AiDifficulty.Medium)]
        [TestCase(9, AiDifficulty.Hard)]
        [TestCase(11, AiDifficulty.Medium)]
        [TestCase(11, AiDifficulty.Hard)]
        public void ChooseMove_AlwaysReturnsAnEmptyCellInRange(int boardSize, AiDifficulty difficulty)
        {
            var ai = new AiPlayer(randomSeed: 42);
            var aiParams = AiParams.ForDifficulty(difficulty, boardSize);
            var match = new MatchController(BoardConfig.ForSize(boardSize), CellOwner.X);

            // A handful of self-played plies is enough to validate move legality without
            // paying for a full game on the larger boards.
            int plies = Math.Min(8, boardSize * boardSize);
            for (int i = 0; i < plies && match.Status == MatchStatus.InProgress; i++)
            {
                var move = ai.ChooseMove(match, aiParams);

                Assert.GreaterOrEqual(move.Row, 0);
                Assert.Less(move.Row, boardSize);
                Assert.GreaterOrEqual(move.Col, 0);
                Assert.Less(move.Col, boardSize);
                Assert.AreEqual(CellOwner.None, match.Board.GetCell(move.Row, move.Col));

                var result = match.PlayMove(move.Row, move.Col);
                Assert.IsTrue(result.IsValid, result.InvalidReason);
            }
        }

        [Test]
        public void ChooseMove_ImmediateWinAvailableAndPWinIsOne_TakesIt()
        {
            var match = new MatchController(BoardConfig.ForSize(3), CellOwner.X);
            match.PlayMove(0, 0); // X
            match.PlayMove(1, 0); // O
            match.PlayMove(0, 1); // X
            match.PlayMove(1, 1); // O
            // X to move, can win at (0,2).

            var ai = new AiPlayer(randomSeed: 1);
            var aiParams = new AiParams(winProbability: 1.0, blockProbability: 1.0, blunderChance: 0.0, searchDepth: 0);

            var move = ai.ChooseMove(match, aiParams);

            Assert.AreEqual((0, 2), move);
        }

        [Test]
        public void ChooseMove_OpponentThreatensWinAndPBlockIsOne_Blocks()
        {
            var match = new MatchController(BoardConfig.ForSize(3), CellOwner.X);
            match.PlayMove(2, 2); // X
            match.PlayMove(0, 0); // O
            match.PlayMove(2, 1); // X
            match.PlayMove(0, 1); // O
            // X to move. O threatens to win at (0,2). pWin=0 forces the AI to ignore any
            // win of its own so this isolates the block step.

            var ai = new AiPlayer(randomSeed: 1);
            var aiParams = new AiParams(winProbability: 0.0, blockProbability: 1.0, blunderChance: 0.0, searchDepth: 0);

            var move = ai.ChooseMove(match, aiParams);

            Assert.AreEqual((0, 2), move);
        }

        private static (int Row, int Col) PickRandomMove(MatchController match, Random rng)
        {
            var empties = new List<(int Row, int Col)>();
            for (int r = 0; r < match.Board.Size; r++)
            {
                for (int c = 0; c < match.Board.Size; c++)
                {
                    if (match.Board.GetCell(r, c) == CellOwner.None)
                        empties.Add((r, c));
                }
            }

            return empties[rng.Next(empties.Count)];
        }
    }
}
