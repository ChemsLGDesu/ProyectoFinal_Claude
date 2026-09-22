namespace TTTXO.Game.Bootstrap
{
    /// <summary>
    /// Result of a completed match from a fixed recording perspective (see
    /// <see cref="MatchHistoryEntry"/>): the human player in Single Player, Player 1 in
    /// Local Multiplayer. Used only for local Match History (Milestone 1.5); not part of
    /// TTTXO.Core, which stays symbol-based (X/O) and mode-agnostic.
    /// </summary>
    public enum MatchOutcome
    {
        Win,
        Loss,
        Draw
    }
}
