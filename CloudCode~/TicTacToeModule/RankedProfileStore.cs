using System.Text.Json;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Model;

namespace TTTXO.CloudCode
{
    /// <summary>Result of <see cref="RankedProfileStore.TouchAsync"/> - the up-to-date board record plus whatever lazy side effects (season rollover reward, inactivity decay) were just collapsed and persisted, so the caller can react (settle MMR against a fresh baseline, credit a season reward, or surface a decay notice to the client).</summary>
    public class RankedTouchResult
    {
        public RankedBoardProfileRecord Record { get; set; } = new();

        /// <summary>Non-null only the FIRST time this player's record is touched after a season boundary passed - the tier reward owed for the season that just closed (design-doc.md section 6.5, "Requisito de elegibilidad: haber completado las 10 partidas de colocacion"). Null if the player was not placement-eligible that season, or no rollover happened this touch.</summary>
        public RankedSeasonRewardDto PendingSeasonReward { get; set; }

        /// <summary>MMR points removed by inactivity decay this touch (0 if none) - design-doc.md section 6.6, `reason = "inactivity_decay"` on <c>ranked_mmr_changed</c>.</summary>
        public int DecayApplied { get; set; }
    }

    /// <summary>Season-close tier reward owed to a player for one board size (design-doc.md section 6.5's tier table) - computed by <see cref="RankedTierCalculator"/>, granted by whichever Ranked function next touches this player's <c>rankedProfile</c> after the season boundary (see <see cref="RankedProfileStore.TouchAsync"/>).</summary>
    public class RankedSeasonRewardDto
    {
        public int Season { get; set; }
        public int BoardSize { get; set; }
        public string Tier { get; set; } = string.Empty;
        public int SoftCurrency { get; set; }
        public bool Top100 { get; set; }
    }

    /// <summary>
    /// Cloud Save Player Data I/O for the <c>rankedProfile</c> key (Milestone 5, design-doc.md
    /// section 6.1 - see <see cref="RankedProfileSaveData"/> remarks for why this is its OWN key,
    /// not literally inside the client-writable <c>profile</c> key). Mirrors
    /// <see cref="OnlineRewardStore"/>'s GetItemAsync/SetItemAsync pattern against Player Data
    /// (keyed by playerId), always with <c>context.ServiceToken</c> so a single Ranked settlement
    /// can update BOTH matched players' own records, not just the caller's.
    /// </summary>
    public static class RankedProfileStore
    {
        /// <summary>
        /// ACCESS CLASS Protected - the player reads their own MMR, tier and placement progress to
        /// draw the profile and the result card, and can never write them.
        ///
        /// This was access class Default until ERR-KB-006 was closed, which made it the worst of the
        /// three affected keys: the MMR stored here is what
        /// <c>CompleteOnlineMatchAsync</c> snapshots as the input of the Elo calculation, and what
        /// ends up published on the Ranked leaderboard. A forged write here was not a local cheat -
        /// it propagated into a shared, already-deployed ranking.
        /// </summary>
        private const string ProfileItemKey = "rankedProfile";

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public static async Task<RankedProfileSaveData> LoadAsync(IExecutionContext context, IGameApiClient gameApiClient, string playerId)
        {
            var result = await gameApiClient.CloudSaveData.GetProtectedItemsAsync(context, context.ServiceToken, context.ProjectId, playerId, new List<string> { ProfileItemKey });
            var item = result.Data.Results.FirstOrDefault(i => i.Key == ProfileItemKey);
            if (item == null)
            {
                return new RankedProfileSaveData();
            }

            string json = item.Value is JsonElement element ? element.GetRawText() : item.Value?.ToString() ?? string.Empty;
            return JsonSerializer.Deserialize<RankedProfileSaveData>(json, JsonOptions) ?? new RankedProfileSaveData();
        }

        public static Task SaveAsync(IExecutionContext context, IGameApiClient gameApiClient, string playerId, RankedProfileSaveData data)
        {
            return gameApiClient.CloudSaveData.SetProtectedItemAsync(context, context.ServiceToken, context.ProjectId, playerId, new SetItemBody(ProfileItemKey, data));
        }

        /// <summary>
        /// THE entry point every Ranked code path should use instead of a raw <see cref="LoadAsync"/>
        /// - collapses (and persists) any pending season rollover (design-doc.md section 6.8 Q9) and
        /// inactivity decay (Q8) BEFORE returning the record, so nobody ever reads/settles against a
        /// stale record. Safe to call as often as needed: a no-op (no extra write) when nothing is
        /// pending, per design-doc.md section 6.8 Q8's "no en cada lectura de profile" - callers that
        /// only need a read-only peek (e.g. a Matchmaker ticket's own MMR attribute) still benefit
        /// from this rather than a raw load, since a rollover/decay that has never been collapsed
        /// would otherwise silently feed a stale MMR into matchmaking.
        /// </summary>
        public static async Task<RankedTouchResult> TouchAsync(
            IExecutionContext context, IGameApiClient gameApiClient, string playerId, int boardSize, RankedConfigDto config, long now)
        {
            var data = await LoadAsync(context, gameApiClient, playerId);
            string key = boardSize.ToString(System.Globalization.CultureInfo.InvariantCulture);

            bool isNew = !data.ByBoardSize.TryGetValue(key, out var record);
            if (isNew)
            {
                record = new RankedBoardProfileRecord
                {
                    Mmr = config.InitialMmr,
                    SeasonId = RankedSeasonCalculator.CurrentSeasonId(config, now),
                    DecayAppliedThroughUnixSeconds = now,
                };
            }

            bool changed = isNew;
            RankedSeasonRewardDto pendingReward = null;

            int currentSeasonId = RankedSeasonCalculator.CurrentSeasonId(config, now);
            if (!isNew && record.SeasonId != currentSeasonId && record.SeasonId < currentSeasonId)
            {
                // design-doc.md section 6.5: reward is based on the MMR the player is AT CLOSE (this
                // value, before the reset below touches it), and only if placement was completed.
                if (record.PlacementsPlayed >= config.PlacementMatches)
                {
                    pendingReward = new RankedSeasonRewardDto
                    {
                        Season = record.SeasonId,
                        BoardSize = boardSize,
                        Tier = RankedTierCalculator.TierFor(config, record.Mmr),
                        SoftCurrency = RankedTierCalculator.SoftCurrencyFor(config, record.Mmr),
                        // Top100 is resolved by the caller (needs an async Leaderboards read against
                        // the just-archived version - see MatchFunctions.GrantPendingSeasonRewardAsync)
                        // and folded back into this same reward before it is credited.
                        Top100 = false,
                    };
                }

                // design-doc.md section 6.5 soft reset: MMR_nuevo = redondear(1000 + (MMR_viejo-1000)*0.5), max(500, ...).
                int reset = (int)Math.Round(config.InitialMmr + ((record.Mmr - config.InitialMmr) * 0.5), MidpointRounding.AwayFromZero);
                record.Mmr = Math.Max(config.MmrFloor, reset);
                record.PlacementsPlayed = 0;
                record.SeasonId = currentSeasonId;
                record.DecayAppliedThroughUnixSeconds = now;
                changed = true;
            }

            int decayApplied = ApplyDecay(record, config, now);
            changed = changed || decayApplied != 0;

            if (changed)
            {
                data.ByBoardSize[key] = record;
                await SaveAsync(context, gameApiClient, playerId, data);
            }

            return new RankedTouchResult { Record = record, PendingSeasonReward = pendingReward, DecayApplied = decayApplied };
        }

        /// <summary>Persists <paramref name="record"/> for <paramref name="boardSize"/> as-is (no rollover/decay recompute) - used by settlement once it has already computed the new MMR/placement count via <see cref="RankedEloCalculator"/> and wants to save the final result.</summary>
        public static async Task SaveBoardRecordAsync(
            IExecutionContext context, IGameApiClient gameApiClient, string playerId, int boardSize, RankedBoardProfileRecord record)
        {
            var data = await LoadAsync(context, gameApiClient, playerId);
            data.ByBoardSize[boardSize.ToString(System.Globalization.CultureInfo.InvariantCulture)] = record;
            await SaveAsync(context, gameApiClient, playerId, data);
        }

        /// <summary>
        /// design-doc.md section 6.6 lazy decay: "Gracia: 7 dias consecutivos... Tasa: -25 MMR por
        /// cada dia adicional... Piso del decay: 1200... Aplicacion perezosa... acumulando los dias
        /// pendientes desde decayAppliedThroughUnixSeconds". Both the "days owed" and "days already
        /// applied" counts are derived from <see cref="RankedBoardProfileRecord.LastRankedMatchAtUnixSeconds"/>
        /// (the decay clock's anchor, reset every time a Ranked unit completes) rather than tracked
        /// as a separate running total, so this stays idempotent/re-derivable from the record alone -
        /// no risk of double-deducting across repeated calls. Mutates <paramref name="record"/> in
        /// place; returns the MMR amount actually removed (0 if none owed or already fully applied).
        /// </summary>
        private static int ApplyDecay(RankedBoardProfileRecord record, RankedConfigDto config, long now)
        {
            if (record.Mmr <= config.DecayProtectedFloor || record.LastRankedMatchAtUnixSeconds <= 0)
            {
                record.DecayAppliedThroughUnixSeconds = Math.Max(record.DecayAppliedThroughUnixSeconds, now);
                return 0;
            }

            long graceSeconds = Math.Max(0, config.DecayGraceDays) * 86400L;

            long daysSinceLastMatch = Math.Max(0, now - record.LastRankedMatchAtUnixSeconds) / 86400L;
            int owedDecayDays = (int)Math.Max(0, daysSinceLastMatch - config.DecayGraceDays);

            long alreadyAppliedElapsed = Math.Max(0, record.DecayAppliedThroughUnixSeconds - record.LastRankedMatchAtUnixSeconds);
            long alreadyAppliedDays = Math.Max(0, (alreadyAppliedElapsed / 86400L) - config.DecayGraceDays);
            int alreadyAppliedDecayDays = (int)Math.Max(0, alreadyAppliedDays);

            int incrementalDays = Math.Max(0, owedDecayDays - alreadyAppliedDecayDays);
            if (incrementalDays == 0)
            {
                return 0;
            }

            int rawDecay = incrementalDays * Math.Max(0, config.DecayPerDay);
            int floor = Math.Max(config.MmrFloor, config.DecayProtectedFloor);
            int applied = Math.Min(rawDecay, Math.Max(0, record.Mmr - floor));

            record.Mmr -= applied;
            record.DecayAppliedThroughUnixSeconds = now;
            return applied;
        }
    }
}
