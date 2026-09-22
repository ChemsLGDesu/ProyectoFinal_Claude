using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace TTTXO.CloudCode
{
    /// <summary>
    /// Server-side gate on which board sizes may actually be played, per mode.
    ///
    /// <c>CreateMatch</c> used to validate only against <see cref="TTTXO.Core.BoardConfig"/>, which is
    /// the table of board *rules* - every size the game has ever specified, including ones not on
    /// offer. That made the 9x9/11x11 launch cut client-side only: the sizes disappeared from the
    /// board select screen, and a modified client could still ask for them and be paid ordinary
    /// rewards. Worse on the Ranked path, which never checked the size at all - <c>mode="ranked"</c>
    /// with an 11x11 board would have created a ranked match on a board with no deployed leaderboard.
    ///
    /// The rule enforced here is the one BOARD_CONFIGS already expresses and nobody was reading:
    /// a size is playable in a mode when the deployed config lists that mode for that size. Remote
    /// Config stays the single source of truth, so bringing boards back remains a config change.
    ///
    /// <para><b>Failure posture, and why it differs from the other readers.</b>
    /// <see cref="MatchmakingConfigReader"/> and <see cref="RankedConfigReader"/> deliberately fail
    /// open to their defaults: a Remote Config hiccup must not stop a match from resolving. This is a
    /// security control, so failing open would mean the gate vanishes exactly when the config read is
    /// flaky. It fails to <see cref="LaunchFloorSizes"/> instead - the sizes that ship today - which
    /// is closed against everything the cut removed while never blocking a board a legitimate client
    /// can actually request. The cost is that this list is a second place to edit when a board comes
    /// back; the same trade already exists in <see cref="RankedQueryFunctions.RankedBoardSizes"/>.</para>
    /// </summary>
    public static class BoardAvailability
    {
        private const string BoardConfigsKey = "BOARD_CONFIGS";

        /// <summary>
        /// Sizes accepted when BOARD_CONFIGS cannot be read at all. Must match the sizes actually on
        /// offer in Assets/RemoteConfig/GameConfig.rc - see the class remarks for the trade this
        /// represents. Deliberately a floor, not a default: it is what the gate falls back to, never
        /// what it prefers.
        /// </summary>
        private static readonly int[] LaunchFloorSizes = { 3, 6 };

        /// <summary>
        /// Throws unless <paramref name="boardSize"/> is offered for <paramref name="mode"/> by the
        /// deployed BOARD_CONFIGS. Call before any match is created, for every mode.
        /// </summary>
        public static async Task EnsureOfferedAsync(
            IExecutionContext context, IGameApiClient gameApiClient, int boardSize, string mode, ILogger logger)
        {
            var boards = await TryReadAsync(context, gameApiClient);

            if (boards == null)
            {
                if (Array.IndexOf(LaunchFloorSizes, boardSize) >= 0)
                {
                    logger.LogWarning(
                        "BoardAvailability: BOARD_CONFIGS unreadable; allowing boardSize={BoardSize} mode={Mode} from the launch floor.",
                        boardSize, mode);
                    return;
                }

                logger.LogWarning(
                    "BoardAvailability: BOARD_CONFIGS unreadable; rejecting boardSize={BoardSize} mode={Mode} because it is outside the launch floor.",
                    boardSize, mode);
                throw new Exception($"Board size {boardSize} is not available.");
            }

            bool offered = boards.Any(b =>
                b.Size == boardSize
                && b.Modes != null
                && b.Modes.Contains(mode, StringComparer.OrdinalIgnoreCase));

            if (offered)
            {
                return;
            }

            // Logged as a warning rather than an error: with a correct client this is unreachable, so
            // every occurrence is either a stale client after a config change or a tampered one. Both
            // are worth seeing, neither is a server fault.
            logger.LogWarning(
                "BoardAvailability: rejected boardSize={BoardSize} for mode={Mode} - not offered by the deployed BOARD_CONFIGS.",
                boardSize, mode);
            throw new Exception($"Board size {boardSize} is not available for mode '{mode}'.");
        }

        /// <summary>Reads BOARD_CONFIGS, or returns null if it cannot be read or parsed. Never throws:
        /// the caller decides what an unreadable config means, and for this gate that decision is the
        /// whole point.</summary>
        private static async Task<List<BoardConfigDto>?> TryReadAsync(IExecutionContext context, IGameApiClient gameApiClient)
        {
            try
            {
                var settingsResult = await gameApiClient.RemoteConfigSettings.AssignSettingsGetAsync(
                    context,
                    context.AccessToken,
                    context.ProjectId,
                    context.EnvironmentId,
                    null,
                    new List<string> { BoardConfigsKey });

                var settings = settingsResult.Data.Configs.Settings;
                if (settings.TryGetValue(BoardConfigsKey, out object rawValue) && rawValue != null)
                {
                    string json = rawValue is JsonElement element ? element.GetRawText() : rawValue.ToString() ?? string.Empty;
                    var parsed = JsonSerializer.Deserialize<List<BoardConfigDto>>(
                        json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    // An empty list is a real answer ("nothing is playable") but far more likely a
                    // broken deploy, and treating it as authoritative would take the whole game down.
                    // Hand it back as unreadable so the launch floor applies instead.
                    if (parsed != null && parsed.Count > 0)
                    {
                        return parsed;
                    }
                }
            }
            catch
            {
                // Swallowed by design - see TryReadAsync's remarks and the class's failure posture.
            }

            return null;
        }
    }
}
