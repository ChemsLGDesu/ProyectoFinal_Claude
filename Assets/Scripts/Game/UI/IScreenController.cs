namespace TTTXO.Game.UI
{
    /// <summary>
    /// Common lifecycle for every per-screen controller managed by <see cref="ScreenRouter"/>.
    /// </summary>
    public interface IScreenController
    {
        /// <summary>Called by the router right before the screen's root becomes visible.</summary>
        void OnShow();

        /// <summary>Called by the router right after the screen's root is hidden.</summary>
        void OnHide();

        /// <summary>
        /// Called by <see cref="ScreenRouter"/> on <c>LocalizationSettings.SelectedLocaleChanged</c>
        /// (see Docs/02-GDD-TicTacToe.md#8 Localización), for every constructed screen regardless of
        /// whether it is currently visible. Must only reassign the <c>.text</c> of elements whose copy
        /// was set once from <see cref="UiText"/> in the constructor - it must never touch gameplay
        /// state (a screen mid-match, e.g. Game, must not reset on a language change).
        /// </summary>
        void RefreshLocalizedText();
    }
}
