using System;

namespace TTTXO.Core
{
    /// <summary>
    /// Per-difficulty AI parameters. Mirrors the future Remote Config key
    /// <c>AI_DIFFICULTY_PARAMS</c> (design-doc.md section 3).
    /// </summary>
    public sealed class AiParams
    {
        // 3x3 always plays full minimax to the end of the game tree when it searches at all
        // (design-doc.md: "minimax completo hasta el final del arbol, 9 plies maximo").
        // 9 is a safe upper bound on remaining plies for a 3x3 board, not a balance value.
        private const int FullSearchDepth3X3 = 9;

        public AiParams(double winProbability, double blockProbability, double blunderChance, int searchDepth)
        {
            WinProbability = winProbability;
            BlockProbability = blockProbability;
            BlunderChance = blunderChance;
            SearchDepth = searchDepth;
        }

        /// <summary>Probability the AI takes an immediate winning move when one exists (pWin).</summary>
        public double WinProbability { get; }

        /// <summary>Probability the AI blocks the opponent's immediate winning move (pBlock).</summary>
        public double BlockProbability { get; }

        /// <summary>Probability the AI plays the second-best positional move instead of the best one.</summary>
        public double BlunderChance { get; }

        /// <summary>Search depth in plies for the positional step; 0 means no search (random among candidates).</summary>
        public int SearchDepth { get; }

        /// <summary>
        /// Builds the params for a fixed difficulty and board size, per design-doc.md section 3.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="difficulty"/> is <see cref="AiDifficulty.Adaptive"/> (use
        /// <see cref="AdaptiveAiController.GetParams"/> instead), or the board size is unknown.
        /// </exception>
        public static AiParams ForDifficulty(AiDifficulty difficulty, int boardSize)
        {
            // Validates the board size against the known configs (throws if unknown).
            BoardConfig.ForSize(boardSize);
            bool is3X3 = boardSize == 3;

            switch (difficulty)
            {
                case AiDifficulty.Easy:
                    // No search at all: cascade steps 1-2 only, then uniform-random among candidates.
                    return new AiParams(0.70, 0.50, 0.0, 0);

                case AiDifficulty.Medium:
                    return new AiParams(1.00, 0.90, 0.25, is3X3 ? FullSearchDepth3X3 : 2);

                case AiDifficulty.Hard:
                    return new AiParams(1.00, 1.00, is3X3 ? 0.0 : 0.05, is3X3 ? FullSearchDepth3X3 : 4);

                case AiDifficulty.Adaptive:
                    throw new ArgumentException(
                        "Adaptive difficulty parameters come from AdaptiveAiController.GetParams, not ForDifficulty.",
                        nameof(difficulty));

                default:
                    throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "Unknown AI difficulty.");
            }
        }
    }
}
