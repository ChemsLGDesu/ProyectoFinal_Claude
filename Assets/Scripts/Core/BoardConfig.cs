using System;
using System.Collections.Generic;

namespace TTTXO.Core
{
    /// <summary>
    /// Immutable board size/win-length pair. Mirrors the future Remote Config key
    /// <c>BOARD_CONFIGS</c> (design-doc.md section 1): only the data source changes
    /// in Milestone 2+, not this shape.
    /// </summary>
    public sealed class BoardConfig
    {
        private static readonly List<BoardConfig> AllConfigs = new List<BoardConfig>
        {
            new BoardConfig(3, 3),
            new BoardConfig(6, 4),
            new BoardConfig(9, 5),
            new BoardConfig(11, 5)
        };

        private BoardConfig(int size, int winLength)
        {
            Size = size;
            WinLength = winLength;
        }

        /// <summary>Board dimension (Size x Size).</summary>
        public int Size { get; }

        /// <summary>Number of contiguous own symbols required to win (overline allowed).</summary>
        public int WinLength { get; }

        /// <summary>All board configs supported in Milestone 1, per design-doc.md section 1.</summary>
        public static IReadOnlyList<BoardConfig> All => AllConfigs;

        /// <summary>
        /// Looks up the config for a given board size.
        /// </summary>
        /// <exception cref="ArgumentException">The size is not one of the supported board sizes.</exception>
        public static BoardConfig ForSize(int size)
        {
            for (int i = 0; i < AllConfigs.Count; i++)
            {
                if (AllConfigs[i].Size == size)
                    return AllConfigs[i];
            }

            throw new ArgumentException($"Unknown board size: {size}", nameof(size));
        }
    }
}
