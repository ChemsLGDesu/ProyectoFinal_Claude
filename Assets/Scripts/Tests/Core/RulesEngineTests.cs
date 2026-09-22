using System.Collections.Generic;
using NUnit.Framework;
using TTTXO.Core;

namespace TTTXO.Core.Tests
{
    /// <summary>
    /// Win-detection tests. BoardState has no public mutator, so boards are built through
    /// legal MatchController.PlayMove sequences (the same path production code uses) and
    /// then inspected via RulesEngine / MoveResult.
    /// </summary>
    public class RulesEngineTests
    {
        private static MatchController NewMatch(int size) =>
            new MatchController(BoardConfig.ForSize(size), CellOwner.X);

        [Test]
        public void FindWinningLine_HorizontalOn3X3_DetectsWin()
        {
            var match = NewMatch(3);
            PlayAll(match, (0, 0), (1, 0), (0, 1), (1, 1), (0, 2)); // X: row 0

            Assert.AreEqual(MatchStatus.XWon, match.Status);
            var line = RulesEngine.FindWinningLine(match.Board, 0, 2, match.Config.WinLength);
            CollectionAssert.AreEquivalent(new[] { (0, 0), (0, 1), (0, 2) }, line);
        }

        [Test]
        public void FindWinningLine_VerticalOn3X3_DetectsWin()
        {
            var match = NewMatch(3);
            PlayAll(match, (0, 0), (1, 1), (1, 0), (1, 2), (2, 0)); // X: col 0

            Assert.AreEqual(MatchStatus.XWon, match.Status);
            var line = RulesEngine.FindWinningLine(match.Board, 2, 0, match.Config.WinLength);
            CollectionAssert.AreEquivalent(new[] { (0, 0), (1, 0), (2, 0) }, line);
        }

        [Test]
        public void FindWinningLine_MainDiagonalOn3X3_DetectsWin()
        {
            var match = NewMatch(3);
            PlayAll(match, (0, 0), (0, 1), (1, 1), (0, 2), (2, 2)); // X: (0,0)-(1,1)-(2,2)

            Assert.AreEqual(MatchStatus.XWon, match.Status);
            var line = RulesEngine.FindWinningLine(match.Board, 2, 2, match.Config.WinLength);
            CollectionAssert.AreEquivalent(new[] { (0, 0), (1, 1), (2, 2) }, line);
        }

        [Test]
        public void FindWinningLine_AntiDiagonalOn3X3_DetectsWin()
        {
            var match = NewMatch(3);
            PlayAll(match, (0, 2), (0, 0), (1, 1), (0, 1), (2, 0)); // X: (0,2)-(1,1)-(2,0)

            Assert.AreEqual(MatchStatus.XWon, match.Status);
            var line = RulesEngine.FindWinningLine(match.Board, 2, 0, match.Config.WinLength);
            CollectionAssert.AreEquivalent(new[] { (0, 2), (1, 1), (2, 0) }, line);
        }

        [Test]
        public void FindWinningLine_HorizontalOn6X6_DetectsWin()
        {
            var match = NewMatch(6);
            PlayAll(match,
                (0, 0), (1, 0), (0, 1), (1, 1), (0, 2), (1, 2), (0, 3)); // X: row 0, cols 0-3 (k=4)

            Assert.AreEqual(MatchStatus.XWon, match.Status);
            var line = RulesEngine.FindWinningLine(match.Board, 0, 3, match.Config.WinLength);
            CollectionAssert.AreEquivalent(new[] { (0, 0), (0, 1), (0, 2), (0, 3) }, line);
        }

        [Test]
        public void FindWinningLine_VerticalOn6X6_DetectsWin()
        {
            var match = NewMatch(6);
            PlayAll(match,
                (0, 0), (1, 1), (1, 0), (1, 2), (2, 0), (1, 3), (3, 0)); // X: col 0, rows 0-3

            Assert.AreEqual(MatchStatus.XWon, match.Status);
            var line = RulesEngine.FindWinningLine(match.Board, 3, 0, match.Config.WinLength);
            CollectionAssert.AreEquivalent(new[] { (0, 0), (1, 0), (2, 0), (3, 0) }, line);
        }

        [Test]
        public void FindWinningLine_MainDiagonalOn6X6_DetectsWin()
        {
            var match = NewMatch(6);
            PlayAll(match,
                (0, 0), (0, 1), (1, 1), (0, 2), (2, 2), (0, 3), (3, 3)); // X: (0,0)-(3,3)

            Assert.AreEqual(MatchStatus.XWon, match.Status);
            var line = RulesEngine.FindWinningLine(match.Board, 3, 3, match.Config.WinLength);
            CollectionAssert.AreEquivalent(new[] { (0, 0), (1, 1), (2, 2), (3, 3) }, line);
        }

        [Test]
        public void FindWinningLine_AntiDiagonalOn6X6_DetectsWin()
        {
            var match = NewMatch(6);
            PlayAll(match,
                (0, 3), (0, 0), (1, 2), (0, 1), (2, 1), (0, 2), (3, 0)); // X: (0,3)-(3,0)

            Assert.AreEqual(MatchStatus.XWon, match.Status);
            var line = RulesEngine.FindWinningLine(match.Board, 3, 0, match.Config.WinLength);
            CollectionAssert.AreEquivalent(new[] { (0, 3), (1, 2), (2, 1), (3, 0) }, line);
        }

        [Test]
        public void FindWinningLine_Overline_CountsAsWin()
        {
            // 6x6, k=4. X fills two disjoint pairs on row 0, then connects them into a run
            // of 5 with a single move: (0,0)(0,1) _ (0,3)(0,4) -> filling (0,2) makes 5-in-a-row.
            var match = NewMatch(6);
            PlayAll(match,
                (0, 0), (1, 0),
                (0, 1), (2, 0),
                (0, 4), (1, 2),
                (0, 3), (2, 2));

            // Sanity: no win yet before the connecting move.
            Assert.AreEqual(MatchStatus.InProgress, match.Status);

            var result = match.PlayMove(0, 2);

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(MatchStatus.XWon, result.StatusAfterMove);
            Assert.AreEqual(5, result.WinningLine.Count);
            CollectionAssert.AreEquivalent(
                new[] { (0, 0), (0, 1), (0, 2), (0, 3), (0, 4) }, result.WinningLine);
        }

        [Test]
        public void FindWinningLine_NoLine_ReturnsNull()
        {
            var match = NewMatch(3);
            match.PlayMove(0, 0);
            var line = RulesEngine.FindWinningLine(match.Board, 0, 0, match.Config.WinLength);
            Assert.IsNull(line);
        }

        private static void PlayAll(MatchController match, params (int Row, int Col)[] moves)
        {
            foreach (var move in moves)
            {
                var result = match.PlayMove(move.Row, move.Col);
                Assert.IsTrue(result.IsValid, $"Move ({move.Row},{move.Col}) was rejected: {result.InvalidReason}");
            }
        }
    }
}
