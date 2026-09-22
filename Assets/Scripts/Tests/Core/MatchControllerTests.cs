using NUnit.Framework;
using TTTXO.Core;

namespace TTTXO.Core.Tests
{
    public class MatchControllerTests
    {
        [Test]
        public void Constructor_FirstPlayerNone_ThrowsArgumentException()
        {
            Assert.Throws<System.ArgumentException>(() =>
                new MatchController(BoardConfig.ForSize(3), CellOwner.None));
        }

        [Test]
        public void PlayMove_OccupiedCell_ReturnsInvalidAndDoesNotChangeState()
        {
            var match = new MatchController(BoardConfig.ForSize(3), CellOwner.X);
            match.PlayMove(0, 0); // X

            var before = match.CurrentPlayer;
            var turnsBefore = match.TurnCount;

            var result = match.PlayMove(0, 0); // O tries the same cell

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("Cell occupied", result.InvalidReason);
            Assert.AreEqual(before, match.CurrentPlayer);
            Assert.AreEqual(turnsBefore, match.TurnCount);
            Assert.AreEqual(CellOwner.X, match.Board.GetCell(0, 0));
        }

        [TestCase(-1, 0)]
        [TestCase(0, -1)]
        [TestCase(3, 0)]
        [TestCase(0, 3)]
        public void PlayMove_OutOfRange_ReturnsInvalidAndDoesNotChangeState(int row, int col)
        {
            var match = new MatchController(BoardConfig.ForSize(3), CellOwner.X);
            var turnsBefore = match.TurnCount;

            var result = match.PlayMove(row, col);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("Cell out of range", result.InvalidReason);
            Assert.AreEqual(turnsBefore, match.TurnCount);
            Assert.AreEqual(MatchStatus.InProgress, match.Status);
        }

        [Test]
        public void PlayMove_AfterMatchFinished_ReturnsInvalidAndDoesNotChangeState()
        {
            var match = new MatchController(BoardConfig.ForSize(3), CellOwner.X);
            // X wins row 0.
            match.PlayMove(0, 0);
            match.PlayMove(1, 0);
            match.PlayMove(0, 1);
            match.PlayMove(1, 1);
            match.PlayMove(0, 2);

            Assert.AreEqual(MatchStatus.XWon, match.Status);
            var turnsBefore = match.TurnCount;

            var result = match.PlayMove(2, 2);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("Match already finished", result.InvalidReason);
            Assert.AreEqual(turnsBefore, match.TurnCount);
            Assert.AreEqual(CellOwner.None, match.Board.GetCell(2, 2));
        }

        [Test]
        public void PlayMove_AlternatesTurnsAndTracksTurnCount()
        {
            var match = new MatchController(BoardConfig.ForSize(3), CellOwner.X);

            Assert.AreEqual(CellOwner.X, match.CurrentPlayer);
            Assert.AreEqual(0, match.TurnCount);

            match.PlayMove(0, 0);
            Assert.AreEqual(CellOwner.O, match.CurrentPlayer);
            Assert.AreEqual(1, match.TurnCount);

            match.PlayMove(1, 1);
            Assert.AreEqual(CellOwner.X, match.CurrentPlayer);
            Assert.AreEqual(2, match.TurnCount);

            match.PlayMove(2, 2);
            Assert.AreEqual(CellOwner.O, match.CurrentPlayer);
            Assert.AreEqual(3, match.TurnCount);
        }

        [Test]
        public void PlayMove_FullBoardNoLine_ReturnsDraw()
        {
            var match = new MatchController(BoardConfig.ForSize(3), CellOwner.X);

            // X O X
            // X O O
            // O X X
            var moves = new (int Row, int Col)[]
            {
                (0, 0), (0, 1), // X, O
                (1, 0), (1, 1), // X, O
                (2, 1), (1, 2), // X, O
                (0, 2), (2, 0), // X, O
                (2, 2)          // X
            };

            MoveResult last = null;
            foreach (var move in moves)
                last = match.PlayMove(move.Row, move.Col);

            Assert.IsTrue(last.IsValid);
            Assert.AreEqual(MatchStatus.Draw, last.StatusAfterMove);
            Assert.AreEqual(MatchStatus.Draw, match.Status);
            Assert.IsNull(last.WinningLine);
            Assert.IsTrue(match.Board.IsFull);
            Assert.AreEqual(0, match.Board.EmptyCellCount);
        }
    }
}
