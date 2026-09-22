using System.Threading.Tasks;
using TTTXO.Game.Bootstrap;
using TTTXO.Game.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace TTTXO.Game.UI.Screens
{
    /// <summary>
    /// Splash: logo + spinner + "Loading…" centered (Docs/06-Wireframes-UI.md screen 1). Hooks the
    /// screen's documented flow ("init UGS → sign-in anonimo → primer GetGameConfig"):
    /// <see cref="UgsInitializer"/> initializes UGS + signs in anonymously, then (best-effort, never
    /// blocking) <see cref="GameConfigService"/> makes the first GetGameConfig call and
    /// <see cref="PlayerDataService"/> loads the Cloud Save mirror, before navigating to Home.
    ///
    /// On <see cref="UgsInitStatus.Failed"/> this shows a generic, non-technical error state with a
    /// Retry button (design-doc wireframe 1a) instead of navigating - Milestone 1 local/1P play is
    /// never blocked by this (GameConfigService/PlayerDataService both degrade to local fallbacks on
    /// their own once a session does exist), but Splash itself only proceeds once UGS init settles.
    /// </summary>
    public class SplashScreenController : IScreenController
    {
        private const int MinimumSpinnerMilliseconds = 500;
        private const int SpinnerIntervalMilliseconds = 16;
        private const float SpinnerDegreesPerSecond = 320f;

        private readonly VisualElement _root;
        private readonly ScreenRouter _router;
        private readonly Label _titleLabel;
        private readonly Label _loadingLabel;
        private readonly VisualElement _spinner;
        private readonly VisualElement _errorContainer;
        private readonly Label _errorLabel;
        private readonly Button _retryButton;

        private IVisualElementScheduledItem _spinTask;
        private float _spinnerAngle;
        private int _runGeneration;

        public SplashScreenController(VisualElement root, ScreenRouter router)
        {
            _root = root;
            _router = router;

            _titleLabel = _root.Q<Label>("splash-title-label");
            _loadingLabel = _root.Q<Label>("splash-loading-label");
            _spinner = _root.Q<VisualElement>("splash-spinner");
            _errorContainer = _root.Q<VisualElement>("splash-error-container");
            _errorLabel = _root.Q<Label>("splash-error-label");
            _retryButton = _root.Q<Button>("splash-retry-button");

            ApplyLocalizedText();
            _retryButton.clicked += OnRetryClicked;
        }

        private void ApplyLocalizedText()
        {
            _titleLabel.text = UiText.Splash.AppTitle;
            _loadingLabel.text = UiText.Splash.Loading;
            _errorLabel.text = UiText.Splash.ErrorMessage;
            _retryButton.text = UiText.Splash.RetryButton;
        }

        public void RefreshLocalizedText()
        {
            ApplyLocalizedText();
        }

        public async void OnShow()
        {
            _runGeneration++;
            int generation = _runGeneration;

            ShowLoadingState();
            await RunStartupSequenceAsync(generation);
        }

        public void OnHide()
        {
            _spinTask?.Pause();
            _spinTask = null;
        }

        private async Task RunStartupSequenceAsync(int generation)
        {
            float startTime = Time.realtimeSinceStartup;

            var status = await UgsInitializer.InitializeAsync();
            if (generation != _runGeneration)
            {
                return;
            }

            if (status == UgsInitStatus.Failed)
            {
                ShowErrorState();
                return;
            }

            // Best-effort session-start hooks: neither can fail the app - see their own try/catch
            // (GameConfigService falls back to local TTTXO.Core values, PlayerDataService just keeps
            // whatever local state was already loaded from PlayerPrefs).
            await GameConfigService.InitializeAsync();
            await PlayerDataService.LoadIntoSessionAsync(GameManager.Instance);
            if (generation != _runGeneration)
            {
                return;
            }

            float elapsedMilliseconds = (Time.realtimeSinceStartup - startTime) * 1000f;
            if (elapsedMilliseconds < MinimumSpinnerMilliseconds)
            {
                await Task.Delay(MinimumSpinnerMilliseconds - (int)elapsedMilliseconds);
                if (generation != _runGeneration)
                {
                    return;
                }
            }

            _router.Show(ScreenId.Home);
        }

        private void OnRetryClicked()
        {
            UgsInitializer.ResetForRetry();
            OnShow();
        }

        private void ShowLoadingState()
        {
            _spinnerAngle = 0f;
            _spinner.style.display = DisplayStyle.Flex;
            _loadingLabel.style.display = DisplayStyle.Flex;
            _errorContainer.style.display = DisplayStyle.None;

            _spinTask?.Pause();
            _spinTask = _root.schedule.Execute(AdvanceSpinner).Every(SpinnerIntervalMilliseconds);
        }

        private void ShowErrorState()
        {
            _spinTask?.Pause();
            _spinTask = null;

            _spinner.style.display = DisplayStyle.None;
            _loadingLabel.style.display = DisplayStyle.None;
            _errorContainer.style.display = DisplayStyle.Flex;
        }

        private void AdvanceSpinner()
        {
            _spinnerAngle = (_spinnerAngle + SpinnerDegreesPerSecond * SpinnerIntervalMilliseconds / 1000f) % 360f;
            _spinner.style.rotate = new Rotate(new Angle(_spinnerAngle, AngleUnit.Degree));
        }
    }
}
