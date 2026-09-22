using System;

namespace TTTXO.Core
{
    /// <summary>
    /// Mutable grid of cell owners for a single match. Mutation is internal so that only
    /// <see cref="MatchController"/> and the AI search (both in TTTXO.Core) can change it;
    /// outside consumers (UI, tests) only read it.
    /// </summary>
    public sealed class BoardState
    {
        private readonly CellOwner[,] cells;

        public BoardState(int size)
        {
            if (size <= 0)
                throw new ArgumentException("Board size must be positive.", nameof(size));

            Size = size;
            cells = new CellOwner[size, size];
            EmptyCellCount = size * size;
        }

        public int Size { get; }

        public int EmptyCellCount { get; private set; }

        public bool IsFull => EmptyCellCount == 0;

        public CellOwner GetCell(int row, int col)
        {
            ValidateCoordinates(row, col);
            return cells[row, col];
        }

        internal void SetCell(int row, int col, CellOwner owner)
        {
            ValidateCoordinates(row, col);
            CellOwner previous = cells[row, col];
            if (previous == owner)
                return;

            cells[row, col] = owner;

            if (previous == CellOwner.None && owner != CellOwner.None)
                EmptyCellCount--;
            else if (previous != CellOwner.None && owner == CellOwner.None)
                EmptyCellCount++;
        }

        internal bool IsInRange(int row, int col) =>
            row >= 0 && row < Size && col >= 0 && col < Size;

        /// <summary>Deep copy used by the AI search so it never mutates the live match board.</summary>
        internal BoardState Clone()
        {
            var clone = new BoardState(Size);
            for (int r = 0; r < Size; r++)
            {
                for (int c = 0; c < Size; c++)
                {
                    clone.cells[r, c] = cells[r, c];
                }
            }

            clone.EmptyCellCount = EmptyCellCount;
            return clone;
        }

        private void ValidateCoordinates(int row, int col)
        {
            if (!IsInRange(row, col))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(row),
                    $"Cell ({row},{col}) is out of range for a {Size}x{Size} board.");
            }
        }
    }
}
