using System.Globalization;
using System.Text.Json;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Model;

namespace TTTXO.CloudCode
{
    /// <summary>
    /// Day-partitioned index of online matches that are (or were, until proven otherwise) in
    /// progress - the PROACTIVE half of risk #4 (Docs/03-Arquitectura-UGS-TicTacToe.md#4. Partidas
    /// online abandonadas: "si el plan de UGS contratado incluye Scheduler/Cloud Code Triggers
    /// programados, se puede sumar además una resolución proactiva"). Cloud Save Custom Data has no
    /// "list every document" call reachable the simple way from this module's installed SDK (see
    /// README "Barrido proactivo de partidas abandonadas" for what was actually checked - a
    /// query/index feature exists in principle but would need its own Cloud Save index
    /// provisioning, an extra moving part this task chose not to take on unverified), so the module
    /// maintains its own lightweight index instead: one Custom Data document per UTC calendar day
    /// (<c>active-matches-{yyyy-MM-dd}</c>), holding the matchIds of every online match that has
    /// completed its 2-player roster and has not yet been observed terminal.
    ///
    /// The day is always computed from the match's OWN <see cref="MatchStateRecord.CreatedAtUnixSeconds"/>
    /// (<see cref="DayUtcFor"/>), used identically by both <see cref="AddMatchAsync"/> and
    /// <see cref="RemoveMatchAsync"/>, so the two can never disagree about which partition a given
    /// match lives in (no drift even across a midnight boundary between when a match was created and
    /// when it later gets added/removed).
    ///
    /// Partitioning by day bounds the size of any single document (never accumulates more than
    /// roughly one day's worth of concurrently-open online matches) and lets
    /// <see cref="MatchFunctions.SweepAbandonedMatches"/> self-prune stale entries opportunistically
    /// on every sweep instead of needing a separate cleanup job - see that method's prune-on-read
    /// strategy.
    ///
    /// Same "no Cloud Save transactions" caveat already documented for the handoff pointer (README
    /// "Limitaciones conocidas"): a read-modify-write race on the SAME day's document is possible if
    /// two matches are added/removed at the same instant, and the loser of the race is silently
    /// dropped from the index. This is an accepted, strictly-additive risk - a match that falls out
    /// of the index only loses the PROACTIVE sweep, never the reactive resolution already covered by
    /// <see cref="MatchFunctions.GetMatchState"/>, so the worst case is identical to this module's
    /// behaviour before this index existed.
    /// </summary>
    public static class MatchIndexStore
    {
        private const string IndexItemKey = "index";

        private static string DayPartitionDocId(string dayUtc) => $"active-matches-{dayUtc}";

        /// <summary>UTC calendar day a match belongs to in the index, computed once from its own <see cref="MatchStateRecord.CreatedAtUnixSeconds"/> - see class remarks for why this (and never "now") is used at both add- and remove-time.</summary>
        public static string DayUtcFor(long createdAtUnixSeconds) =>
            DateTimeOffset.FromUnixTimeSeconds(createdAtUnixSeconds).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>
        /// Adds <paramref name="record"/>'s matchId to its day partition. Called once, from
        /// <see cref="MatchFunctions.CompleteOnlineMatchAsync"/>, exactly when the 2-player roster
        /// completes - a still-1-player "waiting for opponent" match is intentionally never indexed:
        /// there is no rival yet for "both players abandoned" to mean anything, and
        /// <see cref="MatchFunctions.PlayMove"/> already rejects moves on it regardless.
        /// </summary>
        public static async Task AddMatchAsync(IExecutionContext context, IGameApiClient gameApiClient, MatchStateRecord record)
        {
            string dayUtc = DayUtcFor(record.CreatedAtUnixSeconds);
            var index = await GetIndexAsync(context, gameApiClient, dayUtc);
            if (!index.MatchIds.Contains(record.MatchId))
            {
                index.MatchIds.Add(record.MatchId);
                await SaveIndexAsync(context, gameApiClient, dayUtc, index);
            }
        }

        /// <summary>
        /// Removes <paramref name="record"/>'s matchId from its day partition once the match reaches
        /// ANY terminal state - a real move finishing it (<see cref="MatchFunctions.PlayMove"/>), a
        /// reactive abandonment win (<see cref="MatchFunctions.GetMatchState"/>), or a proactive
        /// sweep resolution/prune (<see cref="MatchFunctions.SweepAbandonedMatches"/>). Idempotent:
        /// removing a matchId that is not present (e.g. already pruned by a sweep that ran between
        /// this caller's load and save) is a silent no-op, no extra write.
        /// </summary>
        public static async Task RemoveMatchAsync(IExecutionContext context, IGameApiClient gameApiClient, MatchStateRecord record)
        {
            string dayUtc = DayUtcFor(record.CreatedAtUnixSeconds);
            var index = await GetIndexAsync(context, gameApiClient, dayUtc);
            if (index.MatchIds.Remove(record.MatchId))
            {
                await SaveIndexAsync(context, gameApiClient, dayUtc, index);
            }
        }

        public static async Task<ActiveMatchIndexRecord> GetIndexAsync(IExecutionContext context, IGameApiClient gameApiClient, string dayUtc)
        {
            var result = await gameApiClient.CloudSaveData.GetCustomItemsAsync(
                context, context.ServiceToken, context.ProjectId, DayPartitionDocId(dayUtc));
            var item = result.Data.Results.FirstOrDefault(i => i.Key == IndexItemKey);
            if (item == null)
            {
                return new ActiveMatchIndexRecord();
            }

            string json = item.Value is JsonElement element ? element.GetRawText() : item.Value?.ToString() ?? string.Empty;
            return JsonSerializer.Deserialize<ActiveMatchIndexRecord>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new ActiveMatchIndexRecord();
        }

        /// <summary>Overwrites the whole day partition with <paramref name="index"/> - callers (Add/RemoveMatchAsync, MatchFunctions.SweepAbandonedMatches) always read-modify-write the full list, never a per-matchId patch, to keep this store's contract to a single Cloud Save item per day.</summary>
        public static Task SaveIndexAsync(IExecutionContext context, IGameApiClient gameApiClient, string dayUtc, ActiveMatchIndexRecord index)
        {
            return gameApiClient.CloudSaveData.SetCustomItemAsync(
                context, context.ServiceToken, context.ProjectId, DayPartitionDocId(dayUtc), new SetItemBody(IndexItemKey, index));
        }
    }
}
