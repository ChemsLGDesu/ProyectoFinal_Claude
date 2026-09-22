using System.Text.Json;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace TTTXO.CloudCode
{
    /// <summary>
    /// <c>GetGameConfig</c> - the single Remote Config read path
    /// (Docs/03-Arquitectura-UGS-TicTacToe.md#Remote Config: "el cliente no lee Remote Config
    /// directo... Cloud Code expone un unico endpoint (GetGameConfig) para que la config sea
    /// siempre consistente con lo que el propio modulo usa para resolver partidas"). Reads the
    /// <c>BOARD_CONFIGS</c> and <c>AI_DIFFICULTY_PARAMS</c> Remote Config keys (deployed from
    /// Assets/RemoteConfig/GameConfig.rc) and returns them alongside <see cref="GameProtocol.Version"/>
    /// for the client's handshake check.
    /// </summary>
    public class GetGameConfigFunctions
    {
        private const string BoardConfigsKey = "BOARD_CONFIGS";
        private const string AiDifficultyParamsKey = "AI_DIFFICULTY_PARAMS";

        private readonly ILogger<GetGameConfigFunctions> _logger;

        public GetGameConfigFunctions(ILogger<GetGameConfigFunctions> logger)
        {
            _logger = logger;
        }

        [CloudCodeFunction("GetGameConfig")]
        public async Task<GetGameConfigResponse> GetGameConfig(IExecutionContext context, IGameApiClient gameApiClient)
        {
            try
            {
                // NOTE for whoever deploys this: the Cloud Code C# SDK reference for
                // RemoteConfigSettings is not fully published as of this writing - verify this call's
                // exact signature/response shape against IntelliSense (or the generated bindings)
                // once the module is deployed, and adjust the parsing below if it differs.
                var settingsResult = await gameApiClient.RemoteConfigSettings.AssignSettingsGetAsync(
                    context,
                    context.AccessToken,
                    context.ProjectId,
                    context.EnvironmentId,
                    null,
                    new List<string> { BoardConfigsKey, AiDifficultyParamsKey });

                var settings = settingsResult.Data.Configs.Settings;

                var response = new GetGameConfigResponse
                {
                    Version = GameProtocol.Version,
                    BoardConfigs = ParseSetting<List<BoardConfigDto>>(settings, BoardConfigsKey) ?? new List<BoardConfigDto>(),
                    AiDifficultyParams = ParseSetting<List<AiDifficultyParamsDto>>(settings, AiDifficultyParamsKey) ?? new List<AiDifficultyParamsDto>(),
                    // Milestone 4: MATCHMAKING_CONFIG is read via the shared MatchmakingConfigReader
                    // (also used by MatchFunctions.GetMatchState for risk #4) rather than this
                    // function's own ParseSetting, so both call sites agree on defaults/parsing.
                    MatchmakingConfig = await MatchmakingConfigReader.GetAsync(context, gameApiClient),
                    // Milestone 5, design-doc.md section 6.1: RANKED_CONFIG, same "shared reader, never throws" posture as MatchmakingConfig above.
                    RankedConfig = await RankedConfigReader.GetAsync(context, gameApiClient),
                };

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetGameConfig: failed to read Remote Config settings.");
                throw new Exception($"Failed to load game config: {ex.Message}");
            }
        }

        private static T? ParseSetting<T>(IDictionary<string, object> settings, string key) where T : class
        {
            if (!settings.TryGetValue(key, out object? rawValue) || rawValue == null)
            {
                return null;
            }

            // The Remote Config settings dictionary typically surfaces JSON-type values as a
            // JsonElement (System.Text.Json) or as a raw string, depending on SDK version - handle
            // both so this keeps working across the Cloud Code Apis package's own JSON stack.
            string json = rawValue is JsonElement element ? element.GetRawText() : rawValue.ToString() ?? string.Empty;
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
    }
}
