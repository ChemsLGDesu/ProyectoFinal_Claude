namespace TTTXO.CloudCode
{
    /// <summary>
    /// Pure Elo math for Ranked (Milestone 5, design-doc.md section 6.1 "Formula"). No Cloud Save
    /// I/O here (see <see cref="RankedProfileStore"/>) and no anti-boosting ledger state (see
    /// <see cref="RankedRewardStore"/>'s rival-win counter) - just the arithmetic, kept pure/testable
    /// the same way <see cref="OnlineRewardCalculator"/> is.
    /// </summary>
    public static class RankedEloCalculator
    {
        /// <summary>Score for the winner of the scored unit (series in 3x3, match in 6x6).</summary>
        public const double ScoreWin = 1.0;

        public const double ScoreDraw = 0.5;

        public const double ScoreLoss = 0.0;

        /// <summary>
        /// design-doc.md section 6.1, steps 1-6. Computes ONE side's delta - call twice (once per
        /// player, with sides swapped) for a full unit, since <paramref name="kFactor"/> and
        /// <paramref name="repeatRivalDamping"/> can legitimately differ between the two players
        /// (K depends on each player's own placement/MMR; damping depends on each player's own
        /// win-count against this specific rival today).
        /// </summary>
        /// <param name="selfMmr">This player's MMR before the unit.</param>
        /// <param name="rivalMmr">The rival's MMR before the unit.</param>
        /// <param name="selfMovedFirst">Whether THIS player made the first move of the unit (design-doc.md: `F` only ever applies in 6x6 - callers must pass <paramref name="firstMoveElo"/> = 0 for 3x3, see design-doc.md "En 3x3 F = 0 siempre").</param>
        /// <param name="rivalMovedFirst">Whether the rival made the first move.</param>
        /// <param name="firstMoveElo">`F` - 0 for 3x3, <see cref="RankedConfigDto.FirstMoveEloBoard6"/> for 6x6.</param>
        /// <param name="score">1 (win) / 0.5 (draw) / 0 (loss) of the unit, from THIS player's perspective.</param>
        /// <param name="kFactor">This player's own K (design-doc.md section 6.1 table - depends on THIS player's placements-played/MMR, not the rival's).</param>
        /// <param name="repeatRivalDamping">`D` (design-doc.md section 6.3) - only ever applied when the raw delta is positive (a gain); a loss is never damped regardless of what is passed here (the caller may pass any value for a loss, it is ignored).</param>
        /// <returns>The signed MMR delta for THIS player, already rounded (half-away-from-zero) with the "decisive result can never round to 0" force applied.</returns>
        public static int ComputeDelta(
            int selfMmr, int rivalMmr, bool selfMovedFirst, bool rivalMovedFirst, int firstMoveElo,
            double score, int kFactor, double repeatRivalDamping)
        {
            double rpSelf = selfMmr + (selfMovedFirst ? firstMoveElo : 0);
            double rpRival = rivalMmr + (rivalMovedFirst ? firstMoveElo : 0);

            double expectedSelf = 1.0 / (1.0 + Math.Pow(10.0, (rpRival - rpSelf) / 400.0));
            double rawDelta = kFactor * (score - expectedSelf);

            bool isGain = rawDelta > 0.0;
            double damping = isGain ? repeatRivalDamping : 1.0; // "las pérdidas nunca se amortiguan"
            double dampedDelta = rawDelta * damping;

            int rounded = (int)Math.Round(dampedDelta, MidpointRounding.AwayFromZero);

            bool decisive = score == ScoreWin || score == ScoreLoss;
            if (decisive && damping > 0.0 && rounded == 0)
            {
                // design-doc.md: "Si el resultado fue decisivo (S = 1 o S = 0), D > 0 y el redondeo
                // dio 0, se fuerza ±1" - damping > 0 excludes the deliberate "5th+ win vs same rival
                // today pays exactly 0" anti-boosting case (that 0 must stay 0, not become ±1).
                rounded = dampedDelta >= 0.0 ? 1 : -1;
            }

            return rounded;
        }

        /// <summary>design-doc.md section 6.1 "Piso": <c>max(500, MMR_previo + Δ)</c>.</summary>
        public static int ApplyFloor(int mmrBefore, int delta, int mmrFloor) => Math.Max(mmrFloor, mmrBefore + delta);
    }
}
