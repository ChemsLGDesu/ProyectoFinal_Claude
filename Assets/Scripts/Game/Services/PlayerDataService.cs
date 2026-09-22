using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using TTTXO.Game.Bootstrap;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models.Data.Player;
using UnityEngine;

namespace TTTXO.Game.Services
{
    /// <summary>Cloud Save Player Data payload for the <c>currency</c> key (design-doc.md section 4).</summary>
    [Serializable]
    public class CurrencySaveData
    {
        public int Balance;
        public long UpdatedAtUnixSeconds;
    }

    /// <summary>Cloud Save Player Data payload for the <c>history</c> key (design-doc.md section 3).</summary>
    [Serializable]
    public class HistorySaveData
    {
        public List<MatchHistoryEntry> Entries = new();
        public long UpdatedAtUnixSeconds;
    }

    /// <summary>
    /// Cloud Save Player Data payload for the <c>profile</c> key. Milestone 2 only mirrors the
    /// adaptive AI level per board size here (design-doc.md section 3: "se guarda por tamano").
    /// Keyed by board size as a string ("3", "6", "9", "11") rather than int for JSON-object-key
    /// safety across serializers. MMR/Elo (Docs/03-Arquitectura-UGS-TicTacToe.md#Cloud Save:
    /// "profile (incluye MMR/Elo, usado como atributo del ticket de Matchmaker)") joins this same
    /// key once the Ranked/Matchmaker milestone lands - not part of Milestone 2.
    /// </summary>
    [Serializable]
    public class ProfileSaveData
    {
        public Dictionary<string, int> AdaptiveLevelsByBoardSize = new();

        /// <summary>
        /// Equipped profile cosmetics (see <see cref="CosmeticSelection"/>), so the choice follows a linked
        /// account to another device. A preference, not an entitlement: this records what is displayed, and
        /// carries no authority over what the player is allowed to display. Once cosmetics are sold, the
        /// owned set is decided by Cloud Code and this stays a pointer into it.
        /// </summary>
        public string AvatarId;

        public string FrameId;

        public string BannerId;

        public string PieceSkinId;

        public string BoardSkinId;

        public long UpdatedAtUnixSeconds;
    }

    /// <summary>
    /// Best-effort mirror of the local wallet, match history and adaptive AI levels to Cloud Save
    /// Player Data (keys <c>currency</c>, <c>history</c>, <c>profile</c>, per
    /// Docs/03-Arquitectura-UGS-TicTacToe.md#Cloud Save). Local PlayerPrefs (see
    /// <see cref="GameManager"/>) stays the source of truth whenever there is no UGS session or a
    /// cloud call fails - this class never blocks or breaks gameplay, and it never overwrites local
    /// state with an older cloud snapshot (a simple "updatedAt" timestamp decides which one wins).
    /// </summary>
    public static class PlayerDataService
    {
        private const string CurrencyKey = "currency";
        private const string HistoryKey = "history";
        private const string ProfileKey = "profile";

        /// <summary>
        /// Access class Protected: readable here, writable only by Cloud Code. This is where online
        /// Quickmatch and Ranked rewards land, and it is deliberately NOT <see cref="CurrencyKey"/>
        /// (ERR-KB-006 - crediting them into the key this class mirrors made the server's caps
        /// bypassable with a single REST POST). Nothing in this class ever writes it; there is no
        /// client-side API that could.
        /// </summary>
        private const string AuthoritativeCurrencyKey = "authoritativeCurrency";

        /// <summary>
        /// Called once at session start (after <see cref="UgsInitializer"/> reaches Ready): loads the
        /// cloud snapshot and applies it to <paramref name="gameManager"/> only where it is newer
        /// than the local state already loaded from PlayerPrefs.
        /// </summary>
        public static async Task LoadIntoSessionAsync(GameManager gameManager)
        {
            if (gameManager == null || UgsInitStatus.Ready != UgsInitializer.Status)
            {
                return;
            }

            try
            {
                var keys = new HashSet<string> { CurrencyKey, HistoryKey, ProfileKey };
                var results = await CloudSaveService.Instance.Data.Player.LoadAsync(keys);
                long localUpdatedAt = gameManager.LocalStateUpdatedAtUnixSeconds;

                if (results.TryGetValue(CurrencyKey, out var currencyItem))
                {
                    var cloud = currencyItem.Value.GetAs<CurrencySaveData>();
                    if (cloud != null && cloud.UpdatedAtUnixSeconds > localUpdatedAt)
                    {
                        gameManager.ApplyCloudCurrencySnapshot(cloud.Balance);
                    }
                }

                // Separate round trip on purpose: one LoadAsync reads one access class, and the
                // three keys above are Default while this one is Protected. Its own try/catch so a
                // failure here cannot cost the player the snapshot the Default keys already
                // returned - same best-effort posture as the rest of this class.
                try
                {
                    var protectedResults = await CloudSaveService.Instance.Data.Player.LoadAsync(
                        new HashSet<string> { AuthoritativeCurrencyKey },
                        new LoadOptions(new ProtectedReadAccessClassOptions()));

                    if (protectedResults.TryGetValue(AuthoritativeCurrencyKey, out var authoritativeItem))
                    {
                        var cloud = authoritativeItem.Value.GetAs<CurrencySaveData>();
                        if (cloud != null)
                        {
                            // No freshness comparison, unlike every other key here: the local value is
                            // a display cache of the server's number, not a competing source of truth.
                            gameManager.ApplyCloudAuthoritativeCurrencySnapshot(cloud.Balance);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"PlayerDataService: could not load {AuthoritativeCurrencyKey} - {ex.Message}");
                }

                if (results.TryGetValue(HistoryKey, out var historyItem))
                {
                    var cloud = historyItem.Value.GetAs<HistorySaveData>();
                    if (cloud?.Entries != null && cloud.UpdatedAtUnixSeconds > localUpdatedAt)
                    {
                        gameManager.ApplyCloudMatchHistorySnapshot(cloud.Entries);
                    }
                }

                if (results.TryGetValue(ProfileKey, out var profileItem))
                {
                    var cloud = profileItem.Value.GetAs<ProfileSaveData>();
                    if (cloud?.AdaptiveLevelsByBoardSize != null && cloud.UpdatedAtUnixSeconds > localUpdatedAt)
                    {
                        var levels = new Dictionary<int, int>();
                        foreach (var pair in cloud.AdaptiveLevelsByBoardSize)
                        {
                            if (int.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int boardSize))
                            {
                                levels[boardSize] = pair.Value;
                            }
                        }

                        gameManager.ApplyCloudAdaptiveLevelsSnapshot(levels);
                    }

                    // Cosmetics ride the same freshness check as everything else: a cloud snapshot older
                    // than local state must not undo a change made offline on this device.
                    if (cloud != null && cloud.UpdatedAtUnixSeconds > localUpdatedAt)
                    {
                        CosmeticSelection.ApplyCloudSnapshot(cloud.AvatarId, cloud.FrameId, cloud.BannerId, cloud.PieceSkinId, cloud.BoardSkinId);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"PlayerDataService: load failed, keeping local state - {ex.Message}");
            }
        }

        /// <summary>
        /// Pushes the current local state to Cloud Save. Called fire-and-forget after each finished
        /// match (see GameScreenController.HandleMatchEnd) - local PlayerPrefs is already updated and
        /// remains authoritative regardless of whether this call succeeds.
        /// </summary>
        public static async Task SaveAfterMatchAsync(GameManager gameManager)
        {
            if (gameManager == null || UgsInitStatus.Ready != UgsInitializer.Status)
            {
                return;
            }

            try
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                var currency = new CurrencySaveData { Balance = gameManager.SoftCurrencyBalance, UpdatedAtUnixSeconds = now };
                var history = new HistorySaveData { Entries = new List<MatchHistoryEntry>(gameManager.MatchHistory), UpdatedAtUnixSeconds = now };

                var profile = new ProfileSaveData
                {
                    UpdatedAtUnixSeconds = now,
                    AvatarId = CosmeticSelection.AvatarId,
                    FrameId = CosmeticSelection.FrameId,
                    BannerId = CosmeticSelection.BannerId,
                    PieceSkinId = CosmeticSelection.PieceSkinId,
                    BoardSkinId = CosmeticSelection.BoardSkinId,
                };

                foreach (var pair in gameManager.GetAdaptiveLevelsSnapshot())
                {
                    profile.AdaptiveLevelsByBoardSize[pair.Key.ToString(CultureInfo.InvariantCulture)] = pair.Value;
                }

                var data = new Dictionary<string, object>
                {
                    { CurrencyKey, currency },
                    { HistoryKey, history },
                    { ProfileKey, profile },
                };

                await CloudSaveService.Instance.Data.Player.SaveAsync(data);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"PlayerDataService: save failed, local state is still the source of truth - {ex.Message}");
            }
        }
    }
}
