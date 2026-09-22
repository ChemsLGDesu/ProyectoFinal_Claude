using System;
using System.Collections.Generic;

namespace TTTXO.Core
{
    /// <summary>
    /// Soft-currency reward for a finished match, per design-doc.md section 4. Daily caps
    /// and anti-farming limits are explicitly out of scope here (they are not core game
    /// logic and depend on wall-clock/local persistence).
    /// </summary>
    public static class RewardCalculator
    {
        private const int LocalMultiplayerFlatReward = 2;

        private static readonly Dictionary<int, (int Win, int Draw, int Loss)> BaseRewardsBySize =
            new Dictionary<int, (int Win, int Draw, int Loss)>
            {
                { 3, (10, 5, 2) },
                { 6, (20, 10, 4) },
                { 9, (30, 15, 6) },
                { 11, (40, 20, 8) }
            };

        /// <summary>
        /// Soft currency earned by <paramref name="localPlayer"/> for a finished match.
        /// </summary>
        /// <param name="mode">Single-player (vs AI) or local 2-player.</param>
        /// <param name="difficulty">Required for <see cref="GameMode.SinglePlayer"/>; ignored otherwise.</param>
        /// <param name="boardSize">Board size (3, 6, 9 or 11).</param>
        /// <param name="finalStatus">The match's final status. InProgress (abandoned/unfinished) pays 0.</param>
        /// <param name="localPlayer">Which symbol the rewarded player controlled.</param>
        public static int SoftCurrencyFor(
            GameMode mode, AiDifficulty? difficulty, int boardSize, MatchStatus finalStatus, CellOwner localPlayer)
        {
            // An abandoned/unfinished match pays 0 (design-doc.md section 4).
            if (finalStatus == MatchStatus.InProgress)
                return 0;

            if (mode == GameMode.LocalMultiplayer)
                return LocalMultiplayerFlatReward;

            if (!BaseRewardsBySize.TryGetValue(boardSize, out (int Win, int Draw, int Loss) rewards))
                throw new ArgumentException($"Unknown board size: {boardSize}", nameof(boardSize));

            if (difficulty == null)
                throw new ArgumentException("AI difficulty is required to compute single-player rewards.", nameof(difficulty));

            int baseReward = GetBaseReward(rewards, finalStatus, localPlayer);
            double multiplier = GetDifficultyMultiplier(difficulty.Value);
            return (int)Math.Floor(baseReward * multiplier);
        }

        private static int GetBaseReward((int Win, int Draw, int Loss) rewards, MatchStatus status, CellOwner localPlayer)
        {
            if (status == MatchStatus.Draw)
                return rewards.Draw;

            bool localPlayerWon =
                (status == MatchStatus.XWon && localPlayer == CellOwner.X) ||
                (status == MatchStatus.OWon && localPlayer == CellOwner.O);

            return localPlayerWon ? rewards.Win : rewards.Loss;
        }

        private static double GetDifficultyMultiplier(AiDifficulty difficulty)
        {
            switch (difficulty)
            {
                case AiDifficulty.Easy: return 0.5;
                case AiDifficulty.Medium: return 1.0;
                case AiDifficulty.Hard: return 1.5;
                // Fixed, independent of the adaptive level (design-doc.md section 4): paying more
                // at high L would reward manipulating the adaptive controller.
                case AiDifficulty.Adaptive: return 1.0;
                default:
                    throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "Unknown AI difficulty.");
            }
        }
    }
}
