using System.Text.Json;
using TTTXO.Core;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Model;

namespace TTTXO.CloudCode
{
    /// <summary>
    /// Loads/saves <see cref="MatchStateRecord"/> to Cloud Save Custom Data (key = matchId, per
    /// Docs/03-Arquitectura-UGS-TicTacToe.md#Cloud Save) and replays the recorded moves through a
    /// fresh <see cref="MatchController"/> to reconstruct the exact server-authoritative state on
    /// every call - Cloud Code workers are stateless between invocations, so nothing here relies on
    /// in-memory state surviving between calls (Docs' own module structure guidance).
    /// </summary>
    public static class MatchStateStore
    {
        private const string StateItemKey = "state";
        private const string HandoffPointerItemKey = "matchId";

        /// <summary>Cloud Save Custom Data document id for a handoff-&gt;match idempotency pointer (risk #1). Kept in its own document (never the match's own) so looking one up never needs to know our own matchId first.</summary>
        private static string HandoffPointerDocId(string handoffId) => $"handoff-{handoffId}";

        public static async Task<MatchStateRecord> LoadAsync(IExecutionContext context, IGameApiClient gameApiClient, string matchId)
        {
            var result = await gameApiClient.CloudSaveData.GetCustomItemsAsync(context, context.ServiceToken, context.ProjectId, matchId);
            var item = result.Data.Results.FirstOrDefault(i => i.Key == StateItemKey);

            if (item == null)
            {
                throw new Exception($"Match '{matchId}' was not found.");
            }

            string json = item.Value is JsonElement element ? element.GetRawText() : item.Value?.ToString() ?? string.Empty;
            var record = JsonSerializer.Deserialize<MatchStateRecord>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (record == null)
            {
                throw new Exception($"Match '{matchId}' state could not be parsed.");
            }

            return record;
        }

        public static Task SaveAsync(IExecutionContext context, IGameApiClient gameApiClient, MatchStateRecord record)
        {
            return gameApiClient.CloudSaveData.SetCustomItemAsync(
                context, context.ServiceToken, context.ProjectId, record.MatchId, new SetItemBody(StateItemKey, record));
        }

        /// <summary>
        /// Risk #1 idempotency lookup: returns the matchId already created for this handoffId
        /// (Matchmaker's shared <c>MatchIdAssignment.MatchId</c>), or null if none exists yet (first
        /// caller for this handoff). See <see cref="MatchFunctions.CreateMatch"/> - either matched
        /// player may call CreateMatch first.
        /// </summary>
        public static async Task<string> TryGetMatchIdForHandoffAsync(IExecutionContext context, IGameApiClient gameApiClient, string handoffId)
        {
            var result = await gameApiClient.CloudSaveData.GetCustomItemsAsync(
                context, context.ServiceToken, context.ProjectId, HandoffPointerDocId(handoffId));
            var item = result.Data.Results.FirstOrDefault(i => i.Key == HandoffPointerItemKey);
            if (item == null)
            {
                return null;
            }

            return item.Value is JsonElement element ? element.GetString() : item.Value?.ToString();
        }

        /// <summary>Publishes the handoff-&gt;match pointer the first time CreateMatch creates a new (pending, 1-player) match for this handoff (see <see cref="TryGetMatchIdForHandoffAsync"/>).</summary>
        public static Task SaveHandoffPointerAsync(IExecutionContext context, IGameApiClient gameApiClient, string handoffId, string matchId)
        {
            return gameApiClient.CloudSaveData.SetCustomItemAsync(
                context, context.ServiceToken, context.ProjectId, HandoffPointerDocId(handoffId), new SetItemBody(HandoffPointerItemKey, matchId));
        }

        /// <summary>
        /// Rebuilds a live <see cref="MatchController"/> from a stored record by replaying every move
        /// through the same public <see cref="MatchController.PlayMove"/> the client uses - this is
        /// what guarantees the server never diverges from Milestone 1's already-tested rules
        /// (Docs/01-Directrices-Proyecto.md#Seguridad / anti-cheat).
        /// </summary>
        public static MatchController Replay(MatchStateRecord record)
        {
            var config = BoardConfig.ForSize(record.BoardSize);
            var firstPlayer = record.FirstPlayer == "O" ? CellOwner.O : CellOwner.X;
            var match = new MatchController(config, firstPlayer);

            foreach (var move in record.Moves)
            {
                var result = match.PlayMove(move.Row, move.Col);
                if (!result.IsValid)
                {
                    // A stored record should only ever contain moves that were valid when recorded -
                    // if replay disagrees, the persisted data is corrupt, which is a hard failure.
                    throw new Exception($"Corrupt match state for '{record.MatchId}': stored move ({move.Row},{move.Col}) is invalid on replay.");
                }
            }

            return match;
        }

        /// <summary>
        /// <paramref name="callerPlayerId"/> is only used to look up <see cref="MatchStateDto.AwardedSoftCurrency"/>
        /// in <see cref="MatchStateRecord.AwardedSoftCurrencyByPlayerId"/> (Milestone 4 online
        /// reward, design-doc.md section 4) - omit it (or pass null) for 1P/local matches, where the
        /// dictionary is always empty and the field stays 0.
        /// </summary>
        public static MatchStateDto ToDto(MatchStateRecord record, MatchController match, string callerPlayerId = null)
        {
            var board = new string[record.BoardSize * record.BoardSize];
            for (int row = 0; row < record.BoardSize; row++)
            {
                for (int col = 0; col < record.BoardSize; col++)
                {
                    var owner = match.Board.GetCell(row, col);
                    board[(row * record.BoardSize) + col] = owner switch
                    {
                        CellOwner.X => "X",
                        CellOwner.O => "O",
                        _ => string.Empty,
                    };
                }
            }

            int[][] winningLine = Array.Empty<int[]>();
            if (match.WinningLine != null)
            {
                winningLine = match.WinningLine.Select(cell => new[] { cell.Row, cell.Col }).ToArray();
            }

            string status = match.Status switch
            {
                MatchStatus.XWon => "x_won",
                MatchStatus.OWon => "o_won",
                MatchStatus.Draw => "draw",
                _ => "in_progress",
            };

            // Risk #4 (Docs/03-Arquitectura-UGS-TicTacToe.md#4. Partidas online abandonadas): an
            // abandonment resolution is layered on top of the pure move-replay result here rather
            // than injected as a fake move into Moves, so MatchStateStore.Replay stays a pure
            // function of the recorded moves. Only takes effect if the replay itself hasn't already
            // finished the match through a real move. Two mutually-exclusive abandonment shapes:
            // one player still active (reactive path, AbandonedWinnerPlayerId - that player wins),
            // or neither active (proactive sweep, BothPlayersAbandoned - see MatchFunctions.
            // SweepAbandonedMatches design decision in the README: no natural winner, so a draw).
            bool endedByAbandonment = false;
            if (match.Status == MatchStatus.InProgress && !string.IsNullOrEmpty(record.AbandonedWinnerPlayerId))
            {
                var winner = record.Players.FirstOrDefault(p => p.PlayerId == record.AbandonedWinnerPlayerId);
                if (winner != null)
                {
                    status = winner.Symbol == "X" ? "x_won" : "o_won";
                    endedByAbandonment = true;
                    winningLine = Array.Empty<int[]>();
                }
            }
            else if (match.Status == MatchStatus.InProgress && record.BothPlayersAbandoned)
            {
                status = "draw";
                endedByAbandonment = true;
                winningLine = Array.Empty<int[]>();
            }

            int awardedSoftCurrency = 0;
            if (!string.IsNullOrEmpty(callerPlayerId))
            {
                record.AwardedSoftCurrencyByPlayerId.TryGetValue(callerPlayerId, out awardedSoftCurrency);
            }

            return new MatchStateDto
            {
                MatchId = record.MatchId,
                BoardSize = record.BoardSize,
                WinLength = record.WinLength,
                Mode = record.Mode,
                Board = board,
                CurrentPlayer = match.CurrentPlayer == CellOwner.X ? "X" : "O",
                Status = status,
                TurnCount = match.TurnCount,
                WinningLine = winningLine,
                Players = record.Players.Select(p => new MatchPlayerDto { PlayerId = p.PlayerId, Symbol = p.Symbol }).ToArray(),
                EndedByAbandonment = endedByAbandonment,
                IsWaitingForOpponent = record.Players.Count == 1,
                AwardedSoftCurrency = awardedSoftCurrency,
            };
        }
    }
}
