using System;
using System.Collections.Generic;
using TTTXO.Game.Bootstrap;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace TTTXO.Game.UI.Screens
{
    /// <summary>
    /// Settings screen (Docs/06-Wireframes-UI.md screen 12): Language is a live dropdown listing the
    /// 10 target locales (Docs/02-GDD-TicTacToe.md#8 Localización) by their native name, switching
    /// <see cref="LocalizationSettings.SelectedLocale"/> immediately on selection - Milestone 1's
    /// disabled-dropdown-stuck-on-English stub and its "more languages soon" note are gone as of
    /// Milestone 3. Sound/Music sliders persist their value to PlayerPrefs even though no audio system
    /// plays them back yet. There is no account section or push toggle in Milestone 1 (no
    /// Authentication, no Wire/push yet) - "Delete account & data" uses the same confirm-and-wipe flow
    /// as Profile's "Delete my data".
    /// </summary>
    public class SettingsScreenController : IScreenController
    {
        /// <summary>
        /// Must match <c>UnityEngine.Localization.Settings.PlayerPrefLocaleSelector.PlayerPreferenceKey</c>'s
        /// default value - <see cref="LocalizationSetup"/> wires that selector with this same default key
        /// (Assets/Scripts/Game/Editor/LocalizationSetup.cs), and writing it here too means the choice is
        /// persisted for next launch even if the selector's own change-tracking hook does not run in
        /// this session (e.g. localization is not fully initialized yet).
        /// </summary>
        private const string LocalePlayerPrefKey = "selected-locale";

        private readonly ScreenRouter _router;
        private readonly GameManager _gameManager;
        private readonly VisualElement _root;
        private readonly Slider _soundSlider;
        private readonly Slider _musicSlider;
        private readonly DropdownField _languageDropdown;
        private readonly List<string> _languageNativeNames = new();

        public SettingsScreenController(VisualElement root, ScreenRouter router, GameManager gameManager)
        {
            _root = root;
            _router = router;
            _gameManager = gameManager;

            root.Q<Button>("settings-back-button").text = UiText.Common.BackIcon;
            root.Q<Button>("settings-back-button").clicked += () => _router.GoBack(ScreenId.Home);

            foreach (var entry in SupportedLocales.All)
            {
                _languageNativeNames.Add(entry.NativeName);
            }

            _languageDropdown = root.Q<DropdownField>("settings-language-dropdown");
            _languageDropdown.choices = _languageNativeNames;
            _languageDropdown.SetEnabled(true);
            _languageDropdown.RegisterValueChangedCallback(OnLanguageChanged);

            // The Milestone 1 "More languages soon" note no longer applies - all 10 are live. The
            // label itself stays in the UXML tree (harmless if unused) rather than being removed, in
            // case a future milestone reintroduces it (e.g. "N more languages coming").
            root.Q<Label>("settings-language-note-label").style.display = DisplayStyle.None;

            _soundSlider = root.Q<Slider>("settings-sound-slider");
            _soundSlider.RegisterValueChangedCallback(evt => _gameManager.SetSoundVolume(evt.newValue));

            _musicSlider = root.Q<Slider>("settings-music-slider");
            _musicSlider.RegisterValueChangedCallback(evt => _gameManager.SetMusicVolume(evt.newValue));

            var deleteButton = root.Q<Button>("settings-delete-account-button");
            deleteButton.clicked += OnDeleteAccountClicked;

            ApplyLocalizedText();
        }

        private void ApplyLocalizedText()
        {
            _root.Q<Label>("settings-title-label").text = UiText.Settings.Title;
            _root.Q<Label>("settings-language-label").text = UiText.Settings.LanguageLabel;
            _root.Q<Label>("settings-sound-label").text = UiText.Settings.SoundLabel;
            _root.Q<Label>("settings-music-label").text = UiText.Settings.MusicLabel;
            _root.Q<Button>("settings-delete-account-button").text = UiText.Settings.DeleteAccountButton;

            // Native language names in the dropdown are never translated (a language picker always
            // lists languages by their own name) - only the currently selected value needs re-syncing,
            // in case the locale changed programmatically rather than through this dropdown.
            SyncLanguageDropdown();
        }

        public void OnShow()
        {
            _soundSlider.SetValueWithoutNotify(_gameManager.SoundVolume);
            _musicSlider.SetValueWithoutNotify(_gameManager.MusicVolume);
            SyncLanguageDropdown();
        }

        public void OnHide()
        {
        }

        public void RefreshLocalizedText()
        {
            ApplyLocalizedText();
        }

        private void SyncLanguageDropdown()
        {
            string code = CurrentLocaleCodeOrDefault();

            int index = Array.FindIndex(SupportedLocales.All, entry => entry.Code == code);
            if (index < 0)
            {
                index = Array.FindIndex(SupportedLocales.All, entry => entry.Code == SupportedLocales.DefaultLocaleCode);
            }

            if (index >= 0)
            {
                _languageDropdown.SetValueWithoutNotify(_languageNativeNames[index]);
            }
        }

        private static string CurrentLocaleCodeOrDefault()
        {
            try
            {
                if (LocalizationSettings.HasSettings && LocalizationSettings.InitializationOperation.IsDone)
                {
                    var locale = LocalizationSettings.SelectedLocale;
                    if (locale != null)
                    {
                        return locale.Identifier.Code;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"SettingsScreenController: could not read the current locale - {ex.Message}");
            }

            return SupportedLocales.DefaultLocaleCode;
        }

        private void OnLanguageChanged(ChangeEvent<string> evt)
        {
            int index = _languageNativeNames.IndexOf(evt.newValue);
            if (index < 0)
            {
                return;
            }

            string code = SupportedLocales.All[index].Code;

            // Defensive like GameAnalytics/UiText: picking a language must never throw or freeze the
            // Settings screen, even if localization assets have not been generated yet (see
            // Assets/Scripts/Game/Editor/LocalizationSetup.cs) or the requested locale is missing.
            try
            {
                PlayerPrefs.SetString(LocalePlayerPrefKey, code);
                PlayerPrefs.Save();

                if (!LocalizationSettings.HasSettings)
                {
                    Debug.LogWarning("SettingsScreenController: localization tables are not set up yet (run TTTXO/Setup/Create Localization Assets) - the language choice is saved for the next launch only.");
                    return;
                }

                var locale = LocalizationSettings.AvailableLocales.GetLocale(code);
                if (locale == null)
                {
                    Debug.LogWarning($"SettingsScreenController: locale '{code}' is not registered in LocalizationSettings - selection ignored.");
                    return;
                }

                // Fires LocalizationSettings.SelectedLocaleChanged, which ScreenRouter listens to in
                // order to refresh every constructed screen's localized text (see
                // IScreenController.RefreshLocalizedText) - this is what makes the switch instant.
                LocalizationSettings.SelectedLocale = locale;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"SettingsScreenController: failed to change language to '{code}' - {ex.Message}");
            }
        }

        private void OnDeleteAccountClicked()
        {
            _router.ConfirmDialog.Show(
                UiText.Settings.DeleteAccountConfirmTitle,
                UiText.Profile.DeleteConfirmMessage,
                UiText.Profile.DeleteConfirmButton,
                () =>
                {
                    _gameManager.DeleteAllPlayerData();
                    OnShow();
                    _router.Show(ScreenId.Home);
                });
        }
    }
}
