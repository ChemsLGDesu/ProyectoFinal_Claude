using System.Text.Json;
using TTTXO.Core;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Model;

namespace TTTXO.CloudCode
{
    /// <summary>
    /// Cloud Save Custom Data I/O (key = seriesId) for a 3x3 Ranked series
    /// (<see cref="RankedSeriesRecord"/>), plus the one piece of game-creation logic that is
    /// genuinely series-specific: assigning X/O for game 1 (same lexicographic rule as Quickmatch)
    /// and SWAPPING it for game 2 (design-doc.md section 6.1: "en la partida 1 empieza uno; en la
    /// partida 2, el otro. Cada jugador empieza exactamente una vez"). Everything else about a
    /// series game is a completely ordinary <see cref="MatchStateRecord"/>, created/persisted the
    /// exact same way <see cref="MatchFunctions.CompleteOnlineMatchAsync"/> already does for
    /// Quickmatch - see CloudCode~/TicTacToeModule/README.md "Serie de 2 partidas (Ranked 3x3)".
    /// </summary>
    public static class RankedSeriesStore
    {
        private const string SeriesItemKey = "series";

        public static async Task<RankedSeriesRecord> LoadAsync(IExecutionContext context, IGameApiClient gameApiClient, string seriesId)
        {
            var result = await gameApiClient.CloudSaveData.GetCustomItemsAsync(context, context.ServiceToken, context.ProjectId, seriesId);
            var item = result.Data.Results.FirstOrDefault(i => i.Key == SeriesItemKey);
            if (item == null)
            {
                throw new Exception($"Ranked series '{seriesId}' was not found.");
            }

            string json = item.Value is JsonElement element ? element.GetRawText() : item.Value?.ToString() ?? string.Empty;
            var record = JsonSerializer.Deserialize<RankedSeriesRecord>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (record == null)
            {
                throw new Exception($"Ranked series '{seriesId}' state could not be parsed.");
            }

            return record;
        }

        public static Task SaveAsync(IExecutionContext context, IGameApiClient gameApiClient, RankedSeriesRecord record)
        {
            return gameApiClient.CloudSaveData.SetCustomItemAsync(context, context.ServiceToken, context.ProjectId, record.SeriesId, new SetItemBody(SeriesItemKey, record));
        }

        /// <summary>
        /// First caller for a Ranked 3x3 handoffId: creates a 1-player "waiting" series (mirrors
        /// <see cref="MatchFunctions.CreatePendingOnlineMatchAsync"/>, but for a series instead of a
        /// single match - no <see cref="MatchStateRecord"/> exists yet, there is nothing to index).
        /// Publishes the SAME handoff-pointer doc Quickmatch/6x6-Ranked already use (via
        /// <see cref="MatchStateStore.SaveHandoffPointerAsync"/>, which is mode-agnostic about what
        /// string it points to), pointing at this series' id instead of a matchId.
        /// </summary>
        public static async Task<RankedSeriesRecord> CreatePendingSeriesAsync(IExecutionContext context, IGameApiClient gameApiClient, string handoffId, string firstPlayerId)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var series = new RankedSeriesRecord
            {
                SeriesId = Guid.NewGuid().ToString("N"),
                BoardSize = 3,
                HandoffId = handoffId,
                PlayerIds = new List<string> { firstPlayerId },
                CreatedAtUnixSeconds = now,
                LastActivityAtUnixSeconds = now,
            };

            await SaveAsync(context, gameApiClient, series);
            await MatchStateStore.SaveHandoffPointerAsync(context, gameApiClient, handoffId, series.SeriesId);
            return series;
        }

        /// <summary>
        /// Second caller: completes the 2-player roster, snapshots both players' current MMR
        /// (collapsing any pending decay/season-rollover first - see <see cref="RankedProfileStore.TouchAsync"/>,
        /// so the series never starts from a stale MMR), and creates game 1. Returns the game 1
        /// <see cref="MatchStateRecord"/> (already saved and indexed) - the caller hands its DTO
        /// straight back to the client, exactly like <see cref="MatchFunctions.CompleteOnlineMatchAsync"/>
        /// does for a plain online match.
        /// </summary>
        public static async Task<MatchStateRecord> CompleteRosterAndCreateGame1Async(
            IExecutionContext context, IGameApiClient gameApiClient, RankedSeriesRecord series, RankedConfigDto rankedConfig, BoardConfig boardConfig)
        {
            series.PlayerIds.Add(context.PlayerId);

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (string playerId in series.PlayerIds)
            {
                var touch = await RankedProfileStore.TouchAsync(context, gameApiClient, playerId, boardConfig.Size, rankedConfig, now);
                series.MmrBeforeByPlayerId[playerId] = touch.Record.Mmr;
                // A pending season reward collapsed here (roster just completed, before any game was
                // played) is still granted normally - RankedProfileStore.TouchAsync already persisted
                // it to rankedProfile; MatchFunctions grants the currency/analytics side-effects the
                // next time this player's client calls a Ranked function that surfaces it (see
                // MatchFunctions.GrantPendingSeasonRewardAsync). Nothing further to do here.
            }

            string[] ordered = { series.PlayerIds[0], series.PlayerIds[1] };
            Array.Sort(ordered, StringComparer.Ordinal);

            var game1 = new MatchStateRecord
            {
                MatchId = Guid.NewGuid().ToString("N"),
                BoardSize = boardConfig.Size,
                WinLength = boardConfig.WinLength,
                Mode = "ranked",
                FirstPlayer = "X",
                CreatedAtUnixSeconds = now,
                LastActivityAtUnixSeconds = now,
                TurnStartedAtUnixSeconds = now,
                RankedSeriesId = series.SeriesId,
                RankedSeriesGameIndex = 1,
                RankedMmrBeforeByPlayerId = new Dictionary<string, int>(series.MmrBeforeByPlayerId),
                Players = new List<MatchPlayerRecord>
                {
                    new() { PlayerId = ordered[0], Symbol = "X", LastActivityAtUnixSeconds = now },
                    new() { PlayerId = ordered[1], Symbol = "O", LastActivityAtUnixSeconds = now },
                },
            };

            await MatchStateStore.SaveAsync(context, gameApiClient, game1);
            await MatchIndexStore.AddMatchAsync(context, gameApiClient, game1);

            series.MatchIds.Add(game1.MatchId);
            series.CurrentGameIndex = 1;
            series.LastActivityAtUnixSeconds = now;
            await SaveAsync(context, gameApiClient, series);

            return game1;
        }

        /// <summary>
        /// Creates game 2 right when game 1 ends (called from <see cref="MatchFunctions.PlayMove"/>'s
        /// own invocation, no new Matchmaker ticket - both players are already known). Symbols are
        /// SWAPPED from game 1 (design-doc.md section 6.1) - read directly off <paramref name="game1"/>'s
        /// own roster rather than re-deriving, so this can never disagree with what game 1 actually
        /// used.
        /// </summary>
        public static async Task<MatchStateRecord> CreateGame2Async(
            IExecutionContext context, IGameApiClient gameApiClient, RankedSeriesRecord series, MatchStateRecord game1, BoardConfig boardConfig)
        {
            var game1X = game1.Players.First(p => p.Symbol == "X");
            var game1O = game1.Players.First(p => p.Symbol == "O");

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var game2 = new MatchStateRecord
            {
                MatchId = Guid.NewGuid().ToString("N"),
                BoardSize = boardConfig.Size,
                WinLength = boardConfig.WinLength,
                Mode = "ranked",
                FirstPlayer = "X",
                CreatedAtUnixSeconds = now,
                LastActivityAtUnixSeconds = now,
                TurnStartedAtUnixSeconds = now,
                RankedSeriesId = series.SeriesId,
                RankedSeriesGameIndex = 2,
                RankedMmrBeforeByPlayerId = new Dictionary<string, int>(series.MmrBeforeByPlayerId),
                Players = new List<MatchPlayerRecord>
                {
                    // Swapped: game 1's O becomes game 2's X (moves first this time) and vice versa.
                    new() { PlayerId = game1O.PlayerId, Symbol = "X", LastActivityAtUnixSeconds = now },
                    new() { PlayerId = game1X.PlayerId, Symbol = "O", LastActivityAtUnixSeconds = now },
                },
            };

            await MatchStateStore.SaveAsync(context, gameApiClient, game2);
            await MatchIndexStore.AddMatchAsync(context, gameApiClient, game2);

            series.MatchIds.Add(game2.MatchId);
            series.CurrentGameIndex = 2;
            series.LastActivityAtUnixSeconds = now;
            await SaveAsync(context, gameApiClient, series);

            return game2;
        }
    }
}
