using System;
using System.Collections.Generic;

namespace TTTXO.Core
{
    /// <summary>
    /// Pure win-detection logic, shared by <see cref="MatchController"/> and the AI search.
    /// Designed to be ported verbatim to Cloud Code's PlayMove in Milestone 2+
    /// (design-doc.md section 2).
    /// </summary>
    public static class RulesEngine
    {
        // The four unique line directions; each is walked both backward and forward from
        // the last move, so both diagonals and both horizontal/vertical senses are covered.
        private static readonly (int RowDelta, int ColDelta)[] Directions =
        {
            (0, 1),  // horizontal
            (1, 0),  // vertical
            (1, 1),  // diagonal, top-left to bottom-right
            (1, -1)  // diagonal, top-right to bottom-left
        };

        /// <summary>
        /// Checks whether the move just played at (row, col) completes a line of at least
        /// <paramref name="winLength"/> contiguous same-owner cells (overline counts as a win).
        /// Only scans around the last move, per design-doc.md section 2.
        /// </summary>
        /// <returns>The winning cells (which may be longer than winLength), or null if no win.</returns>
        public static IReadOnlyList<(int Row, int Col)> FindWinningLine(BoardState board, int row, int col, int winLength)
        {
            if (board == null)
                throw new ArgumentNullException(nameof(board));

            CellOwner owner = board.GetCell(row, col);
            if (owner == CellOwner.None)
                return null;

            foreach (var direction in Directions)
            {
                List<(int Row, int Col)> run = CollectRun(board, row, col, direction.RowDelta, direction.ColDelta, owner);
                if (run.Count >= winLength)
                    return run;
            }

            return null;
        }

        private static List<(int Row, int Col)> CollectRun(BoardState board, int row, int col, int rowDelta, int colDelta, CellOwner owner)
        {
            var cells = new List<(int Row, int Col)> { (row, col) };

            int r = row - rowDelta;
            int c = col - colDelta;
            while (board.IsInRange(r, c) && board.GetCell(r, c) == owner)
            {
                cells.Insert(0, (r, c));
                r -= rowDelta;
                c -= colDelta;
            }

            r = row + rowDelta;
            c = col + colDelta;
            while (board.IsInRange(r, c) && board.GetCell(r, c) == owner)
            {
                cells.Add((r, c));
                r += rowDelta;
                c += colDelta;
            }

            return cells;
        }
    }
}
