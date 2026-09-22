using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudCode;
using UnityEngine;

namespace TTTXO.Game.Services
{
    /// <summary>Mirrors one entry of the future Remote Config <c>BOARD_CONFIGS</c> key (design-doc.md section 1).</summary>
    [Serializable]
    public class BoardConfigDto
    {
        public int Size;
        public int WinLength;
        public string[] Modes;
    }

    /// <summary>
    /// Mirrors one (difficulty, boardSize) row of the future Remote Config
    /// <c>AI_DIFFICULTY_PARAMS</c> key (design-doc.md section 3). Adaptive is intentionally not
    /// represented here - its parameters are interpolated client-side (and, later, server-side) by
    /// <see cref="TTTXO.Core.AdaptiveAiController"/> from a continuous level, not looked up by name.
    /// </summary>
    [Serializable]
    public class AiDifficultyParamsDto
    {
        public string Difficulty;
        public int BoardSize;
        public double WinProbability;
        public double BlockProbability;
        public double BlunderChance;
        public int SearchDepth;
    }

    /// <summary>
    /// Mirrors the future Remote Config <c>MATCHMAKING_CONFIG</c> key (Milestone 4). Kept 1:1 with
    /// the server-side <c>MatchmakingConfigDto</c> in CloudCode~/TicTacToeModule/Dtos.cs.
    /// </summary>
    [Serializable]
    public class MatchmakingConfigDto
    {
        public int AbandonTimeoutMinutes = 3;
        public int WaitCeilingSeconds = 45;
        public int TicketTtlSeconds = 180;
    }

    /// <summary>
    /// Mirrors the Remote Config <c>RANKED_CONFIG</c> key (Milestone 5, design-doc.md section 6.1).
    /// Kept 1:1 with the server-side <c>RankedConfigDto</c> in CloudCode~/TicTacToeModule/Dtos.cs -
    /// every number here already lived server-side before this milestone's client work; the client
    /// only needs a handful of them today (<see cref="InitialMmr"/> as the offline-fallback default
    /// for a brand-new player - see <see cref="RankedProfileCache"/> - and the two turn timeouts for a
    /// future turn-timer UI), the rest is mirrored anyway to keep this DTO a complete, auditable copy
    /// of the server contract rather than a hand-picked subset that silently drifts from it.
    /// </summary>
    [Serializable]
    public class RankedConfigDto
    {
        public int InitialMmr = 1000;
        public int MmrFloor = 500;
        public int KPlacement = 40;
        public int KIntermediate = 32;
        public int KEstablished = 24;
        public int KHighMmr = 16;
        public int KHighMmrThreshold = 1600;
        public int PlacementMatches = 5;
        public int FirstMoveEloBoard6 = 50;
        public double[] RepeatRivalDamping = { 1.0, 1.0, 0.5, 0.5, 0.0 };
        public int TurnTimeoutSecondsBoard3 = 20;
        public int TurnTimeoutSecondsBoard6 = 30;
        public int RewardWinBoard3 = 10;
        public int RewardDrawBoard3 = 4;
        public int RewardWinBoard6 = 15;
        public int RewardDrawBoard6 = 5;
        public int DecayProtectedFloor = 1200;
        public int DecayGraceDays = 7;
        public int DecayPerDay = 25;
        public long SeasonStartUnixSeconds;
        public int SeasonDurationMonths = 1;
        public string[] TierNames = { "bronze", "silver", "gold", "platinum", "diamond" };
        public int[] TierMinMmr = { 0, 900, 1100, 1300, 1500 };
        public int[] TierSoftCurrency = { 100, 200, 400, 700, 1200 };
        public int Top100BonusSoftCurrency = 500;
    }

    /// <summary>Response contract of Cloud Code's <c>GetGameConfig</c>. Kept 1:1 with the server-side DTO in CloudCode~/TicTacToeModule.</summary>
    [Serializable]
    public class GetGameConfigResponse
    {
        public int Version;
        public List<BoardConfigDto> BoardConfigs;
        public List<AiDifficultyParamsDto> AiDifficultyParams;
        public MatchmakingConfigDto MatchmakingConfig;
        public RankedConfigDto RankedConfig;
    }

    /// <summary>
    /// Loads game config from Cloud Code's single <c>GetGameConfig</c> endpoint at session start
    /// (Docs/03-Arquitectura-UGS-TicTacToe.md#Remote Config: "el cliente no lee Remote Config
    /// directo... Cloud Code expone un unico endpoint (GetGameConfig)"), with a **local fallback**
    /// built from <see cref="TTTXO.Core"/>'s own hardcoded Milestone 1 values whenever Cloud Code is
    /// unreachable (not deployed yet, no network, session not Ready) - the game must stay 100%
    /// playable offline (Docs/03-Arquitectura-UGS-TicTacToe.md / design-doc.md Milestone 1 scope).
    ///
    /// Milestone 2 scope: this service only loads and exposes the parsed config. Gameplay screens
    /// keep reading <see cref="TTTXO.Core.BoardConfig"/> / <see cref="TTTXO.Core.AiParams"/> directly
    /// for now (that is Milestone 1's already-shipped, tested local logic) - wiring gameplay to
    /// consume this service's values instead is a later milestone's job.
    /// </summary>
    public static class GameConfigService
    {
        private const string ModuleName = "TicTacToeModule";
        private const string FunctionName = "GetGameConfig";

        /// <summary>
        /// Board sizes offered to players at launch.
        ///
        /// 9x9 and 11x11 stay in <see cref="TTTXO.Core.BoardConfig"/> - their rules, rewards and AI
        /// params are all still valid and still deployed - but are not offered: on a 360dp-wide phone
        /// an 11x11 cell measures ~29dp against Android's 48dp minimum tap target, and the ceiling is
        /// arithmetic rather than a layout problem (Docs/09-Encuadre-Dispositivos.md, L-05). Deferred
        /// to post-launch, which is why this is a filter here and not a deletion in Core: bringing
        /// them back is this list plus the Remote Config entries, with no other code to touch.
        ///
        /// Must stay in step with Assets/RemoteConfig/GameConfig.rc's BOARD_CONFIGS, or an offline
        /// player is offered a different set of boards than an online one - which is exactly what
        /// LocalFallback_MirrorsTheDeployedRemoteConfigBoards fails on.
        ///
        /// This gates what is *offered*. The server enforces the same rule independently in
        /// CloudCode~/TicTacToeModule/BoardAvailability.cs, which reads the same BOARD_CONFIGS and
        /// rejects a create for a size the config does not list for that mode - so a modified client
        /// cannot reach a cut board online. Local play is client-side by design (Milestone 1) and is
        /// not covered by that gate, nor by anything else: its rewards already run on the same
        /// client-writable path ERR-KB-006 describes.
        /// </summary>
        internal static readonly int[] ShippedBoardSizes = { 3, 6 };

        public static bool IsLoaded { get; private set; }

        public static bool IsUsingLocalFallback { get; private set; } = true;

        public static int ServerVersion { get; private set; } = GameProtocol.Version;

        public static IReadOnlyList<BoardConfigDto> BoardConfigs { get; private set; } = Array.Empty<BoardConfigDto>();

        public static IReadOnlyList<AiDifficultyParamsDto> AiDifficultyParamsList { get; private set; } = Array.Empty<AiDifficultyParamsDto>();

        /// <summary>Milestone 4 matchmaking tuning (abandon timeout, wait ceiling) - defaults to <see cref="MatchmakingConfigDto"/>'s own defaults until/unless <see cref="InitializeAsync"/> loads a server value.</summary>
        public static MatchmakingConfigDto MatchmakingConfig { get; private set; } = new();

        /// <summary>Milestone 5 Ranked tuning (design-doc.md section 6.1) - defaults to <see cref="RankedConfigDto"/>'s own defaults (the exact values ratified in the design doc) until/unless <see cref="InitializeAsync"/> loads a server value.</summary>
        public static RankedConfigDto RankedConfig { get; private set; } = new();

        /// <summary>
        /// Fetches config from Cloud Code, falling back to local values on any failure. Never throws.
        /// Safe to call once per session start (see SplashScreenController); calling again re-fetches.
        /// </summary>
        public static async Task InitializeAsync()
        {
            if (UgsInitializer.Status != UgsInitStatus.Ready)
            {
                ApplyLocalFallback();
                return;
            }

            try
            {
                var response = await CloudCodeService.Instance.CallModuleEndpointAsync<GetGameConfigResponse>(ModuleName, FunctionName);
                ApplyServerResponse(response);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"GameConfigService: GetGameConfig failed, using local fallback - {ex.Message}");
                ApplyLocalFallback();
            }
        }

        /// <summary>
        /// Applies a <c>GetGameConfig</c> response to the exposed state. Split from the fetch in
        /// <see cref="InitializeAsync"/> so the handling can be exercised without a live Cloud Code
        /// session - the response shapes worth checking (incomplete, partial, version mismatch) are
        /// exactly the ones a happy-path integration run never produces.
        ///
        /// Throws on an incomplete response rather than salvaging it: the caller's catch turns that
        /// into the local fallback, which is a coherent config, whereas half a server response is not.
        /// </summary>
        internal static void ApplyServerResponse(GetGameConfigResponse response)
        {
            if (response?.BoardConfigs == null || response.AiDifficultyParams == null)
            {
                throw new InvalidOperationException("GetGameConfig returned an incomplete response.");
            }

            BoardConfigs = response.BoardConfigs;
            AiDifficultyParamsList = response.AiDifficultyParams;
            MatchmakingConfig = response.MatchmakingConfig ?? new MatchmakingConfigDto();
            RankedConfig = response.RankedConfig ?? new RankedConfigDto();
            ServerVersion = response.Version;
            IsUsingLocalFallback = false;
            IsLoaded = true;

            if (response.Version != GameProtocol.Version)
            {
                // Handshake per Docs/01-Directrices-Proyecto.md: warn only in Milestone 2, never
                // block play - blocking on mismatch is a later-milestone decision (real IAP money
                // is what eventually makes this non-negotiable).
                Debug.LogWarning(
                    $"GameConfigService: client GameProtocol.Version ({GameProtocol.Version}) != " +
                    $"server version ({response.Version}). Update the client or redeploy Cloud Code.");
            }
        }

        internal static void ApplyLocalFallback()
        {
            var boards = new List<BoardConfigDto>();
            foreach (var board in TTTXO.Core.BoardConfig.All)
            {
                // Core still knows 9x9 and 11x11; players are not offered them at launch. See
                // ShippedBoardSizes.
                if (Array.IndexOf(ShippedBoardSizes, board.Size) < 0)
                {
                    continue;
                }

                boards.Add(new BoardConfigDto
                {
                    Size = board.Size,
                    WinLength = board.WinLength,
                    // Every local mode plus online_quickmatch (Milestone 4: the quickmatch-pool
                    // accepts any board size, per Docs/03-Arquitectura-UGS-TicTacToe.md#Matchmaker -
                    // "sin restriccion de habilidad, cualquier tamano de tablero"), plus "ranked"
                    // (Milestone 5) for 3x3/6x6 only, mirroring Assets/RemoteConfig/GameConfig.rc's
                    // deployed BOARD_CONFIGS (design-doc.md section 6.0: "Ranked es 3x3 y 6x6
                    // unicamente al lanzamiento").
                    Modes = board.Size == 3 || board.Size == 6
                        ? new[]
                        {
                            GameAnalytics.ModeCode(TTTXO.Core.GameMode.SinglePlayer),
                            GameAnalytics.ModeCode(TTTXO.Core.GameMode.LocalMultiplayer),
                            GameAnalytics.OnlineQuickmatchModeCode,
                            GameAnalytics.RankedModeCode,
                        }
                        : new[]
                        {
                            GameAnalytics.ModeCode(TTTXO.Core.GameMode.SinglePlayer),
                            GameAnalytics.ModeCode(TTTXO.Core.GameMode.LocalMultiplayer),
                            GameAnalytics.OnlineQuickmatchModeCode,
                        },
                });
            }

            var aiParams = new List<AiDifficultyParamsDto>();
            TTTXO.Core.AiDifficulty[] namedDifficulties =
            {
                TTTXO.Core.AiDifficulty.Easy, TTTXO.Core.AiDifficulty.Medium, TTTXO.Core.AiDifficulty.Hard
            };

            foreach (var difficulty in namedDifficulties)
            {
                foreach (var board in TTTXO.Core.BoardConfig.All)
                {
                    var p = TTTXO.Core.AiParams.ForDifficulty(difficulty, board.Size);
                    aiParams.Add(new AiDifficultyParamsDto
                    {
                        Difficulty = GameAnalytics.DifficultyCode(difficulty),
                        BoardSize = board.Size,
                        WinProbability = p.WinProbability,
                        BlockProbability = p.BlockProbability,
                        BlunderChance = p.BlunderChance,
                        SearchDepth = p.SearchDepth,
                    });
                }
            }

            BoardConfigs = boards;
            AiDifficultyParamsList = aiParams;
            MatchmakingConfig = new MatchmakingConfigDto();
            RankedConfig = new RankedConfigDto();
            ServerVersion = GameProtocol.Version;
            IsUsingLocalFallback = true;
            IsLoaded = true;
        }
    }
}
