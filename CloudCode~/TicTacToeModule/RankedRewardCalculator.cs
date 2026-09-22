namespace TTTXO.CloudCode
{
    /// <summary>
    /// Pure Ranked soft-currency table (design-doc.md section 6.3): flat values, NOT derived from
    /// <see cref="OnlineRewardCalculator"/>'s Quickmatch curve ("Ranked no hereda la curva de
    /// Quickmatch... Tiene tabla propia, expresada en valores absolutos"). Victory/draw only -
    /// defeat and victory-by-abandonment are always 0 (design-doc.md: "identico a Quickmatch, por
    /// las mismas razones ya documentadas... cerrar el vector de colusion").
    /// </summary>
    public static class RankedRewardCalculator
    {
        public const string ResultWin = "win";
        public const string ResultDraw = "draw";
        public const string ResultLoss = "loss";
        public const string ResultNoContest = "no_contest";

        /// <param name="boardSize">3 or 6 - Ranked does not exist on 9x9/11x11 (design-doc.md section 6.0).</param>
        /// <param name="result">The scored unit's result from this player's perspective (win/draw/loss/no_contest) - loss and no_contest both pay 0.</param>
        public static int RawRewardFor(RankedConfigDto config, int boardSize, string result)
        {
            return result switch
            {
                ResultWin => boardSize == 3 ? config.RewardWinBoard3 : config.RewardWinBoard6,
                ResultDraw => boardSize == 3 ? config.RewardDrawBoard3 : config.RewardDrawBoard6,
                _ => 0,
            };
        }
    }
}
