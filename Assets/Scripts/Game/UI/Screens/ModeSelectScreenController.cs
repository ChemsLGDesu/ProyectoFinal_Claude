using TTTXO.Core;
using TTTXO.Game.Bootstrap;
using UnityEngine.UIElements;

namespace TTTXO.Game.UI.Screens
{
    /// <summary>
    /// Mode Select screen (Docs/06-Wireframes-UI.md screen 3): Single Player and Local 2P navigate
    /// straight to Board Select. Online opens an inline submenu - not covered by any wireframe, so
    /// this is a new addition rather than a wireframe deviation - with Quickmatch (Milestone 4) and
    /// Ranked (Milestone 5 - design-doc.md section 6), both navigating to Board Select with a
    /// different pending flow flag (see GameManager.SetPendingOnlineFlow/SetPendingRankedFlow).
    /// </summary>
    public class ModeSelectScreenController : IScreenController
    {
        private readonly ScreenRouter _router;
        private readonly GameManager _gameManager;
        private readonly VisualElement _root;
        private readonly VisualElement _modeCardList;
        private readonly VisualElement _onlineSubmenu;
        private bool _showingOnlineSubmenu;

        public ModeSelectScreenController(VisualElement root, ScreenRouter router, GameManager gameManager)
        {
            _root = root;
            _router = router;
            _gameManager = gameManager;

            _modeCardList = root.Q<VisualElement>("mode-card-list");
            _onlineSubmenu = root.Q<VisualElement>("mode-online-submenu");

            root.Q<Button>("mode-select-back-button").text = UiText.Common.BackIcon;
            root.Q<Button>("mode-select-back-button").clicked += OnBackClicked;

            // The three top-level mode cards carry generated art as a USS background-image
            // (card-icon--image in main.uss), so there is no glyph Label to fill in here. The online
            // submenu's cards below still use UiText.Icons because no art exists for them yet.
            var singlePlayerCard = root.Q<VisualElement>("mode-single-player-card");
            singlePlayerCard.RegisterCallback<ClickEvent>(_ => SelectMode(GameMode.SinglePlayer));

            var localMultiplayerCard = root.Q<VisualElement>("mode-local-multiplayer-card");
            localMultiplayerCard.RegisterCallback<ClickEvent>(_ => SelectMode(GameMode.LocalMultiplayer));

            var onlineCard = root.Q<VisualElement>("mode-online-card");
            onlineCard.RegisterCallback<ClickEvent>(_ => ShowOnlineSubmenu());

            var quickmatchCard = root.Q<VisualElement>("mode-online-quickmatch-card");
            root.Q<Label>("mode-online-quickmatch-icon-label").text = UiText.Icons.Online;
            quickmatchCard.RegisterCallback<ClickEvent>(_ => SelectOnlineQuickmatch());

            var rankedCard = root.Q<VisualElement>("mode-online-ranked-card");
            root.Q<Label>("mode-online-ranked-icon-label").text = UiText.Icons.Online;
            rankedCard.RegisterCallback<ClickEvent>(_ => SelectOnlineRanked());

            ApplyLocalizedText();
        }

        private void ApplyLocalizedText()
        {
            _root.Q<Label>("mode-select-title-label").text = UiText.ModeSelect.Title;
            _root.Q<Label>("mode-single-player-title-label").text = UiText.ModeSelect.SinglePlayerTitle;
            _root.Q<Label>("mode-single-player-description-label").text = UiText.ModeSelect.SinglePlayerDescription;
            _root.Q<Label>("mode-local-multiplayer-title-label").text = UiText.ModeSelect.LocalMultiplayerTitle;
            _root.Q<Label>("mode-local-multiplayer-description-label").text = UiText.ModeSelect.LocalMultiplayerDescription;
            _root.Q<Label>("mode-online-title-label").text = UiText.ModeSelect.OnlineTitle;
            _root.Q<Label>("mode-online-description-label").text = UiText.ModeSelect.OnlineDescription;

            _root.Q<Label>("mode-online-quickmatch-title-label").text = UiText.ModeSelect.OnlineQuickmatchTitle;
            _root.Q<Label>("mode-online-quickmatch-description-label").text = UiText.ModeSelect.OnlineQuickmatchDescription;
            _root.Q<Label>("mode-online-ranked-title-label").text = UiText.ModeSelect.OnlineRankedTitle;
            _root.Q<Label>("mode-online-ranked-description-label").text = UiText.ModeSelect.OnlineRankedDescription;
        }

        private void SelectMode(GameMode mode)
        {
            _gameManager.SetPendingMode(mode);
            _router.Show(ScreenId.BoardSelect);
        }

        private void ShowOnlineSubmenu()
        {
            _showingOnlineSubmenu = true;
            _modeCardList.style.display = DisplayStyle.None;
            _onlineSubmenu.style.display = DisplayStyle.Flex;
        }

        private void SelectOnlineQuickmatch()
        {
            _gameManager.SetPendingOnlineFlow();
            _router.Show(ScreenId.BoardSelect);
        }

        private void SelectOnlineRanked()
        {
            _gameManager.SetPendingRankedFlow();
            _router.Show(ScreenId.BoardSelect);
        }

        private void OnBackClicked()
        {
            if (_showingOnlineSubmenu)
            {
                _showingOnlineSubmenu = false;
                _onlineSubmenu.style.display = DisplayStyle.None;
                _modeCardList.style.display = DisplayStyle.Flex;
                return;
            }

            _router.Show(ScreenId.Home);
        }

        public void OnShow()
        {
            _showingOnlineSubmenu = false;
            _onlineSubmenu.style.display = DisplayStyle.None;
            _modeCardList.style.display = DisplayStyle.Flex;
        }

        public void OnHide()
        {
        }

        public void RefreshLocalizedText()
        {
            ApplyLocalizedText();
        }
    }
}
