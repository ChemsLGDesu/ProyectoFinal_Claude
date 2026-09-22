using System.Globalization;
using TTTXO.Core;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace TTTXO.CloudCode
{
    /// <summary>
    /// Ranked (Milestone 5, design-doc.md section 6) orchestration on top of the EXACT SAME
    /// primitives Quickmatch already uses (<see cref="MatchStateStore"/>, <see cref="MatchIndexStore"/>,
    /// <see cref="OnlineRewardStore"/>) - "reutilizando el flujo online existente (CreateMatch/
    /// PlayMove/GetMatchState)" per this task's own instructions. <see cref="MatchFunctions"/>' four
    /// public endpoints stay the single entry points; every branch below is called FROM them (see
    /// that file's <c>Mode == "ranked"</c> checks) rather than exposing new Cloud Code functions -
    /// Ranked and Quickmatch share <c>CreateMatch</c>/<c>PlayMove</c>/<c>GetMatchState</c> verbatim,
    /// only diverging in what happens once a match/series actually resolves.
    ///
    /// See CloudCode~/TicTacToeModule/README.md "Ranked (Milestone 5)" for the full model.
    /// </summary>
    public static class RankedMatchSupport
    {
        public const string ModeCode = "ranked";

        // ---- CreateMatch -----------------------------------------------------------------

        /// <summary>
        /// Ranked branch of <c>CreateMatch</c>. 6x6 reuses <see cref="MatchFunctions.CreateOnlineMatchAsync"/>
        /// verbatim (mode threads through unchanged; that method's own <c>CompleteOnlineMatchAsync</c>
        /// call applies Ranked-specific setup when it sees <c>Mode == "ranked"</c> - see that file).
        /// 3x3 is a genuinely different shape (design-doc.md section 6.8 Q1: a series WRAPS up to 2
        /// matches, so the "pending" state has no <see cref="MatchStateRecord"/> at all yet) and gets
        /// its own dance against <see cref="RankedSeriesStore"/>.
        /// </summary>
        public static async Task<MatchStateDto> CreateOrJoinRankedMatchAsync(
            IExecutionContext context, IGameApiClient gameApiClient, int boardSize, BoardConfig boardConfig, string ticketId, string handoffId)
        {
            if (boardSize == 3)
            {
                return await CreateOrJoinRankedSeriesAsync(context, gameApiClient, boardConfig, ticketId, handoffId);
            }

            return await MatchFunctions.CreateOnlineMatchAsync(context, gameApiClient, boardSize, ModeCode, boardConfig, ticketId, handoffId);
        }

        private static async Task<MatchStateDto> CreateOrJoinRankedSeriesAsync(
            IExecutionContext context, IGameApiClient gameApiClient, BoardConfig boardConfig, string ticketId, string handoffId)
        {
            if (string.IsNullOrWhiteSpace(ticketId) || string.IsNullOrWhiteSpace(handoffId))
            {
                throw new Exception("Both ticketId and handoffId are required to create a ranked match.");
            }

            await MatchFunctions.VerifyTicketResolvesToHandoffAsync(context, gameApiClient, ticketId, handoffId);

            string existingSeriesId = await MatchStateStore.TryGetMatchIdForHandoffAsync(context, gameApiClient, handoffId);
            if (string.IsNullOrEmpty(existingSeriesId))
            {
                var pending = await RankedSeriesStore.CreatePendingSeriesAsync(context, gameApiClient, handoffId, context.PlayerId);
                return WaitingSeriesDto(pending);
            }

            var series = await RankedSeriesStore.LoadAsync(context, gameApiClient, existingSeriesId);
            if (series.PlayerIds.Count == 1)
            {
                if (series.PlayerIds[0] == context.PlayerId)
                {
                    // Same caller retrying (network blip) before an opponent joined - idempotent no-op, same posture as MatchFunctions.CreateOnlineMatchAsync.
                    return WaitingSeriesDto(series);
                }

                var rankedConfig = await RankedConfigReader.GetAsync(context, gameApiClient);
                var game1 = await RankedSeriesStore.CompleteRosterAndCreateGame1Async(context, gameApiClient, series, rankedConfig, boardConfig);
                var match = MatchStateStore.Replay(game1);
                var dto = MatchStateStore.ToDto(game1, match, context.PlayerId);
                dto.RankedSeriesId = game1.RankedSeriesId;
                dto.RankedSeriesGameIndex = game1.RankedSeriesGameIndex;
                return dto;
            }

            // Roster already complete - idempotent return of the currently active/last game's state.
            string activeMatchId = series.MatchIds.Count > 0 ? series.MatchIds[^1] : null;
            if (activeMatchId == null)
            {
                return WaitingSeriesDto(series);
            }

            var record = await MatchStateStore.LoadAsync(context, gameApiClient, activeMatchId);
            var replay = MatchStateStore.Replay(record);
            var resultDto = MatchStateStore.ToDto(record, replay, context.PlayerId);
            await EnrichDtoWithSeriesInfoAsync(context, gameApiClient, record, resultDto);
            return resultDto;
        }

        /// <summary>
        /// Placeholder DTO for a series still waiting on the second player - <see cref="MatchStateDto.MatchId"/>
        /// is deliberately empty (no <see cref="MatchStateRecord"/> exists yet). IMPORTANT client
        /// contract (see README): unlike Quickmatch's "waiting for opponent" match (which HAS a real
        /// matchId the client can GetMatchState-poll), a client waiting on a Ranked 3x3 series must
        /// re-invoke CreateMatch itself (already idempotent) until <see cref="MatchStateDto.IsWaitingForOpponent"/>
        /// flips false - there is nothing to GetMatchState yet.
        /// </summary>
        private static MatchStateDto WaitingSeriesDto(RankedSeriesRecord series)
        {
            return new MatchStateDto
            {
                MatchId = string.Empty,
                BoardSize = series.BoardSize,
                Mode = ModeCode,
                IsWaitingForOpponent = true,
                RankedSeriesId = series.SeriesId,
            };
        }

        // ---- PlayMove: a real move just ended a Ranked match ------------------------------

        /// <summary>
        /// Called by <see cref="MatchFunctions.PlayMove"/> instead of <c>AwardOnlineRewardsAsync</c>
        /// when <c>record.Mode == "ranked"</c> and the move just applied ended the match
        /// (<c>result.StatusAfterMove != InProgress</c>). For 6x6 (no <see cref="MatchStateRecord.RankedSeriesId"/>):
        /// settles immediately, the match IS the scored unit. For 3x3 game 1: creates game 2 (no
        /// settlement yet - design-doc.md section 6.1 "cada jugador empieza exactamente una vez").
        /// For 3x3 game 2: settles the series. Mutates <paramref name="record"/> in place (caller is
        /// responsible for the final <see cref="MatchStateStore.SaveAsync"/>, same contract
        /// <c>AwardOnlineRewardsAsync</c> already has) and returns any extra DTO fields the caller
        /// (PlayMove) should layer onto its own <see cref="MatchStateStore.ToDto"/> result.
        /// </summary>
        public static async Task<RankedPlayMoveOutcome> HandleRankedMatchEndedAsync(
            IExecutionContext context, IGameApiClient gameApiClient, MatchStateRecord record, MatchStatus finalStatus)
        {
            if (string.IsNullOrEmpty(record.RankedSeriesId))
            {
                await SettleUnitAsync(context, gameApiClient, record, ScoreFromMatchStatus(finalStatus, record.Players[0].Symbol), ScoreFromMatchStatus(finalStatus, record.Players[1].Symbol));
                return new RankedPlayMoveOutcome { SeriesComplete = true };
            }

            var series = await RankedSeriesStore.LoadAsync(context, gameApiClient, record.RankedSeriesId);

            if (record.RankedSeriesGameIndex == 1)
            {
                var boardConfig = BoardConfig.ForSize(record.BoardSize);
                var game2 = await RankedSeriesStore.CreateGame2Async(context, gameApiClient, series, record, boardConfig);
                return new RankedPlayMoveOutcome { SeriesComplete = false, NextMatchId = game2.MatchId };
            }

            // Game 2 just ended - settle the series from both games' own results.
            var game1Record = await MatchStateStore.LoadAsync(context, gameApiClient, series.MatchIds[0]);
            var game1Match = MatchStateStore.Replay(game1Record);
            var game2Match = MatchStateStore.Replay(record); // record IS game 2 here.

            string playerA = series.PlayerIds[0];
            string playerB = series.PlayerIds[1];
            double seriesScoreA = SeriesScoreFor(playerA, game1Record, game1Match, record, game2Match);
            double seriesScoreB = 1.0 - seriesScoreA == 0.5 ? 0.5 : 1.0 - seriesScoreA; // symmetric: win/loss invert, draw stays 0.5

            await SettleSeriesAsync(context, gameApiClient, series, record, seriesScoreA, seriesScoreB);
            return new RankedPlayMoveOutcome { SeriesComplete = true };
        }

        private static double SeriesScoreFor(string playerId, MatchStateRecord game1, MatchController game1Match, MatchStateRecord game2, MatchController game2Match)
        {
            int wins = 0, losses = 0;
            AccumulateGameResult(playerId, game1, game1Match, ref wins, ref losses);
            AccumulateGameResult(playerId, game2, game2Match, ref wins, ref losses);

            if (wins > losses)
            {
                return RankedEloCalculator.ScoreWin;
            }

            return wins < losses ? RankedEloCalculator.ScoreLoss : RankedEloCalculator.ScoreDraw;
        }

        private static void AccumulateGameResult(string playerId, MatchStateRecord game, MatchController match, ref int wins, ref int losses)
        {
            var player = game.Players.First(p => p.PlayerId == playerId);
            double score = ScoreFromMatchStatus(match.Status, player.Symbol);
            if (score == RankedEloCalculator.ScoreWin)
            {
                wins++;
            }
            else if (score == RankedEloCalculator.ScoreLoss)
            {
                losses++;
            }
        }

        private static double ScoreFromMatchStatus(MatchStatus status, string symbol)
        {
            bool won = (status == MatchStatus.XWon && symbol == "X") || (status == MatchStatus.OWon && symbol == "O");
            if (won)
            {
                return RankedEloCalculator.ScoreWin;
            }

            return status == MatchStatus.Draw ? RankedEloCalculator.ScoreDraw : RankedEloCalculator.ScoreLoss;
        }

        // ---- Abandonment (reactive + proactive) --------------------------------------------

        /// <summary>
        /// Called from <see cref="MatchFunctions.ResolveAbandonmentIfNeededAsync"/> right after it
        /// sets <see cref="MatchStateRecord.AbandonedWinnerPlayerId"/> on a Ranked match (reactive
        /// idle-timeout OR turn-timeout - both are "this player didn't act in time" from this
        /// function's point of view, the caller decides which one fired). Full loss for the
        /// abandoner (design-doc.md section 6.4: "Abandono = derrota completa, sin excepciones"). For
        /// a 3x3 series, this decides the ENTIRE series regardless of the partial game score
        /// ("se pierde la serie entera") - game 2 is never created if this fires during/after game 1.
        /// </summary>
        public static async Task HandleAbandonmentAsync(IExecutionContext context, IGameApiClient gameApiClient, MatchStateRecord record, string abandonerPlayerId)
        {
            if (string.IsNullOrEmpty(record.RankedSeriesId))
            {
                double abandonerScore = RankedEloCalculator.ScoreLoss;
                double stayerScore = RankedEloCalculator.ScoreWin;
                bool abandonerIsPlayer0 = record.Players[0].PlayerId == abandonerPlayerId;
                await SettleUnitAsync(context, gameApiClient, record, abandonerIsPlayer0 ? abandonerScore : stayerScore, abandonerIsPlayer0 ? stayerScore : abandonerScore);
                return;
            }

            var series = await RankedSeriesStore.LoadAsync(context, gameApiClient, record.RankedSeriesId);
            if (series.Complete)
            {
                return; // Already settled by the other resolution path - idempotent no-op.
            }

            bool abandonerIsA = series.PlayerIds[0] == abandonerPlayerId;
            await SettleSeriesAsync(
                context, gameApiClient, series, record,
                abandonerIsA ? RankedEloCalculator.ScoreLoss : RankedEloCalculator.ScoreWin,
                abandonerIsA ? RankedEloCalculator.ScoreWin : RankedEloCalculator.ScoreLoss);
        }

        /// <summary>
        /// Called from <see cref="MatchFunctions.SweepAbandonedMatches"/> right after it marks
        /// <see cref="MatchStateRecord.BothPlayersAbandoned"/> on a Ranked match - design-doc.md
        /// section 6.4 "Ambos abandonan... ΔMMR = 0" (no-contest, NOT an Elo draw - see that
        /// section's justification). Writes no MMR delta and no leaderboard update ("el barrido
        /// proactivo no escribe rating en absoluto"), does not increment placement, but still marks
        /// the unit settled/complete (so nobody re-processes it) and records a zero-delta
        /// <see cref="RankedMmrResultDto"/> for UI clarity.
        /// </summary>
        public static async Task HandleBothAbandonedAsync(IExecutionContext context, IGameApiClient gameApiClient, MatchStateRecord record)
        {
            var rankedConfig = await RankedConfigReader.GetAsync(context, gameApiClient);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            foreach (var player in record.Players)
            {
                var touch = await RankedProfileStore.TouchAsync(context, gameApiClient, player.PlayerId, record.BoardSize, rankedConfig, now);
                await GrantPendingSeasonRewardIfAnyAsync(context, gameApiClient, player.PlayerId, record.BoardSize, touch, rankedConfig);

                record.RankedMmrResultByPlayerId[player.PlayerId] = new RankedMmrResultDto
                {
                    BoardSize = record.BoardSize,
                    MmrBefore = touch.Record.Mmr,
                    MmrAfter = touch.Record.Mmr,
                    Delta = 0,
                    OpponentMmr = 0,
                    KFactor = 0,
                    Result = RankedRewardCalculator.ResultNoContest,
                    Season = touch.Record.SeasonId,
                };
            }

            record.RankedSettled = true;

            if (!string.IsNullOrEmpty(record.RankedSeriesId))
            {
                var series = await RankedSeriesStore.LoadAsync(context, gameApiClient, record.RankedSeriesId);
                if (!series.Complete)
                {
                    series.Complete = true;
                    series.LastActivityAtUnixSeconds = now;
                    await RankedSeriesStore.SaveAsync(context, gameApiClient, series);
                }
            }
        }

        // ---- Settlement (shared by "real move ended it" and "abandonment") ----------------

        private static Task SettleUnitAsync(IExecutionContext context, IGameApiClient gameApiClient, MatchStateRecord record, double scorePlayer0, double scorePlayer1)
        {
            var players = new[] { record.Players[0], record.Players[1] };
            var scores = new[] { scorePlayer0, scorePlayer1 };
            return SettleCoreAsync(context, gameApiClient, record.BoardSize, players, scores, record.RankedMmrBeforeByPlayerId, dto => { record.RankedMmrResultByPlayerId[dto.PlayerId] = dto.Result; record.AwardedSoftCurrencyByPlayerId[dto.PlayerId] = dto.Awarded; }, () => record.RankedSettled = true);
        }

        private static async Task SettleSeriesAsync(IExecutionContext context, IGameApiClient gameApiClient, RankedSeriesRecord series, MatchStateRecord lastGame, double scoreA, double scoreB)
        {
            var players = new[]
            {
                new MatchPlayerRecord { PlayerId = series.PlayerIds[0] },
                new MatchPlayerRecord { PlayerId = series.PlayerIds[1] },
            };
            var scores = new[] { scoreA, scoreB };

            await SettleCoreAsync(context, gameApiClient, series.BoardSize, players, scores, series.MmrBeforeByPlayerId,
                dto => lastGame.RankedMmrResultByPlayerId[dto.PlayerId] = dto.Result,
                () => { });

            // Mirror the settlement's soft currency onto the game record the client is actually
            // polling (game 2, or game 1 if the series ended by abandonment before game 2 existed).
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            series.Complete = true;
            series.LastActivityAtUnixSeconds = now;
            await RankedSeriesStore.SaveAsync(context, gameApiClient, series);
        }

        private readonly struct SettledPlayerResult
        {
            public string PlayerId { get; init; }
            public RankedMmrResultDto Result { get; init; }
            public int Awarded { get; init; }
        }

        /// <summary>
        /// The actual Elo + reward + leaderboard settlement, shared by a 6x6 match, a 3x3 series, and
        /// an abandonment resolution of either. <paramref name="onPlayerSettled"/>/<paramref name="onDone"/>
        /// let the 3 callers each mirror the result onto whatever <see cref="MatchStateRecord"/> the
        /// client actually polls, without this method needing to know which shape it is settling.
        /// </summary>
        private static async Task SettleCoreAsync(
            IExecutionContext context, IGameApiClient gameApiClient, int boardSize,
            IReadOnlyList<MatchPlayerRecord> players, IReadOnlyList<double> scores,
            IDictionary<string, int> mmrBeforeByPlayerId,
            Action<SettledPlayerResult> onPlayerSettled, Action onDone)
        {
            var rankedConfig = await RankedConfigReader.GetAsync(context, gameApiClient);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string dayUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            bool isNoContest = scores[0] == RankedEloCalculator.ScoreDraw && scores[1] == RankedEloCalculator.ScoreDraw && mmrBeforeByPlayerId.Count == 0;
            // (isNoContest is never true here in practice - HandleBothAbandonedAsync has its own,
            // separate, MMR-untouched path. This flag only guards against a theoretical future
            // caller reusing SettleCoreAsync for a no-contest by mistake.)

            for (int i = 0; i < 2; i++)
            {
                int j = 1 - i;
                string playerId = players[i].PlayerId;
                string rivalId = players[j].PlayerId;

                int selfMmr = mmrBeforeByPlayerId.TryGetValue(playerId, out int before) ? before : rankedConfig.InitialMmr;
                int rivalMmr = mmrBeforeByPlayerId.TryGetValue(rivalId, out int rivalBefore) ? rivalBefore : rankedConfig.InitialMmr;

                var touch = await RankedProfileStore.TouchAsync(context, gameApiClient, playerId, boardSize, rankedConfig, now);
                await GrantPendingSeasonRewardIfAnyAsync(context, gameApiClient, playerId, boardSize, touch, rankedConfig);

                int kFactor = rankedConfig.KFactorFor(touch.Record.PlacementsPlayed, selfMmr);
                int firstMoveElo = boardSize == 6 ? rankedConfig.FirstMoveEloBoard6 : 0;
                bool selfMovedFirst = boardSize == 6 && players[i].Symbol == "X";
                bool rivalMovedFirst = boardSize == 6 && players[j].Symbol == "X";

                double damping = await ResolveDampingAsync(context, gameApiClient, playerId, rivalId, boardSize, dayUtc, scores[i], rankedConfig);

                int delta = RankedEloCalculator.ComputeDelta(selfMmr, rivalMmr, selfMovedFirst, rivalMovedFirst, firstMoveElo, scores[i], kFactor, damping);
                int newMmr = RankedEloCalculator.ApplyFloor(selfMmr, delta, rankedConfig.MmrFloor);

                touch.Record.Mmr = newMmr;
                touch.Record.PlacementsPlayed += 1;
                touch.Record.LastRankedMatchAtUnixSeconds = now;
                touch.Record.DecayAppliedThroughUnixSeconds = now;
                await RankedProfileStore.SaveBoardRecordAsync(context, gameApiClient, playerId, boardSize, touch.Record);

                // design-doc.md section 6.6 elegibilidad: "un jugador aparece en el leaderboard solo
                // tras completar sus partidas de colocacion... Evita que el #1 de la semana 1 sea
                // alguien con 1 partida jugada." This is the ONLY place a Ranked score is ever
                // published, so the rule is enforceable here and nowhere else - and until the live
                // end-to-end run of 2026-08-08 it was not enforced at all: a player with 1/5 was
                // already ranked, while two comments downstream (RankedQueryFunctions'
                // GetRankedLeaderboard and RankedLeaderboardResponse) asserted the gate existed.
                //
                // PlacementsPlayed was incremented just above, so this publishes for the first time
                // on the very series that completes placement, not the one after it.
                if (touch.Record.PlacementsPlayed >= rankedConfig.PlacementMatches)
                {
                    await RankedLeaderboardStore.SubmitScoreAsync(context, gameApiClient, boardSize, playerId, newMmr);
                }

                string resultCode = scores[i] == RankedEloCalculator.ScoreWin ? RankedRewardCalculator.ResultWin
                    : scores[i] == RankedEloCalculator.ScoreLoss ? RankedRewardCalculator.ResultLoss
                    : RankedRewardCalculator.ResultDraw;

                int rawReward = RankedRewardCalculator.RawRewardFor(rankedConfig, boardSize, resultCode);
                int awarded = rawReward > 0
                    ? await MatchFunctions.ApplyDailyCapsAndCreditAsync(context, gameApiClient, playerId, rawReward, dayUtc)
                    : 0;

                var resultDto = new RankedMmrResultDto
                {
                    BoardSize = boardSize,
                    MmrBefore = selfMmr,
                    MmrAfter = newMmr,
                    Delta = delta,
                    OpponentMmr = rivalMmr,
                    KFactor = kFactor,
                    Result = resultCode,
                    Season = touch.Record.SeasonId,
                };

                onPlayerSettled(new SettledPlayerResult { PlayerId = playerId, Result = resultDto, Awarded = awarded });
            }

            onDone();
        }

        /// <summary>
        /// design-doc.md section 6.3 "Amortiguación por rival repetido": a WIN increments the
        /// scored-win counter (this player, this rival, this board, today) and uses the resulting
        /// ordinal; a DRAW peeks the current count WITHOUT advancing it (draws are not "victorias"
        /// per the design's own wording); a LOSS never matters (see <see cref="RankedEloCalculator.ComputeDelta"/>,
        /// which only ever applies damping to a positive raw delta).
        /// </summary>
        private static async Task<double> ResolveDampingAsync(
            IExecutionContext context, IGameApiClient gameApiClient, string playerId, string rivalId, int boardSize, string dayUtc, double score, RankedConfigDto config)
        {
            if (score == RankedEloCalculator.ScoreWin)
            {
                int ordinal = await RankedRewardStore.IncrementScoredWinCountAsync(context, gameApiClient, playerId, rivalId, boardSize, dayUtc);
                return config.RepeatRivalDampingFor(ordinal);
            }

            if (score == RankedEloCalculator.ScoreDraw)
            {
                int current = await RankedRewardStore.PeekScoredWinCountAsync(context, gameApiClient, playerId, rivalId, boardSize, dayUtc);
                return config.RepeatRivalDampingFor(Math.Max(1, current));
            }

            return 1.0; // Loss - RankedEloCalculator ignores this value for a non-positive raw delta.
        }

        /// <summary>Grants the currency for a season-close tier reward the moment <see cref="RankedProfileStore.TouchAsync"/> surfaces one pending (design-doc.md section 6.5) - resolves the Top 100 bonus against the just-archived Leaderboard version (design-doc.md section 6.8 Q9/Q5) and credits both amounts as ONE <c>soft_currency_earned</c>-worthy grant, exempt from the online daily caps (design-doc.md section 6.3 "Recompensa de temporada - exenta de topes": a direct wallet credit, bypassing <see cref="MatchFunctions.ApplyDailyCapsAndCreditAsync"/> entirely).</summary>
        public static async Task GrantPendingSeasonRewardIfAnyAsync(
            IExecutionContext context, IGameApiClient gameApiClient, string playerId, int boardSize, RankedTouchResult touch, RankedConfigDto rankedConfig)
        {
            if (touch.PendingSeasonReward == null)
            {
                return;
            }

            var reward = touch.PendingSeasonReward;
            bool top100 = await IsInArchivedTop100Async(context, gameApiClient, boardSize, playerId);
            int total = reward.SoftCurrency + (top100 ? rankedConfig.Top100BonusSoftCurrency : 0);

            if (total <= 0)
            {
                return;
            }

            var currency = await OnlineRewardStore.GetAuthoritativeCurrencyAsync(context, gameApiClient, playerId);
            currency.Balance += total;
            currency.UpdatedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await OnlineRewardStore.SetAuthoritativeCurrencyAsync(context, gameApiClient, playerId, currency);
        }

        private static async Task<bool> IsInArchivedTop100Async(IExecutionContext context, IGameApiClient gameApiClient, int boardSize, string playerId)
        {
            try
            {
                string versionId = await RankedLeaderboardStore.GetMostRecentArchivedVersionIdAsync(context, gameApiClient, boardSize);
                if (string.IsNullOrEmpty(versionId))
                {
                    return false;
                }

                var top = await RankedLeaderboardStore.GetArchivedTopAsync(context, gameApiClient, boardSize, versionId, 100);
                return top.Any(e => e.PlayerId == playerId);
            }
            catch
            {
                // Best-effort: a failure here must never block the (already-computed) tier reward from being granted - it only loses the Top 100 bonus for this grant.
                return false;
            }
        }

        // ---- DTO enrichment (shared by CreateMatch/PlayMove/GetMatchState ranked branches) ----

        /// <summary>Layers Ranked-only fields onto an already-built <see cref="MatchStateDto"/> - kept as a post-processing step (rather than a <see cref="MatchStateStore.ToDto"/> parameter) so the widely-used, mode-agnostic <c>ToDto</c> never needs to know Ranked/series exist.</summary>
        public static async Task EnrichDtoWithSeriesInfoAsync(IExecutionContext context, IGameApiClient gameApiClient, MatchStateRecord record, MatchStateDto dto)
        {
            if (record.RankedMmrResultByPlayerId.TryGetValue(context.PlayerId, out var mmrResult))
            {
                dto.RankedMmrResult = mmrResult;
            }

            if (string.IsNullOrEmpty(record.RankedSeriesId))
            {
                dto.RankedSeriesComplete = record.RankedSettled;
                return;
            }

            dto.RankedSeriesId = record.RankedSeriesId;
            dto.RankedSeriesGameIndex = record.RankedSeriesGameIndex;

            var series = await RankedSeriesStore.LoadAsync(context, gameApiClient, record.RankedSeriesId);
            dto.RankedSeriesComplete = series.Complete;
            if (record.RankedSeriesGameIndex == 1 && series.MatchIds.Count > 1)
            {
                dto.RankedNextMatchId = series.MatchIds[1];
            }
        }
    }

    /// <summary>Extra outcome of a Ranked-ending move (see <see cref="RankedMatchSupport.HandleRankedMatchEndedAsync"/>) that <see cref="MatchFunctions.PlayMove"/> layers onto its own returned DTO.</summary>
    public class RankedPlayMoveOutcome
    {
        public bool SeriesComplete { get; set; }
        public string NextMatchId { get; set; }
    }
}
