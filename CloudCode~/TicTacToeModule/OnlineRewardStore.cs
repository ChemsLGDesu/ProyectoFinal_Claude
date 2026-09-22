using System.Text.Json;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Model;

namespace TTTXO.CloudCode
{
    /// <summary>
    /// Cloud Save Player Data I/O for the online soft-currency reward path (Milestone 4).
    ///
    /// ACCESS CLASS - Protected for <c>authoritativeCurrency</c> (the player reads it to display a
    /// balance and can never write it); Private for <c>onlineCurrencyLedger</c> and the per-rival
    /// win counter (the player has no business reading its own anti-farming counters either, and
    /// hiding them removes the reconnaissance step as well as the tamper).
    ///
    /// This declaration exists because its absence is what caused ERR-KB-006. Every key here used
    /// to be written with <c>SetItemAsync</c>, which is access class <c>Default</c>: readable AND
    /// writable by the player against the Cloud Save REST API with their own token, without the
    /// SDK, and without this module ever hearing about it. Nobody chose that - it was inherited by
    /// copying another store's I/O pattern. A store added later must state its class here and say
    /// why, and the question to answer is never "does the client need to read this?" but "what
    /// happens if the client writes it?".
    ///
    /// <c>currency</c> is deliberately NOT written here any more. That key stays access class
    /// Default and stays the client's own local-first offline wallet (1P/local play earns into
    /// PlayerPrefs and mirrors to Cloud Save via <c>PlayerDataService</c>). A match the server never
    /// saw cannot be verified by any design, so locking that mirror would buy nothing. What
    /// ERR-KB-006 was actually about is that server-computed online rewards - capped,
    /// anti-collusion-checked, Elo-relevant - landed in that same writable key, which made every one
    /// of those checks decorative: a single POST skipped them all. They now land in
    /// <c>authoritativeCurrency</c>, which is the balance the store will spend from.
    ///
    /// Both calls are made with <c>context.ServiceToken</c> (elevated), which is what lets a single
    /// PlayMove call credit BOTH matched players' own Player Data, not just the caller's (e.g. a
    /// draw pays both sides from whichever player's move happened to end it).
    ///
    /// No Cloud Save transactions exist for Player Data (same accepted limitation already
    /// documented for the Custom Data handoff pointer in this module's README "Limitaciones
    /// conocidas") - a genuine concurrent double-award for the exact same player (e.g. two matches
    /// for the same player ending at the same instant) is a theoretical read-modify-write race, not
    /// closed here.
    /// </summary>
    public static class OnlineRewardStore
    {
        /// <summary>Access class Protected. NOT <c>currency</c> - see the class remarks for why the two are separate.</summary>
        private const string AuthoritativeCurrencyItemKey = "authoritativeCurrency";

        /// <summary>Access class Private.</summary>
        private const string LedgerItemKey = "onlineCurrencyLedger";

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        /// <summary>Access class Private.</summary>
        private static string RivalWinItemKey(string rivalId, string dayUtc) => $"rivalWin_{rivalId}_{dayUtc}";

        public static async Task<CurrencySaveData> GetAuthoritativeCurrencyAsync(IExecutionContext context, IGameApiClient gameApiClient, string playerId)
        {
            var result = await gameApiClient.CloudSaveData.GetProtectedItemsAsync(context, context.ServiceToken, context.ProjectId, playerId, new List<string> { AuthoritativeCurrencyItemKey });
            var item = result.Data.Results.FirstOrDefault(i => i.Key == AuthoritativeCurrencyItemKey);
            return Deserialize<CurrencySaveData>(item?.Value) ?? new CurrencySaveData();
        }

        public static Task SetAuthoritativeCurrencyAsync(IExecutionContext context, IGameApiClient gameApiClient, string playerId, CurrencySaveData data)
        {
            return gameApiClient.CloudSaveData.SetProtectedItemAsync(context, context.ServiceToken, context.ProjectId, playerId, new SetItemBody(AuthoritativeCurrencyItemKey, data));
        }

        public static async Task<OnlineCurrencyLedgerRecord> GetLedgerAsync(IExecutionContext context, IGameApiClient gameApiClient, string playerId)
        {
            var result = await gameApiClient.CloudSaveData.GetPrivateItemsAsync(context, context.ServiceToken, context.ProjectId, playerId, new List<string> { LedgerItemKey });
            var item = result.Data.Results.FirstOrDefault(i => i.Key == LedgerItemKey);
            return Deserialize<OnlineCurrencyLedgerRecord>(item?.Value) ?? new OnlineCurrencyLedgerRecord();
        }

        public static Task SetLedgerAsync(IExecutionContext context, IGameApiClient gameApiClient, string playerId, OnlineCurrencyLedgerRecord ledger)
        {
            return gameApiClient.CloudSaveData.SetPrivateItemAsync(context, context.ServiceToken, context.ProjectId, playerId, new SetItemBody(LedgerItemKey, ledger));
        }

        /// <summary>
        /// Increments and returns this player's paid-win count against <paramref name="rivalId"/>
        /// for <paramref name="dayUtc"/> ("yyyy-MM-dd") - the return value is this win's ordinal
        /// (1st, 2nd, 3rd, ...) for the caller to compare against
        /// <see cref="OnlineRewardCalculator.MaxPaidWinsPerRivalPerDay"/>. The day is baked into the
        /// item key itself, so a new UTC day starts counting from 0 with no explicit reset/expiry
        /// needed - old (rival, day) keys are simply never read again (same "no cleanup cron"
        /// posture as the rest of this module, see MatchStateStore's handoff pointer).
        /// </summary>
        public static async Task<int> IncrementRivalWinCountAsync(IExecutionContext context, IGameApiClient gameApiClient, string playerId, string rivalId, string dayUtc)
        {
            string key = RivalWinItemKey(rivalId, dayUtc);

            var result = await gameApiClient.CloudSaveData.GetPrivateItemsAsync(context, context.ServiceToken, context.ProjectId, playerId, new List<string> { key });
            var item = result.Data.Results.FirstOrDefault(i => i.Key == key);
            int next = (Deserialize<int?>(item?.Value) ?? 0) + 1;

            await gameApiClient.CloudSaveData.SetPrivateItemAsync(context, context.ServiceToken, context.ProjectId, playerId, new SetItemBody(key, next));
            return next;
        }

        private static T Deserialize<T>(object value)
        {
            if (value == null)
            {
                return default;
            }

            string json = value is JsonElement element ? element.GetRawText() : value.ToString() ?? string.Empty;
            return string.IsNullOrEmpty(json) ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
    }
}
