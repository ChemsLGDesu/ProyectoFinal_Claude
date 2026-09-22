using System.Collections.Generic;

namespace TTTXO.Core
{
    /// <summary>
    /// Outcome of a single <see cref="MatchController.PlayMove"/> call.
    /// </summary>
    public sealed class MoveResult
    {
        internal MoveResult(
            bool isValid,
            string invalidReason,
            MatchStatus statusAfterMove,
            IReadOnlyList<(int Row, int Col)> winningLine)
        {
            IsValid = isValid;
            InvalidReason = invalidReason;
            StatusAfterMove = statusAfterMove;
            WinningLine = winningLine;
        }

        public bool IsValid { get; }

        /// <summary>English reason the move was rejected, or null when <see cref="IsValid"/> is true.</summary>
        public string InvalidReason { get; }

        public MatchStatus StatusAfterMove { get; }

        /// <summary>Winning cells, or null unless this move just won the match.</summary>
        public IReadOnlyList<(int Row, int Col)> WinningLine { get; }

        internal static MoveResult Invalid(string reason, MatchStatus currentStatus) =>
            new MoveResult(false, reason, currentStatus, null);

        internal static MoveResult Valid(MatchStatus statusAfterMove, IReadOnlyList<(int Row, int Col)> winningLine) =>
            new MoveResult(true, null, statusAfterMove, winningLine);
    }
}
