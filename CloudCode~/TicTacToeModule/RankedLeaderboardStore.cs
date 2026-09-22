using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.Leaderboards.Model;

namespace TTTXO.CloudCode
{
    /// <summary>
    /// Thin wrapper around <see cref="IGameApiClient.Leaderboards"/> (Milestone 5, design-doc.md
    /// section 6.6). Two leaderboards, one per board size (see Assets/Leaderboards/ranked_3x3.lb /
    /// ranked_6x6.lb) - NOT one per season/id-suffix: design-doc.md section 6.8 Q5 confirmed (via
    /// SDK reflection + <c>https://docs.unity.com/en-us/leaderboards/concepts/resets.md</c>) that
    /// Leaderboards has NATIVE scheduled resets with archiving (<c>ResetConfig</c>), so a season
    /// rollover is the SAME leaderboard resetting on schedule, with the just-closed season readable
    /// forever afterwards as an archived version (<c>GetLeaderboardVersionScoresAsync</c>) - no
    /// separate <c>ranked_3x3_s{N}</c> ids needed.
    ///
    /// <c>UpdateType = keepLatest</c> is REQUIRED on both leaderboards (not the default-ish
    /// <c>keepBest</c>): MMR must be able to go DOWN (a loss, a decay tick, a season soft-reset) and
    /// still be reflected - "el puntaje publicado es el MMR crudo" (design-doc.md section 6.6), not
    /// a lifetime high-water mark. See Assets/Leaderboards/ranked_3x3.lb.
    ///
    /// Every call here uses <c>context.ServiceToken</c> (confirmed cross-player-write-capable for
    /// Leaderboards' Client API by <c>https://docs.unity.com/en-us/cloud-code/scripts/how-to-guides/token-support.md</c>'s
    /// "Service token support" table, which lists Leaderboards under "UGS Client APIs" - same
    /// pattern already used for Matchmaker/Cloud Save elsewhere in this module) since a Ranked
    /// settlement publishes BOTH matched players' scores from a single caller's Cloud Code
    /// invocation, exactly like <see cref="OnlineRewardStore"/> credits both players' currency.
    ///
    /// Model types verified against the installed Com.Unity.Services.CloudCode.Apis 0.0.26 assembly
    /// (MetadataLoadContext reflection, same method as this module's other SDK verifications): the
    /// score-write parameter type is <see cref="AddLeaderboardScore"/> (NOT <c>LeaderboardScore</c>,
    /// which the current public C# sample at <c>access-cloud-code-csharp.md</c> uses - that sample
    /// appears to target a different/older SDK build than what this project has installed; the type
    /// actually present in 0.0.26 is <c>AddLeaderboardScore</c>, same shape - Score/Metadata/VersionId).
    /// </summary>
    public static class RankedLeaderboardStore
    {
        public const string Board3LeaderboardId = "ranked_3x3";
        public const string Board6LeaderboardId = "ranked_6x6";

        public static string LeaderboardIdFor(int boardSize) => boardSize == 3 ? Board3LeaderboardId : Board6LeaderboardId;

        public static Task SubmitScoreAsync(IExecutionContext context, IGameApiClient gameApiClient, int boardSize, string playerId, int mmr)
        {
            return gameApiClient.Leaderboards.AddLeaderboardPlayerScoreAsync(
                context, context.ServiceToken, Guid.Parse(context.ProjectId), LeaderboardIdFor(boardSize), playerId,
                new AddLeaderboardScore { Score = mmr });
        }

        /// <summary>Top N entries of the LIVE leaderboard (wireframe 10's Leaderboard screen).</summary>
        public static async Task<List<LeaderboardEntry>> GetTopAsync(IExecutionContext context, IGameApiClient gameApiClient, int boardSize, int limit)
        {
            var result = await gameApiClient.Leaderboards.GetLeaderboardScoresAsync(
                context, context.ServiceToken, Guid.Parse(context.ProjectId), LeaderboardIdFor(boardSize), includeMetadata: null, offset: 0, limit: limit);
            return result.Data.Results;
        }

        /// <summary>Top N of a specific archived version (design-doc.md section 6.5's Top 100 season-close bonus - the season already closed and the live leaderboard already reset by the time a given player's lazy rollover runs, so this reads the frozen snapshot, not the live one).</summary>
        public static async Task<List<LeaderboardEntry>> GetArchivedTopAsync(IExecutionContext context, IGameApiClient gameApiClient, int boardSize, string versionId, int limit)
        {
            var result = await gameApiClient.Leaderboards.GetLeaderboardVersionScoresAsync(
                context, context.ServiceToken, Guid.Parse(context.ProjectId), LeaderboardIdFor(boardSize), versionId, includeMetadata: null, offset: 0, limit: limit);
            return result.Data.Results;
        }

        /// <summary>
        /// Archived version id (design-doc.md section 6.5's Top 100 bonus) of the season that most
        /// recently closed for <paramref name="boardSize"/> - the one whose archive window ended
        /// most recently (explicitly ordered by <see cref="LeaderboardVersion.Start"/> here rather
        /// than trusting an assumed API ordering of <c>Results</c>, since neither the reflected
        /// signature nor the docs page fetched for this task document that ordering).
        /// </summary>
        public static async Task<string> GetMostRecentArchivedVersionIdAsync(IExecutionContext context, IGameApiClient gameApiClient, int boardSize)
        {
            var result = await gameApiClient.Leaderboards.GetLeaderboardVersionsAsync(
                context, context.ServiceToken, Guid.Parse(context.ProjectId), LeaderboardIdFor(boardSize), limit: 50);
            var versions = result.Data.Results;
            if (versions == null || versions.Count == 0)
            {
                return null;
            }

            LeaderboardVersion mostRecent = null;
            foreach (var version in versions)
            {
                if (mostRecent == null || version.Start > mostRecent.Start)
                {
                    mostRecent = version;
                }
            }

            return mostRecent?.Id;
        }

        /// <summary>Own rank/score, for a player already past placement (wireframe 10 "fila propia destacada" / Perfil's MMR display) - null if the player has no entry yet (not placement-eligible; the Client API 404s in that case).</summary>
        public static async Task<LeaderboardEntryWithUpdatedTime> GetOwnEntryAsync(IExecutionContext context, IGameApiClient gameApiClient, int boardSize, string playerId)
        {
            try
            {
                var result = await gameApiClient.Leaderboards.GetLeaderboardPlayerScoreAsync(
                    context, context.ServiceToken, Guid.Parse(context.ProjectId), LeaderboardIdFor(boardSize), playerId);
                return result.Data;
            }
            catch
            {
                return null;
            }
        }
    }
}
