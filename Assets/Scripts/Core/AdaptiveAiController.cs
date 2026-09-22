using System;
using System.Collections.Generic;

namespace TTTXO.Core
{
    /// <summary>
    /// Adaptive AI level tracker, per design-doc.md section 3 ("Adaptativo"). One instance
    /// tracks the level/history/streak for a single board size: the design doc requires the
    /// level to be kept per board size ("jugar bien en 3x3 no debe endurecer la IA de 11x11"),
    /// which this API achieves by having the caller own one controller per board size rather
    /// than this class partitioning state internally (see summary notes on this decision).
    /// </summary>
    public sealed class AdaptiveAiController
    {
        private const int WindowSize = 10;
        private const int MinSamplesForAdjustment = 3;
        private const int LevelStep = 10;
        private const int StreakStep = 15;
        private const int StreakLength = 3;
        private const double LowerBand = 0.40;
        private const double UpperBand = 0.60;
        private const int MinLevel = 0;
        private const int MaxLevel = 100;

        // 9 is a safe upper bound on remaining plies for a 3x3 board (full minimax), not a
        // balance value; mirrors AiParams' own 3x3 full-search depth.
        private const int FullSearchDepth3X3 = 9;

        private readonly Queue<double> recentScores = new Queue<double>(WindowSize);

        private int winStreak;
        private int lossStreak;

        public AdaptiveAiController(int initialLevel = 50)
        {
            CurrentLevel = Clamp(initialLevel);
        }

        public int CurrentLevel { get; private set; }

        /// <summary>
        /// Records a finished match's outcome and adjusts <see cref="CurrentLevel"/> per
        /// design-doc.md section 3. Call once per finished match, never mid-match.
        /// </summary>
        public void RecordResult(bool humanWon, bool draw)
        {
            double score = humanWon ? 1.0 : (draw ? 0.5 : 0.0);

            recentScores.Enqueue(score);
            while (recentScores.Count > WindowSize)
                recentScores.Dequeue();

            if (humanWon)
            {
                winStreak++;
                lossStreak = 0;
            }
            else if (draw)
            {
                winStreak = 0;
                lossStreak = 0;
            }
            else
            {
                lossStreak++;
                winStreak = 0;
            }

            if (recentScores.Count >= MinSamplesForAdjustment)
            {
                double winRate = Average(recentScores);
                if (winRate > UpperBand)
                    CurrentLevel = Clamp(CurrentLevel + LevelStep);
                else if (winRate < LowerBand)
                    CurrentLevel = Clamp(CurrentLevel - LevelStep);
            }

            // Streak rule is an additional, independent adjustment on top of the band rule above
            // (design-doc.md: "+15 adicional inmediato" / "-15"). It resets once applied.
            if (winStreak >= StreakLength)
            {
                CurrentLevel = Clamp(CurrentLevel + StreakStep);
                winStreak = 0;
            }
            else if (lossStreak >= StreakLength)
            {
                CurrentLevel = Clamp(CurrentLevel - StreakStep);
                lossStreak = 0;
            }
        }

        /// <summary>
        /// Linearly interpolates <see cref="AiParams"/> from <see cref="CurrentLevel"/>, per
        /// design-doc.md section 3. <paramref name="boardSize"/> only selects the 3x3 vs 6x6+
        /// searchDepth tiering; the level itself is this instance's own <see cref="CurrentLevel"/>.
        /// </summary>
        public AiParams GetParams(int boardSize)
        {
            // Validates the board size against the known configs (throws if unknown).
            BoardConfig.ForSize(boardSize);

            double t = CurrentLevel / 100.0;
            double winProbability = 0.70 + 0.30 * t;
            double blockProbability = 0.50 + 0.50 * t;
            double blunderChance = 0.40 * (1 - t);

            int searchDepth;
            if (boardSize == 3)
            {
                searchDepth = CurrentLevel < 35 ? 0 : FullSearchDepth3X3;
            }
            else
            {
                if (CurrentLevel < 35) searchDepth = 0;
                else if (CurrentLevel < 70) searchDepth = 2;
                else searchDepth = 4;
            }

            // blunderChance only applies when there is a search to blunder within
            // (design-doc.md: "aplica solo cuando hay busqueda, es decir L >= 35").
            if (searchDepth == 0)
                blunderChance = 0.0;

            return new AiParams(winProbability, blockProbability, blunderChance, searchDepth);
        }

        private static int Clamp(int level) => Math.Max(MinLevel, Math.Min(MaxLevel, level));

        private static double Average(IEnumerable<double> values)
        {
            double sum = 0;
            int count = 0;
            foreach (double value in values)
            {
                sum += value;
                count++;
            }

            return count == 0 ? 0 : sum / count;
        }
    }
}
