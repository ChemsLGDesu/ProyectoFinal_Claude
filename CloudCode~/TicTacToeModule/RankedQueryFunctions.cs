using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace TTTXO.CloudCode
{
    /// <summary>
    /// Read-only Ranked query endpoints (Milestone 5, design-doc.md section 6): <c>GetRankedProfile</c>
    /// (wireframe 8, Perfil) and <c>GetRankedLeaderboard</c> (wireframe 10, Leaderboard). These close
    /// the two client-facing blockers listed in this file's own README ("Lo que esta tarea NO
    /// implementó": "una función GetRankedProfile para leer MMR/tier/decay-warning sin jugar" and the
    /// Leaderboard screen's "no hay Cloud Code function que lea el leaderboard").
    ///
    /// Neither function ever settles/mutates a match, series, or MMR - both are pure reads layered on
    /// top of the exact same primitives <see cref="MatchFunctions"/>/<see cref="RankedMatchSupport"/>
    /// already use to settle a Ranked unit (<see cref="RankedProfileStore.TouchAsync"/>,
    /// <see cref="RankedTierCalculator"/>, <see cref="RankedSeasonCalculator"/>,
    /// <see cref="RankedLeaderboardStore"/>), so the numbers returned here can never disagree with
    /// what a real match would settle to - this file has zero Ranked math/logic of its own.
    /// </summary>
    public class RankedQueryFunctions
    {
        /// <summary>Only board sizes Ranked supports (design-doc.md section 6.0: "Ranked es 3x3 y 6x6 unicamente al lanzamiento") - the single source both functions below iterate/validate against.</summary>
        internal static readonly int[] RankedBoardSizes = { 3, 6 };

        /// <summary><c>GetRankedLeaderboard</c>'s default top-N size (wireframe 10: "Top N... p.ej. 25-50") when the caller does not pass <c>limit</c>.</summary>
        private const int DefaultLeaderboardLimit = 25;

        /// <summary>Hard ceiling on <c>limit</c> regardless of what a caller requests - same defense-in-depth posture as <see cref="MatchFunctions.SweepAbandonedMatches"/>'s <c>maxMatches</c> cap.</summary>
        private const int MaxLeaderboardLimit = 50;

        private readonly ILogger<RankedQueryFunctions> _logger;

        public RankedQueryFunctions(ILogger<RankedQueryFunctions> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// The CALLING player's own authoritative Ranked status (design-doc.md section 6, wireframe 8's
        /// MMR/tier/placement stat) for every Ranked-enabled board size, plus current season context.
        /// Only ever reads/collapses <c>context.PlayerId</c>'s own record - never another player's (see
        /// every <see cref="RankedProfileStore.TouchAsync"/> call below, always passed
        /// <c>context.PlayerId</c>, matching this task's "nunca de otro jugador" requirement).
        ///
        /// Cheap by design, per this task's "debe ser barato de llamar" requirement: for each of the 2
        /// Ranked board sizes, this is a single <see cref="RankedProfileStore.TouchAsync"/> call (one
        /// Cloud Save Player Data read, with a write only on the rare tick where decay/season-rollover
        /// was actually pending - see that method's own remarks on why it is safe to call often) - no
        /// Leaderboards or Matchmaker calls at all. Safe to call on every Profile screen open and
        /// before every Ranked matchmaking search (see this task's <c>MatchmakingService</c> change).
        ///
        /// Reuses <see cref="RankedProfileStore.TouchAsync"/> - the SAME lazy decay/season-rollover
        /// path <c>PlayMove</c>/<c>GetMatchState</c> already use - rather than a raw
        /// <c>RankedProfileStore.LoadAsync</c>, so a player who has not played in a while sees their
        /// REAL current MMR here (post-decay, post soft-reset), never a stale pre-decay number
        /// (design-doc.md section 6.8 Q8/Q9). A season-close reward that becomes due exactly on this
        /// touch (e.g. the player's first action after a season boundary is opening Profile, not
        /// playing a match) is granted here too, via the same
        /// <see cref="RankedMatchSupport.GrantPendingSeasonRewardIfAnyAsync"/> every other Ranked
        /// settlement path already calls - otherwise that reward would be silently lost, since
        /// <see cref="RankedProfileStore.TouchAsync"/> always advances <c>SeasonId</c> immediately even
        /// if nobody ever collects the currency for the season that just closed.
        /// </summary>
        [CloudCodeFunction("GetRankedProfile")]
        public async Task<RankedProfileResponse> GetRankedProfile(IExecutionContext context, IGameApiClient gameApiClient)
        {
            try
            {
                var rankedConfig = await RankedConfigReader.GetAsync(context, gameApiClient);
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                int seasonId = RankedSeasonCalculator.CurrentSeasonId(rankedConfig, now);

                var response = new RankedProfileResponse
                {
                    Season = seasonId,
                    SeasonEndUnixSeconds = RankedSeasonCalculator.SeasonEndUnixSeconds(rankedConfig, seasonId),
                };

                foreach (int boardSize in RankedBoardSizes)
                {
                    var touch = await RankedProfileStore.TouchAsync(context, gameApiClient, context.PlayerId, boardSize, rankedConfig, now);
                    await RankedMatchSupport.GrantPendingSeasonRewardIfAnyAsync(context, gameApiClient, context.PlayerId, boardSize, touch, rankedConfig);

                    response.Boards.Add(new RankedBoardProfileDto
                    {
                        BoardSize = boardSize,
                        Mmr = touch.Record.Mmr,
                        PlacementsPlayed = Math.Min(touch.Record.PlacementsPlayed, rankedConfig.PlacementMatches),
                        PlacementMatchesRequired = rankedConfig.PlacementMatches,
                        IsPlaced = touch.Record.PlacementsPlayed >= rankedConfig.PlacementMatches,
                        Tier = RankedTierCalculator.TierFor(rankedConfig, touch.Record.Mmr),
                    });
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetRankedProfile failed for playerId={PlayerId}.", context.PlayerId);
                throw new Exception($"Failed to get ranked profile: {ex.Message}");
            }
        }

        /// <summary>
        /// Top N of one Ranked board's LIVE leaderboard plus the caller's own row (wireframe 10: "Top N
        /// en cards + fila propia destacada con posicion y MMR"). Reading through Cloud Code (rather
        /// than exposing <see cref="IGameApiClient.Leaderboards"/> to the client) keeps the leaderboard
        /// a single, auditable server-side surface and lets this function fold in "does the caller even
        /// have an entry yet" in one round trip - writing stays exclusively server-side as it already
        /// is (Docs/01-Directrices-Proyecto.md#Seguridad / anti-cheat), this endpoint only ever reads.
        ///
        /// <paramref name="limit"/> defaults to <see cref="DefaultLeaderboardLimit"/> (25) and is
        /// hard-capped at <see cref="MaxLeaderboardLimit"/> (50) regardless of what the caller
        /// requests. <paramref name="boardSize"/> must be one of <see cref="RankedBoardSizes"/> (3 or
        /// 6) - any other value is rejected outright rather than silently returning an empty board.
        /// </summary>
        [CloudCodeFunction("GetRankedLeaderboard")]
        public async Task<RankedLeaderboardResponse> GetRankedLeaderboard(
            IExecutionContext context, IGameApiClient gameApiClient, int boardSize, int? limit = null)
        {
            try
            {
                if (Array.IndexOf(RankedBoardSizes, boardSize) < 0)
                {
                    throw new Exception($"Ranked leaderboard is not available for board size {boardSize}.");
                }

                int resolvedLimit = limit is > 0 ? Math.Min(limit.Value, MaxLeaderboardLimit) : DefaultLeaderboardLimit;

                var rankedConfig = await RankedConfigReader.GetAsync(context, gameApiClient);
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                var top = await RankedLeaderboardStore.GetTopAsync(context, gameApiClient, boardSize, resolvedLimit);

                var response = new RankedLeaderboardResponse
                {
                    BoardSize = boardSize,
                    Season = RankedSeasonCalculator.CurrentSeasonId(rankedConfig, now),
                    Top = top.Select(entry => new RankedLeaderboardEntryDto
                    {
                        PlayerId = entry.PlayerId,
                        PlayerName = entry.PlayerName,
                        Rank = entry.Rank,
                        Mmr = (int)Math.Round(entry.Score),
                    }).ToList(),
                };

                // design-doc.md section 6.6 elegibilidad: a caller who has not finished placement on
                // this board has no leaderboard entry yet, so GetOwnEntryAsync returns null and this
                // degrades to HasOwnEntry = false rather than fabricating a row.
                //
                // What makes that true is the placement check in RankedMatchSupport, which is the
                // only place a score is ever submitted. It is worth naming, because this comment
                // used to claim the same outcome while nothing enforced it: scores were published
                // from the first settled series, so an unplaced player was ranked and got a row
                // here. Fixed 2026-08-08 after the live run measured it. Filtering here instead
                // would mean a profile read per leaderboard entry, which is why the gate is on the
                // write.
                var ownEntry = await RankedLeaderboardStore.GetOwnEntryAsync(context, gameApiClient, boardSize, context.PlayerId);
                if (ownEntry != null)
                {
                    response.HasOwnEntry = true;
                    response.OwnEntry = new RankedLeaderboardEntryDto
                    {
                        PlayerId = ownEntry.PlayerId,
                        PlayerName = ownEntry.PlayerName,
                        Rank = ownEntry.Rank,
                        Mmr = (int)Math.Round(ownEntry.Score),
                    };
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetRankedLeaderboard failed for playerId={PlayerId}, boardSize={BoardSize}.", context.PlayerId, boardSize);
                throw new Exception($"Failed to get ranked leaderboard: {ex.Message}");
            }
        }
    }
}
