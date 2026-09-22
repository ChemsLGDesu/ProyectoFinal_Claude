using System.Text.Json;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace TTTXO.CloudCode
{
    /// <summary>
    /// Reads the Remote Config <c>RANKED_CONFIG</c> key (Milestone 5, design-doc.md section 6.1).
    /// Same posture as <see cref="MatchmakingConfigReader"/>: never throws, falls back to
    /// <see cref="RankedConfigDto"/>'s own defaults (the values design-doc.md section 6 ratified)
    /// on any Remote Config hiccup, so a missing/unreachable key degrades gracefully instead of
    /// breaking a Ranked match/series resolution. Shared by <see cref="GetGameConfigFunctions.GetGameConfig"/>
    /// and every Ranked-aware branch in <see cref="MatchFunctions"/> so both stay in sync.
    /// </summary>
    public static class RankedConfigReader
    {
        private const string RankedConfigKey = "RANKED_CONFIG";

        public static async Task<RankedConfigDto> GetAsync(IExecutionContext context, IGameApiClient gameApiClient)
        {
            try
            {
                var settingsResult = await gameApiClient.RemoteConfigSettings.AssignSettingsGetAsync(
                    context,
                    context.AccessToken,
                    context.ProjectId,
                    context.EnvironmentId,
                    null,
                    new List<string> { RankedConfigKey });

                var settings = settingsResult.Data.Configs.Settings;
                if (settings.TryGetValue(RankedConfigKey, out object rawValue) && rawValue != null)
                {
                    string json = rawValue is JsonElement element ? element.GetRawText() : rawValue.ToString() ?? string.Empty;
                    var parsed = JsonSerializer.Deserialize<RankedConfigDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (parsed != null)
                    {
                        return parsed;
                    }
                }
            }
            catch
            {
                // Intentionally swallowed (see class remarks): fall through to defaults below.
            }

            return new RankedConfigDto();
        }
    }
}
