using System.Text.Json;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace TTTXO.CloudCode
{
    /// <summary>
    /// Reads the Remote Config <c>MATCHMAKING_CONFIG</c> key (Milestone 4: abandon timeout, wait
    /// ceiling, ticket TTL mirror - see <see cref="MatchmakingConfigDto"/>). Shared by
    /// <see cref="GetGameConfigFunctions.GetGameConfig"/> (so the client can read the wait ceiling
    /// for the Matchmaking screen) and <see cref="MatchFunctions.GetMatchState"/> (so the abandonment
    /// check uses the same source of truth) - factored out so both stay in sync instead of each
    /// re-implementing the same Remote Config read/parse.
    ///
    /// Unlike <see cref="GetGameConfigFunctions.GetGameConfig"/>, this never throws: a missing or
    /// unreachable Remote Config key must not prevent <c>PlayMove</c>/<c>GetMatchState</c> from
    /// resolving a match, so any failure here just falls back to <see cref="MatchmakingConfigDto"/>'s
    /// own defaults.
    /// </summary>
    public static class MatchmakingConfigReader
    {
        private const string MatchmakingConfigKey = "MATCHMAKING_CONFIG";

        public static async Task<MatchmakingConfigDto> GetAsync(IExecutionContext context, IGameApiClient gameApiClient)
        {
            try
            {
                var settingsResult = await gameApiClient.RemoteConfigSettings.AssignSettingsGetAsync(
                    context,
                    context.AccessToken,
                    context.ProjectId,
                    context.EnvironmentId,
                    null,
                    new List<string> { MatchmakingConfigKey });

                var settings = settingsResult.Data.Configs.Settings;
                if (settings.TryGetValue(MatchmakingConfigKey, out object rawValue) && rawValue != null)
                {
                    string json = rawValue is JsonElement element ? element.GetRawText() : rawValue.ToString() ?? string.Empty;
                    var parsed = JsonSerializer.Deserialize<MatchmakingConfigDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (parsed != null)
                    {
                        return parsed;
                    }
                }
            }
            catch
            {
                // Intentionally swallowed (see class remarks): fall through to defaults below rather
                // than let a Remote Config hiccup break match resolution.
            }

            return new MatchmakingConfigDto();
        }
    }
}
