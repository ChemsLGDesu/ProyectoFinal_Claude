using System;
using TTTXO.Core;

namespace TTTXO.Game.Bootstrap
{
    /// <summary>
    /// One row of the local Match History (Docs/06-Wireframes-UI.md, screen 9). Persisted as JSON
    /// via <see cref="GameManager"/> so it survives an app restart, mirroring the future Cloud Save
    /// <c>history</c> field (design-doc.md section 3) in shape only - this is a client-local
    /// convenience for Milestone 1.5, not the eventual server-authoritative record.
    /// </summary>
    /// <remarks>
    /// Fields are public and use built-in types only (no <see cref="Nullable{T}"/>) because
    /// <see cref="UnityEngine.JsonUtility"/> cannot serialize nullable value types.
    /// <see cref="DifficultyValue"/> of -1 means "not applicable" (Local Multiplayer matches).
    /// </remarks>
    [Serializable]
    public class MatchHistoryEntry
    {
        public GameMode Mode;
        public int BoardSize;
        public int DifficultyValue = -1;
        public MatchOutcome Outcome;
        public int DurationSeconds;
        public long TimestampUnixSeconds;

        public AiDifficulty? Difficulty => DifficultyValue < 0 ? (AiDifficulty?)null : (AiDifficulty)DifficultyValue;
    }
}
