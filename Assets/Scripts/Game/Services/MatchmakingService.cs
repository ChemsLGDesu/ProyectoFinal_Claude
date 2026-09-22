using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Matchmaker;
using Unity.Services.Matchmaker.Models;
using UnityEngine;

namespace TTTXO.Game.Services
{
    /// <summary>
    /// Drives a single Quickmatch OR Ranked search (Docs/03-Arquitectura-UGS-TicTacToe.md#Matchmaker,
    /// wireframe screen 5): creates a Matchmaker ticket against <c>quickmatch-queue</c> or
    /// <c>ranked-queue</c> (Milestone 5 - design-doc.md section 6.2, <c>Assets/Matchmaker/RankedQueue.mmq</c>)
    /// with the chosen board size (and, for Ranked, the player's MMR) as ticket attributes, polls it,
    /// and - once found - calls <see cref="OnlineMatchService.CreateMatchAsync"/> with retry/backoff
    /// (risk #1). Explicit cancellation (risk #3) is the caller's responsibility
    /// (<see cref="CancelAsync"/>) - called from the Matchmaking screen's <c>OnHide</c>/Cancel button
    /// and from <c>GameManager</c>'s <c>OnApplicationPause</c> hook.
    ///
    /// Static/singleton by design (same convention as <see cref="GameConfigService"/>/<see cref="UgsInitializer"/>):
    /// only one search (Quickmatch or Ranked) can be active at a time in this client.
    /// </summary>
    public static class MatchmakingService
    {
        private const string QuickmatchQueueName = "quickmatch-queue";
        private const string RankedQueueName = "ranked-queue";
        private const string BoardSizeAttribute = "board_size";

        /// <summary><c>Assets/Matchmaker/RankedQueue.mmq</c>'s <c>MmrDifference</c> rule source (design-doc.md section 6.2: "El ticket publica el MMR del tablero al que se está encolando... Un solo campo mmr en el ticket, no dos").</summary>
        private const string MmrAttribute = "mmr";

        public enum SearchStatus
        {
            Idle,
            Searching,
            CreatingMatch,
            Matched,
            Cancelled,
            TimedOut,
            Failed,
        }

        public static SearchStatus Status { get; private set; } = SearchStatus.Idle;

        public static int BoardSize { get; private set; }

        /// <summary>True while the active/last search was against <c>ranked-queue</c> (Milestone 5) rather than <c>quickmatch-queue</c>.</summary>
        public static bool IsRanked { get; private set; }

        /// <summary>Set once <see cref="OnlineMatchService.CreateMatchAsync"/> resolves - the TicTacToe match to open on the Game screen.</summary>
        public static MatchStateDto ResolvedMatch { get; private set; }

        /// <summary>Kept for logs/diagnostics only - never shown to the player (same pattern as UgsInitializer.LastErrorMessage).</summary>
        public static string LastErrorMessage { get; private set; }

        private static string _ticketId;
        private static string _handoffId;
        private static float _searchStartRealtime;
        private static bool _pollInFlight;

        /// <summary>"online_quickmatch" | "ranked" - the <c>mode</c> argument sent to CreateMatch, see <see cref="OnlineMatchService.CreateMatchAsync"/>.</summary>
        private static string ServerModeCode => IsRanked ? GameAnalytics.RankedModeCode : GameAnalytics.OnlineQuickmatchModeCode;

        /// <summary>"quickmatch" | "ranked" - the vocabulary design-doc.md section 6.7 uses for <c>matchmaking_wait_time</c>/<c>matchmaking_cancelled</c>'s <c>mode</c> parameter (deliberately different strings from <see cref="ServerModeCode"/>, which matches match_started/match_finished's own "online_quickmatch"/"ranked" vocabulary - both are pre-existing, unrelated conventions this service just has to speak both of).</summary>
        private static string AnalyticsModeCode => IsRanked ? "ranked" : "quickmatch";

        /// <summary>
        /// Seconds since <see cref="StartSearchAsync"/> - drives the mm:ss display and the "Taking
        /// too long?" fallback card (wireframe screen 5). Deliberately independent of <see cref="Status"/>
        /// (0 only before any search has ever started) - several call sites below report this to
        /// analytics right after setting a terminal <see cref="Status"/>, which would otherwise always
        /// read back 0.
        /// </summary>
        public static float ElapsedSeconds => _searchStartRealtime > 0f ? Time.realtimeSinceStartup - _searchStartRealtime : 0f;

        /// <summary>
        /// Starts a new search: creates a Matchmaker ticket for <paramref name="boardSize"/> against
        /// <c>quickmatch-queue</c> (<paramref name="ranked"/> false, unchanged Milestone 4 behavior)
        /// or <c>ranked-queue</c> (<paramref name="ranked"/> true, Milestone 5 - design-doc.md section
        /// 6.2), publishing the player's current MMR for that board size as the ticket's <c>mmr</c>
        /// attribute in the Ranked case - a fresh <c>GetRankedProfile</c> read
        /// (<see cref="ResolveRankedMmrForTicketAsync"/>), never a stale client guess, since a
        /// mis-seeded ticket would throw off the whole ±100..±600 relaxation curve of design-doc.md
        /// section 6.2. Never throws - failures land in <see cref="SearchStatus.Failed"/>.
        /// </summary>
        public static async Task StartSearchAsync(int boardSize, bool ranked = false)
        {
            BoardSize = boardSize;
            IsRanked = ranked;
            ResolvedMatch = null;
            LastErrorMessage = null;
            _handoffId = null;
            _searchStartRealtime = Time.realtimeSinceStartup;
            Status = SearchStatus.Searching;

            try
            {
                var attributes = new Dictionary<string, object> { { BoardSizeAttribute, boardSize } };
                if (ranked)
                {
                    attributes[MmrAttribute] = await ResolveRankedMmrForTicketAsync(boardSize);
                }

                var players = new List<Player> { new(OnlineMatchService.LocalPlayerId, attributes) };
                var options = new CreateTicketOptions(ranked ? RankedQueueName : QuickmatchQueueName);

                var response = await MatchmakerService.Instance.CreateTicketAsync(players, options);
                _ticketId = response.Id;
            }
            catch (Exception ex)
            {
                LastErrorMessage = ex.Message;
                Debug.LogWarning($"MatchmakingService: CreateTicketAsync failed - {ex.Message}");
                Status = SearchStatus.Failed;
                GameAnalytics.MatchmakingWaitTime(ElapsedSeconds, boardSize, matched: false, AnalyticsModeCode);
                GameAnalytics.MatchmakingCancelled("error");
            }
        }

        /// <summary>
        /// design-doc.md section 6.2: "El ticket publica el MMR del tablero al que se está encolando"
        /// - this MUST be the server's authoritative value, not a client-side guess:
        /// <see cref="RankedProfileCache"/> alone goes stale after a decay tick or a season soft-reset
        /// the client never observed (see that class's remarks), which would silently mis-seed the
        /// skill-based matchmaking curve. Tries a fresh <c>GetRankedProfile</c> read first and mirrors
        /// whatever the server returns back into <see cref="RankedProfileCache"/> (so it stays a
        /// decent fallback for next time); only falls back to the cached last-known value if the read
        /// itself fails (offline, Cloud Code unreachable) - same "degrade gracefully, never break
        /// play" posture as every other Cloud Code read in this project.
        /// </summary>
        private static async Task<int> ResolveRankedMmrForTicketAsync(int boardSize)
        {
            try
            {
                var profile = await RankedQueryService.GetProfileAsync();
                var board = profile?.BoardFor(boardSize);
                if (board != null)
                {
                    RankedProfileCache.SetKnownMmr(boardSize, board.Mmr);
                    return board.Mmr;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"MatchmakingService: GetRankedProfile failed, falling back to last-known MMR - {ex.Message}");
            }

            return RankedProfileCache.GetKnownMmr(boardSize, GameConfigService.RankedConfig.InitialMmr);
        }

        /// <summary>
        /// One polling step - call this repeatedly (e.g. every ~1.5s) from the Matchmaking screen
        /// while <see cref="Status"/> is <see cref="SearchStatus.Searching"/>. Re-entrancy-safe: a
        /// call that arrives while a previous one is still in flight is a no-op.
        /// </summary>
        public static async Task PollOnceAsync()
        {
            if (Status != SearchStatus.Searching || _pollInFlight || string.IsNullOrEmpty(_ticketId))
            {
                return;
            }

            _pollInFlight = true;
            try
            {
                var ticketStatus = await MatchmakerService.Instance.GetTicketAsync(_ticketId);

                if (ticketStatus.Type == typeof(MatchIdAssignment))
                {
                    var assignment = (MatchIdAssignment)ticketStatus.Value;
                    await HandleAssignmentAsync(assignment);
                }
                // Any other assignment type (e.g. a stray MultiplayAssignment) is not expected for
                // quickmatch-pool (matchHosting.type = MatchId) - treat as still-searching rather
                // than failing outright, since a transient/unexpected shape here is not fatal.
            }
            catch (Exception ex)
            {
                LastErrorMessage = ex.Message;
                Debug.LogWarning($"MatchmakingService: ticket poll failed - {ex.Message}");
                Status = SearchStatus.Failed;
                GameAnalytics.MatchmakingWaitTime(ElapsedSeconds, BoardSize, matched: false, AnalyticsModeCode);
                GameAnalytics.MatchmakingCancelled("error");
            }
            finally
            {
                _pollInFlight = false;
            }
        }

        private static async Task HandleAssignmentAsync(MatchIdAssignment assignment)
        {
            switch (assignment.Status)
            {
                case MatchIdAssignment.StatusOptions.Found:
                    Status = SearchStatus.CreatingMatch;
                    _handoffId = assignment.MatchId;
                    try
                    {
                        var match = await OnlineMatchService.CreateMatchAsync(BoardSize, ServerModeCode, _ticketId, _handoffId);
                        ResolvedMatch = match;
                        Status = SearchStatus.Matched;
                        GameAnalytics.MatchmakingWaitTime(ElapsedSeconds, BoardSize, matched: true, AnalyticsModeCode);
                    }
                    catch (Exception ex)
                    {
                        LastErrorMessage = ex.Message;
                        Debug.LogWarning($"MatchmakingService: CreateMatch failed after ticket resolved - {ex.Message}");
                        Status = SearchStatus.Failed;
                        GameAnalytics.MatchmakingWaitTime(ElapsedSeconds, BoardSize, matched: false, AnalyticsModeCode);
                        GameAnalytics.MatchmakingCancelled("error");
                    }
                    break;

                case MatchIdAssignment.StatusOptions.Timeout:
                    Status = SearchStatus.TimedOut;
                    GameAnalytics.MatchmakingWaitTime(ElapsedSeconds, BoardSize, matched: false, AnalyticsModeCode);
                    GameAnalytics.MatchmakingCancelled("timeout");
                    break;

                case MatchIdAssignment.StatusOptions.Failed:
                    LastErrorMessage = assignment.Message;
                    Status = SearchStatus.Failed;
                    GameAnalytics.MatchmakingWaitTime(ElapsedSeconds, BoardSize, matched: false, AnalyticsModeCode);
                    GameAnalytics.MatchmakingCancelled("error");
                    break;

                case MatchIdAssignment.StatusOptions.InProgress:
                default:
                    // Still searching - nothing to do, next PollOnceAsync tries again.
                    break;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Milestone 5 - Ranked 3x3 series contract (CloudCode~/TicTacToeModule/README.md "Serie de 2
        /// partidas (Ranked 3x3)" point 2): while a 3x3 Ranked series waits for the second matched
        /// player, <see cref="ResolvedMatch"/>'s <c>MatchId</c> is empty (no partida created yet) -
        /// the waiting client must re-invoke CreateMatch (already idempotent server-side), NOT
        /// GetMatchState. The Matchmaking screen calls this from its existing ~1.5s poll tick, which
        /// doubles as this retry's backoff. Re-throws on failure so the caller's own try/catch (same
        /// pattern as the initial CreateMatch call) decides how to react.
        /// </summary>
        public static async Task<MatchStateDto> RetryCreateMatchAsync()
        {
            var match = await OnlineMatchService.CreateMatchAsync(BoardSize, ServerModeCode, _ticketId, _handoffId);
            ResolvedMatch = match;
            return match;
        }

        /// <summary>
        /// Explicit ticket cancellation (risk #3: Docs/03-Arquitectura-UGS-TicTacToe.md "Resolucion
        /// detallada de riesgos #3"). Safe to call even if there is no active ticket (no-op). Always
        /// records <c>matchmaking_cancelled</c> with <paramref name="reason"/> unless the search had
        /// already resolved to a terminal state on its own (Matched/TimedOut/Failed already recorded
        /// their own outcome above).
        /// </summary>
        public static async Task CancelAsync(string reason)
        {
            bool wasSearching = Status == SearchStatus.Searching || Status == SearchStatus.CreatingMatch;
            string ticketId = _ticketId;
            _ticketId = null;
            Status = SearchStatus.Cancelled;

            if (wasSearching)
            {
                GameAnalytics.MatchmakingWaitTime(ElapsedSeconds, BoardSize, matched: false, AnalyticsModeCode);
                GameAnalytics.MatchmakingCancelled(reason);
            }

            if (string.IsNullOrEmpty(ticketId))
            {
                return;
            }

            try
            {
                await MatchmakerService.Instance.DeleteTicketAsync(ticketId);
            }
            catch (Exception ex)
            {
                // Best-effort: the pool's own ticket TTL (Assets/Matchmaker/QuickmatchQueue.mmq,
                // timeoutSeconds) is the safety net if this delete call itself fails.
                Debug.LogWarning($"MatchmakingService: DeleteTicketAsync failed - {ex.Message}");
            }
        }

        /// <summary>Cancels an in-flight search only if one is active - safe to call unconditionally (e.g. from GameManager.OnApplicationPause).</summary>
        public static Task CancelIfSearchingAsync(string reason)
        {
            return Status == SearchStatus.Searching || Status == SearchStatus.CreatingMatch
                ? CancelAsync(reason)
                : Task.CompletedTask;
        }

        /// <summary>Resets to <see cref="SearchStatus.Idle"/> before starting a fresh search (e.g. Matchmaking screen's own retry/re-entry).</summary>
        public static void Reset()
        {
            Status = SearchStatus.Idle;
            ResolvedMatch = null;
            LastErrorMessage = null;
            _ticketId = null;
            _handoffId = null;
            IsRanked = false;
        }
    }
}
