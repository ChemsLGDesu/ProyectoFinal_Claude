using System;
using System.Threading.Tasks;
using TTTXO.Core;
using TTTXO.Game.Bootstrap;
using TTTXO.Game.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace TTTXO.Game.UI.Screens
{
    /// <summary>
    /// Game screen (Docs/06-Wireframes-UI.md screen 6): builds the board grid dynamically from
    /// <see cref="BoardConfig.Size"/>, routes taps into <see cref="MatchController.PlayMove"/>,
    /// highlights the winning line, drives the "You · X | Rival · O" turn bar (the timer chip stays
    /// hidden - there is no turn timer in Milestone 1, see design-doc.md section 2), and - in Single
    /// Player - runs the AI's move on a background thread with a small artificial delay so it feels
    /// natural.
    ///
    /// Milestone 4 adds the online mode (<see cref="ConfigureOnline"/>, called by
    /// MatchmakingScreenController before navigating here): moves go through
    /// <see cref="OnlineMatchService.PlayMoveAsync"/>/<c>GetMatchState</c> instead of a local
    /// <see cref="MatchController"/> (the rival plays from another device), the Wire status chip
    /// becomes visible, and a push (or, if Wire is <c>Offline</c>, a ~10s backup poll) refreshes the
    /// board. Still no reaction chips (screen 6, "Fila inferior (solo online)") - that is a
    /// store/social milestone, not this one.
    /// </summary>
    public class GameScreenController : IScreenController
    {
        private const int AiMoveDelayMilliseconds = 400;
        private const int ResultTransitionDelayMilliseconds = 800;
        private const int OnlineBackupPollIntervalMilliseconds = 10000;

        private readonly ScreenRouter _router;
        private readonly GameManager _gameManager;
        private readonly VisualElement _root;
        private readonly VisualElement _boardContainer;

        /// <summary>Last cell size written to the cells, so <see cref="ApplyCellSize"/> can skip a no-op
        /// pass - it runs from a geometry event that its own writes would otherwise retrigger.</summary>
        private float _appliedCellSize;

        /// <summary>The element the rows sit on, rebuilt with every board. Held because its padding - the
        /// inset the board skin adds - comes out of the space the grid has to fill.</summary>
        private VisualElement _boardSurface;
        private readonly VisualElement _pauseOverlay;
        private readonly Label _pauseTitleLabel;
        private readonly Button _resumeButton;
        private readonly Button _quitButton;

        private readonly VisualElement _turnSideHome;
        private readonly VisualElement _turnSideHomeDot;
        private readonly EquippedAvatarView _avatarView;
        private readonly VisualElement _rivalAvatar;
        private readonly Label _turnSideHomeLabel;
        private readonly VisualElement _turnSideAway;
        private readonly VisualElement _turnSideAwayDot;
        private readonly Label _turnSideAwayLabel;

        private readonly VisualElement _wireChip;
        private readonly VisualElement _wireChipDot;
        private readonly Label _wireChipLabel;

        private Button[,] _cellButtons;
        private MatchController _match;
        private AiPlayer _aiPlayer;
        private AiParams _aiParams;

        private bool _isSinglePlayer;
        private CellOwner _humanSymbol;
        private CellOwner _aiSymbol;
        private CellOwner _player1Symbol;
        private CellOwner _player2Symbol;

        private bool _inputLocked;
        private bool _isPaused;
        private bool _matchEnded;
        private int _matchGeneration;
        private float _matchStartTime;

        // --- Milestone 4 online state (see ConfigureOnline) ---
        private bool _isOnline;
        private MatchStateDto _onlineState;
        private string _onlineMySymbol;
        private IVisualElementScheduledItem _onlineBackupPollTask;

        public GameScreenController(VisualElement root, ScreenRouter router, GameManager gameManager)
        {
            _router = router;
            _gameManager = gameManager;
            _root = root;

            _boardContainer = root.Q<VisualElement>("board-container");

            // The play area's size is not known when the board is built - layout has not run yet - and it
            // changes afterwards anyway on rotation or a window resize. Sizing the cells from this event
            // covers both, and it is the only place that knows how much room the board actually has.
            _boardContainer.RegisterCallback<GeometryChangedEvent>(_ => ApplyCellSize());
            _pauseOverlay = root.Q<VisualElement>("pause-overlay");

            _turnSideHome = root.Q<VisualElement>("turn-side-home");
            // The local player's equipped portrait, shown on their side of the turn bar.
            _avatarView = new EquippedAvatarView(
                root.Q<VisualElement>("game-avatar"),
                root.Q<VisualElement>("game-avatar-frame"));

            _rivalAvatar = root.Q<VisualElement>("game-rival-avatar");

            _turnSideHomeDot = root.Q<VisualElement>("turn-side-home-dot");
            _turnSideHomeLabel = root.Q<Label>("turn-side-home-label");
            _turnSideAway = root.Q<VisualElement>("turn-side-away");
            _turnSideAwayDot = root.Q<VisualElement>("turn-side-away-dot");
            _turnSideAwayLabel = root.Q<Label>("turn-side-away-label");

            _wireChip = root.Q<VisualElement>("game-wire-chip");
            _wireChipDot = root.Q<VisualElement>("game-wire-chip-dot");
            _wireChipLabel = root.Q<Label>("game-wire-chip-label");

            var pauseButton = root.Q<Button>("game-pause-button");
            pauseButton.text = UiText.Game.PauseButtonIcon;
            pauseButton.clicked += OnPauseButtonClicked;

            _pauseTitleLabel = root.Q<Label>("pause-title-label");

            _resumeButton = root.Q<Button>("pause-resume-button");
            _resumeButton.clicked += OnResumeButtonClicked;

            _quitButton = root.Q<Button>("pause-quit-button");
            _quitButton.clicked += OnQuitButtonClicked;

            ApplyLocalizedText();
        }

        /// <summary>Called by MatchmakingScreenController right before navigating here with a freshly-completed online match (Milestone 4).</summary>
        public void ConfigureOnline(MatchStateDto initialState)
        {
            _isOnline = true;
            _onlineState = initialState;
        }

        private void ApplyLocalizedText()
        {
            _pauseTitleLabel.text = UiText.Game.PauseTitle;
            _resumeButton.text = UiText.Common.Resume;
            _quitButton.text = UiText.Common.Abandon;

            // The turn-side labels (You/Rival/Player 1/Player 2/"AI is thinking...") are re-set every
            // OnShow/UpdateTurnIndicator from live match state - refreshing them here too, mid-match,
            // would require re-deriving that state and risks racing the AI turn coroutine, so they are
            // intentionally left alone; they self-correct the moment the turn indicator next updates.
        }

        /// <summary>
        /// Only reassigns the static pause-menu copy above - must never touch match/board state (see
        /// <see cref="IScreenController.RefreshLocalizedText"/>), so a language change mid-match is safe.
        /// </summary>
        public void RefreshLocalizedText()
        {
            ApplyLocalizedText();
        }

        public void OnShow()
        {
            _matchEnded = false;
            _inputLocked = false;
            _isPaused = false;
            _pauseOverlay.style.display = DisplayStyle.None;
            _matchStartTime = Time.realtimeSinceStartup;

            if (_isOnline)
            {
                OnShowOnline();
                return;
            }

            _wireChip.style.display = DisplayStyle.None;

            var config = _gameManager.SelectedBoard;
            _isSinglePlayer = _gameManager.Mode == GameMode.SinglePlayer;
            _match = new MatchController(config, CellOwner.X);

            if (_isSinglePlayer)
            {
                _humanSymbol = _gameManager.HumanOrPlayer1StartsAsX ? CellOwner.X : CellOwner.O;
                _aiSymbol = Opposite(_humanSymbol);
                _aiParams = _gameManager.SelectedDifficulty == AiDifficulty.Adaptive
                    ? _gameManager.GetAdaptiveController(config.Size).GetParams(config.Size)
                    : AiParams.ForDifficulty(_gameManager.SelectedDifficulty, config.Size);
                _aiPlayer = new AiPlayer(System.Environment.TickCount);

                SetTurnSideIdentity(_turnSideHomeDot, _humanSymbol);
                _turnSideHomeLabel.text = UiText.Game.YouLabel;
                SetTurnSideIdentity(_turnSideAwayDot, _aiSymbol);
                _turnSideAwayLabel.text = UiText.Game.RivalLabel;
            }
            else
            {
                _player1Symbol = _gameManager.HumanOrPlayer1StartsAsX ? CellOwner.X : CellOwner.O;
                _player2Symbol = Opposite(_player1Symbol);

                SetTurnSideIdentity(_turnSideHomeDot, _player1Symbol);
                _turnSideHomeLabel.text = UiText.Game.Player1Label;
                SetTurnSideIdentity(_turnSideAwayDot, _player2Symbol);
                _turnSideAwayLabel.text = UiText.Game.Player2Label;
            }

            ApplyRivalAvatar();
            BuildBoard(config.Size);
            UpdateTurnIndicator();
            TryTriggerAiTurn();

            AiDifficulty? matchStartedDifficulty = _isSinglePlayer ? _gameManager.SelectedDifficulty : (AiDifficulty?)null;
            GameAnalytics.MatchStarted(_gameManager.Mode, config.Size, matchStartedDifficulty);
        }

        public void OnHide()
        {
            _matchGeneration++;

            if (_isOnline)
            {
                OnHideOnline();
            }
        }

        // ---------------------------------------------------------------------------------------
        // Milestone 4: online match (rival plays from another device). Board/turn UI is shared with
        // 1P/local (BuildBoard, SetTurnSideIdentity, HighlightWinningLine) - only how the board is
        // filled and how a move is applied differ (server round-trip instead of a local
        // MatchController).
        // ---------------------------------------------------------------------------------------

        private void OnShowOnline()
        {
            _onlineMySymbol = null;
            foreach (var player in _onlineState.Players)
            {
                if (player.PlayerId == OnlineMatchService.LocalPlayerId)
                {
                    _onlineMySymbol = player.Symbol;
                }
            }

            var myOwner = _onlineMySymbol == "O" ? CellOwner.O : CellOwner.X;
            var rivalOwner = Opposite(myOwner);
            SetTurnSideIdentity(_turnSideHomeDot, myOwner);
            _turnSideHomeLabel.text = UiText.Game.YouLabel;
            SetTurnSideIdentity(_turnSideAwayDot, rivalOwner);
            _turnSideAwayLabel.text = UiText.Game.RivalLabel;

            ApplyRivalAvatar();
            BuildBoard(_onlineState.BoardSize);
            ApplyOnlineState(_onlineState);

            _wireChip.style.display = DisplayStyle.Flex;
            RefreshWireChip();
            OnlineMatchService.MatchUpdatedPushReceived += OnMatchUpdatedPush;
            _ = SubscribeToWireAsync();

            _onlineBackupPollTask?.Pause();
            _onlineBackupPollTask = _root.schedule.Execute(OnlineBackupPollTick).Every(OnlineBackupPollIntervalMilliseconds);

            // state.Mode is server truth ("online_quickmatch" | "ranked", design-doc.md section 6.7) -
            // using it here (rather than a hardcoded OnlineQuickmatchModeCode, the pre-Ranked bug this
            // replaces) is what makes match_started correctly tag Ranked matches, including game 2 of
            // a 3x3 series re-entering this same method (see TransitionToNextSeriesGameAsync).
            GameAnalytics.MatchStarted(_onlineState.Mode, _onlineState.BoardSize);
        }

        private void OnHideOnline()
        {
            OnlineMatchService.MatchUpdatedPushReceived -= OnMatchUpdatedPush;
            OnlineMatchService.ResetSubscriptionState();
            _onlineBackupPollTask?.Pause();
            _onlineBackupPollTask = null;
            _isOnline = false;
            _onlineState = null;
        }

        private async Task SubscribeToWireAsync()
        {
            await OnlineMatchService.SubscribeAsync();
            RefreshWireChip();
        }

        private void RefreshWireChip()
        {
            var status = OnlineMatchService.ConnectionStatus;
            _wireChipLabel.text = status switch
            {
                OnlineMatchService.WireConnectionStatus.Subscribed => UiText.Game.WireSubscribed,
                OnlineMatchService.WireConnectionStatus.Connecting => UiText.Game.WireConnecting,
                _ => UiText.Game.WireOffline,
            };

            _wireChipDot.EnableInClassList("wire-chip-dot--subscribed", status == OnlineMatchService.WireConnectionStatus.Subscribed);
            _wireChipDot.EnableInClassList("wire-chip-dot--connecting", status == OnlineMatchService.WireConnectionStatus.Connecting);
        }

        /// <summary>Backup poll (~10s), only while Wire is not Subscribed (Docs/06-Wireframes-UI.md screen 6: "refresh por push + polling de respaldo cada ~10s si Wire esta Offline").</summary>
        private async void OnlineBackupPollTick()
        {
            RefreshWireChip();

            if (_matchEnded || _onlineState == null || OnlineMatchService.ConnectionStatus == OnlineMatchService.WireConnectionStatus.Subscribed)
            {
                return;
            }

            try
            {
                var latest = await OnlineMatchService.GetMatchStateAsync(_onlineState.MatchId);
                ApplyOnlineState(latest);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"GameScreenController: online backup poll failed - {ex.Message}");
            }
        }

        /// <summary>Wire push handler (Docs/03-Arquitectura-UGS-TicTacToe.md#Wire): never trusts the push payload, always re-fetches GetMatchState.</summary>
        private async void OnMatchUpdatedPush()
        {
            if (_matchEnded || _onlineState == null)
            {
                return;
            }

            try
            {
                var latest = await OnlineMatchService.GetMatchStateAsync(_onlineState.MatchId);
                ApplyOnlineState(latest);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"GameScreenController: GetMatchState after Wire push failed - {ex.Message}");
            }
        }

        private async void OnCellClickedOnline(int row, int col)
        {
            if (_onlineState.CurrentPlayer != _onlineMySymbol)
            {
                return;
            }

            int size = _onlineState.BoardSize;
            if (!string.IsNullOrEmpty(_onlineState.Board[(row * size) + col]))
            {
                return;
            }

            _inputLocked = true;
            try
            {
                var updated = await OnlineMatchService.PlayMoveAsync(_onlineState.MatchId, row, col);
                ApplyOnlineState(updated);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"GameScreenController: online PlayMove({row},{col}) failed - {ex.Message}");
                _router.Toast.Show(UiText.Game.WireOffline);
            }
            finally
            {
                if (!_matchEnded)
                {
                    _inputLocked = false;
                }
            }
        }

        private void ApplyOnlineState(MatchStateDto state)
        {
            _onlineState = state;
            RefreshBoardFromOnlineState();

            bool homeIsCurrent = state.CurrentPlayer == _onlineMySymbol;
            _turnSideHome.EnableInClassList("turn-side--active", homeIsCurrent);
            _turnSideAway.EnableInClassList("turn-side--active", !homeIsCurrent);

            if (state.Status != "in_progress" && !_matchEnded)
            {
                HandleOnlineMatchEnd(state);
            }
        }

        private void RefreshBoardFromOnlineState()
        {
            int size = _onlineState.BoardSize;
            for (int row = 0; row < size; row++)
            {
                for (int col = 0; col < size; col++)
                {
                    string symbol = _onlineState.Board[(row * size) + col];
                    var cell = _cellButtons[row, col];

                    // This method re-applies the whole board on every poll, so "newly filled" is the
                    // transition from empty to occupied - not simply "is occupied", which would replay the
                    // burst on every cell several times a second. Read from the classes rather than from
                    // cell.text, which no longer holds the symbol now that pieces render as skin art.
                    bool wasEmpty = !cell.ClassListContains("board-cell--x") && !cell.ClassListContains("board-cell--o");
                    bool justFilled = wasEmpty && !string.IsNullOrEmpty(symbol);
                    cell.EnableInClassList("board-cell--x", symbol == "X");
                    cell.EnableInClassList("board-cell--o", symbol == "O");
                    cell.SetEnabled(string.IsNullOrEmpty(symbol));

                    if (justFilled)
                    {
                        BoardEffects.PlayPlaceBurst(cell);
                    }
                }
            }

            if (_onlineState.WinningLine != null)
            {
                foreach (var cell in _onlineState.WinningLine)
                {
                    if (cell.Length == 2)
                    {
                        var winCell = _cellButtons[cell[0], cell[1]];
                        if (!winCell.ClassListContains("board-cell--win"))
                        {
                            // This runs on every poll of a finished match, so guard it - without the check
                            // the glow overlay would be stacked again each time the state is re-applied.
                            winCell.AddToClassList("board-cell--win");
                            BoardEffects.AddWinGlow(winCell);
                        }
                    }
                }
            }
        }

        private void HandleOnlineMatchEnd(MatchStateDto state)
        {
            _matchEnded = true;
            _onlineBackupPollTask?.Pause();

            bool won = state.Status == (_onlineMySymbol == "X" ? "x_won" : "o_won");
            bool draw = state.Status == "draw";
            bool firstPlayerWon = state.Status == "x_won";

            // state.Mode is server truth ("online_quickmatch" | "ranked") - each game of a 3x3 Ranked
            // series still gets its own match_finished (design-doc.md section 6.7), including game 1,
            // which is NOT the end of the series (see the RankedSeriesId branch below).
            float durationSeconds = Time.realtimeSinceStartup - _matchStartTime;
            GameAnalytics.MatchFinished(won, state.BoardSize, state.TurnCount, durationSeconds, state.Status, firstPlayerWon, state.Mode);

            if (state.Mode == GameAnalytics.RankedModeCode && state.EndedByAbandonment)
            {
                // Design-doc.md section 6.4: abandonment can only be observed from the side still
                // present to poll/move - "abandoner" never fires from here, only "stayer" (a real win)
                // or "both" (the proactive sweep's no-contest draw) - see RankedMatchAbandoned's own
                // remarks for why "abandoner" is unreachable client-side.
                string role = draw ? "both" : "stayer";
                GameAnalytics.RankedMatchAbandoned(state.BoardSize, role, state.TurnCount, state.RankedSeriesGameIndex, durationSeconds);
            }

            // Milestone 5 - 3x3 Ranked series (design-doc.md section 6.1, CloudCode~/TicTacToeModule/
            // README.md "Serie de 2 partidas"): game 1 of a series ending a REAL game (win/draw) is not
            // the series ending - RankedNextMatchId is populated and the client must navigate straight
            // to game 2 instead of Result. An abandonment resolution ends the series immediately
            // instead (RankedSeriesComplete true, RankedNextMatchId null), so it falls through to the
            // normal terminal path below.
            if (state.Mode == GameAnalytics.RankedModeCode && !string.IsNullOrEmpty(state.RankedNextMatchId) && !state.RankedSeriesComplete)
            {
                _ = TransitionToNextSeriesGameAsync(state.RankedNextMatchId);
                return;
            }

            var localPlayerOwner = _onlineMySymbol == "O" ? CellOwner.O : CellOwner.X;
            var finalStatus = draw ? MatchStatus.Draw : (state.Status == "x_won" ? MatchStatus.XWon : MatchStatus.OWon);

            // Online reward: server-authoritative (design-doc.md section 4, "Modo online
            // Quickmatch" - Cloud Code's MatchFunctions.PlayMove/GetMatchState already computed and
            // credited it to Cloud Save `currency` before this DTO was returned). The client never
            // calculates or writes online currency (Docs/01-Directrices-Proyecto.md#Seguridad /
            // anti-cheat) - it only displays the amount the server already granted. For a Ranked 3x3
            // series this is 0 on game 1 and only non-zero here on game 2, once the series (the paid
            // unit - design-doc.md section 6.3) actually settles.
            int actualReward = state.AwardedSoftCurrency;

            // Display-only convenience: bumps the in-session wallet shown on Home/header
            // immediately, without touching PlayerPrefs or the offline daily-cap counters (those
            // stay 1P/local-only, see GameManager.AwardMatchReward) - the next
            // PlayerDataService.LoadIntoSessionAsync pull remains the actual source of truth for
            // the persisted balance.
            _gameManager.ApplyOnlineRewardCredit(actualReward);

            if (actualReward > 0)
            {
                string rewardSource = state.Mode == GameAnalytics.RankedModeCode ? "ranked_match" : "online_quickmatch";
                GameAnalytics.SoftCurrencyEarned(actualReward, rewardSource);
            }

            if (state.RankedMmrResult != null)
            {
                var mmr = state.RankedMmrResult;
                GameAnalytics.RankedMmrChanged(mmr.BoardSize, mmr.MmrBefore, mmr.MmrAfter, mmr.Delta, mmr.OpponentMmr, mmr.KFactor, mmr.Result, "match", mmr.Season);

                // Best-effort local mirror of the server's own value (see RankedProfileCache remarks) -
                // never a client computation, just remembering what the server told us so a future
                // Ranked matchmaking ticket for this board size seeds a realistic mmr attribute.
                RankedProfileCache.SetKnownMmr(mmr.BoardSize, mmr.MmrAfter);
            }

            _ = TransitionToOnlineResultAsync(finalStatus, actualReward, localPlayerOwner, state.EndedByAbandonment, state.RankedMmrResult);
        }

        /// <summary>
        /// Milestone 5 - 3x3 Ranked series (design-doc.md section 6.1): game 1 just ended with a real
        /// result but the series isn't over - re-enters this same online game flow for game 2 (symbols
        /// swapped server-side, same two players, no new Matchmaker search) instead of navigating to
        /// Result. Mirrors what <see cref="OnShowOnline"/> already does for a freshly-matched game, so
        /// the board/turn-bar/Wire subscription are rebuilt exactly the same way.
        /// </summary>
        private async Task TransitionToNextSeriesGameAsync(string nextMatchId)
        {
            int generation = _matchGeneration;

            OnlineMatchService.MatchUpdatedPushReceived -= OnMatchUpdatedPush;
            OnlineMatchService.ResetSubscriptionState();

            // Brief pause so the player sees game 1's final board before game 2 replaces it (same
            // delay already used for every other match-end transition on this screen).
            await Task.Delay(ResultTransitionDelayMilliseconds);

            if (generation != _matchGeneration)
            {
                return;
            }

            MatchStateDto nextState;
            try
            {
                nextState = await OnlineMatchService.GetMatchStateAsync(nextMatchId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"GameScreenController: failed to fetch Ranked series game 2 ({nextMatchId}) - {ex.Message}");
                _router.Toast.Show(UiText.Game.WireOffline);
                _router.Show(ScreenId.Home);
                return;
            }

            if (generation != _matchGeneration)
            {
                return;
            }

            _matchEnded = false;
            _inputLocked = false;
            _isPaused = false;
            _pauseOverlay.style.display = DisplayStyle.None;
            _matchStartTime = Time.realtimeSinceStartup;

            ConfigureOnline(nextState);
            OnShowOnline();
        }

        private async Task TransitionToOnlineResultAsync(MatchStatus status, int actualReward, CellOwner localPlayerOwner, bool endedByAbandonment, RankedMmrResultDto rankedMmrResult)
        {
            int generation = _matchGeneration;
            await Task.Delay(ResultTransitionDelayMilliseconds);

            if (generation != _matchGeneration)
            {
                return;
            }

            _router.ResultScreen.ConfigureOnline(status, actualReward, localPlayerOwner, endedByAbandonment, rankedMmrResult);
            _router.Show(ScreenId.Result);
        }

        private void BuildBoard(int size)
        {
            _boardContainer.Clear();
            _cellButtons = new Button[size, size];

            // The skin goes on a surface that hugs the rows, NOT on _boardContainer: that one is
            // flex-grow: 1 and fills the whole play area, so a board skin painted on it stretched far
            // above and below the grid instead of sitting under it.
            var surface = new VisualElement();
            surface.AddToClassList("board-surface");
            ApplyEquippedSkins(surface);
            _boardContainer.Add(surface);
            _boardSurface = surface;

            for (int row = 0; row < size; row++)
            {
                var rowElement = new VisualElement();
                rowElement.AddToClassList("board-row");

                for (int col = 0; col < size; col++)
                {
                    int capturedRow = row;
                    int capturedCol = col;

                    var cell = new Button { text = string.Empty };
                    cell.AddToClassList("board-cell");
                    cell.clicked += () => OnCellClicked(capturedRow, capturedCol);

                    rowElement.Add(cell);
                    _cellButtons[row, col] = cell;
                }

                surface.Add(rowElement);
            }

            ApplyCellSize();
        }

        /// <summary>
        /// Sizes the cells to the room the play area actually has. Called on build and again from the
        /// container's <see cref="GeometryChangedEvent"/>, so the board follows a resize or a rotation.
        /// </summary>
        private void ApplyCellSize()
        {
            if (_cellButtons == null)
            {
                return;
            }

            int size = _cellButtons.GetLength(0);

            // resolvedStyle width/height are the border box, so the container's own padding is inside
            // them and has to come off before the board can be fitted into what is left.
            var c = _boardContainer.resolvedStyle;
            float availableWidth = c.width - c.paddingLeft - c.paddingRight;
            float availableHeight = c.height - c.paddingTop - c.paddingBottom;

            float cellSize = ComputeCellSizePixels(
                size, availableWidth, availableHeight,
                MeasureCellMargins(_cellButtons[0, 0]), MeasureSurfaceInset(_boardSurface));

            // The event fires again for every cell we resize, so without this the callback re-enters
            // once per cell on every layout pass.
            if (Mathf.Approximately(cellSize, _appliedCellSize))
            {
                return;
            }

            _appliedCellSize = cellSize;
            float fontSize = Mathf.Clamp(cellSize * 0.45f, 12f, 32f);

            foreach (var cell in _cellButtons)
            {
                if (cell == null) continue;
                cell.style.width = cellSize;
                cell.style.height = cellSize;
                cell.style.fontSize = fontSize;
            }
        }

        /// <summary>
        /// Puts the equipped piece and board skin classes on the board container, which is what the
        /// descendant rules in main.uss key off. Called from <see cref="BuildBoard"/> so a skin changed
        /// between matches takes effect on the next board without the screen having to listen for it -
        /// a skin cannot change mid-match, since the equip UI lives on Profile.
        /// </summary>
        /// <summary>
        /// Picks the rival's portrait for the match about to start: the robot for the AI, a neutral
        /// silhouette for a human.
        ///
        /// A human rival deliberately gets a placeholder rather than a real face. In local two-player the
        /// second player has no profile of their own, and online the server does not send the opponent's
        /// equipped cosmetics - showing anything else would be inventing something the game does not know.
        /// If the server ever starts sending them, this is the single place that changes.
        /// </summary>
        private void ApplyRivalAvatar()
        {
            if (_rivalAvatar == null)
            {
                return;
            }

            bool rivalIsAi = _isSinglePlayer && !_isOnline;
            _rivalAvatar.EnableInClassList("turn-avatar--ai", rivalIsAi);
            _rivalAvatar.EnableInClassList("turn-avatar--human", !rivalIsAi);
        }

        private static void ApplyEquippedSkins(VisualElement surface)
        {
            surface.AddToClassList(
                CosmeticCatalog.StyleClassFor(CosmeticCatalog.PieceSkins, CosmeticSelection.PieceSkinId));
            surface.AddToClassList(
                CosmeticCatalog.StyleClassFor(CosmeticCatalog.BoardSkins, CosmeticSelection.BoardSkinId));
        }

        /// <summary>Cell margins assumed before anything has been laid out. Replaced by the measured value
        /// on the next pass, so it only has to keep the first frame from being wildly wrong.</summary>
        private const float FallbackCellMarginPixels = 4f;

        /// <summary>
        /// How much wider than its assigned <c>width</c> a cell actually sits: its margins, and nothing
        /// else. UI Toolkit sizes on the border box, so the cell's own border and padding are already
        /// inside the number we set - subtracting them, as an earlier version did, shrinks the board for
        /// no reason.
        ///
        /// Measured rather than hardcoded because guessing it was wrong three times running: first the
        /// margin alone, then margin plus border, then margin plus border plus padding. Each guess was off
        /// by exactly whatever it had misjudged, and the board either overflowed its container or sat
        /// smaller than it needed to.
        /// </summary>
        private static float MeasureCellMargins(VisualElement cell)
        {
            if (cell == null) return FallbackCellMarginPixels;
            float margins = cell.resolvedStyle.marginLeft + cell.resolvedStyle.marginRight;
            return float.IsNaN(margins) || margins < 0f ? FallbackCellMarginPixels : margins;
        }

        /// <summary>
        /// Horizontal padding the surface holds around the grid. The board-skin rules add <c>padding: 6px</c>
        /// so the skin frames the cells instead of ending flush against them, and that inset comes out of
        /// the space the grid has.
        /// </summary>
        private static float MeasureSurfaceInset(VisualElement surface)
        {
            if (surface == null) return 0f;
            float inset = surface.resolvedStyle.paddingLeft + surface.resolvedStyle.paddingRight;
            return float.IsNaN(inset) || inset < 0f ? 0f : inset;
        }

        /// <summary>Floor for a cell, so an 11x11 in a small window stays tappable rather than collapsing.</summary>
        private const float MinCellSizePixels = 24f;

        /// <summary>Board span assumed for the very first paint, before layout has measured anything.
        /// Only ever visible for one frame - the geometry callback corrects it - so it just has to be
        /// closer to right than zero would be.</summary>
        private const float UnmeasuredBoardSpanPixels = 520f;

        /// <summary>
        /// Cell size for a board of <paramref name="boardSize"/> in a play area of the given size.
        ///
        /// This used to return four fixed numbers (96/60/44/34px) regardless of the screen, which on a
        /// 720x1400 phone left a 3x3 board 300px wide floating in an 1129px-tall play area - small board,
        /// large holes above and below it. The board is square, so it is the shorter of the two dimensions
        /// that decides.
        ///
        /// There is no width ceiling here on purpose: it lives on <c>.board-container</c> in main.uss, so
        /// the widths of this screen are all in one place and this method just fills whatever it is given.
        /// </summary>
        private static float ComputeCellSizePixels(
            int boardSize, float availableWidth, float availableHeight, float cellMargins, float surfaceInset)
        {
            if (boardSize <= 0) return MinCellSizePixels;

            bool measured = availableWidth > 1f && availableHeight > 1f
                && !float.IsNaN(availableWidth) && !float.IsNaN(availableHeight);

            float span = measured
                ? Mathf.Min(availableWidth, availableHeight)
                : UnmeasuredBoardSpanPixels;

            return Mathf.Max((span - surfaceInset) / boardSize - cellMargins, MinCellSizePixels);
        }

        private void OnCellClicked(int row, int col)
        {
            if (_matchEnded || _inputLocked || _isPaused)
            {
                return;
            }

            if (_isOnline)
            {
                OnCellClickedOnline(row, col);
                return;
            }

            if (_isSinglePlayer && _match.CurrentPlayer != _humanSymbol)
            {
                return;
            }

            ApplyMove(row, col);
        }

        private void ApplyMove(int row, int col)
        {
            var result = _match.PlayMove(row, col);
            if (!result.IsValid)
            {
                Debug.LogWarning($"GameScreenController: rejected move ({row},{col}) - {result.InvalidReason}");
                return;
            }

            RefreshCell(row, col);
            _cellButtons[row, col].SetEnabled(false);
            BoardEffects.PlayPlaceBurst(_cellButtons[row, col]);

            if (result.StatusAfterMove != MatchStatus.InProgress)
            {
                HandleMatchEnd(result);
                return;
            }

            UpdateTurnIndicator();
            TryTriggerAiTurn();
        }

        private void RefreshCell(int row, int col)
        {
            var owner = _match.Board.GetCell(row, col);
            var cell = _cellButtons[row, col];

            // No glyph: a piece is its skin art now. UI Toolkit draws a Button's label ON TOP of its
            // background-image, not behind it, so leaving the letter in place stamped a cyan X over the
            // fire X and a violet O over the ice ring.
            cell.EnableInClassList("board-cell--x", owner == CellOwner.X);
            cell.EnableInClassList("board-cell--o", owner == CellOwner.O);
        }

        private void TryTriggerAiTurn()
        {
            if (_isSinglePlayer && _match.CurrentPlayer == _aiSymbol)
            {
                _ = RunAiTurnAsync();
            }
        }

        private async Task RunAiTurnAsync()
        {
            _inputLocked = true;
            int generation = _matchGeneration;

            var matchRef = _match;
            var paramsRef = _aiParams;
            var aiPlayerRef = _aiPlayer;

            var moveTask = Task.Run(() => aiPlayerRef.ChooseMove(matchRef, paramsRef));
            await Task.WhenAll(moveTask, Task.Delay(AiMoveDelayMilliseconds));

            if (generation != _matchGeneration)
            {
                return;
            }

            _inputLocked = false;

            var move = moveTask.Result;
            ApplyMove(move.Row, move.Col);
        }

        private void HandleMatchEnd(MoveResult result)
        {
            _matchEnded = true;
            HighlightWinningLine(result.WinningLine);

            int rawReward = ComputeRawReward(result.StatusAfterMove);
            int actualReward = _gameManager.AwardMatchReward(rawReward);

            if (_isSinglePlayer && _gameManager.SelectedDifficulty == AiDifficulty.Adaptive)
            {
                bool humanWon = result.StatusAfterMove == WinStatusFor(_humanSymbol);
                bool draw = result.StatusAfterMove == MatchStatus.Draw;
                _gameManager.GetAdaptiveController(_match.Config.Size).RecordResult(humanWon, draw);
            }

            RecordMatchHistory(result.StatusAfterMove);
            _gameManager.NotifyMatchCompleted();
            RecordMatchFinishedAnalytics(result);

            // Best-effort Cloud Save mirror (Docs/03-Arquitectura-UGS-TicTacToe.md#Cloud Save); local
            // PlayerPrefs (already updated above) remains the source of truth regardless of outcome.
            _ = PlayerDataService.SaveAfterMatchAsync(_gameManager);

            _ = TransitionToResultAsync(result.StatusAfterMove, actualReward);
        }

        private void RecordMatchFinishedAnalytics(MoveResult result)
        {
            var perspectiveSymbol = _isSinglePlayer ? _humanSymbol : _player1Symbol;
            bool won = result.StatusAfterMove == WinStatusFor(perspectiveSymbol);
            bool firstPlayerWon = result.StatusAfterMove == MatchStatus.XWon;
            float durationSeconds = Time.realtimeSinceStartup - _matchStartTime;
            AiDifficulty? difficulty = _isSinglePlayer ? _gameManager.SelectedDifficulty : (AiDifficulty?)null;

            GameAnalytics.MatchFinished(
                won, _match.Config.Size, _match.TurnCount, durationSeconds, result.StatusAfterMove, firstPlayerWon, difficulty);
        }

        private int ComputeRawReward(MatchStatus status)
        {
            if (_isSinglePlayer)
            {
                return RewardCalculator.SoftCurrencyFor(GameMode.SinglePlayer, _gameManager.SelectedDifficulty, _match.Config.Size, status, _humanSymbol);
            }

            int rewardForPlayer1 = RewardCalculator.SoftCurrencyFor(GameMode.LocalMultiplayer, null, _match.Config.Size, status, CellOwner.X);
            int rewardForPlayer2 = RewardCalculator.SoftCurrencyFor(GameMode.LocalMultiplayer, null, _match.Config.Size, status, CellOwner.O);
            return rewardForPlayer1 + rewardForPlayer2;
        }

        /// <summary>
        /// Appends the just-finished match to the local Match History (Docs/06-Wireframes-UI.md
        /// screen 9). The recording perspective is the human player in Single Player and Player 1 in
        /// Local Multiplayer (see <see cref="MatchOutcome"/>).
        /// </summary>
        private void RecordMatchHistory(MatchStatus status)
        {
            MatchOutcome outcome;
            if (status == MatchStatus.Draw)
            {
                outcome = MatchOutcome.Draw;
            }
            else
            {
                var perspectiveSymbol = _isSinglePlayer ? _humanSymbol : _player1Symbol;
                outcome = status == WinStatusFor(perspectiveSymbol) ? MatchOutcome.Win : MatchOutcome.Loss;
            }

            float durationSeconds = Time.realtimeSinceStartup - _matchStartTime;
            AiDifficulty? difficulty = _isSinglePlayer ? _gameManager.SelectedDifficulty : (AiDifficulty?)null;

            _gameManager.RecordMatchHistory(_gameManager.Mode, _match.Config.Size, difficulty, outcome, durationSeconds);
        }

        private void HighlightWinningLine(System.Collections.Generic.IReadOnlyList<(int Row, int Col)> winningLine)
        {
            if (winningLine == null)
            {
                return;
            }

            foreach (var (row, col) in winningLine)
            {
                _cellButtons[row, col].AddToClassList("board-cell--win");
                BoardEffects.AddWinGlow(_cellButtons[row, col]);
            }
        }

        private async Task TransitionToResultAsync(MatchStatus status, int actualReward)
        {
            int generation = _matchGeneration;
            await Task.Delay(ResultTransitionDelayMilliseconds);

            if (generation != _matchGeneration)
            {
                return;
            }

            _router.ResultScreen.Configure(status, actualReward, _isSinglePlayer, _humanSymbol, _player1Symbol);
            _router.Show(ScreenId.Result);
        }

        private void UpdateTurnIndicator()
        {
            bool homeIsCurrent = _isSinglePlayer
                ? _match.CurrentPlayer == _humanSymbol
                : _match.CurrentPlayer == _player1Symbol;

            _turnSideHome.EnableInClassList("turn-side--active", homeIsCurrent);
            _turnSideAway.EnableInClassList("turn-side--active", !homeIsCurrent);

            if (_isSinglePlayer)
            {
                _turnSideAwayLabel.text = homeIsCurrent ? UiText.Game.RivalLabel : UiText.Game.AiThinking;
            }
        }

        private static void SetTurnSideIdentity(VisualElement dot, CellOwner symbol)
        {
            dot.EnableInClassList("turn-dot--x", symbol == CellOwner.X);
            dot.EnableInClassList("turn-dot--o", symbol == CellOwner.O);
        }

        private void OnPauseButtonClicked()
        {
            if (_matchEnded)
            {
                return;
            }

            _isPaused = true;
            _pauseOverlay.style.display = DisplayStyle.Flex;
        }

        private void OnResumeButtonClicked()
        {
            _isPaused = false;
            _pauseOverlay.style.display = DisplayStyle.None;
        }

        private void OnQuitButtonClicked()
        {
            if (_isOnline)
            {
                // Online abandonment needs an explicit confirm (Docs/06-Wireframes-UI.md screen 6:
                // "Abandono desde pausa -> confirma -> el rival gana por abandono al detectarlo el
                // server") - there is no "concede" RPC in this milestone, the client just leaves and
                // GetMatchState's reactive check (risk #4) resolves the win for the rival the next
                // time either of them polls/opens the match.
                _router.ConfirmDialog.Show(
                    UiText.Game.AbandonConfirmTitle,
                    UiText.Game.AbandonConfirmMessage,
                    UiText.Common.Abandon,
                    OnConfirmOnlineAbandon);
                return;
            }

            // Abandoning a match before it finishes pays 0 soft currency, is not recorded in Match
            // History and does not advance the first-player alternation sequence (see
            // Docs/design-doc.md sections 1 and 4).
            _isPaused = false;
            _pauseOverlay.style.display = DisplayStyle.None;
            _router.Show(ScreenId.Home);
        }

        private void OnConfirmOnlineAbandon()
        {
            _isPaused = false;
            _pauseOverlay.style.display = DisplayStyle.None;
            _matchEnded = true;
            _router.Show(ScreenId.Home);
        }

        private static CellOwner Opposite(CellOwner symbol)
        {
            return symbol == CellOwner.X ? CellOwner.O : CellOwner.X;
        }

        private static MatchStatus WinStatusFor(CellOwner symbol)
        {
            return symbol == CellOwner.X ? MatchStatus.XWon : MatchStatus.OWon;
        }
    }
}
