namespace TTTXO.Game.Services
{
    /// <summary>
    /// Client/server handshake version (Docs/01-Directrices-Proyecto.md "Control de versiones y
    /// deploy": "Reutilizar el patron de handshake de version (GameProtocol.Version) para evitar
    /// mezclar cliente y servidor incompatibles"). Mirrored verbatim in the Cloud Code module
    /// (CloudCode~/TicTacToeModule/GameProtocol.cs) - bump both sides together whenever
    /// GetGameConfig's response shape or any match RPC contract changes.
    ///
    /// Milestone 2 scope: <see cref="GameConfigService"/> compares this against the version the
    /// server returns and only logs a warning on mismatch (see design-doc.md and
    /// Docs/03-Arquitectura-UGS-TicTacToe.md - IAP with real money makes this critical, but blocking
    /// play on a mismatch is a later-milestone decision, not part of these M2 foundations).
    /// </summary>
    public static class GameProtocol
    {
        /// <summary>
        /// 2 (2026-08-12, ERR-KB-006): online rewards moved from Cloud Save key <c>currency</c>
        /// (access class Default, player-writable) to <c>authoritativeCurrency</c> (Protected). The
        /// RPC shapes did not change, but which key an RPC credits is part of the contract as far as
        /// a client is concerned: a version-1 client reads only <c>currency</c>, so against a
        /// version-2 server it plays fine and silently stops seeing its online winnings. That is
        /// exactly the "looks correct, is wrong" failure this handshake exists to make visible - the
        /// b4 beta APK already in a tester's hands is a version-1 client.
        /// </summary>
        public const int Version = 2;
    }
}
