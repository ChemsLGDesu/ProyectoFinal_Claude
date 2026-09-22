using System.Globalization;
using System.Threading.Tasks;
using TTTXO.Game.Bootstrap;
using TTTXO.Game.Services;
using UnityEngine.UIElements;

namespace TTTXO.Game.UI.Screens
{
    /// <summary>
    /// Home screen (Docs/06-Wireframes-UI.md screen 2): player name + always-visible soft/hard
    /// balances in the header, big primary "Play" button, and the Profile/Store/Settings row.
    /// The player name comes from <see cref="PlayerNameService"/> (Authentication's "Name#1234")
    /// when a UGS session is available, falling back to the local placeholder otherwise.
    /// </summary>
    public class HomeScreenController : IScreenController
    {
        private readonly ScreenRouter _router;
        private readonly GameManager _gameManager;
        private readonly VisualElement _root;
        private readonly Label _playerNameLabel;
        private readonly EquippedAvatarView _avatarView;
        private readonly Label _softCurrencyLabel;
        private readonly Label _hardCurrencyLabel;

        public HomeScreenController(VisualElement root, ScreenRouter router, GameManager gameManager)
        {
            _root = root;
            _router = router;
            _gameManager = gameManager;

            _playerNameLabel = root.Q<Label>("home-player-name-label");

            // The equipped portrait next to the name. Held in a field so it is not collected while it is
            // still subscribed to CosmeticSelection.Changed.
            _avatarView = new EquippedAvatarView(
                root.Q<VisualElement>("home-avatar"),
                root.Q<VisualElement>("home-avatar-frame"));

            var playButton = root.Q<Button>("home-play-button");
            playButton.clicked += () => _router.Show(ScreenId.ModeSelect);

            _softCurrencyLabel = root.Q<Label>("home-soft-currency-label");
            _hardCurrencyLabel = root.Q<Label>("home-hard-currency-label");

            root.Q<Label>("home-profile-icon-label").text = UiText.Icons.Profile;
            root.Q<VisualElement>("home-profile-button").RegisterCallback<ClickEvent>(_ => _router.Show(ScreenId.Profile));

            root.Q<Label>("home-store-icon-label").text = UiText.Icons.Store;
            root.Q<VisualElement>("home-store-button").RegisterCallback<ClickEvent>(_ => _router.Show(ScreenId.Store));

            root.Q<Label>("home-settings-icon-label").text = UiText.Icons.Settings;
            root.Q<VisualElement>("home-settings-button").RegisterCallback<ClickEvent>(_ => _router.Show(ScreenId.Settings));

            // "home-profile-badge" (pending online match notification) stays hidden: there is no
            // online mode yet in Milestone 1, so there is no source of truth for it (see
            // Docs/05-UI-Pantallas-TicTacToe.md screen 2).

            ApplyLocalizedText();
        }

        private void ApplyLocalizedText()
        {
            _root.Q<Label>("home-title-label").text = UiText.Home.Title;
            _root.Q<Button>("home-play-button").text = UiText.Home.PlayButton;
            _root.Q<Label>("home-profile-label").text = UiText.Home.ProfileLabel;
            _root.Q<Label>("home-store-label").text = UiText.Home.StoreLabel;
            _root.Q<Label>("home-settings-label").text = UiText.Home.SettingsLabel;
        }

        public void RefreshLocalizedText()
        {
            ApplyLocalizedText();
        }

        public void OnShow()
        {
            _softCurrencyLabel.text = string.Format(CultureInfo.InvariantCulture, UiText.Home.CurrencyFormat, _gameManager.TotalCurrencyBalance);
            _hardCurrencyLabel.text = string.Format(CultureInfo.InvariantCulture, UiText.Home.CurrencyFormat, _gameManager.HardCurrencyBalance);

            UpdatePlayerNameLabel();
            if (!PlayerNameService.HasPlayerName && UgsInitializer.Status == UgsInitStatus.Ready)
            {
                _ = RefreshPlayerNameAsync();
            }
        }

        public void OnHide()
        {
        }

        private void UpdatePlayerNameLabel()
        {
            _playerNameLabel.text = PlayerNameService.HasPlayerName
                ? PlayerNameService.CachedPlayerName
                : UiText.Common.PlayerNamePlaceholder;
        }

        private async Task RefreshPlayerNameAsync()
        {
            await PlayerNameService.RefreshAsync();
            UpdatePlayerNameLabel();
        }
    }
}
