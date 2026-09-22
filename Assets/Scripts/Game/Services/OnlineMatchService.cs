using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using Unity.Services.CloudCode.Subscriptions;
using UnityEngine;

namespace TTTXO.Game.Services
{
    /// <summary>One matched player and the symbol assigned to them. Kept 1:1 with the server-side <c>MatchPlayerDto</c> (CloudCode~/TicTacToeModule/Dtos.cs).</summary>
    [Serializable]
    public class MatchPlayerDto
    {
        public string PlayerId;
        public string Symbol;
    }

    /// <summary>Client-facing Ranked MMR result (design-doc.md section 6.7 <c>ranked_mmr_changed</c>'s shape, and wireframe 7's "card de MMR en Resultado"). Kept 1:1 with the server-side <c>RankedMmrResultDto</c> - populated on <see cref="MatchStateDto"/> only on the exact call whose move/resolution just settled a Ranked unit (series or 6x6 match); null on every other call, including later polls of the same already-settled match.</summary>
    [Serializable]
    public class RankedMmrResultDto
    {
        public int BoardSize;
        public int MmrBefore;
        public int MmrAfter;
        public int Delta;
        public int OpponentMmr;
        public int KFactor;

        /// <summary>"win" | "draw" | "loss" | "no_contest" (both abandoned - design-doc.md section 6.4).</summary>
        public string Result;

        public int Season;
    }

    /// <summary>Client-facing match state, returned by CreateMatch/PlayMove/GetMatchState. Kept 1:1 with the server-side <c>MatchStateDto</c>.</summary>
    [Serializable]
    public class MatchStateDto
    {
        public string MatchId;
        public int BoardSize;
        public int WinLength;
        public string Mode;
        public string[] Board;
        public string CurrentPlayer;

        /// <summary>"in_progress" | "x_won" | "o_won" | "draw" - same codes as GameAnalytics.MatchStatusCode.</summary>
        public string Status;

        public int TurnCount;
        public int[][] WinningLine;
        public MatchPlayerDto[] Players;
        public bool EndedByAbandonment;
        public bool IsWaitingForOpponent;

        /// <summary>
        /// Soft currency actually granted to the local caller for this match, computed and credited
        /// entirely server-side (design-doc.md section 4, "Modo online Quickmatch" - the client
        /// never calculates or writes online currency, see Docs/01-Directrices-Proyecto.md
        /// #Seguridad / anti-cheat). 0 until the match is terminal. Only ever displayed by
        /// GameScreenController/ResultScreenController, never used to compute anything locally.
        /// </summary>
        public int AwardedSoftCurrency;

        // ---- Milestone 5 - Ranked additions (design-doc.md section 6), mirror server-side MatchStateDto ----

        /// <summary>Non-null only for a 3x3 Ranked game - see CloudCode~/TicTacToeModule README "Serie de 2 partidas".</summary>
        public string RankedSeriesId;

        /// <summary>1 or 2, 0 when not part of a series.</summary>
        public int RankedSeriesGameIndex;

        /// <summary>
        /// Populated the moment game 2 of a 3x3 Ranked series is auto-created (right when game 1
        /// ends), so the client can navigate straight to it without a new Matchmaker search
        /// (design-doc.md section 6.1). See GameScreenController.HandleOnlineMatchEnd.
        /// </summary>
        public string RankedNextMatchId;

        /// <summary>True once this match's owning Ranked unit (the series for 3x3, or the match itself for 6x6) has a final result - game 1 of a 3x3 series ending is NOT series-complete (see <see cref="RankedNextMatchId"/>).</summary>
        public bool RankedSeriesComplete;

        /// <summary>Non-null only on the exact call that just settled Ranked MMR for the caller (wireframe 7's "card de MMR en Resultado").</summary>
        public RankedMmrResultDto RankedMmrResult;
    }

    /// <summary>
    /// Cloud Code RPC wrappers for an online Quickmatch (Milestone 4:
    /// Docs/03-Arquitectura-UGS-TicTacToe.md#Cloud Code / #Wire) plus the Wire push subscription that
    /// tells a screen "something changed, go re-fetch" - the payload itself is never trusted as the
    /// source of truth (arch doc: "el cliente que recibe el push vuelve a pedir GetMatchState"), only
    /// used as a signal to call <see cref="GetMatchStateAsync"/> again.
    ///
    /// Wire push subscription reuses the already-referenced <c>com.unity.services.cloudcode</c>
    /// package's own <c>CloudCodeService.Instance.SubscribeToPlayerMessagesAsync</c> - Wire itself
    /// (<c>com.unity.services.wire</c>) is an internal-only package with no public API (verified via
    /// WebSearch/package docs) that ships as a transitive dependency of CloudCode/Multiplayer Services;
    /// it is intentionally not referenced directly (see this task's final report).
    /// </summary>
    public static class OnlineMatchService
    {
        private const string ModuleName = "TicTacToeModule";
        private const string CreateMatchFunction = "CreateMatch";
        private const string PlayMoveFunction = "PlayMove";
        private const string GetMatchStateFunction = "GetMatchState";

        /// <summary>Wire subscription lifecycle, shown as the online-only status chip (Docs/06-Wireframes-UI.md screen 6).</summary>
        public enum WireConnectionStatus
        {
            Offline,
            Connecting,
            Subscribed,
        }

        public static WireConnectionStatus ConnectionStatus { get; private set; } = WireConnectionStatus.Offline;

        /// <summary>Raised when a Wire push for the current match arrives - subscribers should re-call <see cref="GetMatchStateAsync"/>, never read data off the event itself.</summary>
        public static event Action MatchUpdatedPushReceived;

        private static bool _subscribed;

        /// <summary>Local player id shortcut (Authentication is already signed in anonymously by <see cref="UgsInitializer"/> before any online screen is reachable).</summary>
        public static string LocalPlayerId => AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId : null;

        /// <summary>
        /// Subscribes to this player's Wire channel once (idempotent - safe to call every time the
        /// Matchmaking/Game screen appears). Never throws: a failed subscription just leaves
        /// <see cref="ConnectionStatus"/> at <see cref="WireConnectionStatus.Offline"/>, and callers
        /// fall back to polling (see GameScreenController's ~10s backup poll).
        /// </summary>
        public static async Task SubscribeAsync()
        {
            if (_subscribed)
            {
                return;
            }

            ConnectionStatus = WireConnectionStatus.Connecting;
            try
            {
                var callbacks = new SubscriptionEventCallbacks();
                callbacks.MessageReceived += _ => MatchUpdatedPushReceived?.Invoke();
                callbacks.ConnectionStateChanged += state => ConnectionStatus = MapConnectionState(state);
                callbacks.Kicked += () => { _subscribed = false; ConnectionStatus = WireConnectionStatus.Offline; };
                callbacks.Error += _ => ConnectionStatus = WireConnectionStatus.Offline;

                await CloudCodeService.Instance.SubscribeToPlayerMessagesAsync(callbacks);
                _subscribed = true;
                ConnectionStatus = WireConnectionStatus.Subscribed;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"OnlineMatchService: Wire subscription failed, falling back to polling - {ex.Message}");
                ConnectionStatus = WireConnectionStatus.Offline;
            }
        }

        /// <summary>Called when leaving online play (Home/ModeSelect) so a stale subscription doesn't keep firing pushes for a match the player already left.</summary>
        public static void ResetSubscriptionState()
        {
            _subscribed = false;
            ConnectionStatus = WireConnectionStatus.Offline;
            MatchUpdatedPushReceived = null;
        }

        /// <summary>Maps the CloudCode package's own <see cref="EventConnectionState"/> (verified against the installed Com.Unity.Services.CloudCode 2.10.4 package source) to our 3-state chip.</summary>
        private static WireConnectionStatus MapConnectionState(EventConnectionState state)
        {
            return state switch
            {
                EventConnectionState.Subscribed => WireConnectionStatus.Subscribed,
                EventConnectionState.Subscribing => WireConnectionStatus.Connecting,
                _ => WireConnectionStatus.Offline, // Unknown, Unsubscribed, Unsynced, Error
            };
        }

        /// <summary>
        /// Creates (or, idempotently, fetches/completes) the online match for a resolved Matchmaker
        /// ticket (risk #1). <paramref name="ticketId"/> is this client's own Matchmaker ticket;
        /// <paramref name="handoffId"/> is the shared <c>MatchIdAssignment.MatchId</c> both matched
        /// clients received (see MatchmakingService.PollTicketAsync) - the module uses it to pair up
        /// the two independent CreateMatch calls without either client needing to know the other's
        /// playerId in advance (see CloudCode~/TicTacToeModule/README.md "Flujo CreateMatch"). Retries
        /// with a short backoff on failure - either matched player may call this, so a transient
        /// failure here does not strand the pair (Docs/03-Arquitectura-UGS-TicTacToe.md "Resolucion
        /// detallada de riesgos #1").
        ///
        /// <paramref name="modeCode"/> is <see cref="GameAnalytics.OnlineQuickmatchModeCode"/> or
        /// <see cref="GameAnalytics.RankedModeCode"/> (Milestone 5) - same idempotent call is also how
        /// a Ranked 3x3 series' waiting player re-joins: this call is safe/idempotent to repeat with
        /// the same <paramref name="ticketId"/>/<paramref name="handoffId"/> pair (see
        /// MatchmakingService.RetryCreateMatchAsync and CloudCode~/TicTacToeModule/README.md "Serie de
        /// 2 partidas (Ranked 3x3)" point 2 - "el cliente debe volver a invocar CreateMatch... no
        /// GetMatchState").
        /// </summary>
        public static async Task<MatchStateDto> CreateMatchAsync(int boardSize, string modeCode, string ticketId, string handoffId)
        {
            const int maxAttempts = 3;
            int[] backoffMilliseconds = { 500, 1500 };

            Exception lastException = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    var args = new Dictionary<string, object>
                    {
                        { "boardSize", boardSize },
                        { "mode", modeCode },
                        { "ticketId", ticketId },
                        { "handoffId", handoffId },
                    };

                    return await CloudCodeService.Instance.CallModuleEndpointAsync<MatchStateDto>(ModuleName, CreateMatchFunction, args);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Debug.LogWarning($"OnlineMatchService: CreateMatch attempt {attempt + 1}/{maxAttempts} failed - {ex.Message}");
                    if (attempt < backoffMilliseconds.Length)
                    {
                        await Task.Delay(backoffMilliseconds[attempt]);
                    }
                }
            }

            throw lastException ?? new Exception("CreateMatch failed with no exception recorded.");
        }

        public static Task<MatchStateDto> PlayMoveAsync(string matchId, int row, int col)
        {
            var args = new Dictionary<string, object>
            {
                { "matchId", matchId },
                { "row", row },
                { "col", col },
            };

            return CloudCodeService.Instance.CallModuleEndpointAsync<MatchStateDto>(ModuleName, PlayMoveFunction, args);
        }

        public static Task<MatchStateDto> GetMatchStateAsync(string matchId)
        {
            var args = new Dictionary<string, object> { { "matchId", matchId } };
            return CloudCodeService.Instance.CallModuleEndpointAsync<MatchStateDto>(ModuleName, GetMatchStateFunction, args);
        }
    }
}
