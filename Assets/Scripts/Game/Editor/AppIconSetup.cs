using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace TTTXO.Game.Editor
{
    /// <summary>
    /// Applies the game logo (Assets/Sprites/TTTXO Logo.png) as the default application icon for
    /// all build targets. Idempotent - safe to re-run whenever the logo art changes.
    /// </summary>
    public static class AppIconSetup
    {
        private const string LogoPath = "Assets/Sprites/TTTXO Logo.png";

        [MenuItem("TTTXO/Setup/Apply App Icon")]
        public static void ApplyAppIcon()
        {
            var logo = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath);
            if (logo == null)
            {
                Debug.LogError($"TTTXO AppIconSetup: could not load '{LogoPath}'.");
                return;
            }

            // NamedBuildTarget.Unknown sets the "Default Icon" that every platform inherits unless
            // it gets a per-platform override later (store submissions will eventually need
            // platform-specific sizes - tracked for the release milestone).
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { logo }, IconKind.Application);
            AssetDatabase.SaveAssets();
            Debug.Log("TTTXO AppIconSetup: default application icon applied successfully.");
        }
    }
}
