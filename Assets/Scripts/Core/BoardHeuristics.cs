using System.Collections.Generic;

namespace TTTXO.Core
{
    /// <summary>
    /// Static positional heuristic for boards 6x6 and up, per design-doc.md section 3
    /// ("Heuristica estatica (tableros 6x6+)"). Evaluates every window of length k
    /// (horizontal, vertical, both diagonals) and scores it by how many own symbols
    /// it contains (with no opposing symbol in the same window).
    /// </summary>
    internal static class BoardHeuristics
    {
        // Scores from the design-doc table, keyed by how many of the mover's own symbols
        // sit in a window of length k that otherwise contains only empty cells.
        private const double ImmediateThreatScore = 10000; // k-1 own, 1 empty
        private const double OpenBothEndsScore = 1500;      // k-2 own, open on both outer ends
        private const double OpenOneEndScore = 400;          // k-2 own, open on one outer end
        private const double ThreeShortScore = 50;           // k-3 own
        private const double SingleSymbolScore = 5;           // exactly 1 own

        // Defending weighs more than attacking (design-doc.md section 3).
        private const double DefensiveWeight = 1.2;

        /// <summary>
        /// Whole-board evaluation from <paramref name="self"/>'s perspective: sum of own
        /// window scores minus 1.2x the sum of opponent window scores.
        /// </summary>
        public static double Evaluate(BoardState board, BoardConfig config, CellOwner self, CellOwner opponent)
        {
            double selfScore = 0;
            double opponentScore = 0;

            foreach (var window in EnumerateWindows(board.Size, config.WinLength))
            {
                int selfCount = 0;
                int opponentCount = 0;

                foreach (var cell in window)
                {
                    CellOwner owner = board.GetCell(cell.Row, cell.Col);
                    if (owner == self) selfCount++;
                    else if (owner == opponent) opponentCount++;
                }

                if (opponentCount == 0 && selfCount > 0)
                {
                    selfScore += ScoreWindow(selfCount, config.WinLength, window, board);
                }
                else if (selfCount == 0 && opponentCount > 0)
                {
                    opponentScore += ScoreWindow(opponentCount, config.WinLength, window, board);
                }
            }

            return selfScore - DefensiveWeight * opponentScore;
        }

        private static double ScoreWindow(int ownCount, int winLength, (int Row, int Col)[] window, BoardState board)
        {
            if (ownCount == winLength - 1)
                return ImmediateThreatScore;

            if (ownCount == winLength - 2)
            {
                (bool openBefore, bool openAfter) = CheckOpenEnds(window, board);
                if (openBefore && openAfter) return OpenBothEndsScore;
                if (openBefore || openAfter) return OpenOneEndScore;
                return 0;
            }

            if (winLength - 3 >= 1 && ownCount == winLength - 3)
                return ThreeShortScore;

            if (ownCount == 1)
                return SingleSymbolScore;

            return 0;
        }

        private static (bool OpenBefore, bool OpenAfter) CheckOpenEnds((int Row, int Col)[] window, BoardState board)
        {
            (int Row, int Col) first = window[0];
            (int Row, int Col) last = window[window.Length - 1];
            int rowDelta = window[1].Row - window[0].Row;
            int colDelta = window[1].Col - window[0].Col;

            bool openBefore = IsEmptyInRange(board, first.Row - rowDelta, first.Col - colDelta);
            bool openAfter = IsEmptyInRange(board, last.Row + rowDelta, last.Col + colDelta);
            return (openBefore, openAfter);
        }

        private static bool IsEmptyInRange(BoardState board, int row, int col) =>
            board.IsInRange(row, col) && board.GetCell(row, col) == CellOwner.None;

        private static IEnumerable<(int Row, int Col)[]> EnumerateWindows(int size, int winLength)
        {
            // Horizontal
            for (int r = 0; r < size; r++)
                for (int c = 0; c <= size - winLength; c++)
                    yield return BuildWindow(r, c, 0, 1, winLength);

            // Vertical
            for (int c = 0; c < size; c++)
                for (int r = 0; r <= size - winLength; r++)
                    yield return BuildWindow(r, c, 1, 0, winLength);

            // Diagonal, top-left to bottom-right
            for (int r = 0; r <= size - winLength; r++)
                for (int c = 0; c <= size - winLength; c++)
                    yield return BuildWindow(r, c, 1, 1, winLength);

            // Diagonal, top-right to bottom-left
            for (int r = 0; r <= size - winLength; r++)
                for (int c = winLength - 1; c < size; c++)
                    yield return BuildWindow(r, c, 1, -1, winLength);
        }

        private static (int Row, int Col)[] BuildWindow(int startRow, int startCol, int rowDelta, int colDelta, int winLength)
        {
            var window = new (int Row, int Col)[winLength];
            for (int i = 0; i < winLength; i++)
                window[i] = (startRow + i * rowDelta, startCol + i * colDelta);
            return window;
        }
    }
}
