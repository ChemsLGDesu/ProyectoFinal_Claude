using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TTTXO.Core;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.Matchmaker.Model;

namespace TTTXO.CloudCode
{
    /// <summary>
    /// Match lifecycle functions (Docs/03-Arquitectura-UGS-TicTacToe.md#Cloud Code table):
    /// <c>CreateMatch</c>, <c>PlayMove</c>, <c>GetMatchState</c>, <c>GetAiMove</c>. All game rules
    /// (validity, win/draw detection) run through the exact same <see cref="TTTXO.Core"/> code the
    /// client uses in Milestone 1 local play (see MatchStateStore.Replay) - the client never decides
    /// who won (Docs/01-Directrices-Proyecto.md#Seguridad / anti-cheat).
    ///
    /// Milestone 4 adds the online_quickmatch mode (Docs/03-Arquitectura-UGS-TicTacToe.md#Matchmaker):
    /// <c>CreateMatch</c> becomes idempotent per Matchmaker handoffId (risk #1), <c>PlayMove</c>
    /// enforces turn ownership by the caller's authenticated playerId and pushes a Wire notification
    /// to the rival, and <c>GetMatchState</c> reactively resolves abandoned matches (risk #4). The
    /// existing 1P/local path (empty <c>MatchStateRecord.Players</c>) is untouched byte-for-byte.
    ///
    /// <c>ValidatePurchase</c>/<c>RedeemStoreItem</c> are intentionally not implemented here - IAP is
    /// a later, dedicated milestone (see Docs/04-Store-Catalog-TicTacToe.md).
    /// </summary>
    public class MatchFunctions
    {
        /// <summary>Mode string used by the client for a Matchmaker-created online match (see GameAnalytics.ModeCode's "online_quickmatch" on the client).</summary>
        private const string OnlineQuickmatchMode = "online_quickmatch";

        /// <summary>Mode string for a Ranked match (Milestone 5, design-doc.md section 6) - see <see cref="RankedMatchSupport"/> for everything Ranked-specific; this file only branches on it.</summary>
        private const string RankedMode = RankedMatchSupport.ModeCode;

        /// <summary>Mirrors Core's InvalidReason wording style (see TTTXO.Core.MoveResult) for a rejection that only the module - not Core - can know about: Core has no concept of "which network caller" is moving.</summary>
        private const string NotThisPlayersTurnReason = "Not this player's turn";

        /// <summary>
        /// <see cref="SweepAbandonedMatches"/>'s recommended per-run cap on how many indexed
        /// matchIds it actually loads/evaluates (README "Barrido proactivo de partidas abandonadas" -
        /// "límite de partidas por ejecución"), used whenever the caller (the Scheduler trigger, in
        /// practice) doesn't pass its own <c>maxMatches</c>. Keeps a single sweep invocation's Cloud
        /// Save I/O bounded regardless of how many online matches happen to be indexed on a given
        /// day, at 5-minute default sweep frequency this comfortably drains any realistic backlog
        /// within a few runs even if one run hits the cap.
        /// </summary>
        private const int DefaultMaxMatchesPerSweep = 200;

        /// <summary>Hard ceiling on <c>maxMatches</c> regardless of what a caller requests - defense in depth against an accidental or malicious oversized sweep request blowing out a single function's execution time budget.</summary>
        private const int MaxMatchesPerSweepHardCap = 500;

        /// <summary>
        /// How many of the most recent day partitions <see cref="SweepAbandonedMatches"/> checks per
        /// run. <see cref="MatchmakingConfigDto.AbandonTimeoutMinutes"/> defaults to just 3 minutes,
        /// so 1-2 days would normally be enough, but this stays deliberately generous (covers a
        /// misconfigured/very large abandon timeout, or the sweep simply not having run for a while)
        /// since checking a few extra empty day partitions is cheap.
        /// </summary>
        private const int IndexLookbackDays = 3;

        private readonly ILogger<MatchFunctions> _logger;

        public MatchFunctions(ILogger<MatchFunctions> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Creates a new match for a given board size/mode. For 1P/local (unchanged since
        /// Milestone 2) the client calls this directly with no ticket. For
        /// <paramref name="mode"/> == "online_quickmatch" (Milestone 4), <paramref name="ticketId"/>
        /// (the caller's own Matchmaker ticket - quickmatch-pool has <c>maxPlayersPerTicket = 1</c>,
        /// so this id is unique per player) and <paramref name="handoffId"/> (the shared
        /// <c>MatchIdAssignment.MatchId</c> both matched clients received - see
        /// Assets/Matchmaker/QuickmatchQueue.mmq's <c>matchHosting.type = MatchId</c>) are required.
        ///
        /// The call is idempotent on <paramref name="handoffId"/> (Docs/03-Arquitectura-UGS-TicTacToe.md
        /// "Resolucion detallada de riesgos #1"): either matched player may call this, in either
        /// order. Because <c>IMatchmakerTicketsApi</c> only exposes a per-player ticket lookup (not
        /// the matched roster - verified against the installed Com.Unity.Services.CloudCode.Apis
        /// 0.0.26 assembly), this module cannot learn the rival's playerId up front the way it can
        /// with a Lobby-based design; instead the first caller creates a 1-player "waiting" match and
        /// the second caller (detected via the handoffId pointer) completes the roster - see
        /// <see cref="CreateOnlineMatchAsync"/>. Neither caller ever asserts the *other* player's
        /// identity - each only supplies its own <see cref="IExecutionContext.PlayerId"/> (already
        /// authenticated) plus a ticket that the Matchmaker Admin API independently confirms resolved
        /// to this exact <paramref name="handoffId"/>.
        ///
        /// X/O assignment is deterministic (not a coin flip): once both playerIds are known, they are
        /// sorted lexicographically (ordinal string compare) and the first becomes X (who, per
        /// design-doc.md section 1, always moves first).
        /// </summary>
        [CloudCodeFunction("CreateMatch")]
        public async Task<MatchStateDto> CreateMatch(
            IExecutionContext context, IGameApiClient gameApiClient, int boardSize, string mode, string ticketId = null, string handoffId = null)
        {
            try
            {
                // Two different questions, both of which have to be asked. Core knows whether the
                // board has *rules* - every size the game ever specified, on offer or not. The gate
                // knows whether it is *playable in this mode*, which is what BOARD_CONFIGS says and
                // what the board select screen shows. Only checking the first is what made the
                // 9x9/11x11 cut client-side (see BoardAvailability).
                var boardConfig = BoardConfig.ForSize(boardSize);
                await BoardAvailability.EnsureOfferedAsync(context, gameApiClient, boardSize, mode, _logger);

                bool isRanked = string.Equals(mode, RankedMode, StringComparison.OrdinalIgnoreCase);
                if (isRanked)
                {
                    return await RankedMatchSupport.CreateOrJoinRankedMatchAsync(context, gameApiClient, boardSize, boardConfig, ticketId, handoffId);
                }

                bool isOnline = string.Equals(mode, OnlineQuickmatchMode, StringComparison.OrdinalIgnoreCase);
                if (!isOnline)
                {
                    return await CreateLocalMatchAsync(context, gameApiClient, boardSize, mode, boardConfig);
                }

                return await CreateOnlineMatchAsync(context, gameApiClient, boardSize, mode, boardConfig, ticketId, handoffId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateMatch failed for boardSize={BoardSize}, mode={Mode}.", boardSize, mode);
                throw new Exception($"Failed to create match: {ex.Message}");
            }
        }

        private async Task<MatchStateDto> CreateLocalMatchAsync(
            IExecutionContext context, IGameApiClient gameApiClient, int boardSize, string mode, BoardConfig boardConfig)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var record = new MatchStateRecord
            {
                MatchId = Guid.NewGuid().ToString("N"),
                BoardSize = boardSize,
                WinLength = boardConfig.WinLength,
                Mode = mode,
                FirstPlayer = "X", // design-doc.md section 1: "X siempre mueve primero".
                CreatedAtUnixSeconds = now,
                LastActivityAtUnixSeconds = now,
            };

            await MatchStateStore.SaveAsync(context, gameApiClient, record);

            var match = MatchStateStore.Replay(record);
            return MatchStateStore.ToDto(record, match, context.PlayerId);
        }

        /// <summary>
        /// Internal (not <c>private</c>) so <see cref="RankedMatchSupport.CreateOrJoinRankedMatchAsync"/>
        /// can reuse this verbatim for a 6x6 Ranked match - this method is already generic over
        /// <paramref name="mode"/> (it only threads it through to <see cref="CreatePendingOnlineMatchAsync"/>,
        /// which stamps it onto the record), so Ranked needed zero changes here; see
        /// <see cref="CompleteOnlineMatchAsync"/> for the one spot that IS mode-aware.
        /// </summary>
        internal static async Task<MatchStateDto> CreateOnlineMatchAsync(
            IExecutionContext context, IGameApiClient gameApiClient, int boardSize, string mode, BoardConfig boardConfig,
            string ticketId, string handoffId)
        {
            if (string.IsNullOrWhiteSpace(ticketId) || string.IsNullOrWhiteSpace(handoffId))
            {
                throw new Exception("Both ticketId and handoffId are required to create an online match.");
            }

            // Hard verification with the Matchmaker Client API that the caller's own ticket really
            // resolved to this claimed handoffId (anti-spoof hardening for risk #1) - see
            // VerifyTicketResolvesToHandoffAsync's remarks and README "Verificación de ticket de
            // Matchmaker" for why context.ServiceToken (not context.AccessToken, a Service Account,
            // or extra Dashboard roles) is the correct credential here. A failed verification throws
            // and CreateMatch rejects outright - this is no longer best-effort.
            await VerifyTicketResolvesToHandoffAsync(context, gameApiClient, ticketId, handoffId);

            string existingMatchId = await MatchStateStore.TryGetMatchIdForHandoffAsync(context, gameApiClient, handoffId);
            if (string.IsNullOrEmpty(existingMatchId))
            {
                return await CreatePendingOnlineMatchAsync(context, gameApiClient, boardSize, mode, boardConfig, handoffId);
            }

            var record = await MatchStateStore.LoadAsync(context, gameApiClient, existingMatchId);

            if (record.Players.Count == 1)
            {
                if (record.Players[0].PlayerId == context.PlayerId)
                {
                    // Same caller retrying (e.g. after a network blip) before an opponent joined - idempotent no-op.
                    var waitingMatch = MatchStateStore.Replay(record);
                    return MatchStateStore.ToDto(record, waitingMatch, context.PlayerId);
                }

                return await CompleteOnlineMatchAsync(context, gameApiClient, record);
            }

            // Already complete (2 players) - idempotent return regardless of which of the 2 callers this is.
            var match = MatchStateStore.Replay(record);
            return MatchStateStore.ToDto(record, match, context.PlayerId);
        }

        /// <summary>First caller for this handoffId: creates a 1-player "waiting for opponent" match (see <see cref="MatchStateDto.IsWaitingForOpponent"/>) and publishes the handoff pointer.</summary>
        private static async Task<MatchStateDto> CreatePendingOnlineMatchAsync(
            IExecutionContext context, IGameApiClient gameApiClient, int boardSize, string mode, BoardConfig boardConfig, string handoffId)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var record = new MatchStateRecord
            {
                MatchId = Guid.NewGuid().ToString("N"),
                BoardSize = boardSize,
                WinLength = boardConfig.WinLength,
                Mode = mode,
                FirstPlayer = "X",
                CreatedAtUnixSeconds = now,
                LastActivityAtUnixSeconds = now,
                HandoffId = handoffId,
                Players = new List<MatchPlayerRecord>
                {
                    // Symbol left empty until the opponent joins and both playerIds can be sorted
                    // (see CompleteOnlineMatchAsync) - a lone player can't yet know if it will end up X or O.
                    new() { PlayerId = context.PlayerId, Symbol = string.Empty, LastActivityAtUnixSeconds = now },
                },
            };

            await MatchStateStore.SaveAsync(context, gameApiClient, record);
            await MatchStateStore.SaveHandoffPointerAsync(context, gameApiClient, handoffId, record.MatchId);

            var match = MatchStateStore.Replay(record);
            return MatchStateStore.ToDto(record, match, context.PlayerId);
        }

        /// <summary>
        /// Second caller for this handoffId: completes the roster and assigns X/O deterministically
        /// (lexicographic order of the 2 now-known playerIds, first is X - see CreateMatch's summary).
        /// Ranked-aware (Milestone 5): when <c>record.Mode == "ranked"</c> (always a 6x6 match here -
        /// 3x3 Ranked never reaches this method, see <see cref="RankedMatchSupport"/>), snapshots
        /// both players' current MMR (design-doc.md section 6.8 Q1's "stable snapshot, not a live
        /// re-read") and starts the Ranked turn timer - the one spot in this shared method that IS
        /// mode-aware, everything else about roster completion is identical to Quickmatch.
        /// </summary>
        private static async Task<MatchStateDto> CompleteOnlineMatchAsync(IExecutionContext context, IGameApiClient gameApiClient, MatchStateRecord record)
        {
            string[] orderedPlayerIds = { record.Players[0].PlayerId, context.PlayerId };
            Array.Sort(orderedPlayerIds, StringComparer.Ordinal);

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            record.Players = new List<MatchPlayerRecord>
            {
                new() { PlayerId = orderedPlayerIds[0], Symbol = "X", LastActivityAtUnixSeconds = now },
                new() { PlayerId = orderedPlayerIds[1], Symbol = "O", LastActivityAtUnixSeconds = now },
            };
            record.LastActivityAtUnixSeconds = now;

            if (string.Equals(record.Mode, RankedMode, StringComparison.OrdinalIgnoreCase))
            {
                var rankedConfig = await RankedConfigReader.GetAsync(context, gameApiClient);
                foreach (var player in record.Players)
                {
                    var touch = await RankedProfileStore.TouchAsync(context, gameApiClient, player.PlayerId, record.BoardSize, rankedConfig, now);
                    record.RankedMmrBeforeByPlayerId[player.PlayerId] = touch.Record.Mmr;
                }

                record.TurnStartedAtUnixSeconds = now;
            }

            await MatchStateStore.SaveAsync(context, gameApiClient, record);

            // Risk #4 proactive sweep (README "Barrido proactivo de partidas abandonadas"): this is
            // the ONE moment an online match becomes indexable - a real rival now exists, so "both
            // players abandoned" first becomes a meaningful question here. See MatchIndexStore.
            await MatchIndexStore.AddMatchAsync(context, gameApiClient, record);

            var match = MatchStateStore.Replay(record);
            return MatchStateStore.ToDto(record, match, context.PlayerId);
        }

        /// <summary>
        /// Risk #1 handoff safety net, HARD since this task: confirms with the Matchmaker Client API
        /// (<see cref="IGameApiClient.MatchmakerTickets"/>, <c>Unity.Services.Matchmaker.Api.IMatchmakerTicketsApi</c>
        /// - a Client API, not an Admin API; see README "Verificación de ticket de Matchmaker") that
        /// the caller's own <paramref name="ticketId"/> is real, resolved
        /// (<c>MatchIdAssignment.Status == Found</c>), and resolved to exactly the
        /// <paramref name="handoffId"/> the caller is claiming to join. Throws on ANY failure - ticket
        /// not found/not yet resolved, resolved to a *different* match, or a transport/auth error -
        /// so a failed verification rejects <c>CreateMatch</c> outright.
        ///
        /// The previous best-effort version of this method called
        /// <c>GetTicketStatusAsync(context, context.ProjectId, context.EnvironmentId, ticketId, ...)</c>,
        /// which - against the REAL signature reflected from the installed
        /// Com.Unity.Services.CloudCode.Apis 0.0.26 assembly,
        /// <c>GetTicketStatusAsync(IExecutionContext, string accessToken, string id, string
        /// impersonatedUserId = null, CancellationToken = default)</c> - passed
        /// <c>context.ProjectId</c> (not a token at all) as <c>accessToken</c> and
        /// <c>context.EnvironmentId</c> (not a ticket id) as <c>id</c>. That was always going to 401:
        /// it was never actually looking up <paramref name="ticketId"/>. The fix is
        /// <c>context.ServiceToken</c> as <c>accessToken</c> - the exact same credential
        /// <see cref="MatchStateStore"/>/<see cref="OnlineRewardStore"/> already use for every other
        /// cross-player Cloud Save call in this module - and <paramref name="ticketId"/> in the
        /// <c>id</c> slot. Per Unity's "Service and access token support" docs, the Service Token's
        /// "Supported services" table lists Matchmaker under "UGS Client APIs" explicitly (unlike
        /// "UGS Admin APIs", which the same table marks "Not supported - use Service Account
        /// authentication"); <c>IMatchmakerTicketsApi</c> lives in
        /// <c>Unity.Services.Matchmaker.Api</c>, not a `.Admin.` namespace, confirming it is that
        /// Client API, not the Admin one - so no service account, no extra Matchmaker role, and no
        /// Dashboard IAM configuration is needed beyond the module already having Matchmaker enabled
        /// (which it does - ticket creation itself is already live).
        ///
        /// THE `impersonatedUserId` ARGUMENT IS LOAD-BEARING, and that is what the first live run
        /// found (2026-08-08, development). The reasoning above was right that the Service Token is
        /// the correct credential and wrong that it is sufficient on its own: passing <c>null</c>
        /// there made every call come back <c>Forbidden</c>, so CreateMatch rejected outright and NO
        /// online match - Quickmatch or Ranked - could be created at all. This is a player-scoped
        /// Client API: the same GET, issued with a player's own bearer token, returns 200 with the
        /// assignment, which is how the cause was isolated. A Service Token has no player identity
        /// of its own, so it has to name the player it is acting for - <c>context.PlayerId</c>, the
        /// caller, which is exactly the ticket whose ownership we mean to verify.
        ///
        /// THE TYPED RESPONSE IS UNUSABLE, which the same live run found once auth was fixed:
        /// <c>Unity.Services.Matchmaker.Model.TicketStatusResponse</c> is a generated <c>oneOf</c>
        /// over MatchId/Multiplay/IpPort/Custom/None assignments, and the ordinary MatchId payload
        /// (<c>{"assignmentType":"MatchIdAssignment","message":null,"status":"Found","matchId":"..."}</c>)
        /// deserializes cleanly into THREE of those five - Custom and Multiplay differ only by
        /// optional fields - so the branch never resolves and every call ends in
        /// <c>ApiException(Deserialization)</c>. That is a defect in the generated SDK, not in
        /// anything callers can pass, and 0.0.26 is the newest non-alpha published.
        ///
        /// The way out is that <c>HttpApiClient.ToApiResponse&lt;T&gt;</c> sets <c>RawContent</c>
        /// BEFORE attempting deserialization and hands the whole <c>ApiResponse</c> to the
        /// <c>ApiException</c> it throws. So the body survives, and this method reads
        /// <c>status</c>/<c>matchId</c> straight out of it. Nothing about the security property
        /// changes: the same three rejections apply, from the same two values.
        ///
        /// Note the shape of the earlier mistake, because it repeated: the previous version was
        /// checked by SDK reflection and documentation and shipped as reasoned-but-unexecuted, with
        /// its own comment warning to test it live before trusting it. Nobody did, for two
        /// milestones. Reflection proves a signature compiles; only a live call proves the service
        /// accepts it - and here, two separate failures were stacked behind the first one.
        /// </summary>
        /// <summary>Internal (not <c>private</c>) so <see cref="RankedMatchSupport"/> can reuse this verbatim for Ranked's own handoff verification (identical requirement/mechanics regardless of mode/queue).</summary>
        internal static async Task VerifyTicketResolvesToHandoffAsync(IExecutionContext context, IGameApiClient gameApiClient, string ticketId, string handoffId)
        {
            // Deliberately NOT using the typed .Data: see the remarks above. We read the body the
            // SDK could not model and parse the four fields ourselves.
            string rawContent;
            try
            {
                rawContent = (await gameApiClient.MatchmakerTickets.GetTicketStatusAsync(
                    context, context.ServiceToken, ticketId, context.PlayerId, CancellationToken.None)).RawContent;
            }
            catch (ApiException ex) when (ex.Type == ApiExceptionType.Deserialization && ex.Response != null)
            {
                // This is the expected path today, on every single call. HttpApiClient throws here
                // AFTER attaching the ApiResponse it already built, and RawContent on it is the
                // untouched body - so the SDK's inability to pick a oneOf branch costs us nothing.
                rawContent = ex.Response.RawContent;
            }
            catch (Exception ex)
            {
                throw new Exception($"Matchmaker ticket verification failed for ticket '{ticketId}': {ex.Message}", ex);
            }

            if (string.IsNullOrWhiteSpace(rawContent))
            {
                throw new Exception($"Matchmaker returned an empty ticket status for ticket '{ticketId}'.");
            }

            string status;
            string assignedMatchId;
            try
            {
                using var document = JsonDocument.Parse(rawContent);
                var root = document.RootElement;
                status = root.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
                assignedMatchId = root.TryGetProperty("matchId", out var matchIdElement) ? matchIdElement.GetString() : null;
            }
            catch (JsonException ex)
            {
                throw new Exception($"Matchmaker ticket status for '{ticketId}' was not readable JSON: {ex.Message}", ex);
            }

            // Same three rejections as before, unchanged in meaning - only the source of the two
            // values moved from a typed model to the raw body.
            if (!string.Equals(status, "Found", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Ticket '{ticketId}' has not resolved to a found match (status '{status ?? "none"}').");
            }

            if (!string.Equals(assignedMatchId, handoffId, StringComparison.Ordinal))
            {
                throw new Exception($"Ticket '{ticketId}' resolved to a different match than the claimed handoffId.");
            }
        }

        /// <summary>
        /// Validates and applies a move, server-authoritative (Docs/01-Directrices-Proyecto.md#Seguridad
        /// / anti-cheat). For online matches (Milestone 4): rejects a caller who is not the player
        /// whose turn it is (<see cref="NotThisPlayersTurnReason"/>), then notifies the rival over
        /// Wire (Docs/03-Arquitectura-UGS-TicTacToe.md#Wire) - the rival's client re-fetches
        /// <c>GetMatchState</c> on receiving it rather than trusting the push payload. 1P/local
        /// matches (empty <c>Players</c>) are unaffected: no identity check, no push.
        /// </summary>
        [CloudCodeFunction("PlayMove")]
        public async Task<MatchStateDto> PlayMove(
            IExecutionContext context, IGameApiClient gameApiClient, IPushClient pushClient, string matchId, int row, int col)
        {
            try
            {
                var record = await MatchStateStore.LoadAsync(context, gameApiClient, matchId);

                if (record.Players.Count == 1)
                {
                    throw new Exception("Match is waiting for the opponent to join.");
                }

                bool isOnline = record.Players.Count == 2;
                MatchPlayerRecord rival = null;

                if (isOnline)
                {
                    if (!string.IsNullOrEmpty(record.AbandonedWinnerPlayerId) || record.BothPlayersAbandoned)
                    {
                        throw new Exception("Match already finished");
                    }

                    var caller = record.Players.FirstOrDefault(p => p.PlayerId == context.PlayerId);
                    if (caller == null)
                    {
                        throw new Exception("Caller is not a player in this match.");
                    }

                    var matchBeforeMove = MatchStateStore.Replay(record);
                    string currentSymbol = matchBeforeMove.CurrentPlayer == CellOwner.X ? "X" : "O";
                    if (caller.Symbol != currentSymbol)
                    {
                        throw new Exception($"Invalid move ({row},{col}): {NotThisPlayersTurnReason}");
                    }

                    rival = record.Players.First(p => p.PlayerId != context.PlayerId);
                }

                var match = MatchStateStore.Replay(record);
                var result = match.PlayMove(row, col);
                if (!result.IsValid)
                {
                    throw new Exception($"Invalid move ({row},{col}): {result.InvalidReason}");
                }

                record.Moves.Add(new MoveRecord { Row = row, Col = col });
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                record.LastActivityAtUnixSeconds = now;

                bool isRanked = isOnline && string.Equals(record.Mode, RankedMode, StringComparison.OrdinalIgnoreCase);

                if (isOnline)
                {
                    var caller = record.Players.First(p => p.PlayerId == context.PlayerId);
                    caller.LastActivityAtUnixSeconds = now;

                    if (result.StatusAfterMove != MatchStatus.InProgress)
                    {
                        if (isRanked)
                        {
                            // Milestone 5, design-doc.md section 6: settles the Ranked unit (6x6:
                            // this match; 3x3 game 2: the series; 3x3 game 1: creates game 2 instead
                            // - no settlement yet, see RankedMatchSupport.HandleRankedMatchEndedAsync).
                            // Mutates `record` in place, same contract AwardOnlineRewardsAsync has.
                            await RankedMatchSupport.HandleRankedMatchEndedAsync(context, gameApiClient, record, result.StatusAfterMove);
                        }
                        else
                        {
                            // design-doc.md section 4, "Modo online Quickmatch": the reward is
                            // computed and credited here, server-side, exactly once - the move that
                            // just applied is the one that ended the match (the mover can only win or
                            // draw on their own move, never lose on it), so both players' outcomes
                            // are already knowable. A retried PlayMove for an already-finished match
                            // fails move validation above before ever reaching this point, so this
                            // can never double-award (see AwardOnlineRewardsAsync).
                            await AwardOnlineRewardsAsync(context, gameApiClient, record, result.StatusAfterMove);
                        }
                    }
                    else if (isRanked)
                    {
                        // design-doc.md section 6.1 "Extiende sección 2": a new turn just began - reset the Ranked turn timer (see MatchStateRecord.TurnStartedAtUnixSeconds).
                        record.TurnStartedAtUnixSeconds = now;
                    }
                }

                await MatchStateStore.SaveAsync(context, gameApiClient, record);

                if (isOnline && result.StatusAfterMove != MatchStatus.InProgress)
                {
                    // Risk #4 proactive sweep: a match that just finished by a real move no longer
                    // needs to be swept - drop it from the active-match index (see MatchIndexStore).
                    await MatchIndexStore.RemoveMatchAsync(context, gameApiClient, record);
                }

                if (isOnline && rival != null)
                {
                    await NotifyRivalAsync(context, pushClient, record.MatchId, rival.PlayerId);
                }

                var dto = MatchStateStore.ToDto(record, match, context.PlayerId);
                if (isRanked)
                {
                    await RankedMatchSupport.EnrichDtoWithSeriesInfoAsync(context, gameApiClient, record, dto);
                }

                return dto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PlayMove failed for matchId={MatchId}, move=({Row},{Col}).", matchId, row, col);
                throw new Exception($"Failed to play move: {ex.Message}");
            }
        }

        /// <summary>
        /// Wire push to the rival (Docs/03-Arquitectura-UGS-TicTacToe.md#Wire: "payload liviano
        /// {type, matchId}") - a light "something changed, go re-fetch" signal, never the source of
        /// truth. Never lets a push failure fail the move itself (the move already succeeded and was
        /// persisted above); the rival's polling fallback (see client's OnlineMatchService) covers a
        /// missed push.
        /// </summary>
        private async Task NotifyRivalAsync(IExecutionContext context, IPushClient pushClient, string matchId, string rivalPlayerId)
        {
            try
            {
                // IPushClient.SendPlayerMessageAsync(context, message, messageType, playerId) takes
                // the message as a plain string (verified against the installed
                // Com.Unity.Services.CloudCode.Core 0.0.4 assembly) - serialize the light
                // {type, matchId} payload (Docs/03-Arquitectura-UGS-TicTacToe.md#Wire) to JSON text.
                string payload = JsonSerializer.Serialize(new { type = "match_updated", matchId });
                await pushClient.SendPlayerMessageAsync(context, payload, "match_updated", rivalPlayerId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PlayMove: Wire push to rival {RivalPlayerId} for match {MatchId} failed - rival's polling fallback covers this.", rivalPlayerId, matchId);
            }
        }

        /// <summary>
        /// Milestone 4, design-doc.md section 4 "Modo online Quickmatch": computes and credits the
        /// soft currency owed to BOTH matched players once <paramref name="finalStatus"/> is
        /// terminal - a win only pays the winner (loser = 0, "Derrota online = 0"), a draw pays both
        /// at the base rate ("el empate paga la base tal cual"). Runs entirely server-side
        /// (Docs/01-Directrices-Proyecto.md#Seguridad / anti-cheat: "hay incentivo real de trampa").
        /// Mutates <paramref name="record"/>.<see cref="MatchStateRecord.AwardedSoftCurrencyByPlayerId"/>
        /// in place; the caller (<see cref="PlayMove"/>) is responsible for persisting
        /// <paramref name="record"/> afterwards.
        /// </summary>
        private static async Task AwardOnlineRewardsAsync(
            IExecutionContext context, IGameApiClient gameApiClient, MatchStateRecord record, MatchStatus finalStatus)
        {
            string dayUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            foreach (var player in record.Players)
            {
                var rival = record.Players.First(p => p.PlayerId != player.PlayerId);
                int rawReward = OnlineRewardCalculator.ComputeRawReward(record.BoardSize, finalStatus, player.Symbol);

                bool isWin = finalStatus != MatchStatus.Draw && rawReward > 0;
                if (isWin)
                {
                    // design-doc.md section 4: "A partir de la 4a victoria contra el mismo playerId
                    // rival en la ventana de 24 h, la victoria paga 0" - collusion mitigation,
                    // counted regardless of whether the daily caps below would have paid it anyway.
                    int winOrdinalToday = await OnlineRewardStore.IncrementRivalWinCountAsync(context, gameApiClient, player.PlayerId, rival.PlayerId, dayUtc);
                    if (winOrdinalToday > OnlineRewardCalculator.MaxPaidWinsPerRivalPerDay)
                    {
                        rawReward = 0;
                    }
                }

                int awarded = rawReward > 0
                    ? await ApplyDailyCapsAndCreditAsync(context, gameApiClient, player.PlayerId, rawReward, dayUtc)
                    : 0;

                record.AwardedSoftCurrencyByPlayerId[player.PlayerId] = awarded;
            }
        }

        /// <summary>
        /// design-doc.md section 4: "Tope diario online = 200 monedas/dia" + "El tope global de
        /// 300/dia sigue aplicando por encima de todo" - both reset at UTC server midnight (never
        /// the device's local clock, which is falsifiable - see <see cref="OnlineCurrencyLedgerRecord"/>).
        /// Clamps <paramref name="rawReward"/> to whatever budget remains in both counters, credits
        /// the resulting amount to Cloud Save <c>currency</c>, and persists the updated ledger.
        /// </summary>
        /// <summary>Internal (not <c>private</c>) so <see cref="RankedMatchSupport"/> reuses the exact same daily/global caps for Ranked's own reward crediting (design-doc.md section 6.3: "los mismos de Quickmatch, sin fork").</summary>
        internal static async Task<int> ApplyDailyCapsAndCreditAsync(
            IExecutionContext context, IGameApiClient gameApiClient, string playerId, int rawReward, string dayUtc)
        {
            var ledger = await OnlineRewardStore.GetLedgerAsync(context, gameApiClient, playerId);
            if (ledger.DayUtc != dayUtc)
            {
                ledger.DayUtc = dayUtc;
                ledger.OnlineEarnedToday = 0;
                ledger.GlobalEarnedToday = 0;
            }

            int remainingOnline = Math.Max(0, OnlineRewardCalculator.OnlineDailyCap - ledger.OnlineEarnedToday);
            int remainingGlobal = Math.Max(0, OnlineRewardCalculator.GlobalDailyCap - ledger.GlobalEarnedToday);
            int awarded = Math.Clamp(rawReward, 0, Math.Min(remainingOnline, remainingGlobal));

            ledger.OnlineEarnedToday += awarded;
            ledger.GlobalEarnedToday += awarded;
            await OnlineRewardStore.SetLedgerAsync(context, gameApiClient, playerId, ledger);

            if (awarded > 0)
            {
                var currency = await OnlineRewardStore.GetAuthoritativeCurrencyAsync(context, gameApiClient, playerId);
                currency.Balance += awarded;
                currency.UpdatedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await OnlineRewardStore.SetAuthoritativeCurrencyAsync(context, gameApiClient, playerId, currency);
            }

            return awarded;
        }

        /// <summary>
        /// Recovers the current state of a match (e.g. after a reconnect). For online matches
        /// (Milestone 4), this is also where risk #4's reactive abandonment resolution happens: if
        /// the rival has had no activity for longer than <c>MATCHMAKING_CONFIG.AbandonTimeoutMinutes</c>
        /// (Remote Config, default 3), the match is resolved as a win for the caller and persisted -
        /// no server-side cron needed (Docs/03-Arquitectura-UGS-TicTacToe.md#4. Partidas online
        /// abandonadas). Also touches the caller's own last-activity timestamp. 1P/local matches
        /// (empty <c>Players</c>) are unaffected: no extra read, no save, same behavior as Milestone 2.
        /// </summary>
        [CloudCodeFunction("GetMatchState")]
        public async Task<MatchStateDto> GetMatchState(IExecutionContext context, IGameApiClient gameApiClient, string matchId)
        {
            try
            {
                var record = await MatchStateStore.LoadAsync(context, gameApiClient, matchId);
                var match = MatchStateStore.Replay(record);
                bool isRanked = record.Players.Count == 2 && string.Equals(record.Mode, RankedMode, StringComparison.OrdinalIgnoreCase);

                if (record.Players.Count == 2)
                {
                    await ResolveAbandonmentIfNeededAsync(context, gameApiClient, record, match);
                }

                var dto = MatchStateStore.ToDto(record, match, context.PlayerId);
                if (isRanked)
                {
                    await RankedMatchSupport.EnrichDtoWithSeriesInfoAsync(context, gameApiClient, record, dto);
                }

                return dto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetMatchState failed for matchId={MatchId}.", matchId);
                throw new Exception($"Failed to get match state: {ex.Message}");
            }
        }

        /// <summary>
        /// Reactive resolution shared by Quickmatch and Ranked. Quickmatch: unchanged, only the
        /// 3-minute idle-rival check (risk #4). Ranked (Milestone 5, design-doc.md section 6.1
        /// "Extiende sección 2") ADDS a turn-timer check (20s/3x3, 30s/6x6, whoever's turn it
        /// currently is, regardless of which of the 2 players is the caller - unlike the idle check,
        /// which only ever evaluates "is MY rival idle") - either condition resolves the match as an
        /// abandonment loss for whoever failed to act, and for Ranked additionally triggers full MMR
        /// settlement via <see cref="RankedMatchSupport.HandleAbandonmentAsync"/> (design-doc.md
        /// section 6.4: "Abandono = derrota completa, sin excepciones", and for a 3x3 series, "se
        /// pierde la serie entera" regardless of the partial game score).
        /// </summary>
        private async Task ResolveAbandonmentIfNeededAsync(
            IExecutionContext context, IGameApiClient gameApiClient, MatchStateRecord record, MatchController match)
        {
            var caller = record.Players.FirstOrDefault(p => p.PlayerId == context.PlayerId);
            if (caller == null)
            {
                // Not one of the two matched players (e.g. a stray/expired call) - nothing to touch.
                return;
            }

            bool isRanked = string.Equals(record.Mode, RankedMode, StringComparison.OrdinalIgnoreCase);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool resolvedThisCall = false;
            string abandonerPlayerId = null;

            // The BothPlayersAbandoned guard tolerates a proactive sweep
            // (MatchFunctions.SweepAbandonedMatches) having already closed this match as a
            // both-abandoned draw between this caller's load and this point - without it, a caller
            // who returns after the sweep already ran would overwrite that draw with a reactive win
            // for themselves, which is wrong (the sweep already established neither side was active).
            if (match.Status == MatchStatus.InProgress && string.IsNullOrEmpty(record.AbandonedWinnerPlayerId) && !record.BothPlayersAbandoned)
            {
                if (isRanked)
                {
                    var rankedConfig = await RankedConfigReader.GetAsync(context, gameApiClient);
                    long turnTimeoutSeconds = Math.Max(1, rankedConfig.TurnTimeoutSecondsFor(record.BoardSize));

                    if (record.TurnStartedAtUnixSeconds > 0 && now - record.TurnStartedAtUnixSeconds > turnTimeoutSeconds)
                    {
                        string currentTurnSymbol = match.CurrentPlayer == CellOwner.X ? "X" : "O";
                        var turnPlayer = record.Players.First(p => p.Symbol == currentTurnSymbol);
                        var waitingPlayer = record.Players.First(p => p.Symbol != currentTurnSymbol);

                        record.AbandonedWinnerPlayerId = waitingPlayer.PlayerId;
                        record.AbandonedAtUnixSeconds = now;
                        record.AwardedSoftCurrencyByPlayerId[waitingPlayer.PlayerId] = 0;
                        abandonerPlayerId = turnPlayer.PlayerId;
                        resolvedThisCall = true;
                    }
                }

                if (!resolvedThisCall)
                {
                    var rival = record.Players.First(p => p.PlayerId != context.PlayerId);
                    var config = await MatchmakingConfigReader.GetAsync(context, gameApiClient);
                    long abandonTimeoutSeconds = Math.Max(1, config.AbandonTimeoutMinutes) * 60L;

                    if (now - rival.LastActivityAtUnixSeconds > abandonTimeoutSeconds)
                    {
                        record.AbandonedWinnerPlayerId = caller.PlayerId;
                        record.AbandonedAtUnixSeconds = now;

                        // design-doc.md section 4: "Victoria por abandono del rival = 0" - written
                        // explicitly (rather than left as an absent dictionary entry) so a win-by-
                        // abandonment is recorded as "resolved, paid 0", not "not yet resolved". No
                        // Cloud Save currency/ledger write is needed for a 0 amount.
                        record.AwardedSoftCurrencyByPlayerId[caller.PlayerId] = 0;
                        abandonerPlayerId = rival.PlayerId;
                        resolvedThisCall = true;
                    }
                }

                if (resolvedThisCall && isRanked)
                {
                    await RankedMatchSupport.HandleAbandonmentAsync(context, gameApiClient, record, abandonerPlayerId);
                }
            }

            caller.LastActivityAtUnixSeconds = now;
            record.LastActivityAtUnixSeconds = now;

            await MatchStateStore.SaveAsync(context, gameApiClient, record);

            if (resolvedThisCall)
            {
                // Risk #4 proactive sweep: this match no longer needs sweeping now that the
                // reactive path already resolved it (see MatchIndexStore).
                await MatchIndexStore.RemoveMatchAsync(context, gameApiClient, record);
            }
        }

        /// <summary>
        /// Proactive half of risk #4 (README "Barrido proactivo de partidas abandonadas",
        /// Docs/03-Arquitectura-UGS-TicTacToe.md#4. Partidas online abandonadas): resolves online
        /// matches abandoned by BOTH players, which the reactive path in
        /// <see cref="GetMatchState"/> can never reach on its own (it only runs when a caller shows
        /// up - if nobody ever does, the match never resolves). Intended to be invoked by a Scheduler
        /// + Trigger config (see README "Disparo programado") rather than by any client - it has no
        /// player-specific effect and touches no single player's own data, only cross-player state
        /// via <c>context.ServiceToken</c> (same credential as every other store in this module), so
        /// it works identically whether the caller is a real player context or a triggered/system one
        /// (which typically has no meaningful <c>PlayerId</c>/<c>AccessToken</c> - nothing here reads
        /// either).
        ///
        /// Design decision - "both abandoned" has no natural winner: unlike the reactive path (where
        /// the still-active caller is, by construction, an active player and reasonably wins), a
        /// sweep only ever fires because NEITHER player has been seen recently. Declaring either side
        /// the winner would be arbitrary (why X and not O?) and rewards nothing (both get 0 soft
        /// currency either way, matching "Derrota/abandono online = 0" - see
        /// <see cref="OnlineRewardCalculator"/>), so this resolves as a draw/no-winner
        /// (<see cref="MatchStateRecord.BothPlayersAbandoned"/>) instead of picking a side. This
        /// matters most for Ranked (Milestone 5, not implemented here): a draw is the correct MMR
        /// input for "nobody was actually playing", whereas crediting either side a phantom win would
        /// distort the ladder.
        ///
        /// Idempotent and safe to run concurrently with the reactive path or with itself: every match
        /// is re-checked against its OWN current state right before resolving (never trusts the index
        /// alone), so a match already closed by <see cref="GetMatchState"/> (or a real move, or a
        /// previous sweep run) between being indexed and being swept is silently pruned from the
        /// index instead of being resolved again - see the per-matchId loop below.
        ///
        /// Bounded cost: reads at most <see cref="IndexLookbackDays"/> day-partition index documents
        /// and evaluates at most <paramref name="maxMatches"/> (clamped to
        /// <see cref="MaxMatchesPerSweepHardCap"/>, defaulting to <see cref="DefaultMaxMatchesPerSweep"/>)
        /// matchIds; whatever does not fit in one run stays indexed for the next scheduled run rather
        /// than blocking this one - see README for the recommended sweep frequency/limit pairing.
        /// </summary>
        [CloudCodeFunction("SweepAbandonedMatches")]
        public async Task<SweepResultDto> SweepAbandonedMatches(IExecutionContext context, IGameApiClient gameApiClient, int? maxMatches = null)
        {
            try
            {
                int limit = maxMatches is > 0 ? Math.Min(maxMatches.Value, MaxMatchesPerSweepHardCap) : DefaultMaxMatchesPerSweep;
                var config = await MatchmakingConfigReader.GetAsync(context, gameApiClient);
                long abandonTimeoutSeconds = Math.Max(1, config.AbandonTimeoutMinutes) * 60L;

                int processed = 0;
                int resolved = 0;
                int pruned = 0;
                var today = DateTimeOffset.UtcNow;

                for (int dayOffset = 0; dayOffset < IndexLookbackDays && processed < limit; dayOffset++)
                {
                    string dayUtc = today.AddDays(-dayOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var index = await MatchIndexStore.GetIndexAsync(context, gameApiClient, dayUtc);
                    if (index.MatchIds.Count == 0)
                    {
                        continue;
                    }

                    var remaining = new List<string>();
                    bool indexChanged = false;

                    foreach (string matchId in index.MatchIds)
                    {
                        if (processed >= limit)
                        {
                            // Over budget for this run - leave untouched for the next scheduled sweep.
                            remaining.Add(matchId);
                            continue;
                        }

                        processed++;

                        MatchStateRecord record;
                        try
                        {
                            record = await MatchStateStore.LoadAsync(context, gameApiClient, matchId);
                        }
                        catch (Exception ex)
                        {
                            // Missing/corrupt record - nothing left to resolve, drop it defensively so
                            // it doesn't keep costing a read on every future sweep.
                            _logger.LogWarning(ex, "SweepAbandonedMatches: dropping unloadable matchId {MatchId} from the {DayUtc} index.", matchId, dayUtc);
                            indexChanged = true;
                            pruned++;
                            continue;
                        }

                        var match = MatchStateStore.Replay(record);
                        bool alreadyTerminal = record.Players.Count != 2
                            || match.Status != MatchStatus.InProgress
                            || !string.IsNullOrEmpty(record.AbandonedWinnerPlayerId)
                            || record.BothPlayersAbandoned;

                        if (alreadyTerminal)
                        {
                            // Resolved since it was indexed - by a real move, the reactive path, or an
                            // earlier sweep run - nothing to do here but stop tracking it.
                            indexChanged = true;
                            pruned++;
                            continue;
                        }

                        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        bool player0Idle = now - record.Players[0].LastActivityAtUnixSeconds > abandonTimeoutSeconds;
                        bool player1Idle = now - record.Players[1].LastActivityAtUnixSeconds > abandonTimeoutSeconds;

                        if (!player0Idle || !player1Idle)
                        {
                            // At least one side has been active within the timeout - still a
                            // legitimately live match (or only reactively resolvable, if/when the
                            // idle side's rival next calls GetMatchState). Keep indexed.
                            remaining.Add(matchId);
                            continue;
                        }

                        record.BothPlayersAbandoned = true;
                        record.AbandonedAtUnixSeconds = now;
                        // "Aplicá recompensa 0 en ese caso" (both abandoned = no active player to
                        // reward) - written explicitly per player, same reasoning as the reactive
                        // path's AbandonedWinnerPlayerId branch above.
                        record.AwardedSoftCurrencyByPlayerId[record.Players[0].PlayerId] = 0;
                        record.AwardedSoftCurrencyByPlayerId[record.Players[1].PlayerId] = 0;

                        if (string.Equals(record.Mode, RankedMode, StringComparison.OrdinalIgnoreCase))
                        {
                            // design-doc.md section 6.4: "Ambos abandonan... ΔMMR = 0" (no-contest,
                            // not an Elo draw) - closes the owning series too, if 3x3. No MMR/
                            // leaderboard write happens here (see RankedMatchSupport remarks).
                            await RankedMatchSupport.HandleBothAbandonedAsync(context, gameApiClient, record);
                        }

                        await MatchStateStore.SaveAsync(context, gameApiClient, record);
                        resolved++;
                        indexChanged = true;
                        // Not re-added to `remaining` - this match is terminal now, drop from index.
                    }

                    if (indexChanged)
                    {
                        await MatchIndexStore.SaveIndexAsync(context, gameApiClient, dayUtc, new ActiveMatchIndexRecord { MatchIds = remaining });
                    }
                }

                return new SweepResultDto { MatchesProcessed = processed, MatchesResolvedAbandoned = resolved, MatchesPruned = pruned };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SweepAbandonedMatches failed.");
                throw new Exception($"Failed to sweep abandoned matches: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves the AI's move server-side so it is never inspectable/moddeable from the client
        /// (Docs/03-Arquitectura-UGS-TicTacToe.md#Cloud Code table). This function only computes the
        /// move - the caller applies it with a separate <see cref="PlayMove"/> call, keeping a single
        /// validation/application path for every move regardless of who chose it.
        ///
        /// <paramref name="difficulty"/> is one of "easy" | "medium" | "hard" | "adaptive" (same
        /// codes as GameAnalytics.DifficultyCode on the client). For "adaptive",
        /// <paramref name="adaptiveLevel"/> (0-100, default 50) selects the interpolated parameters
        /// via <see cref="AdaptiveAiController"/> - wiring the persisted `profile` adaptive level
        /// into this call automatically is left for the milestone that moves adaptive AI state fully
        /// server-side (Milestone 2 only mirrors it client-side, see PlayerDataService).
        /// </summary>
        [CloudCodeFunction("GetAiMove")]
        public async Task<AiMoveDto> GetAiMove(
            IExecutionContext context, IGameApiClient gameApiClient, string matchId, string difficulty, int adaptiveLevel = 50)
        {
            try
            {
                var record = await MatchStateStore.LoadAsync(context, gameApiClient, matchId);
                var match = MatchStateStore.Replay(record);

                if (match.Status != MatchStatus.InProgress)
                {
                    throw new Exception($"Match '{matchId}' has already finished.");
                }

                AiParams aiParams = difficulty.ToLowerInvariant() switch
                {
                    "easy" => AiParams.ForDifficulty(AiDifficulty.Easy, record.BoardSize),
                    "medium" => AiParams.ForDifficulty(AiDifficulty.Medium, record.BoardSize),
                    "hard" => AiParams.ForDifficulty(AiDifficulty.Hard, record.BoardSize),
                    "adaptive" => new AdaptiveAiController(adaptiveLevel).GetParams(record.BoardSize),
                    _ => throw new Exception($"Unknown AI difficulty '{difficulty}'."),
                };

                var aiPlayer = new AiPlayer();
                var move = aiPlayer.ChooseMove(match, aiParams);
                return new AiMoveDto { Row = move.Row, Col = move.Col };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAiMove failed for matchId={MatchId}, difficulty={Difficulty}.", matchId, difficulty);
                throw new Exception($"Failed to compute AI move: {ex.Message}");
            }
        }
    }
}
