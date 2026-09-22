using TTTXO.Core;

namespace TTTXO.CloudCode
{
    /// <summary>
    /// Pure soft-currency math for an online Quickmatch match (Milestone 4), server-side only
    /// (design-doc.md section 4, "Modo online Quickmatch" - "el cliente nunca calcula ni escribe la
    /// moneda online", Docs/01-Directrices-Proyecto.md#Seguridad / anti-cheat). No Cloud Save I/O
    /// here (see <see cref="OnlineRewardStore"/>) and no anti-farming ledger state (see
    /// <see cref="MatchFunctions.AwardOnlineRewardsAsync"/>) - just the base x multiplier
    /// arithmetic and the cap constants, kept pure/testable the same way
    /// <see cref="RewardCalculator"/> itself is.
    ///
    /// The base-by-size table is deliberately NOT hardcoded/duplicated here: it is derived by
    /// calling <see cref="RewardCalculator.SoftCurrencyFor"/> with <see cref="AiDifficulty.Medium"/>,
    /// whose x1.0 multiplier makes it an identity read that returns exactly the 1P-vs-Medium base
    /// reward for a given board size/result (design-doc.md section 4: "se parte de la misma base
    /// por tamano de la tabla 1P"). This guarantees the online base can never silently drift from
    /// the single source of truth already in <see cref="TTTXO.Core"/>, which this task does not
    /// modify (Docs/01-Directrices-Proyecto.md: "TTTXO.Core no se modifica").
    /// </summary>
    public static class OnlineRewardCalculator
    {
        /// <summary>design-doc.md section 4: "Multiplicador de victoria = x1,25 sobre la base por tamano (no x1,0)".</summary>
        public const double OnlineWinMultiplier = 1.25;

        /// <summary>design-doc.md section 4: "Tope diario online = 200 monedas/dia (sub-tope dedicado al modo online, por debajo del tope global de 300)".</summary>
        public const int OnlineDailyCap = 200;

        /// <summary>design-doc.md section 4: "El tope global de 300/dia sigue aplicando por encima de todo lo anterior; las ganancias online cuentan contra el." See <see cref="OnlineCurrencyLedgerRecord"/> for the documented online/offline coordination gap.</summary>
        public const int GlobalDailyCap = 300;

        /// <summary>design-doc.md section 4: "Tope por rival repetido = 3 victorias pagadas por rival cada 24 h" - the 4th+ paid occurrence against the same rival in a UTC day is rejected by the caller (see <see cref="MatchFunctions.AwardOnlineRewardsAsync"/>).</summary>
        public const int MaxPaidWinsPerRivalPerDay = 3;

        /// <summary>
        /// Raw reward before any anti-farming cap (rival/online-day/global-day) is applied -
        /// design-doc.md section 4's online table: victory = floor(base_win x 1.25), empate =
        /// base_draw x 1.0, derrota = 0.
        /// </summary>
        /// <param name="boardSize">3, 6, 9 or 11.</param>
        /// <param name="finalStatus">The match's terminal status (never <c>InProgress</c> - callers only invoke this once a match ends).</param>
        /// <param name="playerSymbol">Which symbol ("X"/"O") the player being scored controlled.</param>
        public static int ComputeRawReward(int boardSize, MatchStatus finalStatus, string playerSymbol)
        {
            if (finalStatus == MatchStatus.Draw)
            {
                // Identity read of the 1P base-draw-by-size table (Medium's x1.0 multiplier) -
                // design-doc.md section 4: "el empate paga la base tal cual (x1,0), porque un
                // empate no requiere 'ganarle' a nadie y no es un vector de colusion rentable".
                return RewardCalculator.SoftCurrencyFor(GameMode.SinglePlayer, AiDifficulty.Medium, boardSize, MatchStatus.Draw, CellOwner.X);
            }

            var symbolOwner = playerSymbol == "O" ? CellOwner.O : CellOwner.X;
            bool won = (finalStatus == MatchStatus.XWon && symbolOwner == CellOwner.X) ||
                       (finalStatus == MatchStatus.OWon && symbolOwner == CellOwner.O);

            // design-doc.md section 4: "Derrota online = 0 (a diferencia de 1P, que paga 2-8)".
            if (!won)
            {
                return 0;
            }

            int baseWin = RewardCalculator.SoftCurrencyFor(GameMode.SinglePlayer, AiDifficulty.Medium, boardSize, finalStatus, symbolOwner);
            return (int)Math.Floor(baseWin * OnlineWinMultiplier);
        }

        // --- Ranked (Milestone 5) extension point --------------------------------------------
        // design-doc.md section 4, "Lineamiento para Ranked": Ranked should NOT inherit this flat
        // per-result curve as-is - it should lean on ladder position / MMR tier and season-end
        // rewards instead, with per-match soft currency <= Quickmatch's (or 0, with most of the
        // payout as a season-close placement reward). NOT implemented here (explicitly out of
        // scope per this task). A future Ranked reward function can still reuse the Medium-base
        // read inside ComputeRawReward above as its own floor, then apply its own MMR-tier
        // multiplier instead of OnlineWinMultiplier, and its own (still server-side) cap set
        // instead of OnlineDailyCap/GlobalDailyCap/MaxPaidWinsPerRivalPerDay below - the ledger
        // shape in OnlineRewardStore/OnlineCurrencyLedgerRecord was kept generic enough (keyed by
        // playerId, not by mode) to be reused rather than forked.
    }
}
