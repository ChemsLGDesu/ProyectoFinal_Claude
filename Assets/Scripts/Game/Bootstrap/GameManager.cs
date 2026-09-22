using System;
using System.Collections.Generic;
using TTTXO.Core;
using TTTXO.Game.Services;
using UnityEngine;

namespace TTTXO.Game.Bootstrap
{
    /// <summary>
    /// Persistent session state for Milestone 1: current mode/board/difficulty selection,
    /// per-board-size adaptive AI state, and the in-memory soft currency wallet.
    ///
    /// Design notes (see Docs/design-doc.md sections 1 and 4):
    /// - First-player alternation is tracked per (mode, board size, difficulty) "match sequence".
    ///   Changing any of those three resets the sequence back to "match 1" (human/Player 1 starts as X).
    ///   Staying on the same configuration (including rematches) keeps alternating.
    /// - Soft currency lives in memory during a play session and is mirrored to PlayerPrefs so a
    ///   balance survives an app restart, per the design doc's "simple local persistence" requirement
    ///   for Milestone 1. This is a client-side convenience only; Milestone 2+ moves the source of
    ///   truth to Cloud Save/Cloud Code and the client stops writing currency directly.
    /// - The daily soft-currency cap (300, see design-doc.md section 4) cannot live inside
    ///   TTTXO.Core.RewardCalculator because that method is a pure/stateless function - the cap needs
    ///   day-to-day state, so it is enforced here instead.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        private const int DailySoftCurrencyCap = 300;
        private const int MaxMatchHistoryEntries = 50;
        private const float DefaultVolume = 0.8f;
        private const string PrefBalanceKey = "TTTXO.SoftCurrencyBalance";
        /// <summary>Display cache of the server-granted balance (ERR-KB-006). Never authoritative: Cloud Save class Protected is.</summary>
        private const string PrefAuthoritativeBalanceKey = "TTTXO.AuthoritativeCurrencyBalance";
        private const string PrefDailyEarnedKey = "TTTXO.DailyEarnedSoftCurrency";
        private const string PrefDailyResetDateKey = "TTTXO.DailyResetDate";
        private const string PrefSoundVolumeKey = "TTTXO.SoundVolume";
        private const string PrefMusicVolumeKey = "TTTXO.MusicVolume";
        private const string PrefMatchHistoryKey = "TTTXO.MatchHistory";
        private const string PrefLocalStateUpdatedAtKey = "TTTXO.LocalStateUpdatedAt";

        public static GameManager Instance { get; private set; }

        public GameMode Mode { get; private set; } = GameMode.SinglePlayer;
        public BoardConfig SelectedBoard { get; private set; }
        public AiDifficulty SelectedDifficulty { get; private set; } = AiDifficulty.Medium;

        /// <summary>
        /// True while the player is inside the Online -&gt; Quickmatch flow (ModeSelect's Online
        /// submenu -&gt; BoardSelect -&gt; Matchmaking -&gt; Game). Tracked separately from <see cref="Mode"/>
        /// because <see cref="GameMode"/> is defined in TTTXO.Core, which stays 1P/local-only by
        /// design (Docs/01-Directrices-Proyecto.md: "TTTXO.Core no se modifica") - Milestone 4's
        /// online session state lives entirely in the Game layer instead (this flag plus
        /// <see cref="Services.MatchmakingService"/>/<see cref="Services.OnlineMatchService"/>).
        /// </summary>
        public bool IsOnlineFlow { get; private set; }

        /// <summary>
        /// True while the player is inside the Online -&gt; Ranked flow specifically (Milestone 5 -
        /// design-doc.md section 6). Always implies <see cref="IsOnlineFlow"/> - Ranked reuses every
        /// "this is an online match" branch (Board Select's difficulty section stays hidden, Game
        /// screen routes moves through Cloud Code, etc.) and only needs its own flag where Ranked
        /// differs from Quickmatch: which Matchmaker queue/mode to use (<see cref="Services.MatchmakingService"/>)
        /// and which board sizes Board Select offers (3x3/6x6 only, design-doc.md section 6.0).
        /// </summary>
        public bool IsRankedFlow { get; private set; }

        public void SetPendingOnlineFlow()
        {
            IsOnlineFlow = true;
            IsRankedFlow = false;
        }

        /// <summary>Milestone 5: Online -&gt; Ranked (ModeSelect's online submenu).</summary>
        public void SetPendingRankedFlow()
        {
            IsOnlineFlow = true;
            IsRankedFlow = true;
        }

        public void ClearOnlineFlow()
        {
            IsOnlineFlow = false;
            IsRankedFlow = false;
        }

        /// <summary>
        /// Board size the player picked for a Quickmatch search - remembered only so the Matchmaking
        /// screen's "Play vs AI" fallback (Milestone 4, the one documented deviation from
        /// Docs/06-Wireframes-UI.md screen 5) can land on Board Select with the same size
        /// pre-selected instead of forcing a re-pick. Consumed (cleared) once used - see
        /// BoardSelectScreenController.OnShow.
        /// </summary>
        public int? LastQuickmatchBoardSize { get; private set; }

        public void SetLastQuickmatchBoardSize(int boardSize)
        {
            LastQuickmatchBoardSize = boardSize;
        }

        public void ClearLastQuickmatchBoardSize()
        {
            LastQuickmatchBoardSize = null;
        }

        /// <summary>
        /// True when the human player (single player) or Player 1 (local multiplayer) is assigned
        /// the X symbol - and therefore starts - for the next match played under the current
        /// match sequence key. Flips every time a match is completed (see <see cref="NotifyMatchCompleted"/>).
        /// </summary>
        public bool HumanOrPlayer1StartsAsX { get; private set; } = true;

        /// <summary>
        /// The offline, local-first wallet: what 1P and 2P local play earns. PlayerPrefs is its
        /// source of truth and Cloud Save key <c>currency</c> is a mirror the client owns and
        /// writes. Server-granted online rewards deliberately do NOT land here - see
        /// <see cref="AuthoritativeCurrencyBalance"/>.
        /// </summary>
        public int SoftCurrencyBalance { get; private set; }

        /// <summary>
        /// The server-granted balance: online Quickmatch and Ranked rewards, computed by Cloud Code
        /// with its caps and anti-collusion checks. The client can only ever read this - it lives in
        /// Cloud Save key <c>authoritativeCurrency</c> under access class Protected, and the value
        /// kept here is a display cache so the number survives a restart with no network.
        ///
        /// The split is the fix for ERR-KB-006. Online rewards used to be credited into the same
        /// <c>currency</c> key the client is allowed to write, which made the server's caps
        /// decorative: one REST POST with the player's own token skipped all of them. Local play
        /// still earns into a writable key because a match the server never saw cannot be verified
        /// by any design - but the two are no longer the same number, and the store will spend from
        /// this one.
        /// </summary>
        public int AuthoritativeCurrencyBalance { get; private set; }

        /// <summary>What the player is shown: the two wallets read as one balance (see the two properties above).</summary>
        public int TotalCurrencyBalance => SoftCurrencyBalance + AuthoritativeCurrencyBalance;

        /// <summary>
        /// Hard currency balance shown in the header/Store per Docs/06-Wireframes-UI.md screens 2 and 11.
        /// There is no earn or purchase path yet in Milestone 1 (Store is a navigable stub, see
        /// <c>StoreScreenController</c>), so this is always 0 until IAP/RedeemStoreItem lands in a
        /// later milestone - kept as a computed constant rather than persisted state to avoid dead code.
        /// </summary>
        public int HardCurrencyBalance => 0;

        /// <summary>Sound effects volume (0-1), persisted to PlayerPrefs. No audio system plays it back yet.</summary>
        public float SoundVolume { get; private set; } = DefaultVolume;

        /// <summary>Music volume (0-1), persisted to PlayerPrefs. No audio system plays it back yet.</summary>
        public float MusicVolume { get; private set; } = DefaultVolume;

        /// <summary>Local match history, most recent first, capped at <see cref="MaxMatchHistoryEntries"/> entries.</summary>
        public IReadOnlyList<MatchHistoryEntry> MatchHistory => _matchHistory;

        /// <summary>
        /// Unix timestamp of the last local write to currency/history (PlayerPrefs). Used by
        /// <see cref="PlayerDataService"/> as the "who's newer" tiebreaker against the Cloud
        /// Save snapshot at session start (Milestone 2's simple mirror - see design-doc.md and
        /// Docs/03-Arquitectura-UGS-TicTacToe.md#Cloud Save).
        /// </summary>
        public long LocalStateUpdatedAtUnixSeconds { get; private set; }

        private readonly Dictionary<int, AdaptiveAiController> _adaptiveControllersByBoardSize = new();
        private readonly List<MatchHistoryEntry> _matchHistory = new();
        private (GameMode Mode, int BoardSize, AiDifficulty? Difficulty)? _lastMatchSequenceKey;
        private int _dailyEarnedSoftCurrency;
        private string _dailyResetDate;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadPersistedState();
        }

        /// <summary>
        /// Records the mode chosen on the Mode Select screen so Board Select knows whether to show
        /// the difficulty picker. The choice only becomes "official" once <see cref="ConfigureMatch"/>
        /// is called from the Board Select confirm button.
        /// </summary>
        public void SetPendingMode(GameMode mode)
        {
            Mode = mode;
            IsOnlineFlow = false;
            IsRankedFlow = false;
        }

        /// <summary>
        /// Applies the mode/board/difficulty chosen on the Board Select screen. Resets the
        /// first-player alternation sequence when the configuration differs from the last one played.
        /// </summary>
        public void ConfigureMatch(GameMode mode, BoardConfig board, AiDifficulty difficulty)
        {
            Mode = mode;
            SelectedBoard = board;
            SelectedDifficulty = difficulty;

            AiDifficulty? difficultyKey = mode == GameMode.SinglePlayer ? difficulty : null;
            var key = (mode, board.Size, difficultyKey);

            if (_lastMatchSequenceKey == null || !_lastMatchSequenceKey.Value.Equals(key))
            {
                HumanOrPlayer1StartsAsX = true;
                _lastMatchSequenceKey = key;
            }
        }

        /// <summary>Call once a match reaches a terminal status so the next match in the same sequence swaps symbols.</summary>
        public void NotifyMatchCompleted()
        {
            HumanOrPlayer1StartsAsX = !HumanOrPlayer1StartsAsX;
        }

        /// <summary>Gets (creating if needed) the adaptive AI state for a given board size. Level is tracked per size.</summary>
        public AdaptiveAiController GetAdaptiveController(int boardSize)
        {
            if (!_adaptiveControllersByBoardSize.TryGetValue(boardSize, out var controller))
            {
                controller = new AdaptiveAiController();
                _adaptiveControllersByBoardSize[boardSize] = controller;
            }

            return controller;
        }

        /// <summary>
        /// Applies the daily cap to a raw reward, adds the clamped amount to the wallet, persists it,
        /// and returns the amount actually granted (for display on the Result screen).
        /// </summary>
        public int AwardMatchReward(int rawAmount)
        {
            ResetDailyEarningsIfNewDay();

            int remainingToday = Mathf.Max(0, DailySoftCurrencyCap - _dailyEarnedSoftCurrency);
            int actualAmount = Mathf.Clamp(rawAmount, 0, remainingToday);

            _dailyEarnedSoftCurrency += actualAmount;
            SoftCurrencyBalance += actualAmount;
            PersistState();

            if (actualAmount > 0)
            {
                GameAnalytics.SoftCurrencyEarned(actualAmount, "match_reward");
            }

            return actualAmount;
        }

        /// <summary>
        /// Milestone 4, design-doc.md section 4 "Modo online Quickmatch": applies a soft-currency
        /// amount the server already computed and credited to Cloud Save <c>currency</c> (Cloud
        /// Code's MatchFunctions.PlayMove/GetMatchState - see OnlineMatchService/GameScreenController)
        /// to the in-session wallet, purely for immediate Home/header display. Deliberately does
        /// NOT call <see cref="PersistState"/>/<see cref="TouchLocalStateUpdatedAt"/>: online
        /// rewards are server-authoritative, not local state, and touching the local
        /// "updatedAt" timestamp here would risk this device-local guess winning the
        /// newest-wins comparison in <see cref="PlayerDataService.LoadIntoSessionAsync"/>
        /// against the real Cloud Save balance on a later session start. Also does NOT touch the
        /// offline daily-cap counters (<see cref="AwardMatchReward"/>'s <c>_dailyEarnedSoftCurrency</c>) -
        /// online caps are enforced entirely server-side (see CloudCode~/TicTacToeModule/
        /// OnlineRewardCalculator.cs), not by this device-local 300/day counter.
        /// </summary>
        public void ApplyOnlineRewardCredit(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            // Into the authoritative side, never into SoftCurrencyBalance. That property is mirrored
            // to Cloud Save key `currency` by PlayerDataService, so crediting it here would push a
            // server-granted reward back into the one key the client is allowed to write - and it
            // would be counted twice, since the server already banked it in `authoritativeCurrency`.
            // This is only the in-session bump so the header moves immediately; the next
            // PlayerDataService pull replaces it with the server's own number.
            // In-memory only, and deliberately no PersistState(): that would call
            // TouchLocalStateUpdatedAt, and stamping the local-state clock here would let this
            // device-local guess beat the real Cloud Save balance on the next session start. The
            // authoritative number arrives from the server on the next load; this is just the
            // header moving before it does. Guarded by
            // GameManagerTests.ApplyOnlineRewardCredit_DoesNotTouchLocalStateTimestamp.
            AuthoritativeCurrencyBalance += amount;
        }

        /// <summary>
        /// Applies the Cloud Save <c>authoritativeCurrency</c> snapshot (access class Protected, so
        /// this is the server's number and the client has no way to have altered it). Unlike
        /// <see cref="ApplyCloudCurrencySnapshot"/> there is no "is the cloud newer?" check: the
        /// local value is a display cache, not a competing source of truth, so the server always
        /// wins.
        /// </summary>
        public void ApplyCloudAuthoritativeCurrencySnapshot(int balance)
        {
            AuthoritativeCurrencyBalance = Mathf.Max(0, balance);

            // Writes only its own cache key. Going through PersistState would touch the local-state
            // timestamp that decides freshness for `currency`, `history` and `profile`, and this
            // value has no say over any of those.
            PlayerPrefs.SetInt(PrefAuthoritativeBalanceKey, AuthoritativeCurrencyBalance);
            PlayerPrefs.Save();
        }

        private void ResetDailyEarningsIfNewDay()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (_dailyResetDate != today)
            {
                _dailyResetDate = today;
                _dailyEarnedSoftCurrency = 0;
            }
        }

        /// <summary>Persists the sound effects slider value (Settings screen). No audio system reads it yet.</summary>
        public void SetSoundVolume(float value)
        {
            SoundVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(PrefSoundVolumeKey, SoundVolume);
            PlayerPrefs.Save();
        }

        /// <summary>Persists the music slider value (Settings screen). No audio system reads it yet.</summary>
        public void SetMusicVolume(float value)
        {
            MusicVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(PrefMusicVolumeKey, MusicVolume);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Appends one completed match to the local history (most recent first), trims to
        /// <see cref="MaxMatchHistoryEntries"/> and persists. Only call for matches that reached a
        /// terminal status - abandoned matches are not recorded, mirroring the 0-reward rule.
        /// </summary>
        public void RecordMatchHistory(GameMode mode, int boardSize, AiDifficulty? difficulty, MatchOutcome outcome, float durationSeconds)
        {
            var entry = new MatchHistoryEntry
            {
                Mode = mode,
                BoardSize = boardSize,
                DifficultyValue = difficulty.HasValue ? (int)difficulty.Value : -1,
                Outcome = outcome,
                DurationSeconds = Mathf.Max(0, Mathf.RoundToInt(durationSeconds)),
                TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };

            _matchHistory.Insert(0, entry);
            if (_matchHistory.Count > MaxMatchHistoryEntries)
            {
                _matchHistory.RemoveRange(MaxMatchHistoryEntries, _matchHistory.Count - MaxMatchHistoryEntries);
            }

            PersistMatchHistory();
        }

        /// <summary>
        /// "Delete my data" / "Delete account & data" (Docs/06-Wireframes-UI.md screens 8 and 12):
        /// wipes every locally persisted value and resets in-memory session state to Milestone 1
        /// defaults. There is no server-side account yet, so this only ever touches PlayerPrefs.
        /// </summary>
        public void DeleteAllPlayerData()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();

#if TTTXO_LOCAL_ANALYTICS
            // Beta builds keep recorded events in a local .jsonl file, which is player data like any
            // other - leaving it behind would make this screen's promise false.
            TTTXO.Game.Services.LocalAnalyticsSink.DeleteAll();
#endif

            SoftCurrencyBalance = 0;
            AuthoritativeCurrencyBalance = 0;
            _dailyEarnedSoftCurrency = 0;
            _dailyResetDate = DateTime.Now.ToString("yyyy-MM-dd");
            LocalStateUpdatedAtUnixSeconds = 0;
            _matchHistory.Clear();
            _adaptiveControllersByBoardSize.Clear();
            SoundVolume = DefaultVolume;
            MusicVolume = DefaultVolume;
            HumanOrPlayer1StartsAsX = true;
            _lastMatchSequenceKey = null;
        }

        private void LoadPersistedState()
        {
            SoftCurrencyBalance = PlayerPrefs.GetInt(PrefBalanceKey, 0);
            AuthoritativeCurrencyBalance = PlayerPrefs.GetInt(PrefAuthoritativeBalanceKey, 0);
            _dailyEarnedSoftCurrency = PlayerPrefs.GetInt(PrefDailyEarnedKey, 0);
            _dailyResetDate = PlayerPrefs.GetString(PrefDailyResetDateKey, DateTime.Now.ToString("yyyy-MM-dd"));
            SoundVolume = PlayerPrefs.GetFloat(PrefSoundVolumeKey, DefaultVolume);
            MusicVolume = PlayerPrefs.GetFloat(PrefMusicVolumeKey, DefaultVolume);
            LocalStateUpdatedAtUnixSeconds = long.TryParse(PlayerPrefs.GetString(PrefLocalStateUpdatedAtKey, "0"), out long updatedAt) ? updatedAt : 0;
            ResetDailyEarningsIfNewDay();
            LoadMatchHistory();
        }

        private void PersistState()
        {
            PlayerPrefs.SetInt(PrefBalanceKey, SoftCurrencyBalance);
            PlayerPrefs.SetInt(PrefAuthoritativeBalanceKey, AuthoritativeCurrencyBalance);
            PlayerPrefs.SetInt(PrefDailyEarnedKey, _dailyEarnedSoftCurrency);
            PlayerPrefs.SetString(PrefDailyResetDateKey, _dailyResetDate);
            TouchLocalStateUpdatedAt();
            PlayerPrefs.Save();
        }

        private void PersistMatchHistory()
        {
            var data = new MatchHistoryData { Entries = _matchHistory };
            PlayerPrefs.SetString(PrefMatchHistoryKey, JsonUtility.ToJson(data));
            TouchLocalStateUpdatedAt();
            PlayerPrefs.Save();
        }

        private void TouchLocalStateUpdatedAt()
        {
            LocalStateUpdatedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            PlayerPrefs.SetString(PrefLocalStateUpdatedAtKey, LocalStateUpdatedAtUnixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Applies a Cloud Save <c>currency</c> snapshot that is newer than local state (see
        /// <see cref="PlayerDataService.LoadIntoSessionAsync"/>). Persists locally too, so
        /// the mirror stays consistent even if the app is closed right after.
        /// </summary>
        public void ApplyCloudCurrencySnapshot(int balance)
        {
            SoftCurrencyBalance = Mathf.Max(0, balance);
            PersistState();
        }

        /// <summary>Applies a Cloud Save <c>history</c> snapshot that is newer than local state (see <see cref="PlayerDataService"/>).</summary>
        public void ApplyCloudMatchHistorySnapshot(List<MatchHistoryEntry> entries)
        {
            _matchHistory.Clear();
            if (entries != null)
            {
                _matchHistory.AddRange(entries);
            }

            if (_matchHistory.Count > MaxMatchHistoryEntries)
            {
                _matchHistory.RemoveRange(MaxMatchHistoryEntries, _matchHistory.Count - MaxMatchHistoryEntries);
            }

            PersistMatchHistory();
        }

        /// <summary>Applies a Cloud Save <c>profile</c> snapshot's adaptive AI levels that are newer than local state (see <see cref="PlayerDataService"/>).</summary>
        public void ApplyCloudAdaptiveLevelsSnapshot(IReadOnlyDictionary<int, int> levelsByBoardSize)
        {
            if (levelsByBoardSize == null)
            {
                return;
            }

            foreach (var pair in levelsByBoardSize)
            {
                _adaptiveControllersByBoardSize[pair.Key] = new AdaptiveAiController(pair.Value);
            }
        }

        /// <summary>Snapshot of the current adaptive AI level per board size, for <see cref="PlayerDataService.SaveAfterMatchAsync"/>.</summary>
        public IReadOnlyDictionary<int, int> GetAdaptiveLevelsSnapshot()
        {
            var snapshot = new Dictionary<int, int>();
            foreach (var pair in _adaptiveControllersByBoardSize)
            {
                snapshot[pair.Key] = pair.Value.CurrentLevel;
            }

            return snapshot;
        }

        private void LoadMatchHistory()
        {
            string json = PlayerPrefs.GetString(PrefMatchHistoryKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            var data = JsonUtility.FromJson<MatchHistoryData>(json);
            if (data?.Entries != null)
            {
                _matchHistory.AddRange(data.Entries);
            }
        }

        /// <summary>Wrapper required by JsonUtility, which cannot serialize a top-level List directly.</summary>
        [Serializable]
        private class MatchHistoryData
        {
            public List<MatchHistoryEntry> Entries = new();
        }

        /// <summary>
        /// Risk #3 (Docs/03-Arquitectura-UGS-TicTacToe.md "Resolucion detallada de riesgos #3"):
        /// "cancelacion explicita del ticket... al detectar que la app pasa a background
        /// (OnApplicationPause)". Lives here rather than on the Matchmaking screen controller because
        /// <see cref="GameManager"/> is the one persistent MonoBehaviour that is always present
        /// regardless of which screen is visible - a fire-and-forget best-effort cancel, matching
        /// <see cref="Services.MatchmakingService.CancelIfSearchingAsync"/>'s own no-throw contract.
        /// </summary>
        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                _ = MatchmakingService.CancelIfSearchingAsync("app_background");
            }
        }
    }
}
