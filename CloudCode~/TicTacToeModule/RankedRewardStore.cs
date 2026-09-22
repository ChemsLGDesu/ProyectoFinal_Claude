using System.Text.Json;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Model;

namespace TTTXO.CloudCode
{
    /// <summary>
    /// Cloud Save Player Data I/O for Ranked-specific anti-boosting state (design-doc.md section
    /// 6.3 "Anti-boosting de MMR (nuevo, especifico de Ranked)"). This is a SEPARATE counter from
    /// <see cref="OnlineRewardStore"/>'s money-capping <c>rivalWin_{rivalId}_{day}</c> ledger (which
    /// Ranked still shares unmodified for the currency caps, per design-doc.md "los mismos de
    /// Quickmatch, sin fork") - this one counts scored (Elo-relevant) wins, keyed additionally by
    /// board size (`rankedWin_{rivalId}_{boardSize}_{day}`, design-doc.md section 6.8 Q7 "puede
    /// ganar un discriminador... sin migracion de datos existentes" - a brand new key, never
    /// touches/reads the old <c>rivalWin_*</c> keys).
    /// </summary>
    public static class RankedRewardStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// ACCESS CLASS Private - the player neither reads nor writes this. It is the anti-boosting
        /// counter that drives <see cref="RankedConfigDto.RepeatRivalDampingFor"/>, so a player who
        /// could reset it would farm undamped Elo off the same rival indefinitely; a player who
        /// could merely read it would know exactly when to stop. Private removes both.
        ///
        /// Was access class Default until ERR-KB-006 was closed. Note that this key is NOT in that
        /// record's table of three - it was found while fixing them, by grepping the module for the
        /// same GetItemsAsync/SetItemAsync pattern rather than trusting the list.
        /// </summary>
        private static string RankedRivalWinItemKey(string rivalId, int boardSize, string dayUtc) => $"rankedWin_{rivalId}_{boardSize}_{dayUtc}";

        /// <summary>Increments and returns this player's scored-win ordinal against <paramref name="rivalId"/> on this board size today (1st, 2nd, 3rd...) - drives <see cref="RankedConfigDto.RepeatRivalDampingFor"/> for a WIN's positive delta.</summary>
        public static async Task<int> IncrementScoredWinCountAsync(IExecutionContext context, IGameApiClient gameApiClient, string playerId, string rivalId, int boardSize, string dayUtc)
        {
            string key = RankedRivalWinItemKey(rivalId, boardSize, dayUtc);
            int current = await GetItemAsync<int?>(context, gameApiClient, playerId, key) ?? 0;
            int next = current + 1;
            await SetItemAsync(context, gameApiClient, playerId, key, next);
            return next;
        }

        /// <summary>Read-only peek at the current ordinal (no increment) - used for a DRAW's positive delta, which per design-doc.md section 6.1 step 5 is damped by whatever tier the player is already in against this rival today, without advancing it (the counter only counts WINS, see design-doc.md section 6.3's literal wording).</summary>
        public static async Task<int> PeekScoredWinCountAsync(IExecutionContext context, IGameApiClient gameApiClient, string playerId, string rivalId, int boardSize, string dayUtc)
        {
            string key = RankedRivalWinItemKey(rivalId, boardSize, dayUtc);
            return await GetItemAsync<int?>(context, gameApiClient, playerId, key) ?? 0;
        }

        private static async Task<T> GetItemAsync<T>(IExecutionContext context, IGameApiClient gameApiClient, string playerId, string key)
        {
            var result = await gameApiClient.CloudSaveData.GetPrivateItemsAsync(context, context.ServiceToken, context.ProjectId, playerId, new List<string> { key });
            var item = result.Data.Results.FirstOrDefault(i => i.Key == key);
            if (item == null)
            {
                return default;
            }

            string json = item.Value is JsonElement element ? element.GetRawText() : item.Value?.ToString() ?? string.Empty;
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }

        private static Task SetItemAsync(IExecutionContext context, IGameApiClient gameApiClient, string playerId, string key, object value)
        {
            return gameApiClient.CloudSaveData.SetPrivateItemAsync(context, context.ServiceToken, context.ProjectId, playerId, new SetItemBody(key, value));
        }
    }
}
