using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using UnityEngine;

namespace TTTXO.Game.Services
{
    /// <summary>
    /// Exposes the Authentication player name (format "Name#1234" - Authentication auto-appends a
    /// discriminator suffix so display names stay unique), the same pattern used by TCGMaster
    /// (Docs/03-Arquitectura-UGS-TicTacToe.md#Authentication: "SetPlayerName espeja el nombre
    /// elegido al player name de Authentication (sufijo #1234) para que el leaderboard muestre
    /// nombres reales"). Home shows this name when a session is available and falls back to
    /// <c>UiText.Common.PlayerNamePlaceholder</c> otherwise (see HomeScreenController) - there is
    /// no player-chosen-name UI yet in Milestone 2, only reading Authentication's default name.
    /// </summary>
    public static class PlayerNameService
    {
        // Seed name used the very first time a session has none set yet (Authentication returns
        // null until a name is set at least once - see GetPlayerNameAsync remarks below). There is
        // no name-editing UI in Milestone 2 (that is a later Profile/Settings feature), so this is
        // only what makes the "#1234" discriminator visible on Home from day one.
        private const string DefaultNameSeed = "Player";

        public static string CachedPlayerName { get; private set; }

        public static bool HasPlayerName => !string.IsNullOrEmpty(CachedPlayerName);

        /// <summary>
        /// Fetches the current player name from Authentication (format "Name#1234" - the "#1234"
        /// discriminator is appended server-side automatically). If no name has ever been set for
        /// this session, <c>GetPlayerNameAsync</c> returns null, in which case this seeds one via
        /// <c>UpdatePlayerNameAsync(DefaultNameSeed)</c> and re-reads it. Best-effort throughout:
        /// returns null and logs a warning on any failure instead of throwing - callers should keep
        /// showing the local placeholder in that case.
        /// </summary>
        public static async Task<string> RefreshAsync()
        {
            if (UgsInitializer.Status != UgsInitStatus.Ready)
            {
                return null;
            }

            try
            {
                string name = await AuthenticationService.Instance.GetPlayerNameAsync();
                if (string.IsNullOrEmpty(name))
                {
                    name = await AuthenticationService.Instance.UpdatePlayerNameAsync(DefaultNameSeed);
                }

                CachedPlayerName = name;
                return name;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"PlayerNameService: failed to fetch player name - {ex.Message}");
                return null;
            }
        }
    }
}
