using System.Globalization;
using System.Threading.Tasks;
using TTTXO.Core;
using TTTXO.Game.Bootstrap;
using TTTXO.Game.Services;
using UnityEngine.UIElements;

namespace TTTXO.Game.UI.Screens
{
    /// <summary>
    /// Matchmaking screen (Docs/06-Wireframes-UI.md screen 5): spinner + elapsed mm:ss +
    /// "Searching for a rival...", Cancel always functional (risk #3), and past the wait ceiling
    /// (Remote Config, default 45s) a "Taking too long?" card appears. **One documented deviation
    /// from the wireframe**: the wireframe's fallback offers "Play Quickmatch" (retry) + "Play vs AI";
    /// Milestone 4 only implements "Play vs AI" (navigates to Board Select 1P with the same board
    /// size pre-selected, and cancels the ticket) - a Quickmatch retry button is not part of this
    /// milestone's scope per the task brief. Design-doc.md section 6.2's Ranked fallback table also
    /// lists a "Pasar a Quickmatch" option that is likewise not implemented here for the same reason -
    /// Ranked reuses the same "Play vs AI" fallback (which never scores MMR, per design-doc.md
    /// section 6.2: "no puntúa MMR").
    ///
    /// Milestone 5 (design-doc.md section 6): shared with Ranked via <see cref="StartRankedSearch"/> -
    /// same screen/spinner/fallback-card UI, the only difference is which Matchmaker queue
    /// <see cref="MatchmakingService"/> searches and, once matched, the Ranked-specific "waiting for
    /// the 3x3 series' second player" polling contract (see <see cref="HandleMatchedAsync"/>).
    /// </summary>
    public class MatchmakingScreenController : IScreenController
    {
        private const int PollIntervalMilliseconds = 1500;

        private readonly ScreenRouter _router;
        private readonly GameManager _gameManager;
        private readonly VisualElement _root;
        private readonly Label _elapsedLabel;
        private readonly Label _statusLabel;
        private readonly VisualElement _fallbackCard;
        private readonly Label _fallbackTitleLabel;
        private readonly Button _fallbackAiButton;
        private readonly Button _cancelButton;

        private IVisualElementScheduledItem _pollTask;
        private bool _matchConfigured;
        private MatchStateDto _pendingMatch;

        public MatchmakingScreenController(VisualElement root, ScreenRouter router, GameManager gameManager)
        {
            _root = root;
            _router = router;
            _gameManager = gameManager;

            _elapsedLabel = root.Q<Label>("matchmaking-elapsed-label");
            _statusLabel = root.Q<Label>("matchmaking-status-label");
            _fallbackCard = root.Q<VisualElement>("matchmaking-fallback-card");
            _fallbackTitleLabel = root.Q<Label>("matchmaking-fallback-title-label");
            _fallbackAiButton = root.Q<Button>("matchmaking-fallback-ai-button");
            _cancelButton = root.Q<Button>("matchmaking-cancel-button");

            _fallbackAiButton.clicked += OnPlayVsAiClicked;
            _cancelButton.clicked += OnCancelClicked;

            ApplyLocalizedText();
        }

        private void ApplyLocalizedText()
        {
            _fallbackTitleLabel.text = UiText.Matchmaking.FallbackTitle;
            _fallbackAiButton.text = UiText.Matchmaking.PlayVsAiButton;
            _cancelButton.text = UiText.Common.Cancel;
            // _statusLabel/_elapsedLabel are re-set every poll tick from live search state (see Tick) -
            // refreshed there instead of here, same reasoning as GameScreenController's turn labels.
        }

        public void RefreshLocalizedText()
        {
            ApplyLocalizedText();
        }

        /// <summary>Called by BoardSelectScreenController right before navigating here (Quickmatch).</summary>
        public void StartSearch(int boardSize)
        {
            _gameManager.SetLastQuickmatchBoardSize(boardSize);
            _matchConfigured = false;
            _ = MatchmakingService.StartSearchAsync(boardSize);
        }

        /// <summary>Called by BoardSelectScreenController right before navigating here (Milestone 5 - Ranked).</summary>
        public void StartRankedSearch(int boardSize)
        {
            _gameManager.SetLastQuickmatchBoardSize(boardSize);
            _matchConfigured = false;
            _ = MatchmakingService.StartSearchAsync(boardSize, ranked: true);
        }

        public void OnShow()
        {
            _fallbackCard.style.display = DisplayStyle.None;
            _statusLabel.text = UiText.Matchmaking.SearchingLabel;
            _elapsedLabel.text = FormatElapsed(0f);
            _pendingMatch = null;

            _pollTask?.Pause();
            _pollTask = _root.schedule.Execute(Tick).Every(PollIntervalMilliseconds);
        }

        public void OnHide()
        {
            _pollTask?.Pause();
            _pollTask = null;

            // Risk #3 safety net (Docs/03-Arquitectura-UGS-TicTacToe.md "Resolucion detallada de
            // riesgos #3": "cancelacion explicita del ticket en el OnDisable/OnDestroy de la pantalla
            // de busqueda de partida"): the Cancel/Play-vs-AI buttons already cancel explicitly, and
            // this is a no-op once the search is no longer Searching/CreatingMatch (e.g. after a
            // successful match, right before navigating to the Game screen) - guards against leaving
            // this screen by some other means without an active ticket lingering.
            _ = MatchmakingService.CancelIfSearchingAsync("user_cancel");
        }

        private async void Tick()
        {
            switch (MatchmakingService.Status)
            {
                case MatchmakingService.SearchStatus.Searching:
                    await MatchmakingService.PollOnceAsync();
                    _statusLabel.text = UiText.Matchmaking.SearchingLabel;
                    break;

                case MatchmakingService.SearchStatus.CreatingMatch:
                    _statusLabel.text = UiText.Matchmaking.ConnectingLabel;
                    break;

                case MatchmakingService.SearchStatus.Matched:
                    await HandleMatchedAsync();
                    break;

                case MatchmakingService.SearchStatus.TimedOut:
                case MatchmakingService.SearchStatus.Failed:
                case MatchmakingService.SearchStatus.Cancelled:
                    // Terminal, non-matched state - keep whatever the fallback card is already
                    // showing (it appears purely based on elapsed time below) and stop polling.
                    _pollTask?.Pause();
                    break;
            }

            _elapsedLabel.text = FormatElapsed(MatchmakingService.ElapsedSeconds);

            bool pastWaitCeiling = MatchmakingService.ElapsedSeconds >= GameConfigService.MatchmakingConfig.WaitCeilingSeconds;
            bool stillWaiting = MatchmakingService.Status != MatchmakingService.SearchStatus.Matched || !_matchConfigured;
            _fallbackCard.style.display = pastWaitCeiling && stillWaiting ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private async Task HandleMatchedAsync()
        {
            if (_matchConfigured)
            {
                return;
            }

            _pendingMatch ??= MatchmakingService.ResolvedMatch;
            if (_pendingMatch == null)
            {
                return;
            }

            if (_pendingMatch.IsWaitingForOpponent)
            {
                // The other matched player hasn't completed their own CreateMatch call yet (see
                // CloudCode~/TicTacToeModule/README.md "Flujo CreateMatch") - keep polling until the
                // roster completes. Expected to resolve within a second or two in practice (both
                // clients poll their own ticket concurrently).
                //
                // Milestone 5 - Ranked 3x3 series exception (README "Serie de 2 partidas (Ranked 3x3)"
                // point 2): while the series waits for its second player, there is no MatchStateRecord
                // yet at all, so MatchId comes back empty - GetMatchState would have nothing to fetch.
                // The contract is to re-invoke CreateMatch instead (already idempotent server-side).
                // Every other waiting case (Quickmatch, Ranked 6x6) has a real MatchId while waiting,
                // same as before - checking MatchId emptiness is enough to tell the two apart without
                // this screen needing to know the mode/board size itself.
                _statusLabel.text = UiText.Matchmaking.ConnectingLabel;
                try
                {
                    _pendingMatch = string.IsNullOrEmpty(_pendingMatch.MatchId)
                        ? await MatchmakingService.RetryCreateMatchAsync()
                        : await OnlineMatchService.GetMatchStateAsync(_pendingMatch.MatchId);
                }
                catch
                {
                    // Transient - next Tick tries again with the same pending state.
                    return;
                }

                if (_pendingMatch.IsWaitingForOpponent)
                {
                    return;
                }
            }

            _matchConfigured = true;
            _pollTask?.Pause();

            _gameManager.ClearOnlineFlow();
            _gameManager.ClearLastQuickmatchBoardSize();
            _router.GameScreen.ConfigureOnline(_pendingMatch);
            _router.Show(ScreenId.Game);
        }

        private static string FormatElapsed(float seconds)
        {
            int totalSeconds = seconds < 0f ? 0 : UnityEngine.Mathf.FloorToInt(seconds);
            int minutes = totalSeconds / 60;
            int secs = totalSeconds % 60;
            return string.Format(CultureInfo.InvariantCulture, UiText.Matchmaking.ElapsedFormat, minutes, secs);
        }

        private void OnPlayVsAiClicked()
        {
            _pollTask?.Pause();

            // Milestone 5 - design-doc.md section 6.2/6.7: only meaningful for Ranked (Quickmatch has
            // no ranked_queue_fallback_shown equivalent) and only while the fallback card is actually
            // showing (a stray click right as it hides is not a real "chose the fallback" signal).
            if (MatchmakingService.IsRanked && _fallbackCard.style.display == DisplayStyle.Flex)
            {
                GameAnalytics.RankedQueueFallbackShown(MatchmakingService.BoardSize, MatchmakingService.ElapsedSeconds, "play_ai");
            }

            _ = MatchmakingService.CancelAsync("timeout");

            _gameManager.ClearOnlineFlow();
            _gameManager.SetPendingMode(GameMode.SinglePlayer);
            _router.Show(ScreenId.BoardSelect);
        }

        private void OnCancelClicked()
        {
            _pollTask?.Pause();

            if (MatchmakingService.IsRanked && _fallbackCard.style.display == DisplayStyle.Flex)
            {
                GameAnalytics.RankedQueueFallbackShown(MatchmakingService.BoardSize, MatchmakingService.ElapsedSeconds, "dismiss");
            }

            _ = MatchmakingService.CancelAsync("user_cancel");

            _gameManager.ClearOnlineFlow();
            _router.Show(ScreenId.ModeSelect);
        }
    }
}
