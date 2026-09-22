using System;
using UnityEngine.UIElements;

namespace TTTXO.Game.UI
{
    /// <summary>
    /// One shared confirmation overlay (Docs/06-Wireframes-UI.md screens 8/12 "Delete my data" /
    /// "Delete account & data") mounted once at the app root by <see cref="ScreenRouter"/> and reused
    /// by any screen controller that needs a blocking yes/no prompt, instead of duplicating the
    /// markup and wiring per screen.
    /// </summary>
    public class ConfirmDialogController
    {
        private readonly VisualElement _root;
        private readonly Label _titleLabel;
        private readonly Label _messageLabel;
        private readonly Button _confirmButton;
        private readonly Button _cancelButton;

        private Action _onConfirm;

        public ConfirmDialogController(VisualElement root)
        {
            _root = root;
            _titleLabel = _root.Q<Label>("confirm-dialog-title-label");
            _messageLabel = _root.Q<Label>("confirm-dialog-message-label");
            _confirmButton = _root.Q<Button>("confirm-dialog-confirm-button");
            _cancelButton = _root.Q<Button>("confirm-dialog-cancel-button");

            _cancelButton.text = UiText.Common.Cancel;
            _cancelButton.clicked += Hide;
            _confirmButton.clicked += OnConfirmClicked;

            _root.style.display = DisplayStyle.None;
        }

        /// <summary>Shows the overlay with the given copy; invokes <paramref name="onConfirm"/> only if the player confirms.</summary>
        public void Show(string title, string message, string confirmButtonLabel, Action onConfirm)
        {
            _titleLabel.text = title;
            _messageLabel.text = message;
            _confirmButton.text = confirmButtonLabel;
            _onConfirm = onConfirm;
            _root.style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            _root.style.display = DisplayStyle.None;
            _onConfirm = null;
        }

        /// <summary>
        /// Re-applies the one label this shared overlay owns outright (Cancel - Confirm/title/message
        /// are supplied per-call by <see cref="Show"/> and refreshed by whichever screen re-shows this
        /// dialog). See <see cref="ScreenRouter"/>'s LocalizationSettings.SelectedLocaleChanged handler.
        /// </summary>
        public void RefreshLocalizedText()
        {
            _cancelButton.text = UiText.Common.Cancel;
        }

        private void OnConfirmClicked()
        {
            var callback = _onConfirm;
            Hide();
            callback?.Invoke();
        }
    }
}
