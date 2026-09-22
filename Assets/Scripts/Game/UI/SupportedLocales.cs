namespace TTTXO.Game.UI
{
    /// <summary>
    /// The 10 target languages from Docs/02-GDD-TicTacToe.md#8 (Localización) - all latin-alphabet,
    /// LTR (see Docs/03-Arquitectura-UGS-TicTacToe.md#Localización — implicancias técnicas) - in the
    /// fixed display order shared by <see cref="Screens.SettingsScreenController"/>'s language
    /// dropdown and the Editor asset generator (Assets/Scripts/Game/Editor/LocalizationSetup.cs), so
    /// both always agree on which locale codes exist and what their dropdown-facing name is.
    /// </summary>
    public static class SupportedLocales
    {
        /// <summary>One target locale: its Unity Localization <c>LocaleIdentifier</c> code and the name shown for it in its own language (never translated - a language picker always lists languages by their native name, per Docs/05-UI-Pantallas-TicTacToe.md screen 12).</summary>
        public readonly struct Entry
        {
            public readonly string Code;
            public readonly string NativeName;

            public Entry(string code, string nativeName)
            {
                Code = code;
                NativeName = nativeName;
            }
        }

        /// <summary>pt covers pt-BR/pt-PT with a single shared table for now (see Docs/02-GDD-TicTacToe.md#8, "Portugués (pt-BR / pt-PT)").</summary>
        public static readonly Entry[] All =
        {
            new("en", "English"),
            new("es", "Español"),
            new("fr", "Français"),
            new("de", "Deutsch"),
            new("pt", "Português"),
            new("it", "Italiano"),
            new("id", "Bahasa Indonesia"),
            new("vi", "Tiếng Việt"),
            new("tr", "Türkçe"),
            new("pl", "Polski"),
        };

        /// <summary>Final fallback locale code - used by both the startup locale selector chain and UiText's hard-coded English literals.</summary>
        public const string DefaultLocaleCode = "en";
    }
}
