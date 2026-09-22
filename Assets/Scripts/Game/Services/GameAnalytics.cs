using System;
using TTTXO.Core;
using Unity.Services.Analytics;
using UnityEngine;

namespace TTTXO.Game.Services
{
    /// <summary>
    /// Defensive, typed wrapper around Unity Analytics custom events
    /// (Docs/01-Directrices-Proyecto.md: "El wrapper de Analytics nunca debe poder romper el
    /// juego: toda emision de evento va en try/catch y degrada a warning si falla"). Every public
    /// method here is a no-op-with-warning if the UGS session is not <see cref="UgsInitStatus.Ready"/>
    /// or the SDK throws - callers never need their own try/catch.
    ///
    /// IMPORTANT: every event name below must be registered in the UGS Dashboard's Event Manager,
    /// with these exact parameter names/types, BEFORE it is emitted in a build that reaches
    /// players - otherwise the ingestion pipeline silently drops the event
    /// (Docs/01-Directrices-Proyecto.md: "Un evento sin su definicion correspondiente en el Event
    /// Manager del Dashboard se descarta en el pipeline"). See this task's final report for the
    /// exact list of events/parameters to register.
    /// </summary>
    public static class GameAnalytics
    {
        /// <summary>
        /// Canonical snake_case mode code for an online Quickmatch (Milestone 4). Not a
        /// <see cref="GameMode"/> value - TTTXO.Core stays 1P/local-only by design
        /// (Docs/01-Directrices-Proyecto.md: "TTTXO.Core no se modifica") - online session state is
        /// tracked separately by <see cref="MatchmakingService"/>/<see cref="OnlineMatchService"/>.
        /// Ranked (Milestone 5) is not implemented and has no code here yet.
        /// </summary>
        public const string OnlineQuickmatchModeCode = "online_quickmatch";

        /// <summary>Milestone 5 - canonical snake_case mode code for Ranked (design-doc.md section 6), mirrors <c>CloudCode~/TicTacToeModule/RankedMatchSupport.ModeCode</c> server-side ("ranked").</summary>
        public const string RankedModeCode = "ranked";

        /// <summary>Canonical snake_case event-parameter value for a <see cref="GameMode"/>.</summary>
        public static string ModeCode(GameMode mode)
        {
            return mode switch
            {
                GameMode.SinglePlayer => "single_player",
                GameMode.LocalMultiplayer => "local_multiplayer",
                _ => mode.ToString(),
            };
        }

        /// <summary>Canonical snake_case event-parameter value for an <see cref="AiDifficulty"/>.</summary>
        public static string DifficultyCode(AiDifficulty difficulty)
        {
            return difficulty switch
            {
                AiDifficulty.Easy => "easy",
                AiDifficulty.Medium => "medium",
                AiDifficulty.Hard => "hard",
                AiDifficulty.Adaptive => "adaptive",
                _ => difficulty.ToString(),
            };
        }

        /// <summary>Canonical snake_case event-parameter value for a <see cref="MatchStatus"/>.</summary>
        public static string MatchStatusCode(MatchStatus status)
        {
            return status switch
            {
                MatchStatus.XWon => "x_won",
                MatchStatus.OWon => "o_won",
                MatchStatus.Draw => "draw",
                _ => "in_progress",
            };
        }

        /// <summary>Emitted when a match begins (see GameScreenController.OnShow).</summary>
        public static void MatchStarted(GameMode mode, int boardSize, AiDifficulty? aiDifficulty)
        {
            SafeRecord("match_started", evt =>
            {
                evt.Add("mode", ModeCode(mode));
                evt.Add("board_size", boardSize);
                if (aiDifficulty.HasValue)
                {
                    evt.Add("ai_difficulty", DifficultyCode(aiDifficulty.Value));
                }
            });
        }

        /// <summary>
        /// Emitted when a match reaches a terminal status (see GameScreenController.HandleMatchEnd).
        /// <paramref name="won"/> is from the recording perspective (human in Single Player, Player 1
        /// in Local Multiplayer - same perspective as GameManager.RecordMatchHistory).
        /// <paramref name="firstPlayerWon"/> is design-doc.md section 5's proposed extension: whether
        /// X (who always moves first, per design-doc.md section 1) won - independent of which side
        /// the recorded player was on, to measure first-move advantage across board sizes.
        /// </summary>
        public static void MatchFinished(
            bool won, int boardSize, int turns, float durationSeconds, MatchStatus status,
            bool firstPlayerWon, AiDifficulty? aiDifficulty)
        {
            SafeRecord("match_finished", evt =>
            {
                evt.Add("won", won);
                evt.Add("board_size", boardSize);
                evt.Add("turns", turns);
                evt.Add("duration", Mathf.RoundToInt(durationSeconds));
                evt.Add("reason", MatchStatusCode(status));
                evt.Add("first_player_won", firstPlayerWon);
                if (aiDifficulty.HasValue)
                {
                    evt.Add("ai_difficulty", DifficultyCode(aiDifficulty.Value));
                }
            });
        }

        /// <summary>Online-mode overload of <see cref="MatchStarted"/> (Milestone 4) - takes a plain mode code instead of a <see cref="GameMode"/> since TTTXO.Core has no online mode (see <see cref="OnlineQuickmatchModeCode"/>).</summary>
        public static void MatchStarted(string modeCode, int boardSize)
        {
            SafeRecord("match_started", evt =>
            {
                evt.Add("mode", modeCode);
                evt.Add("board_size", boardSize);
            });
        }

        /// <summary>
        /// Online-mode overload of <see cref="MatchFinished"/> (Milestone 4) - takes plain mode/status
        /// codes instead of <see cref="GameMode"/>/<see cref="MatchStatus"/> since an online match's
        /// terminal state includes abandonment, which has no <see cref="MatchStatus"/> of its own (see
        /// <see cref="OnlineMatchService"/>). <paramref name="modeCode"/> is design-doc.md section 6.7's
        /// extension ("El overload online hoy no emite mode. Debe emitirlo, con valores
        /// online_quickmatch | ranked") - defaults to <see cref="OnlineQuickmatchModeCode"/> so every
        /// pre-Ranked call site keeps compiling/behaving unchanged; Ranked call sites must pass
        /// <c>"ranked"</c> explicitly.
        /// </summary>
        public static void MatchFinished(bool won, int boardSize, int turns, float durationSeconds, string statusCode, bool firstPlayerWon, string modeCode = OnlineQuickmatchModeCode)
        {
            SafeRecord("match_finished", evt =>
            {
                evt.Add("won", won);
                evt.Add("board_size", boardSize);
                evt.Add("turns", turns);
                evt.Add("duration", Mathf.RoundToInt(durationSeconds));
                evt.Add("reason", statusCode);
                evt.Add("first_player_won", firstPlayerWon);
                evt.Add("mode", modeCode);
            });
        }

        /// <summary>Emitted when a board size is picked on Board Select (see BoardSelectScreenController.SelectBoard).</summary>
        public static void BoardSizeSelected(int boardSize, GameMode mode)
        {
            SafeRecord("board_size_selected", evt =>
            {
                evt.Add("board_size", boardSize);
                evt.Add("mode", ModeCode(mode));
            });
        }

        /// <summary>Emitted whenever the wallet actually grows (see GameManager.AwardMatchReward).</summary>
        public static void SoftCurrencyEarned(int amount, string source)
        {
            SafeRecord("soft_currency_earned", evt =>
            {
                evt.Add("amount", amount);
                evt.Add("source", source);
            });
        }

        /// <summary>
        /// Emitted once a Quickmatch/Ranked search ends, matched or not (see MatchmakingService) -
        /// risk #2's monitoring hook (Docs/03-Arquitectura-UGS-TicTacToe.md "Resolucion detallada de
        /// riesgos #2": "Monitorear matchmaking_wait_time desde el lanzamiento"). design-doc.md
        /// section 6.7 extension: <paramref name="modeCode"/> ("quickmatch"|"ranked", defaults to
        /// "quickmatch" so existing call sites keep compiling) and <paramref name="mmrGap"/>
        /// (absolute MMR difference of the resulting match, 0 if unmatched or non-Ranked - "sin
        /// mmr_gap la curva de relajación de 6.2 no se puede tunear con datos").
        /// </summary>
        public static void MatchmakingWaitTime(float seconds, int boardSize, bool matched, string modeCode = "quickmatch", int mmrGap = 0)
        {
            SafeRecord("matchmaking_wait_time", evt =>
            {
                evt.Add("seconds", Mathf.RoundToInt(seconds));
                evt.Add("board_size", boardSize);
                evt.Add("matched", matched);
                evt.Add("mode", modeCode);
                evt.Add("mmr_gap", mmrGap);
            });
        }

        /// <summary>
        /// Emitted whenever a Quickmatch ticket is cancelled before matching (see
        /// MatchmakingService.CancelAsync) - risk #3 (Docs/03-Arquitectura-UGS-TicTacToe.md
        /// "Resolucion detallada de riesgos #3"): "para poder medir cuanto abandono hay en la cola".
        /// </summary>
        /// <param name="reason">"user_cancel" | "app_background" | "timeout" | "error".</param>
        public static void MatchmakingCancelled(string reason)
        {
            SafeRecord("matchmaking_cancelled", evt =>
            {
                evt.Add("reason", reason);
            });
        }

        // ---- Milestone 5 - Ranked (design-doc.md section 6.7) --------------------------------

        /// <summary>Troncal event of the Ranked system - emitted whenever a player's MMR changes for any reason (a settled match/series, a season soft-reset, or a lazy inactivity-decay tick surfaced back to the client).</summary>
        /// <param name="result">"win" | "draw" | "loss" | "no_contest".</param>
        /// <param name="reason">"match" | "season_reset" | "inactivity_decay".</param>
        public static void RankedMmrChanged(int boardSize, int mmrBefore, int mmrAfter, int delta, int opponentMmr, int kFactor, string result, string reason, int season)
        {
            SafeRecord("ranked_mmr_changed", evt =>
            {
                evt.Add("board_size", boardSize);
                evt.Add("mmr_before", mmrBefore);
                evt.Add("mmr_after", mmrAfter);
                evt.Add("delta", delta);
                evt.Add("opponent_mmr", opponentMmr);
                evt.Add("k_factor", kFactor);
                evt.Add("result", result);
                evt.Add("reason", reason);
                evt.Add("season", season);
            });
        }

        /// <summary>Emitted once a player completes their placement matches for a board size/season (design-doc.md section 6.2 "Colocación") - measures whether the post-placement MMR predicts later performance.</summary>
        public static void RankedPlacementCompleted(int boardSize, int finalMmr, int wins, int losses, int draws, int season)
        {
            SafeRecord("ranked_placement_completed", evt =>
            {
                evt.Add("board_size", boardSize);
                evt.Add("final_mmr", finalMmr);
                evt.Add("wins", wins);
                evt.Add("losses", losses);
                evt.Add("draws", draws);
                evt.Add("season", season);
            });
        }

        /// <summary>Emitted whenever a Ranked unit ends by abandonment (design-doc.md section 6.4) - drives the leaver-cooldown decision (6.4 "Lineamiento para después").</summary>
        /// <param name="role">"abandoner" | "stayer" | "both".</param>
        /// <param name="gameIndex">1 or 2 within a 3x3 series (design-doc.md section 6.1), 0 for 6x6 (single-game unit).</param>
        public static void RankedMatchAbandoned(int boardSize, string role, int turnIndex, int gameIndex, float elapsedSeconds)
        {
            SafeRecord("ranked_match_abandoned", evt =>
            {
                evt.Add("board_size", boardSize);
                evt.Add("role", role);
                evt.Add("turn_index", turnIndex);
                evt.Add("game_index", gameIndex);
                evt.Add("elapsed_seconds", Mathf.RoundToInt(elapsedSeconds));
            });
        }

        /// <summary>Emitted when the "taking too long?" fallback card is shown at the 45s wait ceiling (design-doc.md section 6.2) - validates whether the wait ceiling/relaxation curve are well tuned.</summary>
        /// <param name="choice">"keep_waiting" | "switch_quickmatch" | "play_ai" | "dismiss".</param>
        public static void RankedQueueFallbackShown(int boardSize, float waitedSeconds, string choice)
        {
            SafeRecord("ranked_queue_fallback_shown", evt =>
            {
                evt.Add("board_size", boardSize);
                evt.Add("waited_seconds", Mathf.RoundToInt(waitedSeconds));
                evt.Add("choice", choice);
            });
        }

        /// <summary>Emitted whenever a season-close tier reward is granted (design-doc.md section 6.5) - real economic injection per season and tier distribution.</summary>
        public static void RankedSeasonRewardGranted(int season, int boardSize, string tier, int softCurrency, bool top100)
        {
            SafeRecord("ranked_season_reward_granted", evt =>
            {
                evt.Add("season", season);
                evt.Add("board_size", boardSize);
                evt.Add("tier", tier);
                evt.Add("soft_currency", softCurrency);
                evt.Add("top100", top100);
            });
        }

        // TODO(monetization milestone): ad_watched (+placement, ad_type), ad_skipped,
        // iap_purchased (+sku, price), iap_failed. Not implemented yet - there are no ads/IAP in
        // Milestone 2 (see Docs/02-GDD-TicTacToe.md#9 and
        // Docs/03-Arquitectura-UGS-TicTacToe.md#Analytics y Diagnostics). Register these in the
        // Event Manager and add typed methods here when that milestone starts.

        /// <summary>
        /// The single choke point every event passes through - which is what makes swapping the
        /// destination a local change rather than a parallel system. The lambda receives an
        /// <see cref="AnalyticsEvent"/> instead of a <c>CustomEvent</c> so the typed methods above
        /// are identical under either sink.
        ///
        /// With <c>TTTXO_LOCAL_ANALYTICS</c> defined (beta), events go to disk and Unity Analytics
        /// is not called at all - no billable custom events. Note that the "session is not Ready"
        /// bail-out is deliberately absent on that path: the local sink works offline and in the
        /// Editor, where UGS never becomes Ready and every event is currently dropped.
        /// </summary>
        private static void SafeRecord(string eventName, Action<AnalyticsEvent> fillParameters)
        {
            try
            {
                var evt = new AnalyticsEvent(eventName);
                fillParameters(evt);

#if TTTXO_LOCAL_ANALYTICS
                LocalAnalyticsSink.Record(evt);
#else
                if (UgsInitializer.Status != UgsInitStatus.Ready)
                {
                    Debug.LogWarning($"GameAnalytics: skipped '{eventName}' - UGS session is not Ready.");
                    return;
                }

                AnalyticsService.Instance.RecordEvent(evt.ToCustomEvent());
#endif
            }
            catch (Exception ex)
            {
                // Never let a broken analytics call break the game (Docs/01-Directrices-Proyecto.md).
                Debug.LogWarning($"GameAnalytics: failed to record '{eventName}' - {ex.Message}");
            }
        }
    }
}
