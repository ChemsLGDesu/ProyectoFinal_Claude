using UnityEngine;

namespace TTTXO.Game.Services
{
    /// <summary>
    /// EXPLICIT OFFLINE FALLBACK ONLY - a real <c>GetRankedProfile</c> Cloud Code function exists now
    /// (see <see cref="RankedQueryService.GetProfileAsync"/>) and is the authoritative source
    /// <see cref="MatchmakingService"/> uses for a Ranked ticket's <c>mmr</c> attribute (design-doc.md
    /// section 6.2: "El ticket publica el MMR del tablero al que se está encolando... leído por el
    /// cliente del profile"). This cache is only ever consulted when that server read itself fails
    /// (offline, Cloud Code unreachable) - same "degrade gracefully, never break play" posture as
    /// every other Cloud Code read in this project (<see cref="GameConfigService"/>,
    /// <c>UiText.Localize</c>). It remembers the last MMR value the SERVER itself reported back to
    /// this client (either via a fresh <see cref="RankedQueryService.GetProfileAsync"/> call, or via
    /// <c>RankedMmrResultDto</c> on a CreateMatch/PlayMove/GetMatchState response that just settled a
    /// Ranked unit), persisted locally (PlayerPrefs, same device-local convenience pattern as
    /// <see cref="TTTXO.Game.Bootstrap.GameManager"/>'s soft-currency mirror) so it survives an app
    /// restart and a transient network blip.
    ///
    /// This is NEVER computed/derived - only ever a verbatim value the server already told this
    /// client (see <see cref="SetKnownMmr"/> call sites). It degrades gracefully to
    /// <c>RankedConfigDto.InitialMmr</c> (1000, the server's own default for a brand-new player) when
    /// nothing has been observed yet, which is exactly correct for a player who has genuinely never
    /// played Ranked on that board. It can still go stale relative to the server (inactivity decay,
    /// season soft-reset happening while this client is offline) - that staleness window is now
    /// bounded to "GetRankedProfile itself failed right when a Ranked search started", not "every
    /// search", since <see cref="MatchmakingService.StartSearchAsync"/> always tries the live read
    /// first.
    /// </summary>
    public static class RankedProfileCache
    {
        private const string PrefKeyPrefix = "TTTXO.RankedLastKnownMmr.";

        /// <summary>Last MMR the server reported for <paramref name="boardSize"/>, or <paramref name="fallbackInitialMmr"/> if this client has never observed one (new player, or fresh install).</summary>
        public static int GetKnownMmr(int boardSize, int fallbackInitialMmr)
        {
            return PlayerPrefs.GetInt(PrefKeyPrefix + boardSize, fallbackInitialMmr);
        }

        /// <summary>Records a real value the server just returned (see <see cref="RankedMmrResultDto.MmrAfter"/>) - never a client-computed guess.</summary>
        public static void SetKnownMmr(int boardSize, int mmr)
        {
            PlayerPrefs.SetInt(PrefKeyPrefix + boardSize, mmr);
            PlayerPrefs.Save();
        }
    }
}
