using System.Collections.Generic;
using NUnit.Framework;
using TTTXO.Core;
using TTTXO.Game.Bootstrap;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TTTXO.Game.Tests
{
    /// <summary>
    /// Covers the session state <see cref="GameManager"/> owns: the offline daily soft-currency cap,
    /// first-player alternation, local match history and the Cloud Save snapshot appliers.
    ///
    /// <see cref="GameManager"/> is a MonoBehaviour, but none of this needs play mode: EditMode does
    /// not run <c>Awake</c> for a plain MonoBehaviour, so each test gets a component whose fields are
    /// at their declared defaults and whose persisted state was never loaded - which is exactly the
    /// deterministic starting point these assertions want. <see cref="PlayerPrefsSandbox"/> clears
    /// the store in SetUp so the outcome does not depend on what the developer's Editor had saved.
    /// </summary>
    [TestFixture]
    public class GameManagerTests
    {
        private const int DailySoftCurrencyCap = 300;
        private const int MaxMatchHistoryEntries = 50;

        private PlayerPrefsSandbox _prefs;
        private GameObject _host;
        private GameManager _manager;

        [SetUp]
        public void SetUp()
        {
            _prefs = PlayerPrefsSandbox.CaptureAndClear();
            _host = new GameObject(nameof(GameManagerTests));
            _manager = _host.AddComponent<GameManager>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }

            _prefs.Restore();
        }

        // ---------------------------------------------------------------------------------------
        // Offline daily soft-currency cap (design-doc.md section 4: 300/day, enforced here because
        // TTTXO.Core.RewardCalculator is a pure function with no day-to-day state).
        // ---------------------------------------------------------------------------------------

        [Test]
        public void AwardMatchReward_GrantsFullAmount_WhenUnderDailyCap()
        {
            int granted = _manager.AwardMatchReward(100);

            Assert.AreEqual(100, granted);
            Assert.AreEqual(100, _manager.SoftCurrencyBalance);
        }

        [Test]
        public void AwardMatchReward_ClampsToRemainingDailyAllowance()
        {
            _manager.AwardMatchReward(250);

            int granted = _manager.AwardMatchReward(100);

            Assert.AreEqual(50, granted, "only the remaining 50 of the 300/day cap may be granted");
            Assert.AreEqual(DailySoftCurrencyCap, _manager.SoftCurrencyBalance);
        }

        [Test]
        public void AwardMatchReward_GrantsNothing_OnceCapIsReached()
        {
            _manager.AwardMatchReward(DailySoftCurrencyCap);

            int granted = _manager.AwardMatchReward(50);

            Assert.AreEqual(0, granted);
            Assert.AreEqual(DailySoftCurrencyCap, _manager.SoftCurrencyBalance);
        }

        [Test]
        public void AwardMatchReward_IgnoresNegativeAmounts()
        {
            int granted = _manager.AwardMatchReward(-100);

            Assert.AreEqual(0, granted);
            Assert.AreEqual(0, _manager.SoftCurrencyBalance, "a negative reward must never drain the wallet");
        }

        [Test]
        public void AwardMatchReward_PersistsBalanceAndTouchesLocalTimestamp()
        {
            _manager.AwardMatchReward(70);

            Assert.AreEqual(70, PlayerPrefs.GetInt("TTTXO.SoftCurrencyBalance", -1));
            Assert.Greater(
                _manager.LocalStateUpdatedAtUnixSeconds,
                0,
                "a local write must stamp the tiebreaker PlayerDataService uses against Cloud Save");
        }

        // ---------------------------------------------------------------------------------------
        // Online rewards are server-authoritative. ApplyOnlineRewardCredit is display-only and must
        // stay out of both the offline cap counters and the local "who's newer" timestamp - see the
        // method's own docs and CloudCode~/TicTacToeModule/OnlineRewardCalculator.cs.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void ApplyOnlineRewardCredit_AddsToBalance_WithoutConsumingTheOfflineDailyCap()
        {
            _manager.ApplyOnlineRewardCredit(DailySoftCurrencyCap);

            int grantedOffline = _manager.AwardMatchReward(DailySoftCurrencyCap);

            Assert.AreEqual(DailySoftCurrencyCap, grantedOffline, "online credit must not eat the offline 300/day allowance");
            Assert.AreEqual(DailySoftCurrencyCap * 2, _manager.TotalCurrencyBalance, "the player is shown both wallets as one number");
        }

        /// <summary>
        /// The client half of ERR-KB-006. <see cref="GameManager.SoftCurrencyBalance"/> is mirrored
        /// to Cloud Save key <c>currency</c>, which is access class Default and therefore writable by
        /// the player. A server-granted reward that landed there would be laundered straight back
        /// into the key the whole fix exists to stop trusting - and double-counted, because Cloud
        /// Code already banked it in <c>authoritativeCurrency</c>.
        /// </summary>
        [Test]
        public void ApplyOnlineRewardCredit_NeverLandsInTheClientWritableWallet()
        {
            _manager.ApplyOnlineRewardCredit(120);

            Assert.AreEqual(0, _manager.SoftCurrencyBalance, "an online reward must not reach the wallet mirrored to the player-writable `currency` key");
            Assert.AreEqual(120, _manager.AuthoritativeCurrencyBalance);
            Assert.AreEqual(120, _manager.TotalCurrencyBalance);
        }

        /// <summary>
        /// The offline wallet is the one the client owns and mirrors; it must stay out of the
        /// authoritative balance just as firmly as the reverse.
        /// </summary>
        [Test]
        public void AwardMatchReward_NeverLandsInTheAuthoritativeWallet()
        {
            _manager.AwardMatchReward(75);

            Assert.AreEqual(75, _manager.SoftCurrencyBalance);
            Assert.AreEqual(0, _manager.AuthoritativeCurrencyBalance, "only Cloud Code grants the authoritative balance");
        }

        [Test]
        public void ApplyCloudAuthoritativeCurrencySnapshot_ReplacesRatherThanAdds_AndClampsNegatives()
        {
            _manager.ApplyOnlineRewardCredit(500);

            // The in-session bump is a guess; the server's number replaces it outright. No freshness
            // check, unlike the Default-class keys - the local value is a display cache, not a
            // competing source of truth.
            _manager.ApplyCloudAuthoritativeCurrencySnapshot(40);
            Assert.AreEqual(40, _manager.AuthoritativeCurrencyBalance);

            _manager.ApplyCloudAuthoritativeCurrencySnapshot(-40);
            Assert.AreEqual(0, _manager.AuthoritativeCurrencyBalance);
        }

        [Test]
        public void ApplyOnlineRewardCredit_DoesNotTouchLocalStateTimestamp()
        {
            _manager.ApplyOnlineRewardCredit(50);

            Assert.AreEqual(
                0,
                _manager.LocalStateUpdatedAtUnixSeconds,
                "stamping here would let this device-local guess beat the real Cloud Save balance on the next session start");
        }

        [Test]
        public void ApplyOnlineRewardCredit_IgnoresNonPositiveAmounts([Values(0, -25)] int amount)
        {
            _manager.ApplyOnlineRewardCredit(amount);

            Assert.AreEqual(0, _manager.SoftCurrencyBalance);
            Assert.AreEqual(0, _manager.AuthoritativeCurrencyBalance);
        }

        [Test]
        public void ApplyCloudCurrencySnapshot_ClampsNegativeBalanceToZero()
        {
            _manager.ApplyCloudCurrencySnapshot(-40);

            Assert.AreEqual(0, _manager.SoftCurrencyBalance);
        }

        // ---------------------------------------------------------------------------------------
        // First-player alternation. The sequence key is (mode, board size, difficulty), and
        // difficulty only participates in Single Player.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void ConfigureMatch_StartsSequenceWithPlayerOneAsX()
        {
            _manager.ConfigureMatch(GameMode.SinglePlayer, BoardConfig.ForSize(3), AiDifficulty.Medium);

            Assert.IsTrue(_manager.HumanOrPlayer1StartsAsX);
        }

        [Test]
        public void NotifyMatchCompleted_AlternatesTheStartingSymbol()
        {
            _manager.ConfigureMatch(GameMode.SinglePlayer, BoardConfig.ForSize(3), AiDifficulty.Medium);

            _manager.NotifyMatchCompleted();
            Assert.IsFalse(_manager.HumanOrPlayer1StartsAsX);

            _manager.NotifyMatchCompleted();
            Assert.IsTrue(_manager.HumanOrPlayer1StartsAsX);
        }

        [Test]
        public void ConfigureMatch_KeepsAlternating_OnRematchWithTheSameConfiguration()
        {
            _manager.ConfigureMatch(GameMode.SinglePlayer, BoardConfig.ForSize(3), AiDifficulty.Medium);
            _manager.NotifyMatchCompleted();

            _manager.ConfigureMatch(GameMode.SinglePlayer, BoardConfig.ForSize(3), AiDifficulty.Medium);

            Assert.IsFalse(_manager.HumanOrPlayer1StartsAsX, "a rematch continues the sequence instead of restarting it");
        }

        [Test]
        public void ConfigureMatch_ResetsAlternation_WhenBoardSizeChanges()
        {
            _manager.ConfigureMatch(GameMode.SinglePlayer, BoardConfig.ForSize(3), AiDifficulty.Medium);
            _manager.NotifyMatchCompleted();

            _manager.ConfigureMatch(GameMode.SinglePlayer, BoardConfig.ForSize(6), AiDifficulty.Medium);

            Assert.IsTrue(_manager.HumanOrPlayer1StartsAsX);
        }

        [Test]
        public void ConfigureMatch_ResetsAlternation_WhenDifficultyChangesInSinglePlayer()
        {
            _manager.ConfigureMatch(GameMode.SinglePlayer, BoardConfig.ForSize(3), AiDifficulty.Easy);
            _manager.NotifyMatchCompleted();

            _manager.ConfigureMatch(GameMode.SinglePlayer, BoardConfig.ForSize(3), AiDifficulty.Hard);

            Assert.IsTrue(_manager.HumanOrPlayer1StartsAsX);
        }

        [Test]
        public void ConfigureMatch_IgnoresDifficulty_InLocalMultiplayer()
        {
            _manager.ConfigureMatch(GameMode.LocalMultiplayer, BoardConfig.ForSize(3), AiDifficulty.Easy);
            _manager.NotifyMatchCompleted();

            _manager.ConfigureMatch(GameMode.LocalMultiplayer, BoardConfig.ForSize(3), AiDifficulty.Hard);

            Assert.IsFalse(
                _manager.HumanOrPlayer1StartsAsX,
                "there is no AI in local multiplayer, so difficulty is not part of the sequence key");
        }

        [Test]
        public void ConfigureMatch_ResetsAlternation_WhenModeChanges()
        {
            _manager.ConfigureMatch(GameMode.SinglePlayer, BoardConfig.ForSize(3), AiDifficulty.Medium);
            _manager.NotifyMatchCompleted();

            _manager.ConfigureMatch(GameMode.LocalMultiplayer, BoardConfig.ForSize(3), AiDifficulty.Medium);

            Assert.IsTrue(_manager.HumanOrPlayer1StartsAsX);
        }

        // ---------------------------------------------------------------------------------------
        // Local match history (Docs/06-Wireframes-UI.md screen 9): most recent first, capped at 50.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void RecordMatchHistory_InsertsMostRecentFirst()
        {
            _manager.RecordMatchHistory(GameMode.SinglePlayer, 3, AiDifficulty.Easy, MatchOutcome.Win, 10f);
            _manager.RecordMatchHistory(GameMode.SinglePlayer, 6, AiDifficulty.Hard, MatchOutcome.Loss, 20f);

            Assert.AreEqual(2, _manager.MatchHistory.Count);
            Assert.AreEqual(6, _manager.MatchHistory[0].BoardSize);
            Assert.AreEqual(MatchOutcome.Loss, _manager.MatchHistory[0].Outcome);
        }

        [Test]
        public void RecordMatchHistory_CapsAtFiftyEntries_DroppingTheOldest()
        {
            for (int i = 0; i < MaxMatchHistoryEntries + 5; i++)
            {
                _manager.RecordMatchHistory(GameMode.SinglePlayer, 3, AiDifficulty.Easy, MatchOutcome.Win, i);
            }

            Assert.AreEqual(MaxMatchHistoryEntries, _manager.MatchHistory.Count);
            Assert.AreEqual(
                MaxMatchHistoryEntries + 4,
                _manager.MatchHistory[0].DurationSeconds,
                "the newest entry survives");
            Assert.AreEqual(
                5,
                _manager.MatchHistory[MaxMatchHistoryEntries - 1].DurationSeconds,
                "the five oldest entries were dropped");
        }

        [Test]
        public void RecordMatchHistory_MarksDifficultyNotApplicable_ForLocalMultiplayer()
        {
            _manager.RecordMatchHistory(GameMode.LocalMultiplayer, 3, null, MatchOutcome.Draw, 30f);

            Assert.AreEqual(-1, _manager.MatchHistory[0].DifficultyValue);
            Assert.IsNull(_manager.MatchHistory[0].Difficulty);
        }

        [Test]
        public void RecordMatchHistory_ClampsNegativeDurationToZero()
        {
            _manager.RecordMatchHistory(GameMode.SinglePlayer, 3, AiDifficulty.Easy, MatchOutcome.Win, -5f);

            Assert.AreEqual(0, _manager.MatchHistory[0].DurationSeconds);
        }

        [Test]
        public void ApplyCloudMatchHistorySnapshot_TrimsToTheEntryCap()
        {
            var entries = new List<MatchHistoryEntry>();
            for (int i = 0; i < MaxMatchHistoryEntries + 10; i++)
            {
                entries.Add(new MatchHistoryEntry { BoardSize = 3, DurationSeconds = i });
            }

            _manager.ApplyCloudMatchHistorySnapshot(entries);

            Assert.AreEqual(MaxMatchHistoryEntries, _manager.MatchHistory.Count);
            Assert.AreEqual(0, _manager.MatchHistory[0].DurationSeconds, "the snapshot's own order is preserved");
        }

        [Test]
        public void ApplyCloudMatchHistorySnapshot_ClearsHistory_WhenGivenNull()
        {
            _manager.RecordMatchHistory(GameMode.SinglePlayer, 3, AiDifficulty.Easy, MatchOutcome.Win, 10f);

            _manager.ApplyCloudMatchHistorySnapshot(null);

            Assert.AreEqual(0, _manager.MatchHistory.Count);
        }

        // ---------------------------------------------------------------------------------------
        // Adaptive AI state is tracked per board size (design-doc.md section 3).
        // ---------------------------------------------------------------------------------------

        [Test]
        public void GetAdaptiveController_ReturnsTheSameInstanceForABoardSize()
        {
            var first = _manager.GetAdaptiveController(3);
            var second = _manager.GetAdaptiveController(3);

            Assert.AreSame(first, second);
        }

        [Test]
        public void GetAdaptiveController_KeepsBoardSizesIndependent()
        {
            var threeByThree = _manager.GetAdaptiveController(3);
            var sixBySix = _manager.GetAdaptiveController(6);

            Assert.AreNotSame(threeByThree, sixBySix);
        }

        [Test]
        public void ApplyCloudAdaptiveLevelsSnapshot_OverwritesTheLevelPerBoardSize()
        {
            _manager.ApplyCloudAdaptiveLevelsSnapshot(new Dictionary<int, int> { { 3, 20 }, { 6, 80 } });

            Assert.AreEqual(20, _manager.GetAdaptiveController(3).CurrentLevel);
            Assert.AreEqual(80, _manager.GetAdaptiveController(6).CurrentLevel);
        }

        [Test]
        public void ApplyCloudAdaptiveLevelsSnapshot_IsNullSafe()
        {
            Assert.DoesNotThrow(() => _manager.ApplyCloudAdaptiveLevelsSnapshot(null));
        }

        [Test]
        public void GetAdaptiveLevelsSnapshot_ReportsEveryTrackedBoardSize()
        {
            _manager.ApplyCloudAdaptiveLevelsSnapshot(new Dictionary<int, int> { { 3, 20 } });
            _manager.GetAdaptiveController(9);

            var snapshot = _manager.GetAdaptiveLevelsSnapshot();

            Assert.AreEqual(2, snapshot.Count);
            Assert.AreEqual(20, snapshot[3]);
            Assert.IsTrue(snapshot.ContainsKey(9));
        }

        // ---------------------------------------------------------------------------------------
        // Settings sliders and the privacy "delete my data" path.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void SetSoundVolume_ClampsToUnitRange([Values(-0.5f, 1.5f)] float value)
        {
            _manager.SetSoundVolume(value);

            Assert.That(_manager.SoundVolume, Is.InRange(0f, 1f));
        }

        [Test]
        public void SetMusicVolume_PersistsTheClampedValue()
        {
            _manager.SetMusicVolume(0.25f);

            Assert.AreEqual(0.25f, _manager.MusicVolume, 0.0001f);
            Assert.AreEqual(0.25f, PlayerPrefs.GetFloat("TTTXO.MusicVolume", -1f), 0.0001f);
        }

        /// <summary>
        /// "Delete my data" / "Delete account &amp; data" (Docs/06-Wireframes-UI.md screens 8 and 12).
        ///
        /// Note this test really does call <c>PlayerPrefs.DeleteAll()</c>, which wipes every key for
        /// the project and not only the TTTXO ones - the sandbox restores the game's own keys, but
        /// Unity's internal per-project prefs are collateral and get recreated on demand. That is the
        /// cost of covering the privacy path with the production method rather than a stand-in.
        /// </summary>
        [Test]
        public void DeleteAllPlayerData_ResetsSessionStateAndWipesPersistedKeys()
        {
            _manager.AwardMatchReward(120);
            _manager.RecordMatchHistory(GameMode.SinglePlayer, 3, AiDifficulty.Easy, MatchOutcome.Win, 10f);
            _manager.GetAdaptiveController(3);
            _manager.SetSoundVolume(0.1f);
            _manager.NotifyMatchCompleted();

            _manager.DeleteAllPlayerData();

            Assert.AreEqual(0, _manager.SoftCurrencyBalance);
            Assert.AreEqual(0, _manager.MatchHistory.Count);
            Assert.AreEqual(0, _manager.LocalStateUpdatedAtUnixSeconds);
            Assert.AreEqual(0, _manager.GetAdaptiveLevelsSnapshot().Count);
            Assert.IsTrue(_manager.HumanOrPlayer1StartsAsX);
            Assert.IsFalse(PlayerPrefs.HasKey("TTTXO.SoftCurrencyBalance"));
            Assert.IsFalse(PlayerPrefs.HasKey("TTTXO.MatchHistory"));
        }

        [Test]
        public void DeleteAllPlayerData_ResetsTheAlternationSequence()
        {
            _manager.ConfigureMatch(GameMode.SinglePlayer, BoardConfig.ForSize(3), AiDifficulty.Medium);
            _manager.NotifyMatchCompleted();

            _manager.DeleteAllPlayerData();
            _manager.ConfigureMatch(GameMode.SinglePlayer, BoardConfig.ForSize(3), AiDifficulty.Medium);

            Assert.IsTrue(
                _manager.HumanOrPlayer1StartsAsX,
                "the sequence key is cleared too, so the same configuration counts as a fresh sequence");
        }

        // ---------------------------------------------------------------------------------------
        // Online/Ranked flow flags. Ranked always implies online (design-doc.md section 6).
        // ---------------------------------------------------------------------------------------

        [Test]
        public void SetPendingRankedFlow_ImpliesOnlineFlow()
        {
            _manager.SetPendingRankedFlow();

            Assert.IsTrue(_manager.IsOnlineFlow);
            Assert.IsTrue(_manager.IsRankedFlow);
        }

        [Test]
        public void SetPendingOnlineFlow_IsNotRanked()
        {
            _manager.SetPendingRankedFlow();

            _manager.SetPendingOnlineFlow();

            Assert.IsTrue(_manager.IsOnlineFlow);
            Assert.IsFalse(_manager.IsRankedFlow, "Quickmatch must not inherit a stale Ranked flag");
        }

        [Test]
        public void SetPendingMode_LeavesTheOnlineFlow()
        {
            _manager.SetPendingRankedFlow();

            _manager.SetPendingMode(GameMode.SinglePlayer);

            Assert.IsFalse(_manager.IsOnlineFlow);
            Assert.IsFalse(_manager.IsRankedFlow);
        }

        [Test]
        public void ClearOnlineFlow_ClearsBothFlags()
        {
            _manager.SetPendingRankedFlow();

            _manager.ClearOnlineFlow();

            Assert.IsFalse(_manager.IsOnlineFlow);
            Assert.IsFalse(_manager.IsRankedFlow);
        }

        [Test]
        public void LastQuickmatchBoardSize_IsConsumedOnceCleared()
        {
            _manager.SetLastQuickmatchBoardSize(6);
            Assert.AreEqual(6, _manager.LastQuickmatchBoardSize);

            _manager.ClearLastQuickmatchBoardSize();

            Assert.IsNull(_manager.LastQuickmatchBoardSize);
        }
    }
}
