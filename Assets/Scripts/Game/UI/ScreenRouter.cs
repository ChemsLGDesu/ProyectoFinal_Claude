using System;
using System.Collections.Generic;
using TTTXO.Game.Bootstrap;
using TTTXO.Game.UI.Screens;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace TTTXO.Game.UI
{
    /// <summary>
    /// Single root UIDocument navigation host. Every screen is a sub-tree of the same UXML document
    /// (Assets/UI/Screens/Root.uxml); this router shows exactly one at a time by toggling display
    /// style and forwards lifecycle events to each screen's controller. It also mounts the two
    /// shared overlays (confirm dialog, toast) used across several screens.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ScreenRouter : MonoBehaviour
    {
        private static readonly IReadOnlyDictionary<ScreenId, string> ScreenElementNames = new Dictionary<ScreenId, string>
        {
            { ScreenId.Splash, "screen-splash" },
            { ScreenId.Home, "screen-home" },
            { ScreenId.ModeSelect, "screen-mode-select" },
            { ScreenId.BoardSelect, "screen-board-select" },
            { ScreenId.Matchmaking, "screen-matchmaking" },
            { ScreenId.Game, "screen-game" },
            { ScreenId.Result, "screen-result" },
            { ScreenId.Profile, "screen-profile" },
            { ScreenId.MatchHistory, "screen-match-history" },
            { ScreenId.Store, "screen-store" },
            { ScreenId.Settings, "screen-settings" },
            { ScreenId.Leaderboard, "screen-leaderboard" },
        };

        private UIDocument _document;
        private readonly Dictionary<ScreenId, VisualElement> _screenRoots = new();
        private readonly Dictionary<ScreenId, IScreenController> _controllers = new();
        private ScreenId? _currentScreen;
        private bool _initialized;

        public GameScreenController GameScreen { get; private set; }
        public ResultScreenController ResultScreen { get; private set; }
        public MatchmakingScreenController MatchmakingScreen { get; private set; }

        /// <summary>
        /// Navigation history for generic "back" buttons on sub-screens. The rules - and why a
        /// single-level "previous screen" field is not enough - live in <see cref="ScreenHistory"/>,
        /// which holds them without a UIDocument so they can be exercised by a test.
        /// </summary>
        private readonly ScreenHistory _history = new();
        private bool _navigatingBack;

        public ConfirmDialogController ConfirmDialog { get; private set; }
        public ToastController Toast { get; private set; }

        /// <summary>Keeps every screen's content clear of the notch and the home indicator.</summary>
        public SafeAreaController SafeArea { get; private set; }

        /// <summary>Binds every screen root, constructs the per-screen controllers, and shows Splash.</summary>
        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _document = GetComponent<UIDocument>();
            var root = _document.rootVisualElement;

            foreach (var pair in ScreenElementNames)
            {
                var element = root.Q<VisualElement>(pair.Value);
                if (element == null)
                {
                    Debug.LogError($"ScreenRouter: could not find root element '{pair.Value}' for screen {pair.Key}.");
                    continue;
                }

                _screenRoots[pair.Key] = element;
            }

            ConfirmDialog = new ConfirmDialogController(root.Q<VisualElement>("confirm-dialog-overlay"));
            Toast = new ToastController(root.Q<VisualElement>("toast"));

            var gameManager = GameManager.Instance;

            GameScreen = new GameScreenController(_screenRoots[ScreenId.Game], this, gameManager);
            ResultScreen = new ResultScreenController(_screenRoots[ScreenId.Result], this, gameManager);
            MatchmakingScreen = new MatchmakingScreenController(_screenRoots[ScreenId.Matchmaking], this, gameManager);

            _controllers[ScreenId.Splash] = new SplashScreenController(_screenRoots[ScreenId.Splash], this);
            _controllers[ScreenId.Home] = new HomeScreenController(_screenRoots[ScreenId.Home], this, gameManager);
            _controllers[ScreenId.ModeSelect] = new ModeSelectScreenController(_screenRoots[ScreenId.ModeSelect], this, gameManager);
            _controllers[ScreenId.BoardSelect] = new BoardSelectScreenController(_screenRoots[ScreenId.BoardSelect], this, gameManager);
            _controllers[ScreenId.Matchmaking] = MatchmakingScreen;
            _controllers[ScreenId.Game] = GameScreen;
            _controllers[ScreenId.Result] = ResultScreen;
            _controllers[ScreenId.Profile] = new ProfileScreenController(_screenRoots[ScreenId.Profile], this, gameManager);
            _controllers[ScreenId.MatchHistory] = new MatchHistoryScreenController(_screenRoots[ScreenId.MatchHistory], this, gameManager);
            _controllers[ScreenId.Store] = new StoreScreenController(_screenRoots[ScreenId.Store], this, gameManager);
            _controllers[ScreenId.Settings] = new SettingsScreenController(_screenRoots[ScreenId.Settings], this, gameManager);
            _controllers[ScreenId.Leaderboard] = new LeaderboardScreenController(_screenRoots[ScreenId.Leaderboard], this, gameManager);

            foreach (var pair in _screenRoots)
            {
                pair.Value.style.display = DisplayStyle.None;
            }

            // Insets every screen out from under the notch and the home indicator. Constructed after the
            // screen roots are bound and before anything is shown, so the first painted frame is already
            // inside the safe area rather than jumping once the first resize arrives.
            SafeArea = new SafeAreaController(root, new List<VisualElement>(_screenRoots.Values));

            _initialized = true;

            // Milestone 3 (Docs/02-GDD-TicTacToe.md#8 Localización): every screen's static UiText-
            // sourced labels/buttons are only ever assigned once, from each controller's constructor
            // (see IScreenController.RefreshLocalizedText). Re-run that assignment - for every
            // constructed screen, not just the visible one, so a screen the player later navigates
            // back to is already correct - whenever the player picks a different language on the
            // Settings screen (see SettingsScreenController's language dropdown).
            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;

            Show(ScreenId.Splash);
        }

        private void OnDestroy()
        {
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        private void OnSelectedLocaleChanged(Locale _)
        {
            // Defensive like GameAnalytics/UiText: a broken screen refresh must never take down the
            // rest of the app or block navigation, so each controller is refreshed independently.
            foreach (var pair in _controllers)
            {
                try
                {
                    pair.Value.RefreshLocalizedText();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"ScreenRouter: RefreshLocalizedText failed for {pair.Key} - {ex.Message}");
                }
            }

            ConfirmDialog?.RefreshLocalizedText();
        }

        /// <summary>Hides the current screen (if any) and shows the requested one.</summary>
        public void Show(ScreenId screenId)
        {
            if (!_screenRoots.TryGetValue(screenId, out var nextRoot))
            {
                Debug.LogError($"ScreenRouter: screen {screenId} is not registered.");
                return;
            }

            ScreenId? leavingScreen = null;
            if (_currentScreen.HasValue && _screenRoots.TryGetValue(_currentScreen.Value, out var currentRoot))
            {
                currentRoot.style.display = DisplayStyle.None;
                if (_controllers.TryGetValue(_currentScreen.Value, out var currentController))
                {
                    currentController.OnHide();
                }

                leavingScreen = _currentScreen.Value;
            }

            _history.RecordNavigation(leavingScreen, screenId, _navigatingBack);

            nextRoot.style.display = DisplayStyle.Flex;
            _currentScreen = screenId;

            if (_controllers.TryGetValue(screenId, out var nextController))
            {
                nextController.OnShow();
            }
        }

        /// <summary>
        /// Convenience for sub-screens whose back button should return wherever the player came from
        /// (Profile/Store/Settings/MatchHistory can be reached from more than one place), falling back
        /// to <paramref name="fallback"/> if there is no recorded history. Pops the history instead of
        /// pushing onto it, so back-and-forth between two screens can never loop.
        /// </summary>
        public void GoBack(ScreenId fallback)
        {
            var target = _history.ResolveBack(fallback);
            _navigatingBack = true;
            try
            {
                Show(target);
            }
            finally
            {
                _navigatingBack = false;
            }
        }
    }
}
