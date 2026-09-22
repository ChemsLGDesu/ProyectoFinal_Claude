using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TTTXO.Core;
using TTTXO.Game.Bootstrap;
using TTTXO.Game.Services;
using UnityEngine.UIElements;

namespace TTTXO.Game.UI.Screens
{
    /// <summary>
    /// Board Select screen (Docs/06-Wireframes-UI.md screen 4): pick a board size and, only in Single
    /// Player, an AI difficulty. Sizes are never hardcoded here: their rules come from
    /// <see cref="BoardConfig"/> in TTTXO.Core, and which of them are *offered* comes from
    /// <see cref="GameConfigService"/> - i.e. the deployed BOARD_CONFIGS, or the local fallback that
    /// mirrors it. The header title echoes the mode chosen on the previous screen; "Start Game" stays
    /// anchored to the bottom of the screen.
    ///
    /// Milestone 4: when <see cref="GameManager.IsOnlineFlow"/> is set (Online -&gt; Quickmatch on
    /// Mode Select), the size list is filtered to sizes the quickmatch-pool actually accepts
    /// (<see cref="GameConfigService.BoardConfigs"/>'s "online_quickmatch" mode entry - "cualquier
    /// tamano de tablero" per Docs/03-Arquitectura-UGS-TicTacToe.md#Matchmaker, so today this is
    /// every size), the difficulty section stays hidden, and Confirm navigates to Matchmaking instead
    /// of Game.
    ///
    /// Milestone 5: when <see cref="GameManager.IsRankedFlow"/> is also set (Online -&gt; Ranked), the
    /// size list is filtered to <c>"ranked"</c> instead - 3x3/6x6 only at launch, design-doc.md
    /// section 6.0 - and Confirm starts a Ranked Matchmaker search instead of a Quickmatch one.
    /// </summary>
    public class BoardSelectScreenController : IScreenController
    {
        private static readonly AiDifficulty[] DifficultyOrder =
        {
            AiDifficulty.Easy, AiDifficulty.Medium, AiDifficulty.Hard, AiDifficulty.Adaptive
        };

        private readonly ScreenRouter _router;
        private readonly GameManager _gameManager;
        private readonly Label _titleLabel;
        private readonly Label _boardSizeSectionLabel;
        private readonly Label _difficultySectionLabel;
        private readonly VisualElement _boardSizeList;
        private readonly VisualElement _difficultySection;
        private readonly VisualElement _difficultyList;
        private readonly Button _confirmButton;

        private readonly Dictionary<int, VisualElement> _boardCardsBySize = new();
        private readonly Dictionary<AiDifficulty, Label> _difficultyChips = new();

        private BoardConfig _selectedBoard;
        private AiDifficulty _selectedDifficulty = AiDifficulty.Medium;

        public BoardSelectScreenController(VisualElement root, ScreenRouter router, GameManager gameManager)
        {
            _router = router;
            _gameManager = gameManager;

            _titleLabel = root.Q<Label>("board-select-title-label");
            _boardSizeSectionLabel = root.Q<Label>("board-size-section-label");
            _difficultySectionLabel = root.Q<Label>("difficulty-section-label");

            _boardSizeList = root.Q<VisualElement>("board-size-list");
            _difficultySection = root.Q<VisualElement>("difficulty-section");
            _difficultyList = root.Q<VisualElement>("difficulty-list");

            var backButton = root.Q<Button>("board-select-back-button");
            backButton.text = UiText.Common.BackIcon;
            backButton.clicked += () => _router.Show(ScreenId.ModeSelect);

            _confirmButton = root.Q<Button>("board-select-confirm-button");
            _confirmButton.clicked += OnConfirm;

            BuildDifficultyChips();
            ApplyLocalizedText();
        }

        private void ApplyLocalizedText()
        {
            _titleLabel.text = TitleForCurrentSelection();

            _boardSizeSectionLabel.text = UiText.BoardSelect.BoardSizeSectionLabel;
            _difficultySectionLabel.text = UiText.BoardSelect.DifficultyLabel;
            _confirmButton.text = UiText.BoardSelect.ConfirmButton;

            foreach (var pair in _difficultyChips)
            {
                pair.Value.text = DifficultyLabelFor(pair.Key);
            }
        }

        private string TitleForCurrentSelection()
        {
            if (_gameManager.IsRankedFlow)
            {
                return UiText.ModeSelect.OnlineRankedTitle;
            }

            if (_gameManager.IsOnlineFlow)
            {
                return UiText.ModeSelect.OnlineQuickmatchTitle;
            }

            return _gameManager.Mode == GameMode.SinglePlayer
                ? UiText.ModeSelect.SinglePlayerTitle
                : UiText.ModeSelect.LocalMultiplayerTitle;
        }

        public void OnShow()
        {
            _titleLabel.text = TitleForCurrentSelection();

            BuildBoardSizeCards();

            _difficultySection.style.display = !_gameManager.IsOnlineFlow && _gameManager.Mode == GameMode.SinglePlayer
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            _selectedBoard = null;
            SetSelectedDifficulty(AiDifficulty.Medium);

            // Milestone 4 "Play vs AI" Matchmaking fallback (Docs/06-Wireframes-UI.md screen 5's one
            // documented deviation): pre-select the size the player was searching for, then forget it
            // so it never leaks into an unrelated later visit.
            if (!_gameManager.IsOnlineFlow && _gameManager.Mode == GameMode.SinglePlayer &&
                _gameManager.LastQuickmatchBoardSize.HasValue &&
                _boardCardsBySize.ContainsKey(_gameManager.LastQuickmatchBoardSize.Value))
            {
                SelectBoard(BoardConfig.ForSize(_gameManager.LastQuickmatchBoardSize.Value));
                _gameManager.ClearLastQuickmatchBoardSize();
            }

            RefreshConfirmButtonState();
        }

        public void OnHide()
        {
        }

        public void RefreshLocalizedText()
        {
            ApplyLocalizedText();
        }

        private void BuildBoardSizeCards()
        {
            _boardSizeList.Clear();
            _boardCardsBySize.Clear();

            foreach (var board in BoardSizesForCurrentSelection())
            {
                var card = new VisualElement();
                card.AddToClassList("board-size-card");

                var sizeLabel = new Label(string.Format(CultureInfo.InvariantCulture, UiText.BoardSelect.BoardSizeFormat, board.Size));
                sizeLabel.AddToClassList("board-size-card-label");
                card.Add(sizeLabel);

                var alignLabel = new Label(string.Format(CultureInfo.InvariantCulture, UiText.BoardSelect.BoardAlignFormat, board.WinLength));
                alignLabel.AddToClassList("board-size-card-align-label");
                card.Add(alignLabel);

                card.RegisterCallback<ClickEvent>(_ => SelectBoard(board));

                _boardSizeList.Add(card);
                _boardCardsBySize[board.Size] = card;
            }
        }

        /// <summary>
        /// Every flow is filtered by what <see cref="GameConfigService"/> advertises for its mode
        /// (wireframe screen 4: "Tamanos no disponibles para el modo: ocultos, no griseados"): the two
        /// local modes by "single_player"/"local_multiplayer", Quickmatch by "online_quickmatch",
        /// Ranked by "ranked".
        ///
        /// The local branch used to return <c>BoardConfig.All</c> outright, on the reasoning that
        /// offline play should never depend on a config it might not have. That was wrong once a board
        /// size stopped shipping: 9x9/11x11 were cut from BOARD_CONFIGS and kept appearing in 1P and
        /// 2P local, which is exactly where they were played. Core still knows every size the game has
        /// ever specified; what is *offered* is a config decision, and that has to hold offline too -
        /// the local fallback carries the shipped set precisely so this filter has something correct
        /// to read with no network.
        /// </summary>
        private IEnumerable<BoardConfig> BoardSizesForCurrentSelection()
        {
            return EligibleBoardSizes(_gameManager.IsOnlineFlow, _gameManager.IsRankedFlow, GameConfigService.BoardConfigs);
        }

        /// <summary>
        /// The filter itself, lifted out of the screen so a test can reach every branch without a
        /// UIDocument or a GameManager. The 9x9/11x11 cut shipped with this logic wrong and 231 tests
        /// green, because nothing could see it: the only assertion that could have caught it needed
        /// exactly this seam.
        /// </summary>
        internal static IEnumerable<BoardConfig> EligibleBoardSizes(
            bool isOnlineFlow, bool isRankedFlow, IReadOnlyList<BoardConfigDto> configs)
        {
            configs ??= System.Array.Empty<BoardConfigDto>();

            if (!isOnlineFlow)
            {
                var localSizes = new HashSet<int>(
                    configs
                        .Where(b => b.Modes != null
                            && (b.Modes.Contains(GameAnalytics.ModeCode(GameMode.SinglePlayer))
                                || b.Modes.Contains(GameAnalytics.ModeCode(GameMode.LocalMultiplayer))))
                        .Select(b => b.Size));

                if (localSizes.Count == 0)
                {
                    // Same shape as the Quickmatch fallback below: an empty grid with no way to start
                    // is worse than offering more than intended, and local play must survive a config
                    // that never loaded.
                    UnityEngine.Debug.LogWarning(
                        "BoardSelect: no board size advertises a local mode in the loaded config; " +
                        "falling back to all sizes. Redeploy Assets/RemoteConfig/GameConfig.rc to fix the source.");
                    return BoardConfig.All;
                }

                return BoardConfig.All.Where(b => localSizes.Contains(b.Size));
            }

            string modeFilter = isRankedFlow ? GameAnalytics.RankedModeCode : GameAnalytics.OnlineQuickmatchModeCode;

            var eligibleSizes = new HashSet<int>(
                configs
                    .Where(b => b.Modes != null && b.Modes.Contains(modeFilter))
                    .Select(b => b.Size));

            if (eligibleSizes.Count == 0)
            {
                if (isRankedFlow)
                {
                    // Ranked is 3x3/6x6 ONLY at launch - a fixed design invariant (design-doc.md
                    // section 6.0), not configurable data. Unlike Quickmatch's "fall back to every
                    // size" default below (correct there because the quickmatch-pool truly accepts
                    // any size), falling back to every size here would let the player queue Ranked for
                    // 9x9/11x11, which CreateMatch's Mode == "ranked" branch never expects - fall back
                    // to the fixed {3, 6} set instead.
                    UnityEngine.Debug.LogWarning(
                        "BoardSelect: no board size advertises 'ranked' in the loaded config; falling back to " +
                        "the fixed {3, 6} set. Redeploy Assets/RemoteConfig/GameConfig.rc to fix the source.");
                    return BoardConfig.All.Where(b => b.Size == 3 || b.Size == 6);
                }

                // Defensive fallback: the quickmatch-pool accepts any board size
                // (Docs/03-Arquitectura-UGS-TicTacToe.md#Matchmaker), so a config that tags no size for
                // online_quickmatch (e.g. a Remote Config BOARD_CONFIGS predating Milestone 4) must not
                // leave the player with an empty grid and no way to start - fall back to every size and
                // warn, rather than block the whole online flow on a stale server config.
                UnityEngine.Debug.LogWarning(
                    "BoardSelect: no board size advertises 'online_quickmatch' in the loaded config; " +
                    "falling back to all sizes. Redeploy Assets/RemoteConfig/GameConfig.rc to fix the source.");
                return BoardConfig.All;
            }

            return BoardConfig.All.Where(b => eligibleSizes.Contains(b.Size));
        }

        private void BuildDifficultyChips()
        {
            foreach (var difficulty in DifficultyOrder)
            {
                var chip = new Label(DifficultyLabelFor(difficulty));
                chip.AddToClassList("difficulty-chip");
                chip.RegisterCallback<ClickEvent>(_ => SetSelectedDifficulty(difficulty));

                _difficultyList.Add(chip);
                _difficultyChips[difficulty] = chip;
            }
        }

        private static string DifficultyLabelFor(AiDifficulty difficulty)
        {
            return difficulty switch
            {
                AiDifficulty.Easy => UiText.BoardSelect.DifficultyEasy,
                AiDifficulty.Medium => UiText.BoardSelect.DifficultyMedium,
                AiDifficulty.Hard => UiText.BoardSelect.DifficultyHard,
                AiDifficulty.Adaptive => UiText.BoardSelect.DifficultyAdaptive,
                _ => difficulty.ToString(),
            };
        }

        private void SelectBoard(BoardConfig board)
        {
            _selectedBoard = board;

            foreach (var pair in _boardCardsBySize)
            {
                pair.Value.EnableInClassList("board-size-card--selected", pair.Key == board.Size);
            }

            GameAnalytics.BoardSizeSelected(board.Size, _gameManager.Mode);

            RefreshConfirmButtonState();
        }

        private void SetSelectedDifficulty(AiDifficulty difficulty)
        {
            _selectedDifficulty = difficulty;

            foreach (var pair in _difficultyChips)
            {
                pair.Value.EnableInClassList("difficulty-chip--selected", pair.Key == difficulty);
            }
        }

        private void RefreshConfirmButtonState()
        {
            _confirmButton.SetEnabled(_selectedBoard != null);
        }

        private void OnConfirm()
        {
            if (_selectedBoard == null)
            {
                return;
            }

            if (_gameManager.IsOnlineFlow)
            {
                if (_gameManager.IsRankedFlow)
                {
                    _router.MatchmakingScreen.StartRankedSearch(_selectedBoard.Size);
                }
                else
                {
                    _router.MatchmakingScreen.StartSearch(_selectedBoard.Size);
                }

                _router.Show(ScreenId.Matchmaking);
                return;
            }

            _gameManager.ConfigureMatch(_gameManager.Mode, _selectedBoard, _selectedDifficulty);
            _router.Show(ScreenId.Game);
        }
    }
}
