using UnityEngine.UIElements;

namespace TTTXO.Game.UI
{
    /// <summary>
    /// One shared, non-blocking snackbar mounted once at the app root by <see cref="ScreenRouter"/>.
    /// Used for notices that must not block navigation, e.g. tapping the disabled Online mode card
    /// (Docs/06-Wireframes-UI.md screen 3: "al tapearla muestra un aviso no bloqueante y no navega").
    /// </summary>
    public class ToastController
    {
        private const long DisplayMilliseconds = 2200;

        private readonly VisualElement _root;
        private readonly Label _label;
        private IVisualElementScheduledItem _hideTask;

        public ToastController(VisualElement root)
        {
            _root = root;
            _root.pickingMode = PickingMode.Ignore;
            _label = _root.Q<Label>("toast-label");
            _root.style.display = DisplayStyle.None;
        }

        public void Show(string message)
        {
            _label.text = message;
            _root.style.display = DisplayStyle.Flex;

            _hideTask?.Pause();
            _hideTask = _root.schedule.Execute(Hide).StartingIn(DisplayMilliseconds);
        }

        private void Hide()
        {
            _root.style.display = DisplayStyle.None;
        }
    }
}
