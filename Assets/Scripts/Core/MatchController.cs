using System;
using System.Collections.Generic;

namespace TTTXO.Core
{
    /// <summary>
    /// Drives a single local match: validates moves, alternates turns and detects
    /// win/draw using <see cref="RulesEngine"/>. Same rules that will later run
    /// server-side in Cloud Code's PlayMove (design-doc.md section 2).
    /// </summary>
    public sealed class MatchController
    {
        public MatchController(BoardConfig config, CellOwner firstPlayer)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (firstPlayer != CellOwner.X && firstPlayer != CellOwner.O)
                throw new ArgumentException("First player must be X or O.", nameof(firstPlayer));

            Config = config;
            Board = new BoardState(config.Size);
            CurrentPlayer = firstPlayer;
            Status = MatchStatus.InProgress;
            TurnCount = 0;
            WinningLine = null;
        }

        public BoardState Board { get; }

        public BoardConfig Config { get; }

        public CellOwner CurrentPlayer { get; private set; }

        public MatchStatus Status { get; private set; }

        /// <summary>Winning cells, or null unless the match just finished by a win.</summary>
        public IReadOnlyList<(int Row, int Col)> WinningLine { get; private set; }

        public int TurnCount { get; private set; }

        public MoveResult PlayMove(int row, int col)
        {
            if (Status != MatchStatus.InProgress)
                return MoveResult.Invalid("Match already finished", Status);

            if (!Board.IsInRange(row, col))
                return MoveResult.Invalid("Cell out of range", Status);

            if (Board.GetCell(row, col) != CellOwner.None)
                return MoveResult.Invalid("Cell occupied", Status);

            CellOwner mover = CurrentPlayer;
            Board.SetCell(row, col, mover);
            TurnCount++;

            IReadOnlyList<(int Row, int Col)> winningLine = RulesEngine.FindWinningLine(Board, row, col, Config.WinLength);
            if (winningLine != null)
            {
                Status = mover == CellOwner.X ? MatchStatus.XWon : MatchStatus.OWon;
                WinningLine = winningLine;
                return MoveResult.Valid(Status, winningLine);
            }

            if (Board.IsFull)
            {
                Status = MatchStatus.Draw;
                return MoveResult.Valid(Status, null);
            }

            CurrentPlayer = mover == CellOwner.X ? CellOwner.O : CellOwner.X;
            return MoveResult.Valid(Status, null);
        }
    }
}
